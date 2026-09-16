using OptiscalerApp.Models;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace OptiscalerApp.Scanning;

public class GpuDetectService : GpuDetector
{
    public GpuDetectService()
    {
    }
   //LinuxGpuDetectionService && WindowsGpuDetectionService 
    public List<GpuInfo> GetGpu { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
}