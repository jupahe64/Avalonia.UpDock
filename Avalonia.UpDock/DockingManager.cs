using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.UpDock.Controls;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using static Avalonia.UpDock.Controls.DockingTabControl;

namespace Avalonia.UpDock;

public class DockingManager
{
    private DockTabWindow? _draggedWindow;
    private readonly SplitPanel _hostControl;
    private HashSet<SplitPanel> _ignoreModified = [];

    public DockingManager(SplitPanel hostControl)
    {
        _hostControl = hostControl;

        _hostControl.PointerMoved += HostControl_PointerMoved;
        _hostControl.PointerReleased += HostControl_PointerReleased;

        SetupSplitPanelForDocking(hostControl);
    }

    private Window GetHostWindow()
    {
        if (_hostControl.GetVisualRoot() is Window window)
            return window;

        throw new InvalidOperationException(
            $"The hostControl of this {nameof(DockingManager)} is not part of a Window");
    }

    #region Setup
    private void SetupSplitPanelForDocking(SplitPanel splitPanel, Orientation? parentOrientation = null)
    {
        bool hasParent = parentOrientation.HasValue;

        if(hasParent && splitPanel.Fractions.Count == 1)
            throw new ArgumentException("Only the docking host can have 1 defined fraction");

        splitPanel.Children.CollectionChanged += (_,_) => SplitPanel_ChildrenModified(splitPanel);

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
                throw new ArgumentException("All children of a SplitPanel must be either a SplitPanel or DockingTabControl" +
                    $" got {child.GetType().Name}");

            SetupSplitPanelForDocking(childSplitPanel, orientation);
        }
    }

    private void SetupTabControlForDocking(DockingTabControl tabControl)
    {
        tabControl.RegisterDraggedOutTabHanlder(TabControl_DraggedOutTab);
        tabControl.Items.CollectionChanged += (_, _) => TabControl_ItemsModified(tabControl);
    }
    #endregion

    #region Handle Tree Modifications
    /// <summary>
    /// Ensures that after a modification there are still no SplitPanels with less than 2 Children
    /// unless it's the root
    /// </summary>
    private void SplitPanel_ChildrenModified(SplitPanel panel)
    {
        if (_ignoreModified.Contains(panel))
            return;

        if (panel.Parent is not SplitPanel parent)
            return;

        int slotInParent = parent.Children.IndexOf(panel);

        if (panel.Children.Count == 1)
        {
            var child = panel.Children[0];

            //panel is still part of the UI Tree and as such can trigger a cascade of unwanted changes
            //and we can't remove it without triggering ChildrenModified
            //or replacing it with a Dummy Control so we have to:
            _ignoreModified.Add(panel);
            panel.Children.Clear();
            _ignoreModified.Remove(panel);

            parent.Children[slotInParent] = child;
        }
        else if (panel.Children.Count == 0)
            parent.Children.RemoveAt(slotInParent);
    }

    /// <summary>
    /// Ensures that after a modification there are still no TabControls with no Items
    /// unless it's the only child in the root
    /// </summary>
    private static void TabControl_ItemsModified(DockingTabControl tabControl)
    {
        if (tabControl.Items.Count > 0)
            return;

        if (tabControl.Parent is not SplitPanel parent)
            return;

        if (parent.Children.Count == 1 && parent.Parent is not SplitPanel)
            return;

        int indexInParent = parent.Children.IndexOf(tabControl);

        parent.RemoveSlot(indexInParent);
    }
    #endregion

    #region Modify Tree Savely
    /// <summary>
    /// Creates a <see cref="DockingTabControl"/> that has been setup for Docking
    /// </summary>
    private DockingTabControl CreateTabControl(TabItem initialTabItem)
    {
        var tabControl = new DockingTabControl();
        tabControl.Items.Add(initialTabItem);
        SetupTabControlForDocking(tabControl);
        return tabControl;
    }

    /// <summary>
    /// Creates and inserts a <see cref="SplitPanel"/> that has been setup for Docking
    /// <para>It does so in a way that no unwanted side effects are triggered</para>
    /// </summary>
    private void InsertSplitPanel(Orientation orientation,
        (int fraction, Control child) slot1, (int fraction, Control child) slot2,
        Action<SplitPanel> insertAction)
    {
        var panel = new SplitPanel
        {
            Orientation = orientation,
            Fractions = new SplitFractions(slot1.fraction, slot2.fraction)
        };
        SetupSplitPanelForDocking(panel);

        insertAction(panel);
        panel.Children.AddRange([slot1.child, slot2.child]);
    }
    #endregion

    private void TabControl_DraggedOutTab(object? sender, PointerEventArgs e,
        TabItem tabItem, Point offset)
    {
        var tabControl = (DockingTabControl)sender!;
        var hostWindow = GetHostWindow();

        var window = new DockTabWindow(tabItem)
        {
            Width = tabControl.Bounds.Width,
            Height = tabControl.Bounds.Height,
            SystemDecorations = SystemDecorations.None
        };


        window.Position = hostWindow.PointToScreen(e.GetPosition(hostWindow) + offset);

        window.Show(hostWindow);

        ChildWindowMoveHandler.Hookup(hostWindow, window);

        _draggedWindow = window;
        window.OnDragStart(e);
        window.Dragging += DockTabWindow_Dragging;
        window.DragEnd += DockTabWindow_DragEnd;
    }

    private void DockTabWindow_Dragging(object? sender, PointerEventArgs e)
    {
        var window = ((DockTabWindow)sender!);

        Point hitPoint = e.GetPosition(GetHostWindow());
        VisitDockingTabControls(_hostControl, (tabControl) =>
        {
            tabControl.OnDragForeignTabOver(e, window.TabSize, window.TabHeader);
        });
    }

    public static void VisitDockingTabControls(SplitPanel splitPanel, Action<DockingTabControl> visitor)
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
        Point hitPoint = e.GetPosition(_hostControl);

        DockingTabControl? dropTabControl = null;
        DropTarget dropTarget = DropTarget.None;

        VisitDockingTabControls(_hostControl, (tabControl) =>
        {
            var _dropTarget = tabControl.EvaluateDropTarget(e);

            tabControl.OnEndDragForeignTab();

            if (!HitTest(tabControl, hitPoint))
                return;

            if (_dropTarget.IsNone())
                return;

            dropTabControl = tabControl;
            dropTarget = _dropTarget;
        });

        if (dropTabControl == null)
            return;

        DockTabWindow window = (DockTabWindow)sender!;
        TabItem tabItem = window.DetachTabItem();
        window.Close();

        if (dropTarget.IsTabBar(out int index))
        {
            dropTabControl.Items.Insert(index, tabItem);
            return;
        }

        if (dropTarget.IsFill())
        {
            dropTabControl.Items.Add(tabItem);
            return;
        }

        if (dropTarget.IsDock(out Dock dock))
        {
            SplitPanel parent = (SplitPanel)dropTabControl.Parent!;

            Orientation splitOrientation = dock switch
            {
                Dock.Left or Dock.Right => Orientation.Horizontal,
                Dock.Top or Dock.Bottom => Orientation.Vertical,
                _ => throw new NotImplementedException()
            };

            TabControl newTabControl = CreateTabControl(tabItem);
            var dropSlot = parent.Children.IndexOf(dropTabControl);

            parent.GetSlotSize(dropSlot, out int size, out Size size2D);

            int insertSlotSize = size / 2;
            int otherSlotSize = size - insertSlotSize;

            if (parent.TrySplitSlot(dropSlot, (dock, insertSlotSize, newTabControl), otherSlotSize))
                return;

            if (dock is Dock.Left or Dock.Top)
            {
                InsertSplitPanel(splitOrientation,
                    (insertSlotSize, newTabControl), (otherSlotSize, dropTabControl),
                    panel => parent.Children[dropSlot] = panel);
            }
            else
            {
                InsertSplitPanel(splitOrientation,
                    (otherSlotSize, dropTabControl), (insertSlotSize, newTabControl),
                    panel => parent.Children[dropSlot] = panel);
            }
        }
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
        Point topLeft = visual.TranslatePoint(new Point(0, 0), _hostControl)!.Value;
        Point bottomRight = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), _hostControl)!.Value;
        return
            topLeft.X <= hitPoint.X && hitPoint.X <= bottomRight.X &&
            topLeft.Y <= hitPoint.Y && hitPoint.Y <= bottomRight.Y;
    }

    private class ChildWindowMoveHandler
    {
        private (PixelPoint topLeft, Size size) _lastParentWindowBounds;
        private readonly Window _child;
        private readonly Window _parent;
        public static void Hookup(Window parent, Window child)
        {
            var handler = new ChildWindowMoveHandler(parent, child);
            parent.PositionChanged += handler.Parent_PositionChanged;
            child.Closed += handler.Child_Closed;
        }

        private void Child_Closed(object? sender, EventArgs e)
        {
            _parent.PositionChanged -= Parent_PositionChanged;
            _child.Closed -= Child_Closed;
        }

        private void Parent_PositionChanged(object? sender, PixelPointEventArgs e)
        {
            var position = _parent.Position;
            var size = _parent.FrameSize.GetValueOrDefault();
            if (_lastParentWindowBounds.size != size)
            {
                _lastParentWindowBounds = (position, size);
                return;
            }

            var delta = position - _lastParentWindowBounds.topLeft;

            _child.Position += delta;
            _lastParentWindowBounds = (position, size);
        }

        private ChildWindowMoveHandler(Window parent, Window child)
        {
            _child = child;
            _parent = parent;
            _lastParentWindowBounds = (parent.Position, parent.FrameSize.GetValueOrDefault());
        }
    }
}

