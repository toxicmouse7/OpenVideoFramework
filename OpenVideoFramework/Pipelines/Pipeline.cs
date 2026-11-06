using System.Runtime.ExceptionServices;
using OpenVideoFramework.Pipelines.Builder;

namespace OpenVideoFramework.Pipelines;

public class Pipeline
{
    private readonly List<IPipelineElement> _elements;
    private readonly PipelineContext _context;
    private readonly Func<CancellationToken, Task> _produce;
    private int _started;

    internal Pipeline(List<IPipelineElement> elements, PipelineContext context, Func<CancellationToken, Task> produce)
    {
        _elements = elements;
        _context = context;
        _produce = produce;
    }

    public TElement? GetElement<TElement>() =>
        (TElement?)_elements.Select(e => e.GetUnderlyingElement()).FirstOrDefault(e => e is TElement);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("A pipeline can only be run once.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? failure = null;
        var workers = new List<Task>();

        async Task CancelAsync()
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref failure, ex, null);
            }
        }

        async Task Supervise(Func<CancellationToken, Task> execute)
        {
            try
            {
                await execute(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException || !cts.IsCancellationRequested)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }

                await CancelAsync().ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            foreach (var element in _elements.AsEnumerable().Reverse())
            {
                cts.Token.ThrowIfCancellationRequested();
                await element.PrepareForExecutionAsync(_context, cts.Token).ConfigureAwait(false);
            }

            foreach (var buffer in _elements.OfType<IBufferElement>())
            {
                workers.Add(Supervise(buffer.ExecuteAsync));
            }

            workers.Add(Supervise(_produce));
            await Task.WhenAll(workers).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref failure, ex, null);
            await CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch
            {
                /* All workers are observed before resources are released. */
            }
        }
        finally
        {
            await CancelAsync().ConfigureAwait(false);
            var resources = _elements.AsEnumerable().Reverse()
                .SelectMany(e => new[] { e.GetUnderlyingElement(), e }).OfType<object>()
                .Distinct(ReferenceEqualityComparer.Instance);
            foreach (var resource in resources)
            {
                try
                {
                    if (resource is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else if (resource is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    failure ??= ex;
                }
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
