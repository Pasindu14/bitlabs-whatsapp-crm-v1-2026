namespace wa_api.Features.Auth;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Opaque random token returned to the client. Unique.</summary>
    public string Token { get; set; } = null!;

    public Guid UserId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool IsRevoked { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
