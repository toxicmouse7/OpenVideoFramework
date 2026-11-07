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

DynamicallyLoadedBindings.LibrariesPath = Environment.ExpandEnvironmentVariables("%FFMPEG_LIB_PATH%");
DynamicallyLoadedBindings.Initialize();

var logger = new LoggerConfiguration()
    .WriteTo
    .Console(outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
var loggerFactory = new SerilogLoggerFactory(logger);

var pipelineContext = new PipelineContext("My pipeline", loggerFactory);

var pipeline = PipelineBuilder
    .From(
        pipelineContext,
        new RtspSource(new RtspSourceConfiguration
        {
            Url = "rtsp://172.17.91.213:8554/",
            AllowedMediaType = MediaType.Video
        }))
    .Buffer(256)
    .To(new RtpFrameAssemblerUnit())
    .To(new FrameFilterUnit<EncodedVideoFrame>())
    .Buffer(8)
    .Flush(new VideoFileSink(new VideoFileSinkSettings
    {
        RollPeriod = TimeSpan.FromSeconds(10),
        OutputPath = "video.mp4",
        Fps = 25
    }));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await pipeline.RunAsync(cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
}
