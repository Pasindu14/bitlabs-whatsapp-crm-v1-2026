import { type Page, type Locator, expect } from '@playwright/test'

export interface ContactFormData {
  name: string
  phone: string
}

export class ContactPage {
  readonly addButton: Locator
  readonly searchInput: Locator
  readonly table: Locator

  constructor(readonly page: Page) {
    this.addButton = page.getByRole('button', { name: 'Add Contact' })
    this.searchInput = page.getByPlaceholder('Search contacts...')
    this.table = page.getByRole('table')
  }

  // ─── Navigation ────────────────────────────────────────────────────────────
  async goto() {
    await this.page.goto('/contacts')
    await this.page.waitForLoadState('networkidle')
  }

  // ─── Table helpers ──────────────────────────────────────────────────────────
  // Rows are identified by phone — it is unique per company and always visible as sub-text
  getRow(phone: string) {
    return this.page.getByRole('row').filter({ hasText: phone }).first()
  }

  async expectRowExists(phone: string) {
    await expect(this.getRow(phone)).toBeVisible({ timeout: 10_000 })
  }

  async expectRowNotExists(phone: string) {
    await expect(this.getRow(phone)).not.toBeAttached({ timeout: 10_000 })
  }

  async expectRowStatus(phone: string, status: 'Active' | 'Inactive') {
    const row = this.getRow(phone)
    await expect(row).toBeVisible({ timeout: 10_000 })
    await expect(row.getByText(status, { exact: true })).toBeVisible({ timeout: 10_000 })
  }

  async expectTableHasRows() {
    await expect(this.page.getByRole('row').nth(1)).toBeVisible({ timeout: 10_000 })
  }

  // ─── Search ─────────────────────────────────────────────────────────────────
  async search(query: string) {
    await this.searchInput.fill(query)
    await this.page.waitForTimeout(500)
  }

  async clearSearch() {
    await this.searchInput.clear()
    await this.page.waitForTimeout(500)
  }

  // ─── Row actions ─────────────────────────────────────────────────────────────
  async openRowActions(phone: string) {
    const row = this.getRow(phone)
    await row.getByRole('button', { name: 'Open actions' }).click()
  }

  async clickEdit(phone: string) {
    await this.openRowActions(phone)
    await this.page.getByRole('menuitem', { name: 'Edit' }).click()
  }

  async clickDeactivate(phone: string) {
    await this.openRowActions(phone)
    await this.page.getByRole('menuitem', { name: 'Deactivate' }).click()
  }

  async clickActivate(phone: string) {
    await this.openRowActions(phone)
    await this.page.getByRole('menuitem', { name: 'Activate' }).click()
  }

  // ─── Dialog interactions ──────────────────────────────────────────────────────
  async openCreateDialog() {
    await this.addButton.click()
    await expect(this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')).toBeVisible({ timeout: 5_000 })
  }

  private dialog() {
    return this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')
  }

  async fillContactForm(data: Partial<ContactFormData>) {
    const d = this.dialog()
    if (data.name !== undefined) {
      await d.getByLabel('Name', { exact: true }).clear()
      await d.getByLabel('Name', { exact: true }).fill(data.name)
    }
    if (data.phone !== undefined) {
      await d.getByLabel('Phone Number', { exact: true }).clear()
      await d.getByLabel('Phone Number', { exact: true }).fill(data.phone)
    }
  }

  async submitCreateForm() {
    await this.dialog().getByRole('button', { name: 'Add Contact' }).click()
  }

  async submitEditForm() {
    await this.dialog().getByRole('button', { name: 'Update Contact' }).click()
  }

  // ─── Alert-dialog helpers ──────────────────────────────────────────────────
  async confirmAlertAction(buttonName: string) {
    await this.page.getByRole('button', { name: buttonName }).click()
  }

  async cancelAlert() {
    await this.page.getByRole('button', { name: 'Cancel' }).click()
  }

  // ─── Toast assertions ──────────────────────────────────────────────────────
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

  // ─── Assertion helpers ─────────────────────────────────────────────────────
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
