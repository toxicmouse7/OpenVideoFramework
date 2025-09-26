using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

public class UnitElement<TInput, TOutput> : IPipelineElement, IDisposable
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

    public async Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        await _unit.PrepareForExecutionAsync(context, cancellationToken);
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

    public void Dispose()
    {
        if (_unit is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}