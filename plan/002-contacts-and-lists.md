# Plan 002 — Contacts & Contact Lists (create lists, import into a list, add contacts with no list)

Mirror the **WABA Connections** feature exactly (Entity → Service → Controller → DTOs on the
API; `schema → actions → hooks → store → components` on the web). This is PRD **Phase 5.3
(Contacts & lists)** + **5.4 (Excel import)**, kept as small as possible.

---

## The mental model — just 3 tables

| Table | Plain English | Fields (kept minimal) |
|---|---|---|
| **Contact** | One person. | `Phone`, `Name` |
| **ContactList** | A named group, e.g. "VIP", "Newsletter". | `Name`, `Description?` |
| **ContactListMember** | A link row that says *"this contact is in this list."* | `ContactId`, `ContactListId` |

A contact can be in **0, 1, or many** lists. That single design covers all three things you asked for:

- **Add a contact with NO list** → create a `Contact`, write no member rows. ✅
- **Create a contact list directly** → create a `ContactList` (empty). ✅
- **Import contacts into a list** → create `Contact`s + one `ContactListMember` each. ✅

> A `Contact` lives on its own. A list is just a grouping on top. Deleting a contact from a list
> (removing the member row) does **not** delete the contact.

---

## Rules (baked in — same conventions as the rest of the app)

| Topic | Decision |
|---|---|
| Company scoping | **Never send `CompanyId`.** It's auto-stamped from the JWT by `AuditInterceptor`, and the global query filter hides other companies' rows. (Same as every tenant entity.) |
| Who can use it | Any logged-in **company user** (`[Authorize]`). *(Later: gate behind the `ContactList` permission flag from PRD §4 — not now.)* |
| Phone uniqueness | **Unique per company** → composite index `(CompanyId, Phone)`. Two companies can hold the same number; one company cannot have duplicates. This is what makes re-import safe (it **updates** the existing contact instead of duplicating). |
| Soft delete | Deactivate hides, never physically deletes (`IsActive`) — same as `WabaConnection`. |
| Phone format | Store digits in E.164-ish form (e.g. `94771234567`). Basic validation only (digits, length). |

**Deliberately left OUT of v1 (add later, noted in PRD 5.3/5.4):** opt-in enforcement,
free-form `Attributes`/`Category`, and a column-mapping UI for import (we assume fixed columns
`Phone`, `Name`). Keeping these out is what keeps this simple.

---

## Build in 3 small steps — each one works on its own

### STEP 1 — Contacts (standalone)
> Goal: add and manage a single contact. This alone delivers **"add contacts without a list."**

**API** (`Features/Contacts/`)
1. `Entities/Contact.cs` — `: BaseEntity, ITenantEntity` with `Phone`, `Name`.
2. DbContext — `DbSet<Contact>`; config: `(CompanyId, Phone)` unique index; tenant query filter
   `c => _tenant.IsSuperAdmin || c.CompanyId == _tenant.CompanyId` (mirror `WabaConnection`).
3. DTOs — `CreateContactRequest(Phone, Name)`, `UpdateContactRequest(Phone, Name)`,
   `ContactResponse(Id, Phone, Name, IsActive, CreatedAt)`. `[Required]` on params directly.
4. `IContactService` / `ContactService` — `GetPagedAsync` (search ILike on Phone/Name),
   `GetByIdAsync`, `CreateAsync` (duplicate phone → `ConflictException("CONTACT_PHONE_DUPLICATE", …)`),
   `UpdateAsync`, `Activate/DeactivateAsync`. **Do not set `CompanyId`** — interceptor does it.
5. `Controllers/ContactsController.cs` — `[Route("contacts")] [Authorize]`: GET (page/pageSize/
   search/sortBy/sortOrder), GET `{id:guid}`, POST (201), PUT `{id:guid}`, POST `{id}/activate`,
   POST `{id}/deactivate`. Use `ResponseHelper`.
