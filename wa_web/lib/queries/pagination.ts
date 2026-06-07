export type OffsetPagination = {
  page: number
  pageSize: number
}

export type OffsetPaginatedResult<T> = {
  items: T[]
  total: number
  page: number
  pageSize: number
  totalPages: number
  hasMore: boolean
}

export type CursorPagination = {
  cursor?: string | null
  limit: number
}

export type CursorPaginatedResult<T> = {
  items: T[]
  nextCursor: string | null
  hasMore: boolean
}

export const DEFAULT_PAGE_SIZE = 10
export const MAX_PAGE_SIZE = 100

export function getOffset(page: number, pageSize: number): number {
  return (page - 1) * pageSize
}

export function getTotalPages(total: number, pageSize: number): number {
  return Math.ceil(total / pageSize)
}

export function sanitizePagination(
  page?: number,
  pageSize?: number
): OffsetPagination {
  const sanitizedPage = Math.max(1, page ?? 1)
  const sanitizedPageSize = Math.min(
    MAX_PAGE_SIZE,
    Math.max(1, pageSize ?? DEFAULT_PAGE_SIZE)
  )

  return {
    page: sanitizedPage,
    pageSize: sanitizedPageSize,
  }
}

export function sanitizeCursorPagination(
  cursor?: string | null,
  limit?: number
): CursorPagination {
  const sanitizedLimit = Math.min(
    MAX_PAGE_SIZE,
    Math.max(1, limit ?? DEFAULT_PAGE_SIZE)
  )

  return {
    cursor: cursor ?? null,
    limit: sanitizedLimit,
  }
}
