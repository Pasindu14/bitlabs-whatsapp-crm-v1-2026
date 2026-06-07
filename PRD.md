# WhatsApp Business SaaS — Product Requirements Document

**Version:** 1.0
**Type:** Multi-tenant WhatsApp Business messaging platform
**Build model:** Solo full-stack, dependency-ordered phases

---

## 1. Overview

A multi-tenant SaaS platform that lets multiple companies run WhatsApp Business messaging — bulk campaigns, scheduled messaging, a live-chat inbox, contact management, and template management — each company fully isolated, billed via Stripe subscriptions, sending through Meta's official WhatsApp Cloud API.

### 1.1 Goals
- Multiple isolated companies on one platform (row-level multi-tenancy).
- Compliant, non-bannable messaging via official Meta Cloud API.
- Subscription-gated feature access with usage quotas.
- Reliable background processing for campaigns and webhooks.

### 1.2 Non-Goals (v1)
- No unofficial WhatsApp libraries.
- No mobile app (web portal only).
- No voice/calling features.
- **No self-serve client onboarding in v1** — WABAs are connected manually (4.2a); Embedded Signup (4.2b) is deferred to a later phase.

---

## 2. Tech Stack

| Layer | Tech | Host |
|---|---|---|
| Frontend | Next.js 16 App Router, TypeScript, TanStack Query v5, Zustand v5, shadcn/ui | Vercel |
| Web auth/session | NextAuth v5 (session wrapper; tokens issued by API) | Vercel |
| Backend API | .NET 8 ASP.NET Core, vertical slice, EF Core | Docker on Contabo VPS |
| Background jobs | Hangfire | Same container/process |
| DB | PostgreSQL | Supabase (managed, **no** Supabase Auth) |
| Cache / queue / locks | Redis | Local on Contabo |
| Realtime (inbox) | SignalR (in .NET API) | Docker on Contabo VPS |
| Payments | Stripe | — |
| Messaging | Meta WhatsApp Cloud API | — |

### 2.1 Carried over from SFA (reuse as-is)
`ApiResponse<T>` envelope · `ApiError` · vertical slice (Controller → Service → Repository) · soft delete via `IsActive` · camelCase contracts · `/api/v1/` versioning · `AsNoTracking()` on reads · `CancellationToken` on async · Redis distributed locks · `IdempotencyKey` · `AuditInterceptor` · GlobalExceptionMiddleware · CorrelationId · Serilog · web 5-layer feature modules (`schema → actions → hooks → store → components`).

### 2.2 New for this project (not in SFA)
Multi-tenancy (`CompanyId` + global query filter) · encrypted per-company secrets · subscription gate middleware · Meta Cloud API integration · Embedded Signup · 24-hour window state machine · webhook signature verification · token-bucket rate limiter · media handling · SignalR realtime inbox.

---

## 3. Core Cross-Cutting Rules (apply to every module)

1. **No tenant ID from client.** `CompanyId` always resolved server-side from JWT.
2. **Never write manual `WHERE CompanyId`.** Use the EF Core global query filter. Forgetting once = cross-tenant leak.
3. **Encrypt all third-party secrets** (WhatsApp tokens, Stripe IDs) at rest.
4. **Webhooks: verify signature, ack fast (200), process async** via Hangfire.
5. **Status updates are forward-only and idempotent** (`StatusRank`).
6. **Every tenant entity** carries `CompanyId` + `IsActive` and is covered by both query filters.

---

## 4. Access Control & Permissions Model

### Actor hierarchy
Three tiers. The first is platform-level; the other two are company-scoped.

| Actor | Scope | Created by |
|---|---|---|
| **Super-admin** | Platform (all companies) | Seeded (platform owner) |
| **Company Admin** | One company | **Super-admin only** |
| **Company User** | One company | Company Admin (or Super-admin) |

### Exclusive super-admin rights
Only the super-admin can:
- Create / deactivate **companies**
- Connect / manage **WABA accounts** for a company
- Create the **company Admin** user(s) for a company
- Operate across tenants (`IgnoreQueryFilters`)

A Company Admin can **never** create a company, connect a WABA, or act outside their own company.

### Company-scoped creation
When a Company Admin logs in (with super-admin-created credentials), everything they create is **auto-scoped to their own company** — `CompanyId` comes from the JWT, never chosen by them. Within that company they can create **Company Users** and assign each one a set of **permissions**. A Company User can only do what their assigned permissions allow.

