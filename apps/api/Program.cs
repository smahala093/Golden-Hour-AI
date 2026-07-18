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
using Microsoft.Extensions.Options;

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
    .Validate(x => !string.IsNullOrWhiteSpace(x.DefaultNumber)
        && x.DefaultNumber.Length is >= 2 and <= 16
        && (x.DefaultNumber[0] == '+'
            ? x.DefaultNumber.Length >= 3 && x.DefaultNumber.AsSpan(1).ToArray().All(char.IsAsciiDigit)
            : x.DefaultNumber.All(char.IsAsciiDigit))
        && x.AiConfidenceThreshold is >= 0 and <= 1,
        "Emergency configuration requires a safe 2 to 16 character telephone number and a confidence threshold from zero to one.")
    .ValidateOnStart();
builder.Services.AddOptions<AuthenticationOptions>().Bind(builder.Configuration.GetSection("Authentication"))
    .Validate(x => !string.IsNullOrWhiteSpace(x.Jwt.Issuer) && !string.IsNullOrWhiteSpace(x.Jwt.Audience)
        && Encoding.UTF8.GetByteCount(x.Jwt.SigningKey) >= 32 && x.AccessTokenMinutes is >= 1 and <= 60 && x.RefreshTokenDays is >= 1 and <= 30,
        "Authentication configuration must include issuer, audience, and a signing key of at least 32 UTF-8 bytes.")
    .ValidateOnStart();
builder.Services.AddOptions<OpenAiOptions>().Bind(builder.Configuration.GetSection("OpenAI"))
    .Validate(x => Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(x.Model)
        && !string.IsNullOrWhiteSpace(x.SpeechModel) && !string.IsNullOrWhiteSpace(x.TextToSpeechModel)
        && !string.IsNullOrWhiteSpace(x.TextToSpeechVoice), "OpenAI base URL and model names are required.")
    .Validate(x => configuredUseMocks || !string.IsNullOrWhiteSpace(x.ApiKey), "OpenAI API key is required when provider mocks are disabled.")
    .ValidateOnStart();
builder.Services.AddOptions<WebhookOptions>().Bind(builder.Configuration.GetSection("Webhook"))
    .Validate(x => !x.Enabled || (!string.IsNullOrWhiteSpace(x.SigningSecret) && x.SigningSecret.Length >= 16
        && x.AllowedClockSkewMinutes is >= 1 and <= 15 && x.AllowedProviders is { Length: > 0 } && x.AllowedStatuses is { Length: > 0 }),
        "An enabled webhook requires a signing secret, bounded clock skew, providers, and statuses.")
    .ValidateOnStart();
