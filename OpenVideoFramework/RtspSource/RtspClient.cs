using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using OpenVideoFramework.RtspSource.Rtcp;
using OpenVideoFramework.RtspSource.Rtp;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource;

public partial class RtspClient
{
    private readonly TcpClient _rtspClient;
    private readonly Uri _url;
    private readonly string _username;
    private readonly string _password;

    private int _sequence;
    private string? _sessionId;
    private int? _timeout;
    private string _digestNonce = null!;
    private string _digestRealm = null!;
    
    public RtspClient(string url)
    {
        _url = new Uri(url);
        _rtspClient = new TcpClient();

        _username = _url.UserInfo.Length == 0 ? string.Empty : _url.UserInfo.Split(':')[0];
        _password = _url.UserInfo.Length == 0 ? string.Empty : _url.UserInfo.Split(':')[1];
        _url = new Uri(url.Replace($"{_url.UserInfo}@", string.Empty));
    }

    public async Task ConnectAsync(CancellationToken token)
    {
        await _rtspClient.ConnectAsync(_url.Host, _url.Port, token);
    }

    public async Task OptionsAsync()
    {
        var stream = _rtspClient.GetStream();
        await SendRequestAsync(stream, "OPTIONS", _url);
        await ReadResponseAsync(stream);
    }

    public async Task<IReadOnlyCollection<TrackMetadata>> DescribeAsync()
    {
        var stream = _rtspClient.GetStream();
        await SendRequestAsync(stream, "DESCRIBE", _url, "Accept: application/sdp");
        var describeResponse = await ReadResponseAsync(stream);

        if (describeResponse.StatusCode == 401)
        {
            ParseAuthHeaders(describeResponse.Headers);

            await SendRequestAsync(stream, "DESCRIBE", _url, CreateAuthHeader("DESCRIBE", _url.PathAndQuery));
            describeResponse = await ReadResponseAsync(stream);
        }

        var sdp = SdpParser.Parse(describeResponse.Body);

        return sdp;
    }

    public async Task<TrackReceiver> SetupAsync(TrackMetadata metadata)
    {
        var stream = _rtspClient.GetStream();

        var trackUrl = new Uri($"{_url.Scheme}://{_url.Host}:{_url.Port}{metadata.Prefix}");
        var rtpClient = new RtpClient();
        var rtcpClient = new RtcpClient();

        await SendRequestAsync(stream, "SETUP", trackUrl,
            CreateAuthHeader("SETUP", _url.PathAndQuery),
            CreateTransportHeader(rtpClient, rtcpClient));
            
        var setupResponse = await ReadResponseAsync(stream);
            
        var serverTransport = setupResponse.Headers["Transport"];
        var serverPorts = serverTransport
            .Split(';')
            .First(x => x.StartsWith("server_port="))
            .Replace("server_port=", "");
            
        if (string.IsNullOrEmpty(_sessionId))
        {
            var sessionHeader = setupResponse.Headers.First(x => x.Key == "Session").Value;
            _sessionId = sessionHeader.Split(';')[0];

            if (_timeout is null && TimeoutRegex().IsMatch(sessionHeader))
            {
                _timeout = int.Parse(TimeoutRegex().Match(sessionHeader).Groups[1].Value);
            }
        }
            
        rtcpClient.Connect(IPEndPoint.Parse($"{_url.Host}:{serverPorts.Split('-').Last()}"));

        return new TrackReceiver
        {
            Metadata = metadata,
            RtcpClient = rtcpClient,
            RtpClient = rtpClient
        };
    }

    public async Task PlayAsync(CancellationToken token)
    {
        var stream = _rtspClient.GetStream();
        
        await SendRequestAsync(stream, "PLAY", _url, CreateAuthHeader("PLAY", _url.PathAndQuery));
        await ReadResponseAsync(stream);

        if (_timeout is not null)
        {
            await Task.Factory.StartNew(async () =>
            {
                var timeout = TimeSpan.FromSeconds(_timeout.Value * 0.66);
                while (!token.IsCancellationRequested)
                {
                    await GetParameterAsync();
                    await Task.Delay(timeout, token);
                }
            }, TaskCreationOptions.LongRunning);
        }
    }

    public async Task GetParameterAsync()
    {
        var stream = _rtspClient.GetStream();
        
        await SendRequestAsync(stream, "GET_PARAMETER", _url, CreateAuthHeader("GET_PARAMETER", _url.PathAndQuery));
        await ReadResponseAsync(stream);
    }
    
    private async Task SendRequestAsync(
        NetworkStream stream,
        string method,
        Uri uri,
        params string[] headers)
    {
        var request = new StringBuilder();
        request.Append($"{method} {uri} RTSP/1.0\r\n");
        request.Append($"CSeq: {_sequence}\r\n");
        request.Append("User-Agent: C# RTSP Client\r\n");

        foreach (var header in headers)
        {
            request.Append($"{header}\r\n");
        }

        if (!string.IsNullOrEmpty(_sessionId))
            request.Append($"Session: {_sessionId}\r\n");

        request.Append("\r\n");

        var bytes = Encoding.ASCII.GetBytes(request.ToString());
        await stream.WriteAsync(bytes);

        _sequence++;
    }

    private async Task<RtspResponse> ReadResponseAsync(NetworkStream stream)
    {
        var bytesRead = 0;
        var buffer = new byte[4096];
        
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(bytesRead, buffer.Length - bytesRead));
            if (read == 0)
            {
                break;
            }

            bytesRead += read;
            
            var responseStr = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            if (responseStr.Contains("\r\n\r\n"))
            {
                if (TryGetContentLength(responseStr, out var contentLength))
                {
                    var headersEnd = responseStr.IndexOf("\r\n\r\n", StringComparison.InvariantCulture) + 4;
                    var bodyReceived = bytesRead - headersEnd;

                    if (bodyReceived >= contentLength)
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            
            if (bytesRead == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }
        }

        var finalResponseStr = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        return ParseResponse(finalResponseStr);
    }
    
    private static bool TryGetContentLength(string response, out int length)
    {
        length = 0;
        const string contentLengthHeader = "Content-Length:";
        var startIndex = response.IndexOf(contentLengthHeader, StringComparison.OrdinalIgnoreCase);

        if (startIndex == -1) return false;

        startIndex += contentLengthHeader.Length;
        var endIndex = response.IndexOf("\r\n", startIndex, StringComparison.InvariantCulture);
        if (endIndex == -1) return false;

        var lengthStr = response.Substring(startIndex, endIndex - startIndex).Trim();
        return int.TryParse(lengthStr, out length);
    }

    private RtspResponse ParseResponse(string responseStr)
    {
        var response = new RtspResponse();
        var lines = responseStr.Split(["\r\n"], StringSplitOptions.None);
        
        var statusLine = lines[0].Split(' ');
        if (statusLine.Length >= 2)
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

    private static string CreateTransportHeader(RtpClient rtpClient, RtcpClient rtcpClient)
    {
        return $"Transport: RTP/AVP/UDP;unicast;client_port={rtpClient.Port}-{rtcpClient.Port}";
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

    [GeneratedRegex("""
                    realm="([^"]*)"
                    """)]
    private static partial Regex RealmRegex();

    [GeneratedRegex("""
                    nonce="([^"]*)"
                    """)]
    private static partial Regex NonceRegex();

    [GeneratedRegex("""
                    timeout=(\d+)
                    """)]
    private static partial Regex TimeoutRegex();
}