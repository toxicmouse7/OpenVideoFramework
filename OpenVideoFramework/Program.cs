using OpenVideoFramework;
using OpenVideoFramework.Pipelines.Builder;
using OpenVideoFramework.RtspSource;

// var pipeline = PipelineBuilder
//     .From(new RandomStringSource())
//     .To(new TransformerUnit())
//     .Flush(new ConsoleSink());

var pipeline = PipelineBuilder
    .From(new RtspSource(new RtspSourceConfiguration
    {
        Url = "rtsp://localhost:554/"
    }))
    .Build();

var (task, cts) = await pipeline.RunAsync();

task.Wait();
