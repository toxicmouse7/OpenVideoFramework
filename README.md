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

## Record an RTSP video stream

This example records an H.264 stream to MP4 at 25 FPS and starts a new file roughly
every 10 seconds. Set the RTSP URL and the path to your FFmpeg libraries before running it.
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

Use `FrameFilterUnit<EncodedVideoFrame>` to select video frames, or
`FrameFilterUnit<AudioFrame>` for audio. `MediaFileUnit` reads video from file paths;
`ChannelSource<T>` lets you feed a pipeline from a .NET channel.

To add your own components, implement `IPipelineSource<TOutput>`,
`IPipelineUnit<TInput, TOutput>`, or `IPipelineSink<TInput>`.

## Current limitations

- Recorded video uses the configured FPS rather than the source timestamps.
- Direct H.264 recording requires progressive video without B frames or picture
  reordering. The codec and resolution must stay the same throughout a recording.
  Transcode first if the source doesn't meet these requirements.
- H.264 recording starts at a keyframe with SPS/PPS. File rotation also waits for
  a suitable keyframe, so files may be longer than `RollPeriod`.
- File input currently reads video tracks only.

## Building

```bash
dotnet build OpenVideoFramework.sln --configuration Release
dotnet pack OpenVideoFramework/OpenVideoFramework.csproj --configuration Release
```

## License

See [LICENSE](LICENSE) for the OpenVideoFramework license.

FFmpeg.AutoGen is MIT-licensed; its license is included in
[ThirdPartyNotices.txt](ThirdPartyNotices.txt). Native FFmpeg libraries are licensed
separately; see [FFmpeg licensing](https://ffmpeg.org/legal.html).

## Planned features

- Track merging and media concatenation.
- HTTP input and an RTSP server.
