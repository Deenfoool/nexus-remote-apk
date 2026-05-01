using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NexusRemotePC;

public static class NetworkUtil
{
    public static IReadOnlyList<string> GetLocalIPv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .Where(IsPrivateIPv4)
            .Distinct()
            .ToArray();
    }

    public static string GetPrimaryIPv4Address()
    {
        return GetLocalIPv4Addresses().FirstOrDefault() ?? "127.0.0.1";
    }

    public static bool IsAllowedClient(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        return IsPrivateIPv4(address.ToString());
    }

    public static bool IsPrivateIPv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address)) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
               || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
               || bytes[0] == 192 && bytes[1] == 168
               || bytes[0] == 169 && bytes[1] == 254;
    }
}
