using System.Net;
using System.Net.Sockets;
using System.Text;
using Dzl.Core.Remote;
using FluentAssertions;

namespace Dzl.Core.Tests;

public class BattlEyeRconTests
{
    [Fact]
    public void Packet_round_trip_validates_crc_and_payload()
    {
        var payload = BattlEyeRconClient.BuildCommandPayload(7, "players");
        var packet = BattlEyeRconClient.BuildPacket(payload);

        packet.Take(2).Should().Equal(0x42, 0x45);
        packet[6].Should().Be(0xFF);
        BattlEyeRconClient.TryParsePacket(packet, out var parsed).Should().BeTrue();
        parsed.Should().Equal(payload);

        packet[^1] ^= 0x01;
        BattlEyeRconClient.TryParsePacket(packet, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Client_logs_in_and_reassembles_multi_packet_command_response()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var serverTask = Task.Run(async () =>
        {
            var login = await server.ReceiveAsync(timeout.Token);
            BattlEyeRconClient.TryParsePacket(login.Buffer, out var loginPayload).Should().BeTrue();
            loginPayload[0].Should().Be(0x00);
            Encoding.ASCII.GetString(loginPayload, 1, loginPayload.Length - 1).Should().Be("test-secret");
            await server.SendAsync(BattlEyeRconClient.BuildPacket(new byte[] { 0x00, 0x01 }), login.RemoteEndPoint, timeout.Token);

            var command = await server.ReceiveAsync(timeout.Token);
            BattlEyeRconClient.TryParsePacket(command.Buffer, out var commandPayload).Should().BeTrue();
            commandPayload[0].Should().Be(0x01);
            var sequence = commandPayload[1];
            Encoding.ASCII.GetString(commandPayload, 2, commandPayload.Length - 2).Should().Be("players");

            var second = new byte[] { 0x01, sequence, 0x00, 0x02, 0x01 }
                .Concat(Encoding.ASCII.GetBytes("online")).ToArray();
            var first = new byte[] { 0x01, sequence, 0x00, 0x02, 0x00 }
                .Concat(Encoding.ASCII.GetBytes("2 players ")).ToArray();
            await server.SendAsync(BattlEyeRconClient.BuildPacket(second), command.RemoteEndPoint, timeout.Token);
            await server.SendAsync(BattlEyeRconClient.BuildPacket(first), command.RemoteEndPoint, timeout.Token);
        }, timeout.Token);

        await using var client = new BattlEyeRconClient();
        await client.ConnectAsync("127.0.0.1", port, "test-secret", timeout.Token);
        var response = await client.ExecuteAsync("players", timeout.Token);

        response.Should().Be("2 players online");
        await serverTask;
    }

    [Fact]
    public async Task Client_rejects_failed_login()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = Task.Run(async () =>
        {
            var login = await server.ReceiveAsync(timeout.Token);
            await server.SendAsync(BattlEyeRconClient.BuildPacket(new byte[] { 0x00, 0x00 }), login.RemoteEndPoint, timeout.Token);
        }, timeout.Token);

        await using var client = new BattlEyeRconClient();
        var action = async () => await client.ConnectAsync("127.0.0.1", port, "wrong", timeout.Token);
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        await serverTask;
    }
}
