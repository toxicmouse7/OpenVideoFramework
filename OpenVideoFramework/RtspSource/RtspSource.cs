using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtspSource;

public class RtspSource : IPipelineSource<RtpPacket>
{
    private readonly RtspClient _rtspClient;
    private readonly RtcpStatisticsService _statisticsService = new();
    private readonly RtpTimestampSynchronizer _timestampSynchronizer = new();
    private readonly List<TrackReceiver> _trackReceivers = [];
    private readonly RtspSourceConfiguration _configuration;
    private ILogger<RtspSource> _logger;

    public RtspSource(RtspSourceConfiguration configuration)
    {
        _configuration = configuration;
        _rtspClient = new RtspClient(configuration.Url);
    }
    
    public async Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<RtspSource>();
        
        await _rtspClient.ConnectAsync(cancellationToken);

        var metadata = await _rtspClient.DescribeAsync();

        foreach (var trackMetadata in metadata)
        {
            if (_configuration.AllowedMediaType is not null &&
                trackMetadata.MediaType != _configuration.AllowedMediaType)
            {
                _logger.LogInformation("Track \"{track}\" skipped based on AllowedMediaType.", trackMetadata.Prefix);
                continue;
            }
            
            var trackReceiver = await _rtspClient.SetupAsync(trackMetadata);
            _trackReceivers.Add(trackReceiver);
            
            _logger.LogInformation(
                "Track set up. Track: {track}. Media: {media}.",
                trackReceiver.Metadata.Prefix, trackReceiver.Metadata.MediaType);
        }
    }

    public async Task ProduceAsync(ChannelWriter<RtpPacket> output, CancellationToken cancellationToken)
    {
        await _rtspClient.PlayAsync(cancellationToken);
        _logger.LogInformation("Playing RTSP stream.");
        
        var producingTasks = _trackReceivers.Select(r => ProduceRtpPackets(r, output, cancellationToken));
        var calculatingTasks = _trackReceivers.Select(r => CalculateRtcpStatistics(r, cancellationToken));
        
        var allTasks = producingTasks.Concat(calculatingTasks);
        
        await Task.WhenAll(allTasks);
    }

    private async Task CalculateRtcpStatistics(TrackReceiver trackReceiver, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var rtcpPacket = await trackReceiver.RtcpClient.GetPacketAsync(token);

            if (rtcpPacket is RtcpSenderReport senderReport)
            {
                _timestampSynchronizer.UpdateFromSenderReport(senderReport);
                _statisticsService.UpdateStatistics(senderReport);
            }
        }
    }

    private async Task ProduceRtpPackets(
        TrackReceiver trackReceiver,
        ChannelWriter<RtpPacket> output,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        
        while (!token.IsCancellationRequested)
        {
            var rtpPacket = await trackReceiver.RtpClient.GetPacketAsync(token);
            
            _statisticsService.UpdateStatistics(rtpPacket, trackReceiver.Metadata.ClockRate, rtpPacket.ReceivedAt);

            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
            {
                stopwatch.Reset();
                _ = Task.Run(async () =>
                {
                    var receiverReport = _statisticsService.GetReceiverReport(rtpPacket.Header.SSRC);
                    
                    if (receiverReport is not null)
                    {
                        await trackReceiver.RtcpClient.SendReceiverReportAsync(receiverReport);
                    }
                    
                    stopwatch.Start();
                }, token);
            }

            var timestamp = _timestampSynchronizer.ConvertRtpTimestampToUtc(
                rtpPacket.Header.SSRC,
                rtpPacket.Header.Timestamp,
                trackReceiver.Metadata.ClockRate);

            if (timestamp is not null)
            {
                rtpPacket.Stamp(timestamp.Value);
            }
            
            await output.WriteAsync(rtpPacket, token);
        }
    }
}