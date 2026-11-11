# Open Video Framework

[![NuGet](https://img.shields.io/nuget/v/OpenVideoFramework?logo=nuget)](https://www.nuget.org/packages/OpenVideoFramework)
[![Downloads](https://img.shields.io/nuget/dt/OpenVideoFramework?logo=nuget)](https://www.nuget.org/packages/OpenVideoFramework)
[![Pipeline](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fgitlab.toxicmouse7.ru%2Fapi%2Fv4%2Fprojects%2Ftoxicmouse7%252Fopenvideoframework%2Fpipelines%3Fper_page%3D1%26order_by%3Did%26sort%3Ddesc&query=%24%5B0%5D.status&label=build&logo=gitlab)](https://gitlab.toxicmouse7.ru/toxicmouse7/openvideoframework/-/pipelines)
[![License](https://img.shields.io/badge/license-LGPL--2.1-blue)](https://github.com/toxicmouse7/OpenVideoFramework/blob/master/LICENSE)

OpenVideoFramework is a C# library for recording and processing media streams.
Build a pipeline from an RTSP source or a media file, add processing stages,
then write the result to a file or serve it over HTTP.

## Features

- RTSP input over UDP or TCP, with H.264, MJPEG, and AC3 frame assembly.
- H.264 and MJPEG video file input and transcoding.
- Video recording with automatic file rotation, and AC3 audio file output.
- Synchronized H.264 or MJPEG video and unchanged AC3 audio in a single MKV or MP4 file.
- Typed media samples carrying track identifiers and optional presentation, decoding, and absolute timestamps.
- MJPEG streaming over HTTP.
- Branches, optional buffers, and custom pipeline stages.

## Installation and requirements

```bash
dotnet add package OpenVideoFramework
```

Requires .NET 8 and, for file input, recording, or transcoding, **FFmpeg 9 shared libraries**.
Install the native libraries separately, using a build that matches your application's
architecture. The NuGet package includes the [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen)
bindings, but not the native libraries.

## Media samples and timing

Media-processing stages exchange `MediaSample<T>` rather than bare frames. This is
a breaking API change: custom media units and sinks must use the sample types too.
Frames contain encoded data and intrinsic properties; timing belongs to the sample:

```csharp
public sealed record MediaSample<T>(
    T Frame,
    int TrackId,
    TimeSpan? Pts = null,
    TimeSpan? Dts = null,
    DateTimeOffset? WallClock = null) where T : CompleteFrame;
```

- `TrackId` identifies a track within its source.
- `Pts` and `Dts` are optional presentation and decoding timestamps, expressed as
  `TimeSpan`. Negative timestamps are supported; missing timestamps remain unknown.
- `WallClock` is an optional absolute presentation time, not necessarily capture time.

`RtpFrameAssemblerUnit` emits `MediaSample<CompleteFrame>`. It uses the track's RTP
clock rate and handles timestamp wraparound. Reception continues without RTCP
Sender Reports: each track gets a stable relative PTS, while `WallClock` remains
unknown. Sender Reports can establish absolute presentation times later without
rebasing previously established PTS. Independently based RTP tracks do not share a
synchronized timeline just because their relative timestamps both start at zero.
RTP H.264 samples have no DTS because RTP does not provide decoding timestamps.

`MediaFileUnit` converts FFmpeg packet PTS and DTS independently using each stream's
time base. It preserves negative values and offsets between video tracks, leaves
missing timestamps null, and does not invent an absolute clock reference. File
input currently reads video tracks only.

`FrameFilterUnit<T>` selects the frame type while preserving track and timing
metadata. `VideoTranscoderUnit` consumes and produces
`MediaSample<EncodedVideoFrame>`, associates timing with delayed decoder/encoder
output, and preserves `WallClock` where that association is reliable. When input
presentation timestamps are unavailable, it generates timing using the configured FPS.

## Record an RTSP video stream

This example records an H.264 stream to MP4 and starts a new file roughly every
10 seconds. Source timestamps are used when available; 25 FPS supplies packet
duration and fallback timing. Set the RTSP URL and the path to your FFmpeg libraries before running it.
For streams with B frames, add a transcoder as shown below.

```csharp
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using Microsoft.Extensions.Logging.Abstractions;
using OpenVideoFramework.Common;
using OpenVideoFramework.FrameFilterUnit;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.Pipelines.Builder;
using OpenVideoFramework.RtpFrameAssemblerUnit;
using OpenVideoFramework.RtspSource;
using OpenVideoFramework.VideoFileSink;

DynamicallyLoadedBindings.LibrariesPath = "/path/to/ffmpeg/shared/libraries";
DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
DynamicallyLoadedBindings.Initialize();

var context = new PipelineContext("Recording", NullLoggerFactory.Instance);
var pipeline = PipelineBuilder
    .From(context, new RtspSource(new RtspSourceConfiguration
    {
        Url = "rtsp://localhost:8554/h264",
        Transport = RtspTransportType.Tcp,
        AllowedMediaType = MediaType.Video
    }))
    .To(new RtpFrameAssemblerUnit())
    .To(new FrameFilterUnit<EncodedVideoFrame>())
    .Flush(new VideoFileSink(new VideoFileSinkSettings
    {
        OutputPath = "video.mp4",
        RollPeriod = TimeSpan.FromSeconds(10),
        Fps = 25
    }));

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};

try
{
    await pipeline.RunAsync(cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
}
```

The example uses RTP/RTCP interleaved over the RTSP TCP connection. Omit `Transport`
or set it to `RtspTransportType.Udp` to use UDP, the default.

Pass your application's `ILoggerFactory` instead of `NullLoggerFactory.Instance` to
enable logging. See [OpenVideoFramework.Example](OpenVideoFramework.Example) for an
example with Serilog and transcoding.

## Transcoding and HTTP streaming

To re-encode video, add a `VideoTranscoderUnit` between the frame filter and the sink.
Import `OpenVideoFramework.VideoTranscoderUnit`:

```csharp
.To(new VideoTranscoderUnit(new VideoTranscoderUnitSettings
{
    OutputCodec = Codec.H264,
    Fps = 25
}))
```

This re-encodes the stream even if it is already H.264. The output has no B frames
and can be passed to `VideoFileSink`. Use the same FPS for both stages.
Set `SpecificDecoder` or `SpecificEncoder` if you need a particular FFmpeg codec implementation.

For HTTP streaming, set `OutputCodec = Codec.MJPEG` and replace the file sink with
`HttpStreamSink` from `OpenVideoFramework.HttpStreamSink`:

```csharp
.Flush(new HttpStreamSink(new HttpStreamSinkSettings
{
    Port = 8080,
    Route = "/stream"
}));
```

Open `http://localhost:8080/stream` to view the stream. The HTTP sink accepts MJPEG only.

## Record video and audio together

`MediaMuxer` owns one output file and exposes independent typed `Video` and `Audio`
sinks. Use `Branch` to pass AC3 audio through unchanged while transcoding only
video. The supported combinations are H.264 or MJPEG video plus AC3 audio, with
exactly one track of each, in `.mkv` or `.mp4`. The output format follows the extension.
H.264 samples must contain Annex B data, as produced by the existing assembler
and transcoder.

The following pipeline uses the FFmpeg initialization, `context`, and cancellation
setup from the recording example. Its RTSP source must provide reliable RTCP timing
for both tracks, as described below. Leave `AllowedMediaType` unset to receive both:

```csharp
using OpenVideoFramework.MediaMuxer;
using OpenVideoFramework.VideoTranscoderUnit;

using var muxer = new MediaMuxer(new MediaMuxerSettings
{
    OutputPath = "record.mkv",
    SynchronizationMode = MediaSynchronizationMode.WallClock
});

var pipeline = PipelineBuilder
    .From(context, new RtspSource(new RtspSourceConfiguration
    {
        Url = "rtsp://localhost:8554/h264-ac3",
        Transport = RtspTransportType.Tcp
    }))
    .To(new RtpFrameAssemblerUnit())
    .Branch(audio => audio
        .To(new FrameFilterUnit<AudioFrame>())
        .Flush(muxer.Audio))
    .To(new FrameFilterUnit<EncodedVideoFrame>())
    .To(new VideoTranscoderUnit(new VideoTranscoderUnitSettings
    {
        OutputCodec = Codec.H264,
        Fps = 25
    }))
    .Flush(muxer.Video);

await pipeline.RunAsync(cancellation.Token);
```

Use `OutputCodec = Codec.MJPEG` for MJPEG output, or `OutputPath = "record.mp4"`
for MP4. H.264 input with reordered pictures can be muxed directly only when both
PTS and DTS are supplied; RTP H.264 requires transcoding first to obtain DTS.

Choose the synchronization mode according to the source:

| Mode | Required timing |
| --- | --- |
| `WallClock` (default) | Reliable absolute presentation time on every media sample of both tracks. Known DTS-to-PTS offsets are preserved. |
| `SharedTimeline` | Timestamps already belonging to the same timeline across both tracks. A common output origin preserves their relative offsets. |

Do not use `SharedTimeline` to combine independently normalized RTSP tracks.
The muxer fails if the selected mode lacks the required timing; it does not
approximate synchronization from packet arrival times. In particular, RTSP samples
received before Sender Reports establish `WallClock` cannot be muxed in wall-clock
mode. The muxer does not wait for those reports or recover earlier timestamps.

Initialization waits for both track configurations and, for H.264, SPS/PPS and a
decodable IDR. The startup buffer is limited to 256 samples or 32 MiB and fails
clearly if initialization cannot finish within those limits. Codec parameters
must remain compatible throughout recording. H.264 requires both PTS and DTS;
MJPEG and AC3 can use one timestamp for both because they do not reorder frames.
In wall-clock mode, MJPEG and AC3 can also use `WallClock` when both relative
timestamps are missing.

Both inputs must complete successfully to flush interleaved packets and write the
trailer. The first completed input returns without waiting for the other. Cancellation
or disposal before successful completion aborts the recording; it does not finalize
the file. Unlike the separate file sinks, `MediaMuxer` does not rotate files.
MP4 requires at least two video frames; the final video packet's duration is
estimated from the preceding DTS interval without changing PTS or DTS.

## Working with pipelines

`From` selects a source, `To` adds a processing stage, and `Flush` connects the output
to a sink. `RunAsync` runs the pipeline until the source finishes, an error occurs,
or cancellation is requested. Create a new pipeline for each run.

Stages pass data directly to each other. Add `.Buffer(capacity)` to let stages run
independently with a bounded queue between them. By default, a full buffer waits
for space. You can choose a drop policy instead, but dropping H.264 frames can
corrupt the video until the next keyframe.

`Branch` sends data to an additional branch. Both branches receive the same objects,
so don't modify shared frames.

Use `FrameFilterUnit<EncodedVideoFrame>` to select video samples, or
`FrameFilterUnit<AudioFrame>` for audio samples. For example, the video filter maps
`MediaSample<CompleteFrame>` to `MediaSample<EncodedVideoFrame>` without losing metadata.
`MediaFileUnit` reads video from file paths; `ChannelSource<MediaSample<CompleteFrame>>`
lets you feed timed media into a pipeline from a .NET channel.

To add your own components, implement `IPipelineSource<TOutput>`,
`IPipelineUnit<TInput, TOutput>`, or `IPipelineSink<TInput>`.

## Current limitations

- `VideoFileSink` preserves available timestamps and uses configured FPS for packet
  duration and missing timing. `AudioFileSink` uses available timestamps and frame
  duration for fallback timing; output containers may not preserve all timing information.
- Direct H.264 recording with `VideoFileSink` requires progressive video without B frames or picture
  reordering. The codec and resolution must stay the same throughout a recording.
  Transcode first if the source doesn't meet these requirements.
- H.264 recording starts at a keyframe with SPS/PPS. File rotation also waits for
  a suitable keyframe, so files may be longer than `RollPeriod`.
- File input currently reads video tracks only.
- `MediaMuxer` supports H.264/MJPEG plus AC3 in MKV/MP4 only. It requires both tracks
  and sufficient synchronization information; it does not reconstruct H.264 DTS.

## Building

```bash
dotnet build OpenVideoFramework.sln --configuration Release
dotnet pack OpenVideoFramework/OpenVideoFramework.csproj --configuration Release
```

## Tests and CI

The tests require FFmpeg 9 shared libraries and the `ffmpeg` and `ffprobe` commands
on `PATH`, including the `libx264` encoder. They do not skip native-media checks when
FFmpeg is unavailable. On Linux or WSL, with a suitable FFmpeg installation:

```bash
FFMPEG_LIB_PATH=/path/to/ffmpeg/lib \
  dotnet test OpenVideoFramework.sln --configuration Release
```

Alternatively, build the environment from [ci/Dockerfile.tests](ci/Dockerfile.tests)
and run the tests with the repository mounted into the container:

```bash
docker build --provenance=false --sbom=false \
  -f ci/Dockerfile.tests -t ovf-tests ci
docker run --rm -v "$PWD:/workspace" ovf-tests
```

The image contains .NET SDK 8, FFmpeg 9.0.2 shared libraries, `ffmpeg`, `ffprobe`,
and `libx264`. To publish the environment for CI, replace the example registry path
with the project's Container Registry path from GitLab:

```bash
registry_project='registry.example.com/group/project'
docker login "${registry_project%%/*}"
docker tag ovf-tests "$registry_project/tests:dotnet8-ffmpeg9"
docker push "$registry_project/tests:dotnet8-ffmpeg9"
```

GitLab CI runs only on pushed release tags matching `vX.Y.Z`. It pulls the existing
`$CI_REGISTRY_IMAGE/tests:dotnet8-ffmpeg9` image, runs the tests, and saves TRX results
as artifacts for one week, including on failure. Successful tests are followed by
package build and NuGet publication. The tagged commit must belong to the corresponding
`release/X.Y` branch, and publication requires `NUGET_API_KEY` in CI/CD variables.
The image is built and pushed manually when the environment changes, not by CI.

## License

See [LICENSE](LICENSE) for the OpenVideoFramework license.

FFmpeg.AutoGen is MIT-licensed; its license is included in
[ThirdPartyNotices.txt](ThirdPartyNotices.txt). Native FFmpeg libraries are licensed
separately; see [FFmpeg licensing](https://ffmpeg.org/legal.html).

## Planned features

- Media concatenation.
- HTTP input and an RTSP server.
