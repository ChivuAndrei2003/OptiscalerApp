namespace OptiscalerApp.Models;

public enum GpuVendor
{
    Nvidia,
    AMD,
    Intel,
    Unknown
}

public sealed record GpuInfo
(
    string Name,
    GpuVendor Vendor,
    uint VendorId,
    uint DeviceId,
    ulong? DedicatedVram
);
