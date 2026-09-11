using Microsoft.Win32;
using Reactor.Core.Diagnostics;

namespace Reactor.Hardware;

/// <summary>
/// Answers "why is there no CPU package telemetry?".
///
/// Reading a CPU's package temperature, package power and per-core clocks means
/// reading model-specific registers and the on-die management controller, which
/// needs a kernel driver. LibreHardwareMonitor 0.9.x no longer ships its own:
/// it carries PawnIO modules (AMDFamily17, RyzenSMU, IntelMSR, LpcIO) and
/// executes them through the separately installed, Microsoft-signed PawnIO
/// driver. Older releases used WinRing0, which current Windows blocks through
/// the vulnerable-driver blocklist.
///
/// When PawnIO is absent those sensors are still published - they simply never
/// produce a value. Without this check the app would blame elevation, which is
/// necessary but not sufficient, and the user would keep re-running as
/// administrator to no effect.
/// </summary>
public static class CpuDriverProbe
{
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";

    public static bool IsKernelDriverPresent()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(ServiceKey))
                if (key is not null) return true;

            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (File.Exists(Path.Combine(system32, "drivers", "PawnIO.sys"))) return true;

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (Directory.Exists(Path.Combine(programFiles, "PawnIO"))) return true;
        }
        catch (Exception ex)
        {
            // Never let a diagnostic check break startup.
            Log.Warn($"CPU kernel driver probe failed: {ex.GetType().Name}: {ex.Message}");
        }

        return false;
    }

    /// <summary>One line for the log explaining what CPU telemetry to expect.</summary>
    public static string Describe(bool elevated, bool driverPresent) => (elevated, driverPresent) switch
    {
        (false, false) => "not elevated and no PawnIO driver - CPU package sensors will be unavailable",
        (false, true) => "PawnIO present but not elevated - CPU package sensors will be unavailable",
        (true, false) => "elevated but PawnIO driver not installed - CPU package sensors will be unavailable",
        (true, true) => "elevated with PawnIO driver present - CPU package sensors should be readable"
    };
}
