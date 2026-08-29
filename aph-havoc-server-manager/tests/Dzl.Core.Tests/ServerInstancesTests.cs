using Dzl.Core.Servers;
using FluentAssertions;
public class ServerInstancesTests
{
    [Fact]
    public void Discover_lists_instances_with_serverdz_cfg()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var srvA = Path.Combine(root, "servers", "alpha"); Directory.CreateDirectory(srvA);
        File.WriteAllText(Path.Combine(srvA, "serverDZ.cfg"), "port = 2302;");
        Directory.CreateDirectory(Path.Combine(root, "servers", "nope"));

        var found = ServerInstances.Discover(root).Select(i => i.Name).ToList();
        found.Should().Contain("alpha");
        found.Should().NotContain("nope");
    }

    [Fact]
    public void Discover_missing_root_is_empty()
        => ServerInstances.Discover(@"X:\nope").Should().BeEmpty();

    [Theory]
    [InlineData(new int[0], 2302)]
    [InlineData(new[] { 2302 }, 2303)]
    [InlineData(new[] { 2302, 2303, 2305 }, 2304)]
    public void NextPort_picks_first_free_from_2302(int[] used, int expected)
        => ServerInstances.NextPort(used).Should().Be(expected);

    [Fact]
    public void RandomPort_stays_in_range_and_avoids_used_ports()
    {
        var used = Enumerable.Range(2302, 50).ToArray();
        var selected = ServerInstances.RandomPort(used, 2302, 2400);
        selected.Should().BeInRange(2302, 2400);
        used.Should().NotContain(selected);
    }

    [Theory]
    [InlineData(2302, false)]
    [InlineData(2305, false)]
    [InlineData(2299, false)]
    [InlineData(2402, true)]
    public void PortPairAvailable_reserves_game_and_query_ports(int candidate, bool expected)
        => ServerInstances.PortPairAvailable(candidate, new[] { 2302 }).Should().Be(expected);

    [Fact]
    public void RandomServerPort_avoids_all_existing_game_and_query_pairs()
    {
        var used = new[] { 2302, 2402, 2502 };
        var selected = ServerInstances.RandomServerPort(used, 2299, 2510);

        ServerInstances.PortPairAvailable(selected, used).Should().BeTrue();
        selected.Should().BeInRange(2299, 2510);
    }
}
