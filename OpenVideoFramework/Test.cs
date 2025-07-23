using System.Threading.Channels;

namespace OpenVideoFramework;

public class RandomStringSource : IPipelineSource<string>
{
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
    public async Task ProcessAsync(ChannelReader<string> reader, ChannelWriter<string> writer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var s = await reader.ReadAsync(cancellationToken);
            writer.TryWrite(s + " - transformed");
        }
    }
}

public class ConsoleSink : IPipelineSink<string>
{
    public async Task ConsumeAsync(ChannelReader<string> input, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var s = await input.ReadAsync(cancellationToken);
            
            Console.WriteLine(s);
        }
    }
}