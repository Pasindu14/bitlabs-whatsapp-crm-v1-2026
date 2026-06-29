# WhatsApp "STOP" Opt-Out — Default Stop Button + Webhook Handling

Let a recipient opt out of campaigns by **tapping a "Stop" button** on the template (not just typing
"stop"), make that Stop button **present by default** on every template we send, surface opted-out
contacts in the Contacts UI, and let a **company admin manually change a contact's opt-out status**.
Opt-out is an **absolute hard stop** on sending — once set, the *only* sanctioned way back is an admin
lifting it from the contact screen. Honoring opt-outs cleanly is the single biggest lever against a WABA
ban (block/spam reports drive quality down → restriction).

---

## What already exists (do NOT rebuild)

The exploration found this feature is ~70% done already. Confirmed in code:

| Capability | Status | Where |
|---|---|---|
| Free-text `stop` / `unsubscribe` / `optout` → opt-out | ✅ Done | `InboundMessageWebhookHandler.cs` L38–44, L89–99 |
| `Contact.IsOptedOut` / `OptedOutAt` / consent fields | ✅ Done | `Features/Contacts/Entities/Contact.cs` |
| Campaign send loop skips opted-out contacts (`ErrorCode = "OPT_OUT"`) | ✅ Done | `CampaignBatchSendJob.cs` |
| `ContactResponse` exposes `IsOptedOut` / `OptedOutAt` | ✅ Done | `Features/Contacts/Dtos/ContactResponse.cs` |
| Contacts list shows an **"Opted out"** destructive badge | ✅ Done | `wa_web/features/contacts/components/columns.tsx` L72–102 |
| Templates fully support `quick_reply` buttons (model → Meta payload → builder UI) | ✅ Done | `TemplateComponents.cs`, `TemplateMapper.cs`, `buttons-editor.tsx` |

**So the request is NOT "add opt-out."** It is three concrete gaps on top of a working system.

---

## The three real gaps

1. **Button taps don't opt out.** The webhook only reads `m.Text.Body`. A template **quick-reply
   button tap** arrives as `type:"button"` with `button.text` / `button.payload` and **no `text.body`** —
   so today a tapped "Stop" is recorded as a normal message and the contact is **not** suppressed. This
   is the core bug: adding a Stop button without parsing button payloads does nothing.
2. **No default Stop button on templates.** `emptyBuilderValues()` starts with `buttons: []`
   (`template-schema.ts` L179) and nothing enforces a Stop button at submit time.
3. **No way to find or correct opt-out.** The list shows a badge per row but there's **no filter** to
   list "everyone who opted out," the detail/edit dialog doesn't show opt-out status, and **a company
   admin can't lift a mistaken opt-out** — `IsOptedOut` is webhook-only and `UpdateContactRequest`
   exposes just `HasOptedIn` (`UpdateContactRequest.cs` L9).

---

## Decisions (baked in — change if you disagree)

