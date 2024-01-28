using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.UpDock.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Avalonia.UpDock;

public class DockingManager
{
    private DockTabWindow? _draggedWindow;
    private readonly Window _hostWindow;
    private readonly SplitPanel _hostControl;

    public DockingManager(Window hostWindow, SplitPanel hostControl)
    {
        _hostWindow = hostWindow;
        _hostControl = hostControl;

        _hostControl.PointerMoved += HostControl_PointerMoved;
        _hostControl.PointerReleased += HostControl_PointerReleased;

        SetupSplitPanelForDocking(hostControl);
    }

    private void SetupSplitPanelForDocking(SplitPanel splitPanel, Orientation? parentOrientation = null)
    {
        Orientation orientation = splitPanel.Orientation;

        if (orientation == parentOrientation)
            throw new ArgumentException("Cannot have nested SplitPanels of same orientation");

        foreach (var child in splitPanel.Children)
        {
            if (child is DockingTabControl tabControl)
            {
                SetupTabControlForDocking(tabControl);
                continue;
            }

            if (child is not SplitPanel childSplitPanel)
                throw new ArgumentException("All children of a SplitPanel must be either a SplitPanel or TabControl" +
                    $" got {child.GetType().Name}");

            SetupSplitPanelForDocking(childSplitPanel, orientation);
        }
    }

    private void SetupTabControlForDocking(DockingTabControl tabControl)
    {
        tabControl.DraggedOutTab += TabControl_DraggedOutTab;
    }

    private void TabControl_DraggedOutTab(object? sender, PointerEventArgs e,
        DockingTabControl.TabItemRef itemRef, Point offset)
    {
        var tabControl = (DockingTabControl)sender!;
        var tabItem = tabControl.DetachTabItem(itemRef);

        var window = new DockTabWindow(tabItem)
        {
            Width = tabControl.Bounds.Width,
            Height = tabControl.Bounds.Height,
            SystemDecorations = SystemDecorations.None,
            Position = _hostWindow.PointToScreen(e.GetPosition(_hostWindow) + offset)
        };

        window.Show(_hostWindow);
        _draggedWindow = window;
        window.OnDragStart(e);
        window.Dragging += DockTabWindow_Dragging;
        window.DragEnd += DockTabWindow_DragEnd;
    }

    private void DockTabWindow_Dragging(object? sender, PointerEventArgs e)
    {
        var window = ((DockTabWindow)sender!);

        Point hitPoint = e.GetPosition(_hostWindow);
        VisitDockingTabControls(_hostControl, (tabControl) =>
        {
            tabControl.OnDragForeignTabOver(e, window.TabSize, window.TabHeader);
        });
    }

    private static void VisitDockingTabControls(SplitPanel splitPanel, Action<DockingTabControl> visitor)
    {
        foreach (var child in splitPanel.Children)
        {
            if (child is DockingTabControl dockingTabControl)
                visitor(dockingTabControl);
            else if (child is SplitPanel childSplitPanel)
                VisitDockingTabControls(childSplitPanel, visitor);
        }
    }

    private void DockTabWindow_DragEnd(object? sender, PointerEventArgs e)
    {
        Point hitPoint = e.GetPosition(_hostWindow);
        VisitDockingTabControls(_hostControl, (tabControl) =>
        {
            var dropTarget = tabControl.EvaluateDropTarget(e);

            tabControl.OnEndDragForeignTab();

            if (!HitTest(tabControl, hitPoint))
                return;

            if (dropTarget.IsNone())
                return;

            DockTabWindow window = (DockTabWindow)sender!;
            TabItem tabItem = window.DetachTabItem();
            window.Close();

            if (dropTarget.IsTabBar(out int index))
            {
                tabControl.Items.Insert(index, tabItem);
                return;
            }

            //TODO dynamic splitting

            tabControl.Items.Add(tabItem);
        });
    }

