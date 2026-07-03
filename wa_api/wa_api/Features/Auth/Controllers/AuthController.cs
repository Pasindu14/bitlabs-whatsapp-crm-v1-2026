using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using wa_api.Common.Errors;
using wa_api.Features.Auth.Dtos;

namespace wa_api.Features.Auth.Controllers;

/// <summary>
/// Shared authentication endpoints for EVERY role (single sign-in).
/// The issued token's role claim decides which features a user can reach next.
/// </summary>
[ApiController]
[Route("auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    private string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();

    /// <summary>POST /api/v1/auth/login → email + password → JWT + refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>POST /api/v1/auth/refresh → exchange refresh token for new access + refresh tokens.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await authService.RefreshAsync(request.RefreshToken, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>GET /api/v1/auth/me → the current user, from the bearer token.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = GetUserId();
        var result = await authService.GetCurrentUserAsync(userId, ct);
        return Ok(ResponseHelper.Ok(result, CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/auth/logout. Revokes the supplied refresh token (if any);
    /// the access token is stateless and expires naturally.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(LogoutRequest? request, CancellationToken ct)
    {
        await authService.LogoutAsync(request?.RefreshToken, ct);
        return Ok(ResponseHelper.Ok(new { loggedOut = true }, CorrelationId));
    }

    /// <summary>
    /// POST /api/v1/auth/change-password — the signed-in user changes their own password.
    /// Requires the current password; the account is taken from the token, never the body.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await authService.ChangePasswordAsync(GetUserId(), request, ct);
        return Ok(ResponseHelper.Ok(new { changed = true }, CorrelationId));
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id)
            ? id
            : throw new InvalidTokenException();
    }
}
