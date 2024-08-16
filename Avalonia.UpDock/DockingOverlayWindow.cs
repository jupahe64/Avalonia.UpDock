using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.UpDock.Controls;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Avalonia.UpDock;

internal class DockingOverlayWindow : Window
{
    private struct DockAreaInfo
    {
        public Rect Bounds;
        public DockFlags InsertNeighborFlags;
        public int InsertNeighborIndex;
    }

    private struct DockUILayoutInfo
    {
        public float CornerRadius;
        public Rect LeftSplitRect;
        public Rect RightSplitRect;
        public Rect TopSplitRect;
        public Rect BottomSplitRect;
        public Rect CenterRect;

        public Rect LeftNeighborRect;
        public Rect RightNeighborRect;
        public Rect TopNeighborRect;
        public Rect BottomNeighborRect;
    }

    public struct Result(Control? control, DropTarget dropTarget)
    {
        public readonly bool IsSplitControl([NotNullWhen(true)] out Control? target, out Dock dock)
        {
            target = control;
            bool isCorrectTarget = dropTarget.IsSplitDock(out dock);
            return target != null && isCorrectTarget;
        }
        public readonly bool IsFillControl([NotNullWhen(true)] out Control? target)
        {
            target = control;
            return target != null && dropTarget.IsFill();
        }
        public readonly bool IsInsertNextTo([NotNullWhen(true)] out Control? target, out Dock dock)
        {
            target = control;
            bool isCorrectTarget = dropTarget.IsNeighborDock(out dock);
            return target != null && isCorrectTarget;
        }

        public readonly bool IsInsertOuter(out Dock dock)
        {
            bool isCorrectTarget = dropTarget.IsNeighborDock(out dock);
            return control == null && isCorrectTarget;
        }

        public readonly bool IsInsertTab([NotNullWhen(true)] out TabControl? target, out int index)
        {
            target = control as TabControl;
            bool isCorrectTarget = dropTarget.IsTabBar(out index);
            return target != null && isCorrectTarget;
        }
    }

    public Result GetResult() => new(_hoveredControl, _hoveredDropTarget);

    public record AreaEnteredEventArgs(Control Control);
    public record AreaExitedEventArgs(Control Control);

    public event EventHandler<AreaEnteredEventArgs>? AreaEntered;
    public event EventHandler<AreaExitedEventArgs>? AreaExited;

    public DockingOverlayWindow(DockingHost dockingHost)
    {
        _dockingHost = dockingHost;
        Background = null;
    }

    private readonly DockingHost _dockingHost;
    private readonly Dictionary<Control, DockAreaInfo> _areas = [];
    private Control? _hoveredControl = null;
    private DropTarget _hoveredDropTarget = DropTarget.None;
    private DockUILayoutInfo _hoveredControlDockUI;
    private DockUILayoutInfo _outerEdgesDockUI;

