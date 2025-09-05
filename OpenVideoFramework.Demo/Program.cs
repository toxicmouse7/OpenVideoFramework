using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using OpenVideoFramework.FrameAssemblerUnit;
using OpenVideoFramework.HttpStreamSink;
using OpenVideoFramework.Pipelines.Builder;
using OpenVideoFramework.RtpDemuxUnit;
using OpenVideoFramework.RtpDepayUnit;
using OpenVideoFramework.RtspSource;
using OpenVideoFramework.VideoFileSink;

DynamicallyLoadedBindings.LibrariesPath = @"D:\FFmpeg\bin\";
DynamicallyLoadedBindings.Initialize();

var pipeline = PipelineBuilder
    .From(new RtspSource(new RtspSourceConfiguration
    {
        Url = "rtsp://localhost:554/"
    }))
    .To(new RtpDepayUnit())
    .To(new RtpDemuxUnit())
    .To(new StreamFilterUnit(data => 
        data.StreamContext is { MediaType: MediaType.Video, Codec: Codec.MJPEG }))
    .To(new RtpFrameAssemblerUnit())
    .Branch(branch =>
    {
        branch.Flush(new HttpStreamSink(new HttpStreamSinkSettings
        {
            Route = "/stream"
        }));
    })
    .Flush(new VideoFileSink(new VideoFileSinkSettings
    {
        Codec = Codec.MJPEG,
        Fps = 15,
        Width = 720,
        Height = 480,
        RollPeriod = TimeSpan.FromSeconds(10),
    }));

var (task, cts) = await pipeline.RunAsync();

await task;