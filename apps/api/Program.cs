using System.Security.Claims;
using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using GoldenHour.Api.Api;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using GoldenHour.Api.Infrastructure;
using GoldenHour.Api.Realtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddValidatorsFromAssemblyContaining<IncidentExtractionValidator>();
var configuredUseMocks = builder.Configuration.GetValue("Providers:UseMocks", true);
builder.Services.AddOptions<EmergencyOptions>().Bind(builder.Configuration.GetSection("Emergency"))
    .Validate(x => !string.IsNullOrWhiteSpace(x.DefaultNumber) && x.AiConfidenceThreshold is >= 0 and <= 1, "Emergency configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<AuthenticationOptions>().Bind(builder.Configuration.GetSection("Authentication"))
    .Validate(x => !string.IsNullOrWhiteSpace(x.Jwt.Issuer) && !string.IsNullOrWhiteSpace(x.Jwt.Audience)
        && Encoding.UTF8.GetByteCount(x.Jwt.SigningKey) >= 32 && x.AccessTokenMinutes is >= 1 and <= 60 && x.RefreshTokenDays is >= 1 and <= 30,
        "Authentication configuration must include issuer, audience, and a signing key of at least 32 UTF-8 bytes.")
    .ValidateOnStart();
builder.Services.AddOptions<OpenAiOptions>().Bind(builder.Configuration.GetSection("OpenAI"))
    .Validate(x => Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(x.Model), "OpenAI base URL and model are required.")
    .Validate(x => configuredUseMocks || !string.IsNullOrWhiteSpace(x.ApiKey), "OpenAI API key is required when provider mocks are disabled.")
    .ValidateOnStart();
builder.Services.AddOptions<WebhookOptions>().Bind(builder.Configuration.GetSection("Webhook"))
    .Validate(x => !x.Enabled || (!string.IsNullOrWhiteSpace(x.SigningSecret) && x.SigningSecret.Length >= 16), "An enabled webhook requires a signing secret of at least 16 characters.")
    .ValidateOnStart();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("Security:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address)) options.KnownProxies.Add(address);
    }
});
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

var useInMemory = builder.Configuration.GetValue<bool>("Database:UseInMemory");
builder.Services.AddDbContext<GoldenHourDbContext>(options =>
{
    if (useInMemory)
    {
        options.UseInMemoryDatabase($"golden-hour-{builder.Environment.EnvironmentName}");
    }
    else
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("GoldenHour"), npgsql =>
            npgsql.MigrationsAssembly(typeof(GoldenHourDbContext).Assembly.FullName));
    }
});

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<GoldenHourDbContext>()
    .AddDefaultTokenProviders();

