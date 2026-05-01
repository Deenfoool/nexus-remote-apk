using LibreHardwareMonitor.Hardware;

namespace NexusRemotePC;

public static class HardwareSensors
{
    private static readonly object Sync = new();
    private static readonly Computer Computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsControllerEnabled = true
    };

    private static bool _opened;
    private static DateTime _lastReadAt;
    private static HardwareSnapshot _cached = new(null, null, null);

    public static HardwareSnapshot Read()
    {
        lock (Sync)
        {
            if (DateTime.UtcNow - _lastReadAt < TimeSpan.FromSeconds(2))
            {
                return _cached;
            }

            try
            {
                if (!_opened)
                {
                    Computer.Open();
                    _opened = true;
                }

                var gpuLoads = new List<float>();
                var fans = new List<float>();
                var storageTemps = new List<float>();

                foreach (var hardware in Computer.Hardware)
                {
                    hardware.Update();
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                    }

                    CollectHardware(hardware, gpuLoads, fans, storageTemps);
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        CollectHardware(subHardware, gpuLoads, fans, storageTemps);
                    }
                }

                _cached = new HardwareSnapshot(
                    GpuLoad: RoundNullable(gpuLoads.Where(value => value >= 0 && value <= 100).DefaultIfEmpty(float.NaN).Max()),
                    FanRpm: RoundNullable(fans.Where(value => value > 0).DefaultIfEmpty(float.NaN).Max()),
                    StorageTemperature: RoundNullable(storageTemps.Where(value => value > 0).DefaultIfEmpty(float.NaN).Max()));
                _lastReadAt = DateTime.UtcNow;
                return _cached;
            }
            catch
            {
                return _cached;
            }
        }
    }

    private static void CollectHardware(IHardware hardware, List<float> gpuLoads, List<float> fans, List<float> storageTemps)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value == null) continue;

            if (sensor.SensorType == SensorType.Load &&
                (hardware.HardwareType == HardwareType.GpuAmd ||
                 hardware.HardwareType == HardwareType.GpuIntel ||
                 hardware.HardwareType == HardwareType.GpuNvidia) &&
                sensor.Name.Contains("core", StringComparison.OrdinalIgnoreCase))
            {
                gpuLoads.Add(sensor.Value.Value);
            }

            if (sensor.SensorType == SensorType.Fan)
            {
                fans.Add(sensor.Value.Value);
            }

            if (sensor.SensorType == SensorType.Temperature && hardware.HardwareType == HardwareType.Storage)
            {
                storageTemps.Add(sensor.Value.Value);
            }
        }
    }

    private static int? RoundNullable(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? null : (int)Math.Clamp(Math.Round(value), 0, 10000);
    }
}

public sealed record HardwareSnapshot(int? GpuLoad, int? FanRpm, int? StorageTemperature);
