using System.ComponentModel.DataAnnotations;

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
    GpuVendor gpuVendor,
    uint VendorId,
    uint DeviceId

);

public interface GpuDetector
{
    public List<GpuInfo> GetGpu { get; set; }
}