| Topic | Decision | Why |
|---|---|---|
| Opt-out is absolute | STOP → `IsOptedOut = true` is an **un-bypassable** send block. Already enforced in `CampaignBatchSendJob` (L159–165); the consent-override does **not** touch it. No code change — just don't weaken it. | The hard gate is what protects the number. |
| Lifting an opt-out | **Company admin only**, from the contact edit screen + a row quick-action. Customer-initiated `start`/`subscribe` keywords are *optional* on top. | Requirement: STOP is a hard stop; the sanctioned way back is an admin decision. `ContactsController` is already `[Authorize(Roles="CompanyAdmin")]`, so no new auth. |
| Manual opt-out (admin) | Setting it on mirrors the webhook: `IsOptedOut=true`, `OptedOutAt=now`, `HasOptedIn=false`, `ConsentSource=None`. Clearing it sets `IsOptedOut=false`, `OptedOutAt=null` (consent unchanged). | Consistency between the two write paths; clearing suppression ≠ granting consent. |
| Button-tap detection | Match `button.text` **and** `button.payload` against the existing `StopKeywords` set, reusing `IsStopIntent`. | Default button text is "Stop" (already a keyword) → **zero send-path change** needed. |
| Default button: soft vs hard | **Both** — seed it in the builder (visible, editable) **and** enforce it at `SubmitAsync` before the Meta call. | UX shows it pre-filled; submit-time enforcement is the real guarantee, and is the last chokepoint before Meta. |
| Enforcement scope | Only `Category == Marketing` templates (currently always Marketing). Skip if a stop-intent quick-reply already exists or the template already has 10 buttons. | Utility/auth templates are transactional; avoid duplicate buttons and Meta's 10-button cap. |
| Button text / payload | Text `"Stop"`; **optionally** set a fixed payload `"STOP_OPTOUT"` at send time (hardening). | Text-match works for English default; payload-match survives localized button text. |
| Customer re-opt-in keywords | *Optional* — `start` / `unstop` / `subscribe` keywords that clear `IsOptedOut`. The admin path (above) is the required mechanism; this is a convenience on top. | Cheap to add in the same handler; lets a customer self-resubscribe without a support ticket. |
| Opt-out confirmation reply | Out of scope for v1 (see below). | Adds a send inside the webhook; the inbound just reset the 24h window so it's allowed, but it's extra surface — defer. |
| Contacts filter | New `isOptedOut` query param + a toolbar filter; detail dialog shows read-only opt-out badge. | Badge already exists per-row; this makes the cohort findable/auditable. |

---

## Step 1 — Parse button & interactive replies in the webhook payload model *(fix first — unblocks the rest)*

**File:** `wa_api/wa_api/Features/Webhooks/Payloads/MetaWebhookEnvelope.cs`

`WebhookInboundMessage` (L78–85) only has `text`. Add `button` (template quick-reply taps) and
`interactive` (reply/list buttons in free-form messages):

```csharp
public sealed class WebhookInboundMessage
{
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }          // "text" | "button" | "interactive" | ...
    [JsonPropertyName("text")] public WebhookText? Text { get; set; }
    [JsonPropertyName("button")] public WebhookButton? Button { get; set; }            // template quick-reply tap
    [JsonPropertyName("interactive")] public WebhookInteractive? Interactive { get; set; } // interactive reply
}

public sealed class WebhookButton
{
    [JsonPropertyName("text")] public string? Text { get; set; }       // the button's display label, e.g. "Stop"
    [JsonPropertyName("payload")] public string? Payload { get; set; } // dev-defined payload (if set at send time)
}

public sealed class WebhookInteractive
{
    [JsonPropertyName("type")] public string? Type { get; set; }                 // "button_reply" | "list_reply"
    [JsonPropertyName("button_reply")] public WebhookButtonReply? ButtonReply { get; set; }
    [JsonPropertyName("list_reply")] public WebhookButtonReply? ListReply { get; set; }
}

public sealed class WebhookButtonReply
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
}
```

Tolerant deserialize already ignores unknown fields, so this is additive and safe.

---

## Step 2 — Detect a Stop intent from text OR a button tap

**File:** `wa_api/wa_api/Features/Webhooks/Handlers/InboundMessageWebhookHandler.cs`

**2a.** Replace the text-only opt-out block (L89–99) with one that checks every channel the customer
could have used. Keep `IsStopIntent` / `StopKeywords` as-is:

```csharp
// A Stop can arrive as free text, a tapped template quick-reply ("button"), or an interactive reply.
// A button tap has NO text.body, so we must look at button.text/payload and interactive titles/ids.
string?[] stopCandidates =
{
    m.Text?.Body,
    m.Button?.Text, m.Button?.Payload,
    m.Interactive?.ButtonReply?.Title, m.Interactive?.ButtonReply?.Id,
    m.Interactive?.ListReply?.Title,   m.Interactive?.ListReply?.Id,
};

if (!contact.IsOptedOut &&
    stopCandidates.Any(s => !string.IsNullOrWhiteSpace(s) && IsStopIntent(s!)))
{
    contact.IsOptedOut = true;
    contact.OptedOutAt = now;
    contact.HasOptedIn = false;
    contact.ConsentSource = ConsentSource.None;
    logger.LogInformation(
        "Contact {Phone} opted out via {Type} — suppressed from future outbound sends.", phone, m.Type);
}
```

