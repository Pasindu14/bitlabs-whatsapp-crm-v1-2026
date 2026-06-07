import { logger } from '@/lib/logger'
import {
  DatabaseError,
  ConflictError,
  NotFoundError,
  ValidationError,
  UnauthorizedError,
} from '@/lib/errors'
import { ApiError } from '@/lib/api/client'

type QueryConfig = {
  context: string
  method: string
  logParams?: Record<string, any>
}

type PostgresError = {
  code: string
  constraint?: string
  column?: string
  detail?: string
  message: string
  stack?: string
}

function isPostgresError(error: any): error is PostgresError {
  return error && typeof error.code === 'string'
}

export async function executeQuery<T>(
  config: QueryConfig,
  handler: () => Promise<T>
): Promise<T> {
  const startTime = Date.now()
  const { context, method, logParams = {} } = config

  try {
    // Only log debug for operations with parameters
    if (Object.keys(logParams).length > 0) {
      logger.debug(
        { context, method, ...logParams },
        `${method} started`
      )
    }

    const result = await handler()

    logger.info(
      { context, method, duration: Date.now() - startTime },
      `${method} completed`
    )

    return result
  } catch (error: any) {
    const duration = Date.now() - startTime

/*     logger.error(
      {
        context,
        method,
        error: error.message,
        code: error.code,
        stack: error.stack,
        duration,
      },
      `${method} failed`
    ) */

    // Re-throw domain errors as-is
    if (
      error instanceof ConflictError ||
      error instanceof NotFoundError ||
      error instanceof ValidationError ||
      error instanceof DatabaseError ||
      error instanceof UnauthorizedError ||
      error instanceof ApiError
    ) {
      throw error
    }

    // Map Postgres error codes to domain errors
    if (isPostgresError(error)) {
      // Unique violation
      if (error.code === '23505') {
        const constraint = error.constraint ?? ''
        const detail = error.detail ?? ''

        if (constraint.includes('sku') || detail.toLowerCase().includes('sku')) {
          throw new ConflictError('Record with this SKU already exists')
        }
        if (constraint.includes('slug') || detail.toLowerCase().includes('slug')) {
          throw new ConflictError('Record with this slug already exists')
        }
        if (constraint.includes('email') || detail.toLowerCase().includes('email')) {
          throw new ConflictError('Record with this email already exists')
        }

        // Provide more context in the default message
        const constraintInfo = constraint ? `: ${constraint}` : ''
        throw new ConflictError(`Duplicate record${constraintInfo}`)
      }

      // Foreign key violation
      if (error.code === '23503') {
        const detail = error.detail ?? 'Referenced record not found'
        throw new NotFoundError(detail)
      }

      // Not null violation
      if (error.code === '23502') {
        const column = error.column ?? 'field'
        throw new ValidationError(`Required field '${column}' is missing`)
      }

      // Check constraint violation
      if (error.code === '23514') {
        const detail = error.detail ?? 'Data violates constraints'
        throw new ValidationError(detail)
      }

      // Invalid text representation (type mismatch)
      if (error.code === '22P02') {
        throw new ValidationError('Invalid data format')
      }

      // Numeric value out of range
      if (error.code === '22003') {
        throw new ValidationError('Numeric value out of range')
      }
    }

    // Wrap unknown errors
    throw new DatabaseError(`Database operation failed: ${error.message}`)
  }
}