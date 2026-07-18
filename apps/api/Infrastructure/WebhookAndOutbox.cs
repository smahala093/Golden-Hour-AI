using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoldenHour.Api.Application;
using GoldenHour.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoldenHour.Api.Infrastructure;

public sealed class WebhookOptions
{
    public bool Enabled { get; set; } = true;
    public string SigningSecret { get; set; } = string.Empty;
    public int AllowedClockSkewMinutes { get; set; } = 5;
    public string[] AllowedProviders { get; set; } = ["mock-sms"];
    public string[] AllowedStatuses { get; set; } = ["queued", "sent", "delivered", "failed", "undelivered", "rejected"];
}

public sealed record WebhookResult(bool Duplicate, string Status);

public sealed class WebhookCoordinator(
    GoldenHourDbContext dbContext,
    IOptions<WebhookOptions> options,
    IClock clock)
{
    private static readonly SemaphoreSlim ProcessingGate = new(1, 1);

    public async Task<WebhookResult> ProcessAsync(
        string provider,
        string deliveryId,
        string timestampHeader,
        string signatureHeader,
        byte[] body,
        CancellationToken cancellationToken)
    {
        if (body.Length is 0 or > 65_536) throw new InvalidDataException("Webhook payload size is invalid.");
        if (!options.Value.Enabled) throw new InvalidOperationException("Webhook callbacks are disabled.");
        if (string.IsNullOrWhiteSpace(options.Value.SigningSecret)) throw new InvalidOperationException("Webhook signing is not configured.");
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        if (normalizedProvider.Length is 0 or > 80
            || !options.Value.AllowedProviders.Contains(normalizedProvider, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Webhook provider is not allowlisted.");
        if (!long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
            throw new UnauthorizedAccessException("Invalid webhook timestamp.");
        var providerTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        if (Math.Abs((clock.UtcNow - providerTime).TotalMinutes) > options.Value.AllowedClockSkewMinutes)
            throw new UnauthorizedAccessException("Webhook timestamp is outside the allowed window.");
        if (string.IsNullOrWhiteSpace(deliveryId) || deliveryId.Length > 160)
            throw new UnauthorizedAccessException("Webhook delivery ID is invalid.");

        var timestampBytes = Encoding.UTF8.GetBytes(timestampHeader);
        var signedBytes = new byte[timestampBytes.Length + 1 + body.Length];
        timestampBytes.CopyTo(signedBytes, 0);
        signedBytes[timestampBytes.Length] = (byte)'.';
        body.CopyTo(signedBytes, timestampBytes.Length + 1);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.SigningSecret), signedBytes);
        byte[] supplied;
        try
        {
            var value = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase) ? signatureHeader[7..] : signatureHeader;
            supplied = Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            throw new UnauthorizedAccessException("Invalid webhook signature.");
        }

        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            throw new UnauthorizedAccessException("Invalid webhook signature.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Webhook payload is not valid JSON.", exception);
        }
        using (document)
        {
            var root = document.RootElement;
            var messageId = root.TryGetProperty("messageId", out var id) ? id.GetString() : null;
            var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > 160 || string.IsNullOrWhiteSpace(status) || status.Length > 40)
                throw new InvalidDataException("Webhook payload does not contain the required delivery fields.");
            var normalizedStatus = status.Trim().ToLowerInvariant();
            if (!options.Value.AllowedStatuses.Contains(normalizedStatus, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Webhook delivery status is not allowlisted.");

            await ProcessingGate.WaitAsync(cancellationToken);
            try
            {
                if (await dbContext.WebhookReceipts.AnyAsync(
                        x => x.Provider == normalizedProvider && x.DeliveryId == deliveryId, cancellationToken))
                    return new WebhookResult(true, "already-accepted");

                dbContext.WebhookReceipts.Add(new WebhookReceipt
                {
                    Provider = normalizedProvider,
                    DeliveryId = deliveryId,
                    ProviderTimestampUtc = providerTime,
                    PayloadDigest = Convert.ToHexString(SHA256.HashData(body))
                });
                var delivery = await dbContext.NotificationDeliveries.SingleOrDefaultAsync(
                    x => x.Provider == normalizedProvider && x.ProviderMessageId == messageId,
                    cancellationToken);
                if (delivery is null)
                {
                    delivery = new NotificationDelivery { Provider = normalizedProvider, ProviderMessageId = messageId };
                    dbContext.NotificationDeliveries.Add(delivery);
                }
                delivery.Status = normalizedStatus;
                delivery.ProviderConfirmedAtUtc = clock.UtcNow;
                dbContext.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "notification.delivery.updated",
                    PayloadJson = JsonSerializer.Serialize(new { provider = normalizedProvider, messageId, status = normalizedStatus })
                });
                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    dbContext.ChangeTracker.Clear();
                    if (await dbContext.WebhookReceipts.AsNoTracking().AnyAsync(
                            x => x.Provider == normalizedProvider && x.DeliveryId == deliveryId, cancellationToken))
                        return new WebhookResult(true, "already-accepted");
                    throw;
                }
                return new WebhookResult(false, "accepted");
            }
            finally
            {
                ProcessingGate.Release();
            }
        }
    }
}

public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox dispatch cycle failed.");
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GoldenHourDbContext>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var messages = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAtUtc == null && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= clock.UtcNow))
            .OrderBy(x => x.CreatedAtUtc).Take(20).ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                await eventBus.PublishAsync(message.EventType, message.PayloadJson, cancellationToken);
                message.ProcessedAtUtc = clock.UtcNow;
            }
            catch (Exception exception)
            {
                message.Attempts++;
                message.NextAttemptAtUtc = clock.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, message.Attempts)));
                logger.LogWarning(exception, "Outbox message {OutboxMessageId} could not be dispatched.", message.Id);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
