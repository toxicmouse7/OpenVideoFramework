using OpenVideoFramework.Pipelines.Builder;

namespace OpenVideoFramework.Pipelines;

public class Pipeline
{
    private readonly List<IPipelineElement> _elements;

    internal Pipeline(List<IPipelineElement> elements)
    {
        _elements = elements;
    }

    public async Task<(Task, CancellationTokenSource)> RunAsync()
    {
        var cts = new CancellationTokenSource();
        
        await Task.WhenAll(_elements.ConvertAll(e => e.PrepareForExecutionAsync(cts.Token)));
        
        var tasks = _elements.ConvertAll(e => e.ExecuteAsync(cts.Token));
        var combinedTask = Task.WhenAll(tasks);

        return (combinedTask, cts);
    }
}