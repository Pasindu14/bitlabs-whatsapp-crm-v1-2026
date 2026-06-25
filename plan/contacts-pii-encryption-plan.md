# Plan: Encrypt `Contacts` PII (Phone + Name) — Option 4

> **Status:** Awaiting confirmation on 2 open decisions (see end).
> **Owner:** Backend (wa_api)
> **Regulation driver:** UAE PDPL — Federal Decree-Law No. 45 of 2021, **Article 7** (names *encryption* and *pseudonymization* as required security measures). Full enforcement expected **1 Jan 2027**.
> **Requirement source:** Company DPO — contact PII must not be readable to someone holding DB credentials or a database backup/dump.

---

## Objective

Make `Phone` and `Name` on the `Contacts` table **unreadable at rest** (in the database and in backups) using **AES-256-GCM**, while keeping every existing operation fast and indexed:

- Per-message **Meta inbound-webhook** contact lookup (hot path — runs on every inbound WhatsApp message)
- Duplicate detection (per-company phone uniqueness)
- Excel import upsert
- Contact list + conversation inbox search

The technique: **application-level field encryption** (the actual values become ciphertext) **plus HMAC "blind index" columns** (deterministic, one-way) so lookups stay a normal indexed equality match. This maps 1:1 onto PDPL Art. 7's two named measures — *encryption* (AES-GCM) and *pseudonymization* (HMAC blind index).

### Threat model this satisfies

| Who has access | After this change |
|---|---|
| The app (holds the key) | reads plaintext (normal operation) |
| Person with DB credentials / Supabase console | sees **ciphertext only** |
| Someone with a `pg_dump` / backup file | sees **ciphertext only** |
| Stolen physical disk | blocked |

To read a phone/name you need **both** DB access **and** the app's encryption key (which lives outside the database).

---

## Two findings from the existing code that this plan must handle

1. **Audit-log plaintext leak.** `Common/Audit/AuditInterceptor.cs` (`Serialize`, ~line 111) writes every entity's properties into the `AuditLogs` table as JSON `OldValues`/`NewValues`. It excludes `Token`/`Secret`/`Password` but **not** `Phone`/`Name`. Without a fix, every contact create/update would persist **plaintext phone & name into `AuditLogs`**, defeating the encryption. → Fixed in step **E**.
2. **Sort/search run in SQL against the columns.** `ContactService.GetPagedAsync` uses `EF.Functions.ILike(c.Phone/Name, ...)` and `OrderBy(c => c.Phone/Name)`. Once those columns hold ciphertext, ILIKE and ORDER BY become meaningless. → Addressed in step **F** + "Known tradeoffs".

---

## A. New building blocks — `Common/Security/`

