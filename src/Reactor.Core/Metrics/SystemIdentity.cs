namespace Reactor.Core.Metrics;

/// <summary>Static machine identity, resolved once at startup.</summary>
public sealed record SystemIdentity(
    string HostName,
    string CpuName,
    string GpuName,
    string MotherboardName,
    string PrimaryStorageName,
    int CoreCount,
    bool Elevated,
    bool CpuKernelDriverPresent = false)
{
    public static SystemIdentity Unknown { get; } =
        new("UNKNOWN", "CPU UNAVAILABLE", "GPU UNAVAILABLE", "", "", 0, false);
}
