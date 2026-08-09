# Going live with PayHere

**Status: config switched to live 2026-08-09 — NOT yet validated by a real transaction.** The API on
the VPS is running live PayHere credentials (`PayHere__Sandbox=false`, merchant `257759`), but
nothing so far proves PayHere *accepts* that merchant/secret pair for this domain — only a real
payment does. What follows is the record of what was changed plus the steps that are *not* config.

> ⚠️ **Live billing hazard right now:** the plan named **Free** is `IsOnline = true` at **$29 USD**.
> A CompanyAdmin clicking it gets a real PayHere popup charging a real card. Sandbox made this
> harmless before today; it is not harmless now. Fix: uncheck **Online** for `Free` in
> SuperAdmin → Plans.

## Config (done — `/opt/growchat/wa_api/.env`, applied 2026-08-09)

Applied with `docker compose up -d --force-recreate api` — no rebuild, since PayHere vars come from
`env_file: .env` and not the inline `environment:` block in `docker-compose.yml`.

| Key | Live value |
|---|---|
| `PayHere__MerchantId` | `257759` |
| `PayHere__MerchantSecret` | *(in `.env` on the VPS only — never committed)* |
| `PayHere__Sandbox` | `false` |
| `PayHere__ReturnUrl` | `https://growchatapp.croissance-digital.com/my-subscription?payhere=success` |
| `PayHere__CancelUrl` | `https://growchatapp.croissance-digital.com/my-subscription?payhere=cancelled` |
| `PayHere__NotifyUrl` | `https://growchatapi.croissance-digital.com/api/v1/webhooks/payhere` (unchanged from sandbox) |

Sandbox `.env` preserved at `/opt/growchat/wa_api/.env.bak.payhere-sandbox-20260809` — rollback is
`cp .env.bak.payhere-sandbox-20260809 .env && docker compose up -d --force-recreate api`.

Notes for next time:
- **Don't quote the secret** in `.env`. Compose's `env_file` parser doesn't reliably strip quotes and
  a literal `"` in the value breaks every MD5 hash silently.
- Return/Cancel: swap the **host only**, keep `?payhere=success` / `?payhere=cancelled` — the
  frontend keys off those query params on `/my-subscription`.
- No frontend change or Vercel redeploy is needed. `payhere.js` is the same script for both
  environments; the `sandbox` flag reaches the browser inside the API's signed checkout payload.
- `Cors__AllowedOrigins` on the VPS already contains `https://growchatapp.croissance-digital.com`
  (index 3). Without it the checkout POST is blocked before PayHere is ever reached.

## Not config — these gate whether checkout actually appears

1. **`Plan.IsOnline` is a DB flag.** The PayHere button only renders for plans with `IsOnline = true`
   (`MySubscriptionController.cs:75`), and the checkout endpoint hard-rejects otherwise with
   `PLAN_NOT_SELF_SERVICE` (`:161`). It defaults to `false`. Set it in **SuperAdmin → Plans → the
   "Online" checkbox**, per plan — there is no config equivalent.

2. **Currency must be one PayHere accepts** — LKR, USD, GBP, EUR, AUD. `PayHereOrder.Currency` is
   copied straight from `plan.Currency` with no validation (`:174`), and the plan form defaults new
   plans to **AED** (`plan-form.tsx:47`), which PayHere rejects. An AED plan flipped to Online will
   fail at PayHere's end with an unhelpful error.

   For the UAE audience, plans stay **USD-priced and USD-charged**; an indicative dirham figure is
   rendered beside the USD price on both the customer plan picker and the SuperAdmin plans table.
   That conversion is display-only (`GET /api/v1/fx/usd-aed`, 12h cached, falling back to the 3.6725
   peg) and is skipped entirely for non-USD plans. It does not make AED chargeable.

3. **USD settlement approval.** PayHere accounts are LKR-only by default. Every currently-online plan
   is USD, so the live merchant account must have USD settlement explicitly enabled — may require
   KYC/business verification. No code change fixes this.

4. **Domain registration.** `growchatapp.croissance-digital.com` must be registered under
   Integrations → Add Domain/App on the **live** dashboard; the Merchant Secret is issued per-domain.
   PayHere quotes up to 24h for approval. A secret from the wrong domain (or from sandbox) produces a
   hash mismatch and every payment fails.

## Production plan rows as of 2026-08-09

| Plan | Price | Currency | IsOnline | IsActive |
|---|---|---|---|---|
| Starter | 29.00 | USD | ✅ | ✅ |
| Free | 29.00 | USD | ✅ | ✅ |
| Pro | 99.00 | USD | ❌ | ✅ |
| T2-July-2000-uae-marist | 800.00 | AED | ❌ | ✅ |
| ZeroQuota / Marist Live test / TEST_UAE | — | — | ❌ | ❌ |

Two problems in that table:
- **Free** is online at $29 — see the hazard warning at the top. Uncheck Online.
- **Pro** ($99, active) is *not* online, so nobody can self-serve buy it. Possibly the inverse
  mistake; confirm whether that's intentional.

## Verification performed after the switch

- `docker compose ps` → api recreated, healthy; other 4 services untouched.
- `docker compose exec api env | grep PayHere` → all six live values present in the container.
- `GET https://growchatapi.croissance-digital.com/health` → 200.
- `POST https://growchatapi.croissance-digital.com/api/v1/webhooks/payhere` with a forged `md5sig`
  → **400** (endpoint live, signature verification rejecting). Unprefixed `/webhooks/payhere` → 404,
  confirming the `/api/v1` prefix is required.

Still outstanding: **one real card transaction** on the smallest online plan, to confirm the live
notify callback applies the subscription exactly as sandbox did.

## Security

The live Merchant Secret was pasted into a chat transcript during this switch. It signs every
checkout hash and verifies every notify callback — anyone holding it can forge a valid `md5sig` and
grant themselves a subscription. **Rotate it** in the PayHere dashboard (Integrations → the domain
entry → regenerate) once the flow is confirmed, then update `.env` and force-recreate `api`.

Never commit the secret. `contabo.md` (gitignored) is the place for live credentials, not this file.

## Known gotcha (kept from the original notes)

The app applies a global `/api/v1` prefix to every controller route (`RoutePrefixConvention` in
`Program.cs`). The webhook controllers declare `[Route("webhooks/payhere")]`, but the actual served
path is `/api/v1/webhooks/payhere`. This caused 404s on the notify callback once already — any
webhook URL registered with an external provider must include the prefix.

## Reference: the sandbox setup this replaced

- Merchant ID `1237318`, domain `localhost`, `Sandbox=true`
- Test cards: [PayHere Sandbox & Testing docs](https://support.payhere.lk/sandbox-and-testing) —
  e.g. Visa `4916217501611292`, any name/CVV/expiry.
