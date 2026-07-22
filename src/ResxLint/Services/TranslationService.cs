using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResxLint.Models;

namespace ResxLint.Services;

class TranslationService
{
    public static string[] SupportedProviders => ["gemini", "claude", "openai", "deepseek"];

    public static async Task<AiTranslateResult> TranslateAsync(AiTranslateRequest req)
    {
        try
        {
            return req.Provider.ToLowerInvariant() switch
            {
                "gemini" => await TranslateGemini(req),
                "claude" => await TranslateClaude(req),
                "openai" => await TranslateOpenAi(req),
                "deepseek" => await TranslateDeepSeek(req),
                _ => new(false, null, $"Unsupported provider: {req.Provider}")
            };
        }
        catch (Exception ex)
        {
            return new(false, null, ex.Message);
        }
    }

    static string BuildPrompt(string[] texts, string sourceLang, string targetLang)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Translate the following {sourceLang} strings to {targetLang}. " +
            "Preserve all placeholders like {0}, {1}, {name}, etc. exactly as-is. " +
            "Keep HTML tags intact. Return ONLY a JSON array of translated strings in the same order.");
        sb.AppendLine("Input:");
        sb.AppendLine(JsonSerializer.Serialize(texts));
        return sb.ToString();
    }

    static async Task<AiTranslateResult> TranslateGemini(AiTranslateRequest req)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={req.ApiKey}";
        var prompt = BuildPrompt(req.Texts, req.SourceLang, req.TargetLang);

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        using var http = new HttpClient();
        var response = await http.PostAsync(url,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "[]";

        text = text.Replace("```json", "").Replace("```", "").Trim();
        var translations = JsonSerializer.Deserialize<string[]>(text);

        return new(true, translations, null);
    }

    static async Task<AiTranslateResult> TranslateClaude(AiTranslateRequest req)
    {
        var prompt = BuildPrompt(req.Texts, req.SourceLang, req.TargetLang);

        var body = new
        {
            model = "claude-sonnet-4-20250514",
            max_tokens = 8192,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("x-api-key", req.ApiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var response = await http.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "[]";

        text = text.Replace("```json", "").Replace("```", "").Trim();
        var translations = JsonSerializer.Deserialize<string[]>(text);

        return new(true, translations, null);
    }

    static async Task<AiTranslateResult> TranslateOpenAi(AiTranslateRequest req)
    {
        var prompt = BuildPrompt(req.Texts, req.SourceLang, req.TargetLang);

        var body = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.3
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {req.ApiKey}");

        var response = await http.PostAsync("https://api.openai.com/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "[]";

        text = text.Replace("```json", "").Replace("```", "").Trim();
        var translations = JsonSerializer.Deserialize<string[]>(text);

        return new(true, translations, null);
    }

    static async Task<AiTranslateResult> TranslateDeepSeek(AiTranslateRequest req)
    {
        var prompt = BuildPrompt(req.Texts, req.SourceLang, req.TargetLang);

        var body = new
        {
            model = "deepseek-chat",
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.3
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {req.ApiKey}");

        var response = await http.PostAsync("https://api.deepseek.com/chat/completions",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "[]";

        text = text.Replace("```json", "").Replace("```", "").Trim();
        var translations = JsonSerializer.Deserialize<string[]>(text);

        return new(true, translations, null);
    }
}
