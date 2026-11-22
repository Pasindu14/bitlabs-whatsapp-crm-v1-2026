# Plan 005 — Bring the BitLabs WhatsApp chatbot into the CRM

Source: `E:\Github\bitlabs-whatsapp-chatbot-v1\bitlabs-whatsapp-chatbot-v1` — HEAD `c0f336d` (clean tree).
Target: this repo — HEAD `be87cf9`.
Status: **code complete on branch `feat/chatbot-integration` (2026-11-22). Not committed, not deployed,
migration not applied to any database.** See "Implementation status" at the end.

---

## 1. The short version

- The chatbot **is a fork of this CRM** (forked 2026-09-03, then campaigns / contact lists / analytics were
  deleted and the bot was built on top). Git histories are unrelated, so this is a **file-level port**, not a
  `git merge`.
- Compared today: **590 files exist in both, 123 differ, 172 exist only in the chatbot.** Of the 123, most are
  templates (CRM is ahead) or comment-only edits. The real hand-merge is about **25 files**.
- The database change is **purely additive**: 10 new tables, 11 new columns on 3 existing tables, 2 new
  indexes. **No CRM table is dropped, renamed or rewritten.**
- The bot is **off by default** for every company. Existing CRM tenants see no behaviour change until someone
  switches it on.
- Because the schema change is additive with DB defaults, **the old API image keeps working on the new
  schema**. Rollback = redeploy the previous image. No down-migration needed.

### What the first version of this plan missed

| # | Finding | Why it matters |
|---|---|---|
| 1 | Chatbot moved on one commit (`c0f336d`): new `BotProfiles.IndustryRules` column, clinic rules moved out of the prompt | Port from `c0f336d`, not `aa91671` |
| 2 | **Campaign messages have no `ConversationId`** (`CampaignBatchSendJob.cs:425`). The bot loads its transcript by conversation | A customer replying "yes, interested" to a campaign → the bot has no idea what they are replying to |
| 3 | **Bot and campaigns would share 4 Hangfire workers.** Each AI reply holds a worker for several seconds (2–6 s read delay + the Claude call + up to 4 s typing delay) | A campaign blast with the bot on starves campaign sends, or the reverse |
| 4 | **`Plan.FeatureFlags` is not enforced anywhere in the API**, and `CompanyAdmin` passes every `RequirePermission` | Day one, any company admin can switch the AI on and spend your Anthropic key for free |
| 5 | Bot template sends are **body-text only** (`SendTemplateAsync`). The CRM now has media-header and carousel templates | Picking one of those as a follow-up / handoff template gets rejected by Meta |
| 6 | `WabaHealthCheckJob`, `SubscriptionGate`, `TemplateMapper`, `TemplateService`, `AdminReportsService` are **newer in the CRM** than in the chatbot | Blind-copying any shared file from the chatbot would silently undo CRM fixes |
| 7 | Every inbound message enqueues `AiReplyJob` + `ClinicCrmLeadJob` even for companies with no bot | Two wasted jobs per inbound message during a campaign reply surge |

---

## 2. Rules that keep the CRM from breaking

1. **CRM file wins.** For every shared file, start from the CRM version and add the chatbot's hunks by hand.
   Never copy a shared file across. Only chatbot-only files are copied as-is.
2. **Additive schema only.** One new migration on the CRM chain. No drops, no renames, no type changes.
3. **Three off switches**, any one is enough:
   - `Bot:Enabled=false` (new global config kill switch, checked where the webhook enqueues)
   - no `Anthropic__ApiKey` → AI path skips itself, keyword rules still work (already built in)
   - `BotProfiles.IsEnabled=false` per company (default)
4. **Bot runs on its own Hangfire queue** so it can never take a worker from campaigns or webhooks.
5. **Every phase below ends in a green build + full test run** and is deployable on its own.

---

## 3. Database — what happens to every table

Applied by **one new migration `AddChatbotModule`** on top of `20260806095640_AddPayHere`.
Do **not** copy the chatbot's 19 migrations — they are a different chain on a different database.
The API runs `MigrateAsync()` on startup (`DataSeeder.cs:24`), so **deploying = migrating**. Back up first.

