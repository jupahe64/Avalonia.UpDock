using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace Avalonia.UpDock.Controls;

public class DockingTabControl : TabControl
{
    /// <summary>
    /// Pass this to <see cref="DetachTabItem(TabItemRef)"/> to get the actual TabItem detached from the Logical tree
    /// </summary>
    public struct TabItemRef(TabItem item)
    {
        internal readonly TabItem Item => item;
    }

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

    public delegate void DraggedOutEventHandler(object? sender, PointerEventArgs e, TabItemRef itemRef, Point offset);

    public event DraggedOutEventHandler? DraggedOutTab;


    private IPen? _dockIndicatorStrokePen = new Pen(
        DockIndicatorFieldStrokeProperty.GetDefaultValue(typeof(DockingTabControl)),
        DockIndicatorFieldStrokeThicknessProperty.GetDefaultValue(typeof(DockingTabControl)));


    private (TabItem tabItem, Point offset)? _draggedTab = null;

    private (Size dropTabSize, DropTarget dropTarget)? _draggedForeignTabForDrop = null;

    private readonly TabItem _tabDropIndicator = new()
    {
        Background = DockIndicatorFieldHoveredFillProperty.GetDefaultValue(typeof(DockingTabControl))
    };

    private readonly Rectangle _tabBarHitTarget = new()
    {
        Fill = Brushes.Transparent
    };

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

    public TabItem DetachTabItem(TabItemRef tabItemRef)
    {
        if (!Items.Remove(tabItemRef.Item))
            throw new InvalidOperationException("The given TabItem is not in Items");

        if (tabItemRef.Item == _draggedTab?.tabItem)
            _draggedTab = null;

        return tabItemRef.Item;
    }

    protected override Type StyleKeyOverride => typeof(TabControl);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Source == _tabBarHitTarget)
        {
            return; //for now, maybe this was never needed
        }

        //kinda sucks but I'm out of better ideas

        Point hitPoint = e.GetPosition(this);

        TabItem? tabItem = Items.OfType<TabItem>().FirstOrDefault(x=> HitTest(x, hitPoint));

        if (tabItem == null)
            return;

        Point topLeft = tabItem.TranslatePoint(new Point(0, 0), this)!.Value;

        _draggedTab = (tabItem, topLeft - hitPoint);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _draggedTab = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_draggedTab == null)
            return;

        Point hitPoint = e.GetPosition(this);

        if (!HitTest(_tabBarHitTarget, hitPoint))
        {
            OnTabBarLeft(e);
            return;
        }

        OnDragToRearrange(e);
    }

    private void OnDragToRearrange(PointerEventArgs e)
    {
        Point hitPoint = e.GetPosition(this);

        var (draggedTab, _) = _draggedTab!.Value;

        if (Items.Contains(_tabDropIndicator) || !Items.Contains(draggedTab))
            return; //should never happen

        for (int i = 0; i < Items.Count; i++)
        {
            var tab = (TabItem)Items[i]!;

            if (HitTest(tab, hitPoint))
            {
                if (tab == draggedTab)
                    return;

                bool isAfter = i > Items.IndexOf(draggedTab);

                Items.Remove(draggedTab);

                int index = Items.IndexOf(tab);
                if (isAfter)
                    index++;

                Items.Insert(index, draggedTab);
                return;
            }
        }

        if (Items[^1] != draggedTab)
        {
            Items.Remove(draggedTab);
            Items.Add(draggedTab);
            return;
        }
    }

    private void OnTabBarLeft(PointerEventArgs e)
    {
        var (tabItem, offset) = _draggedTab!.Value;

        DraggedOutTab?.Invoke(this, e, new(tabItem), offset);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var value = base.ArrangeOverride(finalSize);

        var maxY = 0.0;
        foreach (var item in Items)
        {
            maxY = Math.Max(((TabItem)item!).Bounds.Bottom, maxY);
        }

        if (VisualChildren[0] != _tabBarHitTarget)
        {
            VisualChildren.Remove(_tabBarHitTarget);
            VisualChildren.Insert(0, _tabBarHitTarget);
        }

        _tabBarHitTarget.Arrange(new Rect(0, 0, finalSize.Width, maxY));

        return value;
    }

    public void OnDragForeignTabOver(PointerEventArgs e, Size tabSize, object? tabHeader)
    {
        var oldValue = _draggedForeignTabForDrop;
        var hitPoint = e.GetPosition(this);
        if (Bounds.WithX(0).WithY(0).Contains(hitPoint))
            _draggedForeignTabForDrop = (tabSize, EvaluateDropTarget(e));
        else
            _draggedForeignTabForDrop = null;

        if (_draggedForeignTabForDrop == oldValue)
            return;

        if (_draggedForeignTabForDrop.HasValue &&
            _draggedForeignTabForDrop.Value.dropTarget.IsTabBar(out int tabIndex))
        {
            _tabDropIndicator.Header = tabHeader;


            Items.Remove(_tabDropIndicator);
            Items.Insert(tabIndex, _tabDropIndicator);
        }
        else
        {
            Items.Remove(_tabDropIndicator);
        }

        InvalidateVisual();
    }

    public DropTarget EvaluateDropTarget(PointerEventArgs e)
    {
        Point hitPoint = e.GetPosition(this);

        {
            int index = Items.IndexOf(_tabDropIndicator);

            if (index != -1 && HitTest(_tabDropIndicator, hitPoint))
                return DropTarget.TabBar(index);
        }

        {

            int iTab = 0;
            foreach (var item in Items)
            {
                if (item == _tabDropIndicator)
                    continue;

                if (HitTest((TabItem)Items[iTab]!, hitPoint))
                    return DropTarget.TabBar(iTab);

                iTab++;
            }

            if (HitTest(_tabBarHitTarget, hitPoint))
                return DropTarget.TabBar(iTab);
        }

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
        Items.Remove(_tabDropIndicator);

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
            _tabDropIndicator.Background = DockIndicatorFieldHoveredFill;
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

        if (dropTarget.IsFill())
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

    private bool HitTest(Visual visual, Point hitPoint)
    {
        Point topLeft = visual.TranslatePoint(new Point(0, 0), this)!.Value;
        Point bottomRight = visual.TranslatePoint(new Point(visual.Bounds.Width, visual.Bounds.Height), this)!.Value;
        return
            topLeft.X <= hitPoint.X && hitPoint.X <= bottomRight.X &&
            topLeft.Y <= hitPoint.Y && hitPoint.Y <= bottomRight.Y;
    }
}
