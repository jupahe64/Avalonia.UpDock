using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.UpDock.Testing;

namespace Avalonia.UpDock;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