1. **`EncryptionOptions`** — binds config section `Encryption:MasterKey` (base64, 32 bytes). Sourced from env var `Encryption__MasterKey` on the VPS. **Fail-closed in Production** if missing/invalid.
2. **`IFieldCipher` / `AesGcmFieldCipher`** (singleton)
   - Derives two subkeys from the master key via **HKDF-SHA256** with distinct info labels: `Kenc` (encryption) and `Kidx` (blind index) — so the index key is cryptographically independent from the encryption key.
   - Ciphertext layout: `[version:1B][nonce:12B][tag:16B][ciphertext:nB]`, base64-encoded, stored in a `text` column. **Random nonce per write** (same input → different ciphertext; equality is hidden at the ciphertext layer — that's why the blind index exists).
   - **Decrypt-tolerant:** if a value lacks our version magic byte or fails GCM auth, return it unchanged (assume legacy plaintext). This lets existing plaintext rows be read during the migration window until the backfill encrypts them.
3. **`IBlindIndex` / `HmacBlindIndex`** (singleton)
   - `HMAC-SHA256(Kidx, normalizedValue)` → `byte[]` (stored as `bytea`, 32 bytes, indexed).
   - Helpers: `ForPhone(fullDigits)`, `ForPhoneTail(last6Digits)`, `ForName(lowerTrim)`.

## B. Entity changes — `Features/Contacts/Entities/Contact.cs`

`Phone` and `Name` stay as plaintext `string` CLR properties — the EF value converter encrypts/decrypts transparently at the DB boundary. Add three **persisted, derived** index columns:

| Column | Type | Purpose |
|---|---|---|
| `PhoneIndex` | `byte[]` | HMAC of full normalized phone → equality, dedupe, webhook lookup, uniqueness |
| `PhoneTailIndex` | `byte[]` | HMAC of last 6 digits → "search by last digits" |
| `NameIndex` | `byte[]` | HMAC of normalized (lower+trim) name → exact name match |

These are never set by callers; they are recomputed automatically (step **D**).

## C. DbContext config — `Infrastructure/Persistence/AppDbContext.cs`

- Inject `IFieldCipher` + `IBlindIndex` (singletons) into the context.
- Build a `ValueConverter<string,string>` on `Phone` and `Name` using the cipher (same pattern already used for `TemplateComponents` jsonb).
- Widen `Phone`/`Name` from `varchar(20)/(200)` → **`text`** (ciphertext is longer than the plaintext caps).
- Map the three `byte[]` index columns; add a non-unique index on each.
- **Swap uniqueness:** `(CompanyId, Phone)` unique → **`(CompanyId, PhoneIndex)`** unique.

## D. Keep indexes in sync — new `BlindIndexInterceptor` (`SaveChanges` interceptor)

On every `Added`/`Modified` `Contact`, recompute `PhoneIndex` / `PhoneTailIndex` / `NameIndex` from the current `Phone` / `Name`. Registered as a singleton and added via `AddInterceptors` next to `AuditInterceptor`. **Guarantees the index columns can never drift** — service code just sets `Phone`/`Name` exactly as today.

## E. Fix the audit leak — `Common/Audit/AuditInterceptor.cs`

In `Serialize(...)`, for any property that has a value converter, serialize the **provider (encrypted) value** rather than the CLR plaintext. Result: `AuditLogs` stores ciphertext for `Phone`/`Name`. General fix — automatically protects any future encrypted field.

## F. Query path rewrites (the only behavioral changes)

| File / location | Today | Change |
|---|---|---|
| `ContactService.CreateAsync` (`:65`) / `UpdateAsync` (`:91`) dedupe | `c.Phone == phone` (SQL vs ciphertext → broken) | `c.PhoneIndex == idx.ForPhone(phone)` |
| `ContactService.GetPagedAsync` search (`:28`) | `ILike(Phone) OR ILike(Name)` | phone term → `PhoneIndex == hmac(full)` **OR** `PhoneTailIndex == hmac(last6)`; name term → `NameIndex == hmac(name)` (+ optional trigram, see tradeoffs) |
| `ContactService.GetPagedAsync` sort (`:36`) | `OrderBy(Phone/Name)` (ciphertext order) | default to `CreatedAt`; server-side name/phone sort dropped (see tradeoffs) |
| `InboundMessageWebhookHandler.UpsertContactAsync` (`:129`) — **Meta hot path** | `c.Phone == phone` SQL | `c.PhoneIndex == idx.ForPhone(phone)` — stays **one indexed lookup** |
| `ConversationService.GetPagedAsync` search (`:41`) | `ILike(Contact.Name/Phone)` | same index-based scheme |
| `ContactListService.ImportContactsAsync` (`:201`) | in-memory dict keyed on decrypted `c.Phone` | **no change needed** — converter auto-decrypts; in-memory dedupe still works |

`.Local` (in-memory) LINQ lookups in the webhook handler operate on decrypted values → unchanged. Message/Conversation DTOs that read `contact.Name`/`contact.Phone` via navigation → unchanged (auto-decrypted; TLS protects transit).

## G. Migration + backfill (staged, near-zero-downtime)

1. **Migration 1 `AddContactEncryptionColumns`** — add the 3 `byte[]` columns (nullable), widen `Phone`/`Name` to `text`, add the new indexes. **Keep** the old `(CompanyId, Phone)` unique for now.
2. **Deploy code** with the decrypt-tolerant cipher + `BlindIndexInterceptor`. Existing plaintext reads still work.
3. **`ContactEncryptionBackfillJob`** (Hangfire, idempotent, batched, `IgnoreQueryFilters`, no tenant context) — for each row: encrypt `Phone`/`Name`, populate the 3 index columns. The version byte lets it skip already-encrypted rows → safely re-runnable. Triggered once from the deploy runbook.
4. **Migration 2 `SwapContactPhoneUnique`** — drop old `(CompanyId, Phone)` unique, add `(CompanyId, PhoneIndex)` unique.

> Single VPS + existing manual runbook → steps 1–4 can be collapsed into one brief-downtime window if preferred (see open decision #2).

## H. Config & DI — `Program.cs`, appsettings

- Register `IFieldCipher` + `IBlindIndex` (singletons), bind `EncryptionOptions`, register and wire `BlindIndexInterceptor`, inject cipher/index into `AppDbContext`.
- `appsettings.json`: add `"Encryption": { "MasterKey": "" }` placeholder. Real 32-byte key supplied via env var `Encryption__MasterKey` on the Contabo VPS (same secret pattern as the existing prod `/opt/wa_api` setup). Add to the deployment runbook and the `appsettings.Development.example.json`.
- **Operational rule (documented):** losing the master key = losing the data. Includes a key backup procedure. The ciphertext version byte enables a future migration to a managed KMS (AWS KMS / Azure Key Vault) **without re-encrypting everything**.

---

## Performance impact

| Operation | Impact |
|---|---|
| Meta inbound message → contact lookup | **None** — single indexed equality on `PhoneIndex` |
| Create / dedupe / import upsert | **None** — indexed equality |
| Read a contact (decrypt) | ~microseconds/row; negligible vs network/DB |
| Exact + last-digit phone search | **None** — indexed |
| Free-text "contains" search | Only loss — exact/normalized unless trigram option is included |

---

## Known tradeoffs (need sign-off)

1. **Name/phone column sorting** becomes non-meaningful (ciphertext order). List sort defaults to `CreatedAt`; server-side name/phone sort is dropped. Most CRMs default to recency — acceptable?
2. **Search becomes exact-ish:** full-number or last-6-digits for phone; exact normalized for name. Typing *part* of a name won't match.
   - To keep "type part of a name" search, add **HMAC trigram tokens for `Name`** (a GIN-indexed `bytea[]` column) — stays indexed/fast, small extra write cost, slight n-gram frequency leakage. **Recommendation: include it for Name** (inbox name-search is common); leave phone as exact + last-6.

## Scope

- **In:** `Contact` (`Phone`, `Name`).
- **Out (unless requested):** `WabaConnection` access token (currently stored plaintext despite the `Encrypted` name) — obvious next candidate, reuses the same toolkit.
- **No frontend changes** in scope (search/sort UX shifts per the tradeoffs above).

---

## Open decisions before implementation

1. **Name substring search** — keep it (add trigram tokens) or go exact-only? *(Recommended: keep.)*
2. **Migration window** — staged near-zero-downtime (4 steps) or collapsed single-window (simpler, brief downtime)?

## Build order (after decisions confirmed)

1. Crypto core + `EncryptionOptions` + DI wiring (non-destructive).
2. EF value converters + `BlindIndexInterceptor` + entity columns.
3. `AuditInterceptor` provider-value fix.
4. Query path rewrites (ContactService, webhook handler, ConversationService).
5. Migration 1 + backfill job + Migration 2.
6. `dotnet build` + verify at each step; runbook + key-management docs.

---

## Compliance mapping (for the DPO / auditor)

| PDPL Art. 7 measure | Implementation |
|---|---|
| "Encrypting the personal data of the data subject" | AES-256-GCM on `Phone` / `Name` (key outside the DB) |
| "Implementation of data pseudonymization" | HMAC-SHA256 blind-index columns (one-way tokens replace the identifier for lookups) |

**Sources:**
- Federal Decree-Law No. 45 of 2021 — official text: https://uaelegislation.gov.ae/en/legislations/1972/download
- Securiti — UAE PDPL overview (Art. 7 security measures): https://securiti.ai/uae-personal-data-protection-law/
- UAE Government — Data protection laws: https://u.ae/en/about-the-uae/digital-uae/data/data-protection-laws
