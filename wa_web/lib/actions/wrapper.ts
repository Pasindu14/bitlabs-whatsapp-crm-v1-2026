// lib/actions/wrapper.ts
import { auth } from '@/auth'
import { logger } from '@/lib/logger'
import { ZodError } from 'zod'
import { AppError, UnauthorizedError, ForbiddenError } from '@/lib/errors'
import { ApiError } from '@/lib/api/client'
import type { ActionResponse } from '@/lib/types/actions'

type ActionConfig = {
  name: string
  requireAuth?: boolean
  requiredRole?: string | string[]
  logInputs?: boolean
}

/**
 * Wraps server actions with consistent error handling, logging, and auth
 * 
 * @example
 * export const getProductsAction = createAction(
 *   { name: 'getProductsAction' },
 *   async (filters, pagination) => {
 *     return await ProductService.getAllPaginated(filters, pagination)
 *   }
 * )
 */
export function createAction<TInput extends any[], TOutput>(
  config: ActionConfig,
  handler: (...args: TInput) => Promise<TOutput>
) {
  return async (...args: TInput): Promise<ActionResponse<TOutput>> => {
    const startTime = Date.now()
    const { name, requireAuth = false, requiredRole, logInputs = true } = config

    try {
      // Log start
      if (logInputs) {
        logger.debug({
          context: name,
          args: sanitizeLogData(args)
        }, `${name} started`)
      } else {
        logger.debug({ context: name }, `${name} started`)
      }

      // Authentication check
      if (requireAuth) {
        const session = await auth()

        if (!session?.user) {
          throw new UnauthorizedError('You must be logged in')
        }

        // Role check
        if (requiredRole) {
          const roles = Array.isArray(requiredRole) ? requiredRole : [requiredRole]
          
          if (!roles.includes(session.user.role)) {
            logger.warn({
              context: name,
              userId: session.user.id,
              userRole: session.user.role,
              requiredRole
            }, 'Forbidden access attempt')
            
            throw new ForbiddenError(
              `This action requires one of these roles: ${roles.join(', ')}`
            )
          }
        }
      }

      // Execute handler
      const result = await handler(...args)

      // Log success
/*       logger.info({
        context: name,
        duration: Date.now() - startTime
      }, `${name} completed successfully`) */

      return {
        success: true,
        data: result
      }
    } catch (error) {
      // Log error
/*       logger.error({
        context: name,
        error,
        duration: Date.now() - startTime
      }, `${name} failed`) */

      // Handle Zod validation errors
      if (error instanceof ZodError) {
        const firstError = error.issues[0] // Changed from error.errors to error.issues
        return {
          success: false,
          error: `${firstError.path.join('.')}: ${firstError.message}`,
          code: 'VALIDATION_ERROR'
        }
      }

      // Handle custom application errors
      if (error instanceof AppError) {
        return {
          success: false,
          error: error.message,
          code: error.code
        }
      }

      // Handle API errors — fields are already flattened to Record<string, string> by the interceptor
      if (error instanceof ApiError) {
        return {
          success: false,
          error: error.message,
          code: error.code,
          ...(error.fields && { fields: error.fields }),
        }
      }

      // Handle unknown errors
      console.error(`[${name}] Unhandled error:`, error)
      return {
        success: false,
        error: 'An unexpected error occurred',
        code: 'INTERNAL_ERROR'
      }
    }
  }
}

/**
 * Sanitize sensitive data for logging
 */
function sanitizeLogData(data: any): any {
  if (Array.isArray(data)) {
    return data.map(sanitizeLogData)
  }
  
  if (data && typeof data === 'object') {
    const sanitized: any = {}
    
    for (const [key, value] of Object.entries(data)) {
      // Mask sensitive fields
      if (['password', 'token', 'secret', 'apiKey', 'creditCard'].includes(key)) {
        sanitized[key] = '***'
      } else if (key === 'price' || key === 'amount') {
        sanitized[key] = '***'
      } else {
        sanitized[key] = sanitizeLogData(value)
      }
    }
    
    return sanitized
  }
  
  return data
}