6. Program.cs — register `IContactService` → `ContactService`.
7. Migration — `dotnet ef migrations add AddContacts` → `database update`.

**Web** (`features/contacts/`, copy `waba-connections/`)
8. Full 5-layer feature + page `app/(protected)/contacts/page.tsx`: searchable table + create/edit
   dialog (Phone, Name) with submit spinner. Sidebar link "Contacts".

**Done when:** create a contact, see it in a searchable table, edit it, deactivate/reactivate it.

---

### STEP 2 — Lists + membership
> Goal: create a list, put contacts in it, view a list's contacts. Delivers **"create a contact
> list directly."**

**API** (`Features/ContactLists/`)
1. `Entities/ContactList.cs` (`Name`, `Description?`) and `Entities/ContactListMember.cs`
   (`ContactId`, `ContactListId`) — both `: BaseEntity, ITenantEntity`.
2. DbContext — two `DbSet`s; `(CompanyId, Name)` unique on lists; `(ContactListId, ContactId)`
   unique on members; tenant query filter on both.
3. DTOs — `CreateContactListRequest(Name, Description?)`, `UpdateContactListRequest(…)`,
   `ContactListResponse(Id, Name, Description, ContactCount, IsActive, CreatedAt)`,
   `AddContactsToListRequest(ContactIds[])` *(and/or `{ Phone, Name }` to create+add in one call)*.
4. `IContactListService` / `ContactListService` — paged list **with member count**, get, create,
   update, activate/deactivate, `AddContactsAsync`, `RemoveContactAsync` (deletes the member row
   only, keeps the contact).
5. `Controllers/ContactListsController.cs` — `[Route("contact-lists")] [Authorize]`: standard CRUD
   + `POST {id}/contacts` (add) + `DELETE {id}/contacts/{contactId}` (remove from list).
6. Extend Step-1 `ContactsController` GET with optional `?listId=` filter (contacts in a list).
7. Program.cs registration + migration `AddContactLists`.

**Web** (`features/contact-lists/`)
8. Lists table page `app/(protected)/contact-lists/page.tsx` (Name, contact count, actions).
9. List detail/drawer: its contacts + "Add contact to list" picker. On Contacts page: list filter
   + "Add to list" row action.

**Done when:** create a list, add existing contacts to it, remove one (contact survives), filter
contacts by list.

---

### STEP 3 — Excel import into a list
> Goal: bulk-fill a list from an `.xlsx`. `ClosedXML` is **already installed** — no new package.

**API**
1. `POST /api/v1/contact-lists/{id}/import` — accepts a multipart file.
2. Service `ImportAsync(listId, stream)`: parse with ClosedXML → expect columns **`Phone`, `Name`**
   (header row) → for each row: validate phone → **upsert** contact by `(CompanyId, Phone)` →
   ensure a `ContactListMember` exists → collect bad rows.
3. Return a report DTO: `{ totalRows, imported, updated, skipped: [{ row, phone, reason }] }`.

**Web**
4. On a list page: an **Import** button → file upload → show the valid/invalid report
   (e.g. "120 imported, 8 updated, 3 skipped" + the skipped rows).

**Done when:** a real file imports, duplicate numbers upsert (no duplicates), invalid rows are
reported clearly, and every valid row lands in the chosen list.

---

## Verify (whole feature)
- `dotnet build` clean; `tsc` clean; both migrations applied.
- Tenant isolation: company A never sees company B's contacts or lists.
- Smoke test the full path: create list → add a contact directly (no list) → add a contact to the
  list → import an `.xlsx` into the list → re-import the same file and confirm **no duplicates**.

---

## How this feeds "send a first message" (next plan, 003)
Once contacts exist, the chat/send slice (PRD Phase 6.2) is tiny: pick a contact → `MetaWhatsAppClient`
POSTs to `/{phoneNumberId}/messages` using the company's stored WABA token → return the `wamid`.
Contacts are the missing input; this plan unblocks it.