### Permissions (granular, extensible)
Company-user access is driven by **discrete permission flags**, not hardcoded roles — so new capabilities are added as new flags without touching existing logic. A Company Admin implicitly holds **all** permissions (including `ManageUser`, which cannot be removed from them).

**Current permission set:**

| Permission | Grants |
|---|---|
| `LiveMessage` | Use the live-chat inbox (send/receive within the 24h window) |
| `TokenPurchase` | Purchase / manage message tokens (credits / top-ups) |
| `ManageTemplate` | Create, edit, submit templates to Meta |
| `ScheduleTemplate` | Create and schedule campaigns / template sends |
| `ContactList` | Manage contacts and contact lists (incl. Excel import) |
| `ManageUser` | Create company users and assign their permissions |
| `Analytics` | View reporting and analytics dashboards |

> This list **will grow**. Add new capabilities as new permission flags — never repurpose or rename an existing one (breaks anyone already assigned it).

### Data model (designed to extend)
- `Permission` — the set of permission keys (enum or reference table). Adding a capability = adding one key.
- `UserPermission` — many-to-many `{ UserId, PermissionKey }`. A user holds zero or more.
- `User.IsCompanyAdmin` — implicitly grants all permissions within the company; bypasses the join.
- `User.IsSuperAdmin` — platform-level, `CompanyId` is null.
- Enforcement: a `[RequirePermission("ManageTemplate")]` attribute/policy on each endpoint, checked server-side against the caller's permission set.

### Enforcement rules
1. Permissions are checked **server-side on every endpoint** — never trusted from the client.
2. A granter can only assign permissions **they themselves hold** — no privilege escalation.
3. Super-admin rights are **never** expressible as company permissions (separate plane).
4. Permission changes apply on the user's **next token issuance / refresh** (embed permission claims in the JWT, or look them up per request — pick one, stay consistent).

---

## 5. Build Order (Dependency Graph)

Each phase depends only on phases above it. Do not start a phase until its dependencies are done.

| # | Phase | Depends on |
|---|-------|-----------|
| 0 | Foundation & Infrastructure | — |
| 1 | Multi-Tenancy Core | 0 |
| 2 | Identity & Access (Company, Users, Auth) | 1 |
| 3 | Billing & Subscription | 2 |
| 4 | WhatsApp Connection + Webhook Receiver | 2 |
| 5 | Messaging Building Blocks (Templates, Contacts, Media) | 4 |
| 6 | Send Pipeline (Rate limiter, Send, Status) | 3, 4, 5 |
| 7 | Conversations / Inbox | 6 |
| 8 | Campaigns, Scheduling & Automation | 6 |
| 9 | Reporting & Analytics | 6, 8 |
| 10 | Platform Admin (Super-admin) | all |

---

## PHASE 0 — Foundation & Infrastructure

> **Goal:** Runnable, observable, deployable skeleton for both apps. Nothing tenant-specific yet.

### 0.1 Monorepo & scaffolding
- **Depends on:** —
- Repos: `wa_api/` (.NET 8), `wa_web/` (Next.js 16). Mirror SFA layout and `CLAUDE.md` conventions.
- `docker-compose.yml` on Contabo: `api`, `redis`, `caddy` (reverse proxy + auto HTTPS).
- **Done when:** API + web run locally; API builds + runs as Docker container behind Caddy with HTTPS.

### 0.2 Core API conventions (port from SFA)
- `ApiResponse<T>`, `ApiError`, `ResponseHelper`, `SFAException` → rename to platform exception.
- `GlobalExceptionMiddleware`, `CorrelationIdMiddleware`, exception→HTTP mapping.
- `/api/v1/` prefix, camelCase serialization, soft delete `IsActive`.
- **Done when:** A sample endpoint returns the standard envelope; thrown exceptions map to correct status + `ApiError`.

### 0.3 Database & EF Core
- **Depends on:** 0.2
- Connect to Supabase Postgres via **Supavisor pooler** connection string (not direct).
- `AppDbContext`, migration pipeline, `DataSeeder`.
- **Done when:** Migrations apply to Supabase; pooled connection verified under concurrent load.

