// lib/logger.ts

type LogLevel = 'debug' | 'info' | 'warn' | 'error'

interface LogContext {
  context?: string
  userId?: string
  [key: string]: any
}

class Logger {
  private isDevelopment = process.env.NODE_ENV === 'development'
  private minLevel: LogLevel = (process.env.LOG_LEVEL as LogLevel) || 'info'

  private levels: Record<LogLevel, number> = {
    debug: 0,
    info: 1,
    warn: 2,
    error: 3
  }

  private shouldLog(level: LogLevel): boolean {
    return this.levels[level] >= this.levels[this.minLevel]
  }

  private formatMessage(level: LogLevel, context: LogContext, message: string): string {
    const timestamp = new Date().toISOString()
    const contextStr = context.context ? `[${context.context}]` : ''
    
    if (this.isDevelopment) {
      // Pretty format for development
      return `${timestamp} ${level.toUpperCase()} ${contextStr} ${message}`
    }
    
    // JSON format for production (structured logging)
    return JSON.stringify({
      timestamp,
      level,
      message,
      ...context
    })
  }

  private log(level: LogLevel, context: LogContext, message: string): void {
    if (!this.shouldLog(level)) return

    const formattedMessage = this.formatMessage(level, context, message)

    switch (level) {
      case 'debug':
        console.debug(formattedMessage, context)
        break
      case 'info':
        console.info(formattedMessage, context)
        break
      case 'warn':
        console.warn(formattedMessage, context)
        break
      case 'error':
        console.error(formattedMessage, context)
        break
    }
  }

  debug(context: LogContext, message: string): void {
    this.log('debug', context, message)
  }

  info(context: LogContext, message: string): void {
    this.log('info', context, message)
  }

  warn(context: LogContext, message: string): void {
    this.log('warn', context, message)
  }

  error(context: LogContext, message: string): void {
    this.log('error', context, message)
  }

  // Convenience method for logging errors with full details
  logError(error: Error, context?: LogContext): void {
    this.error(
      {
        ...context,
        error: {
          name: error.name,
          message: error.message,
          stack: error.stack
        }
      },
      error.message
    )
  }
}

// Export singleton instance
export const logger = new Logger()

// Export type for use in other files
export type { LogLevel, LogContext }