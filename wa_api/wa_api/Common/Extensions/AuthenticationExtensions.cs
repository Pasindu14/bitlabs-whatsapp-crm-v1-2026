using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using wa_api.Common.Errors;
using wa_api.Features.Auth;

namespace wa_api.Common.Extensions;

/// <summary>
/// Wires JWT bearer authentication + authorization and the Auth feature services.
/// Tokens are validated with RAW claim names (MapInboundClaims = false), so the
/// "role" claim drives <c>[Authorize(Roles = "SuperAdmin")]</c> and "sub" is the user id.
/// </summary>
public static class AuthenticationExtensions
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IServiceCollection AddPlatformAuthentication(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration section is missing.");
        if (string.IsNullOrWhiteSpace(jwt.SecretKey))
            throw new InvalidOperationException("Jwt:SecretKey is not configured.");

        // Auth feature services.
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // keep raw claim names ("sub", "role", …)
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };

                // Emit the standard ApiError envelope for 401/403 instead of an empty body.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = ctx =>
                    {
                        ctx.HandleResponse();
                        return WriteError(ctx.HttpContext, 401,
                            "AUTH_REQUIRED", "Authentication is required to access this resource.");
                    },
                    OnForbidden = ctx => WriteError(ctx.HttpContext, 403,
                        "FORBIDDEN_ACCESS", "You do not have permission to access this resource.")
                };
            });

        services.AddAuthorization();
        return services;
    }

    private static Task WriteError(HttpContext ctx, int status, string code, string message)
    {
        if (ctx.Response.HasStarted) return Task.CompletedTask;

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var correlationId = ctx.Items["CorrelationId"]?.ToString();
        var error = new ApiError(code, message, null, null, null, correlationId, DateTime.UtcNow);
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new ApiErrorResponse(false, error), _json));
    }
}
