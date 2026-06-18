# PAS-13 — Do Not Run Schedule or Immediate Send if Full Campaign Tokens Not Available

Block a campaign launch (both **Immediate** and **Scheduled/Recurring**) when the company's
remaining message quota is less than the total recipient count. Also close the secondary gap
where campaign sends never increment the subscription usage counter.

---

## Background & Root Cause

The `POST /api/v1/campaigns/{id}/launch` endpoint carries `[RequireActiveSubscription]`, which
checks that an active subscription exists and that `MessagesUsedThisPeriod < MonthlyMessageQuota`
— but it only checks whether _at least one_ message can be sent. It does **not** compare
the full recipient count against the remaining quota.

There are two gaps:

| # | Gap | Location | Effect |
|---|-----|----------|--------|
| A | No pre-flight quota check at launch | `CampaignService.LaunchAsync` | A 5,000-recipient campaign launches even with 100 messages left |
| B | Campaign sends never increment usage | `CampaignBatchSendJob` | Quota counter stays at zero for all campaign traffic |

Gap B must be fixed first because it is a data-correctness issue that also powers Gap A's check.

---

## Decisions (baked in — change if you disagree)

| Topic | Decision |
|-------|----------|
| When to check | At `LaunchAsync`, **before** any Hangfire job is enqueued — both Immediate and Scheduled/Recurring |
| What to count | Distinct recipients: union of all targeted contact-list members + individually selected contacts, deduped (same logic as `CampaignLaunchJob`) |
| Quota formula | `remaining = Plan.MonthlyMessageQuota − MessagesUsedThisPeriod` |
| Block condition | `totalRecipients > remaining` → throw `BusinessRuleException("INSUFFICIENT_QUOTA", ...)` |
| Scheduled campaigns (fire-time) | Re-check inside `CampaignLaunchJob` at fire time; if quota now insufficient → mark campaign `Failed` with `FailureReason = "INSUFFICIENT_QUOTA_AT_FIRE_TIME"` |
| Usage tracking | Batch-increment `MessagesUsedThisPeriod` once per 50-record batch (not per message) for performance |
| Resume guard | Check remaining Queued recipients vs quota on resume — optional but low-risk |
| SuperAdmin | Inherits existing bypass in `SubscriptionGate` — no changes needed |
| Error code | Reuse `INSUFFICIENT_QUOTA` (already used by `SubscriptionGate`) |

---

## Step 1 — Track usage inside `CampaignBatchSendJob` _(fix first)_

**File:** `wa_api/wa_api/Features/Campaigns/Jobs/CampaignBatchSendJob.cs`

After the per-batch loop completes, add a **single batch increment** to the company's subscription:

```
// after processing each batch of 50:
int successCount = batchResults.Count(r => r.Status == RecipientStatus.Sent);
if (successCount > 0)
    await IncrementSubscriptionUsageAsync(campaign.CompanyId, successCount, ct);
```

Implement `IncrementSubscriptionUsageAsync` following the same pattern as
`WhatsAppMessageSender.cs` lines 64–75:

1. Load the active subscription for `companyId` (ignore query filters — job has no tenant context).
2. `sub.MessagesUsedThisPeriod += delta`.
3. `await db.SaveChangesAsync(ct)`.

Inject the same `AppDbContext` already in scope. No new service needed.

**Why batch, not per-message:** one DB round-trip per 50 messages instead of 50 — reduces lock
contention at high volume without sacrificing correctness (worst-case drift = 49 unrecorded
messages if the process crashes mid-batch, which is acceptable).

---

## Step 2 — Pre-flight quota check in `LaunchAsync`

**File:** `wa_api/wa_api/Features/Campaigns/CampaignService.cs`  
**Method:** `LaunchAsync` (~line 150, after "has at least one recipient" validation)

```
// 1. Count distinct recipients (same dedup logic as CampaignLaunchJob)
var recipientIds = await ResolveDistinctRecipientIdsAsync(campaign, ct);
int totalRecipients = recipientIds.Count;

// 2. Load subscription + plan
var sub = await db.Subscriptions
    .Include(s => s.Plan)
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(s => s.CompanyId == campaign.CompanyId && s.IsActive, ct)
    ?? throw new BusinessRuleException("NO_ACTIVE_SUBSCRIPTION", "No active subscription found.");

// 3. Check remaining quota
int remaining = sub.Plan.MonthlyMessageQuota - sub.MessagesUsedThisPeriod;
if (totalRecipients > remaining)
    throw new BusinessRuleException(
        "INSUFFICIENT_QUOTA",
        $"Campaign requires {totalRecipients} messages but only {remaining} remain in your quota.");
```

