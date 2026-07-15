using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentValidation;
using GoldenHour.Api.Domain;
using Microsoft.Extensions.Options;

namespace GoldenHour.Api.Application;

public sealed record CriticalMissingQuestion(
    string Id,
    string Question,
    [property: JsonConverter(typeof(SnakeCaseEnumJsonConverter<CriticalAnswerType>))] CriticalAnswerType AnswerType);

public enum CriticalAnswerType { YesNo, SingleChoice, Time, Text }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IncidentExtraction(
    string DetectedLanguage,
    decimal LanguageConfidence,
    [property: JsonConverter(typeof(SnakeCaseEnumJsonConverter<IncidentCategory>))] IncidentCategory IncidentCategory,
    [property: JsonConverter(typeof(SnakeCaseEnumJsonConverter<PatientRelationship>))] PatientRelationship PatientRelationship,
    IReadOnlyList<string> Observations,
    string? ReportedSymptomStartTime,
    [property: JsonConverter(typeof(SnakeCaseEnumJsonConverter<TernaryAnswer>))] TernaryAnswer IsConscious,
    [property: JsonConverter(typeof(SnakeCaseEnumJsonConverter<TernaryAnswer>))] TernaryAnswer IsBreathingNormally,
    [property: JsonConverter(typeof(SnakeCaseEnumJsonConverter<TernaryAnswer>))] TernaryAnswer IsHeavyBleedingReported,
    string? LocationDescription,
    [property: JsonConverter(typeof(SnakeCaseEnumJsonConverter<UrgencyClassification>))] UrgencyClassification UrgencyClassification,
    IReadOnlyList<CriticalMissingQuestion> CriticalMissingQuestions,
    IReadOnlyList<string> HandoverFacts,
    IReadOnlyList<string> Uncertainties,
    decimal Confidence);

public sealed record IncidentInterpretation(IncidentExtraction Extraction, bool IsUncertain, bool UsedStaticFallback, string? FailureCode);

public sealed class SnakeCaseEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wireValue = reader.GetString() ?? throw new JsonException($"{typeof(TEnum).Name} must be a string.");
        foreach (var value in Enum.GetValues<TEnum>())
        {
            if (string.Equals(ToWireValue(value), wireValue, StringComparison.Ordinal))
            {
                return value;
            }
        }

        throw new JsonException($"Unsupported {typeof(TEnum).Name} value.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToWireValue(value));

    private static string ToWireValue(TEnum value) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}

public sealed class EmergencyOptions
{
    public string DefaultNumber { get; set; } = "112";
    public decimal AiConfidenceThreshold { get; set; } = 0.7m;
}

