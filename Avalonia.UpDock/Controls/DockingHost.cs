using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using static Avalonia.UpDock.Controls.DockingTabControl;
using LIST_MODIFY_HANDLER = System.Collections.Specialized.NotifyCollectionChangedEventHandler;

namespace Avalonia.UpDock.Controls;

public class DockingHost : DockPanel
{
    private DockTabWindow? _draggedWindow;
    private readonly HashSet<SplitPanel> _ignoreModified = [];

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
                RegisterSplitPanelForDocking(splitPanel, (control as SplitPanel)?.Orientation);
            else if (child is TabControl tabControl)
                RegisterTabControlForDocking(tabControl);
        }
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

    private void RegisterSplitPanelForDocking(SplitPanel splitPanel, Orientation? parentOrientation = null)
    {
        Debug.Assert(!_registeredSplitPanels.ContainsKey(splitPanel));
        bool hasParent = parentOrientation.HasValue;
        Orientation orientation = splitPanel.Orientation;


        LIST_MODIFY_HANDLER handler = (s, e) => SplitPanel_ChildrenModified(splitPanel, e);
        splitPanel.Children.CollectionChanged += handler;
        _registeredSplitPanels[splitPanel] = handler;

        foreach (var child in splitPanel.Children)
        {
            if (child is TabControl tabControl)
                RegisterTabControlForDocking(tabControl);
            else if (child is SplitPanel childSplitPanel)
                RegisterSplitPanelForDocking(childSplitPanel, orientation);
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
        Debug.WriteLine($"Registering " +
            $"{string.Join(' ',tabControl.Items.OfType<TabItem>().Select(x => x.Header))}");

        LIST_MODIFY_HANDLER handler = (_, _) => TabControl_ItemsModified(tabControl);
        tabControl.Items.CollectionChanged += handler;
        _registeredTabControls[tabControl] = handler;

        if (tabControl is DockingTabControl dockingTabControl)
            dockingTabControl.RegisterDraggedOutTabHanlder(TabControl_DraggedOutTab);
    }

    private void UnregisterTabControl(TabControl tabControl)
    {
        Debug.WriteLine($"Unregistering " +
            $"{string.Join(' ', tabControl.Items.OfType<TabItem>().Select(x => x.Header))}");

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
    private void SplitPanel_ChildrenModified(SplitPanel panel, NotifyCollectionChangedEventArgs e)
    {
        if (_ignoreModified.Contains(panel))
            return;

        HandleChildrenModified(panel, e);

        if (e.Action != NotifyCollectionChangedAction.Remove)
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

            if (child is TabControl tabControl)
                UnregisterTabControl(tabControl);
            else if (child is SplitPanel splitPanel)
                UnregisterSplitPanel(splitPanel);

            panel.Children.Clear();
            _ignoreModified.Remove(panel);

            parent.Children[slotInParent] = child;
        }
        else if (panel.Children.Count == 0)
            parent.Children.RemoveAt(slotInParent);
    }

    /// <summary>
    /// Ensures that after a modification there are still no TabControls with no Items
    /// unless it's the only child in the root or the only other children are unsupported Controls
    /// </summary>
    private static void TabControl_ItemsModified(TabControl tabControl)
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
        var window = (DockTabWindow)sender!;

        Point hitPoint = e.GetPosition(GetHostWindow());
        VisitDockingTabControls((tabControl) =>
        {
            tabControl.OnDragForeignTabOver(e, window.TabContentSize, window.TabItemSize);
        });
    }

    public void VisitDockingTabControls(Action<DockingTabControl> visitor)
    {
        foreach (var child in Children)
        {
            if (child is DockingTabControl dockingTabControl)
                visitor(dockingTabControl);
            else if (child is SplitPanel childSplitPanel)
                VisitDockingTabControls(childSplitPanel, visitor);
        }
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
        Point hitPoint = e.GetPosition(this);

        DockingTabControl? dropTabControl = null;
        DropTarget dropTarget = DropTarget.None;

        VisitDockingTabControls((tabControl) =>
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

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_draggedWindow is null)
            return;

        _draggedWindow.OnDragEnd(e);

        _draggedWindow = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedWindow is null)
            return;

        _draggedWindow.OnDragging(e);
    }

    private bool HitTest(Visual visual, Point hitPoint)
    {
        Point topLeft = visual.TranslatePoint(new Point(0, 0), this)!.Value;
        Point bottomRight = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), this)!.Value;
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
