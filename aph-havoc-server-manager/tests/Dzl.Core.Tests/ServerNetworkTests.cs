using Dzl.Core.Servers;
using FluentAssertions;

namespace Dzl.Core.Tests;

public class ServerNetworkTests
{
    [Fact]
    public void SelectBest_prefers_a_gateway_lan_address()
    {
        var candidates = new[]
        {
            new ServerNetwork.Candidate("10.10.0.8", HasGateway: false),
            new ServerNetwork.Candidate("192.168.0.154", HasGateway: true),
            new ServerNetwork.Candidate("203.0.113.4", HasGateway: true, InterfaceRank: 1),
        };

        ServerNetwork.SelectBest(candidates).Should().Be("192.168.0.154");
    }

    [Fact]
    public void SelectBest_ignores_loopback_apipa_ipv6_and_multicast()
    {
        var candidates = new[]
        {
            new ServerNetwork.Candidate("127.0.0.1", true),
            new ServerNetwork.Candidate("169.254.1.2", true),
            new ServerNetwork.Candidate("fe80::1", true),
            new ServerNetwork.Candidate("239.1.2.3", true),
        };

        ServerNetwork.SelectBest(candidates).Should().BeNull();
    }
}