builder.Services.AddOptions<SmsGatewayOptions>().Bind(builder.Configuration.GetSection("SmsGateway"))
    .Validate(options => configuredUseMocks || (options.Enabled
        && Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps
        && Encoding.UTF8.GetByteCount(options.SigningSecret) >= 32
        && options.TimeoutSeconds is >= 2 and <= 30
        && options.MaximumResponseBytes is >= 1_024 and <= 65_536),
        "A production SMS gateway requires an HTTPS endpoint, a 32-byte signing secret, and bounded timeout/response settings.")
    .ValidateOnStart();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("Security:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var address)) options.KnownProxies.Add(address);
    }
    foreach (var network in builder.Configuration.GetSection("Security:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = network.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var prefixLength))
        {
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
        }
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
        options.UseInMemoryDatabase(builder.Configuration["Database:Name"] ?? $"golden-hour-{builder.Environment.EnvironmentName}");
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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<AuthenticationOptions>>((options, configuredOptions) =>
    {
        var authentication = configuredOptions.Value;
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
    var authPermitLimit = builder.Configuration.GetValue("RateLimits:AuthPermitLimit", 10);
    var bystanderPermitLimit = builder.Configuration.GetValue("RateLimits:BystanderPermitLimit", 30);
    var anonymousStartPermitLimit = builder.Configuration.GetValue("RateLimits:AnonymousStartPermitLimit", 15);
    var globalPermitLimit = builder.Configuration.GetValue("RateLimits:GlobalPermitLimit", 180);
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("bystander", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = bystanderPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("anonymous-start", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = anonymousStartPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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
builder.Services.AddScoped<ContactVerificationCoordinator>();
builder.Services.AddScoped<SessionCoordinator>();
builder.Services.AddScoped<ShareTokenCoordinator>();
builder.Services.AddScoped<ParticipantCoordinator>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<WebhookCoordinator>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddSingleton<IEventBus, NullEventBus>();
builder.Services.AddSingleton<IFileStorage, UnavailableFileStorage>();
builder.Services.AddSingleton<IMapProvider, MockMapProvider>();
builder.Services.AddSingleton<MockNotificationProvider>();
builder.Services.AddSingleton<IEmailProvider>(provider => provider.GetRequiredService<MockNotificationProvider>());
builder.Services.AddSingleton<INotificationProvider>(provider => provider.GetRequiredService<MockNotificationProvider>());

if (configuredUseMocks)
{
    builder.Services.AddSingleton<ISmsProvider>(provider => provider.GetRequiredService<MockNotificationProvider>());
    builder.Services.AddSingleton<IAiProvider, MockAiProvider>();
    builder.Services.AddSingleton<ISpeechToTextProvider, MockSpeechToTextProvider>();
    builder.Services.AddSingleton<ITextToSpeechProvider, MockTextToSpeechProvider>();
}
else
{
    builder.Services.AddHttpClient<HmacSmsGatewayProvider>((serviceProvider, client) =>
    {
        var configured = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmsGatewayOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(configured.TimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GoldenHourAI-SmsGateway/1.0");
    });
    builder.Services.AddScoped<ISmsProvider>(provider => provider.GetRequiredService<HmacSmsGatewayProvider>());
    builder.Services.AddHttpClient<OpenAiResponsesProvider>((serviceProvider, client) => ConfigureOpenAiClient(serviceProvider, client));
    builder.Services.AddHttpClient<OpenAiSpeechToTextProvider>((serviceProvider, client) => ConfigureOpenAiClient(serviceProvider, client));
    builder.Services.AddHttpClient<OpenAiTextToSpeechProvider>((serviceProvider, client) => ConfigureOpenAiClient(serviceProvider, client));
    builder.Services.AddScoped<IAiProvider>(provider => provider.GetRequiredService<OpenAiResponsesProvider>());
    builder.Services.AddScoped<ISpeechToTextProvider>(provider => provider.GetRequiredService<OpenAiSpeechToTextProvider>());
    builder.Services.AddScoped<ITextToSpeechProvider>(provider => provider.GetRequiredService<OpenAiTextToSpeechProvider>());
}
builder.Services.AddHostedService<OutboxDispatcher>();

var app = builder.Build();
if (app.Configuration.GetValue<bool>("Security:UseForwardedHeaders")) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    if (app.Configuration.GetValue("Security:RequireHttps", true)) app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
}
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SensitiveResponseCacheMiddleware>();
app.UseExceptionHandler();
app.UseMiddleware<SameOriginMutationMiddleware>();
if (app.Environment.IsDevelopment()) app.UseCors("development-web");
app.UseAuthentication();
app.UseRateLimiter();
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
api.MapGet("/configuration", (IOptions<EmergencyOptions> emergencyOptions) =>
    Results.Ok(new { emergencyNumber = emergencyOptions.Value.DefaultNumber })).AllowAnonymous();
api.MapGet("/protocols", (string? country, ProtocolCatalogue catalogue) =>
    Results.Ok(catalogue.List(string.IsNullOrWhiteSpace(country) ? "IN" : country))).AllowAnonymous();
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
    var roleResult = await userManager.AddToRoleAsync(user, "User");
    if (!roleResult.Succeeded)
    {
        await userManager.DeleteAsync(user);
        return Results.ValidationProblem(roleResult.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
    }
    try
    {
        var profile = new EmergencyProfile { OwnerId = user.Id, PreferredLanguage = user.PreferredLanguage };
        dbContext.EmergencyProfiles.Add(profile);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = user.Id,
            Action = "emergency-profile-created",
            ResourceType = "EmergencyProfile",
            ResourceId = profile.Id.ToString()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch
    {
        await userManager.DeleteAsync(user);
        throw;
    }
    var pair = await tokenService.IssueAsync(user, cancellationToken);
    AuthCookies.Set(context.Response, pair, secure: !app.Environment.IsDevelopment() || context.Request.IsHttps);
    return Results.Created("/api/v1/auth/me", new AuthUserResponse(user.Id, user.Email!, user.PreferredLanguage, pair.AccessExpiresAtUtc));
}).AllowAnonymous();

auth.MapPost("/login", async Task<IResult> (
    LoginRequest request,
    UserManager<ApplicationUser> userManager,
    GoldenHourDbContext dbContext,
    TokenService tokenService,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.Problem(statusCode: 401, title: "Invalid credentials");
    var user = await userManager.FindByEmailAsync(request.Email.Trim());
    if (user is null) return Results.Problem(statusCode: 401, title: "Invalid credentials");
    if (await userManager.IsLockedOutAsync(user))
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Action = "authentication-rejected-account-locked",
            ResourceType = "ApplicationUser",
            ResourceId = user.Id.ToString()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Problem(statusCode: 401, title: "Invalid credentials");
    }
    if (!await userManager.CheckPasswordAsync(user, request.Password))
    {
        await userManager.AccessFailedAsync(user);
        if (await userManager.IsLockedOutAsync(user))
        {
            dbContext.AuditEvents.Add(new AuditEvent
            {
                Action = "authentication-rejected-account-locked",
                ResourceType = "ApplicationUser",
                ResourceId = user.Id.ToString()
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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

api.MapPost("/profile/contacts/{contactId:guid}/verification", async Task<IResult> (
    Guid contactId,
    HttpContext context,
    IWebHostEnvironment environment,
    ISmsProvider smsProvider,
    ContactVerificationCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    if (smsProvider is MockNotificationProvider && !(environment.IsDevelopment() && configuredUseMocks))
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Contact verification delivery is unavailable",
            detail: "Configure a production SMS provider before requesting contact verification.");
    var includeDevelopmentCode = environment.IsDevelopment() && configuredUseMocks && smsProvider is MockNotificationProvider;
    return Results.Accepted(value: await coordinator.RequestAsync(
        RequireUserId(context.User), contactId, includeDevelopmentCode, cancellationToken));
}).RequireAuthorization().RequireRateLimiting("auth");

api.MapPost("/profile/contacts/{contactId:guid}/verification/confirm", async Task<IResult> (
    Guid contactId,
    ConfirmContactVerificationRequest request,
    HttpContext context,
    ContactVerificationCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    await coordinator.VerifyAsync(
        RequireUserId(context.User), contactId, request.Challenge, request.Code, cancellationToken);
    return Results.NoContent();
}).RequireAuthorization().RequireRateLimiting("auth");

api.MapGet("/readiness", async (HttpContext context, ProfileCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.ReadinessAsync(RequireUserId(context.User), cancellationToken))).RequireAuthorization();

var sessions = api.MapGroup("/sessions");
sessions.MapGet("/", async (int? limit, DateTime? beforeUtc, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.ListAsync(RequireUserId(context.User), limit ?? 20, beforeUtc, cancellationToken))).RequireAuthorization();

sessions.MapPost("/", async (CreateSessionRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.CountryCode) || request.CountryCode.Length != 2 || !request.CountryCode.All(char.IsAsciiLetter) || request.TypedLocation?.Length > 300)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["session"] = ["Country code or typed location is invalid."] });
    var ownerId = TryGetUserId(context.User);
    var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
    var response = await coordinator.CreateAsync(ownerId, request, idempotencyKey, cancellationToken);
    return Results.Created($"/api/v1/sessions/{response.Id}", response);
}).AllowAnonymous().RequireRateLimiting("anonymous-start");

sessions.MapGet("/{sessionId:guid}", async (Guid sessionId, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.GetAsync(sessionId, TryGetUserId(context.User), GetAnonymousSessionToken(context.Request), cancellationToken))).AllowAnonymous();

sessions.MapPost("/{sessionId:guid}/incident", async (Guid sessionId, SubmitIncidentRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.SubmitIncidentAsync(sessionId, TryGetUserId(context.User), request,
        context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken, GetAnonymousSessionToken(context.Request)))).AllowAnonymous().RequireRateLimiting("anonymous-start");

sessions.MapPost("/{sessionId:guid}/voice", async Task<IResult> (Guid sessionId, HttpRequest request, HttpContext context, ISpeechToTextProvider speech, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
{
    var userId = TryGetUserId(context.User);
    var anonymousAccessToken = GetAnonymousSessionToken(request);
    await coordinator.EnsureAuthorizedAsync(sessionId, userId, anonymousAccessToken, cancellationToken);
    if (!request.HasFormContentType) return Results.Problem(statusCode: 415, title: "Multipart audio upload required");
    if (request.ContentLength is > 5_505_024) return Results.Problem(statusCode: 413, title: "Audio upload is too large");
    var form = await request.ReadFormAsync(cancellationToken);
    var languageHint = form["languageHint"].FirstOrDefault();
    if (!IsValidLanguageHint(languageHint))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["languageHint"] = ["Use a bounded BCP-47 style language tag, such as en or hi-IN."] });
    var audio = form.Files.GetFile("audio");
    if (audio is null || audio.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["audio"] = ["A non-empty audio recording is required."] });
    if (audio.Length > 5 * 1024 * 1024) return Results.Problem(statusCode: 413, title: "Audio upload is too large");
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "audio/webm", "audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/ogg" };
    if (!allowed.Contains(audio.ContentType)) return Results.Problem(statusCode: 415, title: "Unsupported audio format");
    if (!await HasValidAudioSignatureAsync(audio, cancellationToken)) return Results.Problem(statusCode: 415, title: "Audio content does not match its declared format");
    await using var stream = audio.OpenReadStream();
    var transcription = await speech.TranscribeAsync(stream, audio.ContentType, string.IsNullOrWhiteSpace(languageHint) ? null : languageHint.Trim(), cancellationToken);
    if (transcription.DurationSeconds is null or <= 0 or > 30)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["audio"] = ["Audio duration must be provider-confirmed and no longer than 30 seconds."] });
    var response = await coordinator.SubmitIncidentAsync(sessionId, userId,
        new SubmitIncidentRequest(transcription.OriginalTranscript, transcription.DetectedLanguage, null),
        request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken, anonymousAccessToken);
    return Results.Ok(response);
}).AllowAnonymous().RequireRateLimiting("anonymous-start").DisableAntiforgery()
    .WithMetadata(new RequestSizeLimitAttribute(5_505_024), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = 5_505_024 });

sessions.MapPost("/{sessionId:guid}/protocol-audio", async Task<IResult> (
    Guid sessionId,
    HttpContext context,
    SessionCoordinator coordinator,
    ITextToSpeechProvider speech,
    CancellationToken cancellationToken) =>
{
    var session = await coordinator.GetAsync(sessionId, TryGetUserId(context.User), GetAnonymousSessionToken(context.Request), cancellationToken);
    if (session.Protocol is null) return Results.Problem(statusCode: 409, title: "Select a reviewed protocol before requesting read-aloud audio");
    if (speech is MockTextToSpeechProvider)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Read-aloud audio is unavailable in deterministic mock mode",
            detail: "Use the reviewed protocol text displayed on screen.");
    }
    var audio = await speech.SynthesizeAsync(CreateApprovedProtocolNarration(session.Protocol), session.OriginalLanguage ?? "en", cancellationToken);
    return Results.Stream(audio, "audio/mpeg", enableRangeProcessing: false);
}).AllowAnonymous().RequireRateLimiting("anonymous-start");

sessions.MapPost("/{sessionId:guid}/answers", async Task<IResult> (Guid sessionId, CriticalAnswersRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
{
    IReadOnlyDictionary<string, string> answers = request.Answers
        ?? (!string.IsNullOrWhiteSpace(request.QuestionId) && request.Answer is not null
            ? new Dictionary<string, string> { [request.QuestionId] = request.Answer }
            : new Dictionary<string, string>());
    if (answers.Count is 0 or > 3 || answers.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value) || x.Key.Length > 80 || x.Value.Length > 500))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["answers"] = ["Provide one to three bounded critical answers."] });
    var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
    return Results.Ok(await coordinator.ApplyCriticalAnswersAsync(sessionId, TryGetUserId(context.User), answers, idempotencyKey,
        GetAnonymousSessionToken(context.Request), cancellationToken));
}).AllowAnonymous().RequireRateLimiting("anonymous-start");

sessions.MapPost("/{sessionId:guid}/timeline", async (Guid sessionId, TimelineUpdateRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.AddTimelineAsync(sessionId, TryGetUserId(context.User), request, cancellationToken, GetAnonymousSessionToken(context.Request)))).AllowAnonymous();

sessions.MapPost("/{sessionId:guid}/locations", async (Guid sessionId, LocationUpdateRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.AddLocationAsync(sessionId, TryGetUserId(context.User), request, cancellationToken, GetAnonymousSessionToken(context.Request)))).AllowAnonymous();

sessions.MapPost("/{sessionId:guid}/tasks", async (Guid sessionId, CreateTaskRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.AddTaskAsync(sessionId, RequireUserId(context.User), request,
        context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken))).RequireAuthorization();

sessions.MapPatch("/{sessionId:guid}/tasks/{taskId:guid}", async (Guid sessionId, Guid taskId, UpdateTaskRequest request, HttpContext context, SessionCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.UpdateTaskAsync(sessionId, taskId, RequireUserId(context.User), request, cancellationToken))).RequireAuthorization();

sessions.MapPost("/{sessionId:guid}/share-tokens", async (Guid sessionId, CreateShareTokenRequest request, HttpContext context, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.CreateAsync(sessionId, RequireUserId(context.User), request.LifetimeMinutes,
        context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken))).RequireAuthorization();

