using System.Text.Json;

namespace wa_api.Features.Templates;

/// <summary>Pulls the most user-friendly message out of a Meta Graph API error envelope.</summary>
internal static class MetaError
{
    public static string? Extract(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("error", out var err)) return null;
            return err.TryGetProperty("error_user_msg", out var u) ? u.GetString()
                 : err.TryGetProperty("message", out var m) ? m.GetString()
                 : null;
        }
        catch
        {
            return null;
        }
    }
}
