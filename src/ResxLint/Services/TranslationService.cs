using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResxLint.Models;

namespace ResxLint.Services;

class TranslationService
{
    const string RemoteProvidersUrl = "https://raw.githubusercontent.com/CW-Software-Apps/resx-lint/master/providers.json";
    static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    static AiProviderInfo[]? _cachedProviders;
    static DateTime _lastFetch = DateTime.MinValue;
    static readonly object _lock = new();

    static readonly AiProviderInfo[] _embeddedProviders =
    [
        new("openai",     "OpenAI",            "https://api.openai.com/v1/chat/completions",              "gpt-4.1-mini",       "https://platform.openai.com/api-keys", true),
        new("gemini",     "Google Gemini",     "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}", "gemini-2.5-flash", "https://aistudio.google.com/apikey", true),
        new("claude",     "Anthropic Claude",  "https://api.anthropic.com/v1/messages",                    "claude-sonnet-4-6-20250217", "https://console.anthropic.com/", true),
        new("deepseek",   "DeepSeek",          "https://api.deepseek.com/chat/completions",                "deepseek-chat",     "https://platform.deepseek.com/api_keys"),
        new("openrouter", "OpenRouter",        "https://openrouter.ai/api/v1/chat/completions",            "openai/gpt-4.1-mini", "https://openrouter.ai/keys"),
        new("groq",       "Groq",              "https://api.groq.com/openai/v1/chat/completions",          "llama-3.3-70b-versatile", "https://console.groq.com/keys"),
        new("together",   "Together AI",       "https://api.together.xyz/v1/chat/completions",             "meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo", "https://api.together.ai/settings/api-keys"),
        new("mistral",    "Mistral AI",        "https://api.mistral.ai/v1/chat/completions",               "mistral-small-latest", "https://console.mistral.ai/api-keys"),
        new("perplexity", "Perplexity",        "https://api.perplexity.ai/chat/completions",               "sonar-pro",         "https://www.perplexity.com/settings/api"),
        new("xai",        "xAI Grok",          "https://api.x.ai/v1/chat/completions",                     "grok-2-latest",     "https://console.x.ai/"),
        new("custom",     "Custom (OpenAI-compatible)", "", "", null, false)
    ];

    public static AiProviderInfo[] GetSupportedProviders()
    {
        if (_cachedProviders != null && DateTime.UtcNow - _lastFetch < CacheDuration)
            return _cachedProviders;

        lock (_lock)
        {
            if (_cachedProviders != null && DateTime.UtcNow - _lastFetch < CacheDuration)
                return _cachedProviders;

            _ = TryFetchRemoteProviders();
        }

        return _cachedProviders ?? _embeddedProviders;
    }

    static async Task TryFetchRemoteProviders()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await http.GetStringAsync(RemoteProvidersUrl);
            var remote = JsonSerializer.Deserialize<AiProviderInfo[]>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (remote != null && remote.Length > 0)
            {
                _cachedProviders = remote;
                _lastFetch = DateTime.UtcNow;
            }
        }
        catch
        {
            _lastFetch = DateTime.UtcNow;
        }
    }

    public static async Task<AiTranslateResult> TranslateAsync(AiTranslateRequest req)
    {
        try
        {
            return req.Provider.ToLowerInvariant() switch
            {
                "gemini" => await TranslateGemini(req),
                "claude" => await TranslateClaude(req),
                "custom" => await TranslateOpenAiCompatible(req),
                _ => await TranslateOpenAiCompatible(req)
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

    static string CleanJsonResponse(string raw)
    {
        raw = raw.Replace("```json", "").Replace("```", "").Replace("```JSON", "").Trim();
        return raw;
    }

    static AiProviderInfo? GetProvider(string id)
    {
        var providers = GetSupportedProviders();
        return providers.FirstOrDefault(p => p.Id == id);
    }

    static async Task<AiTranslateResult> TranslateGemini(AiTranslateRequest req)
    {
        var provider = GetProvider("gemini");
        if (provider == null) return new(false, null, "Gemini provider not configured");

        var url = string.Format(provider.Endpoint, provider.Model, req.ApiKey);
        var prompt = BuildPrompt(req.Texts, req.SourceLang, req.TargetLang);

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
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

        var translations = JsonSerializer.Deserialize<string[]>(CleanJsonResponse(text));
        return new(true, translations, null);
    }

    static async Task<AiTranslateResult> TranslateClaude(AiTranslateRequest req)
    {
        var provider = GetProvider("claude");
        if (provider == null) return new(false, null, "Claude provider not configured");

        var prompt = BuildPrompt(req.Texts, req.SourceLang, req.TargetLang);

        var body = new
        {
            model = provider.Model,
            max_tokens = 8192,
            messages = new[] { new { role = "user", content = prompt } }
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("x-api-key", req.ApiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var response = await http.PostAsync(provider.Endpoint,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "[]";

        var translations = JsonSerializer.Deserialize<string[]>(CleanJsonResponse(text));
        return new(true, translations, null);
    }

    static async Task<AiTranslateResult> TranslateOpenAiCompatible(AiTranslateRequest req)
    {
        var isCustom = req.Provider.ToLowerInvariant() == "custom";
        var provider = isCustom ? null : GetProvider(req.Provider.ToLowerInvariant());
        var endpoint = isCustom ? req.CustomEndpoint : provider?.Endpoint;
        var model = isCustom ? req.CustomModel : provider?.Model;

        if (string.IsNullOrWhiteSpace(endpoint))
            return new(false, null, "API endpoint is required. Select a provider or enter a custom endpoint.");
        if (string.IsNullOrWhiteSpace(model))
            return new(false, null, "Model name is required. Select a provider or enter a custom model name.");

        var prompt = BuildPrompt(req.Texts, req.SourceLang, req.TargetLang);

        var body = new
        {
            model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.3
        };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {req.ApiKey}");

        var response = await http.PostAsync(endpoint,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "[]";

        var translations = JsonSerializer.Deserialize<string[]>(CleanJsonResponse(text));
        return new(true, translations, null);
    }
}
