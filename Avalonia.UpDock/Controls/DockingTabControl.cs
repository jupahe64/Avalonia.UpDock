using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Avalonia.UpDock.Controls;

public class DockingTabControl : TabControl
{
    public delegate void DraggedOutTabHandler(object? sender, PointerEventArgs e, TabItem itemRef, Point offset, Size contentSize);

    public void RegisterDraggedOutTabHanlder(DraggedOutTabHandler handler)
    {
        if (_draggedOutTabHandler != null)
            throw new InvalidOperationException(
                $"There is already a {nameof(DraggedOutTabHandler)} registered with this {nameof(DockingTabControl)}\n" +
                $"You must call {nameof(UnregisterDraggedOutTabHanlder)} first");

        _draggedOutTabHandler = handler;
    }

    public void UnregisterDraggedOutTabHanlder() => _draggedOutTabHandler = null;

    private DraggedOutTabHandler? _draggedOutTabHandler;


    private (TabItem tabItem, Point offset)? _draggedTab = null;

    private ItemsPresenter? _itemsPresenterPart;
    private ContentPresenter? _contentPresenterPart;

    public DockingTabControl()
    {
        IsHitTestVisible = true;
        Items.CollectionChanged += Items_CollectionChanged;
        Padding = new Thickness(0);
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<ClosableTabItem>())
                item.Closed -= Item_Closed;
        }
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<ClosableTabItem>())
                item.Closed += Item_Closed;
        }
    }

    private void Item_Closed(object? sender, Interactivity.RoutedEventArgs e)
    {
        var closableTabItem = (ClosableTabItem)sender!;
        Items.Remove(closableTabItem);
    }

    protected override Type StyleKeyOverride => typeof(TabControl);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        //kinda sucks but I'm out of better ideas

        Point hitPoint = e.GetPosition(this);

        TabItem? tabItem = Items.OfType<TabItem>().LastOrDefault(x=> this.GetBoundsOf(x).Contains(hitPoint));

        if (tabItem == null)
            return;

        Point topLeft = tabItem.TranslatePoint(new Point(0, 0), this)!.Value;

        _draggedTab = (tabItem, topLeft - hitPoint);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggedTab = null;
        _draggedTabGhost = null;
        _dragRearrangeDeflicker.Reset();
    }

    private RearrangeDeflickerer _dragRearrangeDeflicker = new();
    private (Rect bounds, int index)? _draggedTabGhost = null;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedTab == null)
            return;

        Point hitPoint = e.GetPosition(this);

        bool tabBarHovered = TryGetTabBarRect(out Rect rect) && rect.Contains(hitPoint);

        if (!tabBarHovered)
        {
            OnTabBarLeft(e);
            return;
        }

        OnDragToRearrange(e);
    }

    private void OnDragToRearrange(PointerEventArgs e)
    {
        var (draggedTab, _) = _draggedTab!.Value;

        #region handle invalid state
        if (Items.Contains(_draggedTab))
        {
            Debug.Fail("Dragged tab is not an Item of this TabControl");
            return;
        }
        #endregion

        if (!TryGetHoveredTabItem(e, out int hoveredIndex, out TabItem? hovered))
            return; //only rearrange when hovering a tab item

        _dragRearrangeDeflicker.Evaluate(hovered, out bool isHoveredValid);

        if (hovered == draggedTab)
            return; //it can not be the same item as the one dragged

        //make dragging back to the last position a lot easier
        if (_draggedTabGhost.HasValue && _draggedTabGhost.Value.bounds.Contains(e.GetPosition(this)))
        {
            Items.Remove(draggedTab);
            Items.Insert(_draggedTabGhost.Value.index, draggedTab);
            return;
        }

        if (!isHoveredValid)
            return; ///don't count the tab hovered after rearrange to prevent flickering
                    ///see <see cref="RearrangeDeflickerer"/>

        int draggedTabIndex = Items.IndexOf(draggedTab);

        bool isAfter = hoveredIndex > draggedTabIndex;

        _draggedTabGhost = (this.GetBoundsOf(draggedTab), draggedTabIndex);

        Items.RemoveAt(draggedTabIndex);

        //insert before or after hovered depending on which "direction" you are dragging
        if (isAfter)
            Items.Insert(Items.IndexOf(hovered) + 1, draggedTab);
        else
            Items.Insert(Items.IndexOf(hovered), draggedTab);

        _dragRearrangeDeflicker.SetRearranged();
    }

    private bool TryGetHoveredTabItem(PointerEventArgs e, 
        out int index, [NotNullWhen(true)] out TabItem? hovered)
    {
        Point hitPoint = e.GetPosition(this);

        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var tab = (TabItem)Items[i]!;

            var tabItemBounds = this.GetBoundsOf(tab);

            if (tabItemBounds.Contains(hitPoint))
            {
                hovered = tab;
                index = i;
                return true;
            }
        }

        hovered = null;
        index = -1;
        return false;
    }

    private void OnTabBarLeft(PointerEventArgs e)
    {
        var (tabItem, offset) = _draggedTab!.Value;

        if (_draggedOutTabHandler == null)
            return;

        //modifying Items has side effects, so we can't rely on the handler still having a value
        var handler = _draggedOutTabHandler;

        Items.Remove(tabItem);

        _draggedTab = null;
        _draggedTabGhost = null;

        handler.Invoke(this, e, tabItem, offset, _contentPresenterPart?.Bounds.Size ?? Bounds.Size);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _itemsPresenterPart = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");
        _contentPresenterPart = e.NameScope.Find<ContentPresenter>("PART_SelectedContentHost");
    }

    private bool TryGetTabBarRect(out Rect rect)
    {
        var tabBarPanel = _itemsPresenterPart?.Panel;
        if (tabBarPanel is null)
        {
            rect = default;
            return false;
        }

        rect = this.GetBoundsOf(tabBarPanel);
        return true;
    }

    private class RearrangeDeflickerer
    {
        private object? _hoveredObjectAfterRearrange = null;
        private bool _hasUnaccountedRearrange = false;

        public void SetRearranged()
        {
            _hoveredObjectAfterRearrange = null;
            _hasUnaccountedRearrange = true;
        }

        public void Evaluate(object hovered, out bool isValid)
        {
            if (_hasUnaccountedRearrange)
            {
                _hoveredObjectAfterRearrange = hovered;
                _hasUnaccountedRearrange = false;
                isValid = false;
                return;
            }

            if (hovered == _hoveredObjectAfterRearrange)
            {
                isValid = false;
                return;
            }

            _hoveredObjectAfterRearrange = null;
            isValid = true;
            return;
        }

        public void Reset()
        {
            _hoveredObjectAfterRearrange = null;
            _hasUnaccountedRearrange = false;
        }
    }
}
