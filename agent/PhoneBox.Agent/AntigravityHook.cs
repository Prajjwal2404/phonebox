using System.Text.Json;
using System.Text.Json.Nodes;

namespace PhoneBox.Agent;

public static class AntigravityHook
{
    public const string PromptEndpoint = "/api/hooks/antigravity/prompt";
    public const string StopEndpoint = "/api/hooks/antigravity/stop";
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly string SettingsPath = Path.Combine(Home, ".gemini", "config", "hooks.json");

    /// <summary>
    /// Handles Antigravity interactive multi-choice question notifications.
    /// Emits a non-blocking alert to PhoneBox and immediately allows execution so PC prompts without waiting.
    /// </summary>
    public static object HandlePrompt(JsonElement input, ConfigStore cfg, PushStore push)
    {
        var c = cfg.Get().Antigravity;
        if (c.PhoneApprovals && (!c.OnlyWhenAway || Idle.Seconds() >= 60) && ApprovalStore.PhoneConnected)
        {
            var toolCall = input.TryGetProperty("toolCall", out var tc) && tc.ValueKind == JsonValueKind.Object ? tc : default;
            var args = toolCall.TryGetProperty("args", out var aEl) && aEl.ValueKind == JsonValueKind.Object ? aEl : default;
            var cwd = Str(input, "cwd") ?? "";
            var project = Path.GetFileName(cwd.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(project)) project = "Antigravity";

            push.Emit("ai-notice", JsonSerializer.SerializeToElement(new
            {
                source = "Antigravity",
                title = "Antigravity",
                text = "Choice needed",
                project,
                kind = "choice",
                sound = c.PlaySound,
                timeoutSec = c.DisableFallbackTimer ? 0 : Math.Clamp(c.TimeoutSec, 5, 3600)
            }));
        }

        return new { decision = "allow" };
    }

    /// <summary>
    /// Handles Antigravity task completion (Stop lifecycle hook).
    /// Emits a small finished notification to the phone.
    /// </summary>
    public static object HandleStop(JsonElement input, ConfigStore cfg, PushStore push)
    {
        var c = cfg.Get().Antigravity;
        var reason = Str(input, "terminationReason");
        if (reason is "NO_TOOL_CALL" or null or "" && c.PhoneApprovals && (!c.OnlyWhenAway || Idle.Seconds() >= 60) && ApprovalStore.PhoneConnected)
        {
            var cwd = Str(input, "cwd") ?? "";
            var project = Path.GetFileName(cwd.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(project)) project = "Antigravity";

            push.Emit("ai-notice", JsonSerializer.SerializeToElement(new
            {
                source = "Antigravity",
                title = "Antigravity",
                text = "Task finished",
                project,
                kind = "done",
                sound = c.PlaySound,
                timeoutSec = c.DisableFallbackTimer ? 0 : Math.Clamp(c.TimeoutSec, 5, 3600)
            }));
        }

        return new { };
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string HookCommand(int port, string endpoint) =>
        $"{(OperatingSystem.IsWindows() ? "curl.exe" : "curl")} -s --max-time 15 -X POST http://127.0.0.1:{port}{endpoint} -H \"Content-Type: application/json\" --data-binary \"@-\"";

    private static bool IsOurs(JsonNode? hook) =>
        hook?["command"]?.GetValue<string>() is { } cmd && (cmd.Contains(PromptEndpoint) || cmd.Contains(StopEndpoint) || cmd.Contains("/api/hooks/antigravity/"));

    public static bool Installed()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            var root = JsonNode.Parse(File.ReadAllText(SettingsPath), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return root?["phonebox"]?["PreToolUse"]?.AsArray().Any(g => g?["hooks"]?.AsArray().Any(IsOurs) == true) == true
                || root?["phonebox"]?["Stop"]?.AsArray().Any(IsOurs) == true;
        }
        catch { return false; }
    }

    public static string? Configure(bool install, int port, int timeoutSec)
    {
        try
        {
            JsonObject root;
            if (File.Exists(SettingsPath))
            {
                var text = File.ReadAllText(SettingsPath);
                root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                       ?? throw new InvalidDataException("hooks.json is not a JSON object");
                File.WriteAllText(SettingsPath + ".phonebox.bak", text);
            }
            else root = new JsonObject();

            var phonebox = root["phonebox"] as JsonObject ?? new JsonObject();

            // Clean existing PreToolUse hooks
            if (phonebox["PreToolUse"] is JsonArray existingPre)
            {
                foreach (var g in existingPre.ToList())
                {
                    if (g?["hooks"] is not JsonArray hs) continue;
                    foreach (var h in hs.Where(IsOurs).ToList()) hs.Remove(h);
                    if (hs.Count == 0) existingPre.Remove(g);
                }
                if (existingPre.Count == 0) phonebox.Remove("PreToolUse");
            }

            // Clean existing Stop hooks
            if (phonebox["Stop"] is JsonArray existingStop)
            {
                foreach (var h in existingStop.Where(IsOurs).ToList()) existingStop.Remove(h);
                if (existingStop.Count == 0) phonebox.Remove("Stop");
            }

            if (install)
            {
                var preGroups = phonebox["PreToolUse"] as JsonArray ?? new JsonArray();
                preGroups.Add(new JsonObject
                {
                    ["matcher"] = "ask_question|AskUserQuestion",
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = HookCommand(port, PromptEndpoint),
                        ["timeout"] = 20,
                    }),
                });
                phonebox["PreToolUse"] = preGroups;

                var stopList = phonebox["Stop"] as JsonArray ?? new JsonArray();
                stopList.Add(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = HookCommand(port, StopEndpoint),
                    ["timeout"] = 20,
                });
                phonebox["Stop"] = stopList;
            }

            if (phonebox.Count > 0) root["phonebox"] = phonebox; else root.Remove("phonebox");

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            File.Move(tmp, SettingsPath, overwrite: true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
