# Contabo Server — wa_api Deployment Guide

Complete runbook for hosting the **wa_api** (ASP.NET Core 8) backend on the Contabo VPS with Docker, plus the database migration we did and the workflow for redeploying after code changes.

> ⚠️ **SENSITIVE FILE — contains passwords and secrets.** Do **not** commit this to a public repo. Add `contabo.md` to `.gitignore` if you keep the secrets inline, or strip the credentials before committing.

---

## 1. Server & Credentials

| Item | Value |
|---|---|
| Provider | Contabo (AS51167) |
| Location | Lauterbourg, France 🇫🇷 |
| Public IP | `213.136.74.159` |
| SSH user | `root` |
| SSH password | `uJ9Gp4H1e` |
| OS | Ubuntu 24.04 LTS |
| App directory | `/opt/wa_api` |
| API public URL | `http://213.136.74.159:8080` |

### Connect via SSH
```bash
ssh root@213.136.74.159
# enter password when prompted: uJ9Gp4H1e
```

---

## 2. Install Docker (one-time, fresh server)

```bash
# Official convenience script — installs Docker Engine + Compose plugin
curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
sh /tmp/get-docker.sh

# Enable + start the daemon on boot
systemctl enable --now docker

# Verify
docker --version
docker compose version
```

---

## 3. Deployment Files

These three files live in the repo at `wa_api/` (committed to git) and are copied to the server.

### `wa_api/Dockerfile`
Multi-stage .NET 8 build (SDK to publish, ASP.NET runtime to run), listening on port 8080.

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY wa_api/wa_api.csproj wa_api/
RUN dotnet restore wa_api/wa_api.csproj
COPY wa_api/ wa_api/
RUN dotnet publish wa_api/wa_api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
ENTRYPOINT ["dotnet", "wa_api.dll"]
```

### `wa_api/.dockerignore`
Keeps build artifacts and secrets out of the image.
```
**/bin/
**/obj/
**/.vs/
**/.git/
**/*.user
**/*.suo
**/appsettings.Development.json
**/appsettings.Development.example.json
wa_api.Tests/
.env
```

### `wa_api/docker-compose.yml`
API + Redis. Secrets injected at runtime from `.env`.
```yaml
services:
  redis:
    image: redis:7-alpine
    restart: unless-stopped
    command: ["redis-server", "--appendonly", "yes"]
    volumes:
      - redis-data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 3s
      retries: 5

  api:
    build:
      context: .
      dockerfile: Dockerfile
    restart: unless-stopped
    env_file: .env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: "http://+:8080"
      REDIS_CONNECTION: "redis:6379,abortConnect=false"
    depends_on:
      redis:
        condition: service_healthy
    ports:
      - "8080:8080"

volumes:
  redis-data:
```

---

## 4. Transfer Source to the Server

The server has **no GitHub token**, so we push the source via tar-over-SSH (no git clone needed).

Run from your **local machine**, in the `wa_api/` folder:
```bash
cd /d/Github/bitlabs-whatsapp-crm-v1-2026/wa_api    # adjust path

ssh root@213.136.74.159 'mkdir -p /opt/wa_api'

tar --exclude='./.vs' --exclude='*/bin' --exclude='*/obj' --exclude='./.git' \
    --exclude='*/appsettings.Development.json' --exclude='./.env' \
    -czf - . | ssh root@213.136.74.159 'tar xzf - -C /opt/wa_api'
```

> **Alternative:** set up a deploy key / PAT on the server and `git clone` / `git pull` instead. We used tar to avoid storing credentials on the box.

---

## 5. Create the Production `.env` (on the server)

The app runs as **Production**, which does **not** read `appsettings.Development.json`. All secrets come from `/opt/wa_api/.env`.

```bash
cat > /opt/wa_api/.env <<'ENVEOF'
# ── Database (Supabase EU / Frankfurt pooler, session mode port 5432) ──
ConnectionStrings__DefaultConnection=Host=aws-1-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.awimwqhqbseaslpyelgb;Password=42AiHuUwRr1zoHoy;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;

