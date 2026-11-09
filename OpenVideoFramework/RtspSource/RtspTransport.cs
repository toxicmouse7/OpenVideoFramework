using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.Rtp;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource;

internal abstract class RtspTransport : IDisposable, IAsyncDisposable
{
    private readonly TcpClient _client = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<int, TaskCompletionSource<RtspResponse>> _pending = [];
    private CancellationTokenRegistration _connectionCancellation;
    private Task _reader = Task.CompletedTask;
    private Exception? _readerFailure;
    private int _stopped;
    private int _disposed;
    private Func<TrackReceiver, RtpPacket, CancellationToken, ValueTask>? _onRtp;
    private Action<RtcpPacket>? _onRtcp;

    protected IPAddress ServerAddress
    {
        get
        {
            var address = ((IPEndPoint)_client.Client.RemoteEndPoint!).Address;
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }
    }

    public async Task ConnectAsync(Uri url, CancellationToken token)
    {
        await _client.ConnectAsync(url.Host, url.Port, token).ConfigureAwait(false);
        _connectionCancellation = token.Register(Stop);
        _reader = ReadMessagesAsync(_shutdown.Token);
    }

    public async Task<RtspResponse> SendRequestAsync(string request, int sequence, CancellationToken token)
    {
        var completion = new TaskCompletionSource<RtspResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending)
        {
            if (_readerFailure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_readerFailure).Throw();
            }

