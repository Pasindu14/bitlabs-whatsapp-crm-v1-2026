# Plan 001 — SuperAdmin User Management (create / update / activate / deactivate tenant users)

Mirror the **Companies** and **WABA Connections** features exactly. SuperAdmin opens one
global page, **selects an active company**, and creates a **tenant user** (default role
`CompanyAdmin`) for it. Same four-layer auth, same auto-refresh DataTable, same submit spinner,
same Company picker (active companies only).

> The `User` entity and `Users` table already exist (migration `AddUsers`). `queryKeys.users`
> already exists. We only **add a management vertical slice** around the existing entity, plus a
> Company FK migration.

---

## Decisions (baked in — change if you disagree)

| Topic | Decision |
|---|---|
| Who can manage | **SuperAdmin only** — one global page at `/superadmin/users`. |
| What gets created | **Tenant users.** Role dropdown limited to `CompanyAdmin` (default) and `Agent` — both require a company. `SuperAdmin` is **not** creatable here. |
| Company | **Required** on create (active companies only). Editable on update (parity with WABA). |
| Password | Required on create (min 8). On **edit** it's optional — blank = keep current. Stored as **BCrypt hash**, never returned. |
| Email | Required, unique platform-wide (DB already has unique index). |
| Response DTO | Never includes `PasswordHash`. Exposes `LastLoginAt`, `CompanyName`. |
| Company FK | Add `User.Company` nav + FK (`OnDelete: Restrict`, optional since SuperAdmin's CompanyId is null) → **one new migration**. |

---

## API tasks (`wa_api/wa_api/Features/Users/`)

1. **Entity tweak** — add `Company` navigation to `Features/Auth/User.cs`
   (`public Company? Company { get; set; }`), replacing the Phase-1.1 TODO comment.
2. **DTOs** (`Features/Users/Dtos/`):
   - `CreateUserRequest(CompanyId, FullName, Email, Password, Role?)` — `[Required]` on params
     directly (NOT `[property:]`). Password `[StringLength(128, MinimumLength = 8)]`.
   - `UpdateUserRequest(CompanyId, FullName, Email, Role, Password?)` — Password optional.
   - `UserResponse(Id, CompanyId, CompanyName, FullName, Email, Role, IsActive, LastLoginAt, CreatedAt)`.
3. **`IUserService` / `UserService`** — mirror `WabaConnectionService`:
   - `GetPagedAsync` — inline projection joining Company name; search ILike on
     `FullName` / `Email` / `Company.Name`; sort by name/email/createdAt; show all (active+inactive).
   - `GetByIdAsync` / `UpdateAsync` / `SetActiveAsync` use `.Include(u => u.Company)`.
   - `CreateAsync` — validate role ∈ {CompanyAdmin, Agent}; company exists **and is active**;
     email unique (`ConflictException("USER_EMAIL_DUPLICATE", …)`); `BCrypt.HashPassword`.
   - `UpdateAsync` — email uniqueness excludes self; replace password only if provided.
   - `ActivateAsync` / `DeactivateAsync` toggle `IsActive`.
4. **`Controllers/UsersController`** — `[Route("users")]`, `[Authorize(Roles = "SuperAdmin")]`;
   GET (page/pageSize/search/sortBy/sortOrder), GET `{id:guid}`, POST (201), PUT `{id:guid}`,
   POST `{id:guid}/activate`, POST `{id:guid}/deactivate`. Use `ResponseHelper`.
5. **DbContext** — add `User.Company` FK config (`HasOne(x => x.Company).WithMany()
   .HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict)`).
6. **Program.cs** — register `IUserService` → `UserService`.
7. **Migration** — `dotnet ef migrations add AddUserCompanyFk` → `database update` (Supabase).

## Web tasks (`wa_web/features/users/`)

8. `schema/user-schema.ts` — `USER_ROLES = ["CompanyAdmin","Agent"]`; create/update zod schemas
   (update: password optional via `.or(z.literal("").transform(() => undefined))`).
9. `types.ts` — `User` interface (+ index signature for ExportableData), `UserListParams`.
10. `services/user-service.ts` — `/api/v1/users` CRUD + activate/deactivate (idempotency key on create).
11. `actions/user-actions.ts` — `createAction`, all `requireAuth + requiredRole: "SuperAdmin"`.
12. `store/user-store.ts` — dialog store + selectors (create/edit/activate/deactivate).
13. `hooks/use-users.ts` — `useUserDataTable` (`.isQueryHook = true`), `useUser`,
    `useActiveCompanies` (reuse companies action), 4 mutations invalidating `queryKeys.users.all`.
14. `components/user-form.tsx` — Company picker (active only), FullName, Email, Password
    (type=password; edit label "leave blank to keep current"), Role dropdown; **submit spinner**.
15. `components/columns.tsx` — Name+email avatar, Company, Role badge, Active badge, LastLogin,
    Created, actions dropdown (Edit + Activate/Deactivate).
16. `components/user-dialogs.tsx` + `components/user-table.tsx` (export off, search on).
17. `app/(protected)/superadmin/users/page.tsx` — server guard `redirect("/unauthorized")`.
18. `components/app-sidebar.tsx` — add `{ title: "Users", url: "/superadmin/users" }` under Platform.

## Verify
19. `dotnet build` clean; `tsc` clean; migration applied; smoke-test create→edit→deactivate→activate.