### 0.4 Redis, Hangfire, Locking
- **Depends on:** 0.3
- Redis: `ICacheService`, `IDistributedLockService` (Redis or Postgres advisory locks), `IIdempotencyService`.
- Hangfire with Redis storage; dashboard secured behind admin auth (add after Phase 2).
- **Done when:** A test recurring + fire-and-forget job runs; distributed lock prevents double execution.

### 0.5 Observability
- **Depends on:** 0.2
- Serilog structured logging with CorrelationId; health checks (`/health`); Application Insights or equivalent.
- **Done when:** Logs are queryable with correlation IDs; health endpoint green.

---

## PHASE 1 — Multi-Tenancy Core

> **Goal:** The tenancy spine. Built before any business entity so every table is tenant-safe from day one.

### 1.1 Tenant primitives
- **Depends on:** 0.3
- `ITenantContext` (scoped) exposing `CompanyId` + `IsSuperAdmin`, populated from JWT claims per request.
- Base entity / interface `ITenantEntity { Guid CompanyId }`.
- **EF Core global query filter** on every tenant entity: `e => e.CompanyId == _tenant.CompanyId && e.IsActive`.
- Extend `AuditInterceptor` to **auto-set `CompanyId`** on insert from `ITenantContext`.
- **Done when:** A seeded entity is invisible to a request carrying a different `CompanyId`, with zero manual filtering in the repository.

### 1.2 Index & constraint strategy
- All tenant uniqueness uses **composite indexes `(CompanyId, X)`** (e.g. phone unique per company, not globally).
- **Done when:** Two companies can hold the same contact phone number without conflict.

### 1.3 Super-admin bypass
- Platform-owner queries use `IgnoreQueryFilters()` behind an explicit `IsSuperAdmin` guard.
- **Done when:** Super-admin can read across companies; normal users cannot, even by tampering with requests.

---

## PHASE 2 — Identity & Access

> **Goal:** Companies exist, users belong to them, JWT carries `companyId` + `role`. Everything downstream authenticates here.

### 2.1 Company
- **Depends on:** 1.1
- Entity: `Company { Id, Name, Logo, BrandingConfig, Status, CreatedAt }`.
- Endpoints: register company, get/update profile, branding.
- **Done when:** A company can be created and its profile updated; `CompanyId` flows into tenant context.

### 2.2 Users & permissions
- **Depends on:** 2.1
- Entities: `User { Id, CompanyId(nullable), Email, PasswordHash, IsSuperAdmin, IsCompanyAdmin, IsActive }`; `UserPermission { UserId, PermissionKey }`.
- Implements the permission model in **Section 4**: Company Admin holds all permissions; Company Users hold an assigned subset; super-admin is platform-level (`CompanyId` null).
- Enforcement via `[RequirePermission(...)]` server-side on every endpoint.
- **Done when:** A user can access only the endpoints their permissions allow; privilege escalation, cross-permission, and cross-tenant access are all denied.

### 2.3 Auth (API)
- **Depends on:** 2.2
- Login → JWT access token with claims `sub`, `email`, `role`, **`companyId`**; refresh token with rotation + revocation list (reuse SFA pattern).
- **Done when:** Login issues tokens; refresh rotates; revoked tokens rejected; `companyId` claim drives tenant context.

### 2.4 Auth (Web)
- **Depends on:** 2.3
- NextAuth v5 session wrapper holding the API-issued token; axios request interceptor attaches `Authorization: Bearer`; protected route group + role guards.
- **Done when:** Login persists session; protected pages redirect unauth users; token auto-attached to all API calls.

### 2.5 Registration & onboarding bootstrap
- **Depends on:** 0.3 (seeded super-admin), 1.3, 2.1, 2.2, 2.3
- **v1 model: super-admin provisioning** — the platform owner (you) creates each company. **No public signup page in v1.**
- **Atomically** create `Company` + its **first Admin user** in one transaction (neither exists without the other). Admin receives an email invite to set their password.
- Exposed as a **super-admin-only API endpoint** (guarded by `IsSuperAdmin`); the full provisioning UI lands in Phase 10.1. Seed one company via `DataSeeder` for dev/testing.
- Public self-serve signup is **deferred** — add later only if you open the platform to direct signups.
- **Done when:** The platform owner provisions a company + admin atomically; the admin sets a password and logs in with a `companyId`-scoped token; no public signup route exists.

---

## PHASE 3 — Billing & Subscription

> **Goal:** Gate every paid feature behind an active subscription + quota. Built before messaging so sends can be blocked when unpaid.

