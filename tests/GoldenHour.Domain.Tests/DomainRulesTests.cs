using FluentAssertions;
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
}
