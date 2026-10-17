using System.Text;
using System.Text.Json;
using FynityConsoleDemo.Models;
using Microsoft.Extensions.Options;

namespace FynityConsoleDemo.Services;

public class FynityOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string DownloadPath { get; set; } = string.Empty;
    public string IssuanceStatusPath { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyHeaderName { get; set; } = "api_key";
}

public interface IFynityApiClient
{
    Task<(byte[] fileBytes, string rawResponseIfNotBinary)> DownloadReportAsync(DownloadRequestDto request, CancellationToken ct);
    Task<string> GetIssuanceStatusAsync(string payloadJson, CancellationToken ct);
}

/// <summary>
/// Talks to the real uatapi-ipbb.fynity.in endpoints: encrypts the outgoing
/// payload, calls the API, decrypts whatever comes back.
/// </summary>
public class FynityApiClient : IFynityApiClient
{
    private readonly HttpClient _http;
    private readonly IAesEncryptionService _crypto;
    private readonly FynityOptions _options;
    private readonly ILogger<FynityApiClient> _logger;

    public FynityApiClient(
        HttpClient http,
        IAesEncryptionService crypto,
        IOptions<FynityOptions> options,
        ILogger<FynityApiClient> logger)
    {
        _http = http;
        _crypto = crypto;
        _options = options.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.ApiKey) &&
            !_http.DefaultRequestHeaders.Contains(_options.ApiKeyHeaderName))
        {
            _http.DefaultRequestHeaders.Add(_options.ApiKeyHeaderName, _options.ApiKey);
        }
    }

    public async Task<(byte[] fileBytes, string rawResponseIfNotBinary)> DownloadReportAsync(
        DownloadRequestDto request, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(new
        {
            source_id = request.SourceId,
            source_type = request.SourceType,
            from = request.From,
            to = request.To,
            type = request.Type
        });

        var encryptedPayload = _crypto.Encrypt(payloadJson);

        // IMPORTANT: the encrypted payload contains '+', '/', '=' which are not
        // URL-safe. Your first sample call passed raw JSON unencoded in the query
        // string (which is why you got "Unable to decrypt payload") and the
        // second sample shows the ciphertext as-is, which — if not URL-encoded —
        // can get mangled in transit (e.g. '+' turning into a space). We URL-encode
        // it properly here via UriBuilder/query string.
        var url = $"{_options.BaseUrl}{_options.DownloadPath}?payload={Uri.EscapeDataString(encryptedPayload)}";

        _logger.LogInformation("GET {Url}", url);

        using var response = await _http.GetAsync(url, ct);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var rawBytes = await response.Content.ReadAsByteArrayAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = Encoding.UTF8.GetString(rawBytes);
            throw new HttpRequestException($"Download call failed ({(int)response.StatusCode}): {errorText}");
        }

        // Case A: server returned JSON like {"message": "..."} (error, or a
        // wrapped encrypted_response) rather than a binary file.
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var text = Encoding.UTF8.GetString(rawBytes);
            try
            {
                var envelope = JsonSerializer.Deserialize<EncryptedResponseEnvelope>(text);
                if (!string.IsNullOrEmpty(envelope?.encrypted_response))
                {
                    var decryptedBytes = _crypto.DecryptToBytes(envelope.encrypted_response);
                    return (await ExtractFileAsync(decryptedBytes, ct), string.Empty);
                }
            }
            catch (JsonException)
            {
                // fall through — not the envelope shape, just return the raw text (e.g. an error message)
            }
            return (Array.Empty<byte>(), text);
        }

        // Case B: server returned the encrypted body as Base64 text.
        var asBase64Text = Encoding.UTF8.GetString(rawBytes).Trim();
        var decrypted = _crypto.DecryptToBytes(asBase64Text);
        return (await ExtractFileAsync(decrypted, ct), string.Empty);
    }

    /// <summary>
    /// The decrypted response is NOT the xlsx itself — it's JSON:
    ///   { "data": { "link": "https://...&signature=...", "content": "&lt;base64 xlsx&gt;" } }
    /// Returning those bytes as-is is what produced an "excel" file containing
    /// the API response. Instead, pull out the real file: decode "content"
    /// (same file the "link" downloads), or fall back to fetching "link".
    /// </summary>
    private async Task<byte[]> ExtractFileAsync(byte[] decryptedBytes, CancellationToken ct)
    {
        // Already a real xlsx (ZIP signature "PK")? Nothing to unwrap.
        if (decryptedBytes.Length > 1 && decryptedBytes[0] == 0x50 && decryptedBytes[1] == 0x4B)
            return decryptedBytes;

        var text = Encoding.UTF8.GetString(decryptedBytes);

        // The decrypted text is the partner's whole HTTP response — status line and
        // headers ("HTTP/1.0 200 OK", "Cache-Control: ...", blank line) followed by the
        // JSON body — so it starts with 'H', not '{'. Skip ahead to the JSON body.
        var jsonStart = text.IndexOf('{');
        if (jsonStart < 0)
            throw new InvalidOperationException($"Decrypted download response contains no JSON body: {text}");
        var json = text[jsonStart..];

        var parsed = JsonSerializer.Deserialize<DownloadDecryptedResponse>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var contentBase64 = parsed?.Data?.Content;
        if (!string.IsNullOrEmpty(contentBase64))
            return Convert.FromBase64String(contentBase64);

        var link = parsed?.Data?.Link;
        if (!string.IsNullOrEmpty(link))
        {
            _logger.LogInformation("No inline content; fetching pre-signed link {Link}", link);
            return await _http.GetByteArrayAsync(link, ct);
        }

        throw new InvalidOperationException($"Decrypted download response had neither data.content nor data.link: {json}");
    }

    public async Task<string> GetIssuanceStatusAsync(string payloadJson, CancellationToken ct)
    {
        var encryptedPayload = _crypto.Encrypt(payloadJson);

        var requestBody = JsonSerializer.Serialize(new IssuanceStatusEnvelope { Payload = encryptedPayload });
        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var url = $"{_options.BaseUrl}{_options.IssuanceStatusPath}";
        _logger.LogInformation("POST {Url}", url);

        using var response = await _http.PostAsync(url, content, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"issuance-status call failed ({(int)response.StatusCode}): {responseText}");

        var envelope = JsonSerializer.Deserialize<EncryptedResponseEnvelope>(responseText)
            ?? throw new InvalidOperationException($"Unexpected response shape: {responseText}");

        return _crypto.Decrypt(envelope.encrypted_response);
    }

    private byte[] DecryptRawBytes(byte[] cipherBytes)
    {
        // Fallback path for when the ciphertext arrives as raw bytes rather
        // than Base64 text. Re-implemented here (rather than reusing
        // DecryptToBytes, which expects Base64) in case that's what your real
        // API does for the binary xlsx response.
        using var aes = System.Security.Cryptography.Aes.Create();
        // The partner's scheme is base64(nonce + tag + ciphertext) text, so this
        // path shouldn't normally trigger. Kept as a safety net in case the
        // download endpoint ever returns the GCM wire bytes without Base64 text-encoding.
        throw new NotSupportedException(
            "Raw (non-Base64) ciphertext bytes encountered for the download response. " +
            "Add a DecryptToBytes overload that accepts byte[] directly (skipping the Base64 decode) and call it here.");
    }
}
