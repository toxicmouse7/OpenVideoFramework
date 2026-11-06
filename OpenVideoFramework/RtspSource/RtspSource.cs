using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtspSource;

/// <summary>
/// Connects to an RTSP video stream and outputs RTP packets for pipeline processing.
/// Supports multiple tracks and maintains stream synchronization and statistics.
/// </summary>
public class RtspSource : IPipelineSource<RtpPacket>, IDisposable
{
    private readonly object _statisticsLock = new();
    private readonly RtspClient _rtspClient;
    private readonly RtcpStatisticsService _statisticsService = new();
    private readonly RtpTimestampSynchronizer _timestampSynchronizer = new();
    private readonly List<TrackReceiver> _trackReceivers = [];
    private readonly RtspSourceConfiguration _configuration;
    private ILogger<RtspSource> _logger = null!;

    public RtspSource(RtspSourceConfiguration configuration)
    {
        _configuration = configuration;
        _rtspClient = new RtspClient(configuration.Url);
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
        await _rtspClient.PlayAsync(cancellationToken);
        _logger.LogInformation("Playing RTSP stream.");
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
                if (ex is not OperationCanceledException || !cts.IsCancellationRequested)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }

                await cts.CancelAsync();
                throw;
            }
        }

        var tasks = new List<Task>();
        tasks.Add(Supervise(() => _rtspClient.KeepAliveAsync(cts.Token)));
        foreach (var receiver in _trackReceivers)
        {
            tasks.Add(Supervise(() => ProduceRtpPackets(receiver, emit, cts.Token)));
            tasks.Add(Supervise(() => CalculateRtcpStatistics(receiver, cts.Token)));
        }
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

    private async Task CalculateRtcpStatistics(TrackReceiver trackReceiver, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var rtcpPacket = await trackReceiver.RtcpClient.GetPacketAsync(token);

            if (rtcpPacket is RtcpSenderReport senderReport)
            {
                lock (_statisticsLock)
                {
                    _timestampSynchronizer.UpdateFromSenderReport(senderReport);
                    _statisticsService.UpdateStatistics(senderReport);
                }
            }
        }
    }

    private async Task ProduceRtpPackets(
        TrackReceiver trackReceiver,
        Emit<RtpPacket> emit,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!token.IsCancellationRequested)
        {
            var rtpPacket = await trackReceiver.RtpClient.GetPacketAsync(token);

            lock (_statisticsLock)
            {
                _statisticsService.UpdateStatistics(rtpPacket, trackReceiver.Metadata.ClockRate, rtpPacket.ReceivedAt);
            }

            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                RtcpReceiverReport? receiverReport;
                lock (_statisticsLock)
                {
                    receiverReport = _statisticsService.GetReceiverReport(rtpPacket.Header.SSRC);
                }

                if (receiverReport is not null)
                {
                    await trackReceiver.RtcpClient.SendReceiverReportAsync(receiverReport, token);
                }

                stopwatch.Restart();
            }

            DateTimeOffset? timestamp;
            lock (_statisticsLock)
            {
                timestamp = _timestampSynchronizer.ConvertRtpTimestampToUtc(
                    rtpPacket.Header.SSRC,
                    rtpPacket.Header.Timestamp,
                    trackReceiver.Metadata.ClockRate);
            }

            if (timestamp is not null)
            {
                rtpPacket.Stamp(timestamp.Value);
            }

            await emit(rtpPacket, token);
        }
    }

    public void Dispose()
    {
        foreach (var receiver in _trackReceivers)
        {
            receiver.RtpClient.Dispose();
            receiver.RtcpClient.Dispose();
        }
        _rtspClient.Dispose();
    }
}
