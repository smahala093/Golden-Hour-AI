using GoldenHour.Api.Domain;

namespace GoldenHour.Api.Application;

public sealed record RegisterRequest(string Email, string Password, string PreferredLanguage = "en");
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthUserResponse(Guid Id, string Email, string PreferredLanguage, DateTime AccessExpiresAtUtc);

public sealed record ContactInput(string Name, string Relationship, string PhoneNumber, bool IsVerified = false);
public sealed record NamedMedicalInput(string Name);
public sealed record ProcedureInput(string Name, int? Year);
public sealed record HospitalInput(string Name, string? PhoneNumber);
public sealed record SharingInput(
    bool ShareName,
    bool ShareApproximateAge,
    bool ShareAllergies,
    bool ShareConditions,
    bool ShareMedications,
    bool ShareEmergencyContact,
    bool Reviewed);

public sealed record ProfileUpsertRequest(
    string FullName,
    DateOnly? DateOfBirth,
    string? BloodGroup,
    string PreferredLanguage,
    string ResponseMode,
    string? InsuranceDetails,
    string? DoctorContact,
    bool AllergyStatusCompleted,
    bool MedicationStatusCompleted,
    bool LocationPermissionReviewed,
    bool Reviewed,
    IReadOnlyList<ContactInput> Contacts,
    IReadOnlyList<NamedMedicalInput> Allergies,
    IReadOnlyList<NamedMedicalInput> Conditions,
    IReadOnlyList<NamedMedicalInput> Medications,
    IReadOnlyList<ProcedureInput> Procedures,
    HospitalInput? PreferredHospital,
    SharingInput Sharing);

public sealed record ProfileResponse(
    Guid Id,
    string FullName,
    DateOnly? DateOfBirth,
    string? BloodGroup,
    string PreferredLanguage,
    string ResponseMode,
    string? InsuranceDetails,
    string? DoctorContact,
    bool AllergyStatusCompleted,
    bool MedicationStatusCompleted,
    bool LocationPermissionReviewed,
    bool IsSelfReported,
    IReadOnlyList<EmergencyContact> Contacts,
    IReadOnlyList<Allergy> Allergies,
    IReadOnlyList<MedicalCondition> Conditions,
    IReadOnlyList<Medication> Medications,
    IReadOnlyList<MedicalProcedure> Procedures,
    PreferredHospital? PreferredHospital,
    SharingPreference? SharingPreference,
    DateTime? ReviewedAtUtc);

public sealed record ContactVerificationChallengeResponse(
    string Challenge,
    DateTime ExpiresAtUtc,
    string Status,
    string? DevelopmentCode = null);
public sealed record ConfirmContactVerificationRequest(string Challenge, string Code);

public sealed record CreateSessionRequest(
    PatientRelationship PatientRelationship,
    IncidentCategory SelectedCategory,
    string? TypedLocation,
    string CountryCode = "IN",
    bool UseOwnerProfileForPatient = false);

public sealed record SubmitIncidentRequest(string OriginalText, string? SelectedLanguage, IncidentCategory? FallbackCategory, bool SkipAi = false);
public sealed record CriticalAnswersRequest(
    IReadOnlyDictionary<string, string>? Answers = null,
    string? QuestionId = null,
    string? Answer = null);
public sealed record CreateTaskRequest(string TaskCode, Guid? AssignedParticipantId);
public sealed record UpdateTaskRequest(GoldenHour.Api.Domain.TaskStatus Status, Guid? AssignedParticipantId, Guid ConcurrencyToken);
public sealed record TimelineUpdateRequest(string Type, string Message, string IdempotencyKey, bool UserConfirmed = false);
public sealed record LocationUpdateRequest(decimal? Latitude, decimal? Longitude, string? Description, bool ConsentProvided, string IdempotencyKey);
public sealed record CreateShareTokenRequest(int LifetimeMinutes = 30);
public sealed record InviteParticipantRequest(string DisplayName, ParticipantRole Role, int LifetimeMinutes = 30);
public sealed record JoinParticipantRequest(string Token);
public sealed record ParticipantInviteResponse(Guid ParticipantId, string Token, DateTime ExpiresAtUtc, string Path);
public sealed record BystanderObservationRequest(TernaryAnswer? Conscious, TernaryAnswer? BreathingNormally, TernaryAnswer? SevereBleeding);
public sealed record BystanderLocationRequest(decimal Latitude, decimal Longitude, bool ConsentConfirmed);

