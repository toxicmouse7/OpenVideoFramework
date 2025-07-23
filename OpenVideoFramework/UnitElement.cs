using System.Threading.Channels;

namespace OpenVideoFramework;

public class UnitElement<TInput, TOutput> : IPipelineElement
{
    private readonly IPipelineUnit<TInput, TOutput> _unit;
    private readonly ChannelReader<TInput> _reader;
    private readonly ChannelWriter<TOutput> _writer;

    public UnitElement(IPipelineUnit<TInput, TOutput> unit, ChannelReader<TInput> reader, ChannelWriter<TOutput> writer)
    {
        _unit = unit;
        _reader = reader;
        _writer = writer;
    }
    
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _unit.ProcessAsync(_reader, _writer, cancellationToken);
        }
        finally
        {
            _writer.Complete();
        }
    }
}