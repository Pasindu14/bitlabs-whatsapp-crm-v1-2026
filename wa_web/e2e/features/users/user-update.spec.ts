import { test, expect } from '@playwright/test'
import { UserPage } from '../../pages/user.page'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = Date.now().toString(36)
const companyName = `E2E Upd Co ${uniqueSuffix}`
const originalEmail = `e2e-upd-${uniqueSuffix}@test.com`
const updatedName = `E2E Updated Name ${uniqueSuffix}`

test.describe.serial('User Update', () => {
  test('setup: create company and user', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: companyName })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()

    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await userPage.fillUserForm({
      companyName,
      fullName: `E2E Edit User ${uniqueSuffix}`,
      email: originalEmail,
      password: 'TestPass123!',
    })
    await userPage.submitCreateForm()
    await userPage.expectDialogClosed()
    await userPage.expectSuccessToast('User created successfully')
    await userPage.expectRowExists(originalEmail)
  })

  test('should open the edit dialog with the pre-filled full name', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickEdit(originalEmail)
    await expect(page.getByRole('heading', { name: 'Edit User' })).toBeVisible()
    const nameInput = page.locator('[role="dialog"]:not([data-nextjs-dialog])').getByLabel('Full Name', { exact: true })
    await expect(nameInput).toHaveValue(`E2E Edit User ${uniqueSuffix}`, { timeout: 10_000 })
  })

  test('should NOT show a password field in edit mode', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickEdit(originalEmail)
    await expect(page.locator('[role="dialog"]:not([data-nextjs-dialog])').getByLabel('Password', { exact: true })).not.toBeVisible()
  })

  test('should show validation error when full name is cleared', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickEdit(originalEmail)
    await page.locator('[role="dialog"]:not([data-nextjs-dialog])').getByLabel('Full Name', { exact: true }).fill('')
    await userPage.submitEditForm()
    await userPage.expectFieldError('Full name must be at least 2 characters')
    await userPage.expectDialogOpen()
  })

  test('should update the full name successfully', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickEdit(originalEmail)
    await userPage.fillUserForm({ fullName: updatedName })
    await userPage.submitEditForm()
    await userPage.expectDialogClosed()
    await userPage.expectSuccessToast('User updated successfully')
    await userPage.expectRowExists(originalEmail)
  })

  test('should change the role from CompanyAdmin to Agent', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickEdit(originalEmail)
    await userPage.fillUserForm({ role: 'Agent' })
    await userPage.submitEditForm()
    await userPage.expectDialogClosed()
    await userPage.expectSuccessToast('User updated successfully')
    await userPage.expectRowRole(originalEmail, 'Agent')
  })
})
