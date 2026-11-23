using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Reactor.Core.Configuration;
using Reactor.Core.Diagnostics;

namespace Reactor.App.Views;

internal static class WindowPlacement
{
    private sealed record Placement(int X, int Y, double Width, double Height, bool Maximized);
    private static string FilePath => Path.Combine(ConfigStore.DefaultDirectory, "window-placement.json");

    public static void Attach(Window window)
    {
        Placement? saved = Load();
        var lastNormal = saved;
        if (saved is { Width: > 0, Height: > 0 } && double.IsFinite(saved.Width) && double.IsFinite(saved.Height))
        {
            var position = new PixelPoint(saved.X, saved.Y);
            var screen = window.Screens.ScreenFromPoint(position) ?? window.Screens.Primary;
            if (screen is not null)
            {
                var area = screen.WorkingArea;
                double scale = screen.Scaling;
                window.Width = Math.Clamp(saved.Width, window.MinWidth, Math.Max(window.MinWidth, area.Width / scale));
                window.Height = Math.Clamp(saved.Height, window.MinHeight, Math.Max(window.MinHeight, area.Height / scale));
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(
                    Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(window.Width * scale))),
                    Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(window.Height * scale))));
                lastNormal = new Placement(window.Position.X, window.Position.Y, window.Width, window.Height, saved.Maximized);
            }
        }

        bool maximized = saved?.Maximized == true;
        window.Opened += (_, _) =>
        {
            if (maximized && window.WindowState == WindowState.Normal)
                window.WindowState = WindowState.Maximized;
            CaptureNormal();
            window.PositionChanged += (_, _) => CaptureNormal();
            window.SizeChanged += (_, _) => CaptureNormal();
            window.GetObservable(Window.WindowStateProperty).Subscribe(new WindowStateObserver(state =>
            {
                if (state != WindowState.Minimized) maximized = state == WindowState.Maximized;
            }));
        };

        window.Closing += (_, _) =>
        {
            CaptureNormal();
            if (lastNormal is null) return;
            try
            {
                Directory.CreateDirectory(ConfigStore.DefaultDirectory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(lastNormal with { Maximized = maximized }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Could not save window placement: {ex.Message}");
            }
        };

        void CaptureNormal()
        {
            if (window.WindowState != WindowState.Normal || window.ClientSize.Width <= 0 || window.ClientSize.Height <= 0) return;
            lastNormal = new Placement(window.Position.X, window.Position.Y,
                window.ClientSize.Width, window.ClientSize.Height, false);
        }
    }

    private static Placement? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Placement>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"Could not load window placement: {ex.Message}");
            return null;
        }
    }

    private sealed class WindowStateObserver(Action<WindowState> next) : IObserver<WindowState>
    {
        public void OnNext(WindowState value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
