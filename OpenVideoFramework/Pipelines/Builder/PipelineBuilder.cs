using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

public static class PipelineBuilder
{
    public static PipelineBuilder<TOutput> From<TOutput>(PipelineContext context, IPipelineSource<TOutput> source)
    {
        return new PipelineBuilder<TOutput>(source, context);
    }
}

public class PipelineBuilder<TCurrentOutput>
{
    private readonly List<IPipelineElement> _elements;
    private readonly List<object> _channels;
    private readonly PipelineContext _context;

    public PipelineBuilder(IPipelineSource<TCurrentOutput> source, PipelineContext context)
    {
        _context = context;
        _channels = [];
        _elements = [];


        var sourceChannel = Channel.CreateUnbounded<TCurrentOutput>();
        var sourceElement = new SourceElement<TCurrentOutput>(source, sourceChannel.Writer);
        _elements.Add(sourceElement);
        _channels.Add(sourceChannel);
    }

    private PipelineBuilder(List<IPipelineElement> elements, List<object> channels, PipelineContext context)
    {
        _elements = elements;
        _channels = channels;
        _context = context;
    }

    public PipelineBuilder<TNextOutput> To<TNextOutput>(IPipelineUnit<TCurrentOutput, TNextOutput> unit)
    {
        var channel = Channel.CreateUnbounded<TNextOutput>();

        var previousChannel = (Channel<TCurrentOutput>)_channels[^1];
        var unitElement = new UnitElement<TCurrentOutput, TNextOutput>(unit, previousChannel.Reader, channel.Writer);
        _elements.Add(unitElement);
        _channels.Add(channel);

        return new PipelineBuilder<TNextOutput>(_elements, _channels, _context);
    }

    public PipelineBuilder<TCurrentOutput> Branch(Action<PipelineBuilder<TCurrentOutput>> branchBuilderAction)
    {
        var currentChannel = (Channel<TCurrentOutput>)_channels[^1];

        var splitter = new ChannelSplitterElement<TCurrentOutput>(currentChannel.Reader);

        var continuationChannel = Channel.CreateUnbounded<TCurrentOutput>();
        var branchChannel = Channel.CreateUnbounded<TCurrentOutput>();
        
        splitter.AddBranch(continuationChannel.Writer);
        splitter.AddBranch(branchChannel.Writer);

        _elements.Add(splitter);
        _channels.Add(continuationChannel);

        var branchBuilder = new PipelineBuilder<TCurrentOutput>(_elements, [branchChannel], _context);
        branchBuilderAction(branchBuilder);

        return this;
    }

    public Pipeline Flush(IPipelineSink<TCurrentOutput> sink)
    {
        var lastChannel = (Channel<TCurrentOutput>)_channels[^1];
        var sinkElement = new SinkElement<TCurrentOutput>(sink, lastChannel.Reader);
        _elements.Add(sinkElement);

        return new Pipeline(_elements, _context);
    }

    public Pipeline Build()
    {
        var sink = new NullSink<TCurrentOutput>();
        var lastChannel = (Channel<TCurrentOutput>)_channels[^1];
        var sinkElement = new SinkElement<TCurrentOutput>(sink, lastChannel.Reader);
        _elements.Add(sinkElement);

        return new Pipeline(_elements, _context);
    }
}