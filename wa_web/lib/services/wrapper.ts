// lib/services/wrapper.ts

import { logger } from '@/lib/logger'

type ServiceConfig = {
  /** Service class name (e.g., 'ProductService') */
  context: string
  
  /** Method name (e.g., 'create', 'getById') */
  method: string
  
  /** User ID for audit trail */
  userId?: string
  
  /** Additional parameters to log */
  logParams?: Record<string, any>
  
  /** Mask sensitive fields in logs */
  maskFields?: string[]
}

/**
 * Wraps service methods with consistent logging and timing.
 * Does NOT catch errors - lets them bubble to Actions layer.
 * 
 * @example
 * static async create(data: CreateProductDto): Promise<Product> {
 *   return executeService(
 *     { context: 'ProductService', method: 'create' },
 *     async () => {
 *       // Validate business rules
 *       const exists = await ProductQueries.findBySku(data.sku)
 *       if (exists) {
 *         throw new ConflictError('SKU already exists')
 *       }
 *       
 *       // Create product
 *       return await ProductQueries.create(data)
 *     }
 *   )
 * }
 */
export async function executeService<T>(
  config: ServiceConfig,
  handler: () => Promise<T>
): Promise<T> {
  const startTime = Date.now()
  const { context, method, userId, logParams = {}, maskFields = [] } = config

  try {
    // Log service start (only if logParams exist to reduce noise)
    if (Object.keys(logParams).length > 0) {
      logger.debug(
        { context, method, userId, ...logParams },
        `${method} started`
      )
    }

    // Execute service logic
    const result = await handler()

    // Log success
/*     logger.info(
      { 
        context, 
        method, 
        userId, 
        duration: Date.now() - startTime 
      },
      `${method} completed`
    ) */

    return result
  } catch (error: any) {
    // Log error but DO NOT transform it
    // Let it bubble to Actions layer
/*     logger.error(
      {
        context,
        method,
        userId,
        error: error.message,
        errorType: error.constructor.name,
        stack: error.stack,
        duration: Date.now() - startTime,
      },
      `${method} failed`
    ) */

    // Re-throw as-is
    throw error
  }
}

/**
 * Mask sensitive fields in data for logging
 */
export function maskSensitiveData(
  data: any,
  fields: string[] = ['password', 'token', 'secret', 'apiKey', 'privateKey']
): any {
  if (!data || typeof data !== 'object') return data

  const masked = { ...data }
  
  for (const field of fields) {
    if (field in masked) {
      masked[field] = '***REDACTED***'
    }
  }

  return masked
}