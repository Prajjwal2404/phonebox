using System.Globalization;
using System.Text.Json;

namespace PhoneBox.Agent;

public static class WidgetSoundEdit
{
    public sealed record Result(int Status, string? Error = null, string? Version = null);

    public static Result Apply(ConfigStore cfg, string pageId, string widgetId, bool sound, long? expectedVersion = null)
    {
        var version = expectedVersion ?? cfg.Version;
        var json = JsonSerializer.Serialize(cfg.Get(), ConfigStore.WriteOpts);
        var copy = JsonSerializer.Deserialize<AgentConfig>(json, ConfigStore.WriteOpts);
        var page = copy?.Pages?.FirstOrDefault(p => p.Id == pageId);
        var widget = page?.Widgets?.FirstOrDefault(w => w.Id == widgetId);

        if (widget is null && widgetId.Contains(':'))
        {
            var parts = widgetId.Split(':', 2);
            var parent = page?.Widgets?.FirstOrDefault(w => w.Id == parts[0]);
            if (parent?.Options != null && parent.Options.TryGetValue("widgets", out var raw))
            {
                try
                {
                    var cards = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(raw.GetRawText());
                    var hit = cards?.FirstOrDefault(c => c.TryGetValue("id", out var id) && id.GetString() == parts[1]);
                    if (hit is not null)
                    {
                        if (hit.TryGetValue("type", out var type) && type.GetString() != "sessions") return new(400, "option not allowed");
                        var opts = hit.TryGetValue("options", out var o) && o.ValueKind == JsonValueKind.Object
                            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(o.GetRawText()) ?? [] : [];
                        opts["sound"] = sound;
                        hit["options"] = JsonSerializer.SerializeToElement(opts);
                        parent.Set("widgets", cards);
                        var e = cfg.Save(JsonSerializer.Serialize(copy, ConfigStore.WriteOpts), version, out var sVer);
                        if (e == ConfigStore.Conflict) return new(409, e);
                        return e is null ? new(200, null, sVer.ToString(CultureInfo.InvariantCulture)) : new(400, e);
                    }
                }
                catch (JsonException) { }
            }
        }

        if (widget is null) return new(404, "widget not found");
        if (widget.Type != "sessions") return new(400, "option not allowed");

        widget.Set("sound", sound);
        var err = cfg.Save(JsonSerializer.Serialize(copy, ConfigStore.WriteOpts), version, out var savedVersion);
        if (err == ConfigStore.Conflict) return new(409, err);
        return err is null ? new(200, null, savedVersion.ToString(CultureInfo.InvariantCulture)) : new(400, err);
    }
}

