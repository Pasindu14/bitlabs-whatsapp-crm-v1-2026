using System.ComponentModel.DataAnnotations;

namespace wa_api.Features.Auth.Dtos;

/// <summary>Body for <c>POST /api/v1/auth/refresh</c>.</summary>
public record RefreshRequest([Required] string RefreshToken);
