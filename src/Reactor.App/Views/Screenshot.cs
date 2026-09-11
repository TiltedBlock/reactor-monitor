using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Reactor.Core.Diagnostics;

namespace Reactor.App.Views;

/// <summary>
/// Development aid: <c>Reactor.exe --screenshot out.png [seconds]</c> renders
/// the window to a PNG once telemetry has settled, then exits. Renders the
/// visual tree directly rather than grabbing the screen, so it captures exactly
/// this window and nothing else on the desktop.
/// </summary>
public static class Screenshot
{
    public static bool TryScheduleFromArgs(Window window, string[] args, Action onDone)
    {
        var index = Array.FindIndex(args, a =>
            string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));

        if (index < 0 || index + 1 >= args.Length) return false;

        var path = args[index + 1];
        var seconds = index + 2 < args.Length && double.TryParse(args[index + 2], out var s) ? s : 7;

        DispatcherTimer.RunOnce(() =>
        {
            Capture(window, path);
            onDone();
        }, TimeSpan.FromSeconds(seconds));

        return true;
    }

    public static void Capture(Window window, string path)
    {
        try
        {
            var scaling = window.RenderScaling;
            var size = new PixelSize(
                Math.Max(1, (int)Math.Round(window.Bounds.Width * scaling)),
                Math.Max(1, (int)Math.Round(window.Bounds.Height * scaling)));

            using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(window);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            bitmap.Save(path);
            Log.Info($"Screenshot written to {path}");
        }
        catch (Exception ex)
        {
            Log.Error("Screenshot failed", ex);
        }
    }
}
