using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Pipelines;
using OpenVideoFramework.RtpFrameAssemblerUnit;

namespace OpenVideoFramework.HttpStreamSink;

public class HttpStreamSinkSettings
{
    public int Port { get; init; } = 8080;
    public required string Route { get; init; } = null!;
}

public class HttpStreamSink : IPipelineSink<CompleteFrame>, IDisposable
{
    private readonly IWebHost _host;

    private readonly Channel<CompleteFrame> _frameChannel = Channel.CreateBounded<CompleteFrame>(
        new BoundedChannelOptions(10)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

    private ILogger<HttpStreamSink> _logger = null!;

    public HttpStreamSink(HttpStreamSinkSettings settings)
    {
        _host = WebHost.CreateDefaultBuilder()
            .UseKestrel()
            .UseUrls($"http://*:{settings.Port}")
            .ConfigureLogging(l => l.ClearProviders())
            .Configure(app =>
            {
                app.Run(async context =>
                {
                    if (context.Request.Path == settings.Route)
                    {
                        _logger.LogInformation("Client connected to HTTP stream.");
                        await HandleJpegStream(context);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                    }
                });
            })
            .Build();
    }

    public async Task PrepareForExecutionAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        _logger = context.GetLogger<HttpStreamSink>();
        await _host.StartAsync(cancellationToken);
        
        _logger.LogInformation("HTTP stream prepared.");
    }

    public async Task ConsumeAsync(ChannelReader<CompleteFrame> input, CancellationToken cancellationToken)
    {
        await foreach (var frame in input.ReadAllAsync(cancellationToken))
        {
            await _frameChannel.Writer.WriteAsync(frame, cancellationToken);
        }
    }

    private async Task HandleJpegStream(HttpContext context)
    {
        context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
        context.Response.Headers.Add("Cache-Control", "no-cache");
        context.Response.Headers.Add("Connection", "keep-alive");

        try
        {
            await foreach (var frame in _frameChannel.Reader.ReadAllAsync(context.RequestAborted))
            {
                await WriteMjpegFrame(context.Response, frame.Data, context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client disconnected from HTTP stream.");
            // Client disconnected
        }
    }

    private static async Task WriteMjpegFrame(
        HttpResponse response,
        byte[] jpegData,
        CancellationToken cancellationToken)
    {
        var boundary = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpegData.Length}\r\n\r\n";
        var boundaryBytes = Encoding.UTF8.GetBytes(boundary);
        var endBytes = "\r\n"u8.ToArray();

        await response.Body.WriteAsync(boundaryBytes, cancellationToken);
        await response.Body.WriteAsync(jpegData, cancellationToken);
        await response.Body.WriteAsync(endBytes, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _host.Dispose();
        _frameChannel.Writer.Complete();
        GC.SuppressFinalize(this);
    }
}