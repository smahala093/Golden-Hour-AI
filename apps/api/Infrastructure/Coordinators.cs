using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
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
        var isNewProfile = profile is null;
        var verifiedPhoneNumbers = profile?.Contacts
            .Where(contact => contact.IsVerified)
            .Select(contact => NormalizePhoneNumber(contact.PhoneNumber))
            .ToHashSet(StringComparer.Ordinal) ?? [];
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
            // Verification is server-owned. A profile payload cannot promote its own contact.
            IsVerified = verifiedPhoneNumbers.Contains(NormalizePhoneNumber(x.PhoneNumber))
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
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = ownerId,
            Action = isNewProfile ? "emergency-profile-created" : "emergency-profile-updated",
            ResourceType = "EmergencyProfile",
            ResourceId = profile.Id.ToString()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    private static string NormalizePhoneNumber(string value) =>
        string.Concat(value.Where(char.IsAsciiDigit));

    public async Task<ReadinessResult> ReadinessAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var profile = await Query().SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken)
            ?? throw new KeyNotFoundException("Emergency profile not found.");
        var hasActiveShare = await dbContext.EmergencyShareTokens.AnyAsync(
            x => x.Purpose == ShareTokenPurpose.BystanderView && x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow
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
    IOptions<EmergencyOptions> emergencyOptions,
    IValidator<IncidentExtraction> extractionValidator,
    ILogger<SessionCoordinator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SessionResponse> CreateAsync(Guid? ownerId, CreateSessionRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        ValidateCommandKey(idempotencyKey);
        if (string.IsNullOrWhiteSpace(request.CountryCode) || request.CountryCode.Length != 2 || !request.CountryCode.All(char.IsAsciiLetter))
            throw new InvalidDataException("Country code must contain two letters.");
        if (request.TypedLocation?.Length > 300) throw new InvalidDataException("Typed location must be 300 characters or fewer.");
        if (request.UseOwnerProfileForPatient && ownerId is null)
            throw new UnauthorizedAccessException("Authentication is required to use a stored emergency profile.");
        if (request.UseOwnerProfileForPatient && request.PatientRelationship is not (PatientRelationship.Self or PatientRelationship.Family))
            throw new InvalidDataException("A stored owner profile may be selected only for self or family-patient sessions.");
        var createKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{ownerId?.ToString("N") ?? "anonymous"}:{idempotencyKey}"));
        var requestHash = HashCreateRequest(request);
        var existing = await Query().SingleOrDefaultAsync(
            x => x.CreateIdempotencyKeyHash != null && x.CreateIdempotencyKeyHash.SequenceEqual(createKeyHash), cancellationToken);
        if (existing is not null)
        {
            if (existing.CreateRequestHash is null || !CryptographicOperations.FixedTimeEquals(existing.CreateRequestHash, requestHash))
                throw new InvalidOperationException("The idempotency key was already used for a different session request.");
            if (ownerId is null)
                throw new InvalidOperationException("The anonymous access grant is returned only once and cannot be replayed; start a new session with a new Idempotency-Key after an indeterminate response.");
            return Map(existing);
        }
        EmergencyProfile? profile = null;
        var shouldSnapshotOwnerProfile = ownerId is not null
            && (request.PatientRelationship == PatientRelationship.Self
                || request.PatientRelationship == PatientRelationship.Family && request.UseOwnerProfileForPatient);
        if (shouldSnapshotOwnerProfile)
        {
            profile = await dbContext.EmergencyProfiles.Include(x => x.Allergies).Include(x => x.Conditions)
                .Include(x => x.Medications).Include(x => x.Procedures).Include(x => x.Contacts)
                .Include(x => x.SharingPreference).SingleOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);
        }

        var session = new EmergencySession
        {
            OwnerId = ownerId,
            EmergencyProfileId = profile?.Id,
            CreateIdempotencyKeyHash = createKeyHash,
            CreateRequestHash = requestHash,
            PatientRelationship = request.PatientRelationship,
            SelectedCategory = request.SelectedCategory,
            CountryCode = request.CountryCode.Trim().ToUpperInvariant(),
            ProfileSnapshotJson = profile is null ? null : JsonSerializer.Serialize(CreateProfileSnapshot(profile, clock.UtcNow), JsonOptions)
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

    public async Task EnsureAuthorizedAsync(Guid sessionId, Guid? userId, string? anonymousAccessToken, CancellationToken cancellationToken)
    {
        _ = await LoadAuthorizedAsync(sessionId, userId, anonymousAccessToken, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionResponse>> ListAsync(Guid userId, int limit, DateTime? beforeUtc, CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 100);
        var query = Query().Where(x => x.OwnerId == userId || x.Participants.Any(participant => participant.UserId == userId));
        if (beforeUtc is not null) query = query.Where(x => x.UpdatedAtUtc < beforeUtc.Value);
        var sessions = await query.OrderByDescending(x => x.UpdatedAtUtc).Take(boundedLimit).ToListAsync(cancellationToken);
        return sessions.Select(Map).ToArray();
    }

    public async Task<SessionResponse> SubmitIncidentAsync(Guid sessionId, Guid? userId, SubmitIncidentRequest request, string idempotencyKey, CancellationToken cancellationToken, string? anonymousAccessToken = null)
    {
        ValidateCommandKey(idempotencyKey);
        var session = await LoadAuthorizedAsync(sessionId, userId, anonymousAccessToken, cancellationToken);
        var incidentTimelineKey = $"incident:{idempotencyKey}";
        if (session.Timeline.Any(x => x.IdempotencyKey == incidentTimelineKey)) return Map(session);
        if (session.Status == SessionStatus.Closed) throw new InvalidOperationException("Closed sessions cannot accept incident updates.");

        IncidentInterpretation interpretation;
        if (request.SkipAi)
        {
            var category = request.FallbackCategory ?? session.SelectedCategory;
            interpretation = new IncidentInterpretation(
                new IncidentExtraction(
                    request.SelectedLanguage ?? "und", 0, category, session.PatientRelationship,
                    [], null,
                    TernaryAnswer.Unknown, TernaryAnswer.Unknown, TernaryAnswer.Unknown, null,
                    UrgencyClassification.Unknown, [
                        new CriticalMissingQuestion("conscious", "Is the person conscious?", CriticalAnswerType.YesNo),
                        new CriticalMissingQuestion("breathing", "Is the person breathing normally?", CriticalAnswerType.YesNo),
                        new CriticalMissingQuestion("heavy-bleeding", "Is heavy bleeding visible?", CriticalAnswerType.YesNo)
                    ], ["AI was skipped; use the preserved original description and confirmed answers."],
                    ["AI interpretation was skipped by the user."], 0),
                true, true, "ai_skipped");
        }
        else
        {
            interpretation = await incidentUnderstanding.UnderstandAsync(
                request.OriginalText,
                request.SelectedLanguage,
                request.FallbackCategory ?? session.SelectedCategory,
                session.PatientRelationship,
                cancellationToken);
        }
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
        // Extracted facts remain unconfirmed until the user explicitly confirms them.
        session.InterpretationConfirmed = false;

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

        IReadOnlyList<string> suggestions = ["call-emergency-services", "stay-with-patient"];
        AiCallMetadata? suggestionMetadata = null;
        var suggestionSucceeded = false;
        string? suggestionFailureCode = null;
        if (!request.SkipAi)
        {
            try
            {
                var suggestionResult = await aiProvider.SuggestCoordinationTaskCodesAsync(interpretation.Extraction, cancellationToken);
                suggestions = suggestionResult.Value;
                suggestionMetadata = suggestionResult.Metadata;
                suggestionSucceeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                suggestionFailureCode = "ai_timeout";
                logger.LogWarning("AI coordination task suggestion timed out; using the reviewed static task set.");
                suggestions = ["call-emergency-services", "stay-with-patient"];
            }
            catch (AiProviderException exception)
            {
                suggestionMetadata = exception.Metadata;
                suggestionFailureCode = exception.Code switch
                {
                    "refusal" => "ai_refusal",
                    "rate_limited" => "ai_rate_limited",
                    "incomplete_output" => "ai_incomplete_output",
                    "malformed_output" or "empty_output" => "ai_malformed_output",
                    "timeout" => "ai_timeout",
                    _ => "ai_unavailable"
                };
                logger.LogWarning("AI coordination task suggestion failed with safe code {FailureCode}; using the reviewed static task set.", suggestionFailureCode);
                suggestions = ["call-emergency-services", "stay-with-patient"];
            }
            catch (Exception exception)
            {
                suggestionFailureCode = "ai_unavailable";
                logger.LogWarning("AI coordination task suggestion was unavailable ({ExceptionType}); using the reviewed static task set.", exception.GetType().Name);
                suggestions = ["call-emergency-services", "stay-with-patient"];
            }
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

        var incidentEventType = request.SkipAi ? "ai-skipped" : interpretation.UsedStaticFallback ? "ai-unavailable" : "incident-understood";
        AddTimeline(session, incidentEventType,
            request.SkipAi ? "The user skipped AI interpretation; category-based static guidance was selected." : interpretation.IsUncertain ? "Reported facts need user confirmation; static guidance remains available." : "Reported facts were extracted for user review.",
            incidentTimelineKey, userId);
        var aiMetadata = interpretation.AiMetadata ?? (request.SkipAi
            ? new AiCallMetadata("incident_extraction", "none-user-skipped", "none", 0, null, null)
            : new AiCallMetadata("incident_extraction", "unknown", "unknown", 0, null, null));
        dbContext.AiOperations.Add(new AiOperation
        {
            EmergencySessionId = session.Id,
            OperationType = aiMetadata.OperationType,
            Provider = aiMetadata.Provider,
            Model = aiMetadata.Model,
            LatencyMilliseconds = aiMetadata.LatencyMilliseconds,
            InputTokens = aiMetadata.InputTokens,
            OutputTokens = aiMetadata.OutputTokens,
            Succeeded = !interpretation.UsedStaticFallback && !request.SkipAi,
            FailureCode = interpretation.FailureCode
        });
        if (!request.SkipAi)
        {
            suggestionMetadata ??= new AiCallMetadata("coordination_task_suggestion", "unknown", "unknown", 0, null, null);
            dbContext.AiOperations.Add(new AiOperation
            {
                EmergencySessionId = session.Id,
                OperationType = suggestionMetadata.OperationType,
                Provider = suggestionMetadata.Provider,
                Model = suggestionMetadata.Model,
                LatencyMilliseconds = suggestionMetadata.LatencyMilliseconds,
                InputTokens = suggestionMetadata.InputTokens,
                OutputTokens = suggestionMetadata.OutputTokens,
                Succeeded = suggestionSucceeded,
                FailureCode = suggestionFailureCode
            });
        }
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new DbUpdateConcurrencyException($"Incident update concurrency conflict for: {string.Join(", ", exception.Entries.Select(x => x.Metadata.ClrType.Name))}.", exception);
        }
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
        if (request.Message is null || request.Message.Length > 500) throw new InvalidDataException("Timeline observations must be 500 characters or fewer.");
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

    public async Task<SessionResponse> ApplyCriticalAnswersAsync(
        Guid sessionId,
        Guid? userId,
        IReadOnlyDictionary<string, string> answers,
        string idempotencyKey,
        string? anonymousAccessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 80) throw new InvalidDataException("A valid Idempotency-Key header is required.");
        if (answers.Count is 0 or > 3) throw new InvalidDataException("Provide one to three critical answers.");
        var session = await LoadAuthorizedAsync(sessionId, userId, anonymousAccessToken, cancellationToken);
        var extraction = DeserializeFacts(session.IncidentFactsJson) ?? throw new InvalidOperationException("Submit the incident description before answering critical questions.");
        if (answers.All(answer => session.Timeline.Any(x => x.IdempotencyKey == $"{idempotencyKey}:{answer.Key}"))) return Map(session);

        var updated = extraction;
        foreach (var (questionId, rawAnswer) in answers)
        {
            if (questionId.Length > 80 || rawAnswer.Length > 100) throw new InvalidDataException("Critical answer is too long.");
            var timelineKey = $"{idempotencyKey}:{questionId}";
            if (session.Timeline.Any(x => x.IdempotencyKey == timelineKey)) continue;
            var answer = rawAnswer.Trim().ToLowerInvariant();
            switch (questionId)
            {
                case "conscious":
                    updated = updated with { IsConscious = ParseTernary(answer) };
                    AddConfirmedObservation("consciousness", answer);
                    break;
                case "breathing":
                case "breathing-normally":
                    updated = updated with { IsBreathingNormally = ParseTernary(answer) };
                    AddConfirmedObservation("breathing-normally", answer);
                    break;
                case "heavy-bleeding":
                    updated = updated with { IsHeavyBleedingReported = ParseTernary(answer) };
                    AddConfirmedObservation("heavy-bleeding", answer);
                    break;
                case "symptom-start-time":
                    updated = updated with { ReportedSymptomStartTime = rawAnswer.Trim() };
                    AddConfirmedObservation("reported-symptom-start-time", rawAnswer.Trim());
                    break;
                case "confirm-facts":
                    var confirmation = ParseTernary(answer);
                    session.InterpretationConfirmed = confirmation == TernaryAnswer.Yes;
                    AddConfirmedObservation("reported-facts-confirmation", answer);
                    break;
                default:
                    throw new InvalidDataException("Critical question ID is not allowlisted.");
            }

            AddTimeline(session, "critical-answer", $"The user confirmed {questionId.Replace('-', ' ')} as {rawAnswer.Trim()}.", timelineKey, userId);
        }

        updated = updated with
        {
            CriticalMissingQuestions = updated.CriticalMissingQuestions
                .Where(question => !answers.ContainsKey(question.Id))
                .ToArray()
        };
        var validation = await extractionValidator.ValidateAsync(updated, cancellationToken);
        if (!validation.IsValid) throw new InvalidDataException("Critical answers did not produce valid incident facts.");
        var protocol = protocolCatalogue.Select(updated, session.SelectedCategory);
        session.IncidentFactsJson = JsonSerializer.Serialize(updated, JsonOptions);
        session.ProtocolId = protocol.Id;
        session.ProtocolVersion = protocol.Version;
        dbContext.AuditEvents.Add(new AuditEvent { ActorUserId = userId, Action = "critical-answers-applied", ResourceType = "EmergencySession", ResourceId = session.Id.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = Map(session);
        await realtime.NotifySessionAsync(session.Id, "SessionUpdated", response, cancellationToken);
        return response;

        void AddConfirmedObservation(string kind, string value)
        {
            session.Observations.Add(new EmergencyObservation
            {
                EmergencySessionId = session.Id,
                Kind = kind,
                Value = value,
                Source = "user-confirmed-answer",
                IsConfirmed = true
            });
        }
    }

    public async Task<SessionResponse> AddLocationAsync(Guid sessionId, Guid? userId, LocationUpdateRequest request, CancellationToken cancellationToken, string? anonymousAccessToken = null)
    {
        if (!request.ConsentProvided) throw new InvalidDataException("Location sharing requires explicit consent.");
        var hasLatitude = request.Latitude is not null;
        var hasLongitude = request.Longitude is not null;
        var hasDescription = !string.IsNullOrWhiteSpace(request.Description);
        if (hasLatitude != hasLongitude || (!hasLatitude && !hasDescription))
            throw new InvalidDataException("Provide both coordinates or a typed location description.");
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

    public async Task<SessionResponse> AddTaskAsync(Guid sessionId, Guid userId, CreateTaskRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, null, cancellationToken);
        var actor = session.Participants.Single(x => x.UserId == userId);
        var isCoordinator = actor.Role is ParticipantRole.Owner or ParticipantRole.Caregiver;
        if (!isCoordinator) throw new UnauthorizedAccessException("Only the session owner or a caregiver can assign tasks.");
        ValidateCommandKey(idempotencyKey);
        var timelineKey = $"task-command:{idempotencyKey}";
        if (session.Timeline.Any(entry => entry.IdempotencyKey == timelineKey)) return Map(session);
        if (request.AssignedParticipantId is not null && session.Participants.All(x => x.Id != request.AssignedParticipantId))
            throw new InvalidDataException("The task assignee must belong to this emergency session.");
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
            AddTimeline(session, "task-assigned", $"Coordination task assigned: {allowed.Title}", timelineKey, userId);
        }
        else if (request.AssignedParticipantId is not null && task.AssignedParticipantId != request.AssignedParticipantId)
        {
            task.AssignedParticipantId = request.AssignedParticipantId;
            task.Status = Domain.TaskStatus.Assigned;
            AddTimeline(session, "task-assigned", $"Coordination task assigned: {allowed.Title}", timelineKey, userId);
        }
        else AddTimeline(session, "task-assignment-unchanged", $"Coordination task already present: {allowed.Title}", timelineKey, userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await realtime.NotifySessionAsync(session.Id, "TaskUpdated", Map(task), cancellationToken);
        return Map(session);
    }

    public async Task<SessionResponse> UpdateTaskAsync(Guid sessionId, Guid taskId, Guid userId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, null, cancellationToken);
        var task = session.Tasks.SingleOrDefault(x => x.Id == taskId) ?? throw new KeyNotFoundException("Task not found.");
        var actor = session.Participants.Single(x => x.UserId == userId);
        var isCoordinator = actor.Role is ParticipantRole.Owner or ParticipantRole.Caregiver;
        if (request.AssignedParticipantId is not null && session.Participants.All(x => x.Id != request.AssignedParticipantId))
            throw new InvalidDataException("The task assignee must belong to this emergency session.");
        var reassigning = request.AssignedParticipantId is not null && request.AssignedParticipantId != task.AssignedParticipantId;
        if ((request.Status == Domain.TaskStatus.Assigned || reassigning) && !isCoordinator)
            throw new UnauthorizedAccessException("Only the session owner or a caregiver can assign or reassign tasks.");
        if (request.Status is Domain.TaskStatus.Accepted or Domain.TaskStatus.Declined or Domain.TaskStatus.Completed
            && !isCoordinator && task.AssignedParticipantId != actor.Id)
            throw new UnauthorizedAccessException("Only the assignee or a session coordinator can change this task status.");
        if (task.Status == Domain.TaskStatus.Completed && request.Status == Domain.TaskStatus.Completed
            && (request.AssignedParticipantId is null || request.AssignedParticipantId == task.AssignedParticipantId)) return Map(session);
        if (task.ConcurrencyToken != request.ConcurrencyToken) throw new DbUpdateConcurrencyException("The task was updated by another participant.");
        if (!CanTransitionTask(task.Status, request.Status)) throw new InvalidOperationException("The requested task status transition is not allowed.");
        task.Status = request.Status;
        task.AssignedParticipantId = request.AssignedParticipantId ?? task.AssignedParticipantId;
        if (request.Status == Domain.TaskStatus.Accepted) task.AcceptedAtUtc = clock.UtcNow;
        if (request.Status == Domain.TaskStatus.Completed) task.CompletedAtUtc = clock.UtcNow;
        AddTimeline(session, $"task-{request.Status.ToString().ToLowerInvariant()}", $"Coordination task {request.Status.ToString().ToLowerInvariant()}: {task.Title}", $"task:{task.Id}:{request.ConcurrencyToken:N}:{request.Status}", userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await realtime.NotifySessionAsync(session.Id, "TaskUpdated", Map(task), cancellationToken);
        return Map(session);
    }

    public async Task<EmergencySummary> GenerateSummaryAsync(Guid sessionId, Guid userId, SummaryKind kind, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, null, cancellationToken);
        var facts = DeserializeFacts(session.IncidentFactsJson);
        var snapshot = DeserializeSnapshot(session.ProfileSnapshotJson);
        var lines = new List<string>
        {
            $"Summary type: {kind} (server-generated from allowlisted fields)",
            SafetyNotice.ClinicalReview,
            "Disclaimer: Golden Hour AI is not a doctor, diagnostic system, ambulance provider, or replacement for emergency services."
        };

        if (kind == SummaryKind.Responder)
        {
            AppendPermittedProfile(lines, snapshot);
            lines.Add($"Emergency location [user-shared]: {FormatLocation(session.Locations.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault())}");
            lines.Add($"Reported observations [user/AI-extracted, unconfirmed unless labelled]: {JoinOrUnknown(session.Observations.Where(x => !x.IsConfirmed).Select(x => x.Value).Concat(facts?.Observations ?? []))}");
            lines.Add($"Consciousness [reported fact]: {facts?.IsConscious.ToString() ?? "Unknown"}");
            lines.Add($"Breathing status [reported fact]: {facts?.IsBreathingNormally.ToString() ?? "Unknown"}");
            lines.Add($"Symptom start time [reported fact]: {facts?.ReportedSymptomStartTime ?? "unknown"}");
            lines.Add($"AI uncertainty notice: {JoinOrUnknown(facts?.Uncertainties ?? ["Incident interpretation is not confirmed."])}");
        }
        else if (kind == SummaryKind.HospitalHandover)
        {
            lines.Add("Chronological timeline:");
            lines.AddRange(session.Timeline.OrderBy(x => x.Sequence).Select(x => $"- {x.CreatedAtUtc:O} [{TimelineSource(x)}: {x.Type}] {x.Message}"));
            lines.Add($"Original description [user-reported, unconfirmed]: {session.OriginalInput ?? "not provided"}");
            lines.Add($"Confirmed observations [user-confirmed]: {JoinOrUnknown(session.Observations.Where(x => x.IsConfirmed).Select(x => $"{x.Kind}: {x.Value}"))}");
            lines.Add($"Unconfirmed observations [AI-extracted/user-reported]: {JoinOrUnknown(session.Observations.Where(x => !x.IsConfirmed).Select(x => x.Value).Concat(facts?.Observations ?? []))}");
            AppendPermittedProfile(lines, snapshot);
            lines.Add($"Protocol version [reviewed static protocol]: {session.ProtocolId ?? "not selected"} {session.ProtocolVersion ?? ""}");
            lines.Add($"Languages used: original={session.OriginalLanguage ?? "unknown"}; summary=en");
            lines.Add($"Missing or uncertain information: {JoinOrUnknown(facts?.Uncertainties ?? ["Incident interpretation is not confirmed."])}");
            lines.Add($"AI confidence (not a diagnosis): {(session.AiConfidence is null ? "not available" : session.AiConfidence.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))}");
        }
        else
        {
            lines.Add($"Original description [user-reported]: {session.OriginalInput ?? "not provided"}");
            lines.Add($"Confirmed observations: {JoinOrUnknown(session.Observations.Where(x => x.IsConfirmed).Select(x => $"{x.Kind}: {x.Value}"))}");
            lines.Add($"Unconfirmed observations: {JoinOrUnknown(session.Observations.Where(x => !x.IsConfirmed).Select(x => x.Value))}");
            lines.Add($"Coordination tasks: {JoinOrUnknown(session.Tasks.Select(x => $"{x.Title} ({x.Status})"))}");
            lines.Add($"Protocol: {session.ProtocolId ?? "not selected"} {session.ProtocolVersion ?? ""}");
        }
        var summary = new EmergencySummary
        {
            EmergencySessionId = session.Id,
            Kind = kind,
            Content = string.Join('\n', lines),
            Language = "en",
            ProtocolVersion = session.ProtocolVersion ?? "none"
        };
        dbContext.EmergencySummaries.Add(summary);
        AddTimeline(session, "summary-generated", $"{kind} summary generated from labelled reported facts.", $"summary:{summary.Id}", userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return summary;

        static string JoinOrUnknown(IEnumerable<string> values)
        {
            var materialized = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
            return materialized.Length == 0 ? "unknown or not provided" : string.Join("; ", materialized);
        }

        static string FormatLocation(EmergencyLocation? location)
        {
            if (location is null) return "unknown";
            if (!string.IsNullOrWhiteSpace(location.Description)) return location.Description;
            return location.Latitude is not null && location.Longitude is not null
                ? $"{location.Latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {location.Longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : "unknown";
        }

        static void AppendPermittedProfile(List<string> output, EmergencyProfileSnapshot? profile)
        {
            if (profile is null)
            {
                output.Add("Emergency profile [profile snapshot]: not available");
                return;
            }
            output.Add($"Patient identity [profile snapshot]: {(profile.Sharing.ShareName ? profile.FullName : "not shared")}");
            output.Add($"Approximate age [profile snapshot]: {(profile.Sharing.ShareApproximateAge ? CalculateAge(profile.DateOfBirth, profile.CapturedAtUtc)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown" : "not shared")}");
            output.Add($"Critical allergies [profile snapshot]: {(profile.Sharing.ShareAllergies ? JoinOrUnknown(profile.Allergies) : "not shared")}");
            output.Add($"Relevant conditions [profile snapshot]: {(profile.Sharing.ShareConditions ? JoinOrUnknown(profile.Conditions) : "not shared")}");
            output.Add($"Current medicines [profile snapshot, no dosage advice]: {(profile.Sharing.ShareMedications ? JoinOrUnknown(profile.Medications) : "not shared")}");
            output.Add($"Previous procedures [profile snapshot]: {(profile.Sharing.ShareConditions ? JoinOrUnknown(profile.Procedures.Select(procedure => procedure.Year is null ? procedure.Name : $"{procedure.Name} ({procedure.Year})")) : "not shared")}");
            output.Add($"Emergency contact [profile snapshot]: {(profile.Sharing.ShareEmergencyContact && profile.EmergencyContact is not null ? $"{profile.EmergencyContact.Name}, {profile.EmergencyContact.Relationship}, {profile.EmergencyContact.PhoneNumber}" : "not shared")}");
        }
    }

    public async Task<SessionResponse> CloseAsync(Guid sessionId, Guid userId, Guid concurrencyToken, CancellationToken cancellationToken)
    {
        var session = await LoadAuthorizedAsync(sessionId, userId, null, cancellationToken);
        if (session.OwnerId != userId) throw new UnauthorizedAccessException("Only the session owner can close the session.");
        if (session.Status == SessionStatus.Closed) return Map(session);
        if (session.ConcurrencyToken != concurrencyToken) throw new DbUpdateConcurrencyException("The session was updated by another participant.");
        if (!SessionTransitions.CanTransition(session.Status, SessionStatus.Closed)) throw new InvalidOperationException("Session cannot be closed from its current status.");
        session.Status = SessionStatus.Closed;
        AddTimeline(session, "session-closed", "Emergency coordination session closed by its owner.", $"session-closed:{session.Id}", userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await realtime.NotifySessionAsync(session.Id, "SessionUpdated", new { session.Id, session.Status }, cancellationToken);
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
        var snapshot = DeserializeSnapshot(session.ProfileSnapshotJson);
        return new SessionResponse(
            session.Id, session.Status, session.SelectedCategory, session.PatientRelationship,
            emergencyOptions.Value.DefaultNumber, session.CreatedAtUtc, session.UpdatedAtUtc,
            session.OriginalInput, session.OriginalLanguage, session.NormalizedTranscript, facts,
            !session.InterpretationConfirmed,
            protocol,
            snapshot is null ? null : MapSnapshot(snapshot),
            session.Observations.OrderBy(x => x.CreatedAtUtc).Select(x => new ObservationResponse(x.Id, x.Kind, x.Value, x.Source, x.IsConfirmed, x.CreatedAtUtc)).ToArray(),
            session.Tasks.OrderBy(x => x.CreatedAtUtc).Select(Map).ToArray(),
            session.Timeline.OrderBy(x => x.Sequence).Select(Map).ToArray(),
            session.Participants.OrderBy(x => x.CreatedAtUtc).Select(x => new ParticipantResponse(x.Id, x.DisplayName, x.Role, x.AcknowledgedAtUtc)).ToArray(),
            session.Locations.OrderBy(x => x.CreatedAtUtc).Select(x => new LocationResponse(x.Id, x.Latitude, x.Longitude, x.Description, x.CreatedAtUtc)).ToArray(),
            session.ConcurrencyToken, null);
    }

    internal static ProtocolResponse Map(ProtocolDefinition x) => new(x.Id, x.Version, x.ReviewStatus, x.Notice, x.EmergencyCallInstruction, x.DoActions, x.DoNotActions, x.EscalationRule);
    internal static TaskResponse Map(EmergencyTask x) => new(x.Id, x.TaskCode, x.Title, x.IsCritical, x.Status, x.AssignedParticipantId, x.AcceptedAtUtc, x.CompletedAtUtc, x.ConcurrencyToken);
    internal static TimelineEventResponse Map(EmergencyTimelineEvent x) => new(x.Id, x.Sequence, x.Type, x.Message, TimelineSource(x), x.CreatedAtUtc);

    private static string TimelineSource(EmergencyTimelineEvent entry)
    {
        if (entry.Type == "critical-answer" || entry.Type == "call-connected") return "confirmed";
        if (entry.Type == "incident-understood") return "ai-extracted";
        if (entry.Type.StartsWith("bystander-", StringComparison.Ordinal)) return "user-reported";
        return entry.ActorUserId is null ? "system" : "user-reported";
    }

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

    private static void ValidateCommandKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 16 or > 80)
            throw new InvalidDataException("An Idempotency-Key header containing 16 to 80 characters is required.");
    }

    private static byte[] HashCreateRequest(CreateSessionRequest request)
    {
        var canonical = $"{request.PatientRelationship}|{request.SelectedCategory}|{request.TypedLocation?.Trim() ?? string.Empty}|{request.CountryCode.Trim().ToUpperInvariant()}|{request.UseOwnerProfileForPatient}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    private static bool CanTransitionTask(Domain.TaskStatus current, Domain.TaskStatus next) => (current, next) switch
    {
        (Domain.TaskStatus.Suggested, Domain.TaskStatus.Assigned or Domain.TaskStatus.Accepted or Domain.TaskStatus.Declined) => true,
        (Domain.TaskStatus.Assigned, Domain.TaskStatus.Accepted or Domain.TaskStatus.Declined or Domain.TaskStatus.Assigned) => true,
        (Domain.TaskStatus.Accepted, Domain.TaskStatus.Completed or Domain.TaskStatus.Declined) => true,
        (Domain.TaskStatus.Declined, Domain.TaskStatus.Assigned) => true,
        _ => false
    };

    private static TernaryAnswer ParseTernary(string value) => value switch
    {
        "yes" => TernaryAnswer.Yes,
        "no" => TernaryAnswer.No,
        "unknown" => TernaryAnswer.Unknown,
        _ => throw new InvalidDataException("Answer must be yes, no, or unknown.")
    };

    private static EmergencyProfileSnapshot CreateProfileSnapshot(EmergencyProfile profile, DateTime capturedAtUtc) => new(
        profile.FullName,
        profile.DateOfBirth,
        profile.Allergies.Select(x => x.Name).ToArray(),
        profile.Conditions.Select(x => x.Name).ToArray(),
        profile.Medications.Select(x => x.Name).ToArray(),
        profile.Procedures.Select(x => new SnapshotProcedureResponse(x.Name, x.Year)).ToArray(),
        profile.Contacts.FirstOrDefault() is { } contact
            ? new SnapshotContactResponse(contact.Name, contact.Relationship, contact.PhoneNumber)
            : null,
        profile.SharingPreference is { } sharing
            ? new SnapshotSharing(sharing.ShareName, sharing.ShareApproximateAge, sharing.ShareAllergies, sharing.ShareConditions, sharing.ShareMedications, sharing.ShareEmergencyContact)
            : new SnapshotSharing(false, false, false, false, false, false),
        capturedAtUtc);

    private static EmergencyProfileSnapshot? DeserializeSnapshot(string? json) => string.IsNullOrWhiteSpace(json)
        ? null
        : JsonSerializer.Deserialize<EmergencyProfileSnapshot>(json, JsonOptions);

    private static PatientSnapshotResponse MapSnapshot(EmergencyProfileSnapshot snapshot) => new(
        snapshot.Sharing.ShareName ? snapshot.FullName : null,
        snapshot.Sharing.ShareApproximateAge ? CalculateAge(snapshot.DateOfBirth, snapshot.CapturedAtUtc) : null,
        snapshot.Sharing.ShareAllergies ? snapshot.Allergies : [],
        snapshot.Sharing.ShareConditions ? snapshot.Conditions : [],
        snapshot.Sharing.ShareMedications ? snapshot.Medications : [],
        snapshot.Sharing.ShareConditions ? snapshot.Procedures : [],
        snapshot.Sharing.ShareEmergencyContact ? snapshot.EmergencyContact : null,
        snapshot.CapturedAtUtc);

    private static int? CalculateAge(DateOnly? dateOfBirth, DateTime atUtc)
    {
        if (dateOfBirth is null) return null;
        var date = DateOnly.FromDateTime(atUtc);
        var age = date.Year - dateOfBirth.Value.Year;
        if (date < dateOfBirth.Value.AddYears(age)) age--;
        return age;
    }

    private sealed record SnapshotSharing(
        bool ShareName,
        bool ShareApproximateAge,
        bool ShareAllergies,
        bool ShareConditions,
        bool ShareMedications,
        bool ShareEmergencyContact);

    private sealed record EmergencyProfileSnapshot(
        string FullName,
        DateOnly? DateOfBirth,
        IReadOnlyList<string> Allergies,
        IReadOnlyList<string> Conditions,
        IReadOnlyList<string> Medications,
        IReadOnlyList<SnapshotProcedureResponse> Procedures,
        SnapshotContactResponse? EmergencyContact,
        SnapshotSharing Sharing,
        DateTime CapturedAtUtc);
}

public sealed class ShareTokenCoordinator(
    GoldenHourDbContext dbContext,
    ProtocolCatalogue protocols,
    IClock clock,
    IOptions<EmergencyOptions> emergencyOptions,
    IRealtimeNotifier realtime)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ShareTokenResponse> CreateAsync(Guid sessionId, Guid ownerId, int lifetimeMinutes, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 16 or > 80)
            throw new InvalidDataException("An Idempotency-Key header containing 16 to 80 characters is required.");
        var session = await dbContext.EmergencySessions.Include(x => x.Timeline).SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Emergency session not found.");
        if (session.OwnerId != ownerId) throw new UnauthorizedAccessException("Only the session owner can create a share link.");
        var commandKey = $"share-token:{idempotencyKey}";
        if (session.Timeline.Any(entry => entry.IdempotencyKey == commandKey))
            throw new InvalidOperationException("The share-token response contains a one-time secret and cannot be replayed; use a new Idempotency-Key after an indeterminate response.");
        var lifetime = Math.Clamp(lifetimeMinutes, 5, 120);
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var entity = new EmergencyShareToken
        {
            EmergencySessionId = sessionId,
            Purpose = ShareTokenPurpose.BystanderView,
            TokenHash = tokenHash,
            ExpiresAtUtc = clock.UtcNow.AddMinutes(lifetime)
        };
        dbContext.EmergencyShareTokens.Add(entity);
        session.Timeline.Add(new EmergencyTimelineEvent
        {
            EmergencySessionId = sessionId,
            Sequence = session.Timeline.Count == 0 ? 1 : session.Timeline.Max(entry => entry.Sequence) + 1,
            Type = "share-token-created",
            Message = "A time-limited emergency share link was created.",
            IdempotencyKey = commandKey,
            ActorUserId = ownerId
        });
        dbContext.AuditEvents.Add(new AuditEvent { ActorUserId = ownerId, Action = "share-token-created", ResourceType = "EmergencySession", ResourceId = sessionId.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ShareTokenResponse(entity.Id, raw, entity.ExpiresAtUtc, $"/share#{raw}");
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
            profile.Name,
            profile.ApproximateAge,
            session.SelectedCategory,
            locationText,
            profile.Allergies,
            profile.Conditions,
            profile.Medications,
            contact is null ? null : new BystanderContactResponse(contact.Name, contact.Relationship, contact.PhoneNumber),
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
            var today = DateOnly.FromDateTime(clock.UtcNow);
            age = today.Year - date.Year - (today < date.AddYears(today.Year - date.Year) ? 1 : 0);
        }
        static string[] Names(JsonElement rootElement, string property) => rootElement.TryGetProperty(property, out var values)
            ? values.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>().ToArray() : [];
        EmergencyContact? contact = null;
        if (Shared("shareEmergencyContact") && root.TryGetProperty("emergencyContact", out var contactElement) && contactElement.ValueKind == JsonValueKind.Object)
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