    public void UpdateAreas()
    {
        _areas.Clear();
        _dockingHost.VisitDockingTreeNodes<Control>(control =>
        {
            var dockAreaInfo = new DockAreaInfo
            {
                Bounds = _dockingHost.GetBoundsOf(control)
            };

            if (_dockingHost.CanDockNextTo(control, out DockFlags dockFlags, out int insertIndex))
            {
                dockAreaInfo.InsertNeighborFlags = dockFlags;
                dockAreaInfo.InsertNeighborIndex = insertIndex;
            }

            _areas[control] = dockAreaInfo;
        });

        _outerEdgesDockUI = CalcDockUILayout(_dockingHost, isForOuterEdges: true);
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

        void DrawDockControl(Rect rect, (double l, double r) lrPercent, (double t, double b) tbPercent,
                bool isHovered, float cornerRadius, bool isNeighborDock = false)
        {
            static double Lerp(double a, double b, double t) => (1 - t) * a + t * b;
            var l = Lerp(rect.Left, rect.Right, lrPercent.l);
            var r = Lerp(rect.Left, rect.Right, lrPercent.r);
            var t = Lerp(rect.Top, rect.Bottom, tbPercent.t);
            var b = Lerp(rect.Top, rect.Bottom, tbPercent.b);
            var fillRect = new Rect(new Point(l, t), new Point(r, b));

            if (isHovered)
                ctx.FillRectangle(_dockingHost.DockIndicatorFieldHoveredFill, fillRect, cornerRadius);
            else
                ctx.FillRectangle(_dockingHost.DockIndicatorFieldFill, fillRect, cornerRadius);

            if (isNeighborDock)
                ctx.DrawRectangle(_dockingHost.DockIndicatorStrokePen, fillRect, cornerRadius);
            else
                ctx.DrawRectangle(_dockingHost.DockIndicatorStrokePen, rect, cornerRadius);
        }

        var draggedTabInfo = draggedTabInfoNullable.Value;

        Span<Rect> rects = stackalloc Rect[9];

        Dock dock;
        if (_hoveredControl != null && _areas.TryGetValue(_hoveredControl, out DockAreaInfo areaInfo))
        {
            ctx.DrawRectangle(_dockingHost.DockIndicatorStrokePen, areaInfo.Bounds);

            var dockUI = _hoveredControlDockUI;
            var hoveredTarget = _hoveredDropTarget;
            var cornerRadius = dockUI.CornerRadius;

            //left
            DrawDockControl(dockUI.LeftSplitRect, (0, .5), (0, 1), hoveredTarget.IsSplitDock(Dock.Left), cornerRadius);
            //right
            DrawDockControl(dockUI.RightSplitRect, (.5, 1), (0, 1), hoveredTarget.IsSplitDock(Dock.Right), cornerRadius);
            //top
            DrawDockControl(dockUI.TopSplitRect, (0, 1), (0, .5), hoveredTarget.IsSplitDock(Dock.Top), cornerRadius);
            //bottom
            DrawDockControl(dockUI.BottomSplitRect, (0, 1), (.5, 1), hoveredTarget.IsSplitDock(Dock.Bottom), cornerRadius);

            DrawDockControl(dockUI.CenterRect, (0, 1), (0, 1), hoveredTarget.IsFill(), cornerRadius);

            DrawDockControl(dockUI.LeftNeighborRect, (0, .5), (0, 1), hoveredTarget.IsNeighborDock(Dock.Left), cornerRadius, true);
            DrawDockControl(dockUI.RightNeighborRect, (.5, 1), (0, 1), hoveredTarget.IsNeighborDock(Dock.Right), cornerRadius, true);
            DrawDockControl(dockUI.TopNeighborRect, (0, 1), (0, .5), hoveredTarget.IsNeighborDock(Dock.Top), cornerRadius, true);
            DrawDockControl(dockUI.BottomNeighborRect, (0, 1), (.5, 1), hoveredTarget.IsNeighborDock(Dock.Bottom), cornerRadius, true);

            if (_hoveredDropTarget.IsSplitDock(out dock))
            {
                var rect = DockingHost.CalculateDockRect(draggedTabInfo, areaInfo.Bounds, dock);
                ctx.FillRectangle(_dockingHost.DockIndicatorFieldFill, rect);
            }
            else if (_hoveredDropTarget.IsNeighborDock(out dock))
            {
                if (areaInfo.InsertNeighborIndex < _dockingHost.Children.Count)
                {
                    var control = _dockingHost.Children[areaInfo.InsertNeighborIndex];
                    var bounds = _dockingHost.GetBoundsOf(control);
                    var rect = CalcNeighborDockAreaRect(bounds, draggedTabInfo.ContentSize, dock);
                    ctx.FillRectangle(_dockingHost.DockIndicatorFieldFill, rect);
                }
                else
                    Debug.Fail("Something went wrong");
            }
            else if (_hoveredDropTarget.IsFill())
            {
                ctx.FillRectangle(_dockingHost.DockIndicatorFieldFill, areaInfo.Bounds);
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

        {
            var dockUI = _outerEdgesDockUI;
            var hoveredTarget = _hoveredControl == null ? _hoveredDropTarget : DropTarget.None;
            var cornerRadius = dockUI.CornerRadius;

            DrawDockControl(dockUI.LeftNeighborRect, (0, .5), (0, 1), hoveredTarget.IsNeighborDock(Dock.Left), cornerRadius, true);
            DrawDockControl(dockUI.RightNeighborRect, (.5, 1), (0, 1), hoveredTarget.IsNeighborDock(Dock.Right), cornerRadius, true);
            DrawDockControl(dockUI.TopNeighborRect, (0, 1), (0, .5), hoveredTarget.IsNeighborDock(Dock.Top), cornerRadius, true);
            DrawDockControl(dockUI.BottomNeighborRect, (0, 1), (.5, 1), hoveredTarget.IsNeighborDock(Dock.Bottom), cornerRadius, true);
        }

        if (_hoveredControl == null && _hoveredDropTarget.IsNeighborDock(out dock))
        {
            var bounds = _dockingHost.Bounds.WithX(0).WithY(0);
            var rect = CalcNeighborDockAreaRect(bounds, draggedTabInfo.ContentSize, dock);
            ctx.FillRectangle(_dockingHost.DockIndicatorFieldFill, rect);
        }
    }

    private Rect CalcNeighborDockAreaRect(Rect bounds, Size contentSize, Dock dock)
    {
        if (dock is Dock.Left or Dock.Right)
            contentSize = contentSize.WithHeight(bounds.Height);
        else
            contentSize = contentSize.WithWidth(bounds.Width);

        var rect = new Rect(bounds.TopLeft, contentSize);

        return dock switch
        {
            Dock.Top or Dock.Left => rect.Translate(bounds.TopLeft - rect.TopLeft),
            Dock.Bottom or Dock.Right => rect.Translate(bounds.BottomRight - rect.BottomRight),
            _ => throw null!,
        };
    }

    public new void OnPointerMoved(PointerEventArgs e)
    {
        var pos = e.GetPosition(_dockingHost);

        var (newHoveredControl, areaInfo) = _areas.FirstOrDefault(x=>x.Value.Bounds.Contains(pos));

        if (newHoveredControl != null)
        {
            if (newHoveredControl != _hoveredControl)
                _hoveredControlDockUI = CalcDockUILayout(newHoveredControl);

            _hoveredDropTarget = EvaluateDropTarget(pos, newHoveredControl, _hoveredControlDockUI);
        }
        else
            _hoveredDropTarget = DropTarget.None;

        var outerEdgesDropTarget = EvaluateDropTarget(pos, null, _outerEdgesDockUI);
        if (!outerEdgesDropTarget.IsNone())
        {
            _hoveredDropTarget = outerEdgesDropTarget;
            newHoveredControl = null;
        }

        InvalidateVisual();

        if (newHoveredControl == _hoveredControl)
            return;

        if (_hoveredControl != null)
            AreaExited?.Invoke(this, new AreaExitedEventArgs(_hoveredControl));

        if (newHoveredControl != null)
            AreaEntered?.Invoke(this, new AreaEnteredEventArgs(newHoveredControl));

        _hoveredControl = newHoveredControl;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        
        if (_hoveredControl != null)
            AreaExited?.Invoke(this, new AreaExitedEventArgs(_hoveredControl));

        _hoveredControl = null;
        _hoveredDropTarget = DropTarget.None;
    }

    private DockUILayoutInfo CalcDockUILayout(Control control, bool isForOuterEdges = false)
    {
        static Rect DockUIRect(ReadOnlySpan<Rect> rects, int x, int y)
        {
            int idx = (Math.Clamp(x, -1, 1) + 1) + (Math.Clamp(y, -1, 1) + 1) * 3;

            Rect rect = rects[idx];

            var offset = rect.Center - rects[4].Center;
            //handle x and y values outside the rects
            rect = rect.Translate(-offset);
            return rect.Translate(new Vector(offset.X * Math.Abs(x), offset.Y * Math.Abs(y)));
        }

        Span<Rect> rects = stackalloc Rect[9];
        DockFlags insertNeighborFlags;
        DockUILayoutInfo info;
        Rect bounds;
        if (isForOuterEdges)
        {
            Debug.Assert(control == _dockingHost);
            bounds = control.Bounds.WithX(0).WithY(0);
            Calculate3x3DockUIRectsWithMargin(bounds, rects, out float cornerRadius);

            insertNeighborFlags = DockFlags.All;
            info = new DockUILayoutInfo
            {
                CornerRadius = cornerRadius
            };
        }
        else
        {
            var areaInfo = _areas[control];
            insertNeighborFlags = areaInfo.InsertNeighborFlags;
            bounds = areaInfo.Bounds;
            Calculate3x3DockUIRectsWithMargin(bounds, rects, out float cornerRadius);

            info = new DockUILayoutInfo
            {
                CornerRadius = cornerRadius
            };

            if (_dockingHost.CanSplit(control))
            {
                info.LeftSplitRect = DockUIRect(rects, -1, 0);
                info.RightSplitRect = DockUIRect(rects, 1, 0);
                info.TopSplitRect = DockUIRect(rects, 0, -1);
                info.BottomSplitRect = DockUIRect(rects, 0, 1);
            }

            if (_dockingHost.CanFill(control))
                info.CenterRect = DockUIRect(rects, 0, 0);
        }

        bool isClamp = !isForOuterEdges;

        if (insertNeighborFlags.HasFlag(DockFlags.Left))
            info.LeftNeighborRect = AlignLeft(DockUIRect(rects, -2, 0), bounds, isClamp);

        if (insertNeighborFlags.HasFlag(DockFlags.Right))
            info.RightNeighborRect = AlignRight(DockUIRect(rects, 2, 0), bounds, isClamp);

        if (insertNeighborFlags.HasFlag(DockFlags.Top))
            info.TopNeighborRect = AlignTop(DockUIRect(rects, 0, -2), bounds, isClamp);

        if (insertNeighborFlags.HasFlag(DockFlags.Bottom))
            info.BottomNeighborRect = AlignBottom(DockUIRect(rects, 0, 2), bounds, isClamp);

        return info;
    }

    private static Rect AlignLeft(Rect rect, Rect bounds, bool isClamp) =>
            !isClamp || rect.Left < bounds.Left ?
            rect.Translate(Vector.UnitX * (bounds.Left - rect.Left)) : rect;

    private static Rect AlignRight(Rect rect, Rect bounds, bool isClamp) =>
        !isClamp || rect.Right > bounds.Right ?
        rect.Translate(Vector.UnitX * (bounds.Right - rect.Right)) : rect;

    private static Rect AlignTop(Rect rect, Rect bounds, bool isClamp) =>
        !isClamp || rect.Top < bounds.Top ?
        rect.Translate(Vector.UnitY * (bounds.Top - rect.Top)) : rect;

    private static Rect AlignBottom(Rect rect, Rect bounds, bool isClamp) =>
        !isClamp || rect.Bottom > bounds.Bottom ?
        rect.Translate(Vector.UnitY * (bounds.Bottom - rect.Bottom)) : rect;

    private DropTarget EvaluateDropTarget(Point pos, Control? targetControl, in DockUILayoutInfo layoutInfo)
    {
        if (layoutInfo.CenterRect.Contains(pos))
            return DropTarget.Fill;

        if (layoutInfo.LeftSplitRect.Contains(pos))
            return DropTarget.SplitDock(Dock.Left);
        if (layoutInfo.RightSplitRect.Contains(pos))
            return DropTarget.SplitDock(Dock.Right);
        if (layoutInfo.TopSplitRect.Contains(pos))
            return DropTarget.SplitDock(Dock.Top);
        if (layoutInfo.BottomSplitRect.Contains(pos))
            return DropTarget.SplitDock(Dock.Bottom);

        if (layoutInfo.LeftNeighborRect.Contains(pos))
            return DropTarget.NeighborDock(Dock.Left);
        if (layoutInfo.RightNeighborRect.Contains(pos))
            return DropTarget.NeighborDock(Dock.Right);
        if (layoutInfo.TopNeighborRect.Contains(pos))
            return DropTarget.NeighborDock(Dock.Top);
        if (layoutInfo.BottomNeighborRect.Contains(pos))
            return DropTarget.NeighborDock(Dock.Bottom);

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

    private void Calculate3x3DockUIRectsWithMargin(Rect bounds, Span<Rect> rects, out float cornerRadius)
    {
        double fieldSize = _dockingHost.DockIndicatorFieldSize;
        double fieldSpacing = _dockingHost.DockIndicatorFieldSpacing;
        double totalSize = 
            _dockingHost.DockIndicatorFieldSize * 5 + 
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
