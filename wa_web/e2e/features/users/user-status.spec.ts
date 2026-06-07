import { test, expect } from '@playwright/test'
import { UserPage } from '../../pages/user.page'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = `s${Date.now().toString(36)}`
const companyName = `E2E Sts Co ${uniqueSuffix}`
const userEmail = `e2e-sts-${uniqueSuffix}@test.com`

test.describe.serial('User Status Toggle', () => {
  test('setup: create company and user (starts Active)', async ({ page }) => {
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
      fullName: `E2E Status User ${uniqueSuffix}`,
      email: userEmail,
      password: 'TestPass123!',
    })
    await userPage.submitCreateForm()
    await userPage.expectDialogClosed()
    await userPage.expectSuccessToast('User created successfully')
    await userPage.expectRowStatus(userEmail, 'Active')
  })

  test('should show Edit and Reset Password in the actions menu', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openRowActions(userEmail)
    await expect(page.getByRole('menuitem', { name: 'Edit' })).toBeVisible()
    await expect(page.getByRole('menuitem', { name: 'Reset Password' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show Deactivate option for an active user', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openRowActions(userEmail)
    await expect(page.getByRole('menuitem', { name: 'Deactivate' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show the deactivate confirmation dialog', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickDeactivate(userEmail)
    await expect(page.getByRole('heading', { name: 'Deactivate user?' })).toBeVisible()
  })

  test('should cancel deactivation and keep the user Active', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickDeactivate(userEmail)
    await userPage.cancelAlert()
    await userPage.expectRowStatus(userEmail, 'Active')
  })

  test('should deactivate the user and show Inactive badge', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickDeactivate(userEmail)
    await userPage.confirmAlertAction('Deactivate')
    await userPage.expectSuccessToast('User deactivated successfully')
    await userPage.expectRowStatus(userEmail, 'Inactive')
  })

  test('should show Activate option for an inactive user', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.openRowActions(userEmail)
    await expect(page.getByRole('menuitem', { name: 'Activate' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show the activate confirmation dialog', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickActivate(userEmail)
    await expect(page.getByRole('heading', { name: 'Activate user?' })).toBeVisible()
  })

  test('should activate the user and show Active badge', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickActivate(userEmail)
    await userPage.confirmAlertAction('Activate')
    await userPage.expectSuccessToast('User activated successfully')
    await userPage.expectRowStatus(userEmail, 'Active')
  })
})
