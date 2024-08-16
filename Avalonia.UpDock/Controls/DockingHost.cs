using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using LIST_MODIFY_HANDLER = System.Collections.Specialized.NotifyCollectionChangedEventHandler;

namespace Avalonia.UpDock.Controls;

public partial class DockingHost : DockSplitPanel
{
    public record struct TabInfo(object? Header, Size TabItemSize, Size ContentSize, Size TabControlSize);

    public DockingHost()
    {
        
    }

    private DockingOverlayWindow? _overlayWindow;

    private DockTabWindow? _draggedWindow;
    private readonly HashSet<SplitPanel> _ignoreModified = [];

    private IPen _dockIndicatorStrokePen = new Pen(
        DockIndicatorFieldStrokeProperty.GetDefaultValue(typeof(DockingTabControl)),
        DockIndicatorFieldStrokeThicknessProperty.GetDefaultValue(typeof(DockingTabControl)));

    #region Properties
    public static StyledProperty<double> DockIndicatorFieldSizeProperty { get; private set; } =
        AvaloniaProperty.Register<DockingTabControl, double>(nameof(DockIndicatorFieldSize), 40);

    public static StyledProperty<double> DockIndicatorFieldSpacingProperty { get; private set; } =
        AvaloniaProperty.Register<DockingTabControl, double>(nameof(DockIndicatorFieldSpacing), 10);

    public static StyledProperty<float> DockIndicatorFieldCornerRadiusProperty { get; private set; } =
        AvaloniaProperty.Register<DockingTabControl, float>(nameof(DockIndicatorFieldCornerRadius), 5);

    public static StyledProperty<IBrush> DockIndicatorFieldFillProperty { get; private set; } =
        AvaloniaProperty.Register<DockingTabControl, IBrush>(nameof(DockIndicatorFieldFill), new SolidColorBrush(Colors.CornflowerBlue, 0.5));
    public static StyledProperty<IBrush> DockIndicatorFieldHoveredFillProperty { get; private set; } =
        AvaloniaProperty.Register<DockingTabControl, IBrush>(nameof(DockIndicatorFieldHoveredFill), new SolidColorBrush(Colors.CornflowerBlue));

    public static StyledProperty<IBrush> DockIndicatorFieldStrokeProperty { get; private set; } =
        AvaloniaProperty.Register<DockingTabControl, IBrush>(nameof(DockIndicatorFieldStroke), Brushes.CornflowerBlue);

    public static StyledProperty<double> DockIndicatorFieldStrokeThicknessProperty { get; private set; } =
        AvaloniaProperty.Register<DockingTabControl, double>(nameof(DockIndicatorFieldStrokeThickness), 1);

    public double DockIndicatorFieldSize
    { get => GetValue(DockIndicatorFieldSizeProperty); set => SetValue(DockIndicatorFieldSizeProperty, value); }
    public double DockIndicatorFieldSpacing
    { get => GetValue(DockIndicatorFieldSpacingProperty); set => SetValue(DockIndicatorFieldSpacingProperty, value); }
    public float DockIndicatorFieldCornerRadius
    { get => GetValue(DockIndicatorFieldCornerRadiusProperty); set => SetValue(DockIndicatorFieldCornerRadiusProperty, value); }
    public IBrush DockIndicatorFieldFill
    { get => GetValue(DockIndicatorFieldFillProperty); set => SetValue(DockIndicatorFieldFillProperty, value); }
    public IBrush DockIndicatorFieldHoveredFill
    { get => GetValue(DockIndicatorFieldHoveredFillProperty); set => SetValue(DockIndicatorFieldHoveredFillProperty, value); }
    public IBrush DockIndicatorFieldStroke
    { get => GetValue(DockIndicatorFieldStrokeProperty); set => SetValue(DockIndicatorFieldStrokeProperty, value); }
    public double DockIndicatorFieldStrokeThickness
    { get => GetValue(DockIndicatorFieldStrokeThicknessProperty); set => SetValue(DockIndicatorFieldStrokeThicknessProperty, value); }

