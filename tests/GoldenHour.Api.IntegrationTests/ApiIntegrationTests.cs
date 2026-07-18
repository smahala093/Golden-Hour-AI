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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        var configurationResponse = await client.GetAsync("/api/v1/configuration");
        configurationResponse.EnsureSuccessStatusCode();
        configurationResponse.Headers.CacheControl!.Public.Should().BeTrue();
        var configuration = await configurationResponse.Content.ReadFromJsonAsync<JsonElement>();
        configuration.GetProperty("emergencyNumber").GetString().Should().Be("112");
        var protocolResponse = await client.GetAsync("/api/v1/protocols?country=IN");
        protocolResponse.EnsureSuccessStatusCode();
        protocolResponse.Headers.CacheControl.Should().NotBeNull();
        protocolResponse.Headers.CacheControl!.Public.Should().BeTrue();
        protocolResponse.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(1));
        protocolResponse.Headers.CacheControl.Extensions.Should().Contain(extension =>
            extension.Name == "stale-while-revalidate" && extension.Value == "604800");
        protocolResponse.Headers.Pragma.Should().BeEmpty();
        var protocols = await protocolResponse.Content.ReadFromJsonAsync<JsonElement>();

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
        var registration = await RegisterAsync(client);
        var ownerId = (await registration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
        profile.GetProperty("contacts").EnumerateArray().Should().OnlyContain(contact =>
            !contact.GetProperty("isVerified").GetBoolean(),
            "contact verification is server-owned and cannot be asserted by a profile payload");
        var readiness = await client.GetFromJsonAsync<JsonElement>("/api/v1/readiness");
        readiness.GetProperty("score").GetInt32().Should().Be(80, "contact verification and an active share link remain incomplete");
        using var scope = factory.Services.CreateScope();
        var profileAudits = await scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>().AuditEvents
            .Where(audit => audit.ActorUserId == ownerId && audit.ResourceType == "EmergencyProfile")
            .OrderBy(audit => audit.CreatedAtUtc)
            .ToListAsync();
        profileAudits.Select(audit => audit.Action).Should().Equal("emergency-profile-created", "emergency-profile-updated");
        profileAudits.Should().OnlyContain(audit => audit.MetadataJson == null && audit.ResourceId == profile.GetProperty("id").GetGuid().ToString());
    }

    [Fact]
    public async Task ProfileTrustBoundary_RejectsOversizedInvalidAndNullNestedValues()
    {
        using var client = factory.CreateClient();
        await RegisterAsync(client);
        var invalid = new
        {
            fullName = new string('n', 161),
            dateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd"),
            bloodGroup = "X+",
            preferredLanguage = "invalid_language",
            responseMode = "diagnostic",
            insuranceDetails = new string('i', 1_001),
            doctorContact = new string('d', 301),
            allergyStatusCompleted = true,
            medicationStatusCompleted = true,
            locationPermissionReviewed = true,
            reviewed = true,
            contacts = new[] { new { name = new string('c', 161), relationship = "", phoneNumber = new string('1', 41), isVerified = true } },
            allergies = Enumerable.Range(0, 51).Select(index => new { name = $"Allergy {index}" }).ToArray(),
            conditions = new[] { new { name = new string('x', 161) } },
            medications = new[] { new { name = "" } },
            procedures = new[] { new { name = "Procedure", year = DateTime.UtcNow.Year + 1 } },
            preferredHospital = new { name = new string('h', 161), phoneNumber = new string('1', 41) },
            sharing = (object?)null
        };

        var response = await client.PutAsJsonAsync("/api/v1/profile", invalid);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = problem.GetProperty("errors");
        foreach (var key in new[] { "fullName", "dateOfBirth", "bloodGroup", "preferredLanguage", "responseMode", "insuranceDetails", "doctorContact", "contacts", "allergies", "conditions", "medications", "procedures", "preferredHospital", "sharing" })
            errors.TryGetProperty(key, out _).Should().BeTrue($"{key} is a bounded profile trust boundary");
    }

    [Fact]
    public async Task ProfileContacts_RequirePhoneShapedUniqueNormalizedNumbers()
    {
        using var client = factory.CreateClient();
        await RegisterAsync(client);

        object ProfileWithContacts(object[] contacts) => new
        {
            fullName = "Phone Boundary", dateOfBirth = "1980-06-20", bloodGroup = "O+", preferredLanguage = "en", responseMode = "text",
            insuranceDetails = (string?)null, doctorContact = (string?)null, allergyStatusCompleted = true, medicationStatusCompleted = true,
            locationPermissionReviewed = true, reviewed = true, contacts, allergies = Array.Empty<object>(), conditions = Array.Empty<object>(),
            medications = Array.Empty<object>(), procedures = Array.Empty<object>(), preferredHospital = (object?)null,
            sharing = new { shareName = false, shareApproximateAge = false, shareAllergies = false, shareConditions = false, shareMedications = false, shareEmergencyContact = false, reviewed = true }
        };

        var duplicate = await client.PutAsJsonAsync("/api/v1/profile", ProfileWithContacts([
            new { name = "One", relationship = "Family", phoneNumber = "+91 90000 10001", isVerified = false },
            new { name = "Two", relationship = "Friend", phoneNumber = "+91 (90000) 10001", isVerified = false }
        ]));
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("unique after normalization");

        var nonPhone = await client.PutAsJsonAsync("/api/v1/profile", ProfileWithContacts([
            new { name = "One", relationship = "Family", phoneNumber = "call-me-1234567", isVerified = false }
        ]));
        nonPhone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await nonPhone.Content.ReadAsStringAsync()).Should().Contain("ASCII digits");
    }

    [Fact]
    public async Task SkipAi_UsesOnlyDeterministicReviewedContent_AndNeverInvokesAiProvider()
    {
        var provider = new CountingAiProvider();
        await using var skipAiFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiProvider>();
            services.AddSingleton<IAiProvider>(provider);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        }));
        using var client = skipAiFactory.CreateClient();
        var create = await PostIdempotentJsonAsync(client, "/api/v1/sessions", new
        {
            patientRelationship = "self", selectedCategory = "heavy_bleeding", typedLocation = (string?)null, countryCode = "IN"
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = created.GetProperty("id").GetGuid();
        var token = created.GetProperty("anonymousAccessToken").GetString()!;

        using var incidentRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/incident")
        {
            Content = JsonContent.Create(new
            {
                originalText = "The user chose the reviewed severe bleeding category.",
                selectedLanguage = "en",
                fallbackCategory = "heavy_bleeding",
                skipAi = true
            })
        };
        incidentRequest.Headers.Add("X-Emergency-Access-Token", token);
        incidentRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await client.SendAsync(incidentRequest);

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        session.GetProperty("protocol").GetProperty("id").GetString().Should().Be("heavy-external-bleeding");
        session.GetProperty("incidentFacts").GetProperty("criticalMissingQuestions").EnumerateArray()
            .Select(question => question.GetProperty("id").GetString())
            .Should().BeEquivalentTo(["conscious", "breathing", "heavy-bleeding"]);
        provider.TotalCallCount.Should().Be(0, "Skip AI must bypass extraction, translation, and task suggestion calls");
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
        var shareKey = Guid.NewGuid().ToString("N");
        var shareResponse = await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/share-tokens", new { lifetimeMinutes = 30 }, shareKey);
        shareResponse.EnsureSuccessStatusCode();
        var share = await shareResponse.Content.ReadFromJsonAsync<JsonElement>();
        var retriedShare = await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/share-tokens", new { lifetimeMinutes = 30 }, shareKey);
        retriedShare.StatusCode.Should().Be(HttpStatusCode.Conflict, "one-time capability secrets are never retained for replay");
        var token = share.GetProperty("token").GetString()!;
        AssertCapabilityToken(token);
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
        (await SendWebhookAsync(client, payload, timestamp, Guid.NewGuid().ToString("N"), signature, "unknown-provider"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var unknownStatusPayload = JsonSerializer.Serialize(new { messageId = $"message-{Guid.NewGuid():N}", status = "provider-owned-arbitrary-state" });
        var unknownStatusSignature = SignWebhook(timestamp, unknownStatusPayload);
        (await SendWebhookAsync(client, unknownStatusPayload, timestamp, Guid.NewGuid().ToString("N"), unknownStatusSignature))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var first = await SendWebhookAsync(client, payload, timestamp, deliveryId, signature);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var replay = await SendWebhookAsync(client, payload, timestamp, deliveryId, signature);
        replay.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean().Should().BeTrue();

        var nonAsciiBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            messageId = $"message-{Guid.NewGuid():N}",
            status = "delivered",
            note = "स्थान"
        }));
        (await SendWebhookBytesAsync(client, nonAsciiBody, timestamp, Guid.NewGuid().ToString("N"), SignWebhook(timestamp, nonAsciiBody)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var invalidUtf8Body = new byte[] { (byte)'{', (byte)'"', (byte)'x', (byte)'"', (byte)':', (byte)'"', 0xFF, (byte)'"', (byte)'}' };
        (await SendWebhookBytesAsync(client, invalidUtf8Body, timestamp, Guid.NewGuid().ToString("N"), SignWebhook(timestamp, invalidUtf8Body)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the exact raw bytes must pass HMAC verification before invalid JSON is rejected");
    }

    [Fact]
    public async Task Webhook_ConcurrentIdenticalDeliveries_CreateOneReceiptAndOneOutboxEvent()
    {
        using var client = factory.CreateClient();
        var messageId = $"concurrent-message-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new { messageId, status = "delivered" });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var deliveryId = $"concurrent-delivery-{Guid.NewGuid():N}";
        var signature = SignWebhook(timestamp, payload);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => SendWebhookAsync(client, payload, timestamp, deliveryId, signature)));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Accepted);
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<JsonElement>()));
        bodies.Count(body => !body.GetProperty("duplicate").GetBoolean()).Should().Be(1);
        bodies.Count(body => body.GetProperty("duplicate").GetBoolean()).Should().Be(11);
        foreach (var response in responses) response.Dispose();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
        (await db.WebhookReceipts.CountAsync(receipt => receipt.Provider == "mock-sms" && receipt.DeliveryId == deliveryId)).Should().Be(1);
        (await db.NotificationDeliveries.CountAsync(delivery => delivery.Provider == "mock-sms" && delivery.ProviderMessageId == messageId)).Should().Be(1);
        (await db.OutboxMessages.CountAsync(message => message.EventType == "notification.delivery.updated" && message.PayloadJson.Contains(messageId))).Should().Be(1);
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
        using var auditScope = factory.Services.CreateScope();
        var audit = await auditScope.ServiceProvider.GetRequiredService<GoldenHourDbContext>().AuditEvents
            .SingleAsync(entry => entry.Action == "refresh-token-reuse-detected");
        audit.ActorUserId.Should().BeNull();
        audit.MetadataJson.Should().BeNull();
        audit.ResourceId.Should().NotContain(oldRefresh);
    }

    [Fact]
    public async Task LockedAccountLogin_IsExternallyIndistinguishableFromInvalidCredentials()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        var registeredUser = await registration.Content.ReadFromJsonAsync<JsonElement>();
        var userId = registeredUser.GetProperty("id").GetGuid();
        var email = registeredUser.GetProperty("email").GetString();
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByIdAsync(userId.ToString());
            (await manager.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(10))).Succeeded.Should().BeTrue();
        }

        var locked = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "Integration-Only-Password-2026!"
        });
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = $"unknown-{Guid.NewGuid():N}@example.test",
            password = "Integration-Only-Password-2026!"
        });
        var lockedProblem = await locked.Content.ReadFromJsonAsync<JsonElement>();
        var unknownProblem = await unknown.Content.ReadFromJsonAsync<JsonElement>();

        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized).And.Be(unknown.StatusCode);
        lockedProblem.GetProperty("title").GetString().Should().Be("Invalid credentials")
            .And.Be(unknownProblem.GetProperty("title").GetString());
        lockedProblem.TryGetProperty("detail", out _).Should().Be(unknownProblem.TryGetProperty("detail", out _));
        using var auditScope = factory.Services.CreateScope();
        var audit = await auditScope.ServiceProvider.GetRequiredService<GoldenHourDbContext>().AuditEvents
            .SingleAsync(entry => entry.Action == "authentication-rejected-account-locked" && entry.ResourceId == userId.ToString());
        audit.MetadataJson.Should().BeNull();
        audit.ActorUserId.Should().BeNull();
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
    public async Task SignalRGroup_AllowsMemberBroadcasts_AndRejectsNonMemberJoin()
    {
        using var owner = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var ownerRegistration = await RegisterAsync(owner);
        var ownerAccessCookie = ExtractCookie(ownerRegistration, "gh_access");
        owner.DefaultRequestHeaders.Add("Cookie", $"gh_access={ownerAccessCookie}");
        var sessionId = await CreateAuthenticatedSessionAsync(owner);
        await SubmitAuthenticatedIncidentAsync(owner, sessionId);

        using var stranger = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var strangerRegistration = await RegisterAsync(stranger);
        var strangerAccessCookie = ExtractCookie(strangerRegistration, "gh_access");

        await using var ownerConnection = CreateHubConnection(factory, ownerAccessCookie);
        await using var strangerConnection = CreateHubConnection(factory, strangerAccessCookie);
        var ownerEvent = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var strangerEvent = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ownerSubscription = ownerConnection.On<JsonElement>("TimelineAdded", payload => ownerEvent.TrySetResult(payload));
        using var strangerSubscription = strangerConnection.On<JsonElement>("TimelineAdded", payload => strangerEvent.TrySetResult(payload));

        await ownerConnection.StartAsync();
        await strangerConnection.StartAsync();
        await ownerConnection.InvokeAsync("JoinSession", sessionId);
        Func<Task> deniedJoin = () => strangerConnection.InvokeAsync("JoinSession", sessionId);
        await deniedJoin.Should().ThrowAsync<HubException>().WithMessage("*access denied*");

        var update = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/timeline", new
        {
            type = "observation",
            message = "Authorized realtime integration test update.",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        update.EnsureSuccessStatusCode();

        var delivered = await ownerEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        delivered.GetProperty("type").GetString().Should().Be("observation");
        await Task.Delay(250);
        strangerEvent.Task.IsCompleted.Should().BeFalse("a rejected connection must not receive session-group events");
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
    public async Task VoiceUpload_UnauthorizedSessionNeverCallsSpeechProvider()
    {
        var speech = new CountingSpeechProvider();
        await using var voiceFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISpeechToTextProvider>();
            services.AddSingleton<ISpeechToTextProvider>(speech);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        }));
        using var owner = voiceFactory.CreateClient();
        await RegisterAsync(owner);
        var sessionId = await CreateAuthenticatedSessionAsync(owner);
        using var stranger = voiceFactory.CreateClient();
        await RegisterAsync(stranger);
        var webm = new byte[32];
        new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.CopyTo(webm, 0);

        var response = await SendAudioAsync(stranger, sessionId, webm, "audio/webm", "unauthorized.webm");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        speech.CallCount.Should().Be(0, "session authorization must occur before any billable transcription call");
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
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle()
            .Which.Should().Contain("media-src 'self' blob:");
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
    public async Task ProductionMutations_RequireAndAcceptOnlySameOriginBrowserSource()
    {
        using var production = new ProductionApiFactory();
        using var client = production.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://goldenhour.test"),
            AllowAutoRedirect = false
        });
        var registration = new
        {
            email = $"production-{Guid.NewGuid():N}@example.test",
            password = "Integration-Only-Password-2026!",
            preferredLanguage = "en"
        };

        var missingSource = await client.PostAsJsonAsync("/api/v1/auth/register", registration);
        missingSource.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var crossSiteRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(registration)
        };
        crossSiteRequest.Headers.Add("Origin", "https://attacker.example");
        (await client.SendAsync(crossSiteRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var sameOriginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(registration)
        };
        sameOriginRequest.Headers.Add("Origin", "https://goldenhour.test");
        (await client.SendAsync(sameOriginRequest)).StatusCode.Should().Be(HttpStatusCode.Created);
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

        AssertCapabilityToken(first.GetProperty("anonymousAccessToken").GetString()!);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, "the anonymous capability secret is returned only once");
        var independentResponse = await PostIdempotentJsonAsync(client, "/api/v1/sessions", body);
        var independent = await independentResponse.Content.ReadFromJsonAsync<JsonElement>();
        AssertCapabilityToken(independent.GetProperty("anonymousAccessToken").GetString()!);
        independent.GetProperty("anonymousAccessToken").GetString().Should().NotBe(first.GetProperty("anonymousAccessToken").GetString());
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
            var incidentResponse = await client.SendAsync(incident);
            incidentResponse.EnsureSuccessStatusCode();
            (await incidentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("interpretationUncertain").GetBoolean().Should().BeTrue(
                "AI confidence never confirms extracted facts");
        }

        var answersKey = Guid.NewGuid().ToString("N");
        await SendAnswersAsync(client, sessionId, token, answersKey, new Dictionary<string, string> { ["conscious"] = "no" });
        var finalAnswer = await SendAnswersAsync(client, sessionId, token, answersKey, new Dictionary<string, string> { ["conscious"] = "no", ["breathing"] = "yes" });
        var body = await finalAnswer.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("protocol").GetProperty("id").GetString().Should().Be("unconscious-breathing");
        body.GetProperty("interpretationUncertain").GetBoolean().Should().BeTrue();
        var explicitlyConfirmed = await SendAnswersAsync(client, sessionId, token, Guid.NewGuid().ToString("N"),
            new Dictionary<string, string> { ["confirm-facts"] = "yes" });
        (await explicitlyConfirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("interpretationUncertain").GetBoolean().Should().BeFalse();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
        var aiOperations = await db.AiOperations.Where(x => x.EmergencySessionId == sessionId).ToListAsync();
        aiOperations.Should().HaveCount(2);
        var aiOperation = aiOperations.Single(x => x.OperationType == "incident_extraction");
        aiOperation.OperationType.Should().Be("incident_extraction");
        aiOperation.Provider.Should().Be("deterministic-mock");
        aiOperation.Model.Should().Be("deterministic-mock-v1");
        aiOperation.LatencyMilliseconds.Should().Be(0);
        aiOperation.InputTokens.Should().BeNull();
        aiOperation.OutputTokens.Should().BeNull();
        var suggestionOperation = aiOperations.Single(x => x.OperationType == "coordination_task_suggestion");
        suggestionOperation.Provider.Should().Be("deterministic-mock");
        suggestionOperation.Model.Should().Be("deterministic-mock-v1");
        suggestionOperation.Succeeded.Should().BeTrue();
        suggestionOperation.InputTokens.Should().BeNull();
        suggestionOperation.OutputTokens.Should().BeNull();
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
        var inviteResponse = await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/participants/invitations", new { displayName = "Family member", role = "family", lifetimeMinutes = 30 });
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

        await PutReadyProfileAsync(owner, "Procedure Sharing Allowed", shareName: true, shareConditions: true);
        var procedureSessionId = await CreateAuthenticatedSessionAsync(owner);
        await SubmitAuthenticatedIncidentAsync(owner, procedureSessionId);
        var observation = await owner.PostAsJsonAsync($"/api/v1/sessions/{procedureSessionId}/timeline", new
        {
            type = "observation", message = "Owner-reported handover detail.", idempotencyKey = Guid.NewGuid().ToString("N")
        });
        observation.EnsureSuccessStatusCode();
        var projected = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/sessions/{procedureSessionId}");
        projected.GetProperty("patientSnapshot").GetProperty("procedures").EnumerateArray()
            .Should().ContainSingle(procedure => procedure.GetProperty("name").GetString() == "Private Procedure");

        var procedureResponder = await owner.PostAsync($"/api/v1/sessions/{procedureSessionId}/summaries/responder", null);
        var procedureResponderBody = await procedureResponder.Content.ReadFromJsonAsync<JsonElement>();
        procedureResponderBody.GetProperty("language").GetString().Should().Be("en");
        procedureResponderBody.GetProperty("content").GetString().Should().Contain("Private Procedure (2024)");
        var handover = await owner.PostAsync($"/api/v1/sessions/{procedureSessionId}/summaries/hospital-handover", null);
        var handoverBody = await handover.Content.ReadFromJsonAsync<JsonElement>();
        var handoverContent = handoverBody.GetProperty("content").GetString()!;
        handoverBody.GetProperty("language").GetString().Should().Be("en");
        handoverContent.Should().Contain("[system: session-created]")
            .And.Contain("[ai-extracted: incident-understood]")
            .And.Contain("[user-reported: observation]")
            .And.Contain("Languages used: original=en; summary=en")
            .And.Contain("Private Procedure (2024)");
    }

    [Fact]
    public async Task FamilyProfileSnapshot_RequiresAuthenticatedExplicitPatientSelection()
    {
        using var owner = factory.CreateClient();
        await RegisterAsync(owner);
        await PutReadyProfileAsync(owner, "Explicit Family Patient", shareName: true);

        var withoutConsent = await PostIdempotentJsonAsync(owner, "/api/v1/sessions", new
        {
            patientRelationship = "family", selectedCategory = "chest_pain", countryCode = "IN", useOwnerProfileForPatient = false
        });
        var withoutConsentBody = await withoutConsent.Content.ReadFromJsonAsync<JsonElement>();
        withoutConsentBody.TryGetProperty("patientSnapshot", out _).Should().BeFalse();

        var withConsent = await PostIdempotentJsonAsync(owner, "/api/v1/sessions", new
        {
            patientRelationship = "family", selectedCategory = "chest_pain", countryCode = "IN", useOwnerProfileForPatient = true
        });
        withConsent.EnsureSuccessStatusCode();
        var withConsentBody = await withConsent.Content.ReadFromJsonAsync<JsonElement>();
        withConsentBody.GetProperty("patientSnapshot").GetProperty("fullName").GetString().Should().Be("Explicit Family Patient");

        using var anonymous = factory.CreateClient();
        (await PostIdempotentJsonAsync(anonymous, "/api/v1/sessions", new
        {
            patientRelationship = "family", selectedCategory = "chest_pain", countryCode = "IN", useOwnerProfileForPatient = true
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostIdempotentJsonAsync(owner, "/api/v1/sessions", new
        {
            patientRelationship = "bystander", selectedCategory = "chest_pain", countryCode = "IN", useOwnerProfileForPatient = true
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
        var inviteKey = Guid.NewGuid().ToString("N");
        var inviteResponse = await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/participants/invitations", new { displayName = "Assigned family", role = "family", lifetimeMinutes = 30 }, inviteKey);
        var invite = await inviteResponse.Content.ReadFromJsonAsync<JsonElement>();
        AssertCapabilityToken(invite.GetProperty("token").GetString()!);
        var retriedInvite = await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/participants/invitations", new { displayName = "Assigned family", role = "family", lifetimeMinutes = 30 }, inviteKey);
        retriedInvite.StatusCode.Should().Be(HttpStatusCode.Conflict, "one-time invitation secrets are never retained for replay");
        var participantId = invite.GetProperty("participantId").GetGuid();
        var join = await family.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/participants/join", new { token = invite.GetProperty("token").GetString() });
        join.EnsureSuccessStatusCode();
        (await family.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/participants/join", new { token = invite.GetProperty("token").GetString() })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var acknowledged = await family.PostAsync($"/api/v1/sessions/{sessionId}/participants/{participantId}/acknowledge", null);
        acknowledged.EnsureSuccessStatusCode();
        (await acknowledged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("acknowledgedAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));

        (await family.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks", new { taskCode = "unlock-entry", assignedParticipantId = participantId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/tasks", new { taskCode = "unlock-entry", assignedParticipantId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var taskCommandKey = Guid.NewGuid().ToString("N");
        var assignedResponse = await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/tasks", new { taskCode = "stay-with-patient", assignedParticipantId = participantId }, taskCommandKey);
        assignedResponse.EnsureSuccessStatusCode();
        (await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/tasks", new { taskCode = "stay-with-patient", assignedParticipantId = participantId }, taskCommandKey)).EnsureSuccessStatusCode();
        var assignedSession = await assignedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var task = assignedSession.GetProperty("tasks").EnumerateArray().Single(x => x.GetProperty("code").GetString() == "stay-with-patient");
        var accepted = await family.PatchAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks/{task.GetProperty("id").GetGuid()}", new
        {
            status = "accepted", assignedParticipantId = (Guid?)null, concurrencyToken = task.GetProperty("concurrencyToken").GetGuid()
        });
        accepted.EnsureSuccessStatusCode();
        using var taskScope = factory.Services.CreateScope();
        (await taskScope.ServiceProvider.GetRequiredService<GoldenHourDbContext>().EmergencyTimelineEvents.CountAsync(
            entry => entry.EmergencySessionId == sessionId && entry.IdempotencyKey == $"task-command:{taskCommandKey}"))
            .Should().Be(1);
    }

    [Fact]
    public async Task AudioAndWebhookTrustBoundaries_RejectSpoofingOversizeStaleAndMalformedPayloads()
    {
        using var client = factory.CreateClient();
        await RegisterAsync(client);
        var sessionId = await CreateAuthenticatedSessionAsync(client);
        (await SendAudioAsync(client, sessionId, [1, 2, 3, 4], "audio/webm", "recording.webm")).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await SendAudioAsync(client, sessionId, [0x1A, 0x45, 0xDF, 0xA3], "audio/webm", "truncated.webm")).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var paddedWebm = new byte[32];
        new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.CopyTo(paddedWebm, 0);
        (await SendAudioAsync(client, sessionId, paddedWebm, "audio/webm", "recording.webm", new string('a', 36))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var valid = await SendAudioAsync(client, sessionId, paddedWebm, "audio/webm", "..\\ignored-name.webm");
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
    public async Task VoiceWorkflow_RejectsProviderReportedAudioLongerThanThirtySeconds()
    {
        await using var durationFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISpeechToTextProvider>();
            services.AddSingleton<ISpeechToTextProvider, OversizedDurationSpeechProvider>();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        }));
        using var client = durationFactory.CreateClient();
        await RegisterAsync(client);
        var sessionId = await CreateAuthenticatedSessionAsync(client);
        var webm = new byte[32];
        new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.CopyTo(webm, 0);

        var response = await SendAudioAsync(client, sessionId, webm, "audio/webm", "bounded.webm");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no longer than 30 seconds");
        var session = await client.GetFromJsonAsync<JsonElement>($"/api/v1/sessions/{sessionId}");
        session.TryGetProperty("originalInput", out _).Should().BeFalse("over-duration audio must not enter incident processing");
    }

    [Fact]
    public async Task ContactVerification_IsOwnerBoundExpiringOneTimeAndServerOwned()
    {
        var clock = new MutableClock(DateTime.UtcNow);
        await using var verificationFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        }));
        using var owner = verificationFactory.CreateClient();
        var ownerRegistration = await RegisterAsync(owner);
        var ownerId = (await ownerRegistration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await PutReadyProfileAsync(owner, "Verification Owner", shareName: false);
        var profile = await owner.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        var contacts = profile.GetProperty("contacts").EnumerateArray().ToArray();
        var firstContactId = contacts[0].GetProperty("id").GetGuid();
        var secondContactId = contacts[1].GetProperty("id").GetGuid();

        var challengeResponse = await owner.PostAsync($"/api/v1/profile/contacts/{firstContactId}/verification", null);
        challengeResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<JsonElement>();
        challenge.GetProperty("status").GetString().Should().Contain("no SMS delivery occurred");
        var token = challenge.GetProperty("challenge").GetString()!;
        var code = challenge.GetProperty("developmentCode").GetString()!;
        var wrongCode = code == "000000" ? "000001" : "000000";
        (await owner.PostAsJsonAsync($"/api/v1/profile/contacts/{firstContactId}/verification/confirm", new { challenge = token, code = wrongCode }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var otherUser = verificationFactory.CreateClient();
        await RegisterAsync(otherUser);
        (await otherUser.PostAsJsonAsync($"/api/v1/profile/contacts/{firstContactId}/verification/confirm", new { challenge = token, code }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await owner.PostAsJsonAsync($"/api/v1/profile/contacts/{firstContactId}/verification/confirm", new { challenge = token, code }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.PostAsJsonAsync($"/api/v1/profile/contacts/{firstContactId}/verification/confirm", new { challenge = token, code }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "repeating the same consumed critical command is idempotent");
        var verifiedProfile = await owner.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        verifiedProfile.GetProperty("contacts").EnumerateArray()
            .Single(contact => contact.GetProperty("id").GetGuid() == firstContactId)
            .GetProperty("isVerified").GetBoolean().Should().BeTrue();

        var expiringResponse = await owner.PostAsync($"/api/v1/profile/contacts/{secondContactId}/verification", null);
        var expiring = await expiringResponse.Content.ReadFromJsonAsync<JsonElement>();
        clock.Advance(TimeSpan.FromMinutes(11));
        (await owner.PostAsJsonAsync($"/api/v1/profile/contacts/{secondContactId}/verification/confirm", new
        {
            challenge = expiring.GetProperty("challenge").GetString(), code = expiring.GetProperty("developmentCode").GetString()
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = verificationFactory.Services.CreateScope();
        var audits = await scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>().AuditEvents
            .Where(entry => entry.ActorUserId == ownerId && (entry.Action == "contact-verified" || entry.Action == "contact-verification-consumed"))
            .ToListAsync();
        audits.Select(entry => entry.Action).Should().BeEquivalentTo(["contact-verified", "contact-verification-consumed"]);
        audits.Should().OnlyContain(entry => entry.MetadataJson == null);
    }

    [Fact]
    public async Task ProductionContactVerification_RejectsMockSmsProviderWithoutExposingCode()
    {
        await using var production = new ProductionApiFactory();
        using var client = production.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://goldenhour.test"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        var email = $"verification-{Guid.NewGuid():N}@example.test";
        using var register = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new { email, password = "Integration-Only-Password-2026!", preferredLanguage = "en" })
        };
        register.Headers.Add("Origin", "https://goldenhour.test");
        (await client.SendAsync(register)).StatusCode.Should().Be(HttpStatusCode.Created);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/profile/contacts/{Guid.NewGuid()}/verification");
        request.Headers.Add("Origin", "https://goldenhour.test");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Configure a production SMS provider").And.NotContain("developmentCode");
    }

    [Fact]
    public async Task MockProtocolReadAloud_ReturnsExplicitUnavailableProblemWithoutClaimingAudio()
    {
        using var client = factory.CreateClient();
        var (sessionId, token) = await CreateAnonymousIncidentAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/protocol-audio");
        request.Headers.Add("X-Emergency-Access-Token", token);

        var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Content.Headers.ContentType.MediaType.Should().NotStartWith("audio/");
        problem.GetProperty("title").GetString().Should().Be("Read-aloud audio is unavailable in deterministic mock mode");
        problem.GetProperty("detail").GetString().Should().Contain("reviewed protocol text");
    }

    [Fact]
    public async Task SessionLifecycle_LocationTimelineHistorySummaryAndCloseRemainAuthoritativeAndIdempotent()
    {
        using var owner = factory.CreateClient();
        await RegisterAsync(owner);
        var sessionId = await CreateAuthenticatedSessionAsync(owner);
        await SubmitAuthenticatedIncidentAsync(owner, sessionId);

        var noConsent = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/locations", new
        {
            latitude = 26.9124m, longitude = 75.7873m, description = (string?)null, consentProvided = false, idempotencyKey = Guid.NewGuid().ToString("N")
        });
        noConsent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var incompleteCoordinates = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/locations", new
        {
            latitude = 26.9124m, longitude = (decimal?)null, description = (string?)null, consentProvided = true, idempotencyKey = Guid.NewGuid().ToString("N")
        });
        incompleteCoordinates.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var locationKey = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var location = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/locations", new
            {
                latitude = 26.9124m, longitude = 75.7873m, description = (string?)null, consentProvided = true, idempotencyKey = locationKey
            });
            location.EnsureSuccessStatusCode();
        }

        var observationKey = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var observation = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/timeline", new
            {
                type = "observation", message = "Family reports the patient remains conscious.", idempotencyKey = observationKey
            });
            observation.EnsureSuccessStatusCode();
        }
        var unsupportedConfirmation = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/timeline", new
        {
            type = "call-connected", message = "connected", idempotencyKey = Guid.NewGuid().ToString("N"), userConfirmed = false
        });
        unsupportedConfirmation.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var confirmedCall = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/timeline", new
        {
            type = "call-connected", message = "untrusted client claim", idempotencyKey = Guid.NewGuid().ToString("N"), userConfirmed = true
        });
        confirmedCall.EnsureSuccessStatusCode();

        var summary = await owner.PostAsync($"/api/v1/sessions/{sessionId}/summaries/hospital-handover", null);
        summary.EnsureSuccessStatusCode();
        var summaryText = (await summary.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content").GetString()!;
        summaryText.Should().Contain("Chronological timeline").And.Contain("Confirmed observations").And.Contain("Unconfirmed observations")
            .And.Contain("Protocol version").And.Contain("AI confidence (not a diagnosis)");
        var history = await owner.GetFromJsonAsync<JsonElement>("/api/v1/sessions?limit=10");
        history.EnumerateArray().Should().Contain(x => x.GetProperty("id").GetGuid() == sessionId);

        var shareResponse = await PostIdempotentJsonAsync(owner, $"/api/v1/sessions/{sessionId}/share-tokens", new { lifetimeMinutes = 30 });
        var share = await shareResponse.Content.ReadFromJsonAsync<JsonElement>();
        var shareToken = share.GetProperty("token").GetString()!;
        (await owner.PostAsJsonAsync("/api/v1/bystander/location", new { latitude = 26.9m, longitude = 75.8m, consentConfirmed = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var bystanderLocationKey = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var bystanderLocation = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bystander/location")
            {
                Content = JsonContent.Create(new { latitude = 26.9m, longitude = 75.8m, consentConfirmed = true })
            };
            bystanderLocation.Headers.Add("X-Emergency-Share-Token", shareToken);
            bystanderLocation.Headers.Add("Idempotency-Key", bystanderLocationKey);
            (await owner.SendAsync(bystanderLocation)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var departed = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/timeline", new
        {
            type = "patient-departed", message = "untrusted", idempotencyKey = Guid.NewGuid().ToString("N")
        });
        departed.EnsureSuccessStatusCode();
        var arrived = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/timeline", new
        {
            type = "patient-arrived", message = "untrusted", idempotencyKey = Guid.NewGuid().ToString("N")
        });
        arrived.EnsureSuccessStatusCode();
        var arrivedSession = await arrived.Content.ReadFromJsonAsync<JsonElement>();
        arrivedSession.GetProperty("status").GetString().Should().Be("arrived_at_hospital");
        var closed = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/close", new { concurrencyToken = arrivedSession.GetProperty("concurrencyToken").GetGuid() });
        closed.EnsureSuccessStatusCode();
        (await closed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("closed");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
        (await db.EmergencyLocations.CountAsync(x => x.EmergencySessionId == sessionId)).Should().Be(2);
        (await db.EmergencyTimelineEvents.CountAsync(x => x.EmergencySessionId == sessionId && x.IdempotencyKey == observationKey)).Should().Be(1);
        (await db.EmergencyTimelineEvents.CountAsync(x => x.EmergencySessionId == sessionId && x.IdempotencyKey == bystanderLocationKey)).Should().Be(1);
    }

    [Fact]
    public async Task TerminalTaskAndSessionCommandReplays_AreIdempotentWithStaleConcurrencyTokens()
    {
        using var owner = factory.CreateClient();
        await RegisterAsync(owner);
        var sessionId = await CreateAuthenticatedSessionAsync(owner);
        await SubmitAuthenticatedIncidentAsync(owner, sessionId);
        var initial = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/sessions/{sessionId}");
        var task = initial.GetProperty("tasks").EnumerateArray().First(entry => entry.GetProperty("code").GetString() == "stay-with-patient");
        var taskId = task.GetProperty("id").GetGuid();
        var accepted = await owner.PatchAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks/{taskId}", new
        {
            status = "accepted", assignedParticipantId = (Guid?)null, concurrencyToken = task.GetProperty("concurrencyToken").GetGuid()
        });
        accepted.EnsureSuccessStatusCode();
        var acceptedBody = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        var acceptedTask = acceptedBody.GetProperty("tasks").EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == taskId);
        var completeCommand = new
        {
            status = "completed", assignedParticipantId = (Guid?)null, concurrencyToken = acceptedTask.GetProperty("concurrencyToken").GetGuid()
        };
        (await owner.PatchAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks/{taskId}", completeCommand)).EnsureSuccessStatusCode();
        (await owner.PatchAsJsonAsync($"/api/v1/sessions/{sessionId}/tasks/{taskId}", completeCommand)).EnsureSuccessStatusCode();

        var beforeClose = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/sessions/{sessionId}");
        var closeCommand = new { concurrencyToken = beforeClose.GetProperty("concurrencyToken").GetGuid() };
        (await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/close", closeCommand)).EnsureSuccessStatusCode();
        var replayedClose = await owner.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/close", closeCommand);
        replayedClose.EnsureSuccessStatusCode();
        (await replayedClose.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("closed");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
        (await db.EmergencyTimelineEvents.CountAsync(entry => entry.EmergencySessionId == sessionId && entry.Type == "task-completed")).Should().Be(1);
        (await db.EmergencyTimelineEvents.CountAsync(entry => entry.EmergencySessionId == sessionId && entry.Type == "session-closed")).Should().Be(1);
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

    private static async Task PutReadyProfileAsync(HttpClient client, string fullName, bool shareName, bool shareConditions = false)
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
                shareConditions,
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

    private static async Task<HttpResponseMessage> SendAudioAsync(HttpClient client, Guid sessionId, byte[] bytes, string contentType, string filename, string? languageHint = null)
    {
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(file, "audio", filename);
        if (languageHint is not null) multipart.Add(new StringContent(languageHint), "languageHint");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sessions/{sessionId}/voice") { Content = multipart };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static HubConnection CreateHubConnection(ApiFactory apiFactory, string accessCookie) =>
        new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/emergency", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.Headers.Add("Cookie", $"gh_access={accessCookie}");
                options.HttpMessageHandlerFactory = _ => apiFactory.Server.CreateHandler();
            })
            .Build();

    private static async Task<HttpResponseMessage> SendWebhookAsync(HttpClient client, string payload, string timestamp, string deliveryId, string signature, string provider = "mock-sms")
        => await SendWebhookBytesAsync(client, Encoding.UTF8.GetBytes(payload), timestamp, deliveryId, signature, provider);

    private static async Task<HttpResponseMessage> SendWebhookBytesAsync(HttpClient client, byte[] payload, string timestamp, string deliveryId, string signature, string provider = "mock-sms")
    {
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/{provider}") { Content = content };
        request.Headers.Add("X-GoldenHour-Timestamp", timestamp);
        request.Headers.Add("X-GoldenHour-Delivery-Id", deliveryId);
        request.Headers.Add("X-GoldenHour-Signature", $"sha256={signature}");
        return await client.SendAsync(request);
    }

    private static string SignWebhook(string timestamp, string payload) =>
        SignWebhook(timestamp, Encoding.UTF8.GetBytes(payload));

    private static string SignWebhook(string timestamp, byte[] payload)
    {
        var timestampBytes = Encoding.UTF8.GetBytes(timestamp);
        var signed = new byte[timestampBytes.Length + 1 + payload.Length];
        timestampBytes.CopyTo(signed, 0);
        signed[timestampBytes.Length] = (byte)'.';
        payload.CopyTo(signed, timestampBytes.Length + 1);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiFactory.WebhookSecret), signed)).ToLowerInvariant();
    }

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

    private static void AssertCapabilityToken(string token)
    {
        token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        var padded = token.Replace('-', '+').Replace('_', '/') + "=";
        Convert.FromBase64String(padded).Should().HaveCount(32);
    }

    private sealed class CountingAiProvider : IAiProvider
    {
        private int totalCallCount;

        public int TotalCallCount => Volatile.Read(ref totalCallCount);

        public Task<AiProviderResult<IncidentExtraction>> ExtractIncidentAsync(string originalText, string? selectedLanguage, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref totalCallCount);
            throw new InvalidOperationException("AI extraction must not be called when Skip AI is selected.");
        }

        public Task<string> TranslateApprovedTextAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref totalCallCount);
            throw new InvalidOperationException("AI translation must not be called when Skip AI is selected.");
        }

        public Task<AiProviderResult<IReadOnlyList<string>>> SuggestCoordinationTaskCodesAsync(IncidentExtraction incident, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref totalCallCount);
            throw new InvalidOperationException("AI task suggestions must not be called when Skip AI is selected.");
        }
    }

    private sealed class OversizedDurationSpeechProvider : ISpeechToTextProvider
    {
        public Task<SpeechTranscription> TranscribeAsync(Stream audio, string contentType, string? languageHint, CancellationToken cancellationToken) =>
            Task.FromResult(new SpeechTranscription("Reported emergency.", "en", 0.9m, 30.01m));
    }

    private sealed class CountingSpeechProvider : ISpeechToTextProvider
    {
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);

        public Task<SpeechTranscription> TranscribeAsync(Stream audio, string contentType, string? languageHint, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new SpeechTranscription("Should not be called.", "en", 1m, 1m));
        }
    }

    private sealed class MutableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; private set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }
}
