using Microsoft.Extensions.Logging.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.FrameFilterUnit;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.Rtp;
using Xunit;

namespace OpenVideoFramework.Tests;

public class RtpTimingTests
{
    internal static PipelineContext Context => new("Timing tests", NullLoggerFactory.Instance);

    internal static RtpPacket Packet(uint timestamp, int trackId = 0, uint clockRate = 90000) => new()
    {
        TrackId = trackId,
        ClockRate = clockRate,
        Header = new RtpPacketHeader(false, false, 0, true, PayloadType.JPEG, 1, timestamp,
            (uint)trackId + 1, [], 0),
        Content = [0, 0, 0, 0, 1, 50, 8, 8, 0]
    };

    internal static RtcpSenderReport Report(uint rtpTimestamp, uint ssrc = 1, uint seconds = 3900000000) => new()
    {
        SSRC = ssrc,
        SenderInfo = new SenderInfo
        {
            RtpTimestamp = rtpTimestamp,
            NtpTimestampSeconds = seconds,
            NtpTimestampFractions = 0x80000000
        }
    };

    [Fact]
    public async Task NoSenderReportsStillProducesRelativePtsAcrossWrapAndReordering()
    {
        var assembler = new RtpFrameAssemblerUnit.RtpFrameAssemblerUnit();
        await assembler.PrepareForExecutionAsync(Context, default);
        var samples = new List<MediaSample<CompleteFrame>>();
        var origin = uint.MaxValue - 1799;
        foreach (var offset in new uint[] { 0, 7200, 3600, 10800 })
        {
            await assembler.ProcessAsync(Packet(unchecked(origin + offset)), (sample, _) =>
            {
                samples.Add(sample);
                return ValueTask.CompletedTask;
            }, default);
        }

        Assert.Equal(new[] { 0, 80, 40, 120 }, samples.Select(sample => (int)sample.Pts!.Value.TotalMilliseconds));
        Assert.All(samples, sample =>
        {
            Assert.Null(sample.WallClock);
            Assert.Equal(sample.Pts, sample.Dts);
        });
    }

    [Fact]
    public async Task LateSenderReportsAddWallClockWithoutRebasingTrackPts()
    {
        var statistics = new RtcpStatisticsService();
        var assembler = new RtpFrameAssemblerUnit.RtpFrameAssemblerUnit();
        await assembler.PrepareForExecutionAsync(Context, default);
        var samples = new List<MediaSample<CompleteFrame>>();
        async Task Push(RtpPacket packet)
        {
            statistics.UpdateStatistics(packet, packet.ClockRate, packet.ReceivedAt);
            packet.WallClock = statistics.GetWallClock(packet.Header.SSRC, packet.Header.Timestamp);
            await assembler.ProcessAsync(packet, (sample, _) =>
            {
                samples.Add(sample);
                return ValueTask.CompletedTask;
            }, default);
        }

        await Push(Packet(1000));
        await Push(Packet(900000, 1, 48000));
        statistics.UpdateStatistics(Report(1000));
        // The same NTP instant maps to audio PTS = 20 ms and video PTS = 0.
        statistics.UpdateStatistics(Report(900960, 2));
        await Push(Packet(4600));
        await Push(Packet(902880, 1, 48000));

        Assert.Null(samples[0].WallClock);
        Assert.Null(samples[1].WallClock);
        Assert.Equal(TimeSpan.FromMilliseconds(40), samples[2].Pts);
        Assert.Equal(TimeSpan.FromMilliseconds(60), samples[3].Pts);
        Assert.Equal(samples[2].WallClock, samples[3].WallClock);
        var expected = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .AddSeconds(3900000000).AddMilliseconds(540);
        Assert.Equal(expected, samples[2].WallClock);
    }

    [Fact]
    public void SenderReportBeforeMediaMapsAcrossWrapAndMultipleCycles()
    {
        var statistics = new RtcpStatisticsService();
        var origin = uint.MaxValue - 1799;
        statistics.UpdateStatistics(Report(origin));
        var first = Packet(unchecked(origin + 3600));
        statistics.UpdateStatistics(first, first.ClockRate, first.ReceivedAt);
        var time = statistics.GetWallClock(1, first.Header.Timestamp);
        Assert.NotNull(time);
        // Keep differences below half a timestamp cycle, even without further SRs.
        for (long offset = 1000000000; offset <= 5000000000; offset += 1000000000)
        {
            var packet = Packet(unchecked((uint)(origin + offset)));
            statistics.UpdateStatistics(packet, packet.ClockRate, packet.ReceivedAt);
        }

        var later = statistics.GetWallClock(1, unchecked((uint)(origin + 5000000000L)));
        Assert.InRange((later!.Value - time!.Value).TotalSeconds, (5000000000d - 3600) / 90000 - 0.000001,
            (5000000000d - 3600) / 90000 + 0.000001);
        Assert.Null(statistics.GetWallClock(2, 1));
    }

    [Fact]
    public async Task FilteringPreservesEveryMetadataField()
    {
        var frame = new EncodedVideoFrame
        {
            Codec = Codec.H264,
            Width = 64,
            Height = 64,
            Data = new byte[] { 1 },
            IsKeyFrame = true
        };
        var input = new MediaSample<CompleteFrame>(frame, 7, null, TimeSpan.FromMilliseconds(-80),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var filter = new FrameFilterUnit<EncodedVideoFrame>();
        MediaSample<EncodedVideoFrame>? output = null;
        await filter.ProcessAsync(input, (sample, _) =>
        {
            output = sample;
            return ValueTask.CompletedTask;
        }, default);
        Assert.NotNull(output);
        Assert.Same(frame, output.Frame);
        Assert.Equal(input.TrackId, output.TrackId);
        Assert.Equal(input.Pts, output.Pts);
        Assert.Equal(input.Dts, output.Dts);
        Assert.Equal(input.WallClock, output.WallClock);
    }
}