var authentication = builder.Configuration.GetSection("Authentication").Get<AuthenticationOptions>() ?? new AuthenticationOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authentication.Jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = authentication.Jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authentication.Jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AuthCookies.Access, out var token)) context.Token = token;
                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 401,
                    Title = "Authentication required",
                    Type = "https://httpstatuses.io/401"
                });
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 403,
                    Title = "Access denied",
                    Type = "https://httpstatuses.io/403"
                });
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("administrator", policy => policy.RequireRole("Administrator"));
    options.AddPolicy("caregiver", policy => policy.RequireRole("Caregiver", "Administrator"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("bystander", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("anonymous-start", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 180, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Golden Hour AI API",
        Version = "v1",
        Description = "Emergency coordination prototype. Not a diagnostic or ambulance service."
    });
    options.AddSecurityDefinition("cookieAuth", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = AuthCookies.Access });
});
builder.Services.AddSignalR().AddJsonProtocol(options =>
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));
builder.Services.AddCors(options => options.AddPolicy("development-web", policy => policy
    .WithOrigins("http://localhost:5173", "https://localhost:5173")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddSingleton(serviceProvider =>
{
    var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
    return ProtocolCatalogue.Load(Path.Combine(environment.ContentRootPath, "Protocols"));
});
builder.Services.AddSingleton<PromptTemplateStore>();
builder.Services.AddSingleton<IncidentDataMinimizer>();
builder.Services.AddScoped<IncidentUnderstandingService>();
builder.Services.AddScoped<ProfileCoordinator>();
builder.Services.AddScoped<SessionCoordinator>();
builder.Services.AddScoped<ShareTokenCoordinator>();
builder.Services.AddScoped<ParticipantCoordinator>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<WebhookCoordinator>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton<IEventBus, NullEventBus>();
builder.Services.AddSingleton<IFileStorage, UnavailableFileStorage>();
builder.Services.AddSingleton<IMapProvider, MockMapProvider>();
builder.Services.AddSingleton<ITextToSpeechProvider, MockTextToSpeechProvider>();
builder.Services.AddSingleton<MockNotificationProvider>();
builder.Services.AddSingleton<ISmsProvider>(provider => provider.GetRequiredService<MockNotificationProvider>());
builder.Services.AddSingleton<IEmailProvider>(provider => provider.GetRequiredService<MockNotificationProvider>());
builder.Services.AddSingleton<INotificationProvider>(provider => provider.GetRequiredService<MockNotificationProvider>());

if (configuredUseMocks)
{
    builder.Services.AddSingleton<IAiProvider, MockAiProvider>();
    builder.Services.AddSingleton<ISpeechToTextProvider, MockSpeechToTextProvider>();
}
else
{
    builder.Services.AddHttpClient<OpenAiResponsesProvider>((serviceProvider, client) => ConfigureOpenAiClient(serviceProvider, client));
    builder.Services.AddHttpClient<OpenAiSpeechToTextProvider>((serviceProvider, client) => ConfigureOpenAiClient(serviceProvider, client));
    builder.Services.AddScoped<IAiProvider>(provider => provider.GetRequiredService<OpenAiResponsesProvider>());
    builder.Services.AddScoped<ISpeechToTextProvider>(provider => provider.GetRequiredService<OpenAiSpeechToTextProvider>());
}
builder.Services.AddHostedService<OutboxDispatcher>();

var app = builder.Build();
if (app.Configuration.GetValue<bool>("Security:UseForwardedHeaders")) app.UseForwardedHeaders();
if (app.Environment.IsProduction())
{
    app.UseHsts();
    if (app.Configuration.GetValue("Security:RequireHttps", true)) app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
}
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseMiddleware<SameOriginMutationMiddleware>();
if (app.Environment.IsDevelopment()) app.UseCors("development-web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy", utc = DateTime.UtcNow }))
    .AllowAnonymous().ExcludeFromDescription();
app.MapGet("/health/ready", async (GoldenHourDbContext dbContext, CancellationToken cancellationToken) =>
{
    try
    {
        var connected = dbContext.Database.IsInMemory() || await dbContext.Database.CanConnectAsync(cancellationToken);
        return connected
            ? Results.Ok(new { status = "ready", database = "available" })
            : Results.Json(new { status = "not-ready", database = "unavailable" }, statusCode: 503);
    }
    catch
    {
        return Results.Json(new { status = "not-ready", database = "unavailable" }, statusCode: 503);
    }
}).AllowAnonymous().ExcludeFromDescription();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var api = app.MapGroup("/api/v1");
var auth = api.MapGroup("/auth").RequireRateLimiting("auth");

