import { test, expect } from '@playwright/test'
import { ContactPage } from '../../pages/contact.page'

const uniqueSuffix = `c${Date.now().toString(36)}`
const phone = `947${Date.now().toString().slice(-8)}`

test.describe.serial('Contact Create', () => {
  test('should open the create dialog when Add Contact is clicked', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await expect(page.getByRole('heading', { name: 'Add Contact' })).toBeVisible()
  })

  test('should close the dialog on Escape key', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await page.keyboard.press('Escape')
    await contactPage.expectDialogClosed()
  })

  test('should show validation error when form is submitted empty', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await contactPage.submitCreateForm()
    await contactPage.expectFieldError('Name is required')
    await contactPage.expectDialogOpen()
  })

  test('should show validation error when name is filled but phone is empty', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await contactPage.fillContactForm({ name: `E2E Contact ${uniqueSuffix}` })
    await contactPage.submitCreateForm()
    await contactPage.expectFieldError('Phone number is required')
    await contactPage.expectDialogOpen()
  })

  test('should show validation error for a phone number that is too short', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await contactPage.fillContactForm({ name: `E2E Contact ${uniqueSuffix}`, phone: '123' })
    await contactPage.submitCreateForm()
    await contactPage.expectFieldError('Phone number is required')
    await contactPage.expectDialogOpen()
  })

  test('should create a contact successfully', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await contactPage.fillContactForm({
      name: `E2E Contact ${uniqueSuffix}`,
      phone,
    })
    await contactPage.submitCreateForm()
    await contactPage.expectDialogClosed()
    await contactPage.expectSuccessToast('Contact added successfully')
    await contactPage.expectRowExists(phone)
  })

  test('should reject a duplicate phone number', async ({ page }) => {
    const contactPage = new ContactPage(page)
    await contactPage.goto()
    await contactPage.openCreateDialog()
    await contactPage.fillContactForm({
      name: `E2E Duplicate ${uniqueSuffix}`,
      phone, // same phone as the contact created above
    })
    await contactPage.submitCreateForm()
    await contactPage.expectDialogOpen()
  })
})
