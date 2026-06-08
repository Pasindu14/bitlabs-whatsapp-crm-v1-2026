import { test, expect } from '@playwright/test'
import { WabaConnectionPage } from '../../pages/waba-connection.page'
import { CompanyPage } from '../../pages/company.page'

const uniqueSuffix = `s${Date.now().toString(36)}`
const companyName = `E2E WC Sts Co ${uniqueSuffix}`
const phoneBase = `30${Date.now().toString().slice(-10)}`
const phoneNumberId = `${phoneBase}1`

test.describe.serial('WABA Connection Status Toggle', () => {
  test('setup: create company and WABA connection (starts Active + Connected)', async ({ page }) => {
    const companyPage = new CompanyPage(page)
    await companyPage.goto()
    await companyPage.openCreateDialog()
    await companyPage.fillCompanyForm({ name: companyName })
    await companyPage.submitCreateForm()
    await companyPage.expectDialogClosed()
    await companyPage.expectSuccessToast('Company created successfully')

    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openCreateDialog()
    await wabaPage.fillWabaConnectionForm({
      companyName,
      phoneNumberId,
      wabaId: `w${phoneBase}`,
      accessToken: `EAAG_sts_${uniqueSuffix}`,
      status: 'Connected',
    })
    await wabaPage.submitCreateForm()
    await wabaPage.expectDialogClosed()
    await wabaPage.expectSuccessToast('WABA connection created successfully')
    await wabaPage.expectActiveStatus(phoneNumberId, 'Active')
    await wabaPage.expectConnectionStatus(phoneNumberId, 'Connected')
  })

  test('should show Deactivate option in actions menu for an active connection', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openRowActions(phoneNumberId)
    await expect(page.getByRole('menuitem', { name: 'Deactivate' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show the deactivate confirmation dialog', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickDeactivate(phoneNumberId)
    await expect(page.getByRole('heading', { name: 'Deactivate connection?' })).toBeVisible()
  })

  test('should cancel deactivation and keep the connection Active', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickDeactivate(phoneNumberId)
    await wabaPage.cancelAlert()
    await wabaPage.expectActiveStatus(phoneNumberId, 'Active')
  })

  test('should deactivate the connection — badge shows Inactive and status shows Disconnected', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickDeactivate(phoneNumberId)
    await wabaPage.confirmAlertAction('Deactivate')
    await wabaPage.expectSuccessToast('WABA connection deactivated successfully')
    await wabaPage.expectActiveStatus(phoneNumberId, 'Inactive')
    await wabaPage.expectConnectionStatus(phoneNumberId, 'Disconnected')
  })

  test('should show Activate option in actions menu for an inactive connection', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.openRowActions(phoneNumberId)
    await expect(page.getByRole('menuitem', { name: 'Activate' })).toBeVisible()
    await page.keyboard.press('Escape')
  })

  test('should show the activate confirmation dialog', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickActivate(phoneNumberId)
    await expect(page.getByRole('heading', { name: 'Activate connection?' })).toBeVisible()
  })

  test('should activate the connection — badge shows Active and status shows Connected', async ({ page }) => {
    const wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
    await wabaPage.clickActivate(phoneNumberId)
    await wabaPage.confirmAlertAction('Activate')
    await wabaPage.expectSuccessToast('WABA connection activated successfully')
    await wabaPage.expectActiveStatus(phoneNumberId, 'Active')
    await wabaPage.expectConnectionStatus(phoneNumberId, 'Connected')
  })
})
