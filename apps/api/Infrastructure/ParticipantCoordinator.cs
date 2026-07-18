using System.Security.Cryptography;
using System.Text;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoldenHour.Api.Infrastructure;

public sealed class ParticipantCoordinator(
    GoldenHourDbContext dbContext,
    IClock clock,
    IRealtimeNotifier realtime)
{
    public async Task<ParticipantInviteResponse> InviteAsync(Guid sessionId, Guid ownerId, InviteParticipantRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 16 or > 80)
            throw new InvalidDataException("An Idempotency-Key header containing 16 to 80 characters is required.");
        var session = await dbContext.EmergencySessions.Include(x => x.Participants).Include(x => x.Timeline)
            .SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Emergency session not found.");
        if (session.OwnerId != ownerId) throw new UnauthorizedAccessException("Only the session owner can invite participants.");
        if (session.Status == SessionStatus.Closed) throw new InvalidOperationException("Closed sessions cannot invite participants.");
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 100) throw new InvalidDataException("Participant display name is invalid.");
        if (request.Role == ParticipantRole.Owner) throw new InvalidDataException("Owner invitations are not permitted.");
        var commandKey = $"participant-invite:{idempotencyKey}";
        if (session.Timeline.Any(entry => entry.IdempotencyKey == commandKey))
            throw new InvalidOperationException("The participant invitation contains a one-time secret and cannot be replayed; use a new Idempotency-Key after an indeterminate response.");
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        if (session.Participants.Count >= 20) throw new InvalidOperationException("Participant limit reached.");

        var participant = new EmergencyParticipant
        {
            EmergencySessionId = sessionId,
            DisplayName = request.DisplayName.Trim(),
            Role = request.Role
        };
        var expires = clock.UtcNow.AddMinutes(Math.Clamp(request.LifetimeMinutes, 5, 120));
        dbContext.EmergencyParticipants.Add(participant);
        dbContext.EmergencyShareTokens.Add(new EmergencyShareToken
        {
            EmergencySessionId = sessionId,
            EmergencyParticipantId = participant.Id,
            Purpose = ShareTokenPurpose.ParticipantInvite,
            TokenHash = tokenHash,
            ExpiresAtUtc = expires
        });
        session.Timeline.Add(new EmergencyTimelineEvent
        {
            EmergencySessionId = sessionId,
            Sequence = session.Timeline.Count == 0 ? 1 : session.Timeline.Max(entry => entry.Sequence) + 1,
            Type = "participant-invited",
            Message = "A time-limited participant invitation was created.",
            IdempotencyKey = commandKey,
            ActorUserId = ownerId
        });
        dbContext.AuditEvents.Add(new AuditEvent { ActorUserId = ownerId, Action = "participant-invited", ResourceType = "EmergencyParticipant", ResourceId = participant.Id.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ParticipantInviteResponse(participant.Id, raw, expires, $"/emergency/{sessionId}/join#{raw}");
    }

    public async Task<ParticipantResponse> JoinAsync(Guid sessionId, Guid userId, string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) throw new UnauthorizedAccessException("Invalid or expired participant invitation.");
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var candidates = await dbContext.EmergencyShareTokens
            .Where(x => x.EmergencySessionId == sessionId && x.Purpose == ShareTokenPurpose.ParticipantInvite
                && x.RevokedAtUtc == null && x.ExpiresAtUtc > clock.UtcNow)
            .ToListAsync(cancellationToken);
        var invite = candidates.SingleOrDefault(x => CryptographicOperations.FixedTimeEquals(x.TokenHash, suppliedHash))
            ?? throw new UnauthorizedAccessException("Invalid or expired participant invitation.");
        var participant = await dbContext.EmergencyParticipants.SingleAsync(x => x.Id == invite.EmergencyParticipantId, cancellationToken);
        if (participant.UserId is not null && participant.UserId != userId) throw new UnauthorizedAccessException("Invitation was already used.");
        var alreadyJoined = await dbContext.EmergencyParticipants.AnyAsync(x => x.EmergencySessionId == sessionId && x.UserId == userId && x.Id != participant.Id, cancellationToken);
        if (alreadyJoined) throw new InvalidOperationException("User already participates in this session.");
        participant.UserId = userId;
        invite.RevokedAtUtc = clock.UtcNow;
        var sequence = await dbContext.EmergencyTimelineEvents.Where(x => x.EmergencySessionId == sessionId).Select(x => (long?)x.Sequence).MaxAsync(cancellationToken) ?? 0;
        dbContext.EmergencyTimelineEvents.Add(new EmergencyTimelineEvent
        {
            EmergencySessionId = sessionId,
            Sequence = sequence + 1,
            Type = "participant-joined",
            Message = "An invited participant joined the emergency session.",
            IdempotencyKey = $"participant-joined:{participant.Id}",
            ActorUserId = userId
        });
        dbContext.AuditEvents.Add(new AuditEvent { ActorUserId = userId, Action = "participant-joined", ResourceType = "EmergencyParticipant", ResourceId = participant.Id.ToString() });
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = new ParticipantResponse(participant.Id, participant.DisplayName, participant.Role, participant.AcknowledgedAtUtc);
        await realtime.NotifySessionAsync(sessionId, "ParticipantJoined", response, cancellationToken);
        return response;
    }

    public async Task<ParticipantResponse> AcknowledgeAsync(Guid sessionId, Guid participantId, Guid userId, CancellationToken cancellationToken)
    {
        var participant = await dbContext.EmergencyParticipants.SingleOrDefaultAsync(x => x.Id == participantId && x.EmergencySessionId == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException("Emergency participant not found.");
        if (participant.UserId != userId) throw new UnauthorizedAccessException("Participants may only acknowledge their own invitation.");
        if (participant.AcknowledgedAtUtc is null)
        {
            participant.AcknowledgedAtUtc = clock.UtcNow;
            var sequence = await dbContext.EmergencyTimelineEvents.Where(x => x.EmergencySessionId == sessionId).Select(x => (long?)x.Sequence).MaxAsync(cancellationToken) ?? 0;
            dbContext.EmergencyTimelineEvents.Add(new EmergencyTimelineEvent
            {
                EmergencySessionId = sessionId,
                Sequence = sequence + 1,
                Type = "contact-acknowledged",
                Message = "An invited participant acknowledged the emergency session.",
                IdempotencyKey = $"participant-acknowledged:{participant.Id}",
                ActorUserId = userId
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        var response = new ParticipantResponse(participant.Id, participant.DisplayName, participant.Role, participant.AcknowledgedAtUtc);
        await realtime.NotifySessionAsync(sessionId, "ContactAcknowledged", response, cancellationToken);
        return response;
    }
}
