using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Cngn.Sdk;

internal enum ResponseShape
{
    Object,
    Array
}

internal sealed class CngnTransport : IDisposable
{
    private static readonly string UserAgent =
        $"Cngn.Sdk/{typeof(CngnClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly CngnCrypto _crypto;
    private readonly Uri _baseUri;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;
    private bool _disposed;

    public CngnEnvironment Environment { get; }

    public CngnTransport(CngnClientOptions options, HttpClient? httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        _apiKey = Required(options.ApiKey, nameof(options.ApiKey));
        Environment = _apiKey.StartsWith("cngn_test_", StringComparison.Ordinal) ? CngnEnvironment.Test
            : _apiKey.StartsWith("cngn_live_", StringComparison.Ordinal) ? CngnEnvironment.Live
            : throw new CngnException(CngnErrorKind.Configuration,
                "API key must start with cngn_test_ or cngn_live_");
        if (options.Environment is not null && options.Environment != Environment)
            throw new CngnException(CngnErrorKind.Configuration, "API key and environment do not match");

        Uri baseUri = options.BaseUri ?? throw new CngnException(CngnErrorKind.Configuration, "Base URI is required");
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("https" or "http")
            || (baseUri.Scheme == "http" && !baseUri.IsLoopback))
            throw new CngnException(CngnErrorKind.Configuration, "Base URI must use HTTPS");
        if (!string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new CngnException(CngnErrorKind.Configuration,
                "Base URI cannot contain credentials, query, or fragment");
        _baseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);

