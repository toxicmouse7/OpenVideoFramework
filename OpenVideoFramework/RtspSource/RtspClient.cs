using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using OpenVideoFramework.RtspSource.Rtp;
using OpenVideoFramework.RtspSource.Rtp.Mjpeg;

namespace OpenVideoFramework.RtspSource;

public partial class RtspClient
{
    private readonly TcpClient _rtspClient;
    private readonly UdpClient _rtpClient;
    private readonly UdpClient _rtcpClient;

    private readonly Uri _url;
    private readonly byte[] _rawBuffer;
    private readonly string _username;
    private readonly string _password;

    private Uri? _mediaUri;
    private int _sequence;
    private string? _sessionId;
    private string _digestNonce;
    private string _digestRealm;

    public RtspClient(string url)
    {
        _url = new Uri(url);
        _rtspClient = new TcpClient();
        _rtcpClient = new UdpClient(0);
        _rtpClient = new UdpClient(0);
        _rawBuffer = new byte[4096];

        _username = _url.UserInfo.Length == 0 ? string.Empty : _url.UserInfo.Split(':')[0];
        _password = _url.UserInfo.Length == 0 ? string.Empty : _url.UserInfo.Split(':')[1];
        _url = new Uri(url.Replace($"{_url.UserInfo}@", string.Empty));
    }

    private async Task SendRequestAsync(
        NetworkStream stream,
        string method,
        Uri uri,
        params string[] headers)
    {
        var request = new StringBuilder();
        request.AppendLine($"{method} {uri} RTSP/1.0");
        request.AppendLine($"CSeq: {_sequence}");
        request.AppendLine("User-Agent: C# RTSP Client");

        foreach (var header in headers)
        {
            request.AppendLine(header);
        }

        if (!string.IsNullOrEmpty(_sessionId))
            request.AppendLine($"Session: {_sessionId}");

        request.AppendLine();

        var bytes = Encoding.ASCII.GetBytes(request.ToString());
        await stream.WriteAsync(bytes);

        _sequence++;
    }