            _pending.Add(sequence, completion);
        }

        try
        {
            await WriteAsync(Encoding.ASCII.GetBytes(request), token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            lock (_pending)
            { _pending.Remove(sequence); }
        }
    }

    protected async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken token)
    {
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await _client.GetStream().WriteAsync(data, token).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    public abstract TrackReceiver CreateTrack(TrackMetadata metadata);
    public abstract string GetTransportHeader(TrackReceiver track);
    public abstract void ConfigureTrack(TrackReceiver track, string transport);
    public abstract ValueTask SendReceiverReportAsync(TrackReceiver track, RtcpReceiverReport report, CancellationToken token);

    public async Task ReceiveAsync(
        Func<TrackReceiver, RtpPacket, CancellationToken, ValueTask> onRtp,
        Action<RtcpPacket> onRtcp,
        CancellationToken token)
    {
        _onRtp = onRtp;
        _onRtcp = onRtcp;
        using var cancellation = token.Register(Stop);
        Exception? failure = null;

        async Task Supervise(Task task)
        {
            try
            { await task.ConfigureAwait(false); }
            catch (Exception ex)
            {
                if (!_shutdown.IsCancellationRequested)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }

                Stop();
                throw;
            }
        }

        var tasks = new List<Task> { Supervise(_reader) };
        tasks.AddRange(ReceiveMedia(_shutdown.Token).Select(Supervise));
        try
        { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch
        {
            if (failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            token.ThrowIfCancellationRequested();
            throw;
        }
    }

    protected virtual IEnumerable<Task> ReceiveMedia(CancellationToken token) => [];

    protected ValueTask ReceiveRtpAsync(TrackReceiver track, byte[] data, CancellationToken token)
    {
        if (_onRtp is null)
        {
            throw new InvalidDataException("Received RTP before starting media reception.");
        }

        var packet = RtpSerializer.DeserializeRtpPacket(data, track.Metadata.PayloadType, track.H264ParameterSets);
        return _onRtp(track, packet, token);
    }

    protected void ReceiveRtcp(byte[] data)
    {
        var offset = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 4 || data[offset] >> 6 != 2)
            {
                throw new InvalidDataException("Invalid RTCP packet header.");
            }

            var length = (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2)) + 1) * 4;
            if (length > data.Length - offset)
            {
                throw new InvalidDataException("Incomplete RTCP packet.");
            }

            if (data[offset + 1] == (byte)RtcpPacketType.SenderReport)
            {
                var reportCount = data[offset] & 0x1F;
                if (length < 28 + reportCount * 24)
                {
                    throw new InvalidDataException("Incomplete RTCP sender report.");
                }

                _onRtcp?.Invoke(RtcpSerializer.DeserializeRtcpPacket(data[offset..(offset + length)]));
            }

            offset += length;
        }
    }

    protected virtual ValueTask ReceiveInterleavedAsync(byte channel, byte[] data, CancellationToken token) =>
        ValueTask.FromException(new InvalidDataException("The server sent interleaved data for a UDP session."));

    private async Task ReadMessagesAsync(CancellationToken token)
    {
        try
        {
            using var input = new BufferedStream(_client.GetStream(), 8192);
            var first = new byte[1];
            while (true)
            {
                await input.ReadExactlyAsync(first, token).ConfigureAwait(false);
                if (first[0] == '$')
                {
                    await ReadInterleavedAsync(input, token).ConfigureAwait(false);
                    continue;
                }

                var response = await ReadResponseAsync(input, first[0], token).ConfigureAwait(false);
                if (!response.Headers.TryGetValue("CSeq", out var cseq) || !int.TryParse(cseq, out var sequence))
                {
                    throw new InvalidDataException("RTSP response has no valid CSeq.");
                }

                lock (_pending)
                {
                    if (_pending.TryGetValue(sequence, out var completion))
                    {
                        completion.TrySetResult(response);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var failure = token.IsCancellationRequested ? new OperationCanceledException(token) : ex;
            lock (_pending)
            {
                _readerFailure = failure;
                foreach (var completion in _pending.Values)
                {
                    if (token.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(token);
                    }
                    else
                    {
                        completion.TrySetException(failure);
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            throw;
        }
    }

    private async ValueTask ReadInterleavedAsync(Stream input, CancellationToken token)
    {
        var header = new byte[3];
        await input.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var data = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1))];
        await input.ReadExactlyAsync(data, token).ConfigureAwait(false);
        await ReceiveInterleavedAsync(header[0], data, token).ConfigureAwait(false);
    }

    private static async Task<RtspResponse> ReadResponseAsync(Stream input, byte first, CancellationToken token)
    {
        var header = new List<byte> { first };
        var next = new byte[1];
        while (header.Count < 4 || header[^4] != '\r' || header[^3] != '\n' || header[^2] != '\r' || header[^1] != '\n')
        {
            if (header.Count >= 65536)
            {
                throw new InvalidDataException("RTSP response headers are too large.");
            }

            await input.ReadExactlyAsync(next, token).ConfigureAwait(false);
            header.Add(next[0]);
        }

        var response = ParseResponseHeaders(Encoding.ASCII.GetString([.. header]));
        if (response.Headers.TryGetValue("Content-Length", out var contentLength))
        {
            if (!int.TryParse(contentLength, out var length) || length < 0 || length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("Invalid RTSP response body length.");
            }

            var body = new byte[length];
            await input.ReadExactlyAsync(body, token).ConfigureAwait(false);
            response.Body = Encoding.UTF8.GetString(body);
        }

        return response;
    }

    private static RtspResponse ParseResponseHeaders(string header)
    {
        var lines = header.Split("\r\n", StringSplitOptions.None);
        var status = lines[0].Split(' ', 3);
        if (status.Length < 2 || status[0] != "RTSP/1.0" || !int.TryParse(status[1], out var code))
        {
            throw new InvalidDataException("Invalid RTSP response status line.");
        }

        var response = new RtspResponse { StatusCode = code };
        foreach (var line in lines.Skip(1).Where(line => line.Length != 0))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                throw new InvalidDataException("Invalid RTSP response header.");
            }

            response.Headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        return response;
    }

    protected static string GetTransportParameter(string transport, string name)
    {
        var prefix = name + "=";
        var parameter = transport.Split(';', StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return parameter is not null ? parameter[prefix.Length..] :
            throw new InvalidDataException($"RTSP Transport header has no {name} parameter.");
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        { return; }
        _shutdown.Cancel();
        _client.Dispose();
    }

    public virtual void Dispose() => Stop();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        { return; }
        Dispose();
        try
        { await _reader.ConfigureAwait(false); }
        catch { /* Reader failures are propagated through requests and ReceiveAsync. */ }
        _connectionCancellation.Dispose();
        _shutdown.Dispose();
        _writeGate.Dispose();
    }
}