### 3.1 New tables (10) — created empty

| Table | Purpose | Key columns | Indexes / constraints |
|---|---|---|---|
| `BotProfiles` | One row per company: AI settings | `IsEnabled`, `BusinessName`, `BusinessDescription`, `ToneNotes`, `Location`, `ContactNote`, `CurrencyLabel`, `BookingInstructions`, `PreferredLanguage`, `DailyReplyLimit`, `FollowUpSteps` (jsonb), `ResumeBotAfterIdleHours` (default 24), `HandoffAlertPhones` (text[]), `HandoffAlertTemplateId`, `HandoffResumeMinutes` (default 0), `IndustryRules`, `BookingsEnabled` | UNIQUE `CompanyId`; FK Company (Restrict) |
| `AutoReplyRules` | Keyword rules (run before the AI) | `Name`, `Trigger`, `Terms` (text[]), `ReplyBody`, `Priority`, `IsEnabled` | UNIQUE (`CompanyId`,`Name`); (`CompanyId`,`Priority`) |
| `BotFaqs` | Q&A the AI may quote | `Question`, `Answer`, `SortOrder`, `IsEnabled` | (`CompanyId`,`SortOrder`) |
| `BotLinks` | Links the AI may share | `Title`, `Url`, `WhenToShare`, `Message`, `SortOrder`, `IsEnabled` | (`CompanyId`,`SortOrder`) |
| `AiReplyLogs` | One row per Claude call: tokens, cost, outcome | `ConversationId`, `InboundMessageId`, `Kind`, `Model`, `Outcome`, `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheWriteTokens`, `CostUsd` numeric(12,6), `LatencyMs`, `Iterations`, `ToolCallsJson` (jsonb), `Error` | (`CompanyId`,`CreatedAt`); `ConversationId` |
| `AppointmentTypes` | Bookable services | `Name`, `Description`, `DurationMinutes`, `Price` numeric(18,2), `BufferBefore/AfterMinutes`, `SlotIntervalMinutes`, `MinimumNoticeMinutes`, `BookingHorizonDays`, `ExternalTreatmentId`, `IsEnabled` | UNIQUE (`CompanyId`,`Name`) WHERE `IsActive` |
| `BusinessHours` | Opening hours per weekday | `DayOfWeek`, `StartLocal`, `EndLocal` (time) | (`CompanyId`,`DayOfWeek`) |
| `CalendarBlackouts` | Closures | `StartUtc`, `EndUtc`, `Reason` | (`CompanyId`,`StartUtc`) |
| `Appointments` | Bookings (bot or staff) | `AppointmentTypeId`, `ContactId`, `ConversationId`, `StartUtc`, `EndUtc`, `BlockStartUtc`, `BlockEndUtc`, `Status`, `Source`, `Notes`, `HoldExpiresAt`, `CancelledAt`, `CancellationReason`, `ReminderSentAt`, `CreatedByUserId`, `ExternalBookingId`, `ExternalCustomerId`, `ExternalSyncStatus`, `ExternalSyncError`, `ExternalSyncedAt` | (`CompanyId`,`StartUtc`); `ContactId`; `ConversationId`; `AppointmentTypeId`; `HoldExpiresAt` partial; FK Contact / AppointmentType (Restrict), Conversation (SetNull); **`EX_Appointments_NoOverlap`** (below) |
| `ClinicCrmConnections` | A company's link to its clinic CRM | `BaseUrl`, `ClientId`, `ClientSecret` (encrypted), `IsEnabled`, `LastSuccessAt`, `LastError`, `LastErrorAt` | UNIQUE `CompanyId` WHERE `IsActive` |

All ten carry the standard `Id`, `CompanyId`, `IsActive`, `CreatedAt`, `UpdatedAt`, a Company FK and the tenant query filter.

### 3.2 Existing tables that get new columns (3)

