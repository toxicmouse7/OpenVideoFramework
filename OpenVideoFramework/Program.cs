using OpenVideoFramework;
using OpenVideoFramework.Pipelines.Builder;

var pipeline = PipelineBuilder
    .From(new RandomStringSource())
    .To(new TransformerUnit())
    .Flush(new ConsoleSink());

var (task, cts) = await pipeline.RunAsync();

cts.Cancel();

