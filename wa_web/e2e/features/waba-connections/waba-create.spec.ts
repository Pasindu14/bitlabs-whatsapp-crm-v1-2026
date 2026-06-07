import { test, expect } from '@playwright/test'
import { WabaConnectionPage } from '../../pages/waba-connection.page'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = Date.now().toString(36)
const companyName = `E2E WC Co ${uniqueSuffix}`
// Phone Number IDs must be unique platform-wide; prefix '10' ensures no collision with other spec files
const phoneBase = `10${Date.now().toString().slice(-10)}`

test.describe.serial('WABA Connection Create', () => {
  test('setup: create a prerequisite active company', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: companyName })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company created successfully')
    await companyPage.expectRowExists(companyName)
  })

  test('should open the create dialog when Add Connection is clicked', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await expect(page.getByRole('heading', { name: 'Add WABA Connection' })).toBeVisible()
  })

  test('should close the dialog on Escape key', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await page.keyboard.press('Escape')
    await wabaPage.expectDialogClosed()
  })

  test('should show validation errors when form is submitted empty', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await wabaPage.submitCreateForm()
    await wabaPage.expectFieldError('Select a company')
    await wabaPage.expectDialogOpen()
  })

  test('should show validation error when company is selected but other required fields are empty', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await wabaPage.fillWabaConnectionForm({ companyName })
    await wabaPage.submitCreateForm()
    await wabaPage.expectFieldError('Phone number id is required')
    await wabaPage.expectDialogOpen()
  })

  test('should show validation error when phone and waba are filled but token is missing', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await wabaPage.fillWabaConnectionForm({
      companyName,
      phoneNumberId: `${phoneBase}1`,
      wabaId: `w${phoneBase}1`,
    })
    await wabaPage.submitCreateForm()
    await wabaPage.expectFieldError('Access token is required')
    await wabaPage.expectDialogOpen()
  })

  test('should create a WABA connection with required fields only', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await wabaPage.fillWabaConnectionForm({
      companyName,
      phoneNumberId: `${phoneBase}2`,
      wabaId: `w${phoneBase}2`,
      accessToken: `EAAG_test_${uniqueSuffix}_2`,
    })
    await wabaPage.submitCreateForm()
    await wabaPage.expectDialogClosed()
    await wabaPage.expectSuccessToast('WABA connection created successfully')
    await wabaPage.expectRowExists(`${phoneBase}2`)
  })

  test('should create a WABA connection with all fields filled', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await wabaPage.fillWabaConnectionForm({
      companyName,
      phoneNumberId: `${phoneBase}3`,
      wabaId: `w${phoneBase}3`,
      displayPhoneNumber: '+1 555 0199',
      accessToken: `EAAG_test_${uniqueSuffix}_3`,
      status: 'Connected',
    })
    await wabaPage.submitCreateForm()
    await wabaPage.expectDialogClosed()
    await wabaPage.expectSuccessToast('WABA connection created successfully')
    await wabaPage.expectRowExists(`${phoneBase}3`)
  })

  test('should reject a duplicate phone number id', async ({ page }) => {
    // Try to create a second connection with the same phone number id
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await wabaPage.fillWabaConnectionForm({
      companyName,
      phoneNumberId: `${phoneBase}2`, // already created above
      wabaId: `w${phoneBase}dup`,
      accessToken: `EAAG_test_${uniqueSuffix}_dup`,
    })
    await wabaPage.submitCreateForm()
    // Dialog must stay open (API returns a conflict error)
    await wabaPage.expectDialogOpen()
  })
})