| Table | New column | Type | Default for existing rows |
|---|---|---|---|
| `Companies` | `TimeZoneId` | varchar(64) NOT NULL | `'UTC'` |
| `Conversations` | `BotState` | varchar(20) NOT NULL | **`'Active'`** — hand-edit; EF generates `''`, which crashes the inbox on every old thread |
| `Conversations` | `BotPausedAt` | timestamptz NULL | null |
| `Conversations` | `BotPausedByUserId` | uuid NULL | null |
| `Conversations` | `BotResumesAt` | timestamptz NULL | null |
| `Conversations` | `BotAsksMade` | int NOT NULL | 0 |
| `Conversations` | `BotConvertedAt` | timestamptz NULL | null |
| `Conversations` | `BotFollowUpSentAt` | timestamptz NULL | null |
| `Conversations` | `BotFollowUpStepsSent` | int NOT NULL | 0 |
| `Messages` | `IsAutoReply` | boolean NOT NULL | false |
| `Messages` | `MetaCostUsd` | numeric(10,6) NULL | null |

New indexes on `Conversations`: `IX_Conversations_BotResumesAt` (WHERE `BotResumesAt` IS NOT NULL) and
`IX_Conversations_PausedIdle` on (`BotState`,`LastMessageAt`) WHERE `BotState`='Paused'.

All are constant-default adds → metadata-only in Postgres, no table rewrite, no long lock on `Messages`.

### 3.3 Tables that must NOT change

`Campaigns`, `CampaignContacts`, `CampaignContactLists`, `CampaignRecipients`, `ContactLists`,
`ContactListMembers`, `TemplateMediaSamples`, `Templates`, `Notifications`, `Contacts`, `Plans`,
`Subscriptions`, `Invoices`, `Packages*`, `PayHereOrders`, `WabaConnections`, `Users`, `AuditLogs`,
`WhatsAppWebhookEvents`, everything else.

Three traps here — the chatbot **deleted** these and the generated migration must not:
- `Messages.CampaignId`, `Messages.CampaignRunNumber` and the `CampaignId` index → **keep**.
- `Notifications.CampaignId`, `Notifications.TemplateId`, their unique indexes and FKs → **keep**.
- The CRM `NotificationType` enum (quota warnings, campaign failed/throttled/completed, template approved)
  → **keep**, and add one member `System` for bot handoff alerts (stored as text, no migration).

**Check before committing the migration:** the `Up()` must contain only `CreateTable`, `AddColumn`,
`CreateIndex` and the two raw SQL blocks below. Any `DropColumn` / `DropTable` / `DropIndex` /
`AlterColumn` means an entity was merged wrong — fix the entity, regenerate.