# ── Auth ──
Jwt__SecretKey=<generate-a-strong-48+char-secret>
Jwt__Issuer=wa-api
Jwt__Audience=wa-api
Jwt__AccessTokenExpiryMinutes=480

# ── Encryption (MUST stay this value to decrypt Meta tokens already in the DB) ──
Encryption__Key=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=

# ── App ──
AppUrl=http://213.136.74.159:8080

# ── Meta (WhatsApp) ──
Meta__AppId=868454245574284
Meta__AppSecret=a8512e281038dbdd700721e061f0a68e
Meta__WebhookVerifyToken=wa-crm-local-verify-9f3k2x7q

# ── Stripe (fill in when ready) ──
Stripe__SecretKey=
Stripe__WebhookSecret=
ENVEOF

chmod 600 /opt/wa_api/.env
```

> To generate a JWT secret: `openssl rand -base64 48`
> **`Encryption__Key` must never change** while pointing at a DB that has encrypted Meta tokens, or they become undecryptable.

---

## 6. Build & Start

```bash
cd /opt/wa_api
docker compose up -d --build
```

First build takes a few minutes (NuGet restore + publish). Check status:
```bash
docker compose ps
docker compose logs api --tail 50
```

---

## 7. Firewall

`ufw` is inactive and iptables policy is `ACCEPT`, and Docker publishes port 8080 directly — **no changes needed**. If you ever enable `ufw`:
```bash
ufw allow 8080/tcp
```

---

## 8. Verify the Deployment

```bash
# Liveness (process up)
curl -s http://localhost:8080/health/live          # -> 200

# Full health (DB + Redis)
curl -s http://localhost:8080/health               # -> {"status":"Healthy",...}

# Real end-to-end test (auth + DB)
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"superadmin@btilabs.com","password":"ChangeMe123!"}'
```
Health endpoints: `/health`, `/health/live`, `/health/ready`.
From anywhere: `http://213.136.74.159:8080/health`.

---

## 9. Database — Supabase Migration (Singapore → Frankfurt)

We moved the DB from Singapore to Frankfurt because the VPS is in France; cross-continent latency was ~170 ms/query (login ~1 s). After the move: DB query ~10 ms, login ~250–300 ms.

> Supabase **cannot** change an existing project's region. You create a new project in the target region and migrate the data.

**Current databases:**
| | Project ref | Host | Password |
|---|---|---|---|
| **Active (EU)** | `awimwqhqbseaslpyelgb` | `aws-1-eu-central-1.pooler.supabase.com` | `42AiHuUwRr1zoHoy` |
| Old (rollback) | `nbhctpxgukhjfpqgthyo` | `aws-1-ap-southeast-1.pooler.supabase.com` | `u0StgXwXetjD6DlK` |

### Migration steps (run on the server; uses dockerized psql/pg_dump)
Supabase server is **Postgres 17**, so use `pg_dump` 17.

```bash
OLD="postgresql://postgres.nbhctpxgukhjfpqgthyo:u0StgXwXetjD6DlK@aws-1-ap-southeast-1.pooler.supabase.com:5432/postgres?sslmode=require"
NEW="postgresql://postgres.awimwqhqbseaslpyelgb:42AiHuUwRr1zoHoy@aws-1-eu-central-1.pooler.supabase.com:5432/postgres?sslmode=require"

# 1. Dump the public schema (schema + data) from the OLD db
mkdir -p /opt/wa_api/backups
docker run --rm postgres:17-alpine pg_dump "$OLD" \
  --schema=public --no-owner --no-privileges \
  > /opt/wa_api/backups/wa_public.sql

# 2. Restore into the NEW db
docker run --rm -v /opt/wa_api/backups:/b postgres:17-alpine \
  psql "$NEW" -v ON_ERROR_STOP=0 -f /b/wa_public.sql
#    (the only expected error: 'schema "public" already exists' — harmless)

# 3. Verify row counts match
docker run --rm postgres:17-alpine psql "$NEW" -c "
SELECT relname, n_live_tup FROM pg_stat_user_tables
WHERE schemaname='public' ORDER BY relname;"
```

