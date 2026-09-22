using OptiscalerApp.Models;
using OptiscalerApp.Scanning;
using Xunit;

namespace Optiscaler.Tests;

public sealed class GpuDetectServiceTests
{
    [Fact]
    public void ParsesAllDisplayControllersAndKeepsNumericIds()
    {
        const string output = """
                              Slot:	0000:01:00.0
                              Class:	VGA compatible controller [0300]
                              Vendor:	NVIDIA Corporation [10de]
                              Device:	AD104 [GeForce RTX 4070] [2786]
                              Driver:	nvidia

                              Slot:	0000:0a:00.0
                              Class:	Display controller [0380]
                              Vendor:	Advanced Micro Devices, Inc. [AMD/ATI] [1002]
                              Device:	Navi 31 [Radeon RX 7900 XT/7900 XTX/7900M] [744c]
                              Driver:	amdgpu
                              """;

        var result = GpuDetectService.ParseLinuxGpus(output);

        Assert.Collection(result,
                          gpu =>
                          {
                              Assert.Equal("NVIDIA Corporation AD104 [GeForce RTX 4070]", gpu.Name);
                              Assert.Equal(GpuVendor.Nvidia, gpu.Vendor);
                              Assert.Equal(0x10DEu, gpu.VendorId);
                              Assert.Equal(0x2786u, gpu.DeviceId);
                              Assert.Null(gpu.DedicatedVram);
                          },
                          gpu =>
                          {
                              Assert.Equal("Advanced Micro Devices, Inc. [AMD/ATI] Navi 31 " +
                                           "[Radeon RX 7900 XT/7900 XTX/7900M]", gpu.Name);
                              Assert.Equal(GpuVendor.AMD, gpu.Vendor);
                              Assert.Equal(0x1002u, gpu.VendorId);
                              Assert.Equal(0x744Cu, gpu.DeviceId);
                              Assert.Null(gpu.DedicatedVram);
                          });
    }

    [Fact]
    public void IgnoresNonDisplayAndMalformedRecords()
    {
        const string output = """
                              Slot:	0000:00:14.0
                              Class:	USB controller [0c03]
                              Vendor:	Intel Corporation [8086]
                              Device:	USB Controller [7a60]

                              Slot:	0000:00:02.0
                              Class:	VGA compatible controller [0300]
                              Vendor:	Intel Corporation
                              Device:	Iris Xe Graphics [9a49]
                              """;

        Assert.Empty(GpuDetectService.ParseLinuxGpus(output));
    }
}
