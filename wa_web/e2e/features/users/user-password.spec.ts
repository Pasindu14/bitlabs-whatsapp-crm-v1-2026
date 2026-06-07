import { test, expect } from '@playwright/test'
import { UserPage } from '../../pages/user.page'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = `p${Date.now().toString(36)}`
const companyName = `E2E Pwd Co ${uniqueSuffix}`
const userEmail = `e2e-pwd-${uniqueSuffix}@test.com`

test.describe.serial('User Reset Password', () => {
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
      fullName: `E2E Pwd User ${uniqueSuffix}`,
      email: userEmail,
      password: 'OldPass123!',
    })
    await userPage.submitCreateForm()
    await userPage.expectDialogClosed()
    await userPage.expectSuccessToast('User created successfully')
  })

  test('should open the Reset Password dialog from the actions menu', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickResetPassword(userEmail)
    await expect(page.getByRole('heading', { name: 'Reset Password' })).toBeVisible()
  })

  test('should close the Reset Password dialog on Escape', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickResetPassword(userEmail)
    await page.keyboard.press('Escape')
    await userPage.expectDialogClosed()
  })

  test('should show validation error when new password is too short', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickResetPassword(userEmail)
    await userPage.fillResetPasswordForm('short')
    await userPage.submitResetPasswordForm()
    await userPage.expectFieldError('Password must be at least 8 characters')
    await userPage.expectDialogOpen()
  })

  test('should reset the password successfully', async ({ page }) => {
    const userPage = new UserPage(page)
    await userPage.goto()
    await userPage.clickResetPassword(userEmail)
    await userPage.fillResetPasswordForm('NewSecure456!')
    await userPage.submitResetPasswordForm()
    await userPage.expectDialogClosed()
    await userPage.expectSuccessToast('Password reset successfully')
  })
})