### Repoint the app at the new DB
```bash
cd /opt/wa_api
cp .env .env.singapore.bak          # backup old config for rollback
# edit the ConnectionStrings__DefaultConnection line in .env to the NEW host/user/password
docker compose up -d --force-recreate api
```

---

## 10. Where the Connection String Lives

| Location | File / Key | Used for |
|---|---|---|
| Local (your PC) | `wa_api/wa_api/appsettings.Development.json` → `ConnectionStrings:DefaultConnection` | Running API locally + EF migrations. **Gitignored.** |
| Server (prod) | `/opt/wa_api/.env` → `ConnectionStrings__DefaultConnection` | The deployed container. |
| Server (rollback) | `/opt/wa_api/.env.singapore.bak` | Old Singapore string. |
| Repo template | `wa_api/wa_api/appsettings.Development.example.json` | Placeholders only — no real secret. |

Active connection string (both local + server):
```
Host=aws-1-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.awimwqhqbseaslpyelgb;Password=42AiHuUwRr1zoHoy;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;
```

The web app reads `API_URL` from `wa_web/.env.local`:
- Full local stack: `API_URL=https://localhost:7169`
- Hit the live API: `API_URL=http://213.136.74.159:8080`

---

## 11. ⭐ Redeploy After a Code Change

Whenever you change **C# / backend code**, do this:

```bash
# 1. LOCAL: from the wa_api/ folder, push updated source to the server
cd /d/Github/bitlabs-whatsapp-crm-v1-2026/wa_api
tar --exclude='./.vs' --exclude='*/bin' --exclude='*/obj' --exclude='./.git' \
    --exclude='*/appsettings.Development.json' --exclude='./.env' \
    -czf - . | ssh root@213.136.74.159 'tar xzf - -C /opt/wa_api'

# 2. SERVER: rebuild + restart (the .env on the server is preserved)
ssh root@213.136.74.159 'cd /opt/wa_api && docker compose up -d --build'

# 3. Verify
ssh root@213.136.74.159 'curl -s http://localhost:8080/health'
```

Notes:
- `docker compose up -d --build` only restarts the `api` container if its image changed; Redis keeps running.
- `appsettings.Development.json` and `.env` are **never** overwritten by the transfer (excluded), so server secrets are safe.
- If you only changed **config** (not code), edit `/opt/wa_api/.env` on the server and run `docker compose up -d --force-recreate api` — no rebuild needed.

---

## 12. Common Operations

```bash
cd /opt/wa_api

docker compose ps                       # container status
docker compose logs -f api              # live API logs
docker compose logs api --tail 100      # last 100 log lines
docker compose restart api              # restart API only
docker compose down                     # stop everything
docker compose up -d                    # start everything
docker compose up -d --build            # rebuild + start (after code change)
docker stats --no-stream                # CPU/RAM usage
```

---

## 13. Rollback

If a deploy or the EU DB goes bad:
```bash
cd /opt/wa_api

# Roll back the DB connection to Singapore:
cp .env.singapore.bak .env
docker compose up -d --force-recreate api

# Roll back code: re-transfer the previous source and rebuild.
```

---

## 14. TODO / Not Yet Done

- **Domain + HTTPS.** Currently HTTP only on `:8080`. **Meta and Stripe webhooks require a public HTTPS URL** — they won't work until a domain is added. Recommended: put **Caddy** in front for automatic Let's Encrypt TLS.
- **CORS for a deployed web app.** The API's CORS allow-list defaults to `http://localhost:3000`. When the web app is deployed, add its origin via `Cors__AllowedOrigins__0=https://yourwebdomain` in `/opt/wa_api/.env` and restart.
- **Mixed content.** A web app served over HTTPS cannot call this HTTP API from the browser (SignalR/REST) — needs the HTTPS step above.
- **Decommission Singapore.** Once the EU DB is proven over a few days, pause/delete the old Supabase project so there's a single source of truth.
- **Rotate the JWT secret** in `/opt/wa_api/.env` to a strong random value if the dev placeholder is still in use.
