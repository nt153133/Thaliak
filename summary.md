# Thaliak Codebase Summary

## What is Thaliak?

Thaliak is a Final Fantasy XIV patch tracking, notification, and analysis service. It monitors patch servers for all three FFXIV regions (JP/Global, Korea/KR, China/CN), records patch metadata, notifies subscribers when new patches drop, and optionally downloads and analyzes the patch files themselves.

---

## General Workflow

### Core Loop (Polling)

1. **Poller services run on a schedule** for each region:
   - **JP (Global / Square Enix)**: Logs in to the FFXIV launcher using a service account, retrieves the offered patch list. Also scrapes the Lodestone for maintenance windows to know when to poll more aggressively. Polls at/near odd-numbered minute boundaries (V2) or every 40–59 minutes (V1).
   - **KR (Actoz)**: Fetches the KR patch list directly (no login required). Polls every 40–59 minutes.
   - **CN (Shanda)**: Fetches the CN patch list directly (no login required). Polls every 40–59 minutes.

2. **Patch reconciliation** runs after each poll:
   - Compares the remote patch list to what is stored in the database.
   - New patches are inserted; existing patches have their `last_seen`/`last_offered` timestamps updated.
   - Upgrade-path (patch edge) chains are recorded — which patch must be applied before the next.
   - `is_active` status is updated: patches no longer in the remote list are marked inactive.

3. **Notifications** fire for newly discovered patches:
   - V1: Sends Discord webhook embeds directly to all registered webhook URLs in the database.
   - V2: Dispatches an internal HTTP webhook to the **downloader service**, and also sends JSON webhook payloads to all user-registered webhook endpoints (filtered by JP/KR/CN subscription preference).

4. **Download service** (triggered after new patches are found):
   - Downloads the actual `.patch` files from SE/Actoz/Shanda servers to a local storage directory.
   - Skips files already present on disk.
   - After download, notifies the **analysis service** (V2 only).

5. **Analysis service** (V2 only):
   - Receives a webhook from the downloader when a patch file is ready.
   - Applies the ZiPatch file to a maintained game root directory using `zipatch`.
   - Extracts all `.exe` and `.dll` files from the resulting game tree.
   - Computes SHA1, SHA256, and MD5 checksums for each binary.
   - Records file records in the database, and stores/symlinks binaries in a content-addressable directory tree.

