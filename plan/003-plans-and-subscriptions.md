# Plan 003 — Plans & Subscriptions (PRD Phase 3.1, **Stripe deferred**)

PRD **Phase 3.1 (Plans & quotas)** only — *no Stripe in this slice* (3.2 Checkout, 3.3 webhooks,
3.5 invoices are explicitly out of scope here). Mirror the **WABA Connections** feature exactly
(Entity → Service → Controller → DTOs on the API; `schema → actions → hooks → store → components`
on the web).

Two decisions locked in for this slice:
- **Assignment = super-admin, manual.** No self-serve, no payment. The super-admin picks a plan
  for a company via a guarded endpoint/UI (same plane as connecting a WABA).
- **Include a basic gate stub.** Beyond the data model we add a minimal
  `[RequireActiveSubscription]` filter + quota check and wire it onto the **existing**
  `MessagesController` send endpoint as the proof. The full gate (PRD 3.4) hardens later.

> ⚠️ **Open item — the pasted "#3 (+4 lines)" requirement never reached me.** This plan is built
> from PRD 3.1 + the two scope answers above. If those 4 lines added anything (the `TokenPurchase`
> permission already in `Permission.cs` hints at **message credits / top-ups**, which 3.1 does not
> mention), paste them and I'll fold a STEP 5 in. Nothing below assumes them.

---

## The mental model — 2 tables, different planes

| Table | Plane | Plain English | Fields (kept minimal) |
|---|---|---|---|
| **Plan** | **Platform** (like `Company`, **not** tenant-scoped) | The shared price-list of tiers every company can be put on. | `Name`, `MonthlyMessageQuota`, `FeatureFlags`, `Price`, `IsActive` |
| **Subscription** | **Tenant** (`: ITenantEntity`, query-filtered) | *"This company is on this plan, with this much used this period."* | `CompanyId`, `PlanId`, `Status`, `CurrentPeriodStart`, `CurrentPeriodEnd`, `MessagesUsedThisPeriod` |

Why split planes:
- A **Plan** is a catalog row shared across all tenants — it carries **no `CompanyId`** and **no
  query filter**, exactly like `Company`. Only the super-admin writes it.
- A **Subscription** is the per-company link onto a plan. It is a normal tenant entity: carries
  `CompanyId`, gets auto-stamped by `AuditInterceptor`, and is hidden cross-tenant by the global
  query filter (mirror `WabaConnection`).

> A company holds **at most one active subscription**. "Change plan" = update the existing row's
> `PlanId` (+ reset the period). No Stripe ids anywhere.

---

## What we deliberately DROP vs the PRD's 3.1 entity (because no Stripe)