Extract the recipient-resolution logic into a private `ResolveDistinctRecipientIdsAsync` helper
(reused by both `LaunchAsync` and later by `CampaignLaunchJob`).

This blocks **all** schedule types (Immediate, OneTime, Recurring) before any job is enqueued.

---

## Step 3 — Re-check quota at Hangfire fire time

**File:** `wa_api/wa_api/Features/Campaigns/Jobs/CampaignLaunchJob.cs`  
**Method:** `RunAsync` (start of method, before snapshotting contacts)

A OneTime campaign scheduled for tomorrow may fire when quota has changed:

```
// Load subscription + plan (ignore filters — no tenant context in Hangfire)
var sub = await db.Subscriptions
    .Include(s => s.Plan)
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(s => s.CompanyId == campaign.CompanyId && s.IsActive, ct);

int remaining = sub?.Plan.MonthlyMessageQuota - sub?.MessagesUsedThisPeriod ?? 0;

// Resolve recipients first (needed for count)
var recipientIds = await ResolveDistinctRecipientIdsAsync(campaign, ct); // shared helper
if (recipientIds.Count > remaining)
{
    campaign.Status = CampaignStatus.Failed;
    campaign.FailureReason = "INSUFFICIENT_QUOTA_AT_FIRE_TIME";
    campaign.CompletedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);
    return; // abort — do not enqueue CampaignBatchSendJob
}
```

The `FailureReason` column needs a new migration if not already present on the `Campaign` entity.

---

## Step 4 — Frontend error surface

**File:** `wa_web/features/campaigns/hooks/use-campaigns.ts`  
**Hook:** `useLaunchCampaign` (~line 164)

Map `INSUFFICIENT_QUOTA` to a human-readable toast:

```ts
onError: (error) => {
  if (error.code === "INSUFFICIENT_QUOTA") {
    toast.error(error.message); // backend message already includes the numbers
  } else {
    toast.error("Failed to launch campaign.");
  }
}
```

**File:** `wa_web/features/campaigns/components/campaign-dialogs.tsx`  
In the launch confirmation dialog, optionally surface a quota preview:
_"This campaign will send to {n} contacts. You have {remaining} messages remaining."_
(data comes from a new `GET /api/v1/subscriptions/me` or existing quota endpoint.)

---

## Step 5 — Resume guard _(optional, low risk)_

**File:** `wa_api/wa_api/Features/Campaigns/CampaignService.cs`  
**Method:** `ResumeAsync` (~line 210, before setting status back to `Running`)

```
int queuedCount = await db.CampaignRecipients
    .Where(r => r.CampaignId == campaign.Id && r.Status == RecipientStatus.Queued)
    .CountAsync(ct);

if (queuedCount > remaining)
    throw new BusinessRuleException(
        "INSUFFICIENT_QUOTA",
        $"Campaign has {queuedCount} messages left to send but only {remaining} remain in quota.");
```

---

## Implementation Order

```
Step 1  Usage tracking in CampaignBatchSendJob      ← fixes data; unblocks Steps 2 & 3
Step 2  Pre-flight check in LaunchAsync             ← core ticket requirement
Step 3  Fire-time check in CampaignLaunchJob        ← safety net for scheduled campaigns
Step 4  Frontend error mapping                      ← UX polish
Step 5  Resume guard                                ← optional
```

---

## Files Touched

| File | Change |
|------|--------|
| `wa_api/.../Campaigns/Jobs/CampaignBatchSendJob.cs` | Add batch usage increment |
| `wa_api/.../Campaigns/CampaignService.cs` | Add pre-flight check + resume guard + shared helper |
| `wa_api/.../Campaigns/Jobs/CampaignLaunchJob.cs` | Add fire-time re-check |
| `wa_api/.../Campaigns/Entities/Campaign.cs` | Add `FailureReason` string property |
| `wa_api/.../Migrations/` | New migration for `FailureReason` column |
| `wa_web/.../campaigns/hooks/use-campaigns.ts` | Map `INSUFFICIENT_QUOTA` error |
| `wa_web/.../campaigns/components/campaign-dialogs.tsx` | Optional quota preview in launch dialog |

---

## Out of Scope

- Quota reservation / pre-deduction at launch time (overkill — check-then-fire is sufficient)
- Partial-send logic ("send as many as quota allows") — not requested, adds complexity
- Recurring campaign per-recurrence quota check beyond the fire-time check in Step 3
