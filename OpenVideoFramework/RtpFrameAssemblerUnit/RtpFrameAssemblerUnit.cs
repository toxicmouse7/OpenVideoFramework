using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers;
using OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.AC3;
using OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.H264;
using OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.Jpeg;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtpFrameAssemblerUnit;

/// <summary>
/// Assembles individual RTP packets into <see cref="CompleteFrame"/>.
/// </summary>
public class RtpFrameAssemblerUnit : IPipelineUnit<RtpPacket, MediaSample<CompleteFrame>>
{
    private readonly Dictionary<int, TrackAssembly> _tracks = [];
    private PipelineContext _context = null!;

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return Task.CompletedTask;
    }

    public async ValueTask ProcessAsync(
        RtpPacket packet,
        Emit<MediaSample<CompleteFrame>> emit,
        CancellationToken cancellationToken)
    {
        if (packet.ClockRate == 0)
        {
            throw new ArgumentException("RTP clock rate must be positive.", nameof(packet));
        }

        if (!_tracks.TryGetValue(packet.TrackId, out var track))
        {
            track = new TrackAssembly(CreateAssembler(packet), packet);
            _tracks.Add(packet.TrackId, track);
        }

        var pts = track.GetPts(packet);
        var frame = track.Assembler.AddPacket(packet);

        if (frame is null)
        {
            return;
        }

        await emit(new MediaSample<CompleteFrame>(frame, packet.TrackId, pts,
            frame is EncodedVideoFrame { Codec: Codec.H264 } ? null : pts, packet.WallClock), cancellationToken);
    }

    private sealed class TrackAssembly(RtpFrameAssembler assembler, RtpPacket first)
    {
        private long _timestamp = first.Header.Timestamp;
        private readonly long _origin = first.Header.Timestamp;
        private readonly uint _ssrc = first.Header.SSRC;
        private readonly uint _clockRate = first.ClockRate;
        public RtpFrameAssembler Assembler { get; } = assembler;

        public TimeSpan GetPts(RtpPacket packet)
        {
            if (packet.Header.SSRC != _ssrc || packet.ClockRate != _clockRate)
            {
                throw new NotSupportedException("Changing an RTP track's SSRC or clock rate requires a new pipeline.");
            }

            // Signed modular differences allow both wraparound and presentation reordering.
            var timestamp = _timestamp + unchecked((int)(packet.Header.Timestamp - (uint)_timestamp));
            _timestamp = Math.Max(_timestamp, timestamp);
            var ticks = checked((long)Math.Round(
                (timestamp - _origin) * (decimal)TimeSpan.TicksPerSecond / _clockRate));
            return TimeSpan.FromTicks(ticks);
        }
    }

    private RtpFrameAssembler CreateAssembler(RtpPacket packet)
    {
        return packet.Header.PayloadType switch
        {
            PayloadType.JPEG => new JpegRtpFrameAssembler(_context.GetLogger<JpegRtpFrameAssembler>()),
            PayloadType.H264 => new H264RtpFrameAssembler(
                _context.GetLogger<H264RtpFrameAssembler>(), packet.H264ParameterSets),
            PayloadType.AC3 => new Aс3RtpFrameAssembler(_context.GetLogger<Aс3RtpFrameAssembler>()),
            _ => throw new NotSupportedException($"Payload type {packet.Header.PayloadType} is not supported.")
        };
    }
}
