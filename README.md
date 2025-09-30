# Open Video Framework

## Background

This project was inspired by the lack of a convenient library for working with media in C#.
All options are either buggy, have memory leaks, or are difficult to use.
Moreover, most of these tools are wrappers for native libraries.

## About

The framework is designed to simplify the tasks of processing various types of media data.

Interaction is built on the use of pipelines. This approach leads to transparent data flow.

Most of the code is written in pure C#,
but for complex features beyond the capabilities of mere mortals,
[ffmpeg](https://github.com/FFmpeg/FFmpeg) is used.

Currently, [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) is used as a wrapper around libav*,
but it will be replaced with a custom implementation in future versions.

## Usage example

Here is a simple usage example - getting a video track from
an RTSP stream and writing it to disk with 10-second video rotations.

```csharp
// Need to initialize FFmpeg.AutoGen
DynamicallyLoadedBindings.LibrariesPath = "/ffmpeg/lib/path";
DynamicallyLoadedBindings.Initialize();

var pipelineContext = new PipelineContext("My pipeline", loggerFactory);

var pipeline = PipelineBuilder
    .From(pipelineContext, new RtspSource(new RtspSourceConfiguration
    {
        Url = "rtsp://172.17.91.213:8554/xx",
        AllowedMediaType = MediaType.Video
    }))
    .To(new RtpFrameAssemblerUnit())
    .Flush(new VideoFileSink(new VideoFileSinkSettings
    {
        RollPeriod = TimeSpan.FromSeconds(10),
        OutputPath = "video.mp4"
    }));
```

You could find a bit more complex example inside `OpenVideoFramework.Example`

## Planned features

- Media transcoding
- H264 codec support
- Tracks merging
- Media concatination
- RtspSource TCP transport support
- HttpSource
- RTSP server
- Create documentation

## Rebuilding

To rebuild with modified FFmpeg.AutoGen (for different ffmpeg version):
1. Clone this repository
2. Replace FFmpeg.AutoGen reference
3. Run `dotnet pack`