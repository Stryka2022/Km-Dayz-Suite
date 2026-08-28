using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Dzl.Core.Servers;

/// <summary>Finds the machine IPv4 address a local DayZ client should use to reach this server.</summary>
public static class ServerNetwork
{
    /// <summary>
    /// Prefer an active Ethernet/Wi-Fi adapter with a default gateway, then any active non-loopback
    /// adapter. APIPA, loopback and non-IPv4 addresses are ignored. Loopback is the safe fallback.
    /// </summary>
    public static string DetectConnectIp()
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                            && n.NetworkInterfaceType is not NetworkInterfaceType.Tunnel)
                .SelectMany(n =>
                {
                    var properties = n.GetIPProperties();
                    var hasGateway = properties.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.Any.Equals(g.Address));
                    var interfaceRank = n.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                        or NetworkInterfaceType.Wireless80211 ? 0 : 1;
                    return properties.UnicastAddresses.Select(u =>
                        new Candidate(u.Address.ToString(), hasGateway, interfaceRank));
                });

            return SelectBest(candidates) ?? IPAddress.Loopback.ToString();
        }
        catch
        {
            return IPAddress.Loopback.ToString();
        }
    }

    public sealed record Candidate(string Address, bool HasGateway, int InterfaceRank = 0);

    /// <summary>Pure selection surface kept public so address preference is regression tested.</summary>
    public static string? SelectBest(IEnumerable<Candidate> candidates) =>
        candidates
            .Select(c => (candidate: c, parsed: ParseUsableIpv4(c.Address)))
            .Where(x => x.parsed is not null)
            .OrderBy(x => x.candidate.HasGateway ? 0 : 1)
            .ThenBy(x => x.candidate.InterfaceRank)
            .ThenBy(x => IsPrivate(x.parsed!) ? 0 : 1)
            .Select(x => x.parsed!.ToString())
            .FirstOrDefault();

    private static IPAddress? ParseUsableIpv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || IPAddress.IsLoopback(address))
            return null;
        var bytes = address.GetAddressBytes();
        if (bytes[0] == 0 || bytes[0] == 169 && bytes[1] == 254 || bytes[0] >= 224) return null;
        return address;
    }

    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }
}