    public IPen DockIndicatorStrokePen => _dockIndicatorStrokePen;

    public TabInfo? DraggedTabInfo => _draggedWindow == null
                ? null
                : new TabInfo(_draggedWindow.TabHeader, _draggedWindow.TabItemSize, 
                    _draggedWindow.TabContentSize, _draggedWindow.TabControlSize);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DockIndicatorFieldStrokeProperty ||
            change.Property == DockIndicatorFieldStrokeThicknessProperty)
        {
            _dockIndicatorStrokePen = new Pen(DockIndicatorFieldStroke, DockIndicatorFieldStrokeThickness);
            InvalidateVisual();
            return;
        }
        if (change.Property == DockIndicatorFieldHoveredFillProperty)
        {
            InvalidateVisual();
            return;
        }
    }
    #endregion

    protected override void ChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);
        HandleChildrenModified(this, e);
    }

    private void HandleChildrenModified(Control control, NotifyCollectionChangedEventArgs e)
    {
        foreach (var child in e.OldItems?.OfType<Control>() ?? [])
        {
            if (child is SplitPanel splitPanel)
                UnregisterSplitPanel(splitPanel);
            else if (child is TabControl tabControl)
                UnregisterTabControl(tabControl);
        }

        foreach (var child in e.NewItems?.OfType<Control>() ?? [])
        {
            if (child is SplitPanel splitPanel)
                RegisterSplitPanelForDocking(splitPanel);
            else if (child is TabControl tabControl)
                RegisterTabControlForDocking(tabControl);
        }
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        _overlayWindow?.UpdateAreas(); //probably not needed
    }

    private Window GetHostWindow()
    {
        if (VisualRoot is Window window)
            return window;

        throw new InvalidOperationException(
            $"This {nameof(DockingHost)} is not part of a Window");
    }

    #region Register/Unregister
    private readonly Dictionary<TabControl, LIST_MODIFY_HANDLER> _registeredTabControls = [];
    private readonly Dictionary<SplitPanel, LIST_MODIFY_HANDLER> _registeredSplitPanels = [];

    private void RegisterSplitPanelForDocking(SplitPanel splitPanel)
    {
        Debug.Assert(!_registeredSplitPanels.ContainsKey(splitPanel));
        Orientation orientation = splitPanel.Orientation;


        LIST_MODIFY_HANDLER handler = (s, e) => SplitPanel_ChildrenModified(splitPanel, e);
        splitPanel.Children.CollectionChanged += handler;
        _registeredSplitPanels[splitPanel] = handler;

        foreach (var child in splitPanel.Children)
        {
            if (child is TabControl tabControl)
                RegisterTabControlForDocking(tabControl);
            else if (child is SplitPanel childSplitPanel)
                RegisterSplitPanelForDocking(childSplitPanel);
        }
    }

    private void UnregisterSplitPanel(SplitPanel splitPanel)
    {
        Debug.Assert(_registeredSplitPanels.Remove(splitPanel, out var handler));
        splitPanel.Children.CollectionChanged -= handler;

        foreach (var child in splitPanel.Children)
        {
            if (child is TabControl tabControl)
                UnregisterTabControl(tabControl);
            else if (child is SplitPanel childSplitPanel)
                UnregisterSplitPanel(childSplitPanel);
        }
    }

    private void RegisterTabControlForDocking(TabControl tabControl)
    {
        Debug.Assert(!_registeredTabControls.ContainsKey(tabControl));

        LIST_MODIFY_HANDLER handler = (_, e) => TabControl_ItemsModified(tabControl, e);
        tabControl.Items.CollectionChanged += handler;
        _registeredTabControls[tabControl] = handler;

        if (tabControl is DockingTabControl dockingTabControl)
            dockingTabControl.RegisterDraggedOutTabHanlder(TabControl_DraggedOutTab);
    }

    private void UnregisterTabControl(TabControl tabControl)
    {
        Debug.Assert(_registeredTabControls.Remove(tabControl, out var handler));
        tabControl.Items.CollectionChanged -= handler;

        if (tabControl is DockingTabControl dockingTabControl)
            dockingTabControl.UnregisterDraggedOutTabHanlder();
    }
    #endregion

    #region Handle Tree Modifications
    /// <summary>
    /// Ensures that after a modification there are still no SplitPanels with less than 2 Children
    /// unless it's the root
    /// </summary>
    private void SplitPanel_ChildrenModified(SplitPanel splitPanel, NotifyCollectionChangedEventArgs e)
    {
        if (_ignoreModified.Contains(splitPanel))
            return;

        HandleChildrenModified(splitPanel, e);

        if (e.Action != NotifyCollectionChangedAction.Remove)
            return;

        if (splitPanel.Parent is not Panel parentPanel)
            return;

        int indexInParent = parentPanel.Children.IndexOf(splitPanel);

        if (splitPanel.Children.Count == 1)
        {
            var child = splitPanel.Children[0];

            //panel is still part of the UI Tree and as such can trigger a cascade of unwanted changes
            //and we can't remove it without triggering ChildrenModified
            //or replacing it with a Dummy Control so we have to:
            _ignoreModified.Add(splitPanel);

            if (child is TabControl childTabControl)
                UnregisterTabControl(childTabControl);
            else if (child is SplitPanel childSplitPanel)
                UnregisterSplitPanel(childSplitPanel);

            splitPanel.Children.Clear();
            _ignoreModified.Remove(splitPanel);

            parentPanel.Children[indexInParent] = child;
        }
        else if (splitPanel.Children.Count == 0)
            parentPanel.Children.RemoveAt(indexInParent);
    }

    /// <summary>
    /// Ensures that after a modification there are still no TabControls with no Items in the Dock Tree unless it's the last child of the DockingHost
    /// aka the "Fill" control
    /// </summary>
    private void TabControl_ItemsModified(TabControl tabControl, NotifyCollectionChangedEventArgs e)
    {
        if (tabControl.Items.Count > 0)
            return;

        if (tabControl.Parent is not Panel parent)
            return;

        // it's not supposed to happen but we better catch it
        if (e.OldItems?.Cast<object>().Any(x => x is DummyTabItem) == true &&
            e.OldItems.Count == 1)
            return;

        int indexInParent = parent.Children.IndexOf(tabControl);
        if (parent is SplitPanel parentSplitPanel)
        {
            if (indexInParent < parentSplitPanel.SlotCount)
                parentSplitPanel.RemoveSlot(indexInParent);
        }  
        else if (parent == this && tabControl == Children[^1])
            return;
        else
            parent.Children.RemoveAt(indexInParent);
        
    }
    #endregion

    #region Create Dock Tree Nodes Savely
    /// <summary>
    /// Creates a <see cref="DockingTabControl"/> that has been setup for Docking
    /// </summary>
    private static DockingTabControl CreateTabControl(TabItem initialTabItem)
    {
        var tabControl = new DockingTabControl();
        tabControl.Items.Add(initialTabItem);
        return tabControl;
    }

    /// <summary>
    /// Creates and inserts a <see cref="SplitPanel"/> that has been setup for Docking
    /// <para>It does so in a way that no unwanted side effects are triggered</para>
    /// </summary>
    private static void InsertSplitPanel(Orientation orientation,
        (int fraction, Control child) slot1, (int fraction, Control child) slot2,
        Action<SplitPanel> insertAction)
    {
        var panel = new SplitPanel
        {
            Orientation = orientation,
            Fractions = new SplitFractions(slot1.fraction, slot2.fraction)
        };

        insertAction(panel);
        panel.Children.AddRange([slot1.child, slot2.child]);
    }

    private static void ApplySplitDock(Control targetControl, Dock dock, Size dockSize, Control controlToInsert)
    {
        if (targetControl.Parent is not Panel parent)
            throw new InvalidOperationException();

        Orientation splitOrientation = dock switch
        {
            Dock.Left or Dock.Right => Orientation.Horizontal,
            Dock.Top or Dock.Bottom => Orientation.Vertical,
            _ => throw null!
        };

        var slotSize = targetControl.Bounds.Size;

        (int insertSlotSize, int otherSlotSize) = dock switch
        {
            Dock.Left or Dock.Right => ((int)dockSize.Width, (int)(slotSize.Width - dockSize.Width)),
            Dock.Top or Dock.Bottom => ((int)dockSize.Height, (int)(slotSize.Height - dockSize.Height)),
            _ => throw null!
        };

        Action<SplitPanel> insertAction;

        if (parent is SplitPanel splitPanel)
        {
            int dropSlot = splitPanel.Children.IndexOf(targetControl);
            if (splitPanel.TrySplitSlot(dropSlot, (dock, insertSlotSize, controlToInsert), otherSlotSize))
                return;

            insertAction = panel => parent.Children[dropSlot] = panel;
        }
        else
        {
            insertAction = s =>
            {
                var dock = GetDock(targetControl);
                if (dock != DockProperty.GetDefaultValue(typeof(Control)))
                    s.SetValue(DockProperty, dock);

                parent.Children[parent.Children.IndexOf(targetControl)] = s;
            };
        }

        if (dock is Dock.Left or Dock.Top)
        {
            InsertSplitPanel(splitOrientation,
                (insertSlotSize, controlToInsert), (otherSlotSize, targetControl),
                insertAction);
        }
        else
        {
            InsertSplitPanel(splitOrientation,
                (otherSlotSize, targetControl), (insertSlotSize, controlToInsert),
                insertAction);
        }
    }
    #endregion

    private void TabControl_DraggedOutTab(object? sender, PointerEventArgs e,
        TabItem tabItem, Point offset, Size contentSize)
    {
        var tabControl = (DockingTabControl)sender!;
        var hostWindow = GetHostWindow();

        var root = tabControl.ItemsPanelRoot;

        var window = new DockTabWindow(tabItem, contentSize)
        {
            Width = tabControl.Bounds.Width,
            Height = tabControl.Bounds.Height,
            SystemDecorations = SystemDecorations.None,
            Position = hostWindow.PointToScreen(e.GetPosition(hostWindow) + offset)
        };

        window.Show(hostWindow);

        ChildWindowMoveHandler.Hookup(hostWindow, window);

        _draggedWindow = window;
        window.OnDragStart(e);
        window.Dragging += DockTabWindow_Dragging;
        window.DragEnd += DockTabWindow_DragEnd;
    }

    private void OverlayWindow_AreaExited(object? sender, DockingOverlayWindow.AreaExitedEventArgs e)
    {
        if (e.Control is TabControl tabControl)
        {
            var item = tabControl.Items.FirstOrDefault(x => x is DummyTabItem);

            if (item != null)
                tabControl.Items.Remove(item);
        }
    }

    private void OverlayWindow_AreaEntered(object? sender, DockingOverlayWindow.AreaEnteredEventArgs e)
    {
        Debug.Assert(_draggedWindow != null);

        if (e.Control is TabControl tabControl)
        {
            var item = tabControl.Items.FirstOrDefault(x => x is DummyTabItem);

            if (item != null)
                tabControl.Items.Remove(item);

            tabControl.Items.Add(new DummyTabItem(DockIndicatorStrokePen)
            {
                Width = _draggedWindow.TabItemSize.Width,
                Height = _draggedWindow.TabItemSize.Height,
                Opacity = 0.5
            });
        }
    }

    private void DockTabWindow_Dragging(object? sender, PointerEventArgs e)
    {
        _draggedWindow = (DockTabWindow)sender!;

        if (_overlayWindow == null)
        {
            _overlayWindow = new DockingOverlayWindow(this)
            {
                SystemDecorations = SystemDecorations.None,
                Background = null,
                Opacity = 0.5
            };

            _overlayWindow.AreaEntered += OverlayWindow_AreaEntered;
            _overlayWindow.AreaExited += OverlayWindow_AreaExited;

            _overlayWindow.Show(GetHostWindow());
            _overlayWindow.Position = this.PointToScreen(new Point());
            _overlayWindow.Width = Bounds.Width;
            _overlayWindow.Height = Bounds.Height;
            _overlayWindow.UpdateAreas();
        }

        _overlayWindow.OnPointerMoved(e);
    }

    private void DockTabWindow_DragEnd(object? sender, PointerEventArgs e)
    {
        Debug.Assert(DraggedTabInfo.HasValue);
        Debug.Assert(_overlayWindow != null);

        TabInfo tabInfo = DraggedTabInfo.Value;

        var overlayResult = _overlayWindow.GetResult();

        _overlayWindow.Close();
        _overlayWindow = null;
        _draggedWindow = null;

        TabItem CloseAndDetach()
        {
            DockTabWindow window = (DockTabWindow)sender!;
            TabItem tabItem = window.DetachTabItem();
            window.Close();
            return tabItem;
        }

        TabControl? tabControl;
        Control? target;

        if (overlayResult.IsInsertTab(out tabControl, out int index))
        {
            var tabItem = CloseAndDetach();
            tabControl.Items.Insert(index, tabItem);
        }
        else if (overlayResult.IsFillControl(out target))
        {
            tabControl = target as TabControl;
            if (tabControl == null)
            {
                Debug.Fail("Invalid dropTarget for control");
                return;
            }

            var tabItem = CloseAndDetach();
            tabControl.Items.Add(tabItem);
        }
        else if (overlayResult.IsSplitControl(out target, out Dock dock))
        {
            var tabItem = CloseAndDetach();
            TabControl newTabControl = CreateTabControl(tabItem);
            var dockSize = CalculateDockRect(tabInfo,
                new Rect(default, target.Bounds.Size), dock)
                .Size;

            ApplySplitDock(target, dock, dockSize, newTabControl);
        }
        else if (overlayResult.IsInsertNextTo(out target, out dock))
        {
            var tabItem = CloseAndDetach();
            if (!CanDockNextTo(target, out DockFlags dockFlags, out int insertIndex) ||
                !dockFlags.HasFlag(DockAsFlags(dock)))
                throw new Exception("Layout has changed since tab has been dragged");

            TabControl newTabControl = CreateTabControl(tabItem);
            newTabControl.SetValue(DockProperty, dock);

            if (dock is Dock.Left or Dock.Right)
                newTabControl.Width = tabInfo.ContentSize.Width;
            else
                newTabControl.Height = tabInfo.ContentSize.Height;
            Children.Insert(insertIndex, newTabControl);
        }
        else if (overlayResult.IsInsertOuter(out dock))
        {
            var tabItem = CloseAndDetach();
            TabControl newTabControl = CreateTabControl(tabItem);
            newTabControl.SetValue(DockProperty, dock);

            if (dock is Dock.Left or Dock.Right)
                newTabControl.Width = tabInfo.ContentSize.Width;
            else
                newTabControl.Height = tabInfo.ContentSize.Height;
            Children.Insert(0, newTabControl);
        }
    }

    private static DockFlags DockAsFlags(Dock dock) => dock switch
    {
        Dock.Left => DockFlags.Left,
        Dock.Right => DockFlags.Right,
        Dock.Top => DockFlags.Top,
        Dock.Bottom => DockFlags.Bottom,
        _ => throw null!
    };

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggedWindow?.OnDragEnd(e);
        _draggedWindow = null;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _draggedWindow?.OnCaptureLost(e);
        _draggedWindow = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _draggedWindow?.OnDragging(e);
    }

    public bool CanFill(Control control) => control is TabControl;
    public bool CanSplit(Control control) => 
        control is not TabControl tabControl ||
        //we don't want to split empty TabControls as they are not supposed to be children of SplitPanel
        tabControl.Items.Any(x => x is not DummyTabItem); 

    public bool CanDockNextTo(Control target, out DockFlags dockFlags, out int insertIndex)
    {
        if (target.Parent is not SplitPanel panel)
        {
            if (target.Parent != this)
            {
                dockFlags = DockFlags.None;
                insertIndex = -1;
                return false;
            }

            insertIndex = Children.IndexOf(target);

            Debug.Assert(insertIndex >= 0);

            if (target == Children[^1])
            {
                dockFlags = DockFlags.All;
                return true;
            }
            else
            {
                dockFlags = GetDock(target) switch
                {
                    Dock.Left => DockFlags.Left,
                    Dock.Right => DockFlags.Right,
                    Dock.Top => DockFlags.Top,
                    Dock.Bottom => DockFlags.Bottom,
                    _ => DockFlags.None
                };
                return true;
            }
        }

        bool isVertical = panel.Orientation == Orientation.Vertical;

        if (!CanDockNextTo(panel, out dockFlags, out insertIndex))
            return false;

        DockFlags mask = DockFlags.None;
        if (!isVertical && target == panel.GetControlAtSlot(0))
            mask |= DockFlags.Left;
        if (!isVertical && target == panel.GetControlAtSlot(^1))
            mask |= DockFlags.Right;
        if (isVertical && target == panel.GetControlAtSlot(0))
            mask |= DockFlags.Top;
        if (isVertical && target == panel.GetControlAtSlot(^1))
            mask |= DockFlags.Bottom;

        if (isVertical)
            mask |= DockFlags.Left | DockFlags.Right;
        else
            mask |= DockFlags.Top | DockFlags.Bottom;


        dockFlags &= mask;
        return dockFlags != DockFlags.None;
    }

    public void VisitDockingTreeNodes<T>(Action<T> visitor)
        where T : Control
    {
        foreach (var child in Children)
        {
            if (child is SplitPanel childSplitPanel)
                VisitDockingTreeNodes(childSplitPanel, visitor);
            else if (child is T dockingTabControl)
                visitor(dockingTabControl);
        }
    }

    public static void VisitDockingTreeNodes<T>(SplitPanel splitPanel, Action<T> visitor)
        where T : Control
    {
        foreach (var child in splitPanel.Children)
        {
            if (child is SplitPanel childSplitPanel)
                VisitDockingTreeNodes(childSplitPanel, visitor);
            else if (child is T node)
                visitor(node);
        }
    }

    public static Rect CalculateDockRect(TabInfo tabInfo, Rect fitBounds, Dock dock)
    {
        var clampedWidth = Math.Min(tabInfo.TabControlSize.Width, fitBounds.Width / 2);
        var clampedHeight = Math.Min(tabInfo.TabControlSize.Height, fitBounds.Height / 2);

        return dock switch
        {
            Dock.Left => fitBounds.WithWidth(clampedWidth),
            Dock.Top => fitBounds.WithHeight(clampedHeight),
            Dock.Right => new Rect(
                                        fitBounds.TopLeft.WithX(fitBounds.Right - clampedWidth),
                                        fitBounds.BottomRight),
            Dock.Bottom => new Rect(
                                        fitBounds.TopLeft.WithY(fitBounds.Bottom - clampedHeight),
                                        fitBounds.BottomRight),
            _ => throw new InvalidEnumArgumentException(nameof(dock), (int)dock, typeof(Dock)),
        };
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

    private class DummyTabItem(IPen pen) : TabItem 
    {
        public override void Render(DrawingContext ctx)
        {
            ctx.DrawRectangle(pen, Bounds.WithX(0).WithY(0), 4);
        }
    }
}
