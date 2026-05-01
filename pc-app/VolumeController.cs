using NAudio.CoreAudioApi;

namespace NexusRemotePC;

public static class VolumeController
{
    public static int? TryGetVolume()
    {
        try
        {
            using var device = GetDefaultDevice();
            return (int)Math.Clamp(Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), 0, 100);
        }
        catch
        {
            return null;
        }
    }

    public static void SetVolume(int value)
    {
        value = Math.Clamp(value, 0, 100);
        using var device = GetDefaultDevice();
        device.AudioEndpointVolume.MasterVolumeLevelScalar = value / 100f;
    }

    public static void ChangeVolume(int delta)
    {
        var current = TryGetVolume() ?? 50;
        SetVolume(current + delta);
    }

    public static void ToggleMute()
    {
        using var device = GetDefaultDevice();
        device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
    }

    private static MMDevice GetDefaultDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }
}
