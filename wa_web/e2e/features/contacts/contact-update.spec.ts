import { test, expect } from '@playwright/test'
import { ContactPage } from '../../pages/contact.page'

const uniqueSuffix = `u${Date.now().toString(36)}`
const phone = `948${Date.now().toString().slice(-8)}`
const updatedName = `E2E Updated ${uniqueSuffix}`

test.describe.serial('Contact Update', () => {
  test('setup: create a contact to edit', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await contactPage.fillContactForm({
      name: `E2E Edit Me ${uniqueSuffix}`,
      phone,
    })
    await contactPage.submitCreateForm()
    await contactPage.expectDialogClosed()
    await contactPage.expectSuccessToast('Contact added successfully')
    await contactPage.expectRowExists(phone)
  })

  test('should open the edit dialog with pre-filled data', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.clickEdit(phone)
    await expect(page.getByRole('heading', { name: 'Edit Contact' })).toBeVisible()
    await expect(page.locator('[role="dialog"]:not([data-nextjs-dialog])').getByLabel('Phone Number', { exact: true })).not.toHaveValue('')
  })

  test('should show validation error when name is cleared', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.clickEdit(phone)
    await contactPage.fillContactForm({ name: '' })
    await contactPage.submitEditForm()
    await contactPage.expectFieldError('Name is required')
    await contactPage.expectDialogOpen()
  })

  test('should update the contact name successfully', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.clickEdit(phone)
    await contactPage.fillContactForm({ name: updatedName })
    await contactPage.submitEditForm()
    await contactPage.expectDialogClosed()
    await contactPage.expectSuccessToast('Contact updated successfully')
    await contactPage.expectRowExists(phone)
    const row = contactPage.getRow(phone)
    await expect(row).toContainText(updatedName)
  })
})
