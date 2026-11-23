using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Reactor.App;

/// <summary>Windows monotonic clocks with and without time spent suspended.</summary>
internal sealed class WindowsSessionClock
{
    private readonly long _fallbackStart = Stopwatch.GetTimestamp();
    private readonly (long Elapsed, long Awake)? _start = ReadClocks();

    public (TimeSpan Elapsed, TimeSpan? Awake, TimeSpan? Sleep) Read()
    {
        if (_start is not { } start || ReadClocks() is not { } now)
            return (Stopwatch.GetElapsedTime(_fallbackStart), null, null);

        long elapsed = Math.Max(0, now.Elapsed - start.Elapsed);
        long awake = Math.Clamp(now.Awake - start.Awake, 0, elapsed);
        // The clocks have system-timer granularity. Ignore a sub-tick residual.
        if (elapsed - awake < TimeSpan.TicksPerMillisecond * 32) awake = elapsed;
        return (TimeSpan.FromTicks(elapsed), TimeSpan.FromTicks(awake), TimeSpan.FromTicks(elapsed - awake));
    }

    private static (long Elapsed, long Awake)? ReadClocks()
    {
        if (!OperatingSystem.IsWindows()) return null;
        long before = GetTickCount64();
        if (!QueryUnbiasedInterruptTime(out long awake)) return null;
        long after = GetTickCount64();
        long elapsed = before * TimeSpan.TicksPerMillisecond +
            (after - before) * TimeSpan.TicksPerMillisecond / 2;
        return (elapsed, awake);
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern long GetTickCount64();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUnbiasedInterruptTime(out long ticks);
}
