using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GoldenHour.Api.Infrastructure;

public sealed class ContactVerificationCoordinator(
    GoldenHourDbContext dbContext,
    ISmsProvider smsProvider,
    IClock clock,
    IOptions<AuthenticationOptions> authenticationOptions,
    IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ContactVerificationChallengeResponse> RequestAsync(
        Guid ownerId,
        Guid contactId,
        bool includeDevelopmentCode,
        CancellationToken cancellationToken)
    {
        var contact = await LoadOwnedContactAsync(ownerId, contactId, cancellationToken);
        var normalizedPhone = NormalizePhoneNumber(contact.PhoneNumber);
        if (normalizedPhone.Length is < 7 or > 15)
            throw new InvalidDataException("The contact phone number is not eligible for verification.");

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var lifetimeSeconds = Math.Clamp(configuration.GetValue("ContactVerification:LifetimeSeconds", 300), 30, 600);
        var expiresAtUtc = clock.UtcNow.AddSeconds(lifetimeSeconds);
        var payload = new ChallengePayload(
            ownerId,
            contact.Id,
            PhoneDigest(normalizedPhone),
            new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds(),
            Base64UrlEncode(RandomNumberGenerator.GetBytes(18)));
        var payloadSegment = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signature = Sign(payloadSegment, code);
        var challenge = $"{payloadSegment}.{Base64UrlEncode(signature)}";

        await smsProvider.SendAsync(
            contact.PhoneNumber,
            "contact-verification-code",
            new Dictionary<string, string> { ["code"] = code },
            cancellationToken);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = ownerId,
            Action = "contact-verification-requested",
            ResourceType = "EmergencyContact",
            ResourceId = contact.Id.ToString()
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ContactVerificationChallengeResponse(
            challenge,
            expiresAtUtc,
            includeDevelopmentCode ? "development mock; no SMS delivery occurred" : "verification delivery requested",
            includeDevelopmentCode ? code : null);
    }

    public async Task VerifyAsync(
        Guid ownerId,
        Guid contactId,
        string challenge,
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(challenge) || challenge.Length > 1_024
            || string.IsNullOrWhiteSpace(code) || code.Length != 6 || !code.All(char.IsAsciiDigit))
            throw new UnauthorizedAccessException("The contact verification challenge or code is invalid.");

        var segments = challenge.Split('.', 2, StringSplitOptions.None);
        if (segments.Length != 2)
            throw new UnauthorizedAccessException("The contact verification challenge or code is invalid.");

        ChallengePayload payload;
        byte[] suppliedSignature;
        try
        {
            payload = JsonSerializer.Deserialize<ChallengePayload>(Base64UrlDecode(segments[0]), JsonOptions)
                ?? throw new JsonException();
            suppliedSignature = Base64UrlDecode(segments[1]);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new UnauthorizedAccessException("The contact verification challenge or code is invalid.");
        }

        if (payload.OwnerId == Guid.Empty || payload.ContactId == Guid.Empty
            || payload.PhoneDigest.Length != 43 || !IsBase64Url(payload.PhoneDigest)
            || payload.Nonce.Length is < 20 or > 40 || !IsBase64Url(payload.Nonce)
            || payload.ExpiresAtUnixSeconds <= 0)
            throw new UnauthorizedAccessException("The contact verification challenge or code is invalid.");

        var expectedSignature = Sign(segments[0], code);
        if (suppliedSignature.Length != expectedSignature.Length
            || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature)
            || payload.OwnerId != ownerId || payload.ContactId != contactId)
            throw new UnauthorizedAccessException("The contact verification challenge or code is invalid.");
        var alreadyConsumed = await dbContext.AuditEvents.AnyAsync(
            audit => audit.Action == "contact-verification-consumed" && audit.ResourceId == payload.Nonce,
            cancellationToken);
        if (alreadyConsumed) return;
        if (new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds() > payload.ExpiresAtUnixSeconds)
            throw new UnauthorizedAccessException("The contact verification challenge has expired.");

        var contact = await LoadOwnedContactAsync(ownerId, contactId, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(payload.PhoneDigest),
                Encoding.ASCII.GetBytes(PhoneDigest(NormalizePhoneNumber(contact.PhoneNumber)))))
            throw new UnauthorizedAccessException("The contact phone number changed after verification was requested.");
        if (contact.IsVerified)
            throw new InvalidOperationException("The contact is already verified; request a new challenge after any phone-number change.");

        contact.IsVerified = true;
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = ownerId,
            Action = "contact-verified",
            ResourceType = "EmergencyContact",
            ResourceId = contact.Id.ToString()
        });
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = ownerId,
            Action = "contact-verification-consumed",
            ResourceType = "ContactVerificationChallenge",
            ResourceId = payload.Nonce
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<EmergencyContact> LoadOwnedContactAsync(Guid ownerId, Guid contactId, CancellationToken cancellationToken) =>
        await dbContext.EmergencyContacts.SingleOrDefaultAsync(contact => contact.Id == contactId
            && dbContext.EmergencyProfiles.Any(profile => profile.Id == contact.EmergencyProfileId && profile.OwnerId == ownerId), cancellationToken)
        ?? throw new UnauthorizedAccessException("The emergency contact is not available to this user.");

    private byte[] Sign(string payloadSegment, string code)
    {
        var rootKey = Encoding.UTF8.GetBytes(authenticationOptions.Value.Jwt.SigningKey);
        var purposeKey = HMACSHA256.HashData(rootKey, "golden-hour-contact-verification-v1"u8.ToArray());
        return HMACSHA256.HashData(purposeKey, Encoding.UTF8.GetBytes($"{payloadSegment}.{code}"));
    }

    private static string NormalizePhoneNumber(string value) => string.Concat(value.Where(char.IsAsciiDigit));
    private static string PhoneDigest(string normalizedPhone) => Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPhone)));
    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool IsBase64Url(string value) => value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed record ChallengePayload(
        Guid OwnerId,
        Guid ContactId,
        string PhoneDigest,
        long ExpiresAtUnixSeconds,
        string Nonce);
}
