import { test, expect } from '@playwright/test'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = Date.now().toString(36)

test.describe('Company Create', () => {
  let companyPage: CompanyPage

  test.beforeEach(async ({ page }) => {
    companyPage = new CompanyPage(page)
    await companyPage.goto()
  })

  test('should open the create dialog when Add Company is clicked', async ({ page }) => {
    await companyPage.openCreateDialog()
    await expect(page.getByRole('heading', { name: 'Add Company' })).toBeVisible()
  })

  test('should close the dialog on Escape key', async ({ page }) => {
    await companyPage.openCreateDialog()
    await page.keyboard.press('Escape')
    await companyPage.expectDialogClosed()
  })

  test('should show validation error when form is submitted empty', async () => {
    await companyPage.openCreateDialog()
    await companyPage.submitCreateForm()
    await companyPage.expectFieldError('Name must be at least 2 characters')
    await companyPage.expectDialogOpen()
  })

  test('should show validation error when name is a single character', async () => {
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: 'A' })
    await companyPage.submitCreateForm()
    await companyPage.expectFieldError('Name must be at least 2 characters')
    await companyPage.expectDialogOpen()
  })

  test('should show validation error for an invalid email format', async () => {
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: 'Valid Company', email: 'not-an-email' })
    await companyPage.submitCreateForm()
    await companyPage.expectFieldError('Enter a valid email')
    await companyPage.expectDialogOpen()
  })

  test('should create a company with only the required name field', async () => {
    const name = `E2E Min ${uniqueSuffix}`
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company created successfully')
    await companyPage.expectRowExists(name)
  })

  test('should create a company with all fields filled', async () => {
    const name = `E2E Full ${uniqueSuffix}`
    const slug = `e2e-full-${uniqueSuffix}`
    const email = `e2e-${uniqueSuffix}@test.example.com`
    const phone = `+1${Date.now().toString().slice(-9)}`

    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name, slug, email, phone })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company created successfully')
    await companyPage.expectRowExists(name)
  })
})
