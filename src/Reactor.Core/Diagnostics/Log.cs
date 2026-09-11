using System.Diagnostics;
using System.Text;

namespace Reactor.Core.Diagnostics;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Deliberately tiny file logger. The app has one process, one log file and no
/// need for structured logging infrastructure; anything more would be ceremony.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly StringBuilder Pending = new();
    private static string? _path;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static string? FilePath => _path;

    public static void Initialize(string directory, string fileName = "reactor.log")
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                _path = Path.Combine(directory, fileName);

                // Keep the log from growing without bound over long sessions.
                if (File.Exists(_path) && new FileInfo(_path).Length > 512 * 1024)
                    File.Delete(_path);

                File.AppendAllText(_path,
                    $"{Environment.NewLine}=== REACTOR session start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");

                if (Pending.Length > 0)
                {
                    File.AppendAllText(_path, Pending.ToString());
                    Pending.Clear();
                }
            }
            catch (Exception ex)
            {
                _path = null;
                Debug.WriteLine($"[reactor] log init failed: {ex.Message}");
            }
        }
    }

    public static void Debug_(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;

        var line = $"{DateTime.Now:HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-5} {message}{Environment.NewLine}";
        Debug.Write("[reactor] " + line);

        lock (Gate)
        {
            if (_path is null)
            {
                if (Pending.Length < 64 * 1024) Pending.Append(line);
                return;
            }

            try { File.AppendAllText(_path, line); }
            catch { /* logging must never take the app down */ }
        }
    }
}
