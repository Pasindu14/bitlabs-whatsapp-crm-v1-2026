# Going live with PayHere

Checklist for switching the PayHere integration from sandbox to production. Not just a credential swap — a few things need to line up on both the code/config side and PayHere's side.

## Config changes (mechanical — update `/opt/growchat/wa_api/.env` on the VPS, then `docker compose up -d --force-recreate api`, no rebuild needed)

1. **`PayHere__MerchantId`** — swap sandbox value for the live Merchant ID
2. **`PayHere__MerchantSecret`** — swap sandbox value for the live Merchant Secret (per-domain, see below)
3. **`PayHere__Sandbox`** — flip from `true` to `false`. This is passed straight into `payhere.startPayment()`'s payload and tells PayHere's JS SDK to hit the live endpoint instead of sandbox; leaving it mismatched with the credentials will fail.
4. **`PayHere__ReturnUrl`** / **`PayHere__CancelUrl`** — currently point at `http://localhost:3000/my-subscription?...` for testing. Update to wherever the real web app is deployed (e.g. the Vercel domain already in the API's CORS allowlist).
5. **`PayHere__NotifyUrl`** — stays as-is (`https://growchatapi.croissance-digital.com/api/v1/webhooks/payhere`), doesn't change between sandbox/live.

## Things on PayHere's side (not code — do these in the **live**, not sandbox, merchant dashboard)

6. **Register the production web domain** under Integrations → Add Domain/App on the live dashboard to get the live Merchant Secret for that domain. PayHere notes this can take up to 24h for approval.
7. **Confirm the live merchant account is approved for USD settlement.** By default PayHere accounts are LKR-only; since plans are priced in USD for the UAE audience, this needs to be explicitly enabled with PayHere — may need KYC/business verification if not already done for the live account.

## Before opening it to real customers

8. Do **one real test transaction** yourself (smallest paid plan, your own card) to confirm the live notify webhook behaves identically to sandbox. Mechanically it's the same code path, but worth one dry run with real money before customers hit it.

## Reference: current sandbox setup (for comparison)

- Merchant ID: `1237318` (sandbox)
- Domain registered: `localhost` (sandbox only — the live account needs its own domain registration, see #6)
- `NotifyUrl`: `https://growchatapi.croissance-digital.com/api/v1/webhooks/payhere` (same URL will be used in production)
- Sandbox test cards: see [PayHere Sandbox & Testing docs](https://support.payhere.lk/sandbox-and-testing) — e.g. Visa `4916217501611292` for a successful payment, any name/CVV/expiry.

## Known gotcha hit during setup (in case it resurfaces)

The app applies a global `/api/v1` prefix to every controller route (`RoutePrefixConvention` in `Program.cs`). The PayHere/Stripe webhook controllers declare `[Route("webhooks/payhere")]` / `[Route("webhooks/stripe")]`, but the **actual served path includes the prefix**: `/api/v1/webhooks/payhere`, not `/webhooks/payhere`. Got this wrong once already (404s on the notify callback) — double-check any webhook URL registered with an external provider includes `/api/v1`.
