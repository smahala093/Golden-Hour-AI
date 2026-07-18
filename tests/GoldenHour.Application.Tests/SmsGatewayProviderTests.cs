using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GoldenHour.Api.Application;
using GoldenHour.Api.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace GoldenHour.Application.Tests;

public sealed class SmsGatewayProviderTests
{
    private const string SigningSecret = "sms-gateway-test-signing-secret-32-bytes-minimum";

    [Fact]
    public async Task SendAsync_UsesHttpsHmacAndReturnsAcceptanceWithoutDeliveryClaim()
    {
        var handler = new CaptureHandler("""{"messageId":"provider-message-1","status":"accepted"}""");
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var clock = new FixedClock(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc));
        var provider = new HmacSmsGatewayProvider(client, Options.Create(ValidOptions()), clock);

        var result = await provider.SendAsync(
            "+91 90000 10001",
            "contact-verification-code",
            new Dictionary<string, string> { ["code"] = "123456" },
            CancellationToken.None);

        handler.RequestUri.Should().Be(new Uri("https://sms-gateway.example.test/v1/messages"));
        handler.ContentType.Should().Be("application/json");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("destination").GetString().Should().Be("+91 90000 10001");
        body.RootElement.GetProperty("templateCode").GetString().Should().Be("contact-verification-code");
        body.RootElement.GetProperty("values").GetProperty("code").GetString().Should().Be("123456");
        body.RootElement.GetProperty("requestId").GetString().Should().Be(handler.RequestId);
        handler.RequestId.Should().MatchRegex("^[a-f0-9]{32}$");
        var expectedSigned = Encoding.UTF8.GetBytes($"{handler.Timestamp}.{handler.RequestId}.{handler.Body}");
        var expectedSignature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(SigningSecret), expectedSigned)).ToLowerInvariant();
        handler.Signature.Should().Be($"sha256={expectedSignature}");
        result.Should().Be(new ProviderDelivery("provider-message-1", "accepted", false));
    }

    [Fact]
    public async Task SendAsync_RejectsNonHttpsConfigurationBeforeNetworkUse()
    {
        var handler = new CaptureHandler("""{"messageId":"unused","status":"accepted"}""");
        using var client = new HttpClient(handler);
        var options = ValidOptions();
        options.Endpoint = "http://sms-gateway.example.test/v1/messages";
        var provider = new HmacSmsGatewayProvider(client, Options.Create(options), new FixedClock(DateTime.UtcNow));

        var act = () => provider.SendAsync("+919000010001", "contact-verification-code", new Dictionary<string, string> { ["code"] = "123456" }, CancellationToken.None);

        await act.Should().ThrowAsync<NotificationProviderException>().WithMessage("*not configured safely*");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_RejectsUnboundedOrUnrecognizedGatewayResponse()
    {
        var handler = new CaptureHandler(new string('x', 2_000));
        using var client = new HttpClient(handler);
        var options = ValidOptions();
        options.MaximumResponseBytes = 1_024;
        var provider = new HmacSmsGatewayProvider(client, Options.Create(options), new FixedClock(DateTime.UtcNow));

        var act = () => provider.SendAsync("+919000010001", "contact-verification-code", new Dictionary<string, string> { ["code"] = "123456" }, CancellationToken.None);

        await act.Should().ThrowAsync<NotificationProviderException>().WithMessage("*safe limit*");
    }

    [Theory]
    [InlineData("{\"messageId\":\"provider-message-1\",\"status\":\"accepted\",\"unexpected\":true}")]
    [InlineData("{\"messageId\":\"provider message 1\",\"status\":\"accepted\"}")]
    [InlineData("{\"messageId\":\"provider-message-1\",\"status\":\"delivered\"}")]
    public async Task SendAsync_RejectsNonStrictAcceptanceResponse(string responseBody)
    {
        var handler = new CaptureHandler(responseBody);
        using var client = new HttpClient(handler);
        var provider = new HmacSmsGatewayProvider(client, Options.Create(ValidOptions()), new FixedClock(DateTime.UtcNow));

        var act = () => provider.SendAsync("+919000010001", "contact-verification-code", new Dictionary<string, string> { ["code"] = "123456" }, CancellationToken.None);

        await act.Should().ThrowAsync<NotificationProviderException>().WithMessage("*invalid*");
    }

    private static SmsGatewayOptions ValidOptions() => new()
    {
        Enabled = true,
        Endpoint = "https://sms-gateway.example.test/v1/messages",
        SigningSecret = SigningSecret,
        TimeoutSeconds = 5,
        MaximumResponseBytes = 65_536
    };

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    private sealed class CaptureHandler(string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public string? Timestamp { get; private set; }
        public string? RequestId { get; private set; }
        public string? Signature { get; private set; }
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Timestamp = request.Headers.GetValues("X-GoldenHour-Timestamp").Single();
            RequestId = request.Headers.GetValues("X-GoldenHour-Request-Id").Single();
            Signature = request.Headers.GetValues("X-GoldenHour-Signature").Single();
            ContentType = request.Content.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
