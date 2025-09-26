using System.Threading.Channels;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.Demo;

public class RandomStringSource : IPipelineSource<string>
{
    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ProduceAsync(ChannelWriter<string> output, CancellationToken cancellationToken)
    {
        var counter = 0;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            var s = $"string{counter}";

            output.TryWrite(s);
            ++counter;

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}

public class TransformerUnit : IPipelineUnit<string, string>
{
    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ProcessAsync(ChannelReader<string> reader, ChannelWriter<string> writer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var s = await reader.ReadAsync(cancellationToken);
            writer.TryWrite(s + " - transformed");
        }
    }
}

public class ConsoleSink<T> : IPipelineSink<T>
{
    private readonly Guid _id = Guid.NewGuid();

    public Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task ConsumeAsync(ChannelReader<T> input, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var s = await input.ReadAsync(cancellationToken);
            
            Console.WriteLine($"{_id} -- {s}");
        }
    }
}