### 3.1 Plans & quotas
- **Depends on:** 2.1
- Entity: `Plan { Id, Name, MonthlyMessageQuota, FeatureFlags, PriceId }`; `Subscription { CompanyId, PlanId, Status, CurrentPeriodEnd, StripeSubscriptionId }`.
- **Done when:** Plans defined; a company can hold a subscription record.

### 3.2 Stripe integration
- **Depends on:** 3.1
- Stripe Customer per company, Checkout session, subscription creation; store IDs encrypted.
- **Done when:** A company completes Stripe Checkout and a subscription is recorded.

### 3.3 Stripe webhooks
- **Depends on:** 3.2, 0.4
- Handle `customer.subscription.created/updated/deleted`, `invoice.paid`, `invoice.payment_failed`. Verify Stripe signature; ack fast; sync state to DB.
- **Done when:** Subscription state in DB always mirrors Stripe; payment failure flips status correctly.

### 3.4 Subscription gate middleware
- **Depends on:** 3.3
- Middleware/policy blocking message-send + campaign endpoints when subscription inactive **or** monthly quota exhausted.
- **Done when:** Expired/over-quota company is blocked from sending with a clear `ApiError` code.

### 3.5 Invoices & history
- **Depends on:** 3.3
- Read endpoints for invoices + payment history (sourced from Stripe + local mirror).
- **Done when:** Company admin can view billing history.

---

## PHASE 4 — WhatsApp Connection + Webhook Receiver

> **Goal:** Each company connects its own WABA; one shared webhook endpoint routes by phone number. Gates all messaging.

### 4.1 WhatsApp account model
- **Depends on:** 2.1
- Entity: `WhatsAppAccount { Id, CompanyId, WabaId, PhoneNumberId, EncryptedAccessToken, DisplayName, QualityRating, MessagingTier, Status }`.
- Access token **encrypted at rest**; never returned to client.
- **Done when:** A company can store an encrypted WABA connection; token decryptable only server-side.

### 4.2a Manual credential connection — **ship first**
- **Depends on:** 4.1
- **No Tech Provider / App Review / business verification needed. Super-admin only** connects a WABA on behalf of a company (per Section 4) — Company Admins cannot.
- Inputs (entered by super-admin): target company, `WabaId`, `PhoneNumberId`, **System User permanent access token** (not the 23h temporary one), display phone number.
- On save: **validate** by calling Meta `GET /{phoneNumberId}` (or `/{phoneNumberId}/whatsapp_business_profile`) with the token → reject if it fails → **encrypt token** → persist as a `WhatsAppAccount` scoped to that company. Token never returned to client.
- Platform-level config (yours, not per-company): **App ID + App Secret** (for webhook signature verification) + a **Verify Token** for the webhook handshake.
- **Done when:** The super-admin connects a WABA to a company, an invalid token is rejected with a clear `ApiError`, and the stored token is encrypted + usable for sending.

### 4.2b Embedded Signup onboarding — **later (scale)**
- **Depends on:** 4.2a, Meta Tech Provider approval (business verification + App Review + Live mode)
- Self-serve OAuth flow ("Login with Facebook") so clients connect their own WABA without manual credential entry; assets auto-created and shared with your app.
- Build only once self-serve onboarding becomes the bottleneck. Not required for dev, UGB, or first manual clients.
- **Done when:** A client connects a WABA end-to-end through your portal without you touching credentials.

### 4.3 Quality & tier monitoring
- **Depends on:** 4.1, 0.4
- Recurring Hangfire job polling `/phone_numbers/{id}` for quality rating + tier; store + alert. Yellow → throttle, Red → auto-pause sending.
- **Done when:** Rating changes are persisted and a Red rating pauses that company's sends.

### 4.4 Webhook receiver (shared)
- **Depends on:** 4.1, 0.4
- Single public endpoint. **Verify `X-Hub-Signature-256`** against app secret → reject forged. Route by `metadata.phone_number_id` → company. Enqueue raw payload to Hangfire, return 200 immediately.
- **Done when:** Valid Meta callbacks are accepted, routed to the right company, and queued; invalid signatures rejected; response < 2s.

---

## PHASE 5 — Messaging Building Blocks

> **Goal:** The reusable pieces every send needs: templates, contacts, media.

