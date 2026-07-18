using FluentAssertions;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using Xunit;

namespace GoldenHour.Domain.Tests;

public sealed class DomainRulesTests
{
    [Fact]
    public void ReadinessScore_IsDeterministicAndNeverUsesAi()
    {
        var profile = new EmergencyProfile
        {
            PreferredLanguage = "hi",
            AllergyStatusCompleted = true,
            MedicationStatusCompleted = true,
            LocationPermissionReviewed = true,
            ReviewedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            SharingPreference = new SharingPreference { ReviewedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc) },
            Contacts =
            [
                new EmergencyContact { PhoneNumber = "+910000000001", IsVerified = true },
                new EmergencyContact { PhoneNumber = "+910000000002" }
            ]
        };

        var result = ReadinessCalculator.Calculate(profile, new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), true);

        result.Score.Should().Be(100);
        result.Checks.Should().OnlyContain(x => x.Complete);
    }

    [Fact]
    public void TaskCatalogue_DropsUnknownAndDuplicateAiSuggestions()
    {
        var result = TaskCatalogue.ValidateSuggestions(["call-emergency-services", "invent-treatment", "call-emergency-services"]);

        result.Should().ContainSingle().Which.Code.Should().Be("call-emergency-services");
    }

    [Fact]
    public void ClosedSession_CannotBeReopened()
    {
        SessionTransitions.CanTransition(SessionStatus.Closed, SessionStatus.Active).Should().BeFalse();
    }

    [Theory]
    [InlineData(SessionStatus.Active, SessionStatus.Departed)]
    [InlineData(SessionStatus.Active, SessionStatus.ArrivedAtHospital)]
    [InlineData(SessionStatus.Active, SessionStatus.Closed)]
    [InlineData(SessionStatus.Departed, SessionStatus.ArrivedAtHospital)]
    [InlineData(SessionStatus.Departed, SessionStatus.Closed)]
    [InlineData(SessionStatus.ArrivedAtHospital, SessionStatus.Closed)]
    public void SessionTransitions_AllowsOnlyForwardLifecycleMoves(SessionStatus current, SessionStatus next)
    {
        SessionTransitions.CanTransition(current, next).Should().BeTrue();
    }

    [Theory]
    [InlineData(SessionStatus.Active, SessionStatus.Active)]
    [InlineData(SessionStatus.Departed, SessionStatus.Active)]
    [InlineData(SessionStatus.ArrivedAtHospital, SessionStatus.Active)]
    [InlineData(SessionStatus.ArrivedAtHospital, SessionStatus.Departed)]
    [InlineData(SessionStatus.Closed, SessionStatus.Departed)]
    [InlineData(SessionStatus.Closed, SessionStatus.ArrivedAtHospital)]
    [InlineData(SessionStatus.Closed, SessionStatus.Closed)]
    public void SessionTransitions_RejectsDuplicateOrBackwardMoves(SessionStatus current, SessionStatus next)
    {
        SessionTransitions.CanTransition(current, next).Should().BeFalse();
    }

    [Theory]
    [InlineData("stay-with-patient")]
    [InlineData("call-emergency-services")]
    [InlineData("bring-medical-records")]
    [InlineData("bring-identification")]
    [InlineData("unlock-entry")]
    [InlineData("guide-responder")]
    [InlineData("contact-hospital")]
    [InlineData("care-for-dependants")]
    public void TaskCatalogue_ReturnsEveryReviewedCoordinationTask(string code)
    {
        TaskCatalogue.Get(code).Code.Should().Be(code);
    }

    [Fact]
    public void TaskCatalogue_RejectsClinicalOrUnknownTask()
    {
        var act = () => TaskCatalogue.Get("administer-aspirin");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(TernaryAnswer.No, TernaryAnswer.No, TernaryAnswer.Unknown, IncidentCategory.Unknown, "unconscious-not-breathing")]
    [InlineData(TernaryAnswer.No, TernaryAnswer.Yes, TernaryAnswer.Unknown, IncidentCategory.Unknown, "unconscious-breathing")]
    [InlineData(TernaryAnswer.No, TernaryAnswer.Unknown, TernaryAnswer.Unknown, IncidentCategory.Unknown, "unknown-emergency")]
    [InlineData(TernaryAnswer.Yes, TernaryAnswer.Yes, TernaryAnswer.Yes, IncidentCategory.ChestPain, "heavy-external-bleeding")]
    [InlineData(TernaryAnswer.Yes, TernaryAnswer.Yes, TernaryAnswer.No, IncidentCategory.Seizure, "seizure")]
    [InlineData(TernaryAnswer.Yes, TernaryAnswer.Yes, TernaryAnswer.No, IncidentCategory.AllergicReaction, "suspected-allergic-reaction")]
    [InlineData(TernaryAnswer.Yes, TernaryAnswer.Yes, TernaryAnswer.No, IncidentCategory.ChestPain, "chest-pain")]
    [InlineData(TernaryAnswer.Unknown, TernaryAnswer.Unknown, TernaryAnswer.Unknown, IncidentCategory.HeavyBleeding, "heavy-external-bleeding")]
    [InlineData(TernaryAnswer.Yes, TernaryAnswer.Yes, TernaryAnswer.No, IncidentCategory.RoadAccident, "fall-or-injury")]
    public void ProtocolSelection_UsesExplicitFactsAndReviewedFallbacks(
        TernaryAnswer conscious,
        TernaryAnswer breathing,
        TernaryAnswer bleeding,
        IncidentCategory category,
        string expectedProtocol)
    {
        var extraction = new IncidentExtraction(
            "en", 1, category, PatientRelationship.Unknown, [], null, conscious, breathing, bleeding,
            null, UrgencyClassification.Unknown, [], [], [], 1);

        CreateCatalogue().Select(extraction, category).Id.Should().Be(expectedProtocol);
    }

    private static ProtocolCatalogue CreateCatalogue()
    {
        var ids = new[]
        {
            "unconscious-not-breathing", "unconscious-breathing", "heavy-external-bleeding", "seizure",
            "suspected-allergic-reaction", "chest-pain", "fall-or-injury", "unknown-emergency"
        };
        return new ProtocolCatalogue(ids.Select(id => new ProtocolDefinition(
            id, "1.0.0", "prototype", "IN", SafetyNotice.ClinicalReview, "Call emergency services.", [], [],
            "Escalate to emergency services.", "test", new Dictionary<string, string>())));
    }
}