**2b.** Improve the stored message preview (L86) so a button tap isn't logged as `[button]`:

```csharp
var body = m.Text?.Body
    ?? m.Button?.Text
    ?? m.Interactive?.ButtonReply?.Title
    ?? m.Interactive?.ListReply?.Title
    ?? $"[{m.Type ?? "message"}]";
```

**2c. (Recommended) Re-opt-in.** Add a `StartKeywords` set (`"start"`, `"unstop"`, `"subscribe"`) and,
when matched, clear the opt-out so the customer can come back:

```csharp
if (contact.IsOptedOut && startCandidates.Any(s => !string.IsNullOrWhiteSpace(s) && IsStartIntent(s!)))
{
    contact.IsOptedOut = false;
    contact.OptedOutAt = null;
    contact.HasOptedIn = true;
    contact.OptedInAt = now;
    contact.ConsentSource = ConsentSource.InboundMessage;
}
```

> Mind the existing constraints: this handler **never calls `SaveChanges`** (the batch is committed once
> by `WebhookProcessingJob`), stamps `CompanyId` by hand, and reads with `IgnoreQueryFilters()`. The
> changes above only mutate the already-tracked `contact`, so they fit that pattern unchanged.

---

## Step 3 — Make the "Stop" quick-reply button a default on templates

### 3a — Backend enforcement (authoritative)

**File:** `wa_api/wa_api/Features/Templates/TemplateService.cs`

Add a helper and call it in `SubmitAsync` **before** `Validate(...)` (around L167), so the button is
guaranteed on the exact payload sent to Meta and is persisted by Submit's existing `SaveChanges` (L181):

```csharp
// Guarantee a one-tap opt-out on every marketing template before it goes to Meta.
private static void EnsureOptOutButton(Template t)
{
    if (t.Category != TemplateCategory.Marketing) return;
    var buttons = t.Components.Buttons ??= [];
    bool hasStop = buttons.Any(b =>
        b.Type == "quick_reply" && !string.IsNullOrWhiteSpace(b.Text) && IsStopText(b.Text));
    if (hasStop || buttons.Count >= 10) return;          // already covered, or no room (rare)
    buttons.Add(new TemplateButton { Type = "quick_reply", Text = "Stop" });
}
```

`IsStopText` can reuse the same keyword set as the webhook (extract `StopKeywords` to a shared constant,
e.g. `Common/OptOut/StopKeywords.cs`, so the button the UI adds and the keyword the webhook matches can
never drift apart).

**Caveats to handle (note in PR, not blockers):**
- Meta templates are **immutable once approved** — this only affects newly submitted templates; existing
  approved templates won't gain the button retroactively (acceptable).
- Mixing a quick-reply with existing **URL/phone** CTA buttons has Meta ordering rules; a rejection just
  leaves the template in `Rejected` with a reason (non-fatal, already surfaced in the UI).
- Some regions auto-append a native marketing opt-out — verify in one test submit so we don't ship a
  redundant button.

### 3b — Frontend default (visible & editable)

**File:** `wa_web/features/templates/schema/template-schema.ts` — seed `emptyBuilderValues()` (L165–181):

```ts
buttons: [{ type: "quick_reply", text: "Stop", url: "", urlExample: "", phoneNumber: "", example: "" }],
```

