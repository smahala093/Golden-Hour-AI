using System.Text.Json;
using GoldenHour.Api.Domain;

namespace GoldenHour.Api.Application;

public sealed record ProtocolDefinition(
    string Id,
    string Version,
    string ReviewStatus,
    string Country,
    string Notice,
    string EmergencyCallInstruction,
    IReadOnlyList<string> DoActions,
    IReadOnlyList<string> DoNotActions,
    string EscalationRule,
    string Source,
    IReadOnlyDictionary<string, string> TranslationKeys);

public sealed class ProtocolCatalogue
{
    private readonly IReadOnlyDictionary<string, ProtocolDefinition> protocols;

    public ProtocolCatalogue(IEnumerable<ProtocolDefinition> protocols)
    {
        var materialized = protocols.ToArray();
        if (materialized.Length == 0)
        {
            throw new InvalidOperationException("At least one static protocol is required.");
        }

        if (materialized.Any(x => x.Notice != SafetyNotice.ClinicalReview))
        {
            throw new InvalidOperationException("Every prototype protocol must carry the clinical-review notice.");
        }

        this.protocols = materialized.ToDictionary(x => x.Id, StringComparer.Ordinal);
    }

    public static ProtocolCatalogue Load(string protocolDirectory)
    {
        if (!Directory.Exists(protocolDirectory))
        {
            throw new DirectoryNotFoundException($"Protocol directory was not found: {protocolDirectory}");
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var protocols = Directory.EnumerateFiles(protocolDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(path => JsonSerializer.Deserialize<ProtocolDefinition>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException($"Protocol is empty: {Path.GetFileName(path)}"))
            .ToArray();
        return new ProtocolCatalogue(protocols);
    }

    public ProtocolDefinition Get(string id) => protocols.TryGetValue(id, out var protocol)
        ? protocol
        : throw new KeyNotFoundException("The selected protocol is not in the reviewed catalogue.");

    public ProtocolDefinition Select(IncidentExtraction extraction, IncidentCategory fallbackCategory)
    {
        var id = extraction switch
        {
            { IsConscious: TernaryAnswer.No, IsBreathingNormally: TernaryAnswer.No } => "unconscious-not-breathing",
            { IsConscious: TernaryAnswer.No } => "unconscious-breathing",
            { IsHeavyBleedingReported: TernaryAnswer.Yes } => "heavy-external-bleeding",
            _ => (extraction.IncidentCategory is IncidentCategory.Unknown ? fallbackCategory : extraction.IncidentCategory) switch
            {
                IncidentCategory.Seizure => "seizure",
                IncidentCategory.AllergicReaction => "suspected-allergic-reaction",
                IncidentCategory.ChestPain => "chest-pain",
                IncidentCategory.FallOrInjury or IncidentCategory.RoadAccident => "fall-or-injury",
                _ => "unknown-emergency"
            }
        };
        return Get(id);
    }
}
