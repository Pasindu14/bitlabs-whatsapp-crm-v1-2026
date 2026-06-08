import { test, expect } from '@playwright/test'
import { ContactPage } from '../../pages/contact.page'

test.describe('Contact List', () => {
  let contactPage: ContactPage

  test.beforeEach(async ({ page }) => {
    contactPage = new ContactPage(page)
    await contactPage.goto()
  })

  test('should display the page heading', async ({ page }) => {
    await expect(page.getByRole('heading', { name: 'Contacts' })).toBeVisible()
  })

  test('should render the data table', async () => {
    await expect(contactPage.table).toBeVisible()
  })

  test('should display all column headers', async ({ page }) => {
    await expect(page.getByRole('columnheader', { name: 'Contact' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Active' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Created' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Actions' })).toBeVisible()
  })

  test('should display the search input', async () => {
    await expect(contactPage.searchInput).toBeVisible()
  })

  test('should display the Add Contact button', async () => {
    await expect(contactPage.addButton).toBeVisible()
  })

  test('should filter the table when searching', async () => {
    await contactPage.search('xyzNonExistentContact999')
    await expect(contactPage.table).toBeVisible()
  })

  test('should repopulate the table after clearing search', async () => {
    await contactPage.search('xyzNonExistentContact999')
    await contactPage.clearSearch()
    await expect(contactPage.table).toBeVisible()
  })
})
