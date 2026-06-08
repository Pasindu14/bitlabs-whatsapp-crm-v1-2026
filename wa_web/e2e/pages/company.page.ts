import { type Page, type Locator, expect } from '@playwright/test'

export interface CompanyFormData {
  name: string
  slug?: string
  email?: string
  phone?: string
}

export class CompanyPage {
  readonly addButton: Locator
  readonly searchInput: Locator
  readonly table: Locator

  constructor(readonly page: Page) {
    this.addButton = page.getByRole('button', { name: 'Add Company' })
    this.searchInput = page.getByPlaceholder('Search companies...')
    this.table = page.getByRole('table')
  }

  async goto() {
    await this.page.goto('/superadmin/companies')
    await this.page.waitForLoadState('networkidle')
  }

  // ─── Table helpers ────────────────────────────────────────────────────────
  getRow(companyName: string) {
    return this.page.getByRole('row').filter({ hasText: companyName }).first()
  }

  async expectRowExists(companyName: string) {
    await expect(this.getRow(companyName)).toBeVisible({ timeout: 10_000 })
  }

  async expectRowNotExists(companyName: string) {
    await expect(this.getRow(companyName)).not.toBeAttached({ timeout: 10_000 })
  }

  async expectRowStatus(companyName: string, status: 'Active' | 'Inactive') {
    const row = this.getRow(companyName)
    await expect(row).toBeVisible({ timeout: 10_000 })
    await expect(row.getByText(status, { exact: true })).toBeVisible({ timeout: 10_000 })
  }

  async expectTableHasRows() {
    await expect(this.page.getByRole('row').nth(1)).toBeVisible({ timeout: 10_000 })
  }

  // ─── Search ───────────────────────────────────────────────────────────────
  async search(query: string) {
    await this.searchInput.fill(query)
    await this.page.waitForTimeout(500)
  }

  async clearSearch() {
    await this.searchInput.clear()
    await this.page.waitForTimeout(500)
  }

  // ─── Row actions (dropdown) ───────────────────────────────────────────────
  async openRowActions(companyName: string) {
    const row = this.getRow(companyName)
    await row.getByRole('button', { name: 'Open actions' }).click()
  }

  async clickEdit(companyName: string) {
    await this.openRowActions(companyName)
    await this.page.getByRole('menuitem', { name: 'Edit' }).click()
  }

  async clickDeactivate(companyName: string) {
    await this.openRowActions(companyName)
    await this.page.getByRole('menuitem', { name: 'Deactivate' }).click()
  }

  async clickActivate(companyName: string) {
    await this.openRowActions(companyName)
    await this.page.getByRole('menuitem', { name: 'Activate' }).click()
  }

  // ─── Dialog interactions ──────────────────────────────────────────────────
  async openCreateDialog() {
    await this.addButton.click()
    await expect(this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')).toBeVisible({ timeout: 5_000 })
  }

  async fillCompanyForm(data: Partial<CompanyFormData>) {
    const dialog = this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')
    if (data.name !== undefined) {
      await dialog.getByLabel('Name', { exact: true }).clear()
      await dialog.getByLabel('Name', { exact: true }).fill(data.name)
    }
    if (data.slug !== undefined) {
      await dialog.getByLabel('Slug (optional)', { exact: true }).clear()
      await dialog.getByLabel('Slug (optional)', { exact: true }).fill(data.slug)
    }
    if (data.email !== undefined) {
      await dialog.getByLabel('Email (optional)', { exact: true }).clear()
      await dialog.getByLabel('Email (optional)', { exact: true }).fill(data.email)
    }
    if (data.phone !== undefined) {
      await dialog.getByLabel('Phone (optional)', { exact: true }).clear()
      await dialog.getByLabel('Phone (optional)', { exact: true }).fill(data.phone)
    }
  }

  async submitCreateForm() {
    await this.page.locator('[role="dialog"]:not([data-nextjs-dialog])').getByRole('button', { name: 'Create Company' }).click()
  }

  async submitEditForm() {
    await this.page.locator('[role="dialog"]:not([data-nextjs-dialog])').getByRole('button', { name: 'Update Company' }).click()
  }

  async confirmAlertAction(buttonName: string) {
    await this.page.getByRole('button', { name: buttonName }).click()
  }

  async cancelAlert() {
    await this.page.getByRole('button', { name: 'Cancel' }).click()
  }

  // ─── Toast assertions ─────────────────────────────────────────────────────
  async expectSuccessToast(partialText?: string) {
    const toast = this.page.locator('[data-sonner-toast][data-type="success"]').first()
    await expect(toast).toBeVisible({ timeout: 10_000 })
    if (partialText) await expect(toast).toContainText(partialText)
  }

  async expectErrorToast(partialText?: string) {
    const toast = this.page.locator('[data-sonner-toast][data-type="error"]').first()
    await expect(toast).toBeVisible({ timeout: 10_000 })
    if (partialText) await expect(toast).toContainText(partialText)
  }

  // ─── Assertion helpers ────────────────────────────────────────────────────
  async expectFieldError(text: string) {
    await expect(this.page.getByText(text).first()).toBeVisible({ timeout: 5_000 })
  }

  async expectDialogClosed() {
    await expect(this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')).not.toBeAttached({ timeout: 15_000 })
  }

  async expectDialogOpen() {
    await expect(this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')).toBeVisible({ timeout: 5_000 })
  }
}