6. **API** exposes the data:
   - V1 (C#): GraphQL API.
   - V2 (Rust): REST API with OpenAPI/Swagger documentation at `/` with endpoints for services, repositories, patches, and chain resolution.

---

## Setup Requirements

### V1 (C#) Configuration (`appsettings.json` + environment variables)

| Setting | Where | Description |
|---|---|---|
| `ConnectionStrings:pg` | appsettings.json | PostgreSQL connection string |
| `ConnectionStrings:redis` | appsettings.json | Redis connection string |
| `Directories:Boot` | appsettings.json | Local directory for boot patch files |
| `Directories:Patches` | appsettings.json | Local directory to store downloaded patch files |
| `ENABLE_DOWNLOADS` | env var | Set to `true` to enable the download queue |
| FFXIV credentials | stored in DB | The `XivAccount` table holds the JP service account credentials |

> V1 requires both **PostgreSQL** and **Redis** to be running.

---

### V2 (Rust) Configuration (`.env` file)

| Variable | Required | Description |
|---|---|---|
| `PUBLIC_DATABASE_URL` | ✅ | SQLite URL for the public database (patch/repo data) |
| `PRIVATE_DATABASE_URL` | ✅ | SQLite URL for the private database (users/webhooks) |
| `SQEX_USERNAME` | ✅ | FFXIV Global service account username |
| `SQEX_PASSWORD` | ✅ | FFXIV Global service account password |
| `SQEX_INSTALL_DIRECTORY` | ✅ | Path to a local FFXIV installation (used during login) |
| `DOWNLOAD_PATH` | ✅ | Local directory to store downloaded patch files |
| `DOWNLOADER_WEBHOOK_URL` | ✅ | Internal HTTP URL of the downloader service |
| `ANALYSIS_WEBHOOK_URL` | ✅ | Internal HTTP URL of the analysis service |
| `ANALYSIS_CACHE_DIR` | ✅ (analysis) | Directory for the maintained game root during analysis |
| `ANALYSIS_BINARY_DIR` | ✅ (analysis) | Content-addressable directory to store extracted binaries |
| `API_BASE_PATH` | ⬜ optional | Path prefix when behind a reverse proxy |
| `DISCORD_CLIENT_ID` | ⬜ optional | Discord OAuth app client ID (enables user auth) |
| `DISCORD_CLIENT_SECRET` | ⬜ optional | Discord OAuth app client secret |
| `DISCORD_REDIRECT_URL` | ⬜ optional | Discord OAuth redirect URL |
| `FRONTEND_URL` | ⬜ optional | Frontend origin URL (for CORS on auth endpoints) |
| `JWT_SECRET` | ⬜ optional | Secret for signing JWT auth tokens |
| `DISCORD_BOT_TOKEN` | ⬜ optional | Bot token to auto-join users to a Discord server |
| `DISCORD_GUILD_ID` | ⬜ optional | Guild ID for the auto-join feature |

> V2 only requires **SQLite** — no PostgreSQL or Redis needed.

---

## Key Differences: V2 vs V1

> Language differences (C# vs Rust) are excluded.

### 1. Architecture: Monolith → Microservices
- **V1**: Single process handles polling, reconciliation, downloading, and Discord alerts.
- **V2**: Split into dedicated services — `thaliak-poller`, `thaliak-api`, `thaliak-downloader`, `thaliak-analysis`, and `thaliak-admin-cli`. Services communicate via internal HTTP webhooks.

### 2. Database: PostgreSQL + Redis → Dual SQLite
- **V1**: Uses PostgreSQL (via Entity Framework Core) for all data, and Redis (likely for messaging/queuing).
- **V2**: Uses two SQLite databases:
  - **Public DB**: All patch data, repositories, services, expansions, file records — publicly readable and downloadable.
  - **Private DB**: User accounts, webhook subscriptions, component version tracking — never exposed directly.

### 3. Patch Analysis (New in V2)
- **V1**: No analysis. Only records patch metadata and downloads the file.
- **V2**: The `thaliak-analysis` service applies each ZiPatch to a maintained game root, extracts `.exe`/`.dll` files, computes SHA1/SHA256/MD5 checksums, and stores them in the database and in a content-addressable binary archive. This enables binary tracking across versions.

### 4. API: GraphQL → REST + OpenAPI
- **V1**: Exposes a GraphQL API.
- **V2**: Exposes a REST API with full OpenAPI 3 documentation and Swagger UI, including endpoints for services, repositories, patches, and chain resolution. The public SQLite database file is also downloadable directly for custom analysis.

### 5. User Authentication & Self-Managed Webhooks (New in V2)
- **V1**: No user accounts. Discord webhook URLs are hardcoded into the database by an admin.
- **V2**: Users can authenticate via **Discord OAuth** and manage their own webhook endpoints. Each webhook can independently subscribe to JP, KR, and/or CN patch notifications. The API has full CRUD endpoints for webhook management including a test-fire endpoint.

### 6. Webhook System: Discord-Only → Generic JSON Webhooks
- **V1**: Sends formatted Discord embeds directly via the Discord.Net webhook client.
- **V2**: Sends generic JSON HTTP webhooks (`WebhookPayload` structure) to any URL. Discord-formatted notifications would need to be handled by the consumer. The internal service-to-service communication also uses HTTP webhooks (poller → downloader → analysis).

### 7. Prometheus Metrics (New in V2)
- **V1**: No metrics endpoint.
- **V2**: The API service exposes a Prometheus metrics endpoint (port 9090) for observability.

### 8. Component Version Tracking (New in V2)
- **V1**: No version tracking.
- **V2**: Each service records its own git commit hash to the private database on startup, allowing the API's `/status` endpoint to report which version of each component is deployed.

### 9. Sqex Polling Cadence
- **V1**: Polls JP every 40–59 minutes (random, same as KR/CN).
- **V2**: Polls JP precisely at the next odd-numbered minute boundary (i.e., at most 2 minutes between polls), making JP patch detection significantly faster.

### 10. Reconciliation Atomicity
- **V1**: Saves to the database patch-by-patch inside a loop (multiple `SaveChanges()` calls).
- **V2**: Wraps the entire reconciliation in a single SQLite transaction, committed atomically.

---

## Features to Port if Staying on V1 (C#)

If you want to keep the C# codebase and bring it to parity with V2, the following features would need to be implemented:

### High Priority (Functional Gaps)
| Feature | V2 Location | Notes |
|---|---|---|
| **ZiPatch Analysis** | `thaliak-analysis` | Apply patches to a game root, extract .exe/.dll, compute SHA1/SHA256/MD5, store in `file` table. Needs a C# ZiPatch library (XIVLauncher already has one in the `lib/` submodule). |
| **Faster JP Polling** | `thaliak-poller/main.rs` | Change from 40–59 min random interval to polling at odd-minute boundaries for JP. |
| **Generic JSON Webhooks** | `thaliak-common/webhook.rs` | Replace Discord-only alerts with a JSON HTTP webhook payload that consumer-side code can format for Discord or anything else. |
| **Per-User Webhook Subscriptions** | `thaliak-api/routes/user.rs` | Add user account model, CRUD for webhooks, JP/KR/CN subscription flags. |

### Medium Priority (Operational Improvements)
| Feature | V2 Location | Notes |
|---|---|---|
| **Atomic Reconciliation** | `reconciliation.rs` | Wrap the entire reconciliation loop in a single DB transaction instead of per-patch saves. |
| **Downloader as Separate Service** | `thaliak-downloader` | Move the downloader out of the poller into its own service triggered by an internal webhook. |
| **Component Version Tracking** | `thaliak-common/version.rs` | Record git commit hash per service to a `component_versions` table; expose via a status endpoint. |
| **Public DB Export** | V2 API description | Make the public portion of the database downloadable for external analysis. |

### Lower Priority (Nice-to-Have)
| Feature | V2 Location | Notes |
|---|---|---|
| **Prometheus Metrics** | `thaliak-api/metrics.rs` | Add a `/metrics` endpoint for operational observability. |
| **Discord OAuth Login** | `thaliak-api/auth/` | Allow users to log in via Discord to self-manage their webhooks. |
| **OpenAPI/Swagger Docs** | `thaliak-api/main.rs` | Supplement or replace the GraphQL API with a REST+OpenAPI surface. |
| **Admin CLI** | `thaliak-admin-cli` | Admin tooling for database management and seeding. |
