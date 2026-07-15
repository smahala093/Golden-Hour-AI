using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoldenHour.Api.Infrastructure;

public sealed class ProfileCoordinator(GoldenHourDbContext dbContext, IClock clock)
{
    public async Task<ProfileResponse?> GetAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var profile = await Query().SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        return profile is null ? null : ToResponse(profile);
    }

    public async Task<ProfileResponse> UpsertAsync(Guid ownerId, ProfileUpsertRequest request, CancellationToken cancellationToken)
    {
        var profile = await Query().SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        if (profile is null)
        {
            profile = new EmergencyProfile { OwnerId = ownerId };
            dbContext.EmergencyProfiles.Add(profile);
        }
        else
        {
            dbContext.EmergencyContacts.RemoveRange(profile.Contacts);
            dbContext.Allergies.RemoveRange(profile.Allergies);
            dbContext.MedicalConditions.RemoveRange(profile.Conditions);
            dbContext.Medications.RemoveRange(profile.Medications);
            dbContext.MedicalProcedures.RemoveRange(profile.Procedures);
            if (profile.PreferredHospital is not null) dbContext.PreferredHospitals.Remove(profile.PreferredHospital);
            if (profile.SharingPreference is not null) dbContext.SharingPreferences.Remove(profile.SharingPreference);
        }

        profile.FullName = request.FullName.Trim();
        profile.DateOfBirth = request.DateOfBirth;
        profile.BloodGroup = request.BloodGroup?.Trim();
        profile.PreferredLanguage = request.PreferredLanguage.Trim();
        profile.ResponseMode = request.ResponseMode.Trim();
        profile.InsuranceDetails = request.InsuranceDetails?.Trim();
        profile.DoctorContact = request.DoctorContact?.Trim();
        profile.AllergyStatusCompleted = request.AllergyStatusCompleted;
        profile.MedicationStatusCompleted = request.MedicationStatusCompleted;
        profile.LocationPermissionReviewed = request.LocationPermissionReviewed;
        profile.ReviewedAtUtc = request.Reviewed ? clock.UtcNow : profile.ReviewedAtUtc;
        profile.Contacts = request.Contacts.Select(x => new EmergencyContact
        {
            EmergencyProfileId = profile.Id,
            Name = x.Name.Trim(),
            Relationship = x.Relationship.Trim(),
            PhoneNumber = x.PhoneNumber.Trim(),
            IsVerified = x.IsVerified
        }).ToList();
        profile.Allergies = request.Allergies.Select(x => new Allergy { EmergencyProfileId = profile.Id, Name = x.Name.Trim() }).ToList();
        profile.Conditions = request.Conditions.Select(x => new MedicalCondition { EmergencyProfileId = profile.Id, Name = x.Name.Trim() }).ToList();
        profile.Medications = request.Medications.Select(x => new Medication { EmergencyProfileId = profile.Id, Name = x.Name.Trim() }).ToList();
        profile.Procedures = request.Procedures.Select(x => new MedicalProcedure { EmergencyProfileId = profile.Id, Name = x.Name.Trim(), Year = x.Year }).ToList();
        profile.PreferredHospital = request.PreferredHospital is null ? null : new PreferredHospital
        {
            EmergencyProfileId = profile.Id,
            Name = request.PreferredHospital.Name.Trim(),
            PhoneNumber = request.PreferredHospital.PhoneNumber?.Trim()
        };
        profile.SharingPreference = new SharingPreference
        {
            EmergencyProfileId = profile.Id,
            ShareName = request.Sharing.ShareName,
            ShareApproximateAge = request.Sharing.ShareApproximateAge,
            ShareAllergies = request.Sharing.ShareAllergies,
            ShareConditions = request.Sharing.ShareConditions,
            ShareMedications = request.Sharing.ShareMedications,
            ShareEmergencyContact = request.Sharing.ShareEmergencyContact,
            ReviewedAtUtc = request.Sharing.Reviewed ? clock.UtcNow : null
        };
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public async Task<ReadinessResult> ReadinessAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var profile = await Query().SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken)
            ?? throw new KeyNotFoundException("Emergency profile not found.");
        var hasActiveShare = await dbContext.EmergencyShareTokens.AnyAsync(
            x => x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow
                && dbContext.EmergencySessions.Any(session => session.Id == x.EmergencySessionId && session.OwnerId == ownerId),
            cancellationToken);
        return ReadinessCalculator.Calculate(profile, clock.UtcNow, hasActiveShare);
    }

    private IQueryable<EmergencyProfile> Query() => dbContext.EmergencyProfiles
        .Include(x => x.Contacts).Include(x => x.Allergies).Include(x => x.Conditions)
        .Include(x => x.Medications).Include(x => x.Procedures).Include(x => x.PreferredHospital)
        .Include(x => x.SharingPreference);

    private static ProfileResponse ToResponse(EmergencyProfile profile) => new(
        profile.Id, profile.FullName, profile.DateOfBirth, profile.BloodGroup, profile.PreferredLanguage,
        profile.ResponseMode, profile.InsuranceDetails, profile.DoctorContact, profile.AllergyStatusCompleted,
        profile.MedicationStatusCompleted, profile.LocationPermissionReviewed, true,
        profile.Contacts.OrderBy(x => x.Name).ToArray(), profile.Allergies.OrderBy(x => x.Name).ToArray(),
        profile.Conditions.OrderBy(x => x.Name).ToArray(), profile.Medications.OrderBy(x => x.Name).ToArray(),
        profile.Procedures.OrderByDescending(x => x.Year).ToArray(), profile.PreferredHospital, profile.SharingPreference, profile.ReviewedAtUtc);
}

