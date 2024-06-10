using Avalonia.Controls;
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
        TabContentSize = rect.Size;
        TabItemSize = _tabItem.Bounds.Size;

        context.FillRectangle(_tabBackground!, new Rect(topLeft, bottomRight));
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
