using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Reactor.App.ViewModels;
using Reactor.App.Views;
using Reactor.Core.Configuration;
using Reactor.Core.Diagnostics;

namespace Reactor.App;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log.Initialize(ConfigStore.DefaultDirectory);
            Log.Info("REACTOR starting");

            var store = ConfigStore.Default();
            var config = store.Load();

            _viewModel = new MainViewModel(config, store);

            var window = new MainWindow { DataContext = _viewModel };
            if (config.StartMinimized)
                window.WindowState = Avalonia.Controls.WindowState.Minimized;

            desktop.MainWindow = window;

            // Release the monitoring driver deterministically rather than
            // leaving it to finalisation.
            desktop.ShutdownRequested += (_, _) =>
            {
                Log.Info("Shutdown requested");
                _viewModel?.Dispose();
            };

            var args = desktop.Args ?? Array.Empty<string>();

            // Support aid: land straight on the raw sensor listing.
            if (args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))
                _viewModel.IsDiagnosticsOpen = true;

            // Dev aid; see Screenshot for the flag syntax.
            Screenshot.TryScheduleFromArgs(window, args, () => desktop.Shutdown());

            _viewModel.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