The `ButtonsEditor` already renders/removes buttons, so it appears pre-filled and the user can edit the
label. Backend 3a re-adds it at submit if removed. *(Note: `wa_web/AGENTS.md` warns this is a customized
Next.js — check `node_modules/next/dist/docs/` before touching app/router code; this change is schema-only
so it's low risk.)*

### 3c — (Optional hardening) deterministic payload at send time

**File:** `wa_api/wa_api/Features/Campaigns/Jobs/CampaignBatchSendJob.cs` → `SendTemplateMessageAsync`

Only needed if button labels get localized (so `button.text` won't match English keywords). Add a
`{ type:"button", sub_type:"quick_reply", index:"<i>", parameters:[{ type:"payload", payload:"STOP_OPTOUT" }] }`
component for the Stop button's index, and match `button.payload == "STOP_OPTOUT"` in Step 2. Skip for v1
if templates stay English — text-match already covers it.

---

## Step 4 — Contacts: filter + admin-editable opt-out

`ContactsController` is already `[Authorize(Roles = "CompanyAdmin")]` end-to-end, so everything below is
company-admin-only with no new authorization.

### 4a — Backend: let an admin change opt-out status

**File:** `wa_api/.../Features/Contacts/Dtos/UpdateContactRequest.cs` — add the field (L9):

```csharp
public record UpdateContactRequest(
    [Required, StringLength(20, MinimumLength = 5)] string Phone,
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    bool? HasOptedIn = null,
    bool? IsOptedOut = null            // null = leave unchanged
);
```

**File:** `wa_api/.../Features/Contacts/ContactService.cs` → `UpdateAsync` — add **after** the existing
`HasOptedIn` block (L113), so suppression always wins when both are set in one request:

```csharp
if (request.IsOptedOut.HasValue && request.IsOptedOut.Value != contact.IsOptedOut)
{
    if (request.IsOptedOut.Value)
    {
        // Admin manually suppresses — mirror the webhook STOP path exactly.
        contact.IsOptedOut = true;
        contact.OptedOutAt = DateTime.UtcNow;
        contact.HasOptedIn = false;
        contact.ConsentSource = ConsentSource.None;
    }
    else
    {
        // Admin lifts a mistaken opt-out. Re-enables sending; does NOT by itself grant consent.
        contact.IsOptedOut = false;
        contact.OptedOutAt = null;
    }
}
```

`Map` already returns `IsOptedOut` / `OptedOutAt`, so the response reflects the change with no DTO change.

### 4b — Backend: filter the list by opt-out

**Files:** `ContactsController.GetAll` + `ContactService.GetPagedAsync`

```csharp
// controller: [FromQuery] bool? isOptedOut = null  → pass into GetPagedAsync
if (isOptedOut is not null)
    query = query.Where(c => c.IsOptedOut == isOptedOut.Value);
```

(Optional: allow `sortBy=optedOutAt`.)

### 4c — Frontend: editable toggle, row quick-action, filter

**Files:** `wa_web/features/contacts/` — `schema/contact-schema.ts`, `components/contact-form.tsx`,
`components/contact-dialogs.tsx`, `components/columns.tsx`, `components/contact-table.tsx`,
`hooks/use-contacts.ts`, `services/contact-service.ts`, `actions/contact-actions.ts`, `types.ts`

- **Schema:** diverge `updateContactSchema` from create (they're aliased today, L23) to add
  `isOptedOut: z.boolean().optional()`; add it to `UpdateContactInput`.
- **Edit form** (`contact-form.tsx`, edit mode only): add an opt-out toggle styled as a warning/destructive
  control — *"Opted out (suppressed from all sending). Turn off to re-enable."* — and show
  `optedOutAt` when set. Pass `isOptedOut` into the dialog's `defaultValues` (the edit dialog already
  fetches the full contact via `useContact`).
- **Row quick-action** (`columns.tsx` actions menu): "Re-enable sending" when `isOptedOut`, else
  "Suppress (opt out)" — fires the update mutation with the flipped flag. Fastest path for the common
  "they opted out by mistake" case; pair with a small confirm for the suppress direction.
- **Filter** (`contact-table.tsx`): add a toolbar select "Consent: All / Opted in / Opted out" via the
  DataTable `customFilters` / `renderCustomFilters` props; thread `isOptedOut` through the params type →
  service → action → hook.

The per-row **"Opted out" badge already exists** in `columns.tsx` (L72–102) — no change there.

> `wa_web/AGENTS.md` flags this as a customized Next.js — check `node_modules/next/dist/docs/` before
> touching router/server-action code. These changes are component/schema/service level, so low risk.

---

## Step 5 — Tests

- **Webhook (xUnit):** feed `InboundMessageWebhookHandler` a `type:"button"` payload with `button.text="Stop"`
  → assert `IsOptedOut == true`, `OptedOutAt` set; `interactive.button_reply.title="STOP"` → same;
  a normal button (`text:"Track order"`) → **not** opted out; re-opt-in `start` → `IsOptedOut == false`.
- **Template:** `SubmitAsync` on a marketing template with `buttons: []` → payload to Meta includes a
  `quick_reply` "Stop"; with an existing Stop → not duplicated; with 10 buttons → unchanged.
- **Contacts:** `GetPagedAsync(isOptedOut: true)` returns only suppressed contacts; `UpdateAsync` with
  `IsOptedOut:true` sets `OptedOutAt` + clears `HasOptedIn`; with `IsOptedOut:false` clears `OptedOutAt`
  and re-enables sending; setting `HasOptedIn:true` **and** `IsOptedOut:true` in one call leaves the
  contact suppressed (opt-out wins).

---

## Implementation order

```
Step 1  Webhook payload model (button/interactive)     ← unblocks everything
Step 2  Stop detection in inbound handler              ← the core fix (button taps opt out)
Step 3a Backend: enforce default Stop button at submit ← the "by default" guarantee
Step 3b Frontend: seed builder with Stop button        ← visible default
Step 4a Backend: admin-editable opt-out (DTO+service)  ← the escape hatch for a hard stop
Step 4b Backend: isOptedOut list filter
Step 4c Frontend: opt-out toggle + row action + filter ← findable & correctable cohort
Step 5  Tests
Optional  Customer re-opt-in keywords / payload (3c) / opt-out confirmation
```

---

## Files touched

| File | Change |
|---|---|
| `wa_api/.../Webhooks/Payloads/MetaWebhookEnvelope.cs` | Add `button` / `interactive` to inbound message model |
| `wa_api/.../Webhooks/Handlers/InboundMessageWebhookHandler.cs` | Detect Stop via button/interactive; preview text; (rec.) re-opt-in |
| `wa_api/.../Templates/TemplateService.cs` | `EnsureOptOutButton` called in `SubmitAsync` |
| `wa_api/.../Common/OptOut/StopKeywords.cs` *(new)* | Shared keyword set used by webhook + template |
| `wa_api/.../Contacts/Dtos/UpdateContactRequest.cs` | Add `bool? IsOptedOut` field |
| `wa_api/.../Contacts/ContactService.cs` | `UpdateAsync`: apply admin opt-out change; `GetPagedAsync`: `isOptedOut` filter |
| `wa_api/.../Contacts/Controllers/ContactsController.cs` | `isOptedOut` query param on `GetAll` (admin-only already) |
| `wa_web/features/templates/schema/template-schema.ts` | Seed default Stop button in `emptyBuilderValues()` |
| `wa_web/features/contacts/{schema,types,services,actions,hooks,components}` | Editable opt-out toggle + row quick-action + `isOptedOut` filter |
| `wa_api/wa_api.Tests/...` | Webhook + template + contacts tests |

## Out of scope (v1)

- **Opt-out confirmation message** (auto-reply "You've been unsubscribed") — allowed inside the just-opened
  24h window, but adds an outbound send inside the webhook job; defer.
- **Per-tenant configurable keywords / button label** — ship the English default first.
- **Retroactively editing already-approved templates** — Meta forbids it; only new submissions get the button.
- **Send-path payload component (3c)** unless/until template button labels are localized.
</content>
</invoke>
