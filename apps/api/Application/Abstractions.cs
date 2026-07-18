using GoldenHour.Api.Domain;

namespace GoldenHour.Api.Application;

public interface IClock
{
    DateTime UtcNow { get; }
}

public interface IAiProvider
{
    Task<AiProviderResult<IncidentExtraction>> ExtractIncidentAsync(string originalText, string? selectedLanguage, CancellationToken cancellationToken);
    Task<string> TranslateApprovedTextAsync(string text, string targetLanguage, CancellationToken cancellationToken);
    Task<AiProviderResult<IReadOnlyList<string>>> SuggestCoordinationTaskCodesAsync(IncidentExtraction incident, CancellationToken cancellationToken);
}

public sealed record AiCallMetadata(
    string OperationType,
    string Provider,
    string Model,
    long LatencyMilliseconds,
    int? InputTokens,
    int? OutputTokens);

public sealed record AiProviderResult<T>(T Value, AiCallMetadata Metadata);

public sealed class AiProviderException(
    string code,
    string message,
    AiCallMetadata? metadata = null) : Exception(message)
{
    public string Code { get; } = code;
    public AiCallMetadata? Metadata { get; } = metadata;
}

public interface ISpeechToTextProvider
{
    Task<SpeechTranscription> TranscribeAsync(Stream audio, string contentType, string? languageHint, CancellationToken cancellationToken);
}

public interface ITextToSpeechProvider
{
    Task<Stream> SynthesizeAsync(string approvedText, string language, CancellationToken cancellationToken);
}

public interface INotificationProvider
{
    Task<ProviderDelivery> SendAsync(string destination, string templateCode, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken);
}

public interface ISmsProvider : INotificationProvider;
public interface IEmailProvider : INotificationProvider;

public interface IMapProvider
{
    Task<string?> ResolveDescriptionAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken);
}

public interface IEventBus
{
    Task PublishAsync(string eventType, string payloadJson, CancellationToken cancellationToken);
}

public interface IFileStorage
{
    Task<string> PutAsync(Stream content, string safeContentType, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
}

public sealed record SpeechTranscription(string OriginalTranscript, string DetectedLanguage, decimal LanguageConfidence, decimal? DurationSeconds);
public sealed record ProviderDelivery(string ProviderMessageId, string Status, bool ProviderConfirmed);

public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}

public sealed class NullEventBus : IEventBus
{
    public Task PublishAsync(string eventType, string payloadJson, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class MockNotificationProvider : ISmsProvider, IEmailProvider, INotificationProvider
{
    public Task<ProviderDelivery> SendAsync(string destination, string templateCode, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderDelivery($"mock-{Guid.NewGuid():N}", "accepted-by-mock", false));
}

public sealed class MockMapProvider : IMapProvider
{
    public Task<string?> ResolveDescriptionAsync(decimal latitude, decimal longitude, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("Location shared by user");
}

public sealed class UnavailableFileStorage : IFileStorage
{
    public Task<string> PutAsync(Stream content, string safeContentType, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("File storage is not configured.");

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("File storage is not configured.");
}

public sealed class MockTextToSpeechProvider : ITextToSpeechProvider
{
    public Task<Stream> SynthesizeAsync(string approvedText, string language, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Read-aloud audio is unavailable in deterministic mock mode.");
}
