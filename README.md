# Open Video Framework

OpenVideoFramework is a .NET library for building asynchronous media pipelines in C#.
Connect sources, processing units, and sinks to receive, assemble, transcode, record,
and stream media. FFmpeg is used for file input, transcoding, and media file output
through [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen).

## Features

- RTSP input with RTP/UDP transport and RTCP receiver reports.
- H.264, MJPEG, and AC3 RTP frame assembly. H.264 supports SPS/PPS from SDP and RTP,
  caches parameter sets, and waits for an IDR frame before starting output.
- H.264 and MJPEG video file input through `MediaFileUnit`, including AVCC-to-Annex-B conversion.
- H.264 and MJPEG transcoding through native FFmpeg decoder and encoder state,
  including delayed output draining when the pipeline completes.
- Constant-frame-rate video recording with explicit FPS and automatic file rotation.
- AC3 audio file output and MJPEG streaming over HTTP.
- Explicit bounded buffers, branching, and custom sources, units, and sinks.

## Installation and requirements

```bash
dotnet add package OpenVideoFramework
```

The library targets .NET 8.0. Components that use FFmpeg require **FFmpeg 9 shared libraries**
matching the process architecture. The NuGet package provides the managed bindings;
native FFmpeg libraries must be installed separately. An FFmpeg executable alone is not sufficient.
The FFmpeg build must include the decoders and encoders needed by your pipeline.

Initialize the bindings before running a pipeline that uses FFmpeg:

```csharp
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

DynamicallyLoadedBindings.LibrariesPath = "/path/to/ffmpeg/shared/libraries";
DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
DynamicallyLoadedBindings.Initialize();
```

## Record an RTSP video stream

This example records an H.264 stream to MP4 at 25 FPS, rotating files at keyframes
once the output duration reaches 10 seconds. Direct H.264 recording requires a
progressive stream without picture reordering; see the limitations below.

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

Replace `NullLoggerFactory.Instance` with your application's `ILoggerFactory` to see
connection, frame assembly, and file rotation logs. The `OpenVideoFramework.Example`
project contains a runnable example with Serilog and video transcoding.

## Transcoding and HTTP streaming

Insert a `VideoTranscoderUnit` after the video frame filter and before the sink:

```csharp
.To(new VideoTranscoderUnit(new VideoTranscoderUnitSettings
{
    OutputCodec = Codec.H264,
    Fps = 25
}))
```

Import `OpenVideoFramework.VideoTranscoderUnit`. The transcoder re-encodes even when
the input and output codecs match. H.264 encoding disables B frames, so this stage
can prepare reordered input for constant-frame-rate recording. `SpecificDecoder`
and `SpecificEncoder` can select codecs available in your FFmpeg build.
Use the same FPS in the transcoder and video file sink.

For MJPEG over HTTP, use `OutputCodec = Codec.MJPEG` in the transcoder and replace
the file sink with the following sink from `OpenVideoFramework.HttpStreamSink`:

```csharp
.Flush(new HttpStreamSink(new HttpStreamSinkSettings
{
    Port = 8080,
    Route = "/stream"
}));
```

The stream is available at `http://localhost:8080/stream`. The HTTP sink accepts
only MJPEG frames and forwards them at their arrival rate, without automatic pacing.

## Pipeline behavior and frame format

`From` selects a source, `To` adds a processing unit, and `Flush` attaches the final
sink and returns a pipeline. Call `RunAsync` to execute it. A pipeline can be run once.

Stages forward items directly by default. Add `.Buffer(capacity)` where a bounded
queue and a separate worker are needed. The default `OverflowMode.Wait` applies
backpressure; other modes drop items when the buffer is full. Dropping encoded
H.264 frames can break decoding of later dependent frames.

`Branch` sends the same item to another pipeline branch. Treat shared frames as
read-only. `ChannelSource<T>` accepts input from a `System.Threading.Channels.Channel<T>`.
Custom components implement `IPipelineSource`, `IPipelineUnit`, or `IPipelineSink`;
units can emit zero, one, or multiple outputs per input.

`FrameFilterUnit<EncodedVideoFrame>` selects video from a mixed `CompleteFrame`
pipeline; use `FrameFilterUnit<AudioFrame>` for audio. `CompleteFrame` is a marker base.

`EncodedVideoFrame` contains only `Width`, `Height`, `Codec`, `Data`
(`ReadOnlyMemory<byte>`), and `IsKeyFrame`. H.264 data is one complete Annex B access
unit; MJPEG data is one complete JPEG image. An H.264 access unit can depend on
previous frames. Source timestamps, durations, clock rates, and codec extradata
are not part of the video frame model.

## Current limitations

- RTSP media transport uses UDP; interleaved RTP over RTSP/TCP is not implemented.
- Video file output is constant-frame-rate. Original source timing is discarded;
  `VideoFileSinkSettings.Fps` must be finite and greater than zero.
- Direct H.264 recording supports progressive I/P pictures in presentation order
  with fixed codec and dimensions. Unsupported picture ordering and B frames are
  rejected. Transcode such input before recording.
- H.264 recording needs SPS/PPS at the first keyframe. The RTP assembler supplies
  cached parameters before IDR frames when they are missing from the access unit.
- H.264 rotation waits for a suitable keyframe, so segments can exceed `RollPeriod`.
- `MediaFileUnit` currently emits video only; it does not import audio tracks.

## Building

```bash
dotnet restore OpenVideoFramework.sln
dotnet build OpenVideoFramework.sln --configuration Release
dotnet pack OpenVideoFramework/OpenVideoFramework.csproj --configuration Release
```

To create a separate symbols package, add `-p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg`.

## Planned features

- Track merging and media concatenation.
- RTSP/TCP media transport.
- HTTP input and an RTSP server.