### 5.1 Template management
- **Depends on:** 4.1
- Entity: `Template { Id, CompanyId, Name, Category, Language, Components(JSON: header/body/footer/buttons), Variables, MetaTemplateId, Status }`.
- Categories: Marketing, Utility, Authentication, Service. Body mandatory; header/footer/buttons optional; variables `{{1}}`, `{{2}}`.
- **Done when:** A company can build a structurally valid template matching Meta's schema.

### 5.2 Template submission & status sync
- **Depends on:** 5.1, 4.4
- Submit to Meta; track status (Pending/Approved/Rejected) updated via webhook `message_template_status_update`.
- **Done when:** Submitted templates reflect live Meta approval status.

### 5.3 Contacts & lists
- **Depends on:** 2.1
- Entities: `Contact { Id, CompanyId, Phone, Name, Attributes, Category, OptInStatus }`; `ContactList`; `ContactListMember`.
- CRUD, search/filter, list grouping. **Opt-in status required** before marketing sends.
- **Done when:** Contacts can be created, grouped into lists, searched/filtered, tenant-isolated.

### 5.4 Excel import
- **Depends on:** 5.3
- Upload `.xlsx` → parse → column mapping UI → validate phone numbers/required fields → report invalid rows → assign to a list.
- **Done when:** A large file imports with a clear valid/invalid report and assignment to a target list.

### 5.5 Media handling
- **Depends on:** 4.1
- Upload media to Meta → store `media_id` + **own copy** in Supabase Storage (Meta URLs expire ~30 days). Re-upload/refresh logic.
- **Done when:** Media can be attached to a template/message and survives Meta URL expiry.

---

## PHASE 6 — Send Pipeline (Core Engine)

> **Goal:** Deliver messages to Meta safely. The rate limiter is the anti-block heart.

### 6.1 Rate limiter
- **Depends on:** 0.4, 4.1
- Redis **token bucket per `phone_number_id`**, tier-aware (1K → 10K → 100K/day; per-second cap). Works across all worker replicas. Over cap → hold/requeue.
- **Done when:** Total throughput to one number never exceeds its tier regardless of worker count.

### 6.2 Outbound send service
- **Depends on:** 6.1, 5.1, 3.4
- Send approved template via Meta Cloud API; **idempotent** (reuse `IdempotencyKey`) so retries don't double-send; passes through subscription gate + rate limiter.
- **Done when:** A single template message sends, is idempotent on retry, and is blocked when unpaid/over-quota/rate-limited.

### 6.3 Message status tracking
- **Depends on:** 6.2
- Entity: `Message { Id, CompanyId, ContactId, CampaignId?, Wamid, Status, StatusAt, Category, Billable, ErrorCode }`. Map `wamid` on send.
- **Done when:** Every sent message has a row keyed by `wamid`.

### 6.4 Status webhook processor
- **Depends on:** 6.3, 4.4
- Process `statuses[]`: match `wamid`, **forward-only** (`StatusRank: sent<delivered<read<failed`), capture `pricing` object (billable + category), record `errors` on failure.
- **Done when:** Out-of-order/duplicate callbacks update status correctly; cost captured per message; failures logged.

---

## PHASE 7 — Conversations / Inbox

> **Goal:** Real-time live chat with correct billing-window enforcement.

### 7.1 24-hour window state machine
- **Depends on:** 6.3
- Entity: `Conversation { Id, CompanyId, ContactId, LastInboundAt, WindowExpiresAt, AssignedUserId, Status }`. Window opens/resets on each inbound; free-text allowed only while open.
- **Done when:** Window state is accurate; closed window forbids free-text sends server-side.

### 7.2 SignalR hub (realtime channel)
- **Depends on:** 2.3, 2.4
- `InboxHub` in the .NET API, `[Authorize]` with the **same JWT** the API already issues. On connect, add connection to group `company:{companyId}` (claim-based) — isolation reuses your auth, no Supabase JWT/RLS.
- Configure SignalR in `Program.cs`; enable client auto-reconnect.
- **Done when:** An authenticated client connects, joins only its company group, and a test push reaches it; another company's client never receives it.

### 7.3 Incoming message processing
- **Depends on:** 7.1, 7.2, 4.4
- Webhook `messages` (inbound) → **dedupe by `wamid`** (ignore duplicates) → store message → reset window → push to `company:{companyId}` group via `IHubContext`.
- **Done when:** An inbound WhatsApp message is stored once (no dupes) and pushed live to the owning company only.

