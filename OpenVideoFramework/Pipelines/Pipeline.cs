using OpenVideoFramework.Pipelines.Builder;

namespace OpenVideoFramework.Pipelines;

public class Pipeline
{
    private readonly List<IPipelineElement> _elements;
    private readonly PipelineContext _context;

    internal Pipeline(List<IPipelineElement> elements, PipelineContext context)
    {
        _elements = elements;
        _context = context;
    }

    public TElement? GetElement<TElement>()
    {
        var element = _elements.Select(e => e.GetUnderlyingElement())
            .FirstOrDefault(e => e is TElement);

        return (TElement?)element;
    }

    public async Task<(Task, CancellationTokenSource)> RunAsync()
    {
        var cts = new CancellationTokenSource();

        var preparationTasks = _elements.ConvertAll(e => e.PrepareForExecutionAsync(_context, cts.Token));
        await Task.WhenAll(preparationTasks).ConfigureAwait(false);

        var executionTasks = _elements.ConvertAll(async e =>
        {
            try
            {
                await e.ExecuteAsync(cts.Token);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == cts.Token)
            {
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
            {
            }
            catch (Exception)
            {
                if (!cts.IsCancellationRequested)
                {
                    await cts.CancelAsync();
                }

                throw;
            }
            finally
            {
                if (e is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        });

        var executionTask = await Task.Factory.StartNew(async () =>
        {
            try
            {
                await Task.WhenAll(executionTasks);
            }
            catch
            {
                var exceptions = executionTasks
                    .Where(t => t.Exception is not null)
                    .SelectMany(x => x.Exception!.InnerExceptions);
                
                throw new AggregateException(exceptions);
            }
        }, TaskCreationOptions.LongRunning);

        return (executionTask, cts);
    }
}