public sealed class SessionCoordinator(
    GoldenHourDbContext dbContext,
    IncidentUnderstandingService incidentUnderstanding,
    IAiProvider aiProvider,
    ProtocolCatalogue protocolCatalogue,
    IRealtimeNotifier realtime,
    IClock clock,
    IOptions<EmergencyOptions> emergencyOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SessionResponse> CreateAsync(Guid? ownerId, CreateSessionRequest request, CancellationToken cancellationToken)
    {
        if (request.CountryCode.Length != 2 || !request.CountryCode.All(char.IsAsciiLetter)) throw new InvalidDataException("Country code must contain two letters.");
        if (request.TypedLocation?.Length > 300) throw new InvalidDataException("Typed location must be 300 characters or fewer.");
        EmergencyProfile? profile = null;
        if (ownerId is not null)
        {
            profile = await dbContext.EmergencyProfiles.Include(x => x.Allergies).Include(x => x.Conditions)
                .Include(x => x.Medications).Include(x => x.Procedures).Include(x => x.Contacts)
                .Include(x => x.SharingPreference).SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        }

        var session = new EmergencySession
        {
            OwnerId = ownerId,
            EmergencyProfileId = profile?.Id,
            PatientRelationship = request.PatientRelationship,
            SelectedCategory = request.SelectedCategory,
            CountryCode = request.CountryCode.Trim().ToUpperInvariant(),
            ProfileSnapshotJson = profile is null ? null : JsonSerializer.Serialize(CreateProfileSnapshot(profile), JsonOptions)
        };
        session.Timeline.Add(new EmergencyTimelineEvent
        {
            EmergencySessionId = session.Id,
            Sequence = 1,
            Type = "session-created",
            Message = "Emergency session started. The emergency-call action is available immediately.",
            IdempotencyKey = $"session-created:{session.Id}"
        });
        if (ownerId is not null)
        {
            session.Participants.Add(new EmergencyParticipant
            {
                EmergencySessionId = session.Id,
                UserId = ownerId,
                DisplayName = profile?.FullName ?? "Session owner",
                Role = ParticipantRole.Owner
            });
        }

        if (!string.IsNullOrWhiteSpace(request.TypedLocation))
        {
            session.Locations.Add(new EmergencyLocation
            {
                EmergencySessionId = session.Id,
                Description = request.TypedLocation.Trim(),
                ConsentProvided = true
            });
        }

        dbContext.EmergencySessions.Add(session);
        string? anonymousAccessToken = null;
        if (ownerId is null)
        {
            anonymousAccessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            dbContext.EmergencyShareTokens.Add(new EmergencyShareToken
            {
                EmergencySessionId = session.Id,
                Purpose = ShareTokenPurpose.AnonymousSession,
                TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(anonymousAccessToken)),
                ExpiresAtUtc = clock.UtcNow.AddHours(2)
            });
        }
        dbContext.OutboxMessages.Add(new OutboxMessage { EventType = "emergency.session.created", PayloadJson = JsonSerializer.Serialize(new { sessionId = session.Id }, JsonOptions) });
        await dbContext.SaveChangesAsync(cancellationToken);
        await realtime.NotifySessionAsync(session.Id, "SessionUpdated", new { session.Id, session.Status }, cancellationToken);
        return Map(session) with { AnonymousAccessToken = anonymousAccessToken };
    }

    public async Task<SessionResponse> GetAsync(Guid sessionId, Guid? userId, string? anonymousAccessToken, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, anonymousAccessToken, cancellationToken);
        return Map(session);
    }

    public async Task<IReadOnlyList<SessionResponse>> ListAsync(Guid userId, int limit, DateTime? beforeUtc, CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var query = Query().Where(x => x.OwnerId == userId || x.Participants.Any(participant => participant.UserId == userId));
        if (beforeUtc is not null) query = query.Where(x => x.UpdatedAtUtc < beforeUtc.Value);
        var sessions = await query.OrderByDescending(x => x.UpdatedAtUtc).Take(boundedLimit).ToListAsync(cancellationToken);
        return sessions.Select(Map).ToArray();
    }

    public async Task<SessionResponse> SubmitIncidentAsync(Guid sessionId, Guid? userId, SubmitIncidentRequest request, CancellationToken cancellationToken, string? anonymousAccessToken = null)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, anonymousAccessToken, cancellationToken);
        if (session.Status == SessionStatus.Closed) throw new InvalidOperationException("Closed sessions cannot accept incident updates.");

        var interpretation = await incidentUnderstanding.UnderstandAsync(
            request.OriginalText,
            request.SelectedLanguage,
            request.FallbackCategory ?? session.SelectedCategory,
            session.PatientRelationship,
            cancellationToken);
        var protocol = protocolCatalogue.Select(interpretation.Extraction, request.FallbackCategory ?? session.SelectedCategory);
        session.OriginalInput = request.OriginalText;
        session.OriginalLanguage = interpretation.Extraction.DetectedLanguage;
        session.NormalizedTranscript = string.Join(" ", interpretation.Extraction.HandoverFacts);
        session.IncidentFactsJson = JsonSerializer.Serialize(interpretation.Extraction, JsonOptions);
        session.SelectedCategory = interpretation.Extraction.IncidentCategory is IncidentCategory.Unknown
            ? request.FallbackCategory ?? session.SelectedCategory
            : interpretation.Extraction.IncidentCategory;
        session.ProtocolId = protocol.Id;
        session.ProtocolVersion = protocol.Version;
        session.AiConfidence = interpretation.Extraction.Confidence;
        session.InterpretationConfirmed = !interpretation.IsUncertain;

        foreach (var fact in interpretation.Extraction.Observations)
        {
            session.Observations.Add(new EmergencyObservation
            {
                EmergencySessionId = session.Id,
                Kind = "reported-observation",
                Value = fact,
                Source = "ai-extracted-user-report",
                IsConfirmed = false
            });
        }

        IReadOnlyList<string> suggestions;
        try
        {
            suggestions = await aiProvider.SuggestCoordinationTaskCodesAsync(interpretation.Extraction, cancellationToken);
        }
        catch
        {
            suggestions = ["call-emergency-services", "stay-with-patient"];
        }

        foreach (var allowed in TaskCatalogue.ValidateSuggestions(suggestions.Concat(["call-emergency-services"])))
        {
            if (session.Tasks.All(x => x.TaskCode != allowed.Code))
            {
                session.Tasks.Add(new EmergencyTask
                {
                    EmergencySessionId = session.Id,
                    TaskCode = allowed.Code,
                    Title = allowed.Title,
                    IsCritical = allowed.IsCritical
                });
            }
        }

        AddTimeline(session, interpretation.UsedStaticFallback ? "ai-unavailable" : "incident-understood",
            interpretation.IsUncertain ? "Reported facts need user confirmation; static guidance remains available." : "Reported facts were extracted for user review.",
            $"incident:{Guid.NewGuid():N}", userId);
        dbContext.AiOperations.Add(new AiOperation
        {
            EmergencySessionId = session.Id,
            OperationType = "incident-extraction",
            Provider = aiProvider.GetType().Name,
            Model = aiProvider is MockAiProvider ? "deterministic-mock" : "configured-openai-model",
            Succeeded = !interpretation.UsedStaticFallback,
            FailureCode = interpretation.FailureCode
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = Map(session);
        await realtime.NotifySessionAsync(session.Id, "SessionUpdated", response, cancellationToken);
        return response;
    }

    public async Task<SessionResponse> AddTimelineAsync(Guid sessionId, Guid? userId, TimelineUpdateRequest request, CancellationToken cancellationToken, string? anonymousAccessToken = null)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, anonymousAccessToken, cancellationToken);
        if (session.Status == SessionStatus.Closed) throw new InvalidOperationException("Closed sessions cannot accept timeline updates.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw new InvalidDataException("A valid idempotency key is required.");
        if (request.Message.Length > 500) throw new InvalidDataException("Timeline observations must be 500 characters or fewer.");
        var anonymous = userId is null;
        if (anonymous && request.Type is not ("observation" or "critical-answer" or "call-initiated" or "call-connected"))
            throw new UnauthorizedAccessException("Anonymous emergency access cannot change this status.");
        var serverMessage = request.Type switch
        {
            "observation" when !string.IsNullOrWhiteSpace(request.Message) => request.Message.Trim(),
            "critical-answer" => request.Message,
            "call-initiated" => "The user marked an emergency call as initiated. Connection is not yet confirmed.",
            "call-connected" when request.UserConfirmed => "Emergency call connection was explicitly confirmed by the user; provider connection is not independently verified.",
            "call-connected" => throw new InvalidDataException("Call connection requires explicit user confirmation."),
            "contact-acknowledged" when userId is not null => "An emergency participant acknowledged the session.",
            "patient-departed" when userId is not null && session.OwnerId == userId => "The session owner reported that the patient departed.",
            "patient-arrived" when userId is not null && session.OwnerId == userId => "The session owner reported that the patient arrived at hospital.",
            _ => throw new InvalidDataException("Timeline event type is not allowlisted.")
        };
        var existing = session.Timeline.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is null)
        {
            if (request.Type == "patient-departed" && SessionTransitions.CanTransition(session.Status, SessionStatus.Departed)) session.Status = SessionStatus.Departed;
            if (request.Type == "patient-arrived" && SessionTransitions.CanTransition(session.Status, SessionStatus.ArrivedAtHospital)) session.Status = SessionStatus.ArrivedAtHospital;
            existing = AddTimeline(session, request.Type, serverMessage, request.IdempotencyKey, userId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await realtime.NotifySessionAsync(session.Id, "TimelineAdded", Map(existing), cancellationToken);
        }

        return Map(session);
    }

    public async Task<SessionResponse> AddLocationAsync(Guid sessionId, Guid? userId, LocationUpdateRequest request, CancellationToken cancellationToken, string? anonymousAccessToken = null)
    {
        if (!request.ConsentProvided) throw new InvalidOperationException("Location sharing requires explicit consent.");
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180) throw new InvalidDataException("Location coordinates are invalid.");
        if (request.Description?.Length > 300) throw new InvalidDataException("Location description must be 300 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128) throw new InvalidDataException("A valid idempotency key is required.");
        var session = await LoadAuthorizedAsync(sessionId, userId, anonymousAccessToken, cancellationToken);
        if (session.Timeline.All(x => x.IdempotencyKey != request.IdempotencyKey))
        {
            session.Locations.Add(new EmergencyLocation
            {
                EmergencySessionId = session.Id,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Description = request.Description?.Trim(),
                ConsentProvided = true
            });
            AddTimeline(session, "location-updated", "Location was shared with consent.", request.IdempotencyKey, userId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await realtime.NotifySessionAsync(session.Id, "SessionUpdated", new { session.Id, type = "location-updated" }, cancellationToken);
        }
        return Map(session);
    }

    public async Task<SessionResponse> AddTaskAsync(Guid sessionId, Guid userId, CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, null, cancellationToken);
        var allowed = TaskCatalogue.Get(request.TaskCode);
        var task = session.Tasks.SingleOrDefault(x => x.TaskCode == allowed.Code);
        if (task is null)
        {
            task = new EmergencyTask
            {
                EmergencySessionId = session.Id, TaskCode = allowed.Code, Title = allowed.Title,
                IsCritical = allowed.IsCritical, AssignedParticipantId = request.AssignedParticipantId,
                Status = request.AssignedParticipantId is null ? Domain.TaskStatus.Suggested : Domain.TaskStatus.Assigned
            };
            session.Tasks.Add(task);
            AddTimeline(session, "task-assigned", $"Coordination task assigned: {allowed.Title}", $"task-created:{task.Id}", userId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        await realtime.NotifySessionAsync(session.Id, "TaskUpdated", Map(task), cancellationToken);
        return Map(session);
    }

    public async Task<SessionResponse> UpdateTaskAsync(Guid sessionId, Guid taskId, Guid userId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, null, cancellationToken);
        var task = session.Tasks.SingleOrDefault(x => x.Id == taskId) ?? throw new KeyNotFoundException("Task not found.");
        if (task.ConcurrencyToken != request.ConcurrencyToken) throw new DbUpdateConcurrencyException("The task was updated by another participant.");
        if (task.Status == Domain.TaskStatus.Completed && request.Status == Domain.TaskStatus.Completed) return Map(session);
        if (!CanTransitionTask(task.Status, request.Status)) throw new InvalidOperationException("The requested task status transition is not allowed.");
        task.Status = request.Status;
        task.AssignedParticipantId = request.AssignedParticipantId ?? task.AssignedParticipantId;
        if (request.Status == Domain.TaskStatus.Accepted) task.AcceptedAtUtc = clock.UtcNow;
        if (request.Status == Domain.TaskStatus.Completed) task.CompletedAtUtc = clock.UtcNow;
        AddTimeline(session, $"task-{request.Status.ToString().ToLowerInvariant()}", $"Coordination task {request.Status.ToString().ToLowerInvariant()}: {task.Title}", $"task:{task.Id}:{request.Status}", userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await realtime.NotifySessionAsync(session.Id, "TaskUpdated", Map(task), cancellationToken);
        return Map(session);
    }

    public async Task<EmergencySummary> GenerateSummaryAsync(Guid sessionId, Guid userId, SummaryKind kind, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, null, cancellationToken);
        var facts = DeserializeFacts(session.IncidentFactsJson);
        var lines = new List<string>
        {
            $"Summary type: {kind}",
            SafetyNotice.ClinicalReview,
            "Golden Hour AI is not a doctor, diagnostic system, ambulance provider, or replacement for emergency services.",
            $"Protocol: {session.ProtocolId ?? "not selected"} {session.ProtocolVersion ?? ""}",
            $"Original description: {session.OriginalInput ?? "not provided"}",
            $"Reported observations: {string.Join("; ", facts?.Observations ?? [])}",
            $"Unknown or uncertain: {string.Join("; ", facts?.Uncertainties ?? ["Incident interpretation not confirmed."])}",
            $"AI confidence: {(session.AiConfidence is null ? "not available" : session.AiConfidence.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))}"
        };
        if (kind == SummaryKind.HospitalHandover)
        {
            lines.Add("Chronological timeline:");
            lines.AddRange(session.Timeline.OrderBy(x => x.Sequence).Select(x => $"{x.CreatedAtUtc:O} [{x.Type}] {x.Message}"));
        }
        var summary = new EmergencySummary
        {
            EmergencySessionId = session.Id,
            Kind = kind,
            Content = string.Join('\n', lines),
            Language = session.OriginalLanguage ?? "en",
            ProtocolVersion = session.ProtocolVersion ?? "none"
        };
        dbContext.EmergencySummaries.Add(summary);
        AddTimeline(session, "summary-generated", $"{kind} summary generated from labelled reported facts.", $"summary:{summary.Id}", userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return summary;
    }

    public async Task<SessionResponse> CloseAsync(Guid sessionId, Guid userId, Guid concurrencyToken, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, null, cancellationToken);
        if (session.OwnerId != userId) throw new UnauthorizedAccessException("Only the session owner can close the session.");
        if (session.ConcurrencyToken != concurrencyToken) throw new DbUpdateConcurrencyException("The session was updated by another participant.");
        if (session.Status != SessionStatus.Closed)
        {
            if (!SessionTransitions.CanTransition(session.Status, SessionStatus.Closed)) throw new InvalidOperationException("Session cannot be closed from its current status.");
            session.Status = SessionStatus.Closed;
            AddTimeline(session, "session-closed", "Emergency coordination session closed by its owner.", $"session-closed:{session.Id}", userId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await realtime.NotifySessionAsync(session.Id, "SessionUpdated", new { session.Id, session.Status }, cancellationToken);
        }
        return Map(session);
    }

    private async Task<EmergencySession> LoadAuthorizedAsync(Guid sessionId, Guid? userId, string? anonymousAccessToken, CancellationToken cancellationToken)
    {
        var session = await Query().SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Emergency session not found.");
        var userAuthorized = userId is not null && (session.OwnerId == userId || session.Participants.Any(x => x.UserId == userId));
        var grantAuthorized = !userAuthorized && userId is null && await HasValidAnonymousGrantAsync(sessionId, anonymousAccessToken, cancellationToken);
        if (!userAuthorized && !grantAuthorized)
        {
            throw new UnauthorizedAccessException("You do not have access to this emergency session.");
        }
        return session;
    }

    private async Task<bool> HasValidAnonymousGrantAsync(Guid sessionId, string? rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return false;
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var candidates = await dbContext.EmergencyShareTokens
            .Where(x => x.EmergencySessionId == sessionId && x.Purpose == ShareTokenPurpose.AnonymousSession
                && x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow)
            .Select(x => x.TokenHash).ToListAsync(cancellationToken);
        return candidates.Any(hash => CryptographicOperations.FixedTimeEquals(hash, suppliedHash));
    }

    internal IQueryable<EmergencySession> Query() => dbContext.EmergencySessions
        .Include(x => x.Tasks).Include(x => x.Timeline).Include(x => x.Participants)
        .Include(x => x.Locations).Include(x => x.Observations).Include(x => x.Summaries);

    internal SessionResponse Map(EmergencySession session)
    {
        ProtocolResponse? protocol = null;
        if (session.ProtocolId is not null)
        {
            var selected = protocolCatalogue.Get(session.ProtocolId);
            protocol = Map(selected);
        }
        var facts = DeserializeFacts(session.IncidentFactsJson);
        return new SessionResponse(
            session.Id, session.Status, session.SelectedCategory, session.PatientRelationship,
            emergencyOptions.Value.DefaultNumber, session.CreatedAtUtc, session.UpdatedAtUtc,
            session.OriginalInput, session.OriginalLanguage, session.NormalizedTranscript, facts,
            !session.InterpretationConfirmed,
            protocol,
            session.Tasks.OrderBy(x => x.CreatedAtUtc).Select(Map).ToArray(),
            session.Timeline.OrderBy(x => x.Sequence).Select(Map).ToArray(),
            session.Participants.OrderBy(x => x.CreatedAtUtc).Select(x => new ParticipantResponse(x.Id, x.DisplayName, x.Role, x.AcknowledgedAtUtc)).ToArray(),
            session.Locations.OrderBy(x => x.CreatedAtUtc).Select(x => new LocationResponse(x.Id, x.Latitude, x.Longitude, x.Description, x.CreatedAtUtc)).ToArray(),
            session.ConcurrencyToken, null);
    }

    internal static ProtocolResponse Map(ProtocolDefinition x) => new(x.Id, x.Version, x.ReviewStatus, x.Notice, x.EmergencyCallInstruction, x.DoActions, x.DoNotActions, x.EscalationRule);
    internal static TaskResponse Map(EmergencyTask x) => new(x.Id, x.TaskCode, x.Title, x.IsCritical, x.Status, x.AssignedParticipantId, x.AcceptedAtUtc, x.CompletedAtUtc, x.ConcurrencyToken);
    internal static TimelineEventResponse Map(EmergencyTimelineEvent x) => new(x.Id, x.Sequence, x.Type, x.Message, x.CreatedAtUtc);

    private static EmergencyTimelineEvent AddTimeline(EmergencySession session, string type, string message, string idempotencyKey, Guid? actorUserId)
    {
        var entry = new EmergencyTimelineEvent
        {
            EmergencySessionId = session.Id,
            Sequence = session.Timeline.Count == 0 ? 1 : session.Timeline.Max(x => x.Sequence) + 1,
            Type = type,
            Message = message,
            IdempotencyKey = idempotencyKey,
            ActorUserId = actorUserId
        };
        session.Timeline.Add(entry);
        return entry;
    }

    private static IncidentExtraction? DeserializeFacts(string? json) => string.IsNullOrWhiteSpace(json)
        ? null
        : JsonSerializer.Deserialize<IncidentExtraction>(json, JsonOptions);

    private static bool CanTransitionTask(Domain.TaskStatus current, Domain.TaskStatus next) => (current, next) switch
    {
        (Domain.TaskStatus.Suggested, Domain.TaskStatus.Assigned or Domain.TaskStatus.Accepted or Domain.TaskStatus.Declined) => true,
        (Domain.TaskStatus.Assigned, Domain.TaskStatus.Accepted or Domain.TaskStatus.Declined or Domain.TaskStatus.Assigned) => true,
        (Domain.TaskStatus.Accepted, Domain.TaskStatus.Completed or Domain.TaskStatus.Declined) => true,
        (Domain.TaskStatus.Declined, Domain.TaskStatus.Assigned) => true,
        _ => false
    };

    private static object CreateProfileSnapshot(EmergencyProfile profile) => new
    {
        profile.FullName,
        profile.DateOfBirth,
        profile.PreferredLanguage,
        allergies = profile.Allergies.Select(x => x.Name),
        conditions = profile.Conditions.Select(x => x.Name),
        medications = profile.Medications.Select(x => x.Name),
        procedures = profile.Procedures.Select(x => new { x.Name, x.Year }),
        contact = profile.Contacts.FirstOrDefault() is { } contact ? new { contact.Name, contact.Relationship, contact.PhoneNumber } : null,
        sharing = profile.SharingPreference
    };
}

public sealed class ShareTokenCoordinator(
    GoldenHourDbContext dbContext,
    ProtocolCatalogue protocols,
    IClock clock,
    IOptions<EmergencyOptions> emergencyOptions,
    IRealtimeNotifier realtime)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ShareTokenResponse> CreateAsync(Guid sessionId, Guid ownerId, int lifetimeMinutes, CancellationToken cancellationToken)
    {
        var session = await dbContext.EmergencySessions.SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Emergency session not found.");
        if (session.OwnerId != ownerId) throw new UnauthorizedAccessException("Only the session owner can create a share link.");
        var lifetime = Math.Clamp(lifetimeMinutes, 5, 120);
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var entity = new EmergencyShareToken
        {
            EmergencySessionId = sessionId,
            Purpose = ShareTokenPurpose.BystanderView,
            TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(raw)),
            ExpiresAtUtc = clock.UtcNow.AddMinutes(lifetime)
        };
        dbContext.EmergencyShareTokens.Add(entity);
        dbContext.AuditEvents.Add(new AuditEvent { ActorUserId = ownerId, Action = "share-token-created", ResourceType = "EmergencySession", ResourceId = sessionId.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ShareTokenResponse(entity.Id, raw, entity.ExpiresAtUtc, $"/bystander/{raw}");
    }

    public async Task RevokeAsync(Guid sessionId, Guid tokenId, Guid ownerId, CancellationToken cancellationToken)
    {
        var session = await dbContext.EmergencySessions.SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Emergency session not found.");
        if (session.OwnerId != ownerId) throw new UnauthorizedAccessException("Only the session owner can revoke a share link.");
        var token = await dbContext.EmergencyShareTokens.SingleOrDefaultAsync(x => x.Id == tokenId && x.EmergencySessionId == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Share token not found.");
        token.RevokedAtUtc ??= clock.UtcNow;
        dbContext.AuditEvents.Add(new AuditEvent { ActorUserId = ownerId, Action = "share-token-revoked", ResourceType = "EmergencyShareToken", ResourceId = token.Id.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(EmergencyShareToken Token, EmergencySession Session)> ResolveAsync(string rawToken, CancellationToken cancellationToken)
    {
        byte[] hash;
        try { hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)); }
        catch { throw new UnauthorizedAccessException("Invalid or expired share link."); }
        var candidates = await dbContext.EmergencyShareTokens.Where(x => x.Purpose == ShareTokenPurpose.BystanderView && x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow).ToListAsync(cancellationToken);
        var token = candidates.SingleOrDefault(x => CryptographicOperations.FixedTimeEquals(x.TokenHash, hash))
            ?? throw new UnauthorizedAccessException("Invalid or expired share link.");
        var session = await dbContext.EmergencySessions.Include(x => x.Locations).Include(x => x.Timeline).Include(x => x.Observations)
            .SingleAsync(x => x.Id == token.EmergencySessionId, cancellationToken);
        token.LastAccessedAtUtc = clock.UtcNow;
        dbContext.AuditEvents.Add(new AuditEvent { Action = "bystander-share-accessed", ResourceType = "EmergencyShareToken", ResourceId = token.Id.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        return (token, session);
    }

    public async Task<BystanderSessionResponse> GetProjectionAsync(string rawToken, CancellationToken cancellationToken)
    {
        var (token, session) = await ResolveAsync(rawToken, cancellationToken);
        ProtocolResponse? protocol = session.ProtocolId is null ? null : SessionCoordinator.Map(protocols.Get(session.ProtocolId));
        var profile = ParseSharedProfile(session.ProfileSnapshotJson);
        var location = session.Locations.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        var locationText = location?.Description ?? (location?.Latitude is not null && location.Longitude is not null
            ? $"{location.Latitude.Value.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)}, {location.Longitude.Value.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)}"
            : "Location not provided");
        var contact = profile.EmergencyContact;
        return new BystanderSessionResponse(
            session.Id,
            profile.Name ?? "Emergency patient",
            profile.ApproximateAge ?? 0,
            session.SelectedCategory,
            locationText,
            profile.Allergies,
            profile.Conditions,
            profile.Medications,
            new BystanderContactResponse(contact?.Name ?? "Not shared", contact?.Relationship ?? string.Empty, contact?.PhoneNumber ?? string.Empty),
            token.ExpiresAtUtc,
            emergencyOptions.Value.DefaultNumber,
            protocol);
    }

    public async Task RecordObservationAsync(string rawToken, BystanderObservationRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128) throw new InvalidDataException("A valid Idempotency-Key header is required.");
        if (request.Conscious is null && request.BreathingNormally is null && request.SevereBleeding is null)
            throw new InvalidDataException("At least one observation is required.");
        var (token, session) = await ResolveAsync(rawToken, cancellationToken);
        if (session.Timeline.Any(x => x.IdempotencyKey == idempotencyKey)) return;
        Add("conscious", request.Conscious);
        Add("breathing-normally", request.BreathingNormally);
        Add("heavy-bleeding", request.SevereBleeding);
        session.Timeline.Add(new EmergencyTimelineEvent
        {
            EmergencySessionId = session.Id,
            Sequence = session.Timeline.Count == 0 ? 1 : session.Timeline.Max(x => x.Sequence) + 1,
            Type = "bystander-observation",
            Message = "A bystander reported visible observations; facts remain unconfirmed.",
            IdempotencyKey = idempotencyKey
        });
        dbContext.AuditEvents.Add(new AuditEvent { Action = "bystander-observation-recorded", ResourceType = "EmergencyShareToken", ResourceId = token.Id.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        await realtime.NotifySessionAsync(session.Id, "TimelineAdded", new { type = "bystander-observation" }, cancellationToken);

        void Add(string kind, TernaryAnswer? answer)
        {
            if (answer is null) return;
            session.Observations.Add(new EmergencyObservation
            {
                EmergencySessionId = session.Id,
                Kind = kind,
                Value = JsonNamingPolicy.SnakeCaseLower.ConvertName(answer.Value.ToString()),
                Source = "bystander-token",
                IsConfirmed = false
            });
        }
    }

    public async Task RecordLocationAsync(string rawToken, BystanderLocationRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!request.ConsentConfirmed) throw new InvalidDataException("Location sharing requires explicit consent.");
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180) throw new InvalidDataException("Location coordinates are invalid.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128) throw new InvalidDataException("A valid Idempotency-Key header is required.");
        var (token, session) = await ResolveAsync(rawToken, cancellationToken);
        if (session.Timeline.Any(x => x.IdempotencyKey == idempotencyKey)) return;
        session.Locations.Add(new EmergencyLocation
        {
            EmergencySessionId = session.Id,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ConsentProvided = true
        });
        session.Timeline.Add(new EmergencyTimelineEvent
        {
            EmergencySessionId = session.Id,
            Sequence = session.Timeline.Count == 0 ? 1 : session.Timeline.Max(x => x.Sequence) + 1,
            Type = "bystander-location",
            Message = "A bystander shared the current location with consent.",
            IdempotencyKey = idempotencyKey
        });
        dbContext.AuditEvents.Add(new AuditEvent { Action = "bystander-location-recorded", ResourceType = "EmergencyShareToken", ResourceId = token.Id.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        await realtime.NotifySessionAsync(session.Id, "SessionUpdated", new { type = "bystander-location" }, cancellationToken);
    }

    private BystanderProfileResponse ParseSharedProfile(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return new BystanderProfileResponse(null, null, [], [], [], null);
        using var document = JsonDocument.Parse(snapshotJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("sharing", out var sharing) || sharing.ValueKind == JsonValueKind.Null)
            return new BystanderProfileResponse(null, null, [], [], [], null);
        bool Shared(string property) => sharing.TryGetProperty(property, out var value) && value.GetBoolean();
        var name = Shared("shareName") ? root.GetProperty("fullName").GetString() : null;
        int? age = null;
        if (Shared("shareApproximateAge") && root.TryGetProperty("dateOfBirth", out var dob) && dob.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(dob.GetString(), out var date))
        {
            age = clock.UtcNow.Year - date.Year;
        }
        static string[] Names(JsonElement rootElement, string property) => rootElement.TryGetProperty(property, out var values)
            ? values.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>().ToArray() : [];
        EmergencyContact? contact = null;
        if (Shared("shareEmergencyContact") && root.TryGetProperty("contact", out var contactElement) && contactElement.ValueKind == JsonValueKind.Object)
        {
            contact = new EmergencyContact
            {
                Name = contactElement.GetProperty("name").GetString() ?? string.Empty,
                Relationship = contactElement.TryGetProperty("relationship", out var relationship) ? relationship.GetString() ?? string.Empty : string.Empty,
                PhoneNumber = contactElement.GetProperty("phoneNumber").GetString() ?? string.Empty
            };
        }
        return new BystanderProfileResponse(name, age,
            Shared("shareAllergies") ? Names(root, "allergies") : [],
            Shared("shareConditions") ? Names(root, "conditions") : [],
            Shared("shareMedications") ? Names(root, "medications") : [], contact);
    }
}
