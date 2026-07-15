using FluentAssertions;
using GoldenHour.Api.Application;
using Xunit;

namespace GoldenHour.Application.Tests;

public sealed class IncidentDataMinimizerTests
{
    [Fact]
    public void Minimize_RedactsIdentifiersAndSecrets_ButPreservesEmergencyTerms()
    {
        var input = "Raj takes DemoMed. Email raj@example.test, phone +91 98765 43210, policy AB-123456, password=hunter2. सीने में दर्द";

        var result = new IncidentDataMinimizer().Minimize(input);

        result.Should().Contain("Raj").And.Contain("DemoMed").And.Contain("सीने में दर्द");
        result.Should().NotContain("raj@example.test").And.NotContain("98765 43210").And.NotContain("AB-123456").And.NotContain("hunter2");
    }

    [Fact]
    public void Minimize_LeavesHindiDemoUnchanged()
    {
        new IncidentDataMinimizer().Minimize(DemoIncident.HindiChestPain).Should().Be(DemoIncident.HindiChestPain);
    }
}