public sealed partial class IncidentExtractionValidator : AbstractValidator<IncidentExtraction>
{
    public IncidentExtractionValidator()
    {
        RuleFor(x => x.DetectedLanguage).NotEmpty().MaximumLength(35);
        RuleFor(x => x.LanguageConfidence).InclusiveBetween(0, 1);
        RuleFor(x => x.Confidence).InclusiveBetween(0, 1);
        RuleFor(x => x.Observations).NotNull().Must(x => x.Count <= 20).WithMessage("No more than 20 observations are allowed.");
        RuleForEach(x => x.Observations).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CriticalMissingQuestions).NotNull().Must(x => x.Count <= 3).WithMessage("No more than three immediate questions are allowed.");
        RuleForEach(x => x.CriticalMissingQuestions).ChildRules(question =>
        {
            question.RuleFor(x => x.Id).NotEmpty().MaximumLength(80).Matches("^[a-z0-9-]+$");
            question.RuleFor(x => x.Question).NotEmpty().MaximumLength(300);
        });
        RuleFor(x => x.HandoverFacts).NotNull().Must(x => x.Count <= 30);
        RuleForEach(x => x.HandoverFacts).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Uncertainties).NotNull().Must(x => x.Count <= 20);
        RuleForEach(x => x.Uncertainties).NotEmpty().MaximumLength(500);
        RuleFor(x => x).Custom(ValidateForbiddenContent);
    }

    private static void ValidateForbiddenContent(IncidentExtraction extraction, ValidationContext<IncidentExtraction> context)
    {
        var text = string.Join('\n', extraction.Observations
            .Concat(extraction.HandoverFacts)
            .Concat(extraction.Uncertainties)
            .Concat(extraction.CriticalMissingQuestions.Select(x => x.Question)));

        if (DiagnosisPattern().IsMatch(text))
        {
            context.AddFailure("AI output contains a diagnosis or unsupported clinical conclusion.");
        }

        if (MedicationPattern().IsMatch(text))
        {
            context.AddFailure("AI output contains medication or dosage instructions.");
        }

        if (ExternalActionPattern().IsMatch(text))
        {
            context.AddFailure("AI output claims an external emergency action succeeded.");
        }

        if (ImperativeActionPattern().IsMatch(text))
        {
            context.AddFailure("AI output contains treatment or action instructions instead of reported facts.");
        }
    }

    [GeneratedRegex(@"\b(diagnos(?:is|ed|e)|you have|patient has|confirmed (?:heart attack|stroke|anaphylaxis)|(?:likely|possible|possibly|suspected)\s+(?:an?\s+)?(?:heart attack|stroke|anaphylaxis))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosisPattern();

    [GeneratedRegex(@"\b(?:take|give|administer|swallow|inject|use|should (?:take|give|administer))\b.{0,50}\b(?:mg|ml|tablet|capsule|dose|aspirin|epinephrine|adrenaline|nitroglycerin|medicine|medication|drug)\b|\b\d+(?:\.\d+)?\s*(?:mg|ml)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MedicationPattern();

    [GeneratedRegex(@"\b(?:ambulance|emergency services?|hospital|responder)\b.{0,40}\b(?:(?:has been|was|were|is)\s+)?(?:called|contacted|notified|dispatched)\b|\b(?:112|911|999)\s+(?:has been|was|is)\s+(?:called|contacted|notified)\b|\bhelp is on the way\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExternalActionPattern();

    [GeneratedRegex(@"(?:^|[.!?]\s+)(?:call|apply|move|perform|start|stop|place|position|press|elevate|bandage|treat|resuscitate|do cpr|begin cpr|keep the person|lay the person|turn the person)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ImperativeActionPattern();
}

public sealed class IncidentUnderstandingService(
    IAiProvider aiProvider,
    IValidator<IncidentExtraction> validator,
    IOptions<EmergencyOptions> emergencyOptions,
    ILogger<IncidentUnderstandingService> logger)
{
    public async Task<IncidentInterpretation> UnderstandAsync(
        string originalText,
        string? selectedLanguage,
        IncidentCategory fallbackCategory,
        PatientRelationship relationship,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalText);
        if (originalText.Length > 12_000)
        {
            throw new ValidationException("Incident description must be 12,000 characters or fewer.");
        }

        try
        {
            var extraction = await aiProvider.ExtractIncidentAsync(originalText, selectedLanguage, cancellationToken);
            var validation = await validator.ValidateAsync(extraction, cancellationToken);
            if (!validation.IsValid)
            {
                logger.LogWarning("AI incident extraction failed contract validation with {FailureCount} failures.", validation.Errors.Count);
                return Fallback(originalText, selectedLanguage, fallbackCategory, relationship, "invalid_ai_output");
            }

            var uncertain = extraction.Confidence < emergencyOptions.Value.AiConfidenceThreshold
                || extraction.LanguageConfidence < emergencyOptions.Value.AiConfidenceThreshold;
            return new IncidentInterpretation(extraction, uncertain, false, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI incident extraction timed out.");
            return Fallback(originalText, selectedLanguage, fallbackCategory, relationship, "ai_timeout");
        }
        catch (Exception exception) when (exception is not ValidationException and not OperationCanceledException)
        {
            logger.LogWarning(exception, "AI incident extraction was unavailable; using static fallback.");
            return Fallback(originalText, selectedLanguage, fallbackCategory, relationship, "ai_unavailable");
        }
    }

    private static IncidentInterpretation Fallback(
        string originalText,
        string? selectedLanguage,
        IncidentCategory fallbackCategory,
        PatientRelationship relationship,
        string failureCode)
    {
        var extraction = new IncidentExtraction(
            selectedLanguage ?? "und",
            0,
            fallbackCategory,
            relationship,
            ["The original description is preserved for confirmation."],
            null,
            TernaryAnswer.Unknown,
            TernaryAnswer.Unknown,
            TernaryAnswer.Unknown,
            null,
            UrgencyClassification.Unknown,
            [new CriticalMissingQuestion("confirm-facts", "Please confirm the reported facts.", CriticalAnswerType.Text)],
            [originalText],
            ["AI interpretation is unavailable or uncertain."],
            0);
        return new IncidentInterpretation(extraction, true, true, failureCode);
    }
}

public static class DemoIncident
{
    public const string HindiChestPain = "मेरे पिताजी को अचानक सीने में दर्द और बहुत पसीना आ रहा है। उन्हें बोलने में भी परेशानी हो रही है।";
}

public sealed class MockAiProvider : IAiProvider
{
    public Task<IncidentExtraction> ExtractIncidentAsync(string originalText, string? selectedLanguage, CancellationToken cancellationToken)
    {
        var looksHindi = originalText.Any(character => character is >= '\u0900' and <= '\u097F');
        var chestPain = originalText.Contains("chest", StringComparison.OrdinalIgnoreCase)
            || originalText.Contains("सीने", StringComparison.Ordinal);
        var extraction = new IncidentExtraction(
            looksHindi ? "hi" : selectedLanguage ?? "en",
            looksHindi ? 0.98m : 0.94m,
            chestPain ? IncidentCategory.ChestPain : IncidentCategory.Unknown,
            originalText.Contains("पिताजी", StringComparison.Ordinal) || originalText.Contains("father", StringComparison.OrdinalIgnoreCase)
                ? PatientRelationship.Family
                : PatientRelationship.Unknown,
            chestPain
                ? ["Sudden chest pain was reported.", "Heavy sweating was reported.", "Difficulty speaking was reported."]
                : ["An emergency situation was reported."],
            null,
            TernaryAnswer.Unknown,
            TernaryAnswer.Unknown,
            TernaryAnswer.Unknown,
            null,
            chestPain ? UrgencyClassification.Emergency : UrgencyClassification.Unknown,
            [
                new CriticalMissingQuestion("conscious", "Is the person conscious?", CriticalAnswerType.YesNo),
                new CriticalMissingQuestion("breathing", "Is the person breathing normally?", CriticalAnswerType.YesNo)
            ],
            chestPain
                ? ["Family reports sudden chest pain, heavy sweating, and difficulty speaking."]
                : ["Emergency details need confirmation."],
            ["Extracted facts remain user-reported and unconfirmed."],
            chestPain ? 0.93m : 0.55m);
        return Task.FromResult(extraction);
    }

    public Task<string> TranslateApprovedTextAsync(string text, string targetLanguage, CancellationToken cancellationToken) => Task.FromResult(text);

    public Task<IReadOnlyList<string>> SuggestCoordinationTaskCodesAsync(IncidentExtraction incident, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(["call-emergency-services", "stay-with-patient", "bring-medical-records", "not-allowlisted"]);
}

public sealed class MockSpeechToTextProvider : ISpeechToTextProvider
{
    public Task<SpeechTranscription> TranscribeAsync(Stream audio, string contentType, string? languageHint, CancellationToken cancellationToken) =>
        Task.FromResult(new SpeechTranscription(DemoIncident.HindiChestPain, "hi", 0.98m));
}