        _timeout = options.Timeout;
        _maxRetries = options.MaxRetries;
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(10))
            throw new CngnException(CngnErrorKind.Configuration, "Timeout must be between 1 tick and 10 minutes");
        if (_maxRetries is < 0 or > 10)
            throw new CngnException(CngnErrorKind.Configuration, "MaxRetries must be between 0 and 10");

        _crypto = new CngnCrypto(options.EncryptionKey, options.PrivateKey);
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
    }

    public async Task<T> SendAsync<T>(HttpMethod method, string path, ResponseShape shape,
        IReadOnlyDictionary<string, string>? query = null, object? body = null,
        bool safeToRetry = false, string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        JsonElement? data = await SendCoreAsync(method, path, query, body, safeToRetry,
            idempotencyKey, cancellationToken).ConfigureAwait(false);
        JsonElement value;
        if (data is null || data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            using JsonDocument empty = JsonDocument.Parse(shape == ResponseShape.Array ? "[]" : "{}");
            value = empty.RootElement.Clone();
        }
        else
        {
            value = data.Value;
        }
        if (shape == ResponseShape.Array && value.ValueKind != JsonValueKind.Array
            || shape == ResponseShape.Object && value.ValueKind != JsonValueKind.Object)
            throw new CngnException(CngnErrorKind.Api, "Unexpected API response shape");
        try
        {
            return JsonSerializer.Deserialize<T>(value, CngnJson.Options)
                ?? throw new CngnException(CngnErrorKind.Api, "Empty API response");
        }
        catch (JsonException error)
        {
            throw new CngnException(CngnErrorKind.Api, "Invalid API response data", innerException: error);
        }
    }

    public Task<JsonElement?> SendRawAsync(HttpMethod method, string path,
        object body, CancellationToken cancellationToken = default) =>
        SendCoreAsync(method, path, null, body, false, null, cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _crypto.Dispose();
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    private async Task<JsonElement?> SendCoreAsync(HttpMethod method, string path,
        IReadOnlyDictionary<string, string>? query, object? body, bool safeToRetry,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (idempotencyKey is not null && (idempotencyKey.Length is < 1 or > 256
            || idempotencyKey.Any(character => character is < '!' or > '~')))
            throw new CngnException(CngnErrorKind.Configuration, "Invalid idempotency key");

        Uri uri = BuildUri(path, query);
        string? wireBody = body is null ? null : JsonSerializer.Serialize(_crypto.Encrypt(body), CngnJson.Options);
        bool retryAllowed = safeToRetry || idempotencyKey is not null;

        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(method, uri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.UserAgent.ParseAdd(UserAgent);
                if (idempotencyKey is not null)
                    request.Headers.Add("Idempotency-Key", idempotencyKey);
                if (wireBody is not null)
                    request.Content = new StringContent(wireBody, Encoding.UTF8, "application/json");

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_timeout);
                using HttpResponseMessage response = await _httpClient.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                await response.Content.LoadIntoBufferAsync(10_000_000, timeout.Token).ConfigureAwait(false);
                byte[] responseBytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                using JsonDocument document = ParseEnvelope(responseBytes, response.StatusCode);
                JsonElement envelope = document.RootElement;
                int httpStatus = (int)response.StatusCode;
                int apiStatus = envelope.TryGetProperty("status", out JsonElement statusElement)
                    && statusElement.ValueKind == JsonValueKind.Number && statusElement.TryGetInt32(out int parsed)
                    ? parsed : httpStatus;
                if (!response.IsSuccessStatusCode || apiStatus != 200
                    || statusElement.ValueKind == JsonValueKind.False)
                    throw MapError(httpStatus, apiStatus, envelope);

                if (!envelope.TryGetProperty("data", out JsonElement data)
                    || data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return null;
                return data.ValueKind == JsonValueKind.String
                    ? _crypto.DecryptResponse(data.GetString()!) : data.Clone();
            }
            catch (Exception error) when (error is HttpRequestException or IOException
                || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                var networkError = new CngnException(CngnErrorKind.Network,
                    "cNGN request failed", innerException: error);
                if (!retryAllowed || attempt >= _maxRetries) throw networkError;
                await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (CngnException error) when (retryAllowed && attempt < _maxRetries
                && error.Kind is CngnErrorKind.RateLimit or CngnErrorKind.ServiceUnavailable)
            {
                TimeSpan wait = error.Kind == CngnErrorKind.RateLimit
                    ? TimeSpan.FromSeconds(60) : Backoff(attempt);
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Uri BuildUri(string path, IReadOnlyDictionary<string, string>? query)
    {
        var builder = new UriBuilder(new Uri(_baseUri, path.TrimStart('/')));
        if (query is { Count: > 0 })
            builder.Query = string.Join("&", query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri;
    }

    private static JsonDocument ParseEnvelope(byte[] bytes, HttpStatusCode status)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException error)
        {
            throw new CngnException(status == HttpStatusCode.TooManyRequests ? CngnErrorKind.RateLimit
                : status == HttpStatusCode.Unauthorized ? CngnErrorKind.Authentication
                : status == HttpStatusCode.Forbidden ? CngnErrorKind.Permission
                : (int)status >= 500 ? CngnErrorKind.ServiceUnavailable : CngnErrorKind.Api,
                "Invalid API response", (int)status, innerException: error);
        }
        throw new CngnException(CngnErrorKind.Api, "Invalid API response envelope", (int)status);
    }

    private static CngnException MapError(int httpStatus, int apiStatus, JsonElement envelope)
    {
        string message = envelope.TryGetProperty("message", out JsonElement field)
            && field.ValueKind == JsonValueKind.String
            ? field.GetString()! : $"HTTP {apiStatus}";
        string lower = message.ToLowerInvariant();
        CngnErrorKind kind = httpStatus == 429 || apiStatus == 429 || lower.Contains("too many requests", StringComparison.Ordinal)
            ? CngnErrorKind.RateLimit
            : lower.Contains("ip address", StringComparison.Ordinal)
                && (lower.Contains("whitelist", StringComparison.Ordinal)
                    || lower.Contains("determine", StringComparison.Ordinal)) ? CngnErrorKind.IpAllowlist
            : lower.Contains("permission denied", StringComparison.Ordinal) ? CngnErrorKind.Permission
            : httpStatus == 401 || apiStatus == 401 || lower.Contains("token", StringComparison.Ordinal)
                || lower.Contains("merchant not found", StringComparison.Ordinal)
                || lower.Contains("ssh key found", StringComparison.Ordinal) ? CngnErrorKind.Authentication
            : lower.Contains("missing encryption data", StringComparison.Ordinal) ? CngnErrorKind.Encryption
            : lower.Contains("decryption failed", StringComparison.Ordinal) ? CngnErrorKind.Decryption
            : httpStatus == 404 || apiStatus == 404 || lower.Contains("transaction not found", StringComparison.Ordinal)
                ? CngnErrorKind.NotFound
            : httpStatus == 400 || apiStatus == 400 ? CngnErrorKind.Validation
            : httpStatus >= 500 || apiStatus >= 500 ? CngnErrorKind.ServiceUnavailable
            : CngnErrorKind.Api;
        return new CngnException(kind, message, apiStatus, envelope);
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(1 << attempt, 30));

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new CngnException(CngnErrorKind.Configuration, $"{name} is required")
            : value;
}
