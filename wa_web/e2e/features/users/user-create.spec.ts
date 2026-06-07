import { test, expect } from '@playwright/test'
import { UserPage } from '../../pages/user.page'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = Date.now().toString(36)
const companyName = `E2E User Co ${uniqueSuffix}`
const emailBase = `e2e-user-${uniqueSuffix}`

test.describe.serial('User Create', () => {
  test('setup: create a prerequisite active company', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: companyName })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company created successfully')
  })

  test('should open the create dialog when Add User is clicked', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await expect(page.getByRole('heading', { name: 'Add User' })).toBeVisible()
  })

  test('should close the dialog on Escape key', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await page.keyboard.press('Escape')
    await userPage.expectDialogClosed()
  })

  test('should show validation error when form is submitted empty', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await userPage.submitCreateForm()
    await userPage.expectFieldError('Select a company')
    await userPage.expectDialogOpen()
  })

  test('should show validation error when company is filled but name is empty', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await userPage.fillUserForm({ companyName })
    await userPage.submitCreateForm()
    await userPage.expectFieldError('Full name must be at least 2 characters')
    await userPage.expectDialogOpen()
  })

  test('should show validation error for an invalid email format', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await userPage.fillUserForm({ companyName, fullName: 'Test User', email: 'not-an-email' })
    await userPage.submitCreateForm()
    await userPage.expectFieldError('Enter a valid email')
    await userPage.expectDialogOpen()
  })

  test('should show validation error when password is too short', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await userPage.fillUserForm({
      companyName,
      fullName: 'Test User',
      email: `${emailBase}-a@test.com`,
      password: 'short',
    })
    await userPage.submitCreateForm()
    await userPage.expectFieldError('Password must be at least 8 characters')
    await userPage.expectDialogOpen()
  })

  test('should create a user with the default CompanyAdmin role', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await userPage.fillUserForm({
      companyName,
      fullName: `E2E Admin ${uniqueSuffix}`,
      email: `${emailBase}-admin@test.com`,
      password: 'TestPass123!',
    })
    await userPage.submitCreateForm()
    await userPage.expectDialogClosed()
    await userPage.expectSuccessToast('User created successfully')
    await userPage.expectRowExists(`${emailBase}-admin@test.com`)
    await userPage.expectRowRole(`${emailBase}-admin@test.com`, 'Company Admin')
  })

  test('should create a user with the Agent role', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await userPage.fillUserForm({
      companyName,
      fullName: `E2E Agent ${uniqueSuffix}`,
      email: `${emailBase}-agent@test.com`,
      password: 'TestPass123!',
      role: 'Agent',
    })
    await userPage.submitCreateForm()
    await userPage.expectDialogClosed()
    await userPage.expectSuccessToast('User created successfully')
    await userPage.expectRowExists(`${emailBase}-agent@test.com`)
    await userPage.expectRowRole(`${emailBase}-agent@test.com`, 'Agent')
  })

  test('should reject a duplicate email', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openCreateDialog()
    await userPage.fillUserForm({
      companyName,
      fullName: 'Duplicate User',
      email: `${emailBase}-admin@test.com`, // already created above
      password: 'TestPass123!',
    })
    await userPage.submitCreateForm()
    await userPage.expectDialogOpen()
  })
})
