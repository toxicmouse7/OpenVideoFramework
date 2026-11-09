using Microsoft.Extensions.Logging;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtspSource;

/// <summary>
/// Connects to an RTSP video stream and outputs RTP packets for pipeline processing.
/// Supports multiple tracks and maintains RTCP statistics.
/// </summary>
public class RtspSource : IPipelineSource<RtpPacket>, IDisposable, IAsyncDisposable
{
    private readonly object _statisticsLock = new();
    private readonly RtspClient _rtspClient;
    private readonly RtspTransport _transport;
    private readonly RtcpStatisticsService _statisticsService = new();
    private readonly List<TrackReceiver> _trackReceivers = [];
    private readonly RtspSourceConfiguration _configuration;
    private ILogger<RtspSource> _logger = null!;

    public RtspSource(RtspSourceConfiguration configuration)
    {
        _configuration = configuration;
        _transport = configuration.Transport switch
        {
            RtspTransportType.Udp => new UdpRtspTransport(),
            RtspTransportType.Tcp => new TcpRtspTransport(),
            _ => throw new ArgumentOutOfRangeException(nameof(configuration.Transport))
        };
        _rtspClient = new RtspClient(configuration.Url, _transport);
    }

    public async Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<RtspSource>();

        await _rtspClient.ConnectAsync(cancellationToken);

        var metadata = await _rtspClient.DescribeAsync(cancellationToken);

        foreach (var trackMetadata in metadata)
        {
            if (_configuration.AllowedMediaType is not null &&
                trackMetadata.MediaType != _configuration.AllowedMediaType)
            {
                _logger.LogInformation("Track \"{track}\" skipped based on AllowedMediaType.", trackMetadata.Prefix);
                continue;
            }

            var trackReceiver = await _rtspClient.SetupAsync(trackMetadata, cancellationToken);
            _trackReceivers.Add(trackReceiver);

            _logger.LogInformation(
                "Track set up. Track: {track}. Media: {media}.",
                trackReceiver.Metadata.Prefix,
                trackReceiver.Metadata.MediaType);
        }
    }

    public async Task ProduceAsync(Emit<RtpPacket> emit, CancellationToken cancellationToken)
    {
        if (_trackReceivers.Count == 0)
        {
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? failure = null;
        async Task Supervise(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!cts.IsCancellationRequested)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }

                await cts.CancelAsync();
                _transport.Stop();
                throw;
            }
        }

        async Task PlayAndKeepAliveAsync()
        {
            await _rtspClient.PlayAsync(cts.Token).ConfigureAwait(false);
            _logger.LogInformation("Playing RTSP stream.");
            await _rtspClient.KeepAliveAsync(cts.Token).ConfigureAwait(false);
        }

        // Install media callbacks before PLAY: the response and first packet can arrive together.
        var tasks = new[]
        {
            Supervise(() => _transport.ReceiveAsync(
                (track, packet, token) => ProduceRtpPacketAsync(track, packet, emit, token),
                UpdateRtcpStatistics, cts.Token)),
            Supervise(PlayAndKeepAliveAsync)
        };
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            if (failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            throw;
        }
    }

    private void UpdateRtcpStatistics(RtcpPacket packet)
    {
        if (packet is RtcpSenderReport senderReport)
        {
            lock (_statisticsLock)
            {
                _statisticsService.UpdateStatistics(senderReport);
            }
        }
    }

    private async ValueTask ProduceRtpPacketAsync(
        TrackReceiver trackReceiver,
        RtpPacket rtpPacket,
        Emit<RtpPacket> emit,
        CancellationToken token)
    {
        lock (_statisticsLock)
        {
            _statisticsService.UpdateStatistics(rtpPacket, trackReceiver.Metadata.ClockRate, rtpPacket.ReceivedAt);
        }

        if (trackReceiver.ReportTimer.Elapsed > TimeSpan.FromSeconds(5))
        {
            RtcpReceiverReport? receiverReport;
            lock (_statisticsLock)
            {
                receiverReport = _statisticsService.GetReceiverReport(rtpPacket.Header.SSRC);
            }

            if (receiverReport is not null)
            {
                await _transport.SendReceiverReportAsync(trackReceiver, receiverReport, token).ConfigureAwait(false);
            }

            trackReceiver.ReportTimer.Restart();
        }

        await emit(rtpPacket, token).ConfigureAwait(false);
    }

    public void Dispose() => _transport.Dispose();

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
