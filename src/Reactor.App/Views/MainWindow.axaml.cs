using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Reactor.App.ViewModels;

namespace Reactor.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The window draws its own chrome, so it also has to provide the
        // behaviour the system title bar would normally give us.
        var titleBar = this.FindControl<Border>("TitleBar")!;
        titleBar.PointerPressed += OnTitleBarPressed;

        this.FindControl<Button>("MinimizeButton")!.Click += (_, _) => WindowState = WindowState.Minimized;
        this.FindControl<Button>("MaximizeButton")!.Click += (_, _) => ToggleMaximize();
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();

        KeyDown += OnKeyDown;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // A click on one of the buttons in the bar must not start a drag.
        if (e.Source is Control source && source.FindAncestorOfType<Button>() is not null) return;

        if (e.ClickCount == 2) ToggleMaximize();
        else BeginMoveDrag(e);
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.Key)
        {
            case Key.F12:
                vm.IsDiagnosticsOpen = !vm.IsDiagnosticsOpen;
                break;
            case Key.Escape:
                vm.IsDiagnosticsOpen = false;
                vm.IsSettingsOpen = false;
                break;
            case Key.F11:
                ToggleMaximize();
                break;
        }
    }
}
