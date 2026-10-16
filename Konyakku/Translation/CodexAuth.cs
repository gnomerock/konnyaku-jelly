using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Konyakku.Translation;

/// <summary>
/// Reads (and refreshes) the ChatGPT login that the official Codex CLI stores in ~/.codex/auth.json.
/// Refreshed tokens are written back so the Codex CLI keeps working (refresh tokens rotate).
/// </summary>
internal sealed class CodexAuth(HttpClient http)
{
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string TokenUrl = "https://auth.openai.com/oauth/token";

    private readonly SemaphoreSlim gate = new(1, 1);

    public static string AuthPath =>
        Path.Combine(
            Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
            "auth.json");

    public static bool Exists => File.Exists(AuthPath);

    public async Task<(string AccessToken, string? AccountId)> GetAsync(bool forceRefresh, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!Exists)
                throw new InvalidOperationException("ChatGPT login not found. Install Codex CLI and run `codex login`.");

            var root = JsonNode.Parse(await File.ReadAllTextAsync(AuthPath, ct))!.AsObject();
            var tokens = root["tokens"] as JsonObject
                ?? throw new InvalidOperationException("Codex is logged in with an API key, not ChatGPT. Run `codex login` and choose ChatGPT.");

            var access = (string?)tokens["access_token"];
            if (forceRefresh || access is null || IsExpiring(access))
            {
                var refresh = (string?)tokens["refresh_token"]
                    ?? throw new InvalidOperationException("No refresh token. Run `codex login` again.");

                using var resp = await http.PostAsJsonAsync(TokenUrl, new
                {
                    client_id = ClientId,
                    grant_type = "refresh_token",
                    refresh_token = refresh,
                    scope = "openid profile email",
                }, ct);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"ChatGPT token refresh failed ({(int)resp.StatusCode}). Run `codex login` again.");

                var r = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;
                access = (string)r["access_token"]!;
                tokens["access_token"] = access;
                if ((string?)r["refresh_token"] is { } rt) tokens["refresh_token"] = rt;
                if ((string?)r["id_token"] is { } it) tokens["id_token"] = it;
                root["last_refresh"] = DateTime.UtcNow.ToString("o");

                var tmp = AuthPath + ".konyakku.tmp";
                await File.WriteAllTextAsync(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
                File.Move(tmp, AuthPath, true);
            }

            var accountId = (string?)tokens["account_id"] ?? AccountIdFromJwt(access);
            return (access, accountId);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsExpiring(string jwt)
    {
        var payload = DecodeJwt(jwt);
        if (payload?["exp"] is not JsonValue exp) return false;
        return DateTimeOffset.FromUnixTimeSeconds(exp.GetValue<long>()) < DateTimeOffset.UtcNow.AddMinutes(2);
    }

    private static string? AccountIdFromJwt(string jwt) =>
        (string?)DecodeJwt(jwt)?["https://api.openai.com/auth"]?["chatgpt_account_id"];

    private static JsonNode? DecodeJwt(string jwt)
    {
        try
        {
            var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
            return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part)));
        }
        catch
        {
            return null;
        }
    }
}
