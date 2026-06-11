namespace wa_api.Common.Errors;

/// <summary>
/// Base platform exception. Carries a machine-readable error code (and optional
/// payload) that <see cref="Middleware.GlobalExceptionMiddleware"/> maps to an
/// HTTP status + <see cref="ApiError"/>. Renamed from SFA's SFAException.
/// Domain-specific exceptions are added per-feature as subclasses.
/// </summary>
public abstract class AppException(string errorCode, string message, object? data = null) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public new object? Data { get; } = data;
}

// 400 — Validation
public class ValidationException(Dictionary<string, string[]> fields)
    : AppException("VALIDATION_FAILED", "One or more validation errors occurred.")
{
    public Dictionary<string, string[]> Fields { get; } = fields;
}

// 401 — Authentication
public class AuthenticationException(string code, string message) : AppException(code, message);

public class TokenExpiredException()
    : AuthenticationException("AUTH_TOKEN_EXPIRED", "Access token has expired.");

public class InvalidTokenException()
    : AuthenticationException("AUTH_INVALID_TOKEN", "Token is invalid or has been revoked.");

// 403 — Authorization
public class AuthorizationException(string resource)
    : AppException("FORBIDDEN_ACCESS", $"You do not have permission to access {resource}.");

public class PermissionDeniedException(string permission)
    : AppException("PERMISSION_DENIED", $"Permission '{permission}' is required.");

// 404 — Not Found
public class NotFoundException(string entity, object id)
    : AppException($"{entity.ToUpperInvariant()}_NOT_FOUND", $"{entity} with ID '{id}' was not found.");

// 409 — Conflict
public class ConflictException(string code, string message, object? currentData = null)
    : AppException(code, message, currentData);

public class ConcurrencyConflictException(object? currentData = null)
    : ConflictException("CONCURRENCY_CONFLICT", "Record was modified by another user.", currentData);

public class DuplicateResourceException(string entity)
    : ConflictException($"{entity.ToUpperInvariant()}_DUPLICATE", $"{entity} already exists.");

// 422 — Business Rule
public class BusinessRuleException(string code, string message, object? data = null)
    : AppException(code, message, data);

// 429 — Rate Limited
public class RateLimitException()
    : AppException("RATE_LIMITED", "Too many requests. Please retry after the indicated time.");

// 503 — Infrastructure
public class InfrastructureException(string code, string message) : AppException(code, message);

public class StorageUnavailableException()
    : InfrastructureException("SERVICE_UNAVAILABLE", "Storage service is temporarily unavailable.");

public class DatabaseUnavailableException()
    : InfrastructureException("SERVICE_UNAVAILABLE", "Database is temporarily unavailable.");

public class LockServiceUnavailableException()
    : InfrastructureException("LOCK_SERVICE_UNAVAILABLE", "Lock service is temporarily unavailable.");
