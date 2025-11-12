using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.Pipelines;

namespace OpenVideoFramework.HttpStreamSink;

/// <summary>
/// Streams video frames as MJPEG over HTTP for real-time web viewing.
/// Creates a web server that serves a live video stream accessible via web browsers.
/// </summary>
public class HttpStreamSink : IPipelineSink<VideoFrame>, IDisposable
{
    private readonly IWebHost _host;
    private readonly List<Channel<VideoFrame>> _channels;
    private readonly SemaphoreSlim _channelsSemaphore;
    private CancellationToken _cancellationToken = CancellationToken.None;

    private ILogger<HttpStreamSink> _logger = null!;

    public HttpStreamSink(HttpStreamSinkSettings settings)
    {
        _channels = [];
        _channelsSemaphore = new SemaphoreSlim(1, 1);
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
        _cancellationToken = cancellationToken;
        await _host.StartAsync(cancellationToken);
        
        _logger.LogInformation("HTTP stream prepared.");
    }

    public async Task ConsumeAsync(ChannelReader<VideoFrame> input, CancellationToken cancellationToken)
    {
        await foreach (var frame in input.ReadAllAsync(cancellationToken))
        {
            if (frame.Codec != Codec.MJPEG)
            {
                throw new ArgumentException($"Only JPEG streams are supported. Received codec: {frame.Codec}");
            }
            
            await _channelsSemaphore.WaitAsync(cancellationToken);
            foreach (var clientChannel in _channels)
            {
                await clientChannel.Writer.WriteAsync(frame, cancellationToken);
            }
            _channelsSemaphore.Release();
            
            await Task.Delay(frame.Duration, cancellationToken);
        }
    }

    private async Task HandleJpegStream(HttpContext context)
    {
        var channel = Channel.CreateUnbounded<VideoFrame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        
        using var linkedCancellationTokenSource = CancellationTokenSource
            .CreateLinkedTokenSource(_cancellationToken, context.RequestAborted);
        
        await _channelsSemaphore.WaitAsync(linkedCancellationTokenSource.Token);
        _channels.Add(channel);
        _channelsSemaphore.Release();
        
        context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
        context.Response.Headers.Append("Cache-Control", "no-cache");
        context.Response.Headers.Append("Connection", "keep-alive");

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(linkedCancellationTokenSource.Token))
            {
                await WriteMjpegFrame(context.Response, frame.Data, linkedCancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client disconnected from HTTP stream.");
            await _channelsSemaphore.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
            _channels.Remove(channel);
            channel.Writer.Complete();
            _channelsSemaphore.Release();
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
        GC.SuppressFinalize(this);
    }
}