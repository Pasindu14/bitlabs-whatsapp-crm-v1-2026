# 006 — Send images, documents and audio from the inbox (with 90-day retention)

## Goal
An agent can attach a file in the inbox composer and send it to a customer inside the 24-hour window.
Every stored media file (sent AND received) is deleted after **90 days**.

## Decisions
| Question | Decision | Why |
|---|---|---|
| Where do sent files live? | **VPS disk** (Docker volume `media-data` → `/data/media`) | Supabase Postgres is cloud and quota-limited; 150 GB free on the Contabo VPS. |
| How does Meta get the file? | **Upload to Meta** (`POST /{phone}/media` → media id), send by id | `GET /media/{id}` is JWT-protected, so Meta can't fetch a link. |
| Inbound media (already `bytea` in Postgres)? | Left as is, but **purged by the same 90-day job** | Stops Supabase growing; moving it to disk is a separate step. |
| Retention | `Media:RetentionDays` = 90, daily Hangfire job `media-retention` (03:45) | Message text/caption stays; only the file goes. |
| Voice recording in browser | **Out of scope** | Browser records webm; Meta only accepts aac/mp4/mpeg/amr/ogg(opus). Audio *files* are fine. |

## Limits (Meta)
| Type | MIME | Max |
|---|---|---|
| image | jpeg, png | 5 MB |
| audio | aac, mp4, mpeg, amr, ogg | 16 MB |
| video | mp4, 3gpp | 16 MB |
| document | pdf, doc(x), xls(x), ppt(x), txt | 100 MB |

Vercel caps a serverless request body at ~4.5 MB, so the web proxy enforces **4 MB** for now. Raising it
needs a direct-to-API upload (short-lived token). The API itself accepts Meta's full limits.

## Backend (`wa_api`)
1. `IMediaStorage` / `LocalMediaStorage` — save, open, delete under `Media:StoragePath`
   (`{company}/{yyyy}/{MM}/{messageId}.ext`); path-traversal guarded.
2. `OutboundMediaRules` — MIME/size validation per type, before any Meta call.
3. `IWabaMediaUploader` — `POST v19.0/{phoneNumberId}/media` with the WABA token → media id.
4. `IWhatsAppMessageSender.SendMediaAsync` — same pipeline as `SendAsync` (rate limit, reserve, send,
   persist, refund on failure) with `type: image|audio|video|document`.
5. `ConversationService.SendMediaAsync` — window check, bot takeover, validate, upload, send, store file +
   `MessageMedia` row (`StoragePath`), push realtime event.
6. `POST /conversations/{id}/messages/media` (multipart: `file`, `caption?`).
7. `MediaController` — streams from disk when `StoragePath` is set; `410 Gone` once purged.
8. `MediaRetentionJob` — deletes files + rows older than the cutoff, sets `Message.MediaPurgedAt`.
9. Migration: `MessageMedia.StoragePath`, `Message.MediaPurgedAt`. **Prod does not auto-migrate** — apply
   the SQL by hand before deploying.
10. `docker-compose.yml` — `media-data` volume; add it to the VPS backup.

## Frontend (`wa_web`)
1. `POST /api/conversations/[id]/media` — multipart proxy to the API (same pattern as `media-handle`).
2. Composer: paperclip button, file chip with remove, the text box becomes the caption, upload spinner.
3. Bubble: "File expired" state when the media was purged; outbound media renders like inbound.
4. Types: `mediaExpired`.

## Tests
- Rules: allowed/blocked MIME, size caps.
- Storage: round-trip, traversal rejected.
- Retention job: old rows purged (file + row + flag), recent rows kept.

## Rollout
Back up → apply migration SQL → merge to `main` → `deploy.sh` → web deploys on Vercel → send a test image,
PDF and mp3 to a real number.
