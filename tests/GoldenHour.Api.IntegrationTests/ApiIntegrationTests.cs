using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using GoldenHour.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GoldenHour.Api.IntegrationTests;

[Collection("api")]
public sealed class ApiIntegrationTests(ApiFactory factory)
{
    [Fact]
    public async Task HealthAndProtocolCatalogue_AreAvailableWithoutAuthentication()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        var protocols = await client.GetFromJsonAsync<JsonElement>("/api/v1/protocols?country=IN");

        protocols.ValueKind.Should().Be(JsonValueKind.Array);
        protocols.GetArrayLength().Should().Be(8);
        protocols.EnumerateArray().Should().OnlyContain(item =>
            item.GetProperty("notice").GetString() == "Demonstration guidance requiring clinical review before production use.");
    }

    [Fact]
    public async Task Registration_SetsHttpOnlyStrictCookies_AndProtectsProfile()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/profile")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
        var response = await RegisterAsync(client);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();

        cookies.Should().Contain(x => x.StartsWith("gh_access=", StringComparison.Ordinal) && x.Contains("httponly", StringComparison.OrdinalIgnoreCase) && x.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        cookies.Should().Contain(x => x.StartsWith("gh_refresh=", StringComparison.Ordinal));
        cookies.Should().NotContain(x => x.Contains("secure", StringComparison.OrdinalIgnoreCase), "HTTP Development is the documented local-only exception");
        (await client.GetAsync("/api/v1/profile")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProfileRoundTripAndReadiness_AreResourceScopedAndDeterministic()
    {
        using var client = factory.CreateClient();
        await RegisterAsync(client);
        var request = new
        {
            fullName = "Test Patient", dateOfBirth = "1980-06-20", bloodGroup = "O+", preferredLanguage = "hi", responseMode = "both",
            insuranceDetails = "private demo insurance", doctorContact = "fictional doctor", allergyStatusCompleted = true,
            medicationStatusCompleted = true, locationPermissionReviewed = true, reviewed = true,
            contacts = new[]
            {
                new { name = "Contact One", relationship = "Family", phoneNumber = "+91 90000 10001", isVerified = true },
                new { name = "Contact Two", relationship = "Friend", phoneNumber = "+91 90000 10002", isVerified = false }
            },
            allergies = new[] { new { name = "Penicillin" } }, conditions = Array.Empty<object>(), medications = Array.Empty<object>(), procedures = Array.Empty<object>(),
            preferredHospital = new { name = "Fictional Hospital", phoneNumber = "+91 90000 10112" },
            sharing = new { shareName = false, shareApproximateAge = false, shareAllergies = true, shareConditions = false, shareMedications = false, shareEmergencyContact = false, reviewed = true }
        };

        var update = await client.PutAsJsonAsync("/api/v1/profile", request);
        update.EnsureSuccessStatusCode();
        var profile = await update.Content.ReadFromJsonAsync<JsonElement>();
        profile.GetProperty("insuranceDetails").GetString().Should().Be("private demo insurance");
        profile.GetProperty("locationPermissionReviewed").GetBoolean().Should().BeTrue();
        var readiness = await client.GetFromJsonAsync<JsonElement>("/api/v1/readiness");
        readiness.GetProperty("score").GetInt32().Should().Be(95, "the only incomplete check is an active share link");
    }

    [Fact]
    public async Task AnonymousSessionGrant_AllowsOnlyItsSession_AndPreservesHindi()
    {
        using var client = factory.CreateClient();
        var create = await PostIdempotentJsonAsync(client, "/api/v1/sessions", new { patientRelationship = "bystander", selectedCategory = "chest_pain", typedLocation = "Fictional Jaipur landmark", countryCode = "IN" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = created.GetProperty("id").GetGuid();
        var token = created.GetProperty("anonymousAccessToken").GetString()!;
        token.Should().NotBeNullOrWhiteSpace();

        using var wrongRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/incident")
        {
            Content = JsonContent.Create(new { originalText = DemoIncident.HindiChestPain, selectedLanguage = "hi", fallbackCategory = "chest_pain" })
        };
        wrongRequest.Headers.Add("X-Emergency-Access-Token", "tampered-token");
        wrongRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await client.SendAsync(wrongRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/incident")
        {
            Content = JsonContent.Create(new { originalText = DemoIncident.HindiChestPain, selectedLanguage = "hi", fallbackCategory = "chest_pain" })
        };
        request.Headers.Add("X-Emergency-Access-Token", token);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var incident = await client.SendAsync(request);
        Assert.True(incident.IsSuccessStatusCode, await incident.Content.ReadAsStringAsync());
        var session = await incident.Content.ReadFromJsonAsync<JsonElement>();
        session.GetProperty("originalInput").GetString().Should().Be(DemoIncident.HindiChestPain);
        session.GetProperty("originalLanguage").GetString().Should().Be("hi");
        session.GetProperty("protocol").GetProperty("id").GetString().Should().Be("chest-pain");

        using var scope = factory.Services.CreateScope();
        var hashes = await scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>().EmergencyShareTokens.Select(x => x.TokenHash).ToListAsync();
        hashes.Should().NotContain(hash => Encoding.UTF8.GetString(hash).Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CriticalAnswers_ReselectNotBreathingProtocol_AndRejectUnknownQuestion()
    {
        using var client = factory.CreateClient();
        var (sessionId, token) = await CreateAnonymousIncidentAsync(client);
        using var answer = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/answers")
        {
            Content = JsonContent.Create(new { answers = new Dictionary<string, string> { ["conscious"] = "no", ["breathing"] = "no" } })
        };
        answer.Headers.Add("X-Emergency-Access-Token", token);
        answer.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(answer);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("protocol").GetProperty("id").GetString().Should().Be("unconscious-not-breathing");
        body.GetProperty("incidentFacts").GetProperty("isConscious").GetString().Should().Be("no");

        using var unknown = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/answers")
        {
            Content = JsonContent.Create(new { questionId = "invent-clinical-fact", answer = "yes" })
        };
        unknown.Headers.Add("X-Emergency-Access-Token", token);
        unknown.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await client.SendAsync(unknown)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task BystanderToken_HidesUnsharedFields_AcceptsIdempotentReports_AndCanBeRevoked()
    {
        using var owner = factory.CreateClient();
        await RegisterAsync(owner);
        var sessionId = await CreateAuthenticatedSessionAsync(owner);
        var shareResponse = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/share-tokens", new { lifetimeMinutes = 30 });
        shareResponse.EnsureSuccessStatusCode();
        var share = await shareResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = share.GetProperty("token").GetString()!;
        var tokenId = share.GetProperty("id").GetGuid();
        share.GetProperty("path").GetString().Should().Be($"/share#{token}");

        using var anonymous = factory.CreateClient();
        using var projectionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/bystander");
        projectionRequest.Headers.Add("X-Emergency-Share-Token", token);
        var projectionResponse = await anonymous.SendAsync(projectionRequest);
        var projection = await projectionResponse.Content.ReadFromJsonAsync<JsonElement>();
        (await projectionResponse.Content.ReadAsStringAsync()).Should().NotContain(token);
        projection.TryGetProperty("patientName", out _).Should().BeFalse("null private fields are omitted by the JSON policy");
        projection.TryGetProperty("approximateAge", out _).Should().BeFalse();
        projection.TryGetProperty("emergencyContact", out _).Should().BeFalse();

        var idempotency = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var observation = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bystander/observations") { Content = JsonContent.Create(new { conscious = "yes" }) };
            observation.Headers.Add("X-Emergency-Share-Token", token);
            observation.Headers.Add("Idempotency-Key", idempotency);
            (await anonymous.SendAsync(observation)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
            (await db.EmergencyTimelineEvents.CountAsync(x => x.EmergencySessionId == sessionId && x.IdempotencyKey == idempotency)).Should().Be(1);
        }

        var revoke = await owner.DeleteAsync($"/api/v1/sessions/{sessionId}/share-tokens/{tokenId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var revokedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/bystander");
        revokedRequest.Headers.Add("X-Emergency-Share-Token", token);
        (await anonymous.SendAsync(revokedRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Webhook_RejectsTampering_AndTreatsReplayAsIdempotent()
    {
        using var client = factory.CreateClient();
        var payload = JsonSerializer.Serialize(new { messageId = $"message-{Guid.NewGuid():N}", status = "delivered" });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var deliveryId = Guid.NewGuid().ToString("N");

        var invalid = await SendWebhookAsync(client, payload, timestamp, deliveryId, "00");
        invalid.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiFactory.WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        var first = await SendWebhookAsync(client, payload, timestamp, deliveryId, signature);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var replay = await SendWebhookAsync(client, payload, timestamp, deliveryId, signature);
        replay.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task RefreshRotation_RejectsReusedToken()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var register = await RegisterAsync(client);
        var oldRefresh = ExtractCookie(register, "gh_refresh");
        using var rotate = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        rotate.Headers.Add("Cookie", $"gh_refresh={oldRefresh}");
        var rotated = await client.SendAsync(rotate);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        ExtractCookie(rotated, "gh_refresh").Should().NotBe(oldRefresh);

        using var reuse = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        reuse.Headers.Add("Cookie", $"gh_refresh={oldRefresh}");
        (await client.SendAsync(reuse)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SessionResourceAndSignalRNegotiate_RequireMembership()
    {
        using var owner = factory.CreateClient();
        await RegisterAsync(owner);
        var sessionId = await CreateAuthenticatedSessionAsync(owner);
        using var stranger = factory.CreateClient();
        await RegisterAsync(stranger);

        (await stranger.GetAsync($"/api/v1/sessions/{sessionId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().PostAsync("/hubs/emergency/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await owner.PostAsync("/hubs/emergency/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VoiceUpload_RejectsUnsupportedContentType()
    {
        using var client = factory.CreateClient();
        await RegisterAsync(client);
        var sessionId = await CreateAuthenticatedSessionAsync(client);
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
        multipart.Add(file, "audio", "ignored-name.flac");

        (await client.PostAsync($"/api/v1/sessions/{sessionId}/voice", multipart)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task SensitiveResponses_AreNotCacheable_AndRegistrationAssignsUserRole()
    {
        using var client = factory.CreateClient();
        var response = await RegisterAsync(client);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.CacheControl.Private.Should().BeTrue();
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await manager.FindByIdAsync(user.GetProperty("id").GetGuid().ToString());
        (await manager.GetRolesAsync(stored!)).Should().ContainSingle().Which.Should().Be("User");
    }

    [Fact]
    public async Task ProductionStartup_AddsSecurityHeadersAndInitializesRolesWithoutDemoData()
    {
        using var production = new ProductionApiFactory();
        using var client = production.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://goldenhour.test"),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Content-Security-Policy").Should().BeTrue();
        response.Headers.Contains("Strict-Transport-Security").Should().BeTrue();
        var sharePage = await client.GetAsync("/share");
        sharePage.Headers.CacheControl!.NoStore.Should().BeTrue();
        sharePage.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
        using var scope = production.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        (await roles.RoleExistsAsync("User")).Should().BeTrue();
        (await roles.RoleExistsAsync("Caregiver")).Should().BeTrue();
    }

    [Fact]
    public async Task SessionCreate_RequiresKey_RetriesToSameGrant_AndRejectsChangedPayload()
    {
        using var client = factory.CreateClient();
        var body = new { patientRelationship = "bystander", selectedCategory = "chest_pain", typedLocation = "Demo landmark", countryCode = "IN" };
        (await client.PostAsJsonAsync("/api/v1/sessions", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var key = Guid.NewGuid().ToString("N");
        var firstResponse = await PostIdempotentJsonAsync(client, "/api/v1/sessions", body, key);
        var secondResponse = await PostIdempotentJsonAsync(client, "/api/v1/sessions", body, key);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();

        first.GetProperty("id").GetGuid().Should().Be(second.GetProperty("id").GetGuid());
        first.GetProperty("anonymousAccessToken").GetString().Should().Be(second.GetProperty("anonymousAccessToken").GetString());
        var conflict = await PostIdempotentJsonAsync(client, "/api/v1/sessions",
            new { patientRelationship = "bystander", selectedCategory = "seizure", typedLocation = "Different", countryCode = "IN" }, key);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>().EmergencySessions.CountAsync(x => x.Id == first.GetProperty("id").GetGuid())).Should().Be(1);
    }

    [Fact]
    public async Task IncidentAndPartialCriticalAnswerRetries_CommitEachCommandExactlyOnce()
    {
        using var client = factory.CreateClient();
        var create = await PostIdempotentJsonAsync(client, "/api/v1/sessions", new { patientRelationship = "bystander", selectedCategory = "chest_pain", countryCode = "IN" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = created.GetProperty("id").GetGuid();
        var token = created.GetProperty("anonymousAccessToken").GetString()!;
        var incidentKey = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var incident = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/incident")
            {
                Content = JsonContent.Create(new { originalText = DemoIncident.HindiChestPain, selectedLanguage = "hi", fallbackCategory = "chest_pain" })
            };
            incident.Headers.Add("X-Emergency-Access-Token", token);
            incident.Headers.Add("Idempotency-Key", incidentKey);
            (await client.SendAsync(incident)).EnsureSuccessStatusCode();
        }

        var answersKey = Guid.NewGuid().ToString("N");
        await SendAnswersAsync(client, sessionId, token, answersKey, new Dictionary<string, string> { ["conscious"] = "no" });
        var finalAnswer = await SendAnswersAsync(client, sessionId, token, answersKey, new Dictionary<string, string> { ["conscious"] = "no", ["breathing"] = "yes" });
        var body = await finalAnswer.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("protocol").GetProperty("id").GetString().Should().Be("unconscious-breathing");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
        (await db.AiOperations.CountAsync(x => x.EmergencySessionId == sessionId)).Should().Be(1);
        (await db.EmergencyTimelineEvents.CountAsync(x => x.EmergencySessionId == sessionId && x.IdempotencyKey == $"incident:{incidentKey}")).Should().Be(1);
        (await db.EmergencyObservations.CountAsync(x => x.EmergencySessionId == sessionId && x.Kind == "consciousness")).Should().Be(1);
        (await db.EmergencyObservations.CountAsync(x => x.EmergencySessionId == sessionId && x.Kind == "breathing-normally")).Should().Be(1);
    }

    [Fact]
    public async Task UnconsciousWithUnknownBreathing_UsesUnknownEmergencyProtocol()
    {
        using var client = factory.CreateClient();
        var (sessionId, token) = await CreateAnonymousIncidentAsync(client);

        var response = await SendAnswersAsync(client, sessionId, token, Guid.NewGuid().ToString("N"), new Dictionary<string, string> { ["conscious"] = "no" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("protocol").GetProperty("id").GetString().Should().Be("unknown-emergency");
    }

    [Fact]
    public async Task Snapshot_IsImmutableAndPermissionProjected_ForJoinedParticipantAndSummaries()
    {
        using var owner = factory.CreateClient();
        await RegisterAsync(owner);
        await PutReadyProfileAsync(owner, "Original Allowed Name", shareName: true);
        var sessionId = await CreateAuthenticatedSessionAsync(owner);
        await PutReadyProfileAsync(owner, "Changed After Session", shareName: true);

        using var participant = factory.CreateClient();
        await RegisterAsync(participant);
        var inviteResponse = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/participants/invitations", new { displayName = "Family member", role = "family", lifetimeMinutes = 30 });
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var join = await participant.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/participants/join", new { token = invite.GetProperty("token").GetString() });
        join.EnsureSuccessStatusCode();
        (await join.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("acknowledgedAtUtc", out _).Should().BeFalse();

        var session = await participant.GetFromJsonAsync<JsonElement>($"/api/v1/sessions/{sessionId}");
        var snapshot = session.GetProperty("patientSnapshot");
        snapshot.GetProperty("fullName").GetString().Should().Be("Original Allowed Name");
        snapshot.TryGetProperty("approximateAge", out _).Should().BeFalse();
        snapshot.GetProperty("allergies").GetArrayLength().Should().Be(0);
        snapshot.GetProperty("conditions").GetArrayLength().Should().Be(0);
        snapshot.GetProperty("medications").GetArrayLength().Should().Be(0);
        snapshot.GetProperty("procedures").GetArrayLength().Should().Be(0);
        snapshot.TryGetProperty("emergencyContact", out _).Should().BeFalse();

        var responder = await owner.PostAsync($"/api/v1/sessions/{sessionId}/summaries/responder", null);
        var content = (await responder.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content").GetString()!;
        content.Should().Contain("Original Allowed Name").And.NotContain("Changed After Session").And.NotContain("Private Allergy")
            .And.NotContain("Private Condition").And.NotContain("Private Medicine").And.NotContain("Private Procedure").And.NotContain("private demo insurance");
    }

    [Fact]
    public async Task InviteAcknowledgeAndTasks_EnforceOwnerAssigneeAndSessionBoundaries()
    {
        using var owner = factory.CreateClient();
        await RegisterAsync(owner);
        var sessionId = await CreateAuthenticatedSessionAsync(owner);
        await SubmitAuthenticatedIncidentAsync(owner, sessionId);
        using var family = factory.CreateClient();
        await RegisterAsync(family);
        var inviteResponse = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/participants/invitations", new { displayName = "Assigned family", role = "family", lifetimeMinutes = 30 });
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        var participantId = invite.GetProperty("participantId").GetGuid();
        var join = await family.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/participants/join", new { token = invite.GetProperty("token").GetString() });
        join.EnsureSuccessStatusCode();
        (await family.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/participants/join", new { token = invite.GetProperty("token").GetString() })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var acknowledged = await family.PostAsync($"/api/v1/sessions/{sessionId}/participants/{participantId}/acknowledge", null);
        acknowledged.EnsureSuccessStatusCode();
        (await acknowledged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("acknowledgedAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));

        (await family.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks", new { taskCode = "unlock-entry", assignedParticipantId = participantId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks", new { taskCode = "unlock-entry", assignedParticipantId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var assignedResponse = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks", new { taskCode = "stay-with-patient", assignedParticipantId = participantId });
        assignedResponse.EnsureSuccessStatusCode();
        var assignedSession = await assignedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var task = assignedSession.GetProperty("tasks").EnumerateArray().Single(x => x.GetProperty("code").GetString() == "stay-with-patient");
        var accepted = await family.PatchAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks/{task.GetProperty("id").GetGuid()}", new
        {
            status = "accepted", assignedParticipantId = (Guid?)null, concurrencyToken = task.GetProperty("concurrencyToken").GetGuid()
        });
        accepted.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AudioAndWebhookTrustBoundaries_RejectSpoofingOversizeStaleAndMalformedPayloads()
    {
        using var client = factory.CreateClient();
        await RegisterAsync(client);
        var sessionId = await CreateAuthenticatedSessionAsync(client);
        (await SendAudioAsync(client, sessionId, [1, 2, 3, 4], "audio/webm", "recording.webm")).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var valid = await SendAudioAsync(client, sessionId, [0x1A, 0x45, 0xDF, 0xA3], "audio/webm", "..\\ignored-name.webm");
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
        var oversizedBytes = new byte[5 * 1024 * 1024 + 1];
        new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.CopyTo(oversizedBytes, 0);
        (await SendAudioAsync(client, sessionId, oversizedBytes, "audio/webm", "large.webm")).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        var stalePayload = JsonSerializer.Serialize(new { messageId = "stale", status = "delivered" });
        var staleTimestamp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var staleSignature = SignWebhook(staleTimestamp, stalePayload);
        (await SendWebhookAsync(client, stalePayload, staleTimestamp, Guid.NewGuid().ToString("N"), staleSignature)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        const string malformed = "{not-json";
        (await SendWebhookAsync(client, malformed, currentTimestamp, Guid.NewGuid().ToString("N"), SignWebhook(currentTimestamp, malformed))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var oversizedWebhook = new string('x', 70_000);
        (await SendWebhookAsync(client, oversizedWebhook, currentTimestamp, Guid.NewGuid().ToString("N"), "00")).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task ProtocolReadAloud_SynthesizesOnlyStoredReviewedProtocolText()
    {
        using var client = factory.CreateClient();
        var (sessionId, token) = await CreateAnonymousIncidentAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/protocol-audio");
        request.Headers.Add("X-Emergency-Access-Token", token);

        var response = await client.SendAsync(request);
        var mockAudioText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("audio/mpeg");
        mockAudioText.Should().Contain(SafetyNotice.ClinicalReview).And.NotContain(DemoIncident.HindiChestPain);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "Integration-Only-Password-2026!", preferredLanguage = "en" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return response;
    }

    private static async Task<Guid> CreateAuthenticatedSessionAsync(HttpClient client)
    {
        var response = await PostIdempotentJsonAsync(client, "/api/v1/sessions", new { patientRelationship = "self", selectedCategory = "chest_pain", typedLocation = (string?)null, countryCode = "IN" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid SessionId, string Token)> CreateAnonymousIncidentAsync(HttpClient client)
    {
        var create = await PostIdempotentJsonAsync(client, "/api/v1/sessions", new { patientRelationship = "bystander", selectedCategory = "chest_pain", typedLocation = (string?)null, countryCode = "IN" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = created.GetProperty("id").GetGuid();
        var token = created.GetProperty("anonymousAccessToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/incident")
        {
            Content = JsonContent.Create(new { originalText = DemoIncident.HindiChestPain, selectedLanguage = "hi", fallbackCategory = "chest_pain" })
        };
        request.Headers.Add("X-Emergency-Access-Token", token);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (sessionId, token);
    }

    private static async Task<HttpResponseMessage> SendAnswersAsync(
        HttpClient client,
        Guid sessionId,
        string accessToken,
        string idempotencyKey,
        IReadOnlyDictionary<string, string> answers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/answers")
        {
            Content = JsonContent.Create(new { answers })
        };
        request.Headers.Add("X-Emergency-Access-Token", accessToken);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task PutReadyProfileAsync(HttpClient client, string fullName, bool shareName)
    {
        var response = await client.PutAsJsonAsync("/api/v1/profile", new
        {
            fullName,
            dateOfBirth = "1980-06-20",
            bloodGroup = "O+",
            preferredLanguage = "en",
            responseMode = "both",
            insuranceDetails = "private demo insurance",
            doctorContact = "private doctor",
            allergyStatusCompleted = true,
            medicationStatusCompleted = true,
            locationPermissionReviewed = true,
            reviewed = true,
            contacts = new[]
            {
                new { name = "Private Contact One", relationship = "Family", phoneNumber = "+91 90000 20001", isVerified = true },
                new { name = "Private Contact Two", relationship = "Friend", phoneNumber = "+91 90000 20002", isVerified = false }
            },
            allergies = new[] { new { name = "Private Allergy" } },
            conditions = new[] { new { name = "Private Condition" } },
            medications = new[] { new { name = "Private Medicine" } },
            procedures = new[] { new { name = "Private Procedure", year = 2024 } },
            preferredHospital = new { name = "Private Hospital", phoneNumber = "+91 90000 20112" },
            sharing = new
            {
                shareName,
                shareApproximateAge = false,
                shareAllergies = false,
                shareConditions = false,
                shareMedications = false,
                shareEmergencyContact = false,
                reviewed = true
            }
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task SubmitAuthenticatedIncidentAsync(HttpClient client, Guid sessionId)
    {
        var response = await PostIdempotentJsonAsync(client, $"/api/v1/sessions/{sessionId}/incident", new
        {
            originalText = "Sudden chest pain and sweating were reported.", selectedLanguage = "en", fallbackCategory = "chest_pain"
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendAudioAsync(HttpClient client, Guid sessionId, byte[] bytes, string contentType, string filename)
    {
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(file, "audio", filename);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/voice") { Content = multipart };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(HttpClient client, string payload, string timestamp, string deliveryId, string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/mock-sms") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-GoldenHour-Timestamp", timestamp);
        request.Headers.Add("X-GoldenHour-Delivery-Id", deliveryId);
        request.Headers.Add("X-GoldenHour-Signature", $"sha256={signature}");
        return await client.SendAsync(request);
    }

    private static string SignWebhook(string timestamp, string payload) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiFactory.WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

    private static async Task<HttpResponseMessage> PostIdempotentJsonAsync(HttpClient client, string path, object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static string ExtractCookie(HttpResponseMessage response, string name)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith($"{name}=", StringComparison.Ordinal));
        return header[(name.Length + 1)..header.IndexOf(';')];
    }
}
