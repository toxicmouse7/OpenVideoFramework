using System.Threading.Channels;

namespace OpenVideoFramework;

public interface IPipelineSource<TOutput>
{
    Task ProduceAsync(ChannelWriter<TOutput> output, CancellationToken cancellationToken);
}

public interface IPipelineSink<TInput>
{
    Task ConsumeAsync(ChannelReader<TInput> input, CancellationToken cancellationToken);
}

public interface IPipelineUnit<TInput, TOutput>
{
    Task ProcessAsync(ChannelReader<TInput> reader, ChannelWriter<TOutput> writer, CancellationToken cancellationToken);
}

public interface IPipelineElement
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}

public static class PipelineBuilder
{
    public static PipelineBuilder<TOutput> From<TOutput>(IPipelineSource<TOutput> source)
    {
        return new PipelineBuilder<TOutput>(source);
    }
}

public class PipelineBuilder<TCurrentOutput>
{
    private readonly List<IPipelineElement> _elements;
    private readonly List<object> _channels;

    public PipelineBuilder(IPipelineSource<TCurrentOutput> source)
    {
        _channels = [];
        _elements = [];


        var sourceChannel = Channel.CreateUnbounded<TCurrentOutput>();
        var sourceElement = new SourceElement<TCurrentOutput>(source, sourceChannel.Writer);
        _elements.Add(sourceElement);
        _channels.Add(sourceChannel);
    }

    private PipelineBuilder(List<IPipelineElement> elements, List<object> channels)
    {
        _elements = elements;
        _channels = channels;
    }

    public PipelineBuilder<TNextOutput> To<TNextOutput>(IPipelineUnit<TCurrentOutput, TNextOutput> unit)
    {
        var channel = Channel.CreateUnbounded<TNextOutput>();

        var previousChannel = (Channel<TCurrentOutput>)_channels[^1];
        var unitElement = new UnitElement<TCurrentOutput, TNextOutput>(unit, previousChannel.Reader, channel.Writer);
        _elements.Add(unitElement);
        _channels.Add(channel);

        return new PipelineBuilder<TNextOutput>(_elements, _channels);
    }

    public Pipeline Flush(IPipelineSink<TCurrentOutput> sink)
    {
        var lastChannel = (Channel<TCurrentOutput>)_channels[^1];
        var sinkElement = new SinkElement<TCurrentOutput>(sink, lastChannel.Reader);
        _elements.Add(sinkElement);

        return new Pipeline(_elements);
    }
}

public class Pipeline
{
    private readonly List<IPipelineElement> _elements;

    public Pipeline(List<IPipelineElement> elements)
    {
        _elements = elements;
    }

    public (Task, CancellationTokenSource) Run()
    {
        var cts = new CancellationTokenSource();
        var tasks = _elements.ConvertAll(e => e.ExecuteAsync(cts.Token));
        var combinedTask = Task.WhenAll(tasks);

        return (combinedTask, cts);
    }
}