public sealed record ProtocolResponse(
    string Id,
    string Version,
    string ReviewStatus,
    string Notice,
    string EmergencyCallInstruction,
    IReadOnlyList<string> DoActions,
    IReadOnlyList<string> DoNotActions,
    string EscalationRule);

public sealed record TaskResponse(
    Guid Id,
    string Code,
    string Title,
    bool IsCritical,
    GoldenHour.Api.Domain.TaskStatus Status,
    Guid? AssignedParticipantId,
    DateTime? AcceptedAtUtc,
    DateTime? CompletedAtUtc,
    Guid ConcurrencyToken);

public sealed record TimelineEventResponse(Guid Id, long Sequence, string Type, string Message, string Source, DateTime CreatedAtUtc);
public sealed record ParticipantResponse(Guid Id, string DisplayName, ParticipantRole Role, DateTime? AcknowledgedAtUtc);
public sealed record LocationResponse(Guid Id, decimal? Latitude, decimal? Longitude, string? Description, DateTime CreatedAtUtc);
public sealed record ObservationResponse(Guid Id, string Kind, string Value, string Source, bool IsConfirmed, DateTime CreatedAtUtc);
public sealed record SnapshotProcedureResponse(string Name, int? Year);
public sealed record SnapshotContactResponse(string Name, string Relationship, string PhoneNumber);
public sealed record PatientSnapshotResponse(
    string? FullName,
    int? ApproximateAge,
    IReadOnlyList<string> Allergies,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<string> Medications,
    IReadOnlyList<SnapshotProcedureResponse> Procedures,
    SnapshotContactResponse? EmergencyContact,
    DateTime CapturedAtUtc,
    string Source = "profile_snapshot");

public sealed record SessionResponse(
    Guid Id,
    SessionStatus Status,
    IncidentCategory SelectedCategory,
    PatientRelationship PatientRelationship,
    string EmergencyNumber,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? OriginalInput,
    string? OriginalLanguage,
    string? NormalizedTranscript,
    IncidentExtraction? IncidentFacts,
    bool InterpretationUncertain,
    ProtocolResponse? Protocol,
    PatientSnapshotResponse? PatientSnapshot,
    IReadOnlyList<ObservationResponse> Observations,
    IReadOnlyList<TaskResponse> Tasks,
    IReadOnlyList<TimelineEventResponse> Timeline,
    IReadOnlyList<ParticipantResponse> Participants,
    IReadOnlyList<LocationResponse> Locations,
    Guid ConcurrencyToken,
    string? AnonymousAccessToken = null);

public sealed record ShareTokenResponse(Guid Id, string Token, DateTime ExpiresAtUtc, string Path);
public sealed record BystanderProfileResponse(string? Name, int? ApproximateAge, IReadOnlyList<string> Allergies, IReadOnlyList<string> Conditions, IReadOnlyList<string> Medications, EmergencyContact? EmergencyContact);
public sealed record BystanderContactResponse(string Name, string Relationship, string Phone);
public sealed record BystanderSessionResponse(
    Guid SessionId,
    string? PatientName,
    int? ApproximateAge,
    IncidentCategory Category,
    string Location,
    IReadOnlyList<string> Allergies,
    IReadOnlyList<string> Conditions,
    IReadOnlyList<string> Medicines,
    BystanderContactResponse? EmergencyContact,
    DateTime ExpiresAtUtc,
    string EmergencyNumber,
    ProtocolResponse? Protocol);

public interface IRealtimeNotifier
{
    Task NotifySessionAsync(Guid sessionId, string eventName, object payload, CancellationToken cancellationToken);
}
