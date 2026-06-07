import { type Page, type Locator, expect } from '@playwright/test'

export interface WabaConnectionFormData {
  companyName: string
  phoneNumberId: string
  wabaId: string
  displayPhoneNumber?: string
  accessToken: string
  status?: 'Connected' | 'Disconnected' | 'Invalid'
}

export class WabaConnectionPage {
  readonly addButton: Locator
  readonly searchInput: Locator
  readonly table: Locator

  constructor(readonly page: Page) {
    this.addButton = page.getByRole('button', { name: 'Add Connection' })
    this.searchInput = page.getByPlaceholder('Search connections...')
    this.table = page.getByRole('table')
  }

  async goto() {
    await this.page.goto('/superadmin/waba-connections')
    await this.page.waitForLoadState('networkidle')
  }

  // ─── Table helpers ────────────────────────────────────────────────────────
  getRow(phoneNumberId: string) {
    return this.page.getByRole('row').filter({ hasText: phoneNumberId }).first()
  }

  async expectRowExists(phoneNumberId: string) {
    await expect(this.getRow(phoneNumberId)).toBeVisible({ timeout: 10_000 })
  }

  async expectRowNotExists(phoneNumberId: string) {
    await expect(this.getRow(phoneNumberId)).not.toBeAttached({ timeout: 10_000 })
  }

  async expectConnectionStatus(phoneNumberId: string, status: 'Connected' | 'Disconnected' | 'Invalid') {
    const row = this.getRow(phoneNumberId)
    await expect(row).toBeVisible({ timeout: 10_000 })
    await expect(row.getByText(status, { exact: true })).toBeVisible({ timeout: 10_000 })
  }

  async expectActiveStatus(phoneNumberId: string, status: 'Active' | 'Inactive') {
    const row = this.getRow(phoneNumberId)
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

  // ─── Row actions ──────────────────────────────────────────────────────────
  async openRowActions(phoneNumberId: string) {
    const row = this.getRow(phoneNumberId)
    await row.getByRole('button', { name: 'Open actions' }).click()
  }

  async clickEdit(phoneNumberId: string) {
    await this.openRowActions(phoneNumberId)
    await this.page.getByRole('menuitem', { name: 'Edit' }).click()
  }

  async clickDeactivate(phoneNumberId: string) {
    await this.openRowActions(phoneNumberId)
    await this.page.getByRole('menuitem', { name: 'Deactivate' }).click()
  }

  async clickActivate(phoneNumberId: string) {
    await this.openRowActions(phoneNumberId)
    await this.page.getByRole('menuitem', { name: 'Activate' }).click()
  }

  // ─── Dialog interactions ──────────────────────────────────────────────────
  async openCreateDialog() {
    await this.addButton.click()
    await expect(this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')).toBeVisible({ timeout: 5_000 })
  }

  private async selectCompany(companyName: string) {
    const dialog = this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')
    const trigger = dialog.locator('#companyId')
    // Wait for companies to finish loading (trigger becomes enabled)
    await expect(trigger).not.toBeDisabled({ timeout: 10_000 })
    await trigger.click()
    await this.page.getByRole('option', { name: companyName, exact: true }).click()
    await this.page
      .locator('[data-radix-select-content]')
      .waitFor({ state: 'hidden', timeout: 3_000 })
      .catch(() => {})
  }

  private async selectStatus(status: string) {
    const dialog = this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')
    await dialog.locator('#status').click()
    await this.page.getByRole('option', { name: status, exact: true }).click()
    await this.page
      .locator('[data-radix-select-content]')
      .waitFor({ state: 'hidden', timeout: 3_000 })
      .catch(() => {})
  }

  async fillWabaConnectionForm(data: Partial<WabaConnectionFormData>) {
    const dialog = this.page.locator('[role="dialog"]:not([data-nextjs-dialog])')

    if (data.companyName !== undefined) {
      await this.selectCompany(data.companyName)
    }
    if (data.phoneNumberId !== undefined) {
      await dialog.getByLabel('Phone Number ID', { exact: true }).clear()
      await dialog.getByLabel('Phone Number ID', { exact: true }).fill(data.phoneNumberId)
    }
    if (data.wabaId !== undefined) {
      await dialog.getByLabel('WABA ID', { exact: true }).clear()
      await dialog.getByLabel('WABA ID', { exact: true }).fill(data.wabaId)
    }
    if (data.displayPhoneNumber !== undefined) {
      await dialog.getByLabel('Display Phone Number (optional)', { exact: true }).clear()
      await dialog.getByLabel('Display Phone Number (optional)', { exact: true }).fill(data.displayPhoneNumber)
    }
    if (data.accessToken !== undefined) {
      // Label differs between create ("Access Token") and edit ("Access Token (leave blank to keep current)")
      await dialog.getByLabel('Access Token', { exact: false }).clear()
      await dialog.getByLabel('Access Token', { exact: false }).fill(data.accessToken)
    }
    if (data.status !== undefined) {
      await this.selectStatus(data.status)
    }
  }

  async submitCreateForm() {
    await this.page.locator('[role="dialog"]:not([data-nextjs-dialog])').getByRole('button', { name: 'Create Connection' }).click()
  }

  async submitEditForm() {
    await this.page.locator('[role="dialog"]:not([data-nextjs-dialog])').getByRole('button', { name: 'Update Connection' }).click()
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