auth.MapPost("/register", async Task<IResult> (
    RegisterRequest request,
    UserManager<ApplicationUser> userManager,
    GoldenHourDbContext dbContext,
    TokenService tokenService,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Email and password are required."] });
    var user = new ApplicationUser
    {
        Id = Guid.NewGuid(),
        UserName = request.Email.Trim(),
        Email = request.Email.Trim(),
        PreferredLanguage = string.IsNullOrWhiteSpace(request.PreferredLanguage) ? "en" : request.PreferredLanguage.Trim()
    };
    var result = await userManager.CreateAsync(user, request.Password);
    if (!result.Succeeded)
        return Results.ValidationProblem(result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
    await userManager.AddToRoleAsync(user, "User");
    dbContext.EmergencyProfiles.Add(new EmergencyProfile { OwnerId = user.Id, PreferredLanguage = user.PreferredLanguage });
    await dbContext.SaveChangesAsync(cancellationToken);
    var pair = await tokenService.IssueAsync(user, cancellationToken);
    AuthCookies.Set(context.Response, pair, secure: !app.Environment.IsDevelopment() || context.Request.IsHttps);
    return Results.Created("/api/v1/auth/me", new AuthUserResponse(user.Id, user.Email!, user.PreferredLanguage, pair.AccessExpiresAtUtc));
}).AllowAnonymous();

auth.MapPost("/login", async Task<IResult> (
    LoginRequest request,
    UserManager<ApplicationUser> userManager,
    TokenService tokenService,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var user = await userManager.FindByEmailAsync(request.Email.Trim());
    if (user is null) return Results.Problem(statusCode: 401, title: "Invalid credentials");
    if (await userManager.IsLockedOutAsync(user)) return Results.Problem(statusCode: 423, title: "Account temporarily locked");
    if (!await userManager.CheckPasswordAsync(user, request.Password))
    {
        await userManager.AccessFailedAsync(user);
        return Results.Problem(statusCode: 401, title: "Invalid credentials");
    }
    await userManager.ResetAccessFailedCountAsync(user);
    var pair = await tokenService.IssueAsync(user, cancellationToken);
    AuthCookies.Set(context.Response, pair, secure: !app.Environment.IsDevelopment() || context.Request.IsHttps);
    return Results.Ok(new AuthUserResponse(user.Id, user.Email!, user.PreferredLanguage, pair.AccessExpiresAtUtc));
}).AllowAnonymous();

auth.MapPost("/refresh", async Task<IResult> (HttpContext context, TokenService tokenService, CancellationToken cancellationToken) =>
{
    if (!context.Request.Cookies.TryGetValue(AuthCookies.Refresh, out var raw))
        return Results.Problem(statusCode: 401, title: "Refresh token required");
    try
    {
        var pair = await tokenService.RotateAsync(raw, cancellationToken);
        AuthCookies.Set(context.Response, pair, secure: !app.Environment.IsDevelopment() || context.Request.IsHttps);
        return Results.Ok(new { accessExpiresAtUtc = pair.AccessExpiresAtUtc });
    }
    catch (Exception exception) when (exception is SecurityTokenException or RefreshTokenReuseException)
    {
        AuthCookies.Clear(context.Response, secure: !app.Environment.IsDevelopment() || context.Request.IsHttps);
        return Results.Problem(statusCode: 401, title: "Refresh token rejected");
    }
}).AllowAnonymous();

auth.MapPost("/logout", async (HttpContext context, TokenService tokenService, CancellationToken cancellationToken) =>
{
    var userId = RequireUserId(context.User);
    context.Request.Cookies.TryGetValue(AuthCookies.Refresh, out var raw);
    await tokenService.RevokeAsync(raw, userId, cancellationToken);
    AuthCookies.Clear(context.Response, secure: !app.Environment.IsDevelopment() || context.Request.IsHttps);
    return Results.NoContent();
}).RequireAuthorization();

auth.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> manager) =>
{
    var user = await manager.FindByIdAsync(RequireUserId(context.User).ToString());
    return user is null ? Results.NotFound() : Results.Ok(new { user.Id, user.Email, user.PreferredLanguage });
}).RequireAuthorization();

api.MapGet("/profile", async (HttpContext context, ProfileCoordinator coordinator, CancellationToken cancellationToken) =>
{
    var profile = await coordinator.GetAsync(RequireUserId(context.User), cancellationToken);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
}).RequireAuthorization();

