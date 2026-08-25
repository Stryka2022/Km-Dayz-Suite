using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dzl.Core.Remote;

/// <summary>
/// Built-in BattlEye RCon v2 client. BattlEye uses a CRC-protected UDP protocol rather than
/// FTP; this client therefore owns an independent connection while sharing the saved server
/// profile and DPAPI credential store with the FTP workspace.
/// </summary>
public sealed class BattlEyeRconClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<byte, PendingCommand> _pending = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _sequenceLock = new();
    private UdpClient? _udp;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveLoop;
    private Task? _keepAliveLoop;
    private byte _sequence;
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public event EventHandler<string>? ServerMessageReceived;

    public async Task ConnectAsync(string host, int port, string password, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Enter the RCon host or IP address.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "RCon port must be from 1 to 65535.");
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Enter the BattlEye RCon password.", nameof(password));
        EnsureAscii(password, "RCon password");

        await DisconnectAsync();
        var addresses = await Dns.GetHostAddressesAsync(host.Trim(), cancellationToken);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ??
                      addresses.FirstOrDefault() ?? throw new SocketException((int)SocketError.HostNotFound);
        var udp = new UdpClient(address.AddressFamily);
        udp.Connect(new IPEndPoint(address, port));

        try
        {
            await udp.SendAsync(BuildPacket(BuildLoginPayload(password)), cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            while (true)
            {
                var result = await udp.ReceiveAsync(timeout.Token);
                if (!TryParsePacket(result.Buffer, out var payload) || payload.Length < 2 || payload[0] != 0x00)
                    continue;
                if (payload[1] != 0x01) throw new UnauthorizedAccessException("BattlEye rejected the RCon password.");
                break;
            }

            _udp = udp;
            _sequence = 0;
            IsConnected = true;
            _lifetime = new CancellationTokenSource();
            _receiveLoop = ReceiveLoopAsync(_lifetime.Token);
            _keepAliveLoop = KeepAliveLoopAsync(_lifetime.Token);
        }
        catch
        {
            udp.Dispose();
            throw;
        }
    }

    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsConnected || _udp is null) throw new InvalidOperationException("Connect to BattlEye RCon first.");
        command = command.Trim();
        if (command.Length == 0) throw new ArgumentException("Enter an RCon command.", nameof(command));
        EnsureAscii(command, "RCon command");

        var sequence = NextSequence();
        var pending = new PendingCommand();
        if (!_pending.TryAdd(sequence, pending)) throw new InvalidOperationException("RCon sequence is already in use.");
        try
        {
            await SendAsync(BuildCommandPayload(sequence, command), cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var registration = timeout.Token.Register(() => pending.Cancel(timeout.Token));
            return await pending.Task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("BattlEye RCon did not answer within 10 seconds.");
        }
        finally { _pending.TryRemove(sequence, out _); }
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        var lifetime = _lifetime;
        var udp = _udp;
        var receive = _receiveLoop;
        var keepAlive = _keepAliveLoop;
        _lifetime = null;
        _udp = null;
        _receiveLoop = null;
        _keepAliveLoop = null;
        lifetime?.Cancel();
        udp?.Dispose();
        foreach (var pending in _pending.Values) pending.Cancel(CancellationToken.None);
        _pending.Clear();
        foreach (var task in new[] { receive, keepAlive })
            if (task is not null)
                try { await task; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        lifetime?.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _udp is { } udp)
            {
                var result = await udp.ReceiveAsync(cancellationToken);
                if (!TryParsePacket(result.Buffer, out var payload) || payload.Length < 2) continue;
                switch (payload[0])
                {
                    case 0x01:
                        HandleCommandResponse(payload);
                        break;
                    case 0x02:
                        await HandleServerMessageAsync(payload, cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException ex) when (cancellationToken.IsCancellationRequested || ex.SocketErrorCode == SocketError.OperationAborted) { }
        catch (Exception ex)
        {
            IsConnected = false;
            foreach (var pending in _pending.Values) pending.Fail(ex);
            ServerMessageReceived?.Invoke(this, "RCon connection closed: " + ex.Message);
        }
    }

    private async Task HandleServerMessageAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var sequence = payload[1];
        var text = Encoding.ASCII.GetString(payload, 2, payload.Length - 2);
        await SendAsync(new byte[] { 0x02, sequence }, cancellationToken);
        if (text.Length > 0) ServerMessageReceived?.Invoke(this, text);
    }

    private void HandleCommandResponse(byte[] payload)
    {
        var sequence = payload[1];
        if (!_pending.TryGetValue(sequence, out var pending)) return;
        var body = payload.AsSpan(2);
        if (body.Length >= 3 && body[0] == 0x00 && body[1] > 0 && body[2] < body[1])
            pending.AddPart(body[1], body[2], Encoding.ASCII.GetString(body[3..]));
        else
            pending.Complete(Encoding.ASCII.GetString(body));
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await SendAsync(BuildCommandPayload(NextSequence(), ""), cancellationToken);
    }

    private async Task SendAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var udp = _udp ?? throw new InvalidOperationException("RCon is not connected.");
        var packet = BuildPacket(payload);
        await _sendGate.WaitAsync(cancellationToken);
        try { await udp.SendAsync(packet, cancellationToken); }
        finally { _sendGate.Release(); }
    }

    private byte NextSequence()
    {
        lock (_sequenceLock) return _sequence++;
    }

    internal static byte[] BuildLoginPayload(string password) =>
        new[] { (byte)0x00 }.Concat(Encoding.ASCII.GetBytes(password)).ToArray();

    internal static byte[] BuildCommandPayload(byte sequence, string command) =>
        new[] { (byte)0x01, sequence }.Concat(Encoding.ASCII.GetBytes(command)).ToArray();

    internal static byte[] BuildPacket(byte[] payload)
    {
        var packet = new byte[7 + payload.Length];
        packet[0] = 0x42;
        packet[1] = 0x45;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2, 4), ComputeCrc32(payload));
        packet[6] = 0xFF;
        payload.CopyTo(packet, 7);
        return packet;
    }

    internal static bool TryParsePacket(byte[] packet, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (packet.Length < 8 || packet[0] != 0x42 || packet[1] != 0x45 || packet[6] != 0xFF) return false;
        payload = packet[7..];
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(2, 4));
        if (ComputeCrc32(payload) == expected) return true;
        payload = Array.Empty<byte>();
        return false;
    }

    internal static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    private static void EnsureAscii(string value, string field)
    {
        if (value.Any(ch => ch > 0x7F)) throw new ArgumentException(field + " must contain ASCII characters only.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync();
        _sendGate.Dispose();
    }

    private sealed class PendingCommand
    {
        private readonly TaskCompletionSource<string> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _partsLock = new();
        private string?[]? _parts;

        public Task<string> Task => _completion.Task;
        public void Complete(string value) => _completion.TrySetResult(value);
        public void Cancel(CancellationToken token) => _completion.TrySetCanceled(token);
        public void Fail(Exception error) => _completion.TrySetException(error);

        public void AddPart(int count, int index, string value)
        {
            lock (_partsLock)
            {
                _parts ??= new string?[count];
                if (_parts.Length != count || index < 0 || index >= count) return;
                _parts[index] = value;
                if (_parts.All(p => p is not null)) _completion.TrySetResult(string.Concat(_parts));
            }
        }
    }
}
