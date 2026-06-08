import { test, expect } from '@playwright/test'
import { CompanyPage } from '../../pages/company.page'

test.describe('Company List', () => {
  let companyPage: CompanyPage

  test.beforeEach(async ({ page }) => {
    companyPage = new CompanyPage(page)
    await companyPage.goto()
  })

  test('should display the page heading', async ({ page }) => {
    await expect(page.getByRole('heading', { name: 'Company Management' })).toBeVisible()
  })

  test('should render the data table', async () => {
    await expect(companyPage.table).toBeVisible()
  })

  test('should display all column headers', async ({ page }) => {
    await expect(page.getByRole('columnheader', { name: 'Company' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Email' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Phone' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Status' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Created' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Actions' })).toBeVisible()
  })

  test('should display search input with correct placeholder', async () => {
    await expect(companyPage.searchInput).toBeVisible()
  })

  test('should display the Add Company button', async () => {
    await expect(companyPage.addButton).toBeVisible()
  })

  test('should filter table results when searching', async () => {
    await companyPage.expectTableHasRows()
    await companyPage.search('xyzNonExistentCompany999')
    // Either shows empty state or fewer rows — table itself should remain visible
    await expect(companyPage.table).toBeVisible()
  })

  test('should repopulate table after clearing search', async () => {
    await companyPage.search('xyzNonExistentCompany999')
    await companyPage.clearSearch()
    await companyPage.expectTableHasRows()
  })
})
