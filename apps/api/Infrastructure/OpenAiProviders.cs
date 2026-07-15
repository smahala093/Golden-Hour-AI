using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoldenHour.Api.Application;
using Microsoft.Extensions.Options;

namespace GoldenHour.Api.Infrastructure;

public sealed class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string Model { get; set; } = "gpt-4.1-mini";
    public string SpeechModel { get; set; } = "gpt-4o-mini-transcribe";
    public string TextToSpeechModel { get; set; } = "gpt-4o-mini-tts";
    public string TextToSpeechVoice { get; set; } = "alloy";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class AiProviderException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class OpenAiResponsesProvider(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options,
    PromptTemplateStore promptStore,
    IncidentDataMinimizer dataMinimizer,
    ILogger<OpenAiResponsesProvider> logger) : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<IncidentExtraction> ExtractIncidentAsync(string originalText, string? selectedLanguage, CancellationToken cancellationToken)
    {
        var prompt = promptStore.Read("incident-extraction.v1.txt");
        var schema = JsonDocument.Parse(promptStore.Read("incident-extraction.schema.v1.json")).RootElement.Clone();
        var minimizedText = dataMinimizer.Minimize(originalText);
        var userContent = $"Selected language (may be blank): {selectedLanguage ?? ""}\n<incident_text>\n{minimizedText}\n</incident_text>";
        var json = await SendStructuredAsync("incident_extraction", prompt, userContent, "incident_extraction_v1", schema, 1400, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<IncidentExtraction>(json, JsonOptions)
                ?? throw new AiProviderException("empty_output", "The AI response was empty.");
        }
        catch (JsonException exception)
        {
            _ = exception;
            throw new AiProviderException("malformed_output", "The AI response did not match the incident contract.");
        }
    }

    public async Task<string> TranslateApprovedTextAsync(string text, string targetLanguage, CancellationToken cancellationToken)
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{"translatedText":{"type":"string"}},"required":["translatedText"],"additionalProperties":false}
            """).RootElement.Clone();
        var userContent = $"Target language: {targetLanguage}\n<approved_text>\n{text}\n</approved_text>";
        var json = await SendStructuredAsync(
            "translation",
            promptStore.Read("translation.v1.txt"),
            userContent,
            "approved_translation_v1",
            schema,
            1000,
            cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("translatedText").GetString()
            ?? throw new AiProviderException("empty_output", "The translated text was empty.");
    }

    public async Task<IReadOnlyList<string>> SuggestCoordinationTaskCodesAsync(IncidentExtraction incident, CancellationToken cancellationToken)
    {
        var schema = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{"taskCodes":{"type":"array","items":{"type":"string","enum":["stay-with-patient","call-emergency-services","bring-medical-records","bring-identification","unlock-entry","guide-responder","contact-hospital","care-for-dependants"]}}},
              "required":["taskCodes"],"additionalProperties":false
            }
            """).RootElement.Clone();
        var incidentJson = JsonSerializer.Serialize(incident, JsonOptions);
        var json = await SendStructuredAsync(
            "coordination_task_suggestion",
            promptStore.Read("coordination-task-suggestion.v1.txt"),
            $"<incident_facts>\n{incidentJson}\n</incident_facts>",
            "coordination_tasks_v1",
            schema,
            400,
            cancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("taskCodes").EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => x is not null)
            .Cast<string>()
            .ToArray();
    }

    private async Task<string> SendStructuredAsync(
        string operation,
        string systemPrompt,
        string userContent,
        string schemaName,
        JsonElement schema,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            throw new AiProviderException("not_configured", "OpenAI is not configured.");
        }

        var payload = new
        {
            model = configured.Model,
            input = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = schemaName,
                    strict = true,
                    schema
                }
            },
            max_output_tokens = maxOutputTokens,
            store = false
        };

        var started = Stopwatch.GetTimestamp();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configured.ApiKey);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (IsTransient(response.StatusCode) && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI {Operation} failed with status {StatusCode} after {LatencyMs} ms.", operation, (int)response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                throw new AiProviderException(
                    response.StatusCode == HttpStatusCode.TooManyRequests ? "rate_limited" : "provider_error",
                    "The AI provider did not complete the request.");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.TryGetProperty("status", out var status) && status.GetString() == "incomplete")
            {
                throw new AiProviderException("incomplete_output", "The AI provider returned an incomplete response.");
            }

            if (!root.TryGetProperty("output", out var output))
            {
                throw new AiProviderException("malformed_output", "The AI provider response had no output.");
            }

            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "message"
                    || !item.TryGetProperty("content", out var content))
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    var type = part.GetProperty("type").GetString();
                    if (type == "refusal")
                    {
                        throw new AiProviderException("refusal", "The AI provider refused the extraction request.");
                    }

                    if (type == "output_text" && part.TryGetProperty("text", out var text))
                    {
                        logger.LogInformation("OpenAI {Operation} completed with model {Model} in {LatencyMs} ms.", operation, configured.Model, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                        return text.GetString() ?? throw new AiProviderException("empty_output", "The AI provider returned empty output.");
                    }
                }
            }

            throw new AiProviderException("empty_output", "The AI provider returned no usable text output.");
        }

        throw new AiProviderException("provider_error", "The AI provider did not complete the request.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}

public sealed class OpenAiSpeechToTextProvider(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options) : ISpeechToTextProvider
{
    public async Task<SpeechTranscription> TranscribeAsync(Stream audio, string contentType, string? languageHint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            throw new AiProviderException("not_configured", "OpenAI speech transcription is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(options.Value.SpeechModel), "model");
        form.Add(new StringContent("verbose_json"), "response_format");
        if (!string.IsNullOrWhiteSpace(languageHint))
        {
            form.Add(new StringContent(languageHint), "language");
        }

        var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(audioContent, "file", "audio-upload");
        request.Content = form;

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderException("speech_provider_error", "Speech transcription was unavailable.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var transcript = document.RootElement.GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new AiProviderException("empty_audio", "No speech was recognized.");
        }

        var language = document.RootElement.TryGetProperty("language", out var detected)
            ? detected.GetString() ?? languageHint ?? "und"
            : languageHint ?? "und";
        return new SpeechTranscription(transcript, language, language == "und" ? 0.5m : 0.9m);
    }
}

public sealed class OpenAiTextToSpeechProvider(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options) : ITextToSpeechProvider
{
    private const int MaximumInputCharacters = 4096;
    private const int MaximumAudioBytes = 5 * 1024 * 1024;

    public async Task<Stream> SynthesizeAsync(string approvedText, string language, CancellationToken cancellationToken)
    {
        _ = language;
        if (string.IsNullOrWhiteSpace(approvedText) || approvedText.Length > MaximumInputCharacters)
            throw new InvalidDataException("Approved speech text must contain 1 to 4096 characters.");
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
            throw new AiProviderException("not_configured", "OpenAI text-to-speech is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "audio/speech")
        {
            Content = JsonContent.Create(new
            {
                model = options.Value.TextToSpeechModel,
                input = approvedText,
                voice = options.Value.TextToSpeechVoice,
                response_format = "mp3"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AiProviderException("speech_provider_error", "Text-to-speech was unavailable.");
        if (response.Content.Headers.ContentLength is > MaximumAudioBytes)
            throw new AiProviderException("speech_output_too_large", "Text-to-speech output exceeded the safe limit.");

        await using var providerStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var output = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await providerStream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumAudioBytes)
            {
                await output.DisposeAsync();
                throw new AiProviderException("speech_output_too_large", "Text-to-speech output exceeded the safe limit.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        output.Position = 0;
        return output;
    }
}
