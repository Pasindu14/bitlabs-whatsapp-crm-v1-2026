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

  // Team (CompanyAdmin — users within the caller's own company)
  team: {
    all: ['team'] as const,
    lists: () => [...queryKeys.team.all, 'list'] as const,
    list: (filters?: any) => [...queryKeys.team.lists(), filters] as const,
    details: () => [...queryKeys.team.all, 'detail'] as const,
    detail: (id: string) => [...queryKeys.team.details(), id] as const,
  },

  // Companies (SuperAdmin)
  companies: {
    all: ['companies'] as const,
    lists: () => [...queryKeys.companies.all, 'list'] as const,
    list: (filters?: any) => [...queryKeys.companies.lists(), filters] as const,
    details: () => [...queryKeys.companies.all, 'detail'] as const,
    detail: (id: string) => [...queryKeys.companies.details(), id] as const,
  },

  // WABA connections (SuperAdmin)
  wabaConnections: {
    all: ['waba-connections'] as const,
    lists: () => [...queryKeys.wabaConnections.all, 'list'] as const,
    list: (filters?: any) => [...queryKeys.wabaConnections.lists(), filters] as const,
    details: () => [...queryKeys.wabaConnections.all, 'detail'] as const,
    detail: (id: string) => [...queryKeys.wabaConnections.details(), id] as const,
  },

  // Contacts (CompanyAdmin — the caller's own company)
  contacts: {
    all: ['contacts'] as const,
    lists: () => [...queryKeys.contacts.all, 'list'] as const,
    list: (filters?: any) => [...queryKeys.contacts.lists(), filters] as const,
    details: () => [...queryKeys.contacts.all, 'detail'] as const,
    detail: (id: string) => [...queryKeys.contacts.details(), id] as const,
  },

  // Contact lists (CompanyAdmin — the caller's own company)
  contactLists: {
    all: ['contact-lists'] as const,
    lists: () => [...queryKeys.contactLists.all, 'list'] as const,
    list: (filters?: any) => [...queryKeys.contactLists.lists(), filters] as const,
    details: () => [...queryKeys.contactLists.all, 'detail'] as const,
    detail: (id: string) => [...queryKeys.contactLists.details(), id] as const,
  },

  // Stats/Analytics
  stats: {
    all: ['stats'] as const,
    dashboard: () => [...queryKeys.stats.all, 'dashboard'] as const,
    inventory: () => [...queryKeys.stats.all, 'inventory'] as const,
  },
} as const
