using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoldenHour.Api.Application;
using Microsoft.Extensions.Options;

namespace GoldenHour.Api.Infrastructure;

public sealed class SmsGatewayOptions
{
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string SigningSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
    public int MaximumResponseBytes { get; set; } = 65_536;
}

public sealed class NotificationProviderException(string message) : Exception(message);

public sealed partial class HmacSmsGatewayProvider(
    HttpClient httpClient,
    IOptions<SmsGatewayOptions> options,
    IClock clock) : ISmsProvider
{
    private static readonly JsonSerializerOptions GatewayJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public async Task<ProviderDelivery> SendAsync(
        string destination,
        string templateCode,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (!configured.Enabled
            || !Uri.TryCreate(configured.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(configured.SigningSecret)
            || Encoding.UTF8.GetByteCount(configured.SigningSecret) < 32)
            throw new NotificationProviderException("The SMS gateway is not configured safely.");
        if (string.IsNullOrWhiteSpace(destination) || destination.Length > 40
            || string.IsNullOrWhiteSpace(templateCode) || !TemplateCodePattern().IsMatch(templateCode)
            || values.Count is 0 or > 10
            || values.Any(value => !ValueKeyPattern().IsMatch(value.Key) || value.Value is null || value.Value.Length > 500))
            throw new InvalidDataException("The SMS request does not satisfy the bounded template contract.");

        var timestamp = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var requestId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            requestId,
            destination,
            templateCode,
            values
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var signed = new byte[Encoding.UTF8.GetByteCount(timestamp) + 1 + Encoding.UTF8.GetByteCount(requestId) + 1 + body.Length];
        var prefixLength = Encoding.UTF8.GetBytes(timestamp, signed);
        signed[prefixLength] = (byte)'.';
        var requestIdLength = Encoding.UTF8.GetBytes(requestId, signed.AsSpan(prefixLength + 1));
        signed[prefixLength + 1 + requestIdLength] = (byte)'.';
        body.CopyTo(signed.AsSpan(prefixLength + 2 + requestIdLength));
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(configured.SigningSecret), signed)).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-GoldenHour-Timestamp", timestamp);
        request.Headers.Add("X-GoldenHour-Request-Id", requestId);
        request.Headers.Add("X-GoldenHour-Signature", $"sha256={signature}");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new NotificationProviderException("The SMS gateway did not accept the delivery request.");
        if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > configured.MaximumResponseBytes)
            throw new NotificationProviderException("The SMS gateway response exceeded the safe limit.");

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4_096];
        while (true)
        {
            var read = await responseStream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > configured.MaximumResponseBytes)
                throw new NotificationProviderException("The SMS gateway response exceeded the safe limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        GatewayResponse? gateway;
        try
        {
            gateway = JsonSerializer.Deserialize<GatewayResponse>(buffer.ToArray(), GatewayJsonOptions);
        }
        catch (JsonException)
        {
            throw new NotificationProviderException("The SMS gateway returned an invalid response.");
        }
        var status = gateway?.Status?.Trim().ToLowerInvariant();
        if (gateway is null || string.IsNullOrWhiteSpace(gateway.MessageId) || !MessageIdPattern().IsMatch(gateway.MessageId)
            || status is not ("accepted" or "queued"))
            throw new NotificationProviderException("The SMS gateway returned an invalid acceptance response.");
        return new ProviderDelivery(gateway.MessageId, status, false);
    }

    private sealed record GatewayResponse(string MessageId, string Status);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateCodePattern();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValueKeyPattern();

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,159}$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageIdPattern();
}
