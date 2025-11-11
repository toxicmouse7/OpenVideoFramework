using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

/// <summary>
/// Provides methods to create pipeline sources, connect processing units, create branches, and define sinks.
/// Supports type-safe pipeline construction with channel-based communication between components.
/// </summary>
public static class PipelineBuilder
{
    /// <summary>
    /// Creates a pipeline entrypoint.
    /// </summary>
    /// <param name="context"><see cref="PipelineContext"/>.</param>
    /// <param name="source">Source element</param>
    /// <param name="channelFactory">Factory for the underlying channel. Defaults to unbounded channel with single reader.</param>
    /// <typeparam name="TOutput">Produced type</typeparam>
    /// <returns></returns>
    public static PipelineBuilder<TOutput> From<TOutput>(
        PipelineContext context,
        IPipelineSource<TOutput> source,
        Func<Channel<TOutput>>? channelFactory = null)
    {
        return new PipelineBuilder<TOutput>(source, context, channelFactory);
    }
}


/// <inheritdoc cref="PipelineBuilder"/>
public class PipelineBuilder<TCurrentOutput>
{
    private readonly List<IPipelineElement> _elements;
    private readonly List<object> _channels;
    private readonly PipelineContext _context;

    internal PipelineBuilder(
        IPipelineSource<TCurrentOutput> source,
        PipelineContext context,
        Func<Channel<TCurrentOutput>>? channelFactory)
    {
        _context = context;
        _channels = [];
        _elements = [];


        var sourceChannel = channelFactory is not null
            ? channelFactory()
            : Channel.CreateUnbounded<TCurrentOutput>(new UnboundedChannelOptions
            {
                SingleReader = true
            });
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

    /// <summary>
    /// Connects the previous pipeline element to a new one.
    /// </summary>
    /// <param name="unit">A unit to connect to the pipeline.</param>
    /// <param name="channelFactory">Factory for the underlying channel.
    /// Defaults to an unbounded channel with a single reader and writer.</param>
    /// <typeparam name="TNextOutput">Produced type of the added unit</typeparam>
    /// <returns></returns>
    public PipelineBuilder<TNextOutput> To<TNextOutput>(
        IPipelineUnit<TCurrentOutput, TNextOutput> unit,
        Func<Channel<TNextOutput>>? channelFactory = null)
    {
        var channel = channelFactory is not null 
            ? channelFactory()
            : Channel.CreateUnbounded<TNextOutput>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

        var previousChannel = (Channel<TCurrentOutput>)_channels[^1];
        var unitElement = new UnitElement<TCurrentOutput, TNextOutput>(unit, previousChannel.Reader, channel.Writer);
        _elements.Add(unitElement);
        _channels.Add(channel);

        return new PipelineBuilder<TNextOutput>(_elements, _channels, _context);
    }

    /// <summary>
    /// Splits pipeline data flow into separate branches.
    /// </summary>
    /// <param name="branchBuilderAction">Branch builder.</param>
    /// <param name="continuationChannelFactory">Factory for the continuation (general flow) underlying channel.
    /// Defaults to an unbounded channel with a single reader and writer.</param>
    /// <param name="branchChannelFactory">Factory for the branch underlying channel.
    /// Defaults to an unbounded channel with a single reader and writer.</param>
    /// <returns></returns>
    public PipelineBuilder<TCurrentOutput> Branch(
        Action<PipelineBuilder<TCurrentOutput>> branchBuilderAction,
        Func<Channel<TCurrentOutput>>? continuationChannelFactory = null,
        Func<Channel<TCurrentOutput>>? branchChannelFactory = null)
    {
        var currentChannel = (Channel<TCurrentOutput>)_channels[^1];

        var splitter = new ChannelSplitterElement<TCurrentOutput>(currentChannel.Reader);

        var continuationChannel = continuationChannelFactory is not null 
            ? continuationChannelFactory()
            : Channel.CreateUnbounded<TCurrentOutput>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true
            });

        var branchChannel = branchChannelFactory is not null 
            ? branchChannelFactory()
            : Channel.CreateUnbounded<TCurrentOutput>(new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true
            });
        
        splitter.AddBranch(continuationChannel.Writer);
        splitter.AddBranch(branchChannel.Writer);

        _elements.Add(splitter);
        _channels.Add(continuationChannel);

        var branchBuilder = new PipelineBuilder<TCurrentOutput>(_elements, [branchChannel], _context);
        branchBuilderAction(branchBuilder);

        return this;
    }

    /// <summary>
    /// Connects the previous unit to a sink.
    /// </summary>
    /// <param name="sink">A sink to flush data to.</param>
    /// <returns></returns>
    public Pipeline Flush(IPipelineSink<TCurrentOutput> sink)
    {
        var lastChannel = (Channel<TCurrentOutput>)_channels[^1];
        var sinkElement = new SinkElement<TCurrentOutput>(sink, lastChannel.Reader);
        _elements.Add(sinkElement);

        return new Pipeline(_elements, _context);
    }

    /// <summary>
    /// Connects the previous unit to a null sink. Disposable data will be disposed.
    /// </summary>
    /// <returns></returns>
    public Pipeline Build()
    {
        var sink = new NullSink<TCurrentOutput>();
        var lastChannel = (Channel<TCurrentOutput>)_channels[^1];
        var sinkElement = new SinkElement<TCurrentOutput>(sink, lastChannel.Reader);
        _elements.Add(sinkElement);

        return new Pipeline(_elements, _context);
    }
}