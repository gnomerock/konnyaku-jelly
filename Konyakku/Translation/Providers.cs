using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Konyakku.Translation;

internal interface ITranslationProvider
{
    Task<string> CompleteAsync(string instructions, string input, CancellationToken ct);
}

/// <summary>Uses the ChatGPT subscription through the same backend the Codex CLI talks to.</summary>
internal sealed class ChatGptSubscriptionProvider(HttpClient http, CodexAuth auth, Configuration config) : ITranslationProvider
{
    private const string Endpoint = "https://chatgpt.com/backend-api/codex/responses";

    public async Task<string> CompleteAsync(string instructions, string input, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (token, accountId) = await auth.GetAsync(forceRefresh: attempt > 0, ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (accountId != null) req.Headers.Add("chatgpt-account-id", accountId);
            req.Headers.Add("OpenAI-Beta", "responses=experimental");
            req.Headers.Add("originator", "codex_cli_rs");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = JsonContent.Create(new
            {
                model = config.SubscriptionModel,
                instructions,
                input = new[]
                {
                    new { type = "message", role = "user", content = new[] { new { type = "input_text", text = input } } },
                },
                tools = Array.Empty<object>(),
                tool_choice = "auto",
                parallel_tool_calls = false,
                reasoning = new { effort = config.ReasoningEffort },
                store = false,
                stream = true,
            });

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"ChatGPT {(int)resp.StatusCode}: {Truncate(await resp.Content.ReadAsStringAsync(ct))}");

            return await ReadSseAsync(await resp.Content.ReadAsStreamAsync(ct), ct);
        }
    }

    private static async Task<string> ReadSseAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var sb = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data is "" or "[DONE]") continue;

            var ev = JsonNode.Parse(data);
            switch ((string?)ev?["type"])
            {
                case "response.output_text.delta":
                    sb.Append((string?)ev["delta"]);
                    break;
                case "response.output_text.done" when (string?)ev["text"] is { } full:
                    sb.Clear().Append(full);
                    break;
                case "response.completed":
                    return sb.ToString();
                case "response.failed":
                case "error":
                    throw new InvalidOperationException(
                        (string?)ev["response"]?["error"]?["message"] ?? (string?)ev["message"] ?? data);
            }
        }
        return sb.ToString();
    }

    internal static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
}

/// <summary>Standard OpenAI API (billed per token, needs an API key).</summary>
internal sealed class OpenAiApiProvider(HttpClient http, Configuration config) : ITranslationProvider
{
    public async Task<string> CompleteAsync(string instructions, string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not set.");

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        req.Content = JsonContent.Create(new
        {
            model = config.ApiModel,
            messages = new[]
            {
                new { role = "system", content = instructions },
                new { role = "user", content = input },
            },
        });

        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI {(int)resp.StatusCode}: {ChatGptSubscriptionProvider.Truncate(body)}");

        return (string?)JsonNode.Parse(body)?["choices"]?[0]?["message"]?["content"] ?? string.Empty;
    }
}