    protected void HostControl_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedWindow is null)
            return;

        _draggedWindow.OnDragEnd(e);

        _draggedWindow = null;
    }

    protected void HostControl_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedWindow is null)
            return;

        _draggedWindow.OnDragging(e);
    }

    private bool HitTest(Visual visual, Point hitPoint)
    {
        Point topLeft = visual.TranslatePoint(new Point(0, 0), _hostWindow)!.Value;
        Point bottomRight = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), _hostWindow)!.Value;
        return
            topLeft.X <= hitPoint.X && hitPoint.X <= bottomRight.X &&
            topLeft.Y <= hitPoint.Y && hitPoint.Y <= bottomRight.Y;
    }

    private class DockTabWindow : Window
    {
        public Size TabSize { get; private set; }
        public object? TabHeader => _tabItem.Header;

        private record DragInfo(Point Offset);

        private IBrush? _tabBackground = null;
        private IBrush? _tabItemBackground = null;
        private IPen _borderPen = new Pen(Brushes.Gray, 1);
        private TabItem _tabItem;
        private TabControl _tabControl;

        private DragInfo? _dragInfo = null;

        public event EventHandler<PointerEventArgs>? Dragging;
        public event EventHandler<PointerEventArgs>? DragEnd;

        public TabItem DetachTabItem()
        {
            _tabControl.Items.Clear();

            _tabItem.PointerPressed -= TabItem_PointerPressed;
            _tabItem.PointerMoved -= TabItem_PointerMoved;
            _tabItem.PointerReleased -= TabItem_PointerReleased;

            _tabItem.Background = _tabItemBackground;

            return _tabItem;
        }

        public DockTabWindow(TabItem tabItem)
        {
            _tabItem = tabItem;

            _tabItem.PointerPressed += TabItem_PointerPressed;
            _tabItem.PointerMoved += TabItem_PointerMoved;
            _tabItem.PointerReleased += TabItem_PointerReleased;

            _tabItemBackground = tabItem.Background;


            _tabControl = new TabControl()
            {
                Background = Brushes.Transparent, //just to be save
            };
            _tabControl.Items.Add(tabItem);

            Content = _tabControl;
        }

        private void TabItem_PointerPressed(object? sender, PointerEventArgs e) => OnDragStart(e);
        private void TabItem_PointerMoved(object? sender, PointerEventArgs e) => OnDragging(e);
        private void TabItem_PointerReleased(object? sender, PointerEventArgs e) => OnDragEnd(e);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != SystemDecorationsProperty || _tabBackground == null)
                return;

            if (SystemDecorations == SystemDecorations.None)
            {
                Background = null;
            }
            else
                Background = _tabBackground;
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            _tabBackground = Background;
            _tabItem.Background = Background;
            Background = null;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            Point topLeft = _tabItem.TranslatePoint(new Point(0, 0), this)!.Value;
            Point bottomRight = _tabItem.TranslatePoint(new Point(_tabItem.Bounds.Width, _tabItem.Bounds.Height), this)!.Value;

            var rect = Bounds.WithY(bottomRight.Y).WithHeight(Bounds.Height - bottomRight.Y);

            //probably fine
            TabSize = rect.Size;

            context.FillRectangle(_tabBackground!, rect);
            context.DrawRectangle(_borderPen, rect);
            base.Render(context);
        }

        public void OnDragStart(PointerEventArgs e)
        {
            SystemDecorations = SystemDecorations.None;
            _dragInfo = new(e.GetPosition(this));
        }

        public void OnDragEnd(PointerEventArgs e)
        {
            _dragInfo = null;
            DragEnd?.Invoke(this, e);
            SystemDecorations = SystemDecorations.Full;
        }

        public void OnDragging(PointerEventArgs e)
        {
            if (_dragInfo == null)
                return;

            Point offset = _dragInfo.Offset;

            Position = this.PointToScreen(e.GetPosition(this) - offset);
            Dragging?.Invoke(this, e);
        }
    }
}
