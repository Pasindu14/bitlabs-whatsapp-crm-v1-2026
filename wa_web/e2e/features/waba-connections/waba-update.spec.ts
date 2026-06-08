import { test, expect } from '@playwright/test'
import { WabaConnectionPage } from '../../pages/waba-connection.page'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = Date.now().toString(36)
const companyName = `E2E WC Edit Co ${uniqueSuffix}`
const phoneBase = `20${Date.now().toString().slice(-10)}`
const originalPhone = `${phoneBase}1`
const updatedPhone = `${phoneBase}2`
const wabaId = `w${phoneBase}`
const accessToken = `EAAG_upd_${uniqueSuffix}`

test.describe.serial('WABA Connection Update', () => {
  test('setup: create company and WABA connection', async ({ page }) => {
    // Create the prerequisite company
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: companyName })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company created successfully')

    // Create the WABA connection to edit
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await wabaPage.fillWabaConnectionForm({
      companyName,
      phoneNumberId: originalPhone,
      wabaId,
      accessToken,
    })
    await wabaPage.submitCreateForm()
    await wabaPage.expectDialogClosed()
    await wabaPage.expectSuccessToast('WABA connection created successfully')
    await wabaPage.expectRowExists(originalPhone)
  })

  test('should open edit dialog with pre-filled Phone Number ID', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickEdit(originalPhone)
    await expect(page.getByRole('heading', { name: 'Edit WABA Connection' })).toBeVisible()
    const phoneInput = page.locator('[role="dialog"]').getByLabel('Phone Number ID', { exact: true })
    await expect(phoneInput).toHaveValue(originalPhone, { timeout: 10_000 })
  })

  test('should show validation error when Phone Number ID is cleared', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickEdit(originalPhone)
    await page.locator('[role="dialog"]').getByLabel('Phone Number ID', { exact: true }).fill('')
    await wabaPage.submitEditForm()
    await wabaPage.expectFieldError('Phone number id is required')
    await wabaPage.expectDialogOpen()
  })

  test('should show validation error when WABA ID is cleared', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickEdit(originalPhone)
    await page.locator('[role="dialog"]').getByLabel('WABA ID', { exact: true }).fill('')
    await wabaPage.submitEditForm()
    await wabaPage.expectFieldError('WABA id is required')
    await wabaPage.expectDialogOpen()
  })

  test('should update the Phone Number ID and display phone number successfully', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickEdit(originalPhone)
    await wabaPage.fillWabaConnectionForm({
      phoneNumberId: updatedPhone,
      displayPhoneNumber: '+1 555 0200',
    })
    await wabaPage.submitEditForm()
    await wabaPage.expectDialogClosed()
    await wabaPage.expectSuccessToast('WABA connection updated successfully')
    await wabaPage.expectRowExists(updatedPhone)
  })

  test('should update the status field successfully', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickEdit(updatedPhone)
    await wabaPage.fillWabaConnectionForm({ status: 'Invalid' })
    await wabaPage.submitEditForm()
    await wabaPage.expectDialogClosed()
    await wabaPage.expectSuccessToast('WABA connection updated successfully')
    await wabaPage.expectConnectionStatus(updatedPhone, 'Invalid')
  })
})