| PRD field | Decision |
|---|---|
| `Plan.PriceId` (Stripe price) | **Dropped.** Replaced with a plain `Price` (decimal) + `Currency` for display only. |
| `Subscription.StripeSubscriptionId` | **Dropped.** Assignment is manual; no external id to mirror. |
| `Subscription.Status` values | Local enum only: `Active`, `Inactive`, `Trialing`, `Cancelled`. No `PastDue` (that's a Stripe-payment concept — add in 3.3). |

`FeatureFlags` is modelled as a Postgres `text[]` column (same pattern as `User.Permissions`),
holding permission-style keys from `Features/Auth/Permission.cs` that the plan unlocks. Reusing that
catalog means the future gate can map *plan → allowed capabilities* with zero new vocabulary.

---

## Rules (baked in — same conventions as the rest of the app)

| Topic | Decision |
|---|---|
| Who manages plans | **SuperAdmin only** — `[Authorize(Roles = "SuperAdmin")]`, exactly like `WabaConnectionsController`. |
| Who assigns a subscription | **SuperAdmin only**, by `companyId` (super-admin plane — `CompanyId` is a *parameter* here, not from JWT, same as WABA create). |
| Who reads their own subscription | Any logged-in **company user** (`[Authorize]`) reads **their** current subscription — `CompanyId` from JWT, global filter does the isolation. Never send `CompanyId`. |
| Company scoping | `Subscription.CompanyId` auto-stamped by `AuditInterceptor`; never trust it from the client on tenant-facing reads. |
| Soft delete | `IsActive` hides; status transitions are explicit (`Active`/`Inactive`). Mirror `WabaConnection` (IsActive left OUT of the query filter so super-admin sees deactivated rows). |
| One-per-company | Enforce a **unique filtered index on `Subscription.CompanyId` where `Status = Active`** so a company can't hold two active subscriptions. |

---

## Build in 4 small steps — each one works on its own

### STEP 1 — `Plan` catalog (platform-level, super-admin CRUD)
> Goal: define the tiers. This alone delivers **"Plans defined"** from PRD 3.1.

**API** (`Features/Plans/`)
1. `Entities/Plan.cs` — `: BaseEntity` (platform-level, **no `ITenantEntity`**, no `CompanyId`):
   `Name`, `int MonthlyMessageQuota`, `decimal Price`, `string Currency` (default `"USD"`),
   `List<string> FeatureFlags`.
2. DbContext — add `DbSet<Plan> Plans`; config block: `Name` `IsRequired().HasMaxLength(120)` +
   **unique index on `Name`** (platform-unique, like `Company.Name`); `Price` `HasColumnType("numeric(12,2)")`;
   `FeatureFlags` as `text[]` with `HasDefaultValueSql("'{}'::text[]")` (copy the `User.Permissions`
   mapping). **No query filter** (platform catalog, visible to all).
3. DTOs (records, `[Required]` on params directly — never `[property:]`, per house rule):
   `CreatePlanRequest(Name, MonthlyMessageQuota, Price, Currency?, FeatureFlags?)`,
   `UpdatePlanRequest(…)`,
   `PlanResponse(Id, Name, MonthlyMessageQuota, Price, Currency, FeatureFlags, IsActive, CreatedAt)`.
   Validate `FeatureFlags` against `Permission.All` (reject unknown keys → `ValidationException`).
4. `IPlanService` / `PlanService` — `GetPagedAsync` (search ILike on `Name`), `GetByIdAsync`,
   `CreateAsync` (duplicate name → `ConflictException("PLAN_NAME_DUPLICATE", …)`), `UpdateAsync`,
   `Activate/DeactivateAsync`. Same shape as `WabaConnectionService`.
5. `Controllers/PlansController.cs` — `[Route("plans")] [Authorize(Roles = "SuperAdmin")]`:
   GET (page/pageSize/search/sortBy/sortOrder) · GET `{id:guid}` · POST (201) · PUT `{id:guid}` ·
   POST `{id}/activate` · POST `{id}/deactivate`. Use `ResponseHelper`. Copy `WabaConnectionsController` verbatim.
6. Program.cs — register `IPlanService` → `PlanService`.
7. `DataSeeder` — add `SeedPlansAsync`: if no plans exist, seed `Free` (1 000 / month, price 0),
   `Starter`, `Pro` with sensible quotas + feature-flag sets. Call it after `SeedSuperAdminAsync`.
8. Migration — `dotnet ef migrations add AddPlans` → `database update`.

**Web** (`features/plans/`, copy `waba-connections/`) — super-admin pages
9. Full 5-layer feature + page `app/(protected)/superadmin/plans/page.tsx`: searchable table +
   create/edit dialog (Name, MonthlyMessageQuota, Price, Currency, FeatureFlags multi-select sourced
   from the permission catalog) with submit spinner. Sidebar link under the super-admin group.

**Done when:** super-admin creates/edits/deactivates plans; the seeded tiers appear; non-super-admin gets 403.

---

### STEP 2 — `Subscription` (tenant entity, super-admin assigns)
> Goal: put a company onto a plan. Delivers **"a company can hold a subscription record"** (PRD 3.1 Done-when).

**API** (`Features/Subscriptions/`)
1. `Entities/Subscription.cs` — `: BaseEntity, ITenantEntity`:
   `Guid CompanyId`, `Guid PlanId`, `SubscriptionStatus Status` (enum `Active/Inactive/Trialing/Cancelled`),
   `DateTime CurrentPeriodStart`, `DateTime CurrentPeriodEnd`, `int MessagesUsedThisPeriod`,
   nav `Company Company`, `Plan Plan`.
2. DbContext — `DbSet<Subscription>`; config: `Status` `HasConversion<string>().HasMaxLength(20)`;
   FKs to `Company` and `Plan` (`OnDelete(Restrict)`); `HasIndex(CompanyId)`;
   **unique filtered index** `HasIndex(x => x.CompanyId).IsUnique().HasFilter("\"Status\" = 'Active'")`
   (one active sub per company); tenant query filter
   `x => _tenant.IsSuperAdmin || x.CompanyId == _tenant.CompanyId` (mirror `WabaConnection`).
3. DTOs — `AssignSubscriptionRequest(CompanyId, PlanId, PeriodDays?)` *(super-admin)*,
   `ChangePlanRequest(PlanId, ResetPeriod?)`,
   `SubscriptionResponse(Id, CompanyId, CompanyName, PlanId, PlanName, Status, MonthlyMessageQuota,
   MessagesUsedThisPeriod, CurrentPeriodStart, CurrentPeriodEnd, IsActive, CreatedAt)`.
4. `ISubscriptionService` / `SubscriptionService`:
   - `AssignAsync(req)` — validate company + plan exist; **deactivate/cancel any existing active sub**
     for that company, then create a new `Active` one. `CurrentPeriodStart = now`,
     `CurrentPeriodEnd = now + (PeriodDays ?? 30)`, `MessagesUsedThisPeriod = 0`.
     (Super-admin call: `CompanyId` comes from the request, like `WabaConnection.CreateAsync`.)
   - `ChangePlanAsync(companyId, req)` — point the active sub at a new `PlanId`, optionally reset period.
   - `GetForCompanyAsync(companyId)` — single active sub for a company (super-admin path).
   - `GetMineAsync()` — **tenant path**: read the caller's own active sub via the query filter
     (no `CompanyId` param). Returns a "no active subscription" shaped response if none.
   - `GetPagedAsync(...)` — super-admin list across companies (filter bypassed for super-admin).
   - `CancelAsync(companyId)` — set `Status = Cancelled`, `IsActive = false`.
5. Controllers:
   - `Controllers/SubscriptionsController.cs` — `[Route("subscriptions")] [Authorize(Roles = "SuperAdmin")]`:
     GET (paged) · GET `company/{companyId:guid}` · POST `assign` · PUT `company/{companyId:guid}/plan`
     (change plan) · POST `company/{companyId:guid}/cancel`.
   - `Controllers/MySubscriptionController.cs` — `[Route("my-subscription")] [Authorize]`:
     GET `/` → `GetMineAsync()` (the company admin's own plan + quota usage).
6. Program.cs — register `ISubscriptionService`.
7. Migration — `dotnet ef migrations add AddSubscriptions` → `database update`.

**Web**
8. Super-admin: on the companies table add an **"Assign plan"** row action → dialog (plan picker +
   period) → calls `assign`. Show current plan/status in a column.
9. Tenant: `features/subscription/` read-only view + a "Current Plan & Usage" card on
   `app/(protected)/dashboard/page.tsx` (or a `billing/` page) consuming `GET /my-subscription`
   (plan name, quota, used, period end, a usage bar).

**Done when:** super-admin assigns `Pro` to a company; that company's admin sees their plan + quota
at `/my-subscription`; a second active assignment to the same company replaces the first (unique
filtered index holds); company B can't see company A's subscription.

---

### STEP 3 — Gate stub (`[RequireActiveSubscription]` + quota check)
> Goal: a minimal, real gate proving the wiring. Hardened into the full PRD 3.4 middleware later.

**API** (`Common/Subscriptions/` — cross-cutting, not a feature)
1. `ISubscriptionGate` / `SubscriptionGate` (scoped): `Task EnsureCanSendAsync(CancellationToken)`:
   - Resolve the caller's active subscription via the query filter (uses `ITenantContext`).
   - **No active sub** → throw `BusinessRuleException("SUBSCRIPTION_INACTIVE", …)`.
   - `Status != Active` or `CurrentPeriodEnd < now` → same `SUBSCRIPTION_INACTIVE`.
   - `MessagesUsedThisPeriod >= Plan.MonthlyMessageQuota` → `BusinessRuleException("QUOTA_EXCEEDED", …)`.
   - These map to clean `ApiError`s via the existing `GlobalExceptionMiddleware` (422). ✅
2. `RequireActiveSubscriptionAttribute : Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter` —
   resolves `ISubscriptionGate` from DI and calls `EnsureCanSendAsync` before the action runs.
   (Attribute filter, not middleware, so it composes per-endpoint like `[Authorize]`.)
3. Program.cs — register `ISubscriptionGate`.
4. **Wire it onto the existing send path:** put `[RequireActiveSubscription]` on the send action in
   `Features/Messages/Controllers/MessagesController.cs`. Optionally, in `MessageService` send,
   increment `MessagesUsedThisPeriod` on the active subscription after a successful send (so the
   quota actually moves) — keep it best-effort; the authoritative metering lands with Phase 6.

**Web**
5. Surface `SUBSCRIPTION_INACTIVE` / `QUOTA_EXCEEDED` error codes in the send UI as a clear toast
   ("Subscription inactive" / "Monthly quota reached"). Reuse the existing `ApiError` handling.

**Done when:** sending from a company with no/inactive sub or an exhausted quota returns the right
`ApiError` code and is blocked; a company with an `Active`, in-quota sub sends normally.

> **Boundary note:** this is the *stub*. PRD 3.4's real middleware (covering campaign endpoints,
> period rollover/reset, and a central policy) is a later plan — this just proves the gate path end-to-end.

---

### STEP 4 — Period reset (lightweight Hangfire job)
> Goal: quotas actually reset each period without Stripe's billing cycle to lean on.

**API** (`Infrastructure/Jobs/`)
1. `SubscriptionPeriodResetJob` (recurring, daily) — for every `Active` subscription whose
   `CurrentPeriodEnd <= now`: set `MessagesUsedThisPeriod = 0`,
   `CurrentPeriodStart = CurrentPeriodEnd`, `CurrentPeriodEnd += 30 days`. Mirror the existing
   `WabaHealthCheckJob` registration style. Runs with super-admin/system scope (filter bypass).
2. Register the recurring job next to the others.

**Done when:** a subscription past its period end rolls to a fresh period with usage back to 0,
unattended.

---

## Verify (whole feature)
- `dotnet build` clean; `tsc` clean; both migrations applied to Supabase.
- Seed runs: `Free`/`Starter`/`Pro` exist after first boot.
- Tenant isolation: company A never sees company B's subscription (super-admin sees all).
- One-active-sub invariant: assigning twice to one company keeps a single `Active` row (unique
  filtered index doesn't throw).
- Gate path: blocked when inactive/over-quota with the documented `ApiError` codes; allowed when active.
- Reset job: force a past `CurrentPeriodEnd`, run the job, confirm usage resets.

---

## How this feeds later phases
- **PRD 3.4 (Subscription gate middleware):** STEP 3's `ISubscriptionGate` is the seam — promote the
  attribute to a central policy and extend it across campaign/send endpoints.
- **PRD 3.2/3.3/3.5 (Stripe):** when Stripe lands, add `Plan.StripePriceId` +
  `Subscription.StripeSubscriptionId` (encrypted) and a webhook sync that flips `Status` —
  the local model already holds the shape; only the source-of-truth changes from "super-admin sets it"
  to "Stripe mirrors it."
- **PRD 6.x (Send pipeline):** the real per-message metering replaces STEP 3's best-effort increment;
  `MessagesUsedThisPeriod` is already the counter it writes to.