    private async Task<RtspResponse> ReadResponseAsync(NetworkStream stream)
    {
        var bytesRead = await stream.ReadAsync(_rawBuffer);
        var responseStr = Encoding.ASCII.GetString(_rawBuffer, 0, bytesRead);

        var response = new RtspResponse();
        var lines = responseStr.Split(["\r\n"], StringSplitOptions.None);

        var statusLine = lines[0].Split(' ');
        response.StatusCode = int.Parse(statusLine[1]);

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i]))
            {
                response.Body = string.Join("\r\n", lines.Skip(i + 1));
                break;
            }

            var parts = lines[i].Split([": "], 2, StringSplitOptions.None);
            if (parts.Length == 2)
                response.Headers[parts[0]] = parts[1];
        }

        return response;
    }

    private void ParseAuthHeaders(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("WWW-Authenticate", out var authHeader)) return;
        if (!authHeader.Contains("Digest")) return;

        var realmMatch = RealmRegex().Match(authHeader);
        var nonceMatch = NonceRegex().Match(authHeader);

        if (realmMatch.Success) _digestRealm = realmMatch.Groups[1].Value;
        if (nonceMatch.Success) _digestNonce = nonceMatch.Groups[1].Value;
    }

    private string CreateAuthHeader(string method, string uri)
    {
        if (!string.IsNullOrEmpty(_digestNonce))
        {
            // Digest auth
            var ha1 = ComputeMd5Hash($"{_username}:{_digestRealm}:{_password}");
            var ha2 = ComputeMd5Hash($"{method}:{uri}");
            var response = ComputeMd5Hash($"{ha1}:{_digestNonce}:{ha2}");

            return $"Authorization: Digest username=\"{_username}\", realm=\"{_digestRealm}\", "
                   + $"nonce=\"{_digestNonce}\", uri=\"{uri}\", response=\"{response}\"";
        }

        // Basic auth
        var authString = $"{_username}:{_password}";
        var base64Auth = Convert.ToBase64String(Encoding.ASCII.GetBytes(authString));
        return $"Authorization: Basic {base64Auth}";
    }

    private string CreateTransportHeader()
    {
        var rtpPort = ((IPEndPoint)_rtpClient.Client.LocalEndPoint!).Port;
        var rtcpPort = ((IPEndPoint)_rtcpClient.Client.LocalEndPoint!).Port;

        return $"Transport: RTP/AVP/UDP;unicast;client_port={rtpPort}-{rtcpPort}";
    }

    private static string ComputeMd5Hash(string input)
    {
        var inputBytes = Encoding.ASCII.GetBytes(input);
        var hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);

        var sb = new StringBuilder();
        foreach (var b in hashBytes)
            sb.Append(b.ToString("x2"));

        return sb.ToString();
    }

    [GeneratedRegex(@"realm=""([^""]*)""")]
    private static partial Regex RealmRegex();

    [GeneratedRegex(@"nonce=""([^""]*)""")]
    private static partial Regex NonceRegex();


    public async Task ConnectAsync(CancellationToken token)
    {
        await _rtspClient.ConnectAsync(_url.Host, _url.Port, token);
        var stream = _rtspClient.GetStream();
        await SendRequestAsync(stream, "OPTIONS", _url);
        var optionsResponse = await ReadResponseAsync(stream);

        await SendRequestAsync(stream, "DESCRIBE", _url, "Accept: application/sdp");
        var describeResponse = await ReadResponseAsync(stream);

        if (describeResponse.StatusCode == 401)
        {
            ParseAuthHeaders(describeResponse.Headers);

            await SendRequestAsync(stream, "DESCRIBE", _url, CreateAuthHeader("DESCRIBE", _url.PathAndQuery));
            describeResponse = await ReadResponseAsync(stream);

            _mediaUri = new Uri(describeResponse
                .Body
                .Split("\r\n")
                .First(x => x.StartsWith("a=control:rtsp://"))
                .Replace("a=control:", string.Empty));
        }

        await SendRequestAsync(stream, "SETUP", _mediaUri ?? _url,
            CreateAuthHeader("SETUP", _url.PathAndQuery),
            CreateTransportHeader());

        var setupResponse = await ReadResponseAsync(stream);
        _sessionId = setupResponse.Headers.First(x => x.Key == "Session").Value.Split(';')[0];
    }

    public async Task ReceiveAsync(
        Func<RtpPacket, Task> onRtpPacketReceived,
        CancellationToken cancellationToken)
    {
        var stream = _rtspClient.GetStream();

        await SendRequestAsync(stream, "PLAY", _mediaUri ?? _url, CreateAuthHeader("PLAY", _url.PathAndQuery));
        var playResponse = await ReadResponseAsync(stream);

        if (playResponse.StatusCode != 200)
        {
            return;
        }

        var rtpReceiveTask = Task.Factory.StartNew(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var rtpPacket = await ReceiveRtpAsync(cancellationToken);
                await onRtpPacketReceived(rtpPacket);
            }
        }, TaskCreationOptions.LongRunning);

        var rtcpReceiveTask = Task.Factory.StartNew(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var udpPacket = await _rtcpClient.ReceiveAsync(cancellationToken);
            }
        }, TaskCreationOptions.LongRunning);

        await Task.WhenAny(rtpReceiveTask, rtcpReceiveTask);
    }

    public async Task<RtpPacket> ReceiveRtpAsync(CancellationToken cancellationToken)
    {
        var udpPacket = await _rtpClient.ReceiveAsync(cancellationToken);

        var rtpHeader = RtpPacketHeader.Deserialize(udpPacket.Buffer);

        if (rtpHeader.PayloadType == (uint)PayloadType.MJPEG)
        {
            var mjpegHeader = RtpMjpegHeader.Deserialize(udpPacket.Buffer);
            return new RtpMjpegPacket
            {
                Header = rtpHeader,
                MjpegHeader = mjpegHeader
            };
        }

        return new RtpPacket();
    }
}