/**
 * Centralized query key management
 * Provides consistent query keys across the application
 */

export const queryKeys = {
  // Products
  products: {
    all: ['products'] as const,
    lists: () => [...queryKeys.products.all, 'list'] as const,
    list: (filters?: any, pagination?: any) => [...queryKeys.products.lists(), { filters, pagination }] as const,
    details: () => [...queryKeys.products.all, 'detail'] as const,
    detail: (id: number) => [...queryKeys.products.details(), id] as const,
    count: (filters?: any) => [...queryKeys.products.all, 'count', filters] as const,
    exists: (id: number) => [...queryKeys.products.all, 'exists', id] as const,
    infinite: (filters?: any) => [...queryKeys.products.all, 'infinite', filters] as const,
    search: (term?: string) => [...queryKeys.products.all, 'search', term] as const,
  },

  // Categories
  categories: {
    all: ['categories'] as const,
    lists: () => [...queryKeys.categories.all, 'list'] as const,
    list: (filters?: any) => [...queryKeys.categories.lists(), filters] as const,
    details: () => [...queryKeys.categories.all, 'detail'] as const,
    detail: (id: number) => [...queryKeys.categories.details(), id] as const,
  },

  // Users
  users: {
    all: ['users'] as const,
    lists: () => [...queryKeys.users.all, 'list'] as const,
    list: (filters?: any) => [...queryKeys.users.lists(), filters] as const,
    details: () => [...queryKeys.users.all, 'detail'] as const,
    detail: (id: string) => [...queryKeys.users.details(), id] as const,
    profile: () => [...queryKeys.users.all, 'profile'] as const,
  },

  // Stats/Analytics
  stats: {
    all: ['stats'] as const,
    dashboard: () => [...queryKeys.stats.all, 'dashboard'] as const,
    inventory: () => [...queryKeys.stats.all, 'inventory'] as const,
  },
} as const