### 7.4 Inbox UI
- **Depends on:** 7.3, 2.4
- Conversation list, threaded view, full history, multi-chat, assign-to-agent. Subscribe to the SignalR hub once; on `messageReceived`, **update the TanStack Query cache** (no refetch). On reconnect, **refetch the open thread once** to backfill any gap.
- Push to company group; client decides if the message matches the open thread.
- **Done when:** Users with `LiveMessage` see messages live, missed messages backfill on reconnect, and replies send within an open window.

### 7.5 Window enforcement (UI + API)
- **Depends on:** 7.1, 7.4
- Free-text composer disabled when window closed; only approved templates selectable. Enforced again server-side (never trust client).
- **Done when:** Closed-window free-text is blocked in both UI and API.

---

## PHASE 8 — Campaigns, Scheduling & Automation

> **Goal:** Bulk + scheduled messaging on top of the send pipeline.

### 8.1 Campaign creation
- **Depends on:** 6.2, 5.3, 5.1
- Entity: `Campaign { Id, CompanyId, TemplateId, ContactListId, Personalization, Status, Schedule }`. Map template variables to contact attributes.
- **Done when:** A campaign is defined (list + approved template + variable mapping).

### 8.2 Bulk send orchestration
- **Depends on:** 8.1, 6.1
- On launch: create `Message` rows (queued) → batched Hangfire jobs → each gated by rate limiter → send. Track per-message status.
- **Done when:** A campaign to N contacts sends fully, throttled, with per-message status.

### 8.3 Scheduling
- **Depends on:** 8.2
- One-time (date/time) + recurring (daily/weekly/monthly). Pause / modify / cancel.
- **Done when:** Scheduled + recurring campaigns fire at the right time and respect pause/cancel.

### 8.4 Automation engine
- **Depends on:** 8.3
- Hangfire recurring workers process due campaigns + queues reliably; poison-job handling (max retries, dead-letter).
- **Done when:** Campaigns execute unattended; permanently-failed messages dead-letter instead of looping.

### 8.5 Campaign delivery tracking
- **Depends on:** 8.2, 6.4
- Per-campaign rollup of sent/delivered/read/failed.
- **Done when:** A campaign shows accurate live delivery counts.

---

## PHASE 9 — Reporting & Analytics

> **Goal:** Visibility into delivery, performance, and cost.

### 9.1 Message metrics — sent/delivered/read/failed, per company. **Depends on:** 6.4
### 9.2 Campaign performance dashboards — engagement per campaign. **Depends on:** 8.5
### 9.3 Cost analytics — from captured `pricing` object: spend per category/country/company (your margin vs Meta cost). **Depends on:** 6.4
- **Done when:** Each company sees delivery + cost dashboards; you see per-company cost for margin.

---

## PHASE 10 — Platform Admin (Super-Admin)

> **Goal:** Platform-owner control across all tenants.

### 10.1 Super-admin panel
- **Depends on:** 1.3 + all
- **Provision new companies** (create company + first admin — UI over the 2.5 endpoint), manage all companies, view usage, control plans, view payments, system health.
### 10.2 Cross-tenant monitoring
- Aggregate quality ratings, flag high block-rate companies, pause abusers.
- **Done when:** Owner can monitor + intervene across all companies without breaking tenant isolation.

---

## 11. Security & Compliance Checklist (verify throughout)

- [ ] Global query filter on every tenant entity (tenant-isolation tests pass).
- [ ] All third-party tokens encrypted at rest.
- [ ] Webhook signature verification (Meta + Stripe).
- [ ] Rate limiting (API + per-number send throttle).
- [ ] Permissions enforced server-side on every endpoint (no privilege escalation).
- [ ] Opt-in enforced before marketing sends; block rate kept < 2%.
- [ ] Company offboarding wipes its contacts + chat history (data retention).
- [ ] Postgres backups automated (Supabase) + config/Redis backup on Contabo.
- [ ] No stack traces or internal errors leaked to client.

---

## 12. Definition of Done (platform v1)

Once **provisioned by the platform owner**, a company can: subscribe via Stripe → connect its WABA (manual credentials) → import contacts → create + get a template approved → run a throttled bulk campaign → receive replies in a live inbox within the 24-hour window → see delivery + cost reporting — all fully isolated from other companies, with the platform owner able to provision and oversee everything.