api.MapPut("/profile", async Task<IResult> (ProfileUpsertRequest request, HttpContext context, ProfileCoordinator coordinator, CancellationToken cancellationToken) =>
{
    var errors = ValidateProfile(request);
    return errors.Count > 0
        ? Results.ValidationProblem(errors)
        : Results.Ok(await coordinator.UpsertAsync(RequireUserId(context.User), request, cancellationToken));
}).RequireAuthorization();

api.MapGet("/readiness", async (HttpContext context, ProfileCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.ReadinessAsync(RequireUserId(context.User), cancellationToken))).RequireAuthorization();

var sessions = api.MapGroup("/sessions");
sessions.MapGet("/", async (int? limit, DateTime? beforeUtc, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.ListAsync(RequireUserId(context.User), limit ?? 20, beforeUtc, cancellationToken))).RequireAuthorization();

sessions.MapPost("/", async (CreateSessionRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
{
    if (request.CountryCode.Length != 2 || !request.CountryCode.All(char.IsAsciiLetter) || request.TypedLocation?.Length > 300)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["session"] = ["Country code or typed location is invalid."] });
    var ownerId = TryGetUserId(context.User);
    var response = await coordinator.CreateAsync(ownerId, request, cancellationToken);
    return Results.Created($"/api/v1/sessions/{response.Id}", response);
}).AllowAnonymous().RequireRateLimiting("anonymous-start");

sessions.MapGet("/{sessionId:guid}", async (Guid sessionId, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.GetAsync(sessionId, TryGetUserId(context.User), GetAnonymousSessionToken(context.Request), cancellationToken))).AllowAnonymous();

sessions.MapPost("/{sessionId:guid}/incident", async (Guid sessionId, SubmitIncidentRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.SubmitIncidentAsync(sessionId, TryGetUserId(context.User), request, cancellationToken, GetAnonymousSessionToken(context.Request)))).AllowAnonymous().RequireRateLimiting("anonymous-start");

sessions.MapPost("/{sessionId:guid}/voice", async Task<IResult> (Guid sessionId, HttpRequest request, HttpContext context, ISpeechToTextProvider speech, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType) return Results.Problem(statusCode: 415, title: "Multipart audio upload required");
    var form = await request.ReadFormAsync(cancellationToken);
    var audio = form.Files.GetFile("audio");
    if (audio is null || audio.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["audio"] = ["A non-empty audio recording is required."] });
    if (audio.Length > 5 * 1024 * 1024) return Results.Problem(statusCode: 413, title: "Audio upload is too large");
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/webm", "audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/ogg" };
    if (!allowed.Contains(audio.ContentType)) return Results.Problem(statusCode: 415, title: "Unsupported audio format");
    await using var stream = audio.OpenReadStream();
    var transcription = await speech.TranscribeAsync(stream, audio.ContentType, form["languageHint"].FirstOrDefault(), cancellationToken);
    var response = await coordinator.SubmitIncidentAsync(sessionId, TryGetUserId(context.User),
        new SubmitIncidentRequest(transcription.OriginalTranscript, transcription.DetectedLanguage, null), cancellationToken, GetAnonymousSessionToken(request));
    return Results.Ok(response);
}).AllowAnonymous().RequireRateLimiting("anonymous-start").DisableAntiforgery();

sessions.MapPost("/{sessionId:guid}/answers", async Task<IResult> (Guid sessionId, CriticalAnswersRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
{
    IReadOnlyDictionary<string, string> answers = request.Answers
        ?? (!string.IsNullOrWhiteSpace(request.QuestionId) && request.Answer is not null
            ? new Dictionary<string, string> { [request.QuestionId] = request.Answer }
            : new Dictionary<string, string>());
    if (answers.Count is 0 or > 3 || answers.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value) || x.Key.Length > 80 || x.Value.Length > 500))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["answers"] = ["Provide one to three bounded critical answers."] });
    SessionResponse? response = null;
    foreach (var answer in answers.OrderBy(x => x.Key, StringComparer.Ordinal))
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(answer.Value)))[..16];
        response = await coordinator.AddTimelineAsync(sessionId, TryGetUserId(context.User),
            new TimelineUpdateRequest("critical-answer", $"Critical question {answer.Key} was answered and recorded.", $"answer:{answer.Key}:{digest}"),
            cancellationToken, GetAnonymousSessionToken(context.Request));
    }
    return Results.Ok(response);
}).AllowAnonymous().RequireRateLimiting("anonymous-start");

sessions.MapPost("/{sessionId:guid}/timeline", async (Guid sessionId, TimelineUpdateRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.AddTimelineAsync(sessionId, TryGetUserId(context.User), request, cancellationToken, GetAnonymousSessionToken(context.Request)))).AllowAnonymous();

sessions.MapPost("/{sessionId:guid}/locations", async (Guid sessionId, LocationUpdateRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.AddLocationAsync(sessionId, TryGetUserId(context.User), request, cancellationToken, GetAnonymousSessionToken(context.Request)))).AllowAnonymous();

sessions.MapPost("/{sessionId:guid}/tasks", async (Guid sessionId, CreateTaskRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.AddTaskAsync(sessionId, RequireUserId(context.User), request, cancellationToken))).RequireAuthorization();

sessions.MapPatch("/{sessionId:guid}/tasks/{taskId:guid}", async (Guid sessionId, Guid taskId, UpdateTaskRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.UpdateTaskAsync(sessionId, taskId, RequireUserId(context.User), request, cancellationToken))).RequireAuthorization();

sessions.MapPost("/{sessionId:guid}/share-tokens", async (Guid sessionId, CreateShareTokenRequest request, HttpContext context, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.CreateAsync(sessionId, RequireUserId(context.User), request.LifetimeMinutes, cancellationToken))).RequireAuthorization();

sessions.MapDelete("/{sessionId:guid}/share-tokens/{tokenId:guid}", async (Guid sessionId, Guid tokenId, HttpContext context, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
{
    await coordinator.RevokeAsync(sessionId, tokenId, RequireUserId(context.User), cancellationToken);
    return Results.NoContent();
}).RequireAuthorization();

sessions.MapPost("/{sessionId:guid}/participants/invitations", async (Guid sessionId, InviteParticipantRequest request, HttpContext context, ParticipantCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.InviteAsync(sessionId, RequireUserId(context.User), request, cancellationToken))).RequireAuthorization();

sessions.MapPost("/{sessionId:guid}/participants/join", async (Guid sessionId, JoinParticipantRequest request, HttpContext context, ParticipantCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.JoinAsync(sessionId, RequireUserId(context.User), request.Token, cancellationToken))).RequireAuthorization().RequireRateLimiting("bystander");

sessions.MapPost("/{sessionId:guid}/participants/{participantId:guid}/acknowledge", async (Guid sessionId, Guid participantId, HttpContext context, ParticipantCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.AcknowledgeAsync(sessionId, participantId, RequireUserId(context.User), cancellationToken))).RequireAuthorization();

sessions.MapPost("/{sessionId:guid}/summaries/{kind}", async Task<IResult> (Guid sessionId, string kind, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
{
    if (!TryParseSummaryKind(kind, out var summaryKind)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Use family, responder, or hospital-handover."] });
    return Results.Ok(await coordinator.GenerateSummaryAsync(sessionId, RequireUserId(context.User), summaryKind, cancellationToken));
}).RequireAuthorization();

sessions.MapPost("/{sessionId:guid}/close", async (Guid sessionId, CloseSessionRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.CloseAsync(sessionId, RequireUserId(context.User), request.ConcurrencyToken, cancellationToken))).RequireAuthorization();

api.MapGet("/bystander/{token}", async (string token, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.GetProjectionAsync(token, cancellationToken))).AllowAnonymous().RequireRateLimiting("bystander");

api.MapPost("/bystander/{token}/observations", async (string token, BystanderObservationRequest request, HttpContext context, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
{
    await coordinator.RecordObservationAsync(token, request, context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken);
    return Results.Accepted();
}).AllowAnonymous().RequireRateLimiting("bystander");

api.MapPost("/bystander/{token}/location", async (string token, BystanderLocationRequest request, HttpContext context, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
{
    await coordinator.RecordLocationAsync(token, request, context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken);
    return Results.Accepted();
}).AllowAnonymous().RequireRateLimiting("bystander");

api.MapPost("/webhooks/{provider}", async Task<IResult> (string provider, HttpRequest request, WebhookCoordinator coordinator, CancellationToken cancellationToken) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer, cancellationToken);
    if (buffer.Length > 65_536) return Results.Problem(statusCode: 413, title: "Webhook payload is too large");
    var deliveryId = request.Headers["X-GoldenHour-Delivery-Id"].FirstOrDefault() ?? string.Empty;
    var timestamp = request.Headers["X-GoldenHour-Timestamp"].FirstOrDefault() ?? string.Empty;
    var signature = request.Headers["X-GoldenHour-Signature"].FirstOrDefault() ?? string.Empty;
    var result = await coordinator.ProcessAsync(provider, deliveryId, timestamp, signature, buffer.ToArray(), cancellationToken);
    return Results.Accepted(value: result);
}).AllowAnonymous().RequireRateLimiting("bystander");

app.MapHub<EmergencyHub>("/hubs/emergency");
var spaIndex = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
if (File.Exists(spaIndex)) app.MapFallbackToFile("index.html");

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
    if (!migrationDb.Database.IsInMemory()) await migrationDb.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
}

await DemoSeeder.InitializeAsync(app.Services, app.Environment, app.Configuration, app.Lifetime.ApplicationStopping);
await app.RunAsync();

static void ConfigureOpenAiClient(IServiceProvider serviceProvider, HttpClient client)
{
    var configured = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenAiOptions>>().Value;
    client.BaseAddress = new Uri(configured.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(configured.TimeoutSeconds, 3, 60));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GoldenHourAI/1.0");
}

static Guid? TryGetUserId(ClaimsPrincipal principal) =>
    Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

static Guid RequireUserId(ClaimsPrincipal principal) =>
    TryGetUserId(principal) ?? throw new UnauthorizedAccessException("Authentication required.");

static string? GetAnonymousSessionToken(HttpRequest request) =>
    request.Headers["X-Emergency-Access-Token"].FirstOrDefault();

static Dictionary<string, string[]> ValidateProfile(ProfileUpsertRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Length > 160) errors["fullName"] = ["Full name is required and must be 160 characters or fewer."];
    if (string.IsNullOrWhiteSpace(request.PreferredLanguage) || request.PreferredLanguage.Length > 12) errors["preferredLanguage"] = ["A valid preferred language is required."];
    if (request.ResponseMode is not ("text" or "audio" or "both")) errors["responseMode"] = ["Response mode must be text, audio, or both."];
    if (request.Contacts.Count > 10) errors["contacts"] = ["No more than 10 emergency contacts are allowed."];
    if (request.Contacts.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.PhoneNumber))) errors["contacts"] = ["Every contact requires a name and phone number."];
    return errors;
}

static bool TryParseSummaryKind(string value, out SummaryKind kind)
{
    kind = value switch
    {
        "family" => SummaryKind.Family,
        "responder" => SummaryKind.Responder,
        "hospital-handover" => SummaryKind.HospitalHandover,
        _ => default
    };
    return value is "family" or "responder" or "hospital-handover";
}

public sealed record CloseSessionRequest(Guid ConcurrencyToken);

public partial class Program;
