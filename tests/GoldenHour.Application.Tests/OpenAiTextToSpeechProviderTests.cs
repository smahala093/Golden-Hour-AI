using System.Net;
using System.Text.Json;
using FluentAssertions;
using GoldenHour.Api.Domain;
using GoldenHour.Api.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

namespace GoldenHour.Application.Tests;

public sealed class OpenAiTextToSpeechProviderTests
{
    [Fact]
    public async Task SynthesizeAsync_SendsOnlyApprovedTextWithBoundedSpeechContract()
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test/v1/") };
        var provider = new OpenAiTextToSpeechProvider(client, Options.Create(new OpenAiOptions
        {
            ApiKey = "test-key",
            TextToSpeechModel = "gpt-4o-mini-tts",
            TextToSpeechVoice = "alloy"
        }));

        await using var audio = await provider.SynthesizeAsync(SafetyNotice.ClinicalReview, "en", CancellationToken.None);
        using var document = JsonDocument.Parse(handler.RequestBody!);

        handler.Path.Should().Be("/v1/audio/speech");
        handler.Authorization.Should().Be("Bearer test-key");
        document.RootElement.GetProperty("input").GetString().Should().Be(SafetyNotice.ClinicalReview);
        document.RootElement.GetProperty("model").GetString().Should().Be("gpt-4o-mini-tts");
        document.RootElement.GetProperty("voice").GetString().Should().Be("alloy");
        document.RootElement.GetProperty("response_format").GetString().Should().Be("mp3");
        document.RootElement.TryGetProperty("instructions", out _).Should().BeFalse();
        audio.Length.Should().Be(3);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsOversizedTextBeforeCallingProvider()
    {
        var handler = new CaptureHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.test/v1/") };
        var provider = new OpenAiTextToSpeechProvider(client, Options.Create(new OpenAiOptions { ApiKey = "test-key" }));

        var act = () => provider.SynthesizeAsync(new string('x', 4097), "en", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>();
        handler.RequestBody.Should().BeNull();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("ID3"u8.ToArray())
            };
        }
    }
}
