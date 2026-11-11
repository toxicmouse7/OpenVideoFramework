using System.Diagnostics;
using System.Text.Json;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using OpenVideoFramework.AudioFileSink;
using OpenVideoFramework.Common;
using OpenVideoFramework.MediaFile;
using OpenVideoFramework.RtspSource.Rtp;
using OpenVideoFramework.VideoFileSink;
using OpenVideoFramework.VideoTranscoderUnit;
using Xunit;

namespace OpenVideoFramework.Tests;

public sealed class FFmpegTimingTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ovf-timing-").FullName;

    static FFmpegTimingTests()
    {
        DynamicallyLoadedBindings.LibrariesPath = Environment.GetEnvironmentVariable("FFMPEG_LIB_PATH") ?? "";
        DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
        DynamicallyLoadedBindings.Initialize();
    }

    [Theory]
    [InlineData(-7200L, -800000L)]
    [InlineData(3600L, 400000L)]
    [InlineData(0L, 0L)]
    [InlineData(long.MinValue, null)]
    public void TimestampConversionPreservesMissingAndNegativeValues(long timestamp, long? ticks)
    {
        var timeBase = new AVRational { num = 1, den = 90000 };
        var time = FFmpegPacketAdapter.FromTimestamp(timestamp, timeBase);
        Assert.Equal(ticks, time?.Ticks);
        Assert.Equal(timestamp, FFmpegPacketAdapter.ToTimestamp(time, timeBase));
    }

    [Fact]
    public async Task FileSourcePreservesContainerOffsetsNegativeDtsAndMissingRawTimestamps()
    {
        var path = CreateVideo();
        var samples = await ReadVideo(path);
        var first = samples.Where(sample => sample.TrackId == 0).ToList();
        var second = samples.Where(sample => sample.TrackId == 1).ToList();
        Assert.Equal(12, first.Count);
        Assert.Equal(12, second.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(200), second[0].Pts - first[0].Pts);
        Assert.True(first[0].Dts < TimeSpan.Zero);
        Assert.All(samples, sample => Assert.Null(sample.WallClock));

        var rawPath = Path.Combine(_directory, "raw.h264");
        Run("ffmpeg", "-v", "error", "-i", path, "-map", "0:v:0", "-c:v", "copy", rawPath);
        var raw = await ReadVideo(rawPath);
        Assert.Equal(12, raw.Count);
        Assert.All(raw, sample =>
        {
            Assert.Null(sample.Pts);
            Assert.Null(sample.Dts);
        });
    }

    [Fact]
    public async Task H264RtpAssemblyUsesPresentationTimeAndLeavesDtsUnknown()
    {
        var frame = (await ReadVideo(CreateVideo()))[0].Frame;
        var data = frame.Data.ToArray();
        var nals = new List<(int Start, int End)>();
        var start = -1;
        for (var i = 0; i < data.Length - 3; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0)
            {
                continue;
            }

            var prefix = data[i + 2] == 1 ? 3 : data[i + 2] == 0 && data[i + 3] == 1 ? 4 : 0;
            if (prefix == 0)
            {
                continue;
            }

            if (start >= 0)
            {
                nals.Add((start, i));
            }

            start = i + prefix;
            i = start - 1;
        }

        nals.Add((start, data.Length));
        var assembler = new RtpFrameAssemblerUnit.RtpFrameAssemblerUnit();
        await assembler.PrepareForExecutionAsync(RtpTimingTests.Context, default);
        var output = new List<MediaSample<CompleteFrame>>();
        for (var picture = 0; picture < 2; picture++)
        {
            for (var index = 0; index < nals.Count; index++)
            {
                var packet = new RtpPacket
                {
                    TrackId = 7,
                    ClockRate = 90000,
                    Header = new RtpPacketHeader(false, false, 0, index == nals.Count - 1, PayloadType.H264,
                        (ushort)(picture * nals.Count + index), (uint)(1000 + picture * 3600), 1, [], 0),
                    Content = data[nals[index].Start..nals[index].End]
                };
                await assembler.ProcessAsync(packet, (sample, _) =>
                {
                    output.Add(sample);
                    return ValueTask.CompletedTask;
                }, default);
            }
        }

        Assert.Equal(2, output.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(40), output[1].Pts);
        Assert.All(output, sample =>
        {
            Assert.Equal(7, sample.TrackId);
            Assert.Null(sample.Dts);
            Assert.Null(sample.WallClock);
        });
    }

    [Theory]
    [InlineData(Codec.MJPEG)]
    [InlineData(Codec.H264)]
    public async Task TranscoderPreservesReorderedPtsWallClockAndDrainsDelayedOutput(Codec outputCodec)
    {
        var input = (await ReadVideo(CreateVideo())).Where(sample => sample.TrackId == 0)
            .Select(sample => sample with
            {
                TrackId = 7,
                Pts = sample.Pts * 1.5 + TimeSpan.FromMilliseconds(123),
                Dts = sample.Dts * 1.5 + TimeSpan.FromMilliseconds(123)
            }).ToList();
        Assert.Contains(input.Zip(input.Skip(1)), pair => pair.Second.Pts < pair.First.Pts);
        var epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var transcoder = new VideoTranscoderUnit.VideoTranscoderUnit(new VideoTranscoderUnitSettings
        {
            Fps = 25,
            OutputCodec = outputCodec
        });
        await transcoder.PrepareForExecutionAsync(RtpTimingTests.Context, default);
        var output = new List<MediaSample<EncodedVideoFrame>>();
        ValueTask Emit(MediaSample<EncodedVideoFrame> sample, CancellationToken _)
        {
            output.Add(sample);
            return ValueTask.CompletedTask;
        }

        foreach (var sample in input)
        {
            await transcoder.ProcessAsync(sample with { WallClock = epoch + sample.Pts!.Value }, Emit, default);
        }

        Assert.True(output.Count < input.Count);
        await transcoder.CompleteAsync(Emit, default);
        Assert.Equal(input.Select(sample => sample.Pts).Order(), output.Select(sample => sample.Pts));
        Assert.All(output, sample =>
        {
            Assert.Equal(7, sample.TrackId);
            Assert.Equal(epoch + sample.Pts!.Value, sample.WallClock);
            Assert.NotNull(sample.Dts);
        });

        using (var sink = new VideoFileSink.VideoFileSink(new VideoFileSinkSettings
        {
            Fps = 25,
            RollPeriod = TimeSpan.FromSeconds(10),
            OutputPath = Path.Combine(_directory, "timed.mp4")
        }))
        {
            await sink.PrepareForExecutionAsync(RtpTimingTests.Context, default);
            foreach (var sample in output)
            {
                await sink.ConsumeAsync(sample, default);
            }

            await sink.CompleteAsync(default);
        }

        var recorded = await ReadVideo(Assert.Single(Directory.GetFiles(_directory, "timed*.mp4")));
        Assert.Equal(output.Select(sample => sample.Pts), recorded.Select(sample => sample.Pts));
        Assert.Equal(output.Select(sample => sample.Dts), recorded.Select(sample => sample.Dts));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingPtsUsesTranscodingFpsAndVideoRecordingRotates(bool keepDts)
    {
        var input = (await ReadVideo(CreateVideo())).Where(sample => sample.TrackId == 0);
        using var transcoder = new VideoTranscoderUnit.VideoTranscoderUnit(new VideoTranscoderUnitSettings
        {
            Fps = 25,
            OutputCodec = Codec.MJPEG
        });
        await transcoder.PrepareForExecutionAsync(RtpTimingTests.Context, default);
        var output = new List<MediaSample<EncodedVideoFrame>>();
        ValueTask Emit(MediaSample<EncodedVideoFrame> sample, CancellationToken _)
        {
            output.Add(sample);
            return ValueTask.CompletedTask;
        }

        foreach (var sample in input)
        {
            await transcoder.ProcessAsync(
                sample with { Pts = null, Dts = keepDts ? sample.Dts : null }, Emit, default);
        }

        await transcoder.CompleteAsync(Emit, default);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => TimeSpan.FromMilliseconds(i * 40)),
            output.Select(sample => sample.Pts!.Value));
        Assert.All(output, sample => Assert.Null(sample.WallClock));

        using (var sink = new VideoFileSink.VideoFileSink(new VideoFileSinkSettings
        {
            Fps = 25,
            RollPeriod = TimeSpan.FromMilliseconds(160),
            OutputPath = Path.Combine(_directory, "recorded.mp4")
        }))
        {
            await sink.PrepareForExecutionAsync(RtpTimingTests.Context, default);
            foreach (var sample in output)
            {
                await sink.ConsumeAsync(sample with { Pts = null, Dts = null }, default);
            }

            await sink.CompleteAsync(default);
        }

        var files = Directory.GetFiles(_directory, "recorded*.mp4");
        Assert.Equal(3, files.Length);
        var recorded = new List<MediaSample<EncodedVideoFrame>>();
        foreach (var file in files)
        {
            recorded.AddRange(await ReadVideo(file));
        }

        Assert.Equal(output.Select(sample => sample.Pts).Order(), recorded.Select(sample => sample.Pts).Order());
    }

    [Fact]
    public async Task AudioRecordingUsesDurationFallbackAndRotates()
    {
        var path = Path.Combine(_directory, "input.ac3");
        Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=sample_rate=48000:duration=0.192",
            "-c:a", "ac3", "-b:a", "192k", path);
        var data = File.ReadAllBytes(path);
        Assert.Equal(0, data.Length % 768);
        using (var sink = new AudioFileSink.AudioFileSink(new AudioFileSinkSettings
        {
            RollPeriod = TimeSpan.FromMilliseconds(64),
            OutputPath = Path.Combine(_directory, "audio.mka")
        }))
        {
            foreach (var bytes in data.Chunk(768))
            {
                var frame = new AudioFrame
                {
                    Data = bytes,
                    Codec = Codec.AC3,
                    SampleRate = 48000,
                    Channels = 1,
                    Bitrate = 192000,
                    IsKeyFrame = true,
                    Duration = TimeSpan.FromMilliseconds(32)
                };
                await sink.ConsumeAsync(new MediaSample<AudioFrame>(frame, 3), default);
            }

            await sink.CompleteAsync(default);
        }

        var files = Directory.GetFiles(_directory, "audio*.mka");
        Assert.True(files.Length >= 3);
        var timestamps = new List<double>();
        foreach (var file in files)
        {
            using var json = JsonDocument.Parse(Run("ffprobe", "-v", "error", "-show_packets",
                "-show_entries", "packet=pts_time,dts_time", "-of", "json", file));
            foreach (var packet in json.RootElement.GetProperty("packets").EnumerateArray())
            {
                var pts = double.Parse(packet.GetProperty("pts_time").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(packet.GetProperty("pts_time").GetString(), packet.GetProperty("dts_time").GetString());
                timestamps.Add(pts);
            }
        }

        Assert.Equal(Enumerable.Range(0, data.Length / 768).Select(i => i * 0.032), timestamps.Order());
    }

    [Fact]
    public async Task DirectVideoRecordingStillRejectsReorderedH264()
    {
        var input = (await ReadVideo(CreateVideo())).Where(sample => sample.TrackId == 0);
        using var sink = new VideoFileSink.VideoFileSink(new VideoFileSinkSettings
        {
            Fps = 25,
            RollPeriod = TimeSpan.FromSeconds(10),
            OutputPath = Path.Combine(_directory, "direct.mp4")
        });
        await sink.PrepareForExecutionAsync(RtpTimingTests.Context, default);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            foreach (var sample in input)
            {
                await sink.ConsumeAsync(sample, default);
            }
        });
    }

    [Fact]
    public async Task DirectH264RecordingPreservesKnownTimestampsAndRotatesAtKeyframes()
    {
        var input = (await ReadVideo(CreateVideo(0))).Where(sample => sample.TrackId == 0)
            .Select(sample => sample with { Dts = sample.Pts - TimeSpan.FromMilliseconds(80) }).ToList();
        using (var sink = new VideoFileSink.VideoFileSink(new VideoFileSinkSettings
        {
            Fps = 25,
            RollPeriod = TimeSpan.FromMilliseconds(160),
            OutputPath = Path.Combine(_directory, "direct.mp4")
        }))
        {
            await sink.PrepareForExecutionAsync(RtpTimingTests.Context, default);
            foreach (var sample in input)
            {
                await sink.ConsumeAsync(sample, default);
            }

            await sink.CompleteAsync(default);
        }

        var files = Directory.GetFiles(_directory, "direct*.mp4");
        Assert.Equal(2, files.Length);
        var recorded = new List<MediaSample<EncodedVideoFrame>>();
        foreach (var file in files)
        {
            var segment = await ReadVideo(file);
            Assert.True(segment[0].Frame.IsKeyFrame);
            recorded.AddRange(segment);
        }

        Assert.Equal(input.Select(sample => sample.Pts).Order(), recorded.Select(sample => sample.Pts).Order());
        Assert.Equal(input.Select(sample => sample.Dts).Order(), recorded.Select(sample => sample.Dts).Order());
    }

    private string CreateVideo(int bFrames = 2)
    {
        var path = Path.Combine(_directory, "input.mp4");
        Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=25:duration=0.48",
            "-itsoffset", "0.2", "-f", "lavfi", "-i", "testsrc2=size=64x64:rate=25:duration=0.48",
            "-map", "0:v", "-map", "1:v", "-c:v", "libx264", "-bf", bFrames.ToString(), "-g", "6",
            "-fps_mode", "passthrough", path);
        return path;
    }

    internal static async Task<List<MediaSample<EncodedVideoFrame>>> ReadVideo(string path)
    {
        var source = new MediaFileUnit();
        await source.PrepareForExecutionAsync(RtpTimingTests.Context, default);
        var output = new List<MediaSample<EncodedVideoFrame>>();
        var filter = new FrameFilterUnit.FrameFilterUnit<EncodedVideoFrame>();
        await source.ProcessAsync(path, (sample, token) => filter.ProcessAsync(sample, (video, _) =>
        {
            output.Add(video);
            return ValueTask.CompletedTask;
        }, token), default);
        return output;
    }

    internal static string Run(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(true);
            throw new TimeoutException($"{command} did not finish.");
        }

        Assert.True(process.ExitCode == 0, errors.GetAwaiter().GetResult());
        return output.GetAwaiter().GetResult();
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
