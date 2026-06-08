// lib/types/actions.ts

/**
 * Generic action response type
 * Use this for ALL server actions to maintain consistency
 */
export type ActionResponse<T = void> =
  | { success: true; data: T }
  | { success: false; error: string; code?: string; fields?: Record<string, string> }

/**
 * The failure variant of ActionResponse.
 * Use this as the error type in mutation onError handlers instead of `any`.
 */
export type ActionFailure = Extract<ActionResponse, { success: false }>

/**
 * Paginated response type
 * Use this for list endpoints that support pagination
 */
export type PaginatedResponse<T> = {
  items: T[]
  pagination: {
    page: number
    pageSize: number
    total: number
    totalPages: number
    hasMore: boolean
  }
}

/**
 * Pagination params
 * Use this for consistency across all paginated endpoints
 */
export type PaginationParams = {
  page?: number
  pageSize?: number
}

/**
 * Sort params
 * Use this for sortable endpoints
 */
export type SortParams<T = string> = {
  sortBy?: T
  sortOrder?: 'asc' | 'desc'
}

/**
 * Search params
 * Use this for searchable endpoints
 */
export type SearchParams = {
  search?: string
  searchFields?: string[]
}