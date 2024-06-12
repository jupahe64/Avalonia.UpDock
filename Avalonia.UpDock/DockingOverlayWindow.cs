using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.UpDock.Controls;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Avalonia.UpDock;

public class DockingOverlayWindow : Window
{
    public record AreaEnteredEventArgs(Control Control);
    public record AreaExitedEventArgs(Control Control);

    public event EventHandler<AreaEnteredEventArgs>? AreaEntered;
    public event EventHandler<AreaExitedEventArgs>? AreaExited;

    public Control? HoveredControl => _hoveredControl;
    public DropTarget HoveredDropTarget => _hoveredDropTarget;

    public DockingOverlayWindow(DockingHost dockingHost)
    {
        _dockingHost = dockingHost;
        Background = null;
    }

    private readonly DockingHost _dockingHost;
    private readonly Dictionary<Control, Rect> _areas = [];
    private Control? _hoveredControl = null;
    private DropTarget _hoveredDropTarget = DropTarget.None;

    public override void Show()
    {
        SystemDecorations = SystemDecorations.None;
        UpdateAreas();
        _hoveredControl = null;
        _hoveredDropTarget = DropTarget.None;
        base.Show();
    }

    public void UpdateAreas()
    {
        _areas.Clear();
        _dockingHost.VisitDockingTreeNodes<Control>(control =>
        {
            _areas[control] = _dockingHost.GetBoundsOf(control);
        });
    }

    public override void Hide()
    {
        if (_hoveredControl != null)
            AreaExited?.Invoke(this, new AreaExitedEventArgs(_hoveredControl));

        base.Hide();
    }

    public override void Render(DrawingContext ctx)
    {
        var draggedTabInfoNullable = _dockingHost.DraggedTabInfo;
        if (draggedTabInfoNullable == null)
            return;

        var draggedTabInfo = draggedTabInfoNullable.Value;

        Span<Rect> rects = stackalloc Rect[9];

        
        if (_hoveredControl != null && _areas.TryGetValue(_hoveredControl, out Rect bounds))
        {
            ctx.DrawRectangle(_dockingHost.DockIndicatorStrokePen, bounds);

            Calculate3x3DockControlsRects(bounds, rects, out float cornerRadius);

            void DrawDockControl(ReadOnlySpan<Rect> rects, 
                int x, int y, (double l, double r) lrPercent, (double t, double b) tbPercent, bool isHovered)
            {
                int idx = (x + 1) + (y + 1) * 3;
                static double Lerp(double a, double b, double t) => (1 - t) * a + t * b;
                var l = Lerp(rects[idx].Left, rects[idx].Right, lrPercent.l);
                var r = Lerp(rects[idx].Left, rects[idx].Right, lrPercent.r);
                var t = Lerp(rects[idx].Top, rects[idx].Bottom, tbPercent.t);
                var b = Lerp(rects[idx].Top, rects[idx].Bottom, tbPercent.b);
                var fillRect = new Rect(new Point(l, t), new Point(r, b));

                if (isHovered)
                    ctx.FillRectangle(_dockingHost.DockIndicatorFieldHoveredFill, fillRect, cornerRadius);
                else
                    ctx.FillRectangle(_dockingHost.DockIndicatorFieldFill, fillRect, cornerRadius);
                ctx.DrawRectangle(_dockingHost.DockIndicatorStrokePen, rects[idx], cornerRadius);
            }

            //left
            DrawDockControl(rects, -1, 0, (0, .5), (0, 1), _hoveredDropTarget.IsDock(Dock.Left));
            //right
            DrawDockControl(rects, 1, 0, (.5, 1), (0, 1), _hoveredDropTarget.IsDock(Dock.Right));
            //top
            DrawDockControl(rects, 0, -1, (0, 1), (0, .5), _hoveredDropTarget.IsDock(Dock.Top));
            //bottom
            DrawDockControl(rects, 0, 1, (0, 1), (.5, 1), _hoveredDropTarget.IsDock(Dock.Bottom));

            if (_hoveredControl is TabControl)
                DrawDockControl(rects, 0, 0, (0, 1), (0, 1), _hoveredDropTarget.IsFill());

            if (_hoveredDropTarget.IsDock(out Dock dock))
            {
                var rect = DockingHost.CalculateDockRect(draggedTabInfo, bounds, dock);
                ctx.FillRectangle(_dockingHost.DockIndicatorFieldFill, rect);
            }
            else if (_hoveredDropTarget.IsFill())
            {
                ctx.FillRectangle(_dockingHost.DockIndicatorFieldFill, bounds);
            }
        }

        if (_hoveredDropTarget.IsTabBar(out int tabIndex))
        {
            if (_hoveredControl is not TabControl tabControl)
            {
                Debug.Fail("Invalid dropTarget for control");
                return;
            }
            var hoveredTabItem = (TabItem)tabControl.Items[tabIndex]!;
            var rect = new Rect(_dockingHost.GetBoundsOf(hoveredTabItem).TopLeft,
                draggedTabInfo.TabItemSize);
            ctx.FillRectangle(_dockingHost.DockIndicatorFieldHoveredFill, rect);
        }
    }

