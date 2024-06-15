using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.UpDock.Controls;
using System;

namespace Avalonia.UpDock;

internal class DockTabWindow : Window
{
    public Size TabContentSize { get; private set; }
    public Size TabItemSize { get; private set; }
    public Size TabControlSize { get; private set; }
    public object? TabHeader => _tabItem.Header;

    private record DragInfo(Point Offset);

    private IBrush? _tabBackground = null;
    private IBrush? _tabItemBackground = null;
    private IPen _borderPen = new Pen(Brushes.Gray, 1);
    private TabItem _tabItem;
    private readonly Size _contentSize;
    private HookedTabControl _tabControl;

    private DragInfo? _dragInfo = null;

    public event EventHandler<PointerEventArgs>? Dragging;
    public event EventHandler<PointerEventArgs>? DragEnd;

    public TabItem DetachTabItem()
    {
        _tabItem.PointerPressed -= TabItem_PointerPressed;
        _tabItem.PointerMoved -= TabItem_PointerMoved;
        _tabItem.PointerReleased -= TabItem_PointerReleased;
        _tabItem.PointerCaptureLost -= TabItem_PointerCaptureLost;

        _tabControl.Items.Clear();

        if (_tabItem is ClosableTabItem closable)
            closable.Closed -= TabItem_Closed;

        _tabItem.Background = _tabItemBackground;

        return _tabItem;
    }

    public DockTabWindow(TabItem tabItem, Size contentSize)
    {
        #if DEBUG
        this.AttachDevTools();
        #endif

        _tabItem = tabItem;
        _contentSize = contentSize;
        _tabItem.PointerPressed += TabItem_PointerPressed;
        _tabItem.PointerMoved += TabItem_PointerMoved;
        _tabItem.PointerReleased += TabItem_PointerReleased;
        _tabItem.PointerCaptureLost += TabItem_PointerCaptureLost;

        if (_tabItem is ClosableTabItem closable)
            closable.Closed += TabItem_Closed;

        _tabItemBackground = tabItem.Background;


        _tabControl = new HookedTabControl()
        {
            Background = Brushes.Transparent, //just to be save
        };
        _tabControl.Items.Add(tabItem);

        SizeToContent = SizeToContent.WidthAndHeight;
        Content = _tabControl;

        _tabControl.LayoutUpdated += TabControl_LayoutUpdated;
        _tabControl.Padding = new Thickness(0);
    }

    private void TabControl_LayoutUpdated(object? sender, EventArgs e)
    {
        if (_tabControl.Bounds.Size == default)
            return;

        var presenter = _tabControl.ContentPresenter;
        if (presenter != null)
        {
            var extraWidth = _tabControl.Bounds.Width - presenter.Bounds.Width;
            var extraHeight = _tabControl.Bounds.Height - presenter.Bounds.Height;
            
            //probably not needed
            extraWidth += Width - _tabControl.Bounds.Width;
            extraHeight += Height - _tabControl.Bounds.Height;

            var newWidth = Math.Max(Width, _contentSize.Width + extraWidth);
            var newHeight = Math.Max(Height, _contentSize.Height + extraHeight);

            SizeToContent = SizeToContent.Manual;

            Width = newWidth;
            Height = newHeight;
        }

        _tabControl.LayoutUpdated -= TabControl_LayoutUpdated;
    }

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        TabContentSize = _tabControl.ContentPresenter?.Bounds.Size ?? _tabControl.Bounds.Size;
        TabItemSize = _tabItem.Bounds.Size;
        TabControlSize = _tabControl.Bounds.Size;
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

    private void TabItem_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => OnCaptureLost(e);

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

        context.FillRectangle(_tabBackground!, new Rect(topLeft, bottomRight));
        context.FillRectangle(_tabBackground!, rect);
        context.DrawRectangle(_borderPen, rect);
        base.Render(context);
    }

    private PointerEventArgs? _lastPointerEvent = null;

    public void OnDragStart(PointerEventArgs e)
    {
        SystemDecorations = SystemDecorations.None;
        _dragInfo = new(e.GetPosition(this));
        _lastPointerEvent = e;
    }

    public void OnDragEnd(PointerEventArgs e)
    {
        _dragInfo = null;
        DragEnd?.Invoke(this, e);
        SystemDecorations = SystemDecorations.Full;
        _lastPointerEvent = null;
    }

    public void OnDragging(PointerEventArgs e)
    {
        if (_dragInfo == null)
            return;

        _lastPointerEvent = e;

        Point offset = _dragInfo.Offset;

        Position = this.PointToScreen(e.GetPosition(this) - offset);
        Dragging?.Invoke(this, e);
    }

    public void OnCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_lastPointerEvent != null)
            OnDragEnd(_lastPointerEvent);
    }

    private class HookedTabControl : TabControl
    {
        public ContentPresenter? ContentPresenter { get; private set; }
        protected override Type StyleKeyOverride => typeof(TabControl);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            ContentPresenter = e.NameScope.Find<ContentPresenter>("PART_SelectedContentHost");
        }
    }
}
