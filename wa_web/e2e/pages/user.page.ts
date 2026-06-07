import { type Page, type Locator, expect } from '@playwright/test'

export interface UserFormData {
  companyName: string
  fullName: string
  email: string
  password?: string
  role?: 'Company Admin' | 'Agent'
}

export class UserPage {
  readonly addButton: Locator
  readonly searchInput: Locator
  readonly table: Locator

  constructor(readonly page: Page) {
    this.addButton = page.getByRole('button', { name: 'Add User' })
    this.searchInput = page.getByPlaceholder('Search users...')
    this.table = page.getByRole('table')
  }

  async goto() {
    await this.page.goto('/superadmin/users')
    await this.page.waitForLoadState('networkidle')
  }

  // ─── Table helpers ────────────────────────────────────────────────────────
  // Rows are identified by email — it is unique platform-wide and always visible as sub-text
  getRow(email: string) {
    return this.page.getByRole('row').filter({ hasText: email }).first()
  }

  async expectRowExists(email: string) {
    await expect(this.getRow(email)).toBeVisible({ timeout: 10_000 })
  }

  async expectRowNotExists(email: string) {
    await expect(this.getRow(email)).not.toBeAttached({ timeout: 10_000 })
  }

  async expectRowStatus(email: string, status: 'Active' | 'Inactive') {
    const row = this.getRow(email)
    await expect(row).toBeVisible({ timeout: 10_000 })
    await expect(row.getByText(status, { exact: true })).toBeVisible({ timeout: 10_000 })
  }

  async expectRowRole(email: string, role: 'Company Admin' | 'Agent') {
    const row = this.getRow(email)
    await expect(row).toBeVisible({ timeout: 10_000 })
    await expect(row.getByText(role, { exact: true })).toBeVisible({ timeout: 10_000 })
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

  // ─── Row actions ──────────────────────────────────────────────────────────
  async openRowActions(email: string) {
    const row = this.getRow(email)
    await row.getByRole('button', { name: 'Open actions' }).click()
  }

  async clickEdit(email: string) {
    await this.openRowActions(email)
    await this.page.getByRole('menuitem', { name: 'Edit' }).click()
  }

  async clickResetPassword(email: string) {
    await this.openRowActions(email)
    await this.page.getByRole('menuitem', { name: 'Reset Password' }).click()
  }

  async clickDeactivate(email: string) {
    await this.openRowActions(email)
    await this.page.getByRole('menuitem', { name: 'Deactivate' }).click()
  }

  async clickActivate(email: string) {
    await this.openRowActions(email)
    await this.page.getByRole('menuitem', { name: 'Activate' }).click()
  }

  // ─── Dialog interactions ──────────────────────────────────────────────────
  async openCreateDialog() {
    await this.addButton.click()
    await expect(this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')).toBeVisible({ timeout: 5_000 })
  }

  private dialog() {
    return this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')
  }

  private async selectCompany(companyName: string) {
    const trigger = this.dialog().locator('#companyId')
    await expect(trigger).not.toBeDisabled({ timeout: 10_000 })
    await trigger.click()
    await this.page.getByRole('option', { name: companyName, exact: true }).click()
    await this.page
      .locator('[data-radix-select-content]')
      .waitFor({ state: 'hidden', timeout: 3_000 })
      .catch(() => {})
  }

  private async selectRole(role: string) {
    await this.dialog().locator('#role').click()
    await this.page.getByRole('option', { name: role, exact: true }).click()
    await this.page
      .locator('[data-radix-select-content]')
      .waitFor({ state: 'hidden', timeout: 3_000 })
      .catch(() => {})
  }

  async fillUserForm(data: Partial<UserFormData>) {
    const d = this.dialog()
    if (data.companyName !== undefined) await this.selectCompany(data.companyName)
    if (data.fullName !== undefined) {
      await d.getByLabel('Full Name', { exact: true }).clear()
      await d.getByLabel('Full Name', { exact: true }).fill(data.fullName)
    }
    if (data.email !== undefined) {
      await d.getByLabel('Email', { exact: true }).clear()
      await d.getByLabel('Email', { exact: true }).fill(data.email)
    }
    if (data.password !== undefined) {
      await d.getByLabel('Password', { exact: true }).clear()
      await d.getByLabel('Password', { exact: true }).fill(data.password)
    }
    if (data.role !== undefined) await this.selectRole(data.role)
  }

  async submitCreateForm() {
    await this.dialog().getByRole('button', { name: 'Create User' }).click()
  }

  async submitEditForm() {
    await this.dialog().getByRole('button', { name: 'Update User' }).click()
  }

  // ─── Reset-password dialog ────────────────────────────────────────────────
  async fillResetPasswordForm(newPassword: string) {
    await this.dialog().getByLabel('New Password', { exact: true }).clear()
    await this.dialog().getByLabel('New Password', { exact: true }).fill(newPassword)
  }

  async submitResetPasswordForm() {
    await this.dialog().getByRole('button', { name: 'Reset Password' }).click()
  }

  // ─── Alert-dialog helpers ─────────────────────────────────────────────────
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