    public new void OnPointerMoved(PointerEventArgs e)
    {
        var pos = e.GetPosition(_dockingHost);

        var (newHoveredControl, hoveredBounds) = _areas.FirstOrDefault(x=>x.Value.Contains(pos));

        if (newHoveredControl != null)
            _hoveredDropTarget = EvaluateDropTarget(pos, newHoveredControl, hoveredBounds);

        InvalidateVisual();

        if (newHoveredControl == _hoveredControl)
            return;

        if (_hoveredControl != null)
            AreaExited?.Invoke(this, new AreaExitedEventArgs(_hoveredControl));

        if (newHoveredControl != null)
            AreaEntered?.Invoke(this, new AreaEnteredEventArgs(newHoveredControl));

        _hoveredControl = newHoveredControl;
    }

    private DropTarget EvaluateDropTarget(Point pos, Control targetControl, Rect targetBounds)
    {
        Span<Rect> rects = stackalloc Rect[9];
        Calculate3x3DockControlsRects(targetBounds, rects, out float cornerRadius);

        bool HitTest(ReadOnlySpan<Rect> rects, int x, int y) 
            => rects[(x + 1) + (y + 1) * 3].Contains(pos);

        if (_hoveredControl is TabControl && HitTest(rects, 0, 0))
            return DropTarget.Fill;

        if (HitTest(rects, -1, 0))
            return DropTarget.Dock(Dock.Left);
        if (HitTest(rects, 1, 0))
            return DropTarget.Dock(Dock.Right);
        if (HitTest(rects, 0, -1))
            return DropTarget.Dock(Dock.Top);
        if (HitTest(rects, 0, 1))
            return DropTarget.Dock(Dock.Bottom);

        if (targetControl is TabControl tabControl)
        {
            var hoveredTabItem = tabControl.Items
                .OfType<TabItem>()
                .LastOrDefault(x => _dockingHost.GetBoundsOf(x).Contains(pos));

            if (hoveredTabItem != null)
                return DropTarget.TabBar(tabControl.Items.IndexOf(hoveredTabItem));
        }

        return DropTarget.None;
    }

    private void Calculate3x3DockControlsRects(Rect bounds, Span<Rect> rects, out float cornerRadius)
    {
        double fieldSize = _dockingHost.DockIndicatorFieldSize;
        double fieldSpacing = _dockingHost.DockIndicatorFieldSpacing;
        double totalSize = 
            _dockingHost.DockIndicatorFieldSize * 3 + 
            _dockingHost.DockIndicatorFieldSpacing * 2;

        double scaling = Math.Min(bounds.Width, bounds.Height) / totalSize;
        scaling = Math.Min(scaling, 1);

        Size indicatorSizeScaled = new Size(fieldSize, fieldSize) * scaling;
        double spacingScaled = fieldSpacing * scaling;

        var distance = indicatorSizeScaled.Width + spacingScaled;

        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                rects[x + y * 3] = bounds.CenterRect(new Rect(indicatorSizeScaled))
                    .Translate(new Vector(distance * (x - 1), distance * (y - 1)));
            }
        }

        cornerRadius = (float)(_dockingHost.DockIndicatorFieldCornerRadius * scaling);
    }
}
