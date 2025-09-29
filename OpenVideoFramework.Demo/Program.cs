using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using OpenVideoFramework.AudioFileSink;
using OpenVideoFramework.Common;
using OpenVideoFramework.FrameFilterUnit;
using OpenVideoFramework.HttpStreamSink;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.Pipelines.Builder;
using OpenVideoFramework.RtpFrameAssemblerUnit;
using OpenVideoFramework.RtspSource;
using OpenVideoFramework.VideoFileSink;
using Serilog;
using Serilog.Extensions.Logging;

DynamicallyLoadedBindings.LibrariesPath = @"D:\FFmpeg\bin\";
DynamicallyLoadedBindings.Initialize();

var logger = new LoggerConfiguration()
    .WriteTo
    .Console(outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
var loggerFactory = new SerilogLoggerFactory(logger);

var pipelineContext = new PipelineContext("My pipeline", loggerFactory);

var pipeline = PipelineBuilder
    .From(pipelineContext, new RtspSource(new RtspSourceConfiguration
    {
        Url = "rtsp://172.17.91.213:8554/xx",
        AllowedMediaType = MediaType.Video
    }))
    .To(new RtpFrameAssemblerUnit())
    .To(new FrameFilterUnit<VideoFrame>())
    .Flush(new VideoFileSink(new VideoFileSinkSettings
    {
        RollPeriod = TimeSpan.FromSeconds(10),
        OutputPath = @"C:\Users\Aleksej\Desktop\audio.mp4"
    }));

var (task, cts) = await pipeline.RunAsync();

await task;