sessions.MapDelete("/{sessionId:guid}/share-tokens/{tokenId:guid}", async (Guid sessionId, Guid tokenId, HttpContext context, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
{
    await coordinator.RevokeAsync(sessionId, tokenId, RequireUserId(context.User), cancellationToken);
    return Results.NoContent();
}).RequireAuthorization();

sessions.MapPost("/{sessionId:guid}/participants/invitations", async (Guid sessionId, InviteParticipantRequest request, HttpContext context, ParticipantCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.InviteAsync(sessionId, RequireUserId(context.User), request,
        context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken))).RequireAuthorization();

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

api.MapGet("/bystander", async (HttpRequest request, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
    Results.Ok(await coordinator.GetProjectionAsync(GetBystanderShareToken(request), cancellationToken))).AllowAnonymous().RequireRateLimiting("bystander");

api.MapPost("/bystander/observations", async (BystanderObservationRequest request, HttpContext context, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
{
    await coordinator.RecordObservationAsync(GetBystanderShareToken(context.Request), request, context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken);
    return Results.Accepted();
}).AllowAnonymous().RequireRateLimiting("bystander");

api.MapPost("/bystander/location", async (BystanderLocationRequest request, HttpContext context, ShareTokenCoordinator coordinator, CancellationToken cancellationToken) =>
{
    await coordinator.RecordLocationAsync(GetBystanderShareToken(context.Request), request, context.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty, cancellationToken);
    return Results.Accepted();
}).AllowAnonymous().RequireRateLimiting("bystander");

api.MapPost("/webhooks/{provider}", async Task<IResult> (string provider, HttpRequest request, WebhookCoordinator coordinator, CancellationToken cancellationToken) =>
{
    if (request.ContentLength is > 65_536) return Results.Problem(statusCode: 413, title: "Webhook payload is too large");
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer, cancellationToken);
    if (buffer.Length > 65_536) return Results.Problem(statusCode: 413, title: "Webhook payload is too large");
    var deliveryId = request.Headers["X-GoldenHour-Delivery-Id"].FirstOrDefault() ?? string.Empty;
    var timestamp = request.Headers["X-GoldenHour-Timestamp"].FirstOrDefault() ?? string.Empty;
    var signature = request.Headers["X-GoldenHour-Signature"].FirstOrDefault() ?? string.Empty;
    var result = await coordinator.ProcessAsync(provider, deliveryId, timestamp, signature, buffer.ToArray(), cancellationToken);
    return Results.Accepted(value: result);
}).AllowAnonymous().RequireRateLimiting("bystander").WithMetadata(new RequestSizeLimitAttribute(65_536));

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

static string GetBystanderShareToken(HttpRequest request)
{
    var token = request.Headers["X-Emergency-Share-Token"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
        throw new UnauthorizedAccessException("A valid emergency share token is required.");
    return token;
}

static async Task<bool> HasValidAudioSignatureAsync(IFormFile audio, CancellationToken cancellationToken)
{
    var header = new byte[12];
    await using var stream = audio.OpenReadStream();
    var read = await stream.ReadAsync(header, cancellationToken);
    return audio.ContentType.ToLowerInvariant() switch
    {
        "audio/webm" => audio.Length >= 32 && read >= 4 && header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
        "audio/wav" or "audio/x-wav" => audio.Length >= 44 && read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WAVE"u8),
        "audio/mpeg" => audio.Length >= 128 && read >= 3 && (header.AsSpan(0, 3).SequenceEqual("ID3"u8) || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)),
        "audio/mp4" => audio.Length >= 24 && read >= 8 && header.AsSpan(4, 4).SequenceEqual("ftyp"u8),
        "audio/ogg" => audio.Length >= 27 && read >= 4 && header.AsSpan(0, 4).SequenceEqual("OggS"u8),
        _ => false
    };
}

static bool IsValidLanguageHint(string? languageHint) =>
    string.IsNullOrWhiteSpace(languageHint)
    || languageHint.Length <= 35 && System.Text.RegularExpressions.Regex.IsMatch(
        languageHint,
        "^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

static string CreateApprovedProtocolNarration(ProtocolResponse protocol)
{
    var builder = new StringBuilder();
    builder.Append(protocol.Notice).Append(' ').Append(protocol.EmergencyCallInstruction);
    foreach (var action in protocol.DoActions) builder.Append(" Do: ").Append(action);
    foreach (var action in protocol.DoNotActions) builder.Append(" Do not: ").Append(action);
    builder.Append(' ').Append(protocol.EscalationRule);
    return builder.ToString();
}

static Dictionary<string, string[]> ValidateProfile(ProfileUpsertRequest request)
{
    var errors = new Dictionary<string, string[]>();
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var currentYear = today.Year;
    if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Length > 160) errors["fullName"] = ["Full name is required and must be 160 characters or fewer."];
    if (request.DateOfBirth is { } dateOfBirth && (dateOfBirth < new DateOnly(1900, 1, 1) || dateOfBirth > today)) errors["dateOfBirth"] = ["Date of birth must be between 1900-01-01 and today."];
    var bloodGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };
    if (!string.IsNullOrWhiteSpace(request.BloodGroup) && !bloodGroups.Contains(request.BloodGroup.Trim())) errors["bloodGroup"] = ["Blood group must use a supported ABO and Rh value."];
    if (string.IsNullOrWhiteSpace(request.PreferredLanguage) || request.PreferredLanguage.Length > 12 || !IsValidLanguageHint(request.PreferredLanguage)) errors["preferredLanguage"] = ["A valid BCP-47 style preferred language of 12 characters or fewer is required."];
    if (request.ResponseMode is not ("text" or "audio" or "both")) errors["responseMode"] = ["Response mode must be text, audio, or both."];
    if (request.InsuranceDetails?.Length > 1_000) errors["insuranceDetails"] = ["Insurance details must be 1,000 characters or fewer."];
    if (request.DoctorContact?.Length > 300) errors["doctorContact"] = ["Doctor contact must be 300 characters or fewer."];

    if (request.Contacts is null || request.Contacts.Count > 10
        || request.Contacts.Any(contact => contact is null
            || string.IsNullOrWhiteSpace(contact.Name) || contact.Name.Length > 160
            || string.IsNullOrWhiteSpace(contact.Relationship) || contact.Relationship.Length > 80
            || string.IsNullOrWhiteSpace(contact.PhoneNumber) || contact.PhoneNumber.Length > 40))
        errors["contacts"] = ["Provide at most 10 contacts; each requires bounded name, relationship, and phone fields."];
    else
    {
        var normalizedPhones = request.Contacts.Select(contact => string.Concat(contact.PhoneNumber.Where(char.IsAsciiDigit))).ToArray();
        var displayPhonePattern = new System.Text.RegularExpressions.Regex(
            @"^\+?[0-9() .-]+$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50));
        if (request.Contacts.Select((contact, index) => normalizedPhones[index].Length is < 7 or > 15 || !displayPhonePattern.IsMatch(contact.PhoneNumber)).Any(invalid => invalid))
            errors["contacts"] = ["Contact phone numbers must contain 7 to 15 ASCII digits; display spaces, parentheses, periods, hyphens, and a leading + are allowed."];
        else if (normalizedPhones.Distinct(StringComparer.Ordinal).Count() != normalizedPhones.Length)
            errors["contacts"] = ["Emergency contact phone numbers must be unique after normalization."];
    }

    ValidateNamedItems(request.Allergies, "allergies");
    ValidateNamedItems(request.Conditions, "conditions");
    ValidateNamedItems(request.Medications, "medications");
    if (request.Procedures is null || request.Procedures.Count > 50
        || request.Procedures.Any(procedure => procedure is null || string.IsNullOrWhiteSpace(procedure.Name) || procedure.Name.Length > 160
            || procedure.Year is not null && (procedure.Year < 1900 || procedure.Year > currentYear)))
        errors["procedures"] = ["Provide at most 50 procedures with bounded names and years from 1900 through the current year."];
    if (request.PreferredHospital is not null
        && (string.IsNullOrWhiteSpace(request.PreferredHospital.Name) || request.PreferredHospital.Name.Length > 160
            || request.PreferredHospital.PhoneNumber?.Length > 40))
        errors["preferredHospital"] = ["Preferred hospital requires a bounded name and optional phone number."];
    if (request.Sharing is null) errors["sharing"] = ["Emergency sharing preferences are required."];
    return errors;

    void ValidateNamedItems(IReadOnlyList<NamedMedicalInput>? items, string key)
    {
        if (items is null || items.Count > 50 || items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 160))
            errors[key] = [$"Provide at most 50 {key}; every name must contain 1 to 160 characters."];
    }
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
