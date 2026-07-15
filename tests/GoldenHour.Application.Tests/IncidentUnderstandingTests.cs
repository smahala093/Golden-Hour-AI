using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GoldenHour.Application.Tests;

public sealed class IncidentUnderstandingTests
{
    [Fact]
    public async Task MockHindiChestPain_PassesSafetyValidation()
    {
        var service = new IncidentUnderstandingService(
            new MockAiProvider(),
            new IncidentExtractionValidator(),
            Options.Create(new EmergencyOptions { AiConfidenceThreshold = 0.7m }),
            NullLogger<IncidentUnderstandingService>.Instance);

        var result = await service.UnderstandAsync(
            DemoIncident.HindiChestPain,
            "hi",
            IncidentCategory.ChestPain,
            PatientRelationship.Family,
            CancellationToken.None);

        result.UsedStaticFallback.Should().BeFalse();
        result.IsUncertain.Should().BeFalse();
        result.Extraction.DetectedLanguage.Should().Be("hi");
        result.Extraction.IncidentCategory.Should().Be(IncidentCategory.ChestPain);
        result.Extraction.CriticalMissingQuestions.Should().HaveCountLessThanOrEqualTo(3);
    }

    [Fact]
    public void Serialization_UsesExactStrictSchemaWireValues()
    {
        var extraction = new IncidentExtraction(
            "en", 1, IncidentCategory.ChestPain, PatientRelationship.Self, [], null,
            TernaryAnswer.Yes, TernaryAnswer.No, TernaryAnswer.Unknown, null,
            UrgencyClassification.Emergency,
            [new CriticalMissingQuestion("breathing", "Breathing?", CriticalAnswerType.YesNo)],
            [], [], 0.9m);

        var json = JsonSerializer.Serialize(extraction, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"incidentCategory\":\"chest_pain\"");
        json.Should().Contain("\"patientRelationship\":\"self\"");
        json.Should().Contain("\"isConscious\":\"yes\"");
        json.Should().Contain("\"isBreathingNormally\":\"no\"");
        json.Should().Contain("\"answerType\":\"yes_no\"");
    }

    [Fact]
    public void Deserialization_RejectsUnsupportedEnum()
    {
        const string json = """
            {
              "detectedLanguage":"en","languageConfidence":1,"incidentCategory":"heart_attack",
              "patientRelationship":"self","observations":[],"reportedSymptomStartTime":null,
              "isConscious":"yes","isBreathingNormally":"yes","isHeavyBleedingReported":"no",
              "locationDescription":null,"urgencyClassification":"emergency","criticalMissingQuestions":[],
              "handoverFacts":[],"uncertainties":[],"confidence":0.9
            }
            """;

        var act = () => JsonSerializer.Deserialize<IncidentExtraction>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("Patient diagnosed with a heart attack.")]
    [InlineData("Give 300 mg aspirin now.")]
    [InlineData("The ambulance has been contacted.")]
    public void Validator_RejectsForbiddenClinicalOrExternalClaims(string unsafeFact)
    {
        var extraction = new IncidentExtraction(
            "en", 1, IncidentCategory.Unknown, PatientRelationship.Unknown, [unsafeFact], null,
            TernaryAnswer.Unknown, TernaryAnswer.Unknown, TernaryAnswer.Unknown, null,
            UrgencyClassification.Unknown, [], [], [], 0.5m);

        var result = new IncidentExtractionValidator().Validate(extraction);

        result.IsValid.Should().BeFalse();
    }
}
