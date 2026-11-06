using System.Threading.Channels;

namespace OpenVideoFramework.Pipelines.Builder;

internal interface IBufferElement : IPipelineElement
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}

internal sealed class BufferElement<T> : IBufferElement, IDisposable
{
    private readonly Channel<T> _channel;
    private Task _worker = Task.CompletedTask;
    public PipelineOutput<T> Output { get; } = new();

    public BufferElement(int capacity, OverflowMode overflowMode)
    {
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = overflowMode switch
            {
                OverflowMode.Wait => BoundedChannelFullMode.Wait,
                OverflowMode.DropOldest => BoundedChannelFullMode.DropOldest,
                OverflowMode.DropNewest => BoundedChannelFullMode.DropNewest,
                OverflowMode.DropWrite => BoundedChannelFullMode.DropWrite,
                _ => throw new ArgumentOutOfRangeException(nameof(overflowMode))
            }
        });
    }

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public ValueTask ProcessAsync(T item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);
    public Task ExecuteAsync(CancellationToken cancellationToken) => _worker = DrainAsync(cancellationToken);

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await Output.EmitAsync(item, cancellationToken).ConfigureAwait(false);
        }

        await Output.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }

    public object? GetUnderlyingElement() => null;

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        // Workers have stopped. Release queued references without disposing items shared with branches.
        while (_channel.Reader.TryRead(out _))
        {
        }
    }
}
