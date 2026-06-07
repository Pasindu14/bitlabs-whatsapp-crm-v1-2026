// lib/errors.ts

/**
 * Base application error class
 * All custom errors should extend this
 */
export class AppError extends Error {
  public readonly code: string
  public readonly statusCode: number
  public readonly isOperational: boolean

  constructor(
    message: string,
    code: string = 'APP_ERROR',
    statusCode: number = 500,
    isOperational: boolean = true
  ) {
    super(message)
    this.name = this.constructor.name
    this.code = code
    this.statusCode = statusCode
    this.isOperational = isOperational

    // Maintains proper stack trace for where our error was thrown (only available on V8)
    if (Error.captureStackTrace) {
      Error.captureStackTrace(this, this.constructor)
    }
  }
}

/**
 * 401 Unauthorized - User is not authenticated
 */
export class UnauthorizedError extends AppError {
  constructor(message: string = 'Unauthorized access') {
    super(message, 'UNAUTHORIZED', 401)
  }
}

/**
 * 403 Forbidden - User is authenticated but lacks permissions
 */
export class ForbiddenError extends AppError {
  constructor(message: string = 'Access forbidden') {
    super(message, 'FORBIDDEN', 403)
  }
}

/**
 * 404 Not Found - Resource not found
 */
export class NotFoundError extends AppError {
  constructor(message: string = 'Resource not found') {
    super(message, 'NOT_FOUND', 404)
  }
}

/**
 * 400 Bad Request - Invalid input data
 */
export class ValidationError extends AppError {
  public readonly fields?: Record<string, string>

  constructor(message: string = 'Validation failed', fields?: Record<string, string>) {
    super(message, 'VALIDATION_ERROR', 400)
    this.fields = fields
  }
}

/**
 * 409 Conflict - Resource already exists or conflict
 */
export class ConflictError extends AppError {
  constructor(message: string = 'Resource conflict') {
    super(message, 'CONFLICT', 409)
  }
}

/**
 * 429 Too Many Requests - Rate limit exceeded
 */
export class RateLimitError extends AppError {
  public readonly retryAfter?: number

  constructor(message: string = 'Too many requests', retryAfter?: number) {
    super(message, 'RATE_LIMIT', 429)
    this.retryAfter = retryAfter
  }
}

/**
 * 500 Internal Server Error - Unexpected errors
 */
export class InternalError extends AppError {
  constructor(message: string = 'Internal server error') {
    super(message, 'INTERNAL_ERROR', 500, false)
  }
}

/**
 * 503 Service Unavailable - External service error
 */
export class ServiceUnavailableError extends AppError {
  public readonly service?: string

  constructor(message: string = 'Service temporarily unavailable', service?: string) {
    super(message, 'SERVICE_UNAVAILABLE', 503)
    this.service = service
  }
}

/**
 * Database-related errors
 */
export class DatabaseError extends AppError {
  constructor(message: string = 'Database operation failed') {
    super(message, 'DATABASE_ERROR', 500, false)
  }
}

/**
 * External API errors
 */
export class ExternalAPIError extends AppError {
  public readonly apiName?: string
  public readonly originalError?: any

  constructor(
    message: string = 'External API request failed',
    apiName?: string,
    originalError?: any
  ) {
    super(message, 'EXTERNAL_API_ERROR', 502)
    this.apiName = apiName
    this.originalError = originalError
  }
}