internal class DockTabWindow : Window
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

        if (_tabItem is ClosableTabItem closable)
            closable.Closed -= TabItem_Closed;

        _tabItem.Background = _tabItemBackground;

        return _tabItem;
    }

    public DockTabWindow(TabItem tabItem)
    {
        _tabItem = tabItem;

        _tabItem.PointerPressed += TabItem_PointerPressed;
        _tabItem.PointerMoved += TabItem_PointerMoved;
        _tabItem.PointerReleased += TabItem_PointerReleased;

        if (_tabItem is ClosableTabItem closable)
            closable.Closed += TabItem_Closed;

        _tabItemBackground = tabItem.Background;


        _tabControl = new TabControl()
        {
            Background = Brushes.Transparent, //just to be save
        };
        _tabControl.Items.Add(tabItem);

        Content = _tabControl;
    }

    private bool _isTabItemClosed = false;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (!_tabControl.Items.Contains(_tabItem))
            return; //the tabItem is not part of the window anymore

        if (_tabItem is not ClosableTabItem closable)
        {
            e.Cancel = true;
            return;
        }

        if (!_isTabItemClosed)
        {
            e.Cancel = true;
            closable.Close();
        }
    }

    private void TabItem_Closed(object? sender, RoutedEventArgs e)
    {
        _isTabItemClosed = true;
        Close();
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
