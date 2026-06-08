import { test, expect } from '@playwright/test'
import { ContactPage } from '../../pages/contact.page'

const uniqueSuffix = `s${Date.now().toString(36)}`
const phone = `949${Date.now().toString().slice(-8)}`

test.describe.serial('Contact Status Toggle', () => {
  test('setup: create a contact (starts Active)', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await contactPage.fillContactForm({
      name: `E2E Status ${uniqueSuffix}`,
      phone,
    })
    await contactPage.submitCreateForm()
    await contactPage.expectDialogClosed()
    await contactPage.expectSuccessToast('Contact added successfully')
    await contactPage.expectRowStatus(phone, 'Active')
  })

  test('should show Edit and Deactivate in the actions menu for an active contact', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openRowActions(phone)
    await expect(page.getByRole('menuitem', { name: 'Edit' })).toBeVisible()
    await expect(page.getByRole('menuitem', { name: 'Deactivate' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show the deactivate confirmation dialog', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.clickDeactivate(phone)
    await expect(page.getByRole('heading', { name: 'Deactivate contact?' })).toBeVisible()
  })

  test('should cancel deactivation and keep the contact Active', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.clickDeactivate(phone)
    await contactPage.cancelAlert()
    await contactPage.expectRowStatus(phone, 'Active')
  })

  test('should deactivate the contact and show Inactive badge', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.clickDeactivate(phone)
    await contactPage.confirmAlertAction('Deactivate')
    await contactPage.expectSuccessToast('Contact deactivated successfully')
    await contactPage.expectRowStatus(phone, 'Inactive')
  })

  test('should show Activate option for an inactive contact', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openRowActions(phone)
    await expect(page.getByRole('menuitem', { name: 'Activate' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show the activate confirmation dialog', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.clickActivate(phone)
    await expect(page.getByRole('heading', { name: 'Activate contact?' })).toBeVisible()
  })

  test('should activate the contact and show Active badge', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.clickActivate(phone)
    await contactPage.confirmAlertAction('Activate')
    await contactPage.expectSuccessToast('Contact activated successfully')
    await contactPage.expectRowStatus(phone, 'Active')
  })
})
