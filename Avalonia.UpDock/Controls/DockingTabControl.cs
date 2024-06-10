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
    #region DropTarget "enum"
    public struct DropTarget : IEquatable<DropTarget>
    {
        private Dock _dock;
        private bool _isDock;
        private bool _isFill;
        private bool _isTabBar;
        private int _tabIndex;

        public static DropTarget None => new();

        public static DropTarget Dock(Dock dock) 
            => new() { _isDock = true, _dock = dock};
        public static DropTarget Fill 
            => new() { _isFill = true};
        public static DropTarget TabBar(int tabIndex) 
            => new() { _isTabBar = true, _tabIndex = tabIndex};

        public readonly bool IsNone() => !_isDock && !_isFill && !_isTabBar;

        public readonly bool IsDock(Dock dock) => IsDock(out var value) && value == dock;
        public readonly bool IsDock(out Dock dock)
        {
            dock = _dock;
            return _isDock;
        }

        public readonly bool IsFill() => _isFill;

        public readonly bool IsTabBar(int tabIndex) => IsTabBar(out var value) && value == tabIndex;
        public readonly bool IsTabBar(out int tabIndex)
        {
            tabIndex = _tabIndex;
            return _isTabBar;
        }

        public readonly bool Equals(DropTarget other)
        {
            return
                _dock == other._dock && 
                _isDock == other._isDock &&
                _isFill == other._isFill &&
                _isTabBar == other._isTabBar &&
                _tabIndex == other._tabIndex;
        }

        public static bool operator == (DropTarget left, DropTarget right) => left.Equals(right);
        public static bool operator != (DropTarget left, DropTarget right) => !left.Equals(right);

        public override readonly bool Equals(object? obj)
        {
            return obj is DropTarget && Equals((DropTarget)obj);
        }

        public override readonly int GetHashCode() => 0;
    }
    #endregion

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
    {get => GetValue(DockIndicatorFieldSizeProperty); set => SetValue(DockIndicatorFieldSizeProperty, value);}
    public double DockIndicatorFieldSpacing 
    {get => GetValue(DockIndicatorFieldSpacingProperty); set => SetValue(DockIndicatorFieldSpacingProperty, value);}
    public float DockIndicatorFieldCornerRadius 
    {get => GetValue(DockIndicatorFieldCornerRadiusProperty); set => SetValue(DockIndicatorFieldCornerRadiusProperty, value);}
    public IBrush DockIndicatorFieldFill 
    {get => GetValue(DockIndicatorFieldFillProperty); set => SetValue(DockIndicatorFieldFillProperty, value);}
    public IBrush DockIndicatorFieldHoveredFill
    { get => GetValue(DockIndicatorFieldHoveredFillProperty); set => SetValue(DockIndicatorFieldHoveredFillProperty, value); }
    public IBrush DockIndicatorFieldStroke 
    {get => GetValue(DockIndicatorFieldStrokeProperty); set => SetValue(DockIndicatorFieldStrokeProperty, value);}
    public double DockIndicatorFieldStrokeThickness 
    { get => GetValue(DockIndicatorFieldStrokeThicknessProperty); set => SetValue(DockIndicatorFieldStrokeThicknessProperty, value); }
    #endregion

    public delegate void DraggedOutTabHandler(object? sender, PointerEventArgs e, TabItem itemRef, Point offset);

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


    private IPen? _dockIndicatorStrokePen = new Pen(
        DockIndicatorFieldStrokeProperty.GetDefaultValue(typeof(DockingTabControl)),
        DockIndicatorFieldStrokeThicknessProperty.GetDefaultValue(typeof(DockingTabControl)));


    private (TabItem tabItem, Point offset)? _draggedTab = null;

    private (Size dropTabSize, DropTarget dropTarget)? _draggedForeignTabForDrop = null;
    private ItemsPresenter? _itemsPresenterPart;

    public DockingTabControl()
    {
        IsHitTestVisible = true;
        Items.CollectionChanged += Items_CollectionChanged;
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

        TabItem? tabItem = Items.OfType<TabItem>().LastOrDefault(x=> GetBounds(x).Contains(hitPoint));

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
        if (Items.Contains(_appendPlaceholderTab))
        {
            Debug.Fail("Found placeholder tab in Items when rearranging");
            return;
        }

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

        _draggedTabGhost = (GetBounds(draggedTab), draggedTabIndex);

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

            var tabItemBounds = GetBounds(tab);

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

        handler.Invoke(this, e, tabItem, offset);
    }

    private TabItem _appendPlaceholderTab = new DummyTabItem
    {
        Opacity = 0.5
    };

    public void OnDragForeignTabOver(PointerEventArgs e, Size tabContentSize, Size tabItemSize)
    {
        _appendPlaceholderTab.Width = tabItemSize.Width;
        _appendPlaceholderTab.Height = tabItemSize.Height;

        if (!Items.Contains(_appendPlaceholderTab))
            Items.Add(_appendPlaceholderTab);

        var oldValue = _draggedForeignTabForDrop;
        var hitPoint = e.GetPosition(this);
        if (Bounds.WithX(0).WithY(0).Contains(hitPoint))
            _draggedForeignTabForDrop = (tabContentSize, EvaluateDropTarget(e));
        else
            _draggedForeignTabForDrop = null;

        if (_draggedForeignTabForDrop == oldValue)
            return;

        InvalidateVisual();
    }

    public DropTarget EvaluateDropTarget(PointerEventArgs e)
    {
        Point hitPoint = e.GetPosition(this);

        if (TryGetHoveredTabItem(e, out int index, out _))
                return DropTarget.TabBar(index);

        Span<Rect> rects = stackalloc Rect[9];
        Evaluate3x3DockIndicatorRects(rects, out _);

        if (rects[1].Contains(hitPoint))
            return DropTarget.Dock(Dock.Top);

        if (rects[3].Contains(hitPoint))
            return DropTarget.Dock(Dock.Left);

        if (rects[4].Contains(hitPoint))
            return DropTarget.Fill;

        if (rects[5].Contains(hitPoint))
            return DropTarget.Dock(Dock.Right);

        if (rects[7].Contains(hitPoint))
            return DropTarget.Dock(Dock.Bottom);

        return DropTarget.None;
    }

    public void OnEndDragForeignTab()
    {
        _draggedForeignTabForDrop = null;
        Items.Remove(_appendPlaceholderTab);

        InvalidateVisual();
    }

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

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_draggedForeignTabForDrop == null)
            return;

        var (tabSize, dropTarget) = _draggedForeignTabForDrop.Value;

        var pen = _dockIndicatorStrokePen!;
        var brush = DockIndicatorFieldFill!;
        var hoveredBrush = DockIndicatorFieldHoveredFill!;

        var splitWidth = Math.Min(tabSize.Width, Bounds.Width / 2);
        var splitHeight = Math.Min(tabSize.Height, Bounds.Height / 2);

        if (dropTarget.IsTabBar(out int tabIndex))
        {
            var item = Items[tabIndex];
            if (item is TabItem tabItem)
            {
                var bounds = GetBounds(tabItem)
                    .WithWidth(_appendPlaceholderTab.Width)
                    .WithHeight(_appendPlaceholderTab.Height);
                context.FillRectangle(brush, bounds);
            }
        }
        else if (dropTarget.IsFill())
            context.FillRectangle(brush, Bounds.WithX(0).WithY(0));

        else if (dropTarget.IsDock(Dock.Top))
            context.FillRectangle(brush, new Rect(0, 0, Bounds.Width, splitHeight));

        else if (dropTarget.IsDock(Dock.Left))
            context.FillRectangle(brush, new Rect(0, 0, splitWidth, Bounds.Height));

        else if (dropTarget.IsDock(Dock.Bottom))
            context.FillRectangle(brush, new Rect(0, Bounds.Height - splitHeight, Bounds.Width, splitHeight));

        else if (dropTarget.IsDock(Dock.Right))
            context.FillRectangle(brush, new Rect(Bounds.Width - splitWidth, 0, splitWidth, Bounds.Height));

        Span<Rect> rects = stackalloc Rect[9];
        Evaluate3x3DockIndicatorRects(rects, out double scaling);
        var cornerRadius = DockIndicatorFieldCornerRadius * (float)scaling;

        static (Rect left, Rect right) SplitHorizontally(Rect r) =>
            (new Rect(r.TopLeft, new Point(r.Center.X, r.Bottom)), new Rect(new Point(r.Center.X, r.Top), r.BottomRight));

        static (Rect top, Rect bottom) SplitVertically(Rect r) =>
            (new Rect(r.TopLeft, new Point(r.Right, r.Center.Y)), new Rect(new Point(r.Left, r.Center.Y), r.BottomRight));


        context.DrawRectangle(pen, rects[1], cornerRadius);
        context.FillRectangle(dropTarget.IsDock(Dock.Top) ? hoveredBrush : brush,
            SplitVertically(rects[1]).top, cornerRadius);

        context.DrawRectangle(pen, rects[3], cornerRadius);
        context.FillRectangle(dropTarget.IsDock(Dock.Left) ? hoveredBrush : brush,
            SplitHorizontally(rects[3]).left, cornerRadius);

        context.DrawRectangle(pen, rects[4], cornerRadius);
        context.FillRectangle(dropTarget.IsFill() ? hoveredBrush : brush,
            rects[4], 5);

        context.DrawRectangle(pen, rects[5], cornerRadius);
        context.FillRectangle(dropTarget.IsDock(Dock.Right) ? hoveredBrush : brush,
            SplitHorizontally(rects[5]).right, cornerRadius);

        context.DrawRectangle(pen, rects[7], cornerRadius);
        context.FillRectangle(dropTarget.IsDock(Dock.Bottom) ? hoveredBrush : brush,
            SplitVertically(rects[7]).bottom, cornerRadius);
    }

    private void Evaluate3x3DockIndicatorRects(Span<Rect> rects, out double scaling)
    {
        double totalSize = DockIndicatorFieldSize * 3 + DockIndicatorFieldSpacing * 2;

        scaling = Math.Min(Bounds.Width, Bounds.Height) / totalSize;
        scaling = Math.Min(scaling, 1);

        Size indicatorSizeScaled = new Size(DockIndicatorFieldSize, DockIndicatorFieldSize) * scaling;
        double spacingScaled = DockIndicatorFieldSpacing * scaling;

        var localBounds = Bounds.WithX(0).WithY(0);

        var distance = indicatorSizeScaled.Width + spacingScaled;

        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                rects[x + y * 3] = localBounds.CenterRect(new Rect(indicatorSizeScaled))
                    .Translate(new Vector(distance * (x - 1), distance * (y - 1)));
            }
        }
    }

    private Rect GetBounds(Visual visual)
    {
        Point topLeft = visual.TranslatePoint(new Point(0, 0), this)!.Value;
        Point bottomRight = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), this)!.Value;
        return new Rect(topLeft, bottomRight);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _itemsPresenterPart = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");
    }

    private bool TryGetTabBarRect(out Rect rect)
    {
        var tabBarPanel = _itemsPresenterPart?.Panel;
        if (tabBarPanel is null)
        {
            rect = default;
            return false;
        }

        rect = GetBounds(tabBarPanel);
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

    private class DummyTabItem : TabItem { }
}
