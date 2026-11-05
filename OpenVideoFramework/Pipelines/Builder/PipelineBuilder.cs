namespace OpenVideoFramework.Pipelines.Builder;

public static class PipelineBuilder
{
    public static PipelineBuilder<TOutput> From<TOutput>(PipelineContext context, IPipelineSource<TOutput> source)
    {
        var element = new SourceElement<TOutput>(source);
        return new PipelineBuilder<TOutput>([element], element.Output, context, element.ExecuteAsync);
    }
}

public class PipelineBuilder<TCurrentOutput>
{
    private readonly List<IPipelineElement> _elements;
    private readonly PipelineContext _context;
    private readonly Func<CancellationToken, Task> _produce;
    private PipelineOutput<TCurrentOutput> _output;

    internal PipelineBuilder(List<IPipelineElement> elements, PipelineOutput<TCurrentOutput> output,
        PipelineContext context, Func<CancellationToken, Task> produce)
    {
        _elements = elements;
        _output = output;
        _context = context;
        _produce = produce;
    }

    public PipelineBuilder<TNextOutput> To<TNextOutput>(IPipelineUnit<TCurrentOutput, TNextOutput> unit)
    {
        var element = new UnitElement<TCurrentOutput, TNextOutput>(unit);
        _output.Connect(element.ProcessAsync, element.CompleteAsync);
        _elements.Add(element);
        return new PipelineBuilder<TNextOutput>(_elements, element.Output, _context, _produce);
    }

    public PipelineBuilder<TCurrentOutput> Buffer(int capacity, OverflowMode overflowMode = OverflowMode.Wait)
    {
        var element = new BufferElement<TCurrentOutput>(capacity, overflowMode);
        _output.Connect(element.ProcessAsync, element.CompleteAsync);
        _elements.Add(element);
        _output = element.Output;
        return this;
    }

    public PipelineBuilder<TCurrentOutput> Branch(Action<PipelineBuilder<TCurrentOutput>> branchBuilderAction)
    {
        var splitter = new ChannelSplitterElement<TCurrentOutput>();
        _output.Connect(splitter.ProcessAsync, splitter.CompleteAsync);
        _elements.Add(splitter);
        _output = splitter.Continuation;
        branchBuilderAction(new PipelineBuilder<TCurrentOutput>(_elements, splitter.Branch, _context, _produce));
        return this;
    }

    public Pipeline Flush(IPipelineSink<TCurrentOutput> sink)
    {
        var element = new SinkElement<TCurrentOutput>(sink);
        _output.Connect(element.ProcessAsync, element.CompleteAsync);
        _elements.Add(element);
        return new Pipeline([.. _elements], _context, _produce);
    }

    /// <summary>Discards items, disposing them only in pipelines without branches.</summary>
    public Pipeline Build() => Flush(new NullSink<TCurrentOutput>(!_elements.Any(e => e.IsBranch)));
}
