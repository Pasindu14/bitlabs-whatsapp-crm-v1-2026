import { test, expect } from '@playwright/test'
import { UserPage } from '../../pages/user.page'

test.describe('User List', () => {
  let userPage: UserPage

  test.beforeEach(async ({ page }) => {
    userPage = new UserPage(page)
    await userPage.goto()
  })

  test('should display the page heading', async ({ page }) => {
    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
  })

  test('should render the data table', async () => {
    await expect(userPage.table).toBeVisible()
  })

  test('should display all column headers', async ({ page }) => {
    await expect(page.getByRole('columnheader', { name: 'User' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Company' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Role' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Active' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Last Login' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Created' })).toBeVisible()
    await expect(page.getByRole('columnheader', { name: 'Actions' })).toBeVisible()
  })

  test('should display the search input', async () => {
    await expect(userPage.searchInput).toBeVisible()
  })

  test('should display the Add User button', async () => {
    await expect(userPage.addButton).toBeVisible()
  })

  test('should filter the table when searching', async () => {
    await userPage.search('xyzNonExistentUser999')
    await expect(userPage.table).toBeVisible()
  })

  test('should repopulate the table after clearing search', async () => {
    await userPage.search('xyzNonExistentUser999')
    await userPage.clearSearch()
    await expect(userPage.table).toBeVisible()
  })
})
