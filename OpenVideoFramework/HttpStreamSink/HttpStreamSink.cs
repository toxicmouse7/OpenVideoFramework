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
public class HttpStreamSink : IPipelineSink<MediaSample<EncodedVideoFrame>>, IDisposable, IAsyncDisposable
{
    private readonly IWebHost _host;
    private readonly List<Channel<EncodedVideoFrame>> _channels;
    private readonly object _channelsLock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private bool _stopped;
    private bool _hostStarted;
    private bool _disposed;
    private CancellationToken _cancellationToken = CancellationToken.None;

    private ILogger<HttpStreamSink> _logger = null!;

    public HttpStreamSink(HttpStreamSinkSettings settings)
    {
        _channels = [];

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
        _hostStarted = true;

        _logger.LogInformation("HTTP stream prepared.");
    }

    public ValueTask ConsumeAsync(MediaSample<EncodedVideoFrame> sample, CancellationToken cancellationToken)
    {
        var frame = sample.Frame;
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.Codec != Codec.MJPEG)
        {
            throw new ArgumentException($"Only JPEG streams are supported. Received codec: {frame.Codec}");
        }

        lock (_channelsLock)
        {
            foreach (var channel in _channels)
            {
                channel.Writer.TryWrite(frame);
            }
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        lock (_channelsLock)
        {
            _stopped = true;
            foreach (var channel in _channels)
            {
                channel.Writer.TryComplete();
            }
        }
        await _shutdown.CancelAsync();
        if (_hostStarted)
        {
            await _host.StopAsync(cancellationToken);
            _hostStarted = false;
            _logger.LogInformation("HTTP stream stopped.");
        }
    }

    private async Task HandleJpegStream(HttpContext context)
    {
        var channel = Channel.CreateBounded<EncodedVideoFrame>(new BoundedChannelOptions(2)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationToken,
            _shutdown.Token,
            context.RequestAborted);

        lock (_channelsLock)
        {
            if (_stopped)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            _channels.Add(channel);
        }

        try
        {
            context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
            context.Response.Headers.Append("Cache-Control", "no-cache");
            context.Response.Headers.Append("Connection", "keep-alive");
            await foreach (var frame in channel.Reader.ReadAllAsync(cts.Token))
            {
                await WriteMjpegFrame(context.Response, frame, cts.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogInformation("Client disconnected from HTTP stream.");
        }
        finally
        {
            lock (_channelsLock)
            {
                _channels.Remove(channel);
                channel.Writer.TryComplete();
            }
        }
    }

    private static async Task WriteMjpegFrame(
        HttpResponse response,
        EncodedVideoFrame frame,
        CancellationToken cancellationToken)
    {
        var boundaryBytes = Encoding.UTF8.GetBytes(
            $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Data.Length}\r\n\r\n");
        var endBytes = "\r\n"u8.ToArray();

        await response.Body.WriteAsync(boundaryBytes, cancellationToken);
        await response.Body.WriteAsync(frame.Data, cancellationToken);
        await response.Body.WriteAsync(endBytes, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await CompleteAsync(CancellationToken.None);
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _host.Dispose();
        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }
}
