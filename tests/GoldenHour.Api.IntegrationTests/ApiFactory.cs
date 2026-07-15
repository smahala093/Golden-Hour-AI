using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using GoldenHour.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GoldenHour.Api.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<global::Program>
{
    public const string WebhookSecret = "integration-test-webhook-secret-32-characters";
    private readonly string databaseName = $"golden-hour-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
        builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:UseInMemory"] = "true",
            ["Database:Name"] = databaseName,
            ["Database:MigrateOnStartup"] = "false",
            ["Providers:UseMocks"] = "true",
            ["Authentication:Jwt:Issuer"] = "GoldenHourAI.Tests",
            ["Authentication:Jwt:Audience"] = "GoldenHourAI.Tests.Web",
            ["Authentication:Jwt:SigningKey"] = "integration-test-signing-key-at-least-thirty-two-bytes-long",
            ["Webhook:Enabled"] = "true",
            ["Webhook:SigningSecret"] = WebhookSecret,
            ["RateLimits:AuthPermitLimit"] = "1000",
            ["RateLimits:BystanderPermitLimit"] = "1000",
            ["RateLimits:AnonymousStartPermitLimit"] = "1000",
            ["RateLimits:GlobalPermitLimit"] = "1000",
            ["Seed:DemoPassword"] = "GoldenHour-Integration-Only-2026!"
        }));
    }
}

[CollectionDefinition("api", DisableParallelization = true)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

public sealed class ProductionApiFactory : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var productionDatabaseName = $"golden-hour-production-tests-{Guid.NewGuid():N}";
        builder.UseEnvironment("Production");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<GoldenHourDbContext>>();
            services.RemoveAll<GoldenHourDbContext>();
            services.AddDbContext<GoldenHourDbContext>(options => options.UseInMemoryDatabase(productionDatabaseName));
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
        });
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:UseInMemory"] = "true",
            ["Database:Name"] = productionDatabaseName,
            ["Database:MigrateOnStartup"] = "false",
            ["Providers:UseMocks"] = "true",
            ["Authentication:Jwt:Issuer"] = "GoldenHourAI.Tests",
            ["Authentication:Jwt:Audience"] = "GoldenHourAI.Tests.Web",
            ["Authentication:Jwt:SigningKey"] = "integration-test-signing-key-at-least-thirty-two-bytes-long",
            ["Webhook:Enabled"] = "false",
            ["Security:RequireHttps"] = "true",
            ["RateLimits:GlobalPermitLimit"] = "1000"
        }));
    }
}
