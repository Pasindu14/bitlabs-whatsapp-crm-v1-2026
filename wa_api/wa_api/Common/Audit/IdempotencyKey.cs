namespace wa_api.Common.Audit;

/// <summary>
/// Stores the response of a previously-processed idempotent request so retries
/// with the same key return the original result instead of re-executing.
/// </summary>
public class IdempotencyKey
{
    public string Key { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}
