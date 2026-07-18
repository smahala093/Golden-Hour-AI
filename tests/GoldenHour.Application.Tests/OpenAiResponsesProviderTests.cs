using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using GoldenHour.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GoldenHour.Application.Tests;

public sealed class OpenAiResponsesProviderTests
{
    [Fact]
    public async Task ExtractIncidentAsync_CapturesActualModelLatencyAndUsageWithoutPrompts()
    {
        var handler = new StubHandler(_ => JsonResponse(CompletedResponse(ValidExtractionJson(), 123, 45)));
        var provider = CreateProvider(handler, "gpt-audited-model");

        var result = await provider.ExtractIncidentAsync("A fall was reported.", "en", CancellationToken.None);

        result.Value.IncidentCategory.Should().Be(IncidentCategory.Other);
        result.Metadata.OperationType.Should().Be("incident_extraction");
        result.Metadata.Provider.Should().Be("openai");
        result.Metadata.Model.Should().Be("gpt-audited-model");
        result.Metadata.LatencyMilliseconds.Should().BeGreaterThanOrEqualTo(0);
        result.Metadata.InputTokens.Should().Be(123);
        result.Metadata.OutputTokens.Should().Be(45);
        handler.CallCount.Should().Be(1);
        using var request = JsonDocument.Parse(handler.RequestBodies.Single());
        request.RootElement.GetProperty("model").GetString().Should().Be("gpt-audited-model");
        request.RootElement.GetProperty("store").GetBoolean().Should().BeFalse();
        request.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExtractIncidentAsync_Retries429TwiceThenReturnsBoundedFailureMetadata()
    {
        var handler = new StubHandler(_ => JsonResponse("{}", HttpStatusCode.TooManyRequests));
        var provider = CreateProvider(handler, "gpt-rate-test");

        var act = () => provider.ExtractIncidentAsync("Emergency reported.", "en", CancellationToken.None);

        var failure = await act.Should().ThrowAsync<AiProviderException>();
        failure.Which.Code.Should().Be("rate_limited");
        failure.Which.Metadata.Should().NotBeNull();
        failure.Which.Metadata!.Model.Should().Be("gpt-rate-test");
        failure.Which.Metadata.InputTokens.Should().BeNull();
        handler.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task ExtractIncidentAsync_RejectsRefusalAndRetainsOnlySafeUsageMetadata()
    {
        const string refusal = """
            {"status":"completed","usage":{"input_tokens":19,"output_tokens":2},"output":[{"type":"message","content":[{"type":"refusal","refusal":"not available"}]}]}
            """;
        var provider = CreateProvider(new StubHandler(_ => JsonResponse(refusal)));

        var act = () => provider.ExtractIncidentAsync("Emergency reported.", "en", CancellationToken.None);

        var failure = await act.Should().ThrowAsync<AiProviderException>();
        failure.Which.Code.Should().Be("refusal");
        failure.Which.Message.Should().NotContain("not available");
        failure.Which.Metadata!.InputTokens.Should().Be(19);
        failure.Which.Metadata.OutputTokens.Should().Be(2);
    }

    [Theory]
    [InlineData("{", "malformed_output")]
    [InlineData("{\"status\":\"incomplete\",\"usage\":{\"input_tokens\":7,\"output_tokens\":0},\"output\":[]}", "incomplete_output")]
    [InlineData("{\"status\":\"completed\"}", "malformed_output")]
    public async Task ExtractIncidentAsync_RejectsMalformedOrIncompleteProviderEnvelope(string responseBody, string expectedCode)
    {
        var provider = CreateProvider(new StubHandler(_ => JsonResponse(responseBody)));

        var act = () => provider.ExtractIncidentAsync("Emergency reported.", "en", CancellationToken.None);

        (await act.Should().ThrowAsync<AiProviderException>()).Which.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task AdditionalExtractionField_TriggersSafeStaticFallbackWithProviderMetadata()
    {
        var json = ValidExtractionJson()[..^1] + ",\"diagnosis\":\"injected\"}";
        var handler = new StubHandler(_ => JsonResponse(CompletedResponse(json, 21, 8)));
        var provider = CreateProvider(handler, "gpt-strict-test");
        var service = new IncidentUnderstandingService(
            provider,
            new IncidentExtractionValidator(),
            Options.Create(new EmergencyOptions { AiConfidenceThreshold = 0.7m }),
            NullLogger<IncidentUnderstandingService>.Instance);

        var result = await service.UnderstandAsync(
            "A fall was reported.", "en", IncidentCategory.FallOrInjury, PatientRelationship.Self, CancellationToken.None);

        result.UsedStaticFallback.Should().BeTrue();
        result.FailureCode.Should().Be("ai_malformed_output");
        result.AiMetadata!.Model.Should().Be("gpt-strict-test");
        result.AiMetadata.InputTokens.Should().Be(21);
        result.Extraction.Observations.Should().NotContain(value => value.Contains("diagnosis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestCoordinationTasks_CapturesSeparateOperationMetadata()
    {
        var handler = new StubHandler(_ => JsonResponse(CompletedResponse(
            "{\"taskCodes\":[\"call-emergency-services\",\"stay-with-patient\"]}", 31, 6)));
        var provider = CreateProvider(handler, "gpt-task-model");
        var incident = new IncidentExtraction(
            "en", 0.9m, IncidentCategory.Other, PatientRelationship.Self, ["Emergency reported."], null,
            TernaryAnswer.Unknown, TernaryAnswer.Unknown, TernaryAnswer.Unknown, null,
            UrgencyClassification.Unknown, [], ["Emergency reported."], ["Details unconfirmed."], 0.8m);

        var result = await provider.SuggestCoordinationTaskCodesAsync(incident, CancellationToken.None);

        result.Value.Should().Equal("call-emergency-services", "stay-with-patient");
        result.Metadata.OperationType.Should().Be("coordination_task_suggestion");
        result.Metadata.Model.Should().Be("gpt-task-model");
        result.Metadata.InputTokens.Should().Be(31);
        result.Metadata.OutputTokens.Should().Be(6);
    }

    private static OpenAiResponsesProvider CreateProvider(StubHandler handler, string model = "gpt-test-model")
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/"), Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new OpenAiOptions { ApiKey = "test-key", Model = model, BaseUrl = client.BaseAddress.ToString() });
        return new OpenAiResponsesProvider(
            client,
            options,
            new PromptTemplateStore(new TestEnvironment { ContentRootPath = FindApiRoot() }),
            new IncidentDataMinimizer(),
            NullLogger<OpenAiResponsesProvider>.Instance);
    }

    private static string FindApiRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "apps", "api");
            if (Directory.Exists(Path.Combine(candidate, "Prompts"))) return candidate;
        }

        throw new DirectoryNotFoundException("The API prompt root was not found from the test output directory.");
    }

    private static string ValidExtractionJson() =>
        """
        {"detectedLanguage":"en","languageConfidence":0.9,"incidentCategory":"other","patientRelationship":"self","observations":["A fall was reported."],"reportedSymptomStartTime":null,"isConscious":"unknown","isBreathingNormally":"unknown","isHeavyBleedingReported":"unknown","locationDescription":null,"urgencyClassification":"unknown","criticalMissingQuestions":[],"handoverFacts":["A fall was reported."],"uncertainties":["Details remain unconfirmed."],"confidence":0.8}
        """;

    private static string CompletedResponse(string extractionJson, int inputTokens, int outputTokens) => JsonSerializer.Serialize(new
    {
        status = "completed",
        usage = new { input_tokens = inputTokens, output_tokens = outputTokens },
        output = new[]
        {
            new
            {
                type = "message",
                content = new[] { new { type = "output_text", text = extractionJson } }
            }
        }
    });

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<int, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return responseFactory(CallCount);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GoldenHour.Api.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
