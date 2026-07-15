namespace GoldenHour.Api.Domain;

public sealed record ReadinessResult(int Score, IReadOnlyList<ReadinessCheck> Checks);
public sealed record ReadinessCheck(string Code, string Label, bool Complete, int Weight);

public static class ReadinessCalculator
{
    public static ReadinessResult Calculate(EmergencyProfile profile, DateTime nowUtc, bool hasActiveShareToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var checks = new[]
        {
            new ReadinessCheck("contacts", "Add at least two emergency contacts", profile.Contacts.Count >= 2, 15),
            new ReadinessCheck("verified-contact", "Verify an emergency contact", profile.Contacts.Any(x => x.IsVerified), 15),
            new ReadinessCheck("allergies", "Complete allergy status", profile.AllergyStatusCompleted, 15),
            new ReadinessCheck("medications", "Complete medicine status", profile.MedicationStatusCompleted, 15),
            new ReadinessCheck("language", "Select a preferred language", !string.IsNullOrWhiteSpace(profile.PreferredLanguage), 10),
            new ReadinessCheck("sharing", "Review emergency sharing settings", profile.SharingPreference?.ReviewedAtUtc is not null, 10),
            new ReadinessCheck("location", "Review location permission", profile.LocationPermissionReviewed, 5),
            new ReadinessCheck("share-link", "Generate an emergency QR link", hasActiveShareToken, 5),
            new ReadinessCheck("recent-review", "Review the profile within six months", profile.ReviewedAtUtc >= nowUtc.AddMonths(-6), 10)
        };

        return new ReadinessResult(checks.Where(x => x.Complete).Sum(x => x.Weight), checks);
    }
}

public sealed record AllowedTask(string Code, string Title, bool IsCritical);

public static class TaskCatalogue
{
    private static readonly IReadOnlyDictionary<string, AllowedTask> Tasks = new[]
    {
        new AllowedTask("stay-with-patient", "Stay with the patient", false),
        new AllowedTask("call-emergency-services", "Call emergency services", true),
        new AllowedTask("bring-medical-records", "Bring medical records", false),
        new AllowedTask("bring-identification", "Bring identification and insurance documents", false),
        new AllowedTask("unlock-entry", "Unlock the door or gate", false),
        new AllowedTask("guide-responder", "Guide the responder to the location", false),
        new AllowedTask("contact-hospital", "Contact the preferred hospital", false),
        new AllowedTask("care-for-dependants", "Care for children or dependants", false)
    }.ToDictionary(x => x.Code, StringComparer.Ordinal);

    public static IReadOnlyList<AllowedTask> ValidateSuggestions(IEnumerable<string> codes) =>
        codes.Distinct(StringComparer.Ordinal).Where(Tasks.ContainsKey).Select(code => Tasks[code]).ToArray();

    public static AllowedTask Get(string code) => Tasks.TryGetValue(code, out var task)
        ? task
        : throw new ArgumentOutOfRangeException(nameof(code), "Task is not in the reviewed coordination catalogue.");
}

public static class SessionTransitions
{
    public static bool CanTransition(SessionStatus current, SessionStatus next) => (current, next) switch
    {
        (SessionStatus.Active, SessionStatus.Departed) => true,
        (SessionStatus.Active, SessionStatus.ArrivedAtHospital) => true,
        (SessionStatus.Active, SessionStatus.Closed) => true,
        (SessionStatus.Departed, SessionStatus.ArrivedAtHospital) => true,
        (SessionStatus.Departed, SessionStatus.Closed) => true,
        (SessionStatus.ArrivedAtHospital, SessionStatus.Closed) => true,
        _ => false
    };
}
