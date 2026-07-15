namespace GoldenHour.Api.Domain;

public static class SafetyNotice
{
    public const string ClinicalReview = "Demonstration guidance requiring clinical review before production use.";
}

public enum SessionStatus { Active, Departed, ArrivedAtHospital, Closed }
public enum PatientRelationship { Self, Family, Bystander, Unknown }
public enum IncidentCategory { ChestPain, BreathingDifficulty, FallOrInjury, Unconscious, Seizure, HeavyBleeding, RoadAccident, AllergicReaction, ChildEmergency, Other, Unknown }
public enum TernaryAnswer { Yes, No, Unknown }
public enum UrgencyClassification { Emergency, Urgent, Unknown }
public enum TaskStatus { Suggested, Assigned, Accepted, Declined, Completed }
public enum ParticipantRole { Owner, Family, Bystander, Caregiver }
public enum SummaryKind { Family, Responder, HospitalHandover }
public enum ShareTokenPurpose { BystanderView, AnonymousSession, ParticipantInvite }

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

public sealed class EmergencyProfile : Entity
{
    public Guid OwnerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? BloodGroup { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public string ResponseMode { get; set; } = "text";
    public string? InsuranceDetails { get; set; }
    public string? DoctorContact { get; set; }
    public bool AllergyStatusCompleted { get; set; }
    public bool MedicationStatusCompleted { get; set; }
    public bool LocationPermissionReviewed { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public ICollection<EmergencyContact> Contacts { get; set; } = new List<EmergencyContact>();
    public ICollection<Allergy> Allergies { get; set; } = new List<Allergy>();
    public ICollection<MedicalCondition> Conditions { get; set; } = new List<MedicalCondition>();
    public ICollection<Medication> Medications { get; set; } = new List<Medication>();
    public ICollection<MedicalProcedure> Procedures { get; set; } = new List<MedicalProcedure>();
    public PreferredHospital? PreferredHospital { get; set; }
    public SharingPreference? SharingPreference { get; set; }
}

public sealed class EmergencyContact : Entity
{
    public Guid EmergencyProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
}

public sealed class Allergy : Entity
{
    public Guid EmergencyProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSelfReported { get; set; } = true;
}

public sealed class MedicalCondition : Entity
{
    public Guid EmergencyProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSelfReported { get; set; } = true;
}

public sealed class Medication : Entity
{
    public Guid EmergencyProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSelfReported { get; set; } = true;
}

public sealed class MedicalProcedure : Entity
{
    public Guid EmergencyProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Year { get; set; }
    public bool IsSelfReported { get; set; } = true;
}

public sealed class PreferredHospital : Entity
{
    public Guid EmergencyProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

public sealed class SharingPreference : Entity
{
    public Guid EmergencyProfileId { get; set; }
    public bool ShareName { get; set; }
    public bool ShareApproximateAge { get; set; }
    public bool ShareAllergies { get; set; }
    public bool ShareConditions { get; set; }
    public bool ShareMedications { get; set; }
    public bool ShareEmergencyContact { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
}

public sealed class EmergencySession : Entity
{
    public Guid? OwnerId { get; set; }
    public Guid? EmergencyProfileId { get; set; }
    public byte[]? CreateIdempotencyKeyHash { get; set; }
    public byte[]? CreateRequestHash { get; set; }
    public PatientRelationship PatientRelationship { get; set; }
    public IncidentCategory SelectedCategory { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public string? OriginalInput { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? NormalizedTranscript { get; set; }
    public string? IncidentFactsJson { get; set; }
    public string? ProfileSnapshotJson { get; set; }
    public string? ProtocolId { get; set; }
    public string? ProtocolVersion { get; set; }
    public decimal? AiConfidence { get; set; }
    public bool InterpretationConfirmed { get; set; }
    public string CountryCode { get; set; } = "IN";
    public ICollection<EmergencyParticipant> Participants { get; set; } = new List<EmergencyParticipant>();
    public ICollection<EmergencyObservation> Observations { get; set; } = new List<EmergencyObservation>();
    public ICollection<EmergencyTask> Tasks { get; set; } = new List<EmergencyTask>();
    public ICollection<EmergencyTimelineEvent> Timeline { get; set; } = new List<EmergencyTimelineEvent>();
    public ICollection<EmergencyLocation> Locations { get; set; } = new List<EmergencyLocation>();
    public ICollection<EmergencySummary> Summaries { get; set; } = new List<EmergencySummary>();
}

public sealed class EmergencyParticipant : Entity
{
    public Guid EmergencySessionId { get; set; }
    public Guid? UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public ParticipantRole Role { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
}

public sealed class EmergencyObservation : Entity
{
    public Guid EmergencySessionId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Source { get; set; } = "user-reported";
    public bool IsConfirmed { get; set; }
}

public sealed class EmergencyTask : Entity
{
    public Guid EmergencySessionId { get; set; }
    public string TaskCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Suggested;
    public Guid? AssignedParticipantId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class EmergencyTimelineEvent : Entity
{
    public Guid EmergencySessionId { get; set; }
    public long Sequence { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public Guid? ActorUserId { get; set; }
}

public sealed class EmergencyLocation : Entity
{
    public Guid EmergencySessionId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Description { get; set; }
    public bool ConsentProvided { get; set; }
}

public sealed class EmergencySummary : Entity
{
    public Guid EmergencySessionId { get; set; }
    public SummaryKind Kind { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string ProtocolVersion { get; set; } = string.Empty;
}

public sealed class EmergencyShareToken : Entity
{
    public Guid EmergencySessionId { get; set; }
    public Guid? EmergencyParticipantId { get; set; }
    public ShareTokenPurpose Purpose { get; set; } = ShareTokenPurpose.BystanderView;
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime? LastAccessedAtUtc { get; set; }
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid FamilyId { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}

public sealed class NotificationDelivery : Entity
{
    public string Provider { get; set; } = string.Empty;
    public string ProviderMessageId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ProviderConfirmedAtUtc { get; set; }
}

public sealed class AiOperation : Entity
{
    public Guid? EmergencySessionId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long LatencyMilliseconds { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureCode { get; set; }
}

public sealed class AuditEvent : Entity
{
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}

public sealed class OutboxMessage : Entity
{
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
}

public sealed class WebhookReceipt : Entity
{
    public string Provider { get; set; } = string.Empty;
    public string DeliveryId { get; set; } = string.Empty;
    public string PayloadDigest { get; set; } = string.Empty;
    public DateTime ProviderTimestampUtc { get; set; }
}
