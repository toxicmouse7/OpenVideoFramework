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
using OpenVideoFramework.VideoTranscoderUnit;
using Serilog;
using Serilog.Extensions.Logging;

DynamicallyLoadedBindings.LibrariesPath = Environment.ExpandEnvironmentVariables("%FFMPEG_LIB_PATH%");
DynamicallyLoadedBindings.ThrowErrorIfFunctionNotFound = true;
DynamicallyLoadedBindings.Initialize();

var logger = new LoggerConfiguration()
    .WriteTo
    .Console(outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
var loggerFactory = new SerilogLoggerFactory(logger);

var pipelineContext = new PipelineContext("My pipeline", loggerFactory);

var videoSamples = PipelineBuilder
    .From(
        pipelineContext,
        new RtspSource(new RtspSourceConfiguration
        {
            Url = "rtsp://localhost:8554/h264",
            AllowedMediaType = MediaType.Video
        }))
    .To(new RtpFrameAssemblerUnit())
    .To(new FrameFilterUnit<EncodedVideoFrame>());

var pipeline = videoSamples
    .To(new VideoTranscoderUnit(new VideoTranscoderUnitSettings
    {
        Fps = 25,
        OutputCodec = Codec.MJPEG
    }))
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
