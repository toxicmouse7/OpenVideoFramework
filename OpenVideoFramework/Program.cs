using OpenVideoFramework;

var pipeline = PipelineBuilder
    .From(new RandomStringSource())
    .To(new TransformerUnit())
    .Flush(new ConsoleSink());

var (task, cts) = pipeline.Run();

cts.Cancel();

