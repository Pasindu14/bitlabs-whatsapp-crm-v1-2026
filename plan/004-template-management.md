# Plan 004 — Template Management (Marketing templates, full builder)

Mirror the **Contacts / WABA Connections** feature exactly (Entity → Service → Controller → DTOs on
the API; `schema → actions → hooks → store → services → components` on the web). This is PRD
**Phase 5.1 (Template management)** + **5.2 (Submission & status sync)**.

**Scope (decided):** **MARKETING** category only, but the **full component builder** — text **and
media** headers, body with variables, footer, all marketing button types, and (final/stretch step)
carousel. We create real Meta templates against the official endpoint and track approval:

> `POST https://graph.facebook.com/v19.0/{waba-id}/message_templates`
> ([Meta — Message Template API](https://developers.facebook.com/documentation/business-messaging/whatsapp/reference/whatsapp-business-account/message-template-api#post-version-waba-id-message-templates))

Reuse the existing `MetaGraph` named `HttpClient` and pin the **same API version `MessageService`
already uses (`v19.0`)** — do not introduce a second client or version.

---

## The mental model — one table that mirrors a Meta template

A `Template` is our local copy of a WhatsApp message template. The user builds it in our UI, we
**submit** it to Meta, and we **poll** Meta for the approval verdict.

| Field | Plain English | Notes |
|---|---|---|
| `CompanyId` | Owning tenant. | Auto-stamped from JWT (never sent by client). |
| `WabaConnectionId` | Which connection we submit through. | FK → `WabaConnection`. This is **where the access token + WABA id live.** |
| `WabaId` | The Meta WABA the template belongs to. | **Denormalized** copy of the connection's `WabaId` (see "WABA scoping" rule — `WabaId` is *not* unique on `WabaConnection`). |
| `Name` | Meta template name. | `^[a-z0-9_]+$`, ≤512 chars. Unique per `(CompanyId, Name, Language)`. |
| `Language` | Meta language/locale code. | e.g. `en_US`, `si_LK`. Same name + different language = a separate variant row. |
| `Category` | Fixed **`Marketing`** in v1. | Stored as an enum so Utility/Auth can be added later without a migration. |
| `Components` | The header/body/footer/buttons tree. | **`jsonb`** — our normalized shape (below), mapped to Meta's `components` on submit. |
| `ParameterFormat` | `Positional` (`{{1}}`,`{{2}}`). | Pinned for v1 (matches PRD; simplest). |
| `Status` | `Draft → Pending → Approved/Rejected` (+ `Paused`/`Disabled` from Meta). | Drives the table badge + what's editable. |
| `MetaTemplateId` | Id returned by Meta on create. | `null` while `Draft`; used to poll status + delete. |
| `RejectionReason` | Why Meta rejected. | Populated from the status poll. |
| `SubmittedAt` / `LastSyncedAt` | Audit of submit + last poll. | |

### Lifecycle (this is the whole feature)

```
            create            POST /submit              poll job / refresh
  (UI)  ───────────►  DRAFT ───────────────►  PENDING ───────────────►  APPROVED
                        │  (editable)            │ (read-only)      └──►  REJECTED (+reason)
                        │                        └─ Meta may also report PAUSED / DISABLED
                        └─ delete (local only)        delete ⇒ also DELETE from Meta
```

**Edit is allowed only while `Draft`.** Editing an already-submitted template is intentionally
**out of scope** (Meta's edit rules are restrictive and warrant their own slice). To change a
submitted template, delete and recreate — exactly how Meta's own Template Manager nudges you.

---

## How our row maps to Meta's payload

We store a **normalized** component object (easy to render + edit), and a mapper builds Meta's
`components` array on submit. Example — a marketing template with an image header, two body
variables, a footer, and two buttons:

**Our stored `Components` (jsonb):**
```jsonc
{
  "header": { "type": "image", "example": { "mediaHandle": "4::aW1h...:ARZ..." } },
  "body":   { "text": "Hi {{1}}, your {{2}} order ships today!",
              "examples": ["Jane", "birthday gift"] },
  "footer": { "text": "Reply STOP to opt out" },
  "buttons": [
    { "type": "url", "text": "Track order", "url": "https://shop.co/track/{{1}}", "urlExample": "ORD-9" },
    { "type": "quick_reply", "text": "Talk to us" }
  ]
}
```

**What we POST to Meta** (`v19.0/{wabaId}/message_templates`):
```jsonc
{
  "name": "order_shipped",
  "language": "en_US",
  "category": "MARKETING",
  "parameter_format": "POSITIONAL",
  "allow_category_change": true,
  "components": [
    { "type": "HEADER", "format": "IMAGE", "example": { "header_handle": ["4::aW1h...:ARZ..."] } },
    { "type": "BODY", "text": "Hi {{1}}, your {{2}} order ships today!",
      "example": { "body_text": [["Jane", "birthday gift"]] } },
    { "type": "FOOTER", "text": "Reply STOP to opt out" },
    { "type": "BUTTONS", "buttons": [
      { "type": "URL", "text": "Track order", "url": "https://shop.co/track/{{1}}", "example": ["ORD-9"] },
      { "type": "QUICK_REPLY", "text": "Talk to us" }
    ]}
  ]
}
```
**Success →** `{ "id": "<metaTemplateId>", "status": "PENDING", "category": "MARKETING" }`.

---

## Rules (baked in — same conventions as the rest of the app)

| Topic | Decision |
|---|---|
| Company scoping | **Never send `CompanyId`.** Auto-stamped by `AuditInterceptor`; global query filter hides other tenants. (Same as every tenant entity.) |
| WABA scoping | Templates are owned by a **WABA**, and `WabaConnection.WabaId` is **not unique** (two phone numbers can share one WABA). User picks a connection from `GET /my-waba-connections`; we submit with **that connection's token** to its `WabaId` and **denormalize `WabaId`** onto the row. **v1 assumes one connection per WABA**; if a company has two connections sharing a `WabaId`, the template still creates once at the WABA level (documented; revisit if it ever happens). |
| Name | `^[a-z0-9_]+$`, validated server-side → `ValidationException`. Unique per `(CompanyId, Name, Language)` (composite index, mirrors `(CompanyId, Phone)`). |
| Variables | **Positional** `{{1}}, {{2}}` only. Numbering must be contiguous from 1. |
| Examples | **Mandatory** — Meta rejects any variable without a sample. The DTO + builder must collect one example per `{{n}}` (and per dynamic URL / media header). Validate count == placeholder count before submit. |
| Category | Fixed **`Marketing`** in the UI; send `allow_category_change: true` so Meta can re-tag rather than reject. Enum keeps Utility/Auth addable later. |
| Editing | Allowed **only while `Draft`**. Submitted templates are read-only (delete + recreate to change). |
| Soft delete | `DELETE` deactivates locally (`IsActive=false`); if `MetaTemplateId` is set, also call Meta `DELETE /{wabaId}/message_templates?name={name}` (best-effort, logged). |
| Auth | `[Authorize(Roles = "CompanyAdmin")]` — consistent with every existing controller. *(Follow-up: also admit Agents holding the `manage_template` permission — the key already exists in `Permission.cs` + `permissions.ts`. Gate later, exactly as plan 002 deferred `contact_list`.)* |
| Idempotency | Reuse `X-Idempotency-Key` on create/submit (same as `ContactService.create`). |

**Deliberately OUT of v1** (note in PRD 5.x): Authentication/OTP templates, Utility category,
template **editing after approval**, analytics on template usage, and webhook-driven status (we
poll — see Step 3). Carousel + limited-time-offer are the final stretch step.

---

## Build in steps — each works on its own

### STEP 1 — Local draft CRUD (API)
> Goal: build + save a structurally valid Marketing template **locally** (status `Draft`). No Meta
> call yet — this de-risks the data model + builder before touching the network.

**API** (`Features/Templates/`)
1. `Entities/Template.cs` — `: BaseEntity, ITenantEntity` with the fields above. Enums
   `TemplateCategory { Marketing }`, `TemplateStatus { Draft, Pending, Approved, Rejected, Paused, Disabled }`,
   `TemplateParameterFormat { Positional }`.
2. `Entities/TemplateComponents.cs` — the normalized component POCO (Header/Body/Footer/Buttons[],
   optional Carousel) that serializes to the `Components` `jsonb` column.
3. `AppDbContext` — `DbSet<Template>`; config: `Components` mapped `.HasColumnType("jsonb")`
   (System.Text.Json), enum→string conversions (mirror `WabaConnection.Status`),
   `(CompanyId, Name, Language)` unique index, `HasIndex(CompanyId)`, FK → `WabaConnection`
   (`OnDelete Restrict`), and the tenant query filter
   `t => _tenant.IsSuperAdmin || t.CompanyId == _tenant.CompanyId`.
4. DTOs — `CreateTemplateRequest`, `UpdateTemplateRequest` (name, language, wabaConnectionId,
   components), `TemplateResponse` (id, name, language, category, status, components,
   metaTemplateId, rejectionReason, wabaConnectionId + displayPhoneNumber, isActive, createdAt).
   `[Required]` on params; component validation in the service.
5. `ITemplateService` / `TemplateService` — `GetPagedAsync` (search on Name/Language, filter by
   `status`), `GetByIdAsync`, `CreateAsync` (validate name regex, body present, examples match
   placeholders, resolve + denormalize `WabaId` from the chosen connection → store `Draft`),
   `UpdateAsync` (**reject if not `Draft`** → `BusinessRuleException("TEMPLATE_NOT_EDITABLE", …)`),
   `DeleteAsync` (soft delete). **Do not set `CompanyId`.**
6. `Controllers/TemplatesController.cs` — `[Route("templates")] [Authorize(Roles="CompanyAdmin")]`:
   GET (page/pageSize/search/status), GET `{id:guid}`, POST (201), PUT `{id:guid}`, DELETE `{id:guid}`.
   `ResponseHelper` envelopes + `CorrelationId`.
7. `Program.cs` — register `ITemplateService` → `TemplateService`.
8. Migration — `dotnet ef migrations add AddTemplates` → `database update`.

**Done when:** a Marketing template (header/body/footer/buttons) saves as `Draft`, re-loads with its
full component tree intact, edits while draft, and is tenant-isolated.

---

### STEP 2 — Submit to Meta
> Goal: push a `Draft` to Meta and capture the verdict-in-progress.

**API**
1. `MetaTemplateClient` (small typed wrapper around `IHttpClientFactory.CreateClient("MetaGraph")`,
   mirrors `MessageService.CallMetaApiAsync`) with `CreateAsync(wabaId, token, payload)` →
   returns `(metaTemplateId, status, error)`. Maps Meta `error.message`/`error.code` to a clean
   `BusinessRuleException("TEMPLATE_SUBMIT_FAILED", <metaMessage>)`.
2. `TemplateMapper` — turns our normalized `Components` into Meta's `components` array + `example`
   objects (header_text / header_handle / body_text / button examples), per the mapping above.
3. `TemplateService.SubmitAsync(id)` — load the `Draft` + its `WabaConnection` (token), build the
   payload, POST via the client, store `MetaTemplateId`, set `Status=Pending`, `SubmittedAt`.
   Re-validate examples first; block if connection inactive (mirror `MessageService`'s `WABA_INACTIVE`).
4. Controller — `POST {id:guid}/submit`.

**Done when:** submitting a draft returns `Pending` with a stored `MetaTemplateId`; a malformed
template surfaces Meta's exact rejection reason in the standard `ApiError`.

---

### STEP 3 — Status sync (approval tracking)
> Goal: reflect Meta's live approval status. **No webhook receiver exists yet** (PRD 4.4 unbuilt),
> so we **poll** — mirroring the existing `WabaHealthCheckJob`.

**API**
1. `MetaTemplateClient.GetStatusAsync(metaTemplateId, token)` → `GET v19.0/{metaTemplateId}?fields=status,category` (falls back to `GET /{wabaId}/message_templates?name=` if needed).
2. `Infrastructure/Jobs/TemplateStatusSyncJob.cs` — copy `WabaHealthCheckJob`: every 15 min, load
   templates whose `Status == Pending` (non-terminal only), poll, map `APPROVED/REJECTED/PAUSED/
   DISABLED` → our enum, set `RejectionReason` + `LastSyncedAt`. Register in `Program.cs`
   (`RecurringJob.AddOrUpdate<TemplateStatusSyncJob>(…, Cron.MinuteInterval(15))`).
3. `TemplateService.RefreshStatusAsync(id)` + controller `POST {id:guid}/refresh-status` — on-demand
   poll for the "Refresh" button.
4. `DeleteAsync` extension — when `MetaTemplateId` is set, also call Meta `DELETE
   /{wabaId}/message_templates?name={name}` (best-effort; log failures).

> **Follow-up (proper Phase 5.2):** when the shared webhook receiver lands, handle
> `message_template_status_update` to push status instantly and retire the poll to a safety-net.

**Done when:** a pending template flips to Approved/Rejected automatically within a poll cycle, and
the manual "Refresh status" button works on demand.

---

### STEP 4 — Media header handles (enables IMAGE/VIDEO/DOCUMENT headers)
> Goal: support media headers. Meta needs a **sample-media handle** in `example.header_handle`,
> obtained via the **Resumable Upload API** (this is the slice of PRD 5.5 that templates require).

**API**
1. `POST /templates/media-handle` (multipart) — accepts the sample image/video/document.
2. Service `UploadSampleAsync(stream, fileName, mimeType)`:
   - `POST graph.facebook.com/v19.0/{appId}/uploads?file_name=&file_length=&file_type=` (App access
     token `{appId}|{appSecret}`) → upload-session id.
   - `POST graph.facebook.com/v19.0/{uploadSessionId}` with header `Authorization: OAuth {appToken}`
     + `file_offset: 0` + binary body → `{ "h": "<handle>" }`.
   - Return `{ handle }`; the builder drops it into `header.example.mediaHandle`.
3. Config — add `Meta:AppId` + `Meta:AppSecret` to `appsettings` (platform-level, per PRD 4.2a).
   *(Optional now / PRD 5.5 later: also persist a copy to Supabase Storage so the asset survives.)*

**Done when:** a marketing template with an image header uploads a sample, submits with a valid
`header_handle`, and Meta accepts it.

---

### STEP 5 — Web feature slice + builder UI
> Goal: the full builder + management page. Copy `features/contacts/` for the slice scaffolding;
> the builder itself is bespoke. Use shadcn primitives already in `components/ui/`.

**Web** (`features/templates/`)
1. `types.ts`, `schema/template-schema.ts` (zod — name regex, language, component tree, per-variable
   examples), `services/template-service.ts` (CRUD + `submit` + `refreshStatus` + `uploadMediaHandle`),
   `hooks/use-templates.ts` (TanStack Query + mutations), `store/template-store.ts`,
   `actions/template-actions.ts`. Reuse `MyWabaConnectionService` for the connection picker.
2. **Builder** (`components/template-builder-form.tsx`) — a full-page form, not a dialog (it's large):
   - Marketing (fixed badge) · WABA connection picker · name (with regex hint) · language select.
   - **Header**: type picker `None / Text / Image / Video / Document` → text (with one optional
     `{{1}}` + example) or media uploader (Step 4).
   - **Body**: textarea + "Insert variable" → renders an **examples** sub-form (one input per `{{n}}`).
   - **Footer**: optional text.
   - **Buttons**: repeater for `Quick reply / Visit URL (static|dynamic + example) / Call phone /
     Copy code`, within Meta's per-type limits.
3. **Live preview** (`components/template-preview.tsx`) — WhatsApp-style bubble rendering header +
   body (examples substituted) + footer + buttons, updating as they type.
4. **Table** (`components/template-table.tsx` + `columns.tsx`) — name, language, status **badge**
   (Draft/Pending/Approved/Rejected), row actions: Edit (draft only), **Submit**, **Refresh status**,
   Delete; rejection reason in a tooltip/expander.
5. **Routes** — `app/(protected)/templates/page.tsx` (list), `…/templates/new/page.tsx` and
   `…/templates/[id]/page.tsx` (builder). Add **"Templates" → `/templates`** to the **Messaging**
   group in `components/app-sidebar.tsx` (roles `CompanyAdmin`, `Agent`).

**Done when:** a user builds a Marketing template (incl. image header + variables + buttons) with a
live preview, saves a draft, submits, watches it go Pending → Approved, and sees rejects with reasons.

---

### STEP 6 — (stretch) Carousel & limited-time offer
> Only after Steps 1–5 ship. Extend the normalized `Components` with a `carousel.cards[]` (each card:
> media header + body + up to 2 buttons) and the `LIMITED_TIME_OFFER` component, plus their mappers
> and preview. Sequenced last so the core feature isn't blocked on the most complex Meta sub-flow.

---

## Verify (whole feature)
- `dotnet build` clean; `tsc` clean; migration applied.
- Tenant isolation: company A never sees / submits against company B's templates or WABA.
- Name regex + `(CompanyId, Name, Language)` uniqueness enforced (same name, two languages = OK).
- Examples required: a template missing a variable example is rejected **before** the Meta call.
- Full happy path against a real test WABA: build (image header + 2 vars + buttons) → draft →
  submit → poll flips to Approved → delete removes it from Meta too.
- Edit blocked once submitted; Meta rejection reason surfaces verbatim in the UI.

---

## How this feeds the rest of the platform
Approved templates are the required input for **Phase 6.2 (outbound send)** and **Phase 8.1
(campaigns)** — a campaign = approved template + contact list + variable→attribute mapping. This
slice produces the approved templates those phases consume.
