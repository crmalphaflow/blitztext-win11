using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BlitztextWindows;

public sealed class OpenAIClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    public async Task<string> TranscribeAsync(string apiKey, string audioPath, string language, CancellationToken cancellationToken = default)
    {
        try
        {
            return await TranscribeWithModelAsync(
                apiKey,
                audioPath,
                language,
                OpenAIModels.Transcription,
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ShouldTryTranscriptionFallback(ex.Message))
        {
            return await TranscribeWithModelAsync(
                apiKey,
                audioPath,
                language,
                OpenAIModels.TranscriptionFallback,
                cancellationToken);
        }
    }

    private static async Task<string> TranscribeWithModelAsync(
        string apiKey,
        string audioPath,
        string language,
        string model,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(audioPath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", "audio.wav");
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent("text"), "response_format");
        form.Add(new StringContent(language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = form;

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(OpenAIErrorParser.MessageOrStatus(body, (int)response.StatusCode));
        }

        var text = body.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Transkription fehlgeschlagen.");
        }

        return text;
    }

    private static bool ShouldTryTranscriptionFallback(string message)
    {
        return message.Contains(OpenAIModels.Transcription, StringComparison.OrdinalIgnoreCase) ||
               message.Contains("model", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("access", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> RewriteAsync(
        string apiKey,
        string text,
        string systemPrompt,
        string model,
        double temperature,
        CancellationToken cancellationToken = default)
    {
        var payload = new ChatRequest(
            model,
            [new ChatMessage("system", systemPrompt), new ChatMessage("user", text)],
            temperature);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(OpenAIErrorParser.MessageOrStatus(body, (int)response.StatusCode));
        }

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
        var content = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Keine Antwort erhalten.");
        }

        return content;
    }

    private sealed record ChatRequest(string Model, ChatMessage[] Messages, double Temperature);
    private sealed record ChatMessage(string Role, string Content);

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public Choice[]? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ResponseMessage? Message { get; set; }
    }

    private sealed class ResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}

internal static class OpenAIErrorParser
{
    public static string MessageOrStatus(string body, int statusCode)
    {
        try
        {
            var error = System.Text.Json.JsonSerializer.Deserialize<OpenAIErrorResponse>(body);
            if (!string.IsNullOrWhiteSpace(error?.Error?.Message))
            {
                return error.Error.Message;
            }
        }
        catch
        {
        }

        return statusCode switch
        {
            401 => "API Key ungueltig oder abgelaufen.",
            403 => "Dein API Key hat keinen Zugriff auf dieses Modell.",
            408 => "Die Anfrage hat zu lange gedauert. Bitte nochmal versuchen.",
            429 => "OpenAI-Limit erreicht. Bitte kurz warten und erneut versuchen.",
            >= 500 => "OpenAI ist gerade nicht erreichbar. Bitte spaeter nochmal versuchen.",
            _ => $"OpenAI-Fehler: Status {statusCode}"
        };
    }

    private sealed class OpenAIErrorResponse
    {
        [JsonPropertyName("error")]
        public OpenAIError? Error { get; set; }
    }

    private sealed class OpenAIError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
