import { test, expect } from '@playwright/test'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = Date.now().toString(36)
const originalName = `E2E Edit Orig ${uniqueSuffix}`
const updatedName = `E2E Edit Upd ${uniqueSuffix}`

test.describe.serial('Company Update', () => {
  test('setup: create a company to edit', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: originalName })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company created successfully')
    await companyPage.expectRowExists(originalName)
  })

  test('should open the edit dialog with the pre-filled name', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickEdit(originalName)
    await expect(page.getByRole('heading', { name: 'Edit Company' })).toBeVisible()
    const nameInput = page.locator('[role="dialog"]').getByLabel('Name', { exact: true })
    await expect(nameInput).toHaveValue(originalName, { timeout: 10_000 })
  })

  test('should show validation error when name is cleared', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickEdit(originalName)
    await page.locator('[role="dialog"]').getByLabel('Name', { exact: true }).fill('')
    await companyPage.submitEditForm()
    await companyPage.expectFieldError('Name must be at least 2 characters')
    await companyPage.expectDialogOpen()
  })

  test('should update the company name successfully', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickEdit(originalName)
    await companyPage.fillCompanyForm({ name: updatedName })
    await companyPage.submitEditForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company updated successfully')
    await companyPage.expectRowExists(updatedName)
  })

  test('should update optional fields (slug, email, phone)', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.clickEdit(updatedName)
    await companyPage.fillCompanyForm({
      name: updatedName,
      slug: `e2e-upd-${uniqueSuffix}`,
      email: `updated-${uniqueSuffix}@example.com`,
      phone: `+4${Date.now().toString().slice(-9)}`,
    })
    await companyPage.submitEditForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company updated successfully')
    await companyPage.expectRowExists(updatedName)
  })
})
