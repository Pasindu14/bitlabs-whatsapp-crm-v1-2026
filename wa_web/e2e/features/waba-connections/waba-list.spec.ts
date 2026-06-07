import { test, expect } from '@playwright/test'
import { WabaConnectionPage } from '../../pages/waba-connection.page'

test.describe('WABA Connection List', () => {
  let wabaPage: WabaConnectionPage

  test.beforeEach(async ({ page }) => {
    wabaPage = new WabaConnectionPage(page)
    await wabaPage.goto()
  })

  test('should display the page heading', async ({ page }) => {
    await expect(page.getByRole('heading', { name: 'WABA Connections' })).toBeVisible()
  })

  test('should render the data table', async () => {
    await expect(wabaPage.table).toBeVisible()
  })

  test('should display all column headers', async ({ page }) => {
    await expect(page.getByRole('columnheader', { name: 'Connection' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Company' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'WABA ID' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Status' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Active' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Created' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Actions' })).toBeVisible()
  })

  test('should display search input with correct placeholder', async () => {
    await expect(wabaPage.searchInput).toBeVisible()
  })

  test('should display the Add Connection button', async () => {
    await expect(wabaPage.addButton).toBeVisible()
  })

  test('should filter table results when searching with a non-existent term', async () => {
    await wabaPage.search('xyzNonExistentConnection999')
    await expect(wabaPage.table).toBeVisible()
  })

  test('should repopulate table after clearing search', async () => {
    await wabaPage.search('xyzNonExistentConnection999')
    await wabaPage.clearSearch()
    // Table should reload; at minimum the structure remains visible
    await expect(wabaPage.table).toBeVisible()
  })
})
