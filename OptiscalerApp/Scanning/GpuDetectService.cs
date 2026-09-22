using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using CliWrap.Exceptions;
using OptiscalerApp.Models;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace OptiscalerApp.Scanning;

public static class GpuDetectService
{
    private static readonly Regex PciIdAtEnd = new(@"\[([0-9a-f]{4})\]\s*$",
                                                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task<IReadOnlyList<GpuInfo>> DetectGpus_Async(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows()) return DetectGpusOnWindows();
        if (OperatingSystem.IsLinux()) return await DetectGpusOnLinux_Async(cancellationToken);

        return [];
    }

    public static List<GpuInfo> DetectGpusOnWindows()
    {
        var result = new List<GpuInfo>();
        using IDXGIFactory1 factory = CreateDXGIFactory1<IDXGIFactory1>();

        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            using (adapter)
            {
                var description = adapter.Description1;
                if ((description.Flags & AdapterFlags.Software) != 0) continue;

                result.Add(new GpuInfo(
                    description.Description,
                    GetVendor(description.VendorId),
                    description.VendorId,
                    description.DeviceId,
                    (ulong)description.DedicatedVideoMemory
                ));
            }
        }

        return result;
    }

    public static async Task<List<GpuInfo>> DetectGpusOnLinux_Async(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            var result = await Cli.Wrap("lspci")
                .WithArguments(["-D", "-vmm", "-nn", "-d", "::03xx"])
                .WithEnvironmentVariables(environment => environment.Set("LC_ALL", "C"))
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(timeout.Token);

            return result.ExitCode == 0 ? ParseLinuxGpus(result.StandardOutput) : [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (Win32Exception)
        {
            return [];
        }
        catch (CommandExecutionException)
        {
            return [];
        }
    }

    internal static List<GpuInfo> ParseLinuxGpus(string output)
    {
        var result = new List<GpuInfo>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(output);

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                AddLinuxGpu(fields, result);
                fields.Clear();
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator > 0)
                fields[line[..separator]] = line[(separator + 1)..].Trim();
        }

        AddLinuxGpu(fields, result);
        return result;
    }

    private static void AddLinuxGpu(IReadOnlyDictionary<string, string> fields, ICollection<GpuInfo> result)
    {
        if (!fields.TryGetValue("Class", out var classValue) ||
            !TryReadPciId(classValue, out var classId, out _) ||
            (classId >> 8) != 0x03 ||
            !fields.TryGetValue("Vendor", out var vendorValue) ||
            !TryReadPciId(vendorValue, out var vendorId, out var vendorName) ||
            !fields.TryGetValue("Device", out var deviceValue) ||
            !TryReadPciId(deviceValue, out var deviceId, out var deviceName))
        {
            return;
        }

        var vendor = GetVendor(vendorId);
        var name = $"{vendorName} {deviceName}".Trim();

        if (string.IsNullOrWhiteSpace(name))
            name = $"{vendor} GPU [{vendorId:X4}:{deviceId:X4}]";

        result.Add(new GpuInfo(name, vendor, vendorId, deviceId, null));
    }

    private static bool TryReadPciId(string value, out uint id, out string name)
    {
        var match = PciIdAtEnd.Match(value);
        id = 0;
        name = match.Success ? value[..match.Index].Trim() : string.Empty;

        return match.Success && uint.TryParse(match.Groups[1].Value, NumberStyles.HexNumber,
                                              CultureInfo.InvariantCulture, out id);
    }

    private static GpuVendor GetVendor(uint vendorId) => vendorId switch
    {
        0x10DE => GpuVendor.Nvidia,
        0x1002 => GpuVendor.AMD,
        0x8086 => GpuVendor.Intel,
        _ => GpuVendor.Unknown
    };
}