### 3.4 Hand-written SQL inside the migration

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
```

```sql
ALTER TABLE "Appointments"
ADD CONSTRAINT "EX_Appointments_NoOverlap"
EXCLUDE USING gist ("CompanyId" WITH =, tstzrange("BlockStartUtc","BlockEndUtc") WITH &&)
WHERE ("Status" <> 'Cancelled');
```

This is what makes a double booking impossible at the database level. EF cannot express it.
Skip the chatbot's two data backfills (`AiReplyLogs.CostUsd`, `BotProfiles.IndustryRules`) — the tables start empty here.

### 3.5 Template category

`TemplateCategory` gains `Utility` (follow-ups are utility-rated, cheaper than marketing). It is stored as
text and `TemplateMapper` already sends `Category.ToString().ToUpperInvariant()` → **no migration**.

---

## 4. Backend (`wa_api`)

### 4.1 Copy in as-is (chatbot-only files)

- `Features/Bot/**` (41 files), `Features/Calendar/**` (22), `Features/ClinicCrm/**` (16)
- `Common/Tenancy/ScopedTenantContext.cs` → replaces `HttpTenantContext.cs` (delete the old one; same
  behaviour in a request, plus `EnterCompany` for jobs)
- `Infrastructure/Http/DualStackConnect.cs` (IPv4-first to Meta; fixed 15 s hangs)
- `Features/Messages/MetaCostCalculator.cs`
- `Features/Webhooks/Processing/WebhookPostCommitActions.cs`
- `Features/Templates/TemplateFill.cs`, `StarterTemplates.cs` (compile against the CRM's
  `TemplateComponents`, which is a superset)
- `Features/Conversations/Dtos/PauseBotRequest.cs`

Do **not** copy: the chatbot's `Features/Templates/Dtos/TemplateDtos.cs`, its `TemplateService`,
`TemplateMapper`, `MetaTemplateClient`, `TemplateStatus*` — the CRM's template module stays.

### 4.2 Merge by hand (CRM file + chatbot hunks)

| File | Keep from CRM | Add from chatbot |
|---|---|---|
| `Program.cs` | campaigns, contact lists, analytics, media uploader DI; campaign + quota recurring jobs; `TemplateStatusSyncJob` registration as is | `ScopedTenantContext` registration; bot / calendar / clinic DI; Anthropic options + HttpClient; cost calculators; `WebhookPostCommitActions`; `DualStackConnect` on both Meta HttpClients; `bot-auto-resume` recurring job |
| `AppDbContext.cs` | every existing set and config | 10 new `DbSet`s + configs; `Company.TimeZoneId`; `Message.MetaCostUsd` precision; `Conversation.BotState` + 2 indexes; `internal ITenantContext Tenant` |
| `Conversation.cs`, `Company.cs`, `Message.cs` | all existing fields (incl. `CampaignId`, `CampaignRunNumber`) | `Bot*` fields, `TimeZoneId`, `IsAutoReply`, `MetaCostUsd` |
| `InboundMessageWebhookHandler.cs` | opt-out handling, media handling | post-commit notify; Meta `sentAt` for the 24 h window; late-delivery guard (10 min); team-number skip; reset follow-up counters; enqueue `AiReplyJob`; clinic lead save |
| `MessageStatusWebhookHandler.cs` | **campaign recipient sync** (`campaignMessageUpdates`) and quota refunds | `MetaCostUsd` on first pricing; `QueueStatusPush` realtime |
| `WebhookProcessingJob.cs` | — | `postCommit.Clear()` per attempt, `postCommit.RunAsync()` after commit |
| `WhatsAppWebhookController.cs` | — | `TryLookupAsync`: undecryptable connection → log + 200, never a 5xx on the shared webhook URL |
| `WabaConnectionService.cs` | — | `ReplaceUndecryptableSecretsAsync` |
| `IWhatsAppMessageSender.cs` / `WhatsAppMessageSender.cs` | everything (CRM is a strict subset) | `SendTemplateAsync`, `ShowTypingAsync`, `preview_url` for links, shared `PostToMetaAsync` |
| `ConversationService`, `IConversationService`, `ConversationsController`, `ConversationResponse` | — | `SetBotStateAsync` (pause / resume), manual send pauses the bot, 3 new DTO fields |
| `IChatNotifier` / `ChatNotifier` | — | `BotStateChangedAsync`, `BotTypingAsync`, `MessageStatusAsync` |
| `AuditInterceptor.cs` | — | tenant from the DbContext's own scope (jobs get a correct `CompanyId` stamp); user id from the `sub` claim |
| `Permission.cs` | `ManageTemplate`, `ScheduleCampaign`, `ContactList`, `Analytics` | `ManageBot`, `ManageCalendar` |
| `DataSeeder.cs` | CRM seeder as is | add the two new permissions to the seeded paid plans (fresh DBs only — the seeder skips when plans exist) |
| `Template.cs` | CRM entity | `Utility` enum member only |
| `ITemplateService` / `TemplateService` / `TemplatesController` | CRM versions | `GetApprovedAsync`, `CreateStarterPackAsync` + their endpoints |
| `AdminReportsService` + DTO | CRM version (newer) | AI cost + Meta cost columns |
| `MonitoringService` + DTO | CRM version | the small bot additions |
| `wa_api.csproj` | `ClosedXML` | `Anthropic` 12.45.0, `UserSecretsId` |
| `appsettings.json` | `CampaignWaitCapMs`, burst settings | `Anthropic:Pricing`, `Meta:Pricing`, `ClinicCrm`, `Bot` sections |

Leave alone (the CRM version is right, the chatbot's differs only by removals or is older):
`ISubscriptionGate` / `SubscriptionGate` (keep `GetRemainingQuotaAsync`), `SubscriptionMeter`,
`RateLimitOptions`, `MetaPolicyErrorCodes` (keep `IsCampaignPause`), `WabaHealthCheckJob`,
`NotificationService` signature, `ContactService`, `MessageService`, `TemplateMapper`, `MetaTemplateClient`,
`TemplateStatusSyncJob`, `TemplateStatusWebhookHandler`.

Two small adapters this forces:
- Bot code calls `notifications.CreateAsync(companyId, type, title, body)`. The CRM signature has
  `campaignId, templateId` in the middle → add an overload, don't change the existing one.
- Chatbot test fakes for `ISubscriptionGate` implement `EnsureCanSendBatchAsync` → switch them to
  `GetRemainingQuotaAsync`.

### 4.3 New work — the gaps (not in either repo today)

| # | Change | Where | Size |
|---|---|---|---|
| G1 | **Campaign context for the bot.** When building the transcript, also load campaign messages sent to the same contact + WABA in the last 7 days (`ConversationId IS NULL AND CampaignId IS NOT NULL`) and merge them in by time | `AiReplyJob.LoadTranscriptAsync` | S |
| G2 | **Own queue.** `[Queue("bot")]` on `AiReplyJob`, `BotFollowUpJob`, `ClinicCrmLeadJob`, `ClinicCrmSyncJob`; a second Hangfire server for `bot` with `Hangfire:BotWorkerCount` (default 2). Main server stays `["webhooks","default"]` × 4 | `HangfireExtensions.cs` | S |
| G3 | **Plan gate.** `IBotEntitlement`: company's active plan has `manage_bot` in `FeatureFlags`. Enforced when enabling the profile and again in `AiReplyJob` / `BotFollowUpJob`. Scoped to bot + calendar only — do **not** start enforcing the other flags, that would lock out current customers | new service + 3 call sites | M |
| G4 | **Text-only template pickers.** `GetApprovedAsync` gets a filter: no media header, no carousel. Used by the follow-up and handoff-alert pickers; the send path re-checks and skips with a log | `TemplateService`, `BotFollowUpJob`, `StaffAlertService` | S |
| G5 | **Enqueue gate.** The handler already loads the `BotProfile` once per batch — enqueue `AiReplyJob` only when the company has an enabled profile or any enabled rule, and `ClinicCrmLeadJob` only when a connection exists. Plus the global `Bot:Enabled` switch | `InboundMessageWebhookHandler` | S |
| G6 | **Stale guard at run time.** If the message is older than 10 min when the job actually starts, skip and leave it for a human (today the age check only runs at webhook time) | `AiReplyJob` | S |

---

## 5. Frontend (`wa_web`)

### Copy in
- `features/bot/**`, `features/calendar/**`, `features/clinic-crm/**`
- `app/(protected)/bot/page.tsx`, `calendar/page.tsx`, `calendar/settings/page.tsx`

### Merge (CRM file + chatbot hunks)
| File | Add |
|---|---|
| `components/app-sidebar.tsx` | **Chatbot** + **Calendar** entries. Keep Overview, Campaigns, Contact Lists, Analytics |
| `features/team/permissions.ts` | `manage_bot`, `manage_calendar`. Keep the four the chatbot removed. This also makes them tickable in the SuperAdmin plan form |
| `features/conversations/*` (`message-thread`, `inbox`, `conversation-store`, `use-chat-hub`, `use-conversations`, `types`, service, actions) | bot pause / resume bar, typing bubble, live status ticks, "bot handed this over" state |
| `lib/hooks/query-keys.ts` | bot, calendar, clinic-crm keys. Keep campaign / list / analytics keys |
| `features/notifications/*`, `notification-bell.tsx` | `System` type rendering (handoff alerts) |
| `features/admin-reports/usage-report.tsx`, `types.ts` | AI + Meta cost columns |
| `features/templates/*` | **CRM builder stays.** Add only the `Utility` category option and a "Create starter pack" button |

Leave alone: `auth.ts`, `auth.config.ts`, `lib/api/client.ts`, `sign-in/page.tsx`, `company-logo.tsx`,
`current-plan-card.tsx`, `package.json`, `playwright.config.ts` — CRM versions are newer or branding-specific.
ClinicCrm settings stay hidden unless a connection exists or SuperAdmin opens it (single-client feature).

---

## 6. Tests

Port from `wa_api.Tests` (chatbot-only files):
- `Bot/*` (17), `Calendar/*` (2), `ClinicCrm/*` (8)
- `Common/AuditInterceptorTests`, `Common/DualStackConnectTests`
- `Messages/TypingIndicatorTests`, `Webhooks/MessageStatusPricingTests`,
  `Webhooks/WhatsAppWebhookUndecryptableConnectionTests`
- `Templates/FollowUpTemplateTests`, `Templates/StarterTemplateTests` (adapt to the CRM template service)

Do **not** port `TemplateMapperTests`, `TemplateServiceTests`, `TemplateStatus*Tests` — they test the
chatbot's template module, which is not coming across.

Keep the CRM versions of the 4 shared test files that differ (`AdminReportsServiceTests`,
`FreeWindowBillingTests`, `SubscriptionGateSplitTests`, `InboundMediaWebhookHandlerTests`) and extend their
fakes for the new constructor parameters.

New tests for the gaps: campaign message appears in the bot transcript (G1); disabled plan flag blocks the
AI (G3); media / carousel template is refused for follow-ups (G4); no job enqueued for a bot-less company (G5).

---

## 7. Config and deploy

- `Anthropic__ApiKey` as an env var on the CRM VPS (`/opt/growchat`). Never in committed json.
- `Bot__Enabled` (global switch, ship as `false`), `Hangfire__BotWorkerCount=2`.
- `Meta:Pricing` rate card and `Anthropic:Pricing` come from `appsettings.json`.
- Webhook URL does not change — the CRM already receives the Meta events.
- 2 more Hangfire workers = up to 2 more DB connections. Check the pool size on the connection string first.
- **Before deploy:** DB backup, and a note of the current image tag for rollback.
- **Rollback:** redeploy the previous image. The new columns have DB defaults and the new tables are ignored
  by old code, so no schema rollback is needed.

---

## 8. Order of work

Branch `feat/chatbot-integration`. Each phase ends with `dotnet build` + full `dotnet test` green.

| Phase | Work | Proof it is safe |
|---|---|---|
| 0 | Confirm the CRM has no pending model changes (`dotnet ef migrations has-pending-model-changes`). Copy prod DB to a local Postgres | Clean baseline |
| 1 | **Plumbing with no bot:** `ScopedTenantContext`, `AuditInterceptor`, `DualStackConnect`, `WebhookPostCommitActions`, undecryptable-secret handling | All 26 CRM test files green; inbox realtime still works |
| 2 | **Schema:** entities + `AppDbContext` + migration `AddChatbotModule` with the hand edits (§3) | Migration `Up()` reviewed line by line; applied to the prod copy; old build still runs against it |
| 3 | **Calendar module** (API + tests) | Calendar tests green |
| 4 | **Bot engine:** copy-in, sender additions, handler hooks, conversation pause / resume, notifier events, G2 + G5 + G6 | Bot tests green; with `Bot:Enabled=false` zero jobs are enqueued |
| 5 | **CRM-specific gaps:** G1 campaign context, G3 plan gate, G4 template filter, `Utility` category, starter pack | New gap tests green |
| 6 | **ClinicCrm** + cost tracking + admin report columns | ClinicCrm tests green |
| 7 | **Frontend:** copy-ins, merges, `tsc` + lint + build | No type errors; campaigns / templates / contact lists pages unchanged |
| 8 | **End-to-end on a test WABA:** keyword rule, AI reply, booking, double-booking refusal, handoff + alert, agent takeover pauses the bot, auto-resume, in-window follow-up, out-of-window template follow-up, STOP opt-out, **campaign send → customer reply → bot answers in context**, and a campaign running while the bot is busy | Checklist signed off |
| 9 | **Deploy dark:** `Bot:Enabled=false`. Watch a day of normal campaign traffic | No change in send rate, webhook lag or error rate |
| 10 | Set `Bot:Enabled=true`, tick `manage_bot` on one plan, enable one pilot company | Pilot runs clean for a week |

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| A merged entity makes EF drop a CRM column | §3.3 check on the migration before it is committed |
| Bot talks over a human agent | Already handled: a manual send pauses the bot; `BotAutoResumeJob` hands it back after the idle window |
| Campaign reply surge floods the bot | Own queue (G2), stale guard (G6), `DailyReplyLimit` (500 / company / day), then keyword fallback |
| AI spend with nobody paying | Plan gate (G3) + daily limit. Token cost is logged per company in `AiReplyLogs` but **not billed** — pricing decision below |
| Follow-up templates spend the customer's message quota | By design: `SendTemplateAsync` reserves quota like any business-initiated send. In-window bot replies are free (`chargeCredit: false`). Say so in the UI |
| `Company.TimeZoneId` defaults to `UTC` | Calendar settings screen prompts for the real one. Set it per company before enabling bookings, or slots are offered in UTC |
| Two repos drifting | Freeze the chatbot repo the day Phase 4 starts. Any fix after that goes into the CRM only |
| Agents still don't see the campaign message inside the inbox thread | Not made worse by this work. Optional follow-up: stamp `ConversationId` on campaign messages at send time |

---

## 10. Decisions needed

1. **Live chatbot customers on the second VPS (`77.237.245.19`, separate DB).**
   Recommendation: **keep that stack running** through Phases 0–10, then migrate once the pilot is clean.
   The migration is a one-off copy in FK order — Companies → Users → WabaConnections → Contacts →
   Conversations → Messages → Templates → Bot* → Calendar* → ClinicCrmConnections — with three catches:
   secrets must be re-encrypted if the two `Encryption:Key` values differ; `Users.Email` and
   `WabaConnections.PhoneNumberId` are unique and can collide; each company needs a plan + subscription in
   the CRM. The Meta webhook for those numbers is repointed to the CRM API at cutover.
2. **How the AI is sold.** Recommendation: a plan flag now (G3), a per-plan monthly AI-reply cap later using
   the `AiReplyLogs` counts that are already recorded.

---

## 11. Implementation status (2026-11-22)

Branch `feat/chatbot-integration`. Phases 1–7 are done in code. Phases 8–10 (end-to-end on a test WABA,
dark deploy, pilot) are not started.

### Verified
- Backend builds with 0 errors. **671 tests pass**: the CRM's original 169, the ported chatbot tests, and
  25 new ones for the gaps (plan gate, bot gate, campaign context, stale guard, enqueue gate, text-only
  templates).
- Migration `20261122063748_AddChatbotModule`: `Up()` is 10 `CreateTable`, 11 `AddColumn`, 18 `CreateIndex`
  and the two hand-written SQL blocks. The generated SQL script has **no** `DROP` and no `ALTER COLUMN`.
- Web app: `tsc` clean, `next build` succeeds, all existing routes still present plus `/bot`, `/calendar`,
  `/calendar/settings`.

### Not verified
- The API has not been started and the migration has not been run against Postgres. The exclusion
  constraint and `btree_gist` are therefore untested on a real database.
- No message has gone through Meta or Claude from this branch.

### How it differs from the plan above
- **G3 went further:** `[RequirePlanFeature]` enforces `manage_bot` / `manage_calendar` on the bot, clinic
  and calendar endpoints, and `GET /bot/access` tells the web app what to show. The sidebar, the inbox bot
  bar and the three pages all hide themselves for a company whose plan lacks the feature.
- **One gate for everything:** `IBotGate` = platform switch (`Bot:Enabled`) + plan flag + "an enabled
  profile or rule exists". The webhook, both bot jobs and the agent-reply auto-pause all ask it.
- **Agent replies only pause a bot that exists.** Otherwise every manual reply in a bot-less company would
  be stamped "paused" and un-stamped an hour later.
- **Template builder:** the `Utility` option was not added to the builder. Utility templates arrive through
  the starter pack button on the Templates page, which covers follow-ups, appointment notices and the staff
  alert.
- **Campaign context** is given to the reply job only, not to the follow-up job.
- `DataSeeder` adds the two new flags to the seeded **Pro** plan only.

### Before the first deploy
1. Back up the database. Starting the API applies the migration.
2. Leave `Bot__Enabled` unset (off). Confirm campaigns and the inbox behave as before.
3. Add `Anthropic__ApiKey` and `Bot__Enabled=true` to the server `.env`.
4. As SuperAdmin, tick **Manage Chatbot** and **Manage Calendar** on the plan that should include them.
