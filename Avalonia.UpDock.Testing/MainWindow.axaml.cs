using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.UpDock.Controls;
using System;

namespace Avalonia.UpDock.Testing;

public partial class MainWindow : Window
{
    private DockingManager? _dockingManager;

    public MainWindow()
    {
        InitializeComponent(true, true);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (Design.IsDesignMode)
            return;

        _dockingManager = new DockingManager(this, DockingHost);
    }
}