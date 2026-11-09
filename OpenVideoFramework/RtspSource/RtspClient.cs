using System.Text;
using System.Text.RegularExpressions;
using OpenVideoFramework.RtspSource.SDP;

namespace OpenVideoFramework.RtspSource;

internal partial class RtspClient
{
    private readonly RtspTransport _transport;
    private readonly Uri _url;
    private Uri _controlBaseUrl;
    private readonly string _username;
    private readonly string _password;
    private int _sequence;
    private string? _sessionId;
    private int? _timeout;
    private string _digestNonce = null!;
    private string _digestRealm = null!;

    public RtspClient(string url, RtspTransport transport)
    {
        _url = new Uri(url);
        _transport = transport;
        _username = _url.UserInfo.Length == 0 ? string.Empty : _url.UserInfo.Split(':')[0];
        _password = _url.UserInfo.Length == 0 ? string.Empty : _url.UserInfo.Split(':')[1];
        _url = new Uri(url.Replace($"{_url.UserInfo}@", string.Empty));
        _controlBaseUrl = _url;
    }

    public Task ConnectAsync(CancellationToken token) => _transport.ConnectAsync(_url, token);

    public async Task OptionsAsync(CancellationToken token = default) =>
        EnsureSuccess(await SendRequestAsync("OPTIONS", _url, token).ConfigureAwait(false));

    public async Task<IReadOnlyCollection<TrackMetadata>> DescribeAsync(CancellationToken token = default)
    {
        var response = await SendRequestAsync("DESCRIBE", _url, token, "Accept: application/sdp").ConfigureAwait(false);
        if (response.StatusCode == 401)
        {
            ParseAuthHeaders(response.Headers);
            response = await SendRequestAsync("DESCRIBE", _url, token,
                "Accept: application/sdp", CreateAuthHeader("DESCRIBE", _url.PathAndQuery)).ConfigureAwait(false);
        }

        EnsureSuccess(response);
        _controlBaseUrl = response.Headers.TryGetValue("Content-Base", out var contentBase)
            ? new Uri(_url, contentBase)
            : response.Headers.TryGetValue("Content-Location", out var contentLocation)
                ? new Uri(_url, contentLocation)
                : _url;
        return SdpParser.Parse(response.Body);
    }

    public async Task<TrackReceiver> SetupAsync(TrackMetadata metadata, CancellationToken token = default)
    {
        var trackUrl = new Uri(_controlBaseUrl, metadata.Prefix == "*" ? string.Empty : metadata.Prefix);
        var track = _transport.CreateTrack(metadata);
        var response = await SendRequestAsync("SETUP", trackUrl, token,
            CreateAuthHeader("SETUP", trackUrl.AbsoluteUri), _transport.GetTransportHeader(track)).ConfigureAwait(false);
        EnsureSuccess(response);
        _transport.ConfigureTrack(track, response.Headers["Transport"]);

        if (string.IsNullOrEmpty(_sessionId))
        {
            var sessionHeader = response.Headers["Session"];
            _sessionId = sessionHeader.Split(';')[0];
            var timeout = TimeoutRegex().Match(sessionHeader);
            if (timeout.Success)
            {
                _timeout = int.Parse(timeout.Groups[1].Value);
            }
        }

        return track;
    }

    public async Task PlayAsync(CancellationToken token) =>
        EnsureSuccess(await SendRequestAsync("PLAY", _url, token,
            CreateAuthHeader("PLAY", _url.PathAndQuery)).ConfigureAwait(false));

    public async Task KeepAliveAsync(CancellationToken token)
    {
        if (_timeout is null)
        {
            return;
        }

        var timeout = TimeSpan.FromSeconds(_timeout.Value * 0.66);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await GetParameterAsync(token).ConfigureAwait(false);
            await Task.Delay(timeout, token).ConfigureAwait(false);
        }
    }

    public async Task GetParameterAsync(CancellationToken token = default) =>
        EnsureSuccess(await SendRequestAsync("GET_PARAMETER", _url, token,
            CreateAuthHeader("GET_PARAMETER", _url.PathAndQuery)).ConfigureAwait(false));

    private Task<RtspResponse> SendRequestAsync(string method, Uri uri, CancellationToken token, params string[] headers)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var request = new StringBuilder();
        request.Append($"{method} {uri} RTSP/1.0\r\n");
        request.Append($"CSeq: {sequence}\r\n");
        request.Append("User-Agent: C# RTSP Client\r\n");
        foreach (var header in headers)
        { request.Append($"{header}\r\n"); }
        if (!string.IsNullOrEmpty(_sessionId))
        { request.Append($"Session: {_sessionId}\r\n"); }
        request.Append("\r\n");
        return _transport.SendRequestAsync(request.ToString(), sequence, token);
    }

    private static void EnsureSuccess(RtspResponse response)
    {
        if (response.StatusCode is < 200 or >= 300)
        {
            throw new InvalidOperationException($"RTSP request failed with status {response.StatusCode}.");
        }
    }

    private void ParseAuthHeaders(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("WWW-Authenticate", out var authHeader))
        {
            return;
        }

        if (!authHeader.Contains("Digest"))
        {
            return;
        }

        var realmMatch = RealmRegex().Match(authHeader);
        var nonceMatch = NonceRegex().Match(authHeader);

        if (realmMatch.Success)
        {
            _digestRealm = realmMatch.Groups[1].Value;
        }

        if (nonceMatch.Success)
        {
            _digestNonce = nonceMatch.Groups[1].Value;
        }
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

    private static string ComputeMd5Hash(string input)
    {
        var inputBytes = Encoding.ASCII.GetBytes(input);
        var hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);

        var sb = new StringBuilder();
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }

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
