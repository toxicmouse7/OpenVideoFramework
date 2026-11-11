using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using OpenVideoFramework.ChannelSource;
using OpenVideoFramework.Common;
using OpenVideoFramework.FrameFilterUnit;
using OpenVideoFramework.MediaMuxer;
using OpenVideoFramework.Pipelines.Builder;
using OpenVideoFramework.VideoTranscoderUnit;
using Xunit;

namespace OpenVideoFramework.Tests;

[Trait("Category", "FFmpeg")]
public sealed class MediaMuxerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ovf-muxer-").FullName;
    private readonly DateTimeOffset _epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private string OutputPath => Path.Combine(_directory, "output.mkv");

    [Theory]
    [InlineData(MediaSynchronizationMode.SharedTimeline, true)]
    [InlineData(MediaSynchronizationMode.WallClock, false)]
    [InlineData(MediaSynchronizationMode.SharedTimeline, false, true)]
    [InlineData(MediaSynchronizationMode.SharedTimeline, true, false, Codec.MJPEG)]
    [InlineData(MediaSynchronizationMode.WallClock, false, false, Codec.MJPEG)]
    [InlineData(MediaSynchronizationMode.SharedTimeline, true, false, Codec.H264, ".mp4")]
    [InlineData(MediaSynchronizationMode.WallClock, false, false, Codec.H264, ".mp4")]
    [InlineData(MediaSynchronizationMode.SharedTimeline, false, true, Codec.MJPEG, ".mp4")]
    [InlineData(MediaSynchronizationMode.WallClock, true, false, Codec.MJPEG, ".mp4")]
    public async Task MuxesBothTracksWithOffsetsAndEitherCompletionOrder(
        MediaSynchronizationMode mode, bool videoFirst, bool concurrent = false, Codec videoCodec = Codec.H264,
        string extension = ".mkv")
    {
        var (video, audio) = await CreateInput(videoCodec);
        var outputPath = Path.ChangeExtension(OutputPath, extension);
        using var muxer = new MediaMuxer.MediaMuxer(new MediaMuxerSettings
        {
            OutputPath = outputPath,
            SynchronizationMode = mode
        });
        await muxer.Video.PrepareForExecutionAsync(RtpTimingTests.Context, default);
        await muxer.Audio.PrepareForExecutionAsync(RtpTimingTests.Context, default);
        async Task WriteVideo()
        {
            foreach (var sample in video)
            {
                var timed = sample with { WallClock = _epoch + sample.Pts!.Value };
                if (videoCodec == Codec.MJPEG)
                {
                    timed = timed with
                    {
                        Pts = mode == MediaSynchronizationMode.WallClock ? null : sample.Pts,
                        Dts = null
                    };
                }

                await muxer.Video.ConsumeAsync(timed, default);
            }

            var completion = muxer.Video.CompleteAsync(default);
            Assert.True(completion.IsCompletedSuccessfully);
            await completion;
        }

        async Task WriteAudio()
        {
            foreach (var sample in audio)
            {
                var timed = mode == MediaSynchronizationMode.WallClock
                    ? sample with
                    {
                        Pts = sample.Pts - TimeSpan.FromMilliseconds(200),
                        Dts = null,
                        WallClock = _epoch + sample.Pts!.Value
                    }
                    : sample;
                await muxer.Audio.ConsumeAsync(timed, default);
            }

            var completion = muxer.Audio.CompleteAsync(default);
            Assert.True(completion.IsCompletedSuccessfully);
            await completion;
        }

        if (concurrent)
        {
            await Task.WhenAll(Task.Run(WriteVideo), Task.Run(WriteAudio));
        }
        else if (videoFirst)
        {
            await WriteVideo();
            await muxer.Video.CompleteAsync(default);
            await WriteAudio();
        }
        else
        {
            await WriteAudio();
            await muxer.Audio.CompleteAsync(default);
            await WriteVideo();
        }

        await muxer.Audio.CompleteAsync(default);
        await muxer.Video.CompleteAsync(default);
        AssertOutput(video.Count, audio, videoCodec, outputPath);
    }

    [Theory]
    [InlineData(Codec.H264)]
    [InlineData(Codec.MJPEG)]
    [InlineData(Codec.H264, ".mp4")]
    [InlineData(Codec.MJPEG, ".mp4")]
    public async Task BranchPipelineTranscodesVideoAndPassesAudioUnchanged(
        Codec outputCodec, string extension = ".mkv")
    {
        var (video, audio) = await CreateInput();
        var outputPath = Path.ChangeExtension(OutputPath, extension);
        var channel = Channel.CreateUnbounded<MediaSample<CompleteFrame>>();
        var input = video.Select(sample => new MediaSample<CompleteFrame>(sample.Frame, sample.TrackId,
                sample.Pts, sample.Dts))
            .Concat(audio.Select(sample => new MediaSample<CompleteFrame>(sample.Frame, sample.TrackId,
                sample.Pts, sample.Dts))).OrderBy(sample => sample.Dts);
        foreach (var sample in input)
        {
            Assert.True(channel.Writer.TryWrite(sample));
        }

        channel.Writer.Complete();
        using var muxer = new MediaMuxer.MediaMuxer(new MediaMuxerSettings
        {
            OutputPath = outputPath,
            SynchronizationMode = MediaSynchronizationMode.SharedTimeline
        });
        var pipeline = PipelineBuilder.From(RtpTimingTests.Context,
                new ChannelSource<MediaSample<CompleteFrame>>(channel))
            .Branch(branch => branch.To(new FrameFilterUnit<AudioFrame>()).Flush(muxer.Audio))
            .To(new FrameFilterUnit<EncodedVideoFrame>())
            .To(new VideoTranscoderUnit.VideoTranscoderUnit(new VideoTranscoderUnitSettings
            {
                Fps = 25,
                OutputCodec = outputCodec
            }))
            .Flush(muxer.Video);

        await pipeline.RunAsync().WaitAsync(TimeSpan.FromSeconds(15));
        AssertOutput(video.Count, audio, outputCodec, outputPath);
    }

    [Fact]
    public async Task InitializationRetainsIdrUntilSeparateSpsAndPpsArrive()
    {
        var (video, audio) = await CreateInput();
        var inputPath = Path.Combine(_directory, "video.mp4");
        foreach (var (name, filter) in new[]
        {
            ("picture", "remove_types=7|8"), ("sps", "pass_types=7"), ("pps", "pass_types=8")
        })
        {
            FFmpegTimingTests.Run("ffmpeg", "-v", "error", "-i", inputPath, "-c:v", "copy", "-frames:v", "1",
                "-bsf:v", $"h264_mp4toannexb,filter_units={filter}", "-f", "h264",
                Path.Combine(_directory, name + ".h264"));
        }

        EncodedVideoFrame Frame(string name) => new()
        {
            Codec = Codec.H264,
            Width = 64,
            Height = 64,
            IsKeyFrame = name == "picture",
            Data = File.ReadAllBytes(Path.Combine(_directory, name + ".h264"))
        };

        using var muxer = new MediaMuxer.MediaMuxer(new MediaMuxerSettings
        {
            OutputPath = OutputPath,
            SynchronizationMode = MediaSynchronizationMode.SharedTimeline
        });
        foreach (var sample in audio)
        {
            await muxer.Audio.ConsumeAsync(sample, default);
        }

        await muxer.Video.ConsumeAsync(video[0] with { Frame = Frame("picture") }, default);
        Assert.False(File.Exists(OutputPath));
        await muxer.Video.ConsumeAsync(new MediaSample<EncodedVideoFrame>(Frame("sps"), 0), default);
        Assert.False(File.Exists(OutputPath));
        await muxer.Video.ConsumeAsync(new MediaSample<EncodedVideoFrame>(Frame("pps"), 0), default);
        foreach (var sample in video.Skip(1))
        {
            await muxer.Video.ConsumeAsync(sample, default);
        }

        await muxer.Audio.CompleteAsync(default);
        await muxer.Video.CompleteAsync(default);
        AssertOutput(video.Count, audio);
    }

    [Theory]
    [InlineData("wallclock")]
    [InlineData("pts")]
    [InlineData("dts")]
    [InlineData("ordering")]
    [InlineData("codec")]
    public async Task RejectsUnsafeTimingAndUnsupportedCodecs(string scenario)
    {
        var (video, audio) = await CreateInput();
        using var muxer = new MediaMuxer.MediaMuxer(new MediaMuxerSettings
        {
            OutputPath = OutputPath,
            SynchronizationMode = scenario == "wallclock"
                ? MediaSynchronizationMode.WallClock : MediaSynchronizationMode.SharedTimeline
        });
        var sample = video[0];
        if (scenario == "pts")
        {
            sample = sample with { Pts = null };
        }
        else if (scenario == "dts")
        {
            sample = sample with { Dts = null };
        }

        if (scenario == "codec")
        {
            sample = sample with
            {
                Frame = new EncodedVideoFrame
                {
                    Codec = Codec.AC3,
                    Width = 64,
                    Height = 64,
                    Data = new byte[] { 1 },
                    IsKeyFrame = true
                }
            };
        }

        if (scenario == "ordering")
        {
            await muxer.Audio.ConsumeAsync(audio[0], default);
            await muxer.Video.ConsumeAsync(sample, default);
        }

        await Assert.ThrowsAsync<NotSupportedException>(async () => await muxer.Video.ConsumeAsync(sample, default));
        Assert.Throws<InvalidOperationException>(() => muxer.Audio.ConsumeAsync(audio[0], default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndRepeatedDisposalReleaseResources(bool initialize)
    {
        var (video, audio) = await CreateInput();
        var muxer = new MediaMuxer.MediaMuxer(new MediaMuxerSettings
        {
            OutputPath = OutputPath,
            SynchronizationMode = MediaSynchronizationMode.SharedTimeline
        });
        await muxer.Audio.ConsumeAsync(audio[0], default);
        if (initialize)
        {
            await muxer.Video.ConsumeAsync(video[0], default);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await muxer.Audio.ConsumeAsync(audio[1], cts.Token));
        muxer.Dispose();
        muxer.Dispose();
        if (initialize)
        {
            using var file = File.Open(OutputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        else
        {
            Assert.False(File.Exists(OutputPath));
        }
    }

    [Fact]
    public async Task StartupIsBoundedAndMissingTrackFailsCompletion()
    {
        var (_, audio) = await CreateInput();
        using var muxer = new MediaMuxer.MediaMuxer(new MediaMuxerSettings
        {
            OutputPath = OutputPath,
            SynchronizationMode = MediaSynchronizationMode.SharedTimeline
        });
        for (var index = 0; index < 256; index++)
        {
            var pts = TimeSpan.FromMilliseconds(index * 32);
            await muxer.Audio.ConsumeAsync(audio[0] with { Pts = pts, Dts = pts }, default);
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await muxer.Audio.ConsumeAsync(audio[0], default));
        Assert.Contains("initialization", error.Message);
        Assert.False(File.Exists(OutputPath));
        using var empty = new MediaMuxer.MediaMuxer(new MediaMuxerSettings { OutputPath = OutputPath });
        await empty.Video.CompleteAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await empty.Audio.CompleteAsync(default));
    }

    [Fact]
    public void RejectsUnsupportedContainerAndSynchronizationMode()
    {
        Assert.Throws<ArgumentException>(() => new MediaMuxer.MediaMuxer(
            new MediaMuxerSettings { OutputPath = "output.avi" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaMuxer.MediaMuxer(
            new MediaMuxerSettings { OutputPath = OutputPath, SynchronizationMode = (MediaSynchronizationMode)42 }));
    }

    private async Task<(List<MediaSample<EncodedVideoFrame>>, List<MediaSample<AudioFrame>>)> CreateInput(
        Codec videoCodec = Codec.H264)
    {
        var path = Path.Combine(_directory, "video.mp4");
        var codecArguments = videoCodec == Codec.H264
            ? new[] { "-c:v", "libx264", "-bf", "2", "-g", "6" }
            : ["-c:v", "mjpeg", "-pix_fmt", "yuvj420p"];
        FFmpegTimingTests.Run("ffmpeg", ["-v", "error", "-f", "lavfi", "-i",
            "testsrc2=size=64x64:rate=25:duration=0.48", .. codecArguments, path]);
        var audioPath = Path.Combine(_directory, "audio.ac3");
        FFmpegTimingTests.Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i",
            "sine=sample_rate=48000:duration=0.448", "-c:a", "ac3", "-b:a", "192k", audioPath);
        var audio = File.ReadAllBytes(audioPath).Chunk(768).Select((data, index) =>
        {
            var pts = TimeSpan.FromMilliseconds(200 + index * 32);
            return new MediaSample<AudioFrame>(new AudioFrame
            {
                Codec = Codec.AC3,
                Data = data,
                IsKeyFrame = true,
                SampleRate = 48000,
                Channels = 1,
                Bitrate = 192000,
                Duration = TimeSpan.FromMilliseconds(32)
            }, 1, pts, pts);
        }).ToList();
        return (await FFmpegTimingTests.ReadVideo(path), audio);
    }

    private void AssertOutput(int videoCount, List<MediaSample<AudioFrame>> audio, Codec videoCodec = Codec.H264,
        string? outputPath = null)
    {
        outputPath ??= OutputPath;
        using var output = JsonDocument.Parse(FFmpegTimingTests.Run("ffprobe", "-v", "error", "-show_streams",
            "-show_packets", "-show_data_hash", "sha256", "-of", "json", outputPath));
        var streams = output.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert.Equal(new[] { videoCodec == Codec.H264 ? "h264" : "mjpeg", "ac3" },
            streams.Select(stream => stream.GetProperty("codec_name").GetString()));
        var packets = output.RootElement.GetProperty("packets").EnumerateArray().ToArray();
        var videoPackets = packets.Where(packet => packet.GetProperty("stream_index").GetInt32() == 0).ToArray();
        var audioPackets = packets.Where(packet => packet.GetProperty("stream_index").GetInt32() == 1).ToArray();
        Assert.Equal(videoCount, videoPackets.Length);
        Assert.Equal(audio.Count, audioPackets.Length);
        double Pts(JsonElement packet) => double.Parse(packet.GetProperty("pts_time").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(Pts(audioPackets[0]) - Pts(videoPackets[0]), 0.199, 0.201);
        Assert.Equal(audio.Select(sample => "SHA256:" +
                Convert.ToHexString(SHA256.HashData(sample.Frame.Data)).ToLowerInvariant()),
            audioPackets.Select(packet => packet.GetProperty("data_hash").GetString()));
        FFmpegTimingTests.Run("ffmpeg", "-v", "error", "-i", outputPath, "-f", "null", "-");
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
