# F1 Competition Platform 🏎️

[![Build and Push F1 API](https://github.com/PhilipWoulfe/F1Competition/actions/workflows/docker-build.yaml/badge.svg)](https://github.com/PhilipWoulfe/F1Competition/actions/workflows/docker-build.yaml)
[![CodeQL](https://github.com/PhilipWoulfe/F1Competition/actions/workflows/codeql.yml/badge.svg)](https://github.com/PhilipWoulfe/F1Competition/actions/workflows/codeql.yml)
![Dependabot](https://img.shields.io/badge/dependabot-enabled-025E8C?logo=dependabot&logoColor=white)
![GitHub last commit](https://img.shields.io/github/last-commit/PhilipWoulfe/F1Competition)
![Code Coverage](https://img.shields.io/badge/Code%20Coverage-50%25-yellow?style=flat)

![Deployment](https://img.shields.io/badge/Deployment-Staged-blue)

![Runtime](https://img.shields.io/badge/Runtime-LXC_now%20%E2%86%92%20VM_target-orange)

## Overview

The **F1 Competition Platform** is a modular solution designed to aggregate Formula 1 data and serve it via a modern RESTful API. Built with **.NET 8** and following **Clean Architecture** principles, it automates data ingestion from external providers and serves it through a containerized infrastructure.

It consists of two main components:

1.  **F1.DataSyncWorker (.NET Worker)**: A scheduled worker that fetches baseline data from Jolpica and seeds the live Postgres model (competitions, drivers, and races).
2.  **F1.Api (ASP.NET Core)**: A RESTful API that serves the aggregated data to clients.

Legacy note: `PopulateF1Database` (Azure Functions/Cosmos path) is kept only for transition support and is no longer the canonical baseline data path.

The solution is built using **.NET 8** and follows **Clean Architecture** principles, leveraging dependency injection, configuration management, and containerization.

---

## 🏗️ Infrastructure & CI/CD

Current runtime is a local **Proxmox Virtualization Environment** using Debian 12 LXC containers. The active migration target is a dedicated VM so Docker is isolated from the Proxmox host.

Deployment runbook:
- See [DEPLOYMENT.md](DEPLOYMENT.md) for the living VM setup, secrets, deploy, and rollback instructions.
- See [scripts/github-sync-environment.sh](scripts/github-sync-environment.sh) to batch-apply GitHub Environment vars/secrets via CLI.
- See [scripts/generate-github-env-files.sh](scripts/generate-github-env-files.sh) to generate test/production GitHub env files from current LXC `.env`.
- See [scripts/bootstrap-vm.sh](scripts/bootstrap-vm.sh) to automate first-time VM bootstrap.

### **The Pipeline**
1. **Continuous Integration**: GitHub Actions builds the .NET solution, executes unit tests, and enforces a code coverage gate (80% target).
2. **Registry**: Successful builds on `main` are packaged into Docker images and pushed to **GitHub Container Registry (GHCR)**.
3. **Automated Staging (`f1-test`)**: GitHub Actions deploys the moving `:test` aliases to the test host using the same preflight and smoke-check flow as production.
4. **Production Gate (`f1-prod`)**: Deployment to production requires **Manual Approval** via GitHub Environments, ensuring a stable "human-in-the-loop" verification before live updates.

### **Image Tag Strategy**
- `sha-<shortsha>`: immutable build artifact for traceability and rollback.
- `test`: moving alias for the current main-branch build used by the test environment.
- `stable`: moving alias for the manually approved production build.

Rollback process:
- Set `TAG=sha-<known-good-sha>` on the target host.
- Recreate the containers so Docker Compose uses that pinned image tag.

| Environment | Host IP (Internal) | Port | Deployment Logic |
| :--- | :--- | :--- | :--- |
| **Test** | `192.168.0.50` | `5000` | Automated (CI deploy workflow) |
| **Production** | `192.168.0.51` | `5000` | Manual (Gatekeeper) |

### **Public Access (Cloudflare Tunnel)**
The solution uses **Cloudflare Tunnels** to securely expose the services without opening ports on the router.
- **Profile**: `cloud` (Enabled via `COMPOSE_PROFILES=cloud` or `--profile cloud`)
- **Token**: Managed via `TUNNEL_TOKEN` in `.env`.
- **Profiles by environment**: Test and production should both use `COMPOSE_PROFILES=cloud`.
- **Domains**:
  - Test: `https://f1-test.philipwoulfe.com`
  - API: `https://f1-api-test.philipwoulfe.com`

### **Network Model (Migration Target)**
- **Cloudflare** handles public ingress: DNS, TLS, tunnel, and edge access policy.
- **Tailscale** handles private operator and deployment access to the VM.
- Public SSH should remain disabled on the VM; CI deploys should use the private Tailscale path.

---

## 🚀 Features

- **Scheduled Data Sync Worker**: `F1.DataSyncWorker` ingests and upserts baseline competition, driver, and race data from Jolpica into Postgres.
- **ASP.NET Core API**: A containerized Web API for data access.
- **Persistence**: API runtime persistence via **Postgres**.
- **Containerized**: Full Docker support for reproducible environments across Proxmox LXCs.

---

## 🛠️ Project Structure

```text
.
├── .github/workflows/    # CI/CD Pipelines (Build, Test, Deploy)
├── src/
│   ├── F1.Api            # ASP.NET Core Web API (Entry Point)
│   ├── F1.Core           # Domain Entities & Interfaces
│   ├── F1.Infrastructure # Database & External Integrations
│   ├── F1.DataSyncWorker  # Scheduled Postgres baseline seed worker
│   ├── F1.Services       # Business Logic
│   └── PopulateF1...     # Legacy Azure Function ingestion apps (deprecated path)
├── tests/
│   └── F1.Api.Tests      # XUnit Test Suite
│   └── F1.Web.Tests      # XUnit Test Suite
├── docker-compose.yml    # Infrastructure Blueprint
└── Dockerfile            # Multi-stage Docker Build
```

## 🚦 Local Development Guide

### 1. Prerequisites
- **.NET 8 SDK**
- **Docker & Docker Compose**
- **entr** (for live test-watching on Linux/WSL):
  ```bash
  sudo apt-get update && sudo apt-get install entr
  ```

### 2. Configuration
The solution uses `.env` for Docker Compose plus appsettings/environment variables for API and worker runtime settings.

#### A. Docker Environment (`.env`)
Create a `.env` file in the root of the project. This file controls ports, URLs, and environment settings for the local Docker containers.

```bash
# Copy the example file to create your local configuration
cp .env.example .env
```
The default values in `.env.example` are configured for a standard local setup.

Required API values in `.env`:

- `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`: used to build `ConnectionStrings__Postgres` for `f1-api`.
- `CLOUDFLARE_AUDIENCE`: mapped to `CloudflareAccess__Audience` for `f1-api`.
- `CLOUDFLARE_ISSUER`: mapped to `CloudflareAccess__Issuer` for `f1-api`.

For non-Docker local API runs (`dotnet run`), configure `ConnectionStrings:Postgres` via environment variable or user-secrets instead of committing credentials in appsettings files:

```bash
# Option A: environment variable
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=f1competition;Username=<user>;Password=<password>'

# Option B: user secrets (from src/F1.Api)
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=f1competition;Username=<user>;Password=<password>"
```

Note: `src/F1.Api/appsettings.json` and `src/F1.Api/appsettings.Development.json` intentionally keep `ConnectionStrings:Postgres` empty so missing configuration fails fast with a clear startup error.

Optional Postgres bootstrap value in `.env`:

- `DB_AUTO_MIGRATE`: mapped to `Database__AutoMigrate` for `f1-api`. When `true`, the API applies EF Core migrations on startup. Baseline competition/driver/race ingestion is handled by `f1-data-sync-worker`.
  - Default in `.env.example`: `true`.

Optional worker values in `.env`:

These `DATA_SYNC_*` values are consumed by `docker-compose.yml` and mapped to `DataSyncWorker__*` environment variables for the `f1-data-sync-worker` container.

- `DATA_SYNC_INTERVAL_MINUTES`: mapped to `DataSyncWorker__IntervalMinutes` for `f1-data-sync-worker`. `0` means run once and exit.
- `DATA_SYNC_AUTO_MIGRATE`: mapped to `DataSyncWorker__AutoMigrate` for `f1-data-sync-worker`.
  - Default in `.env.example`: `false` to keep a single migration owner by default (the API via `DB_AUTO_MIGRATE=true`). Set to `true` only if you intentionally want the worker to own migrations.
- `DATA_SYNC_HTTP_RETRY_COUNT`: mapped to `DataSyncWorker__HttpRetryCount` for retry attempts against Jolpica.
- `DATA_SYNC_HTTP_RETRY_DELAY_MS`: mapped to `DataSyncWorker__HttpRetryDelayMs` for retry backoff delay.
- `DATA_SYNC_DEADLINE_MINUTES_BEFORE_START`: mapped to `DataSyncWorker__DeadlineMinutesBeforeStart`; default placeholder policy is `30`.
- `DATA_SYNC_JOLPICA_BASE_URL`: mapped to `DataSyncWorker__JolpicaBaseUrl`.
- `DATA_SYNC_CONTINUE_ON_ERROR`: mapped to `DataSyncWorker__ContinueOnError`.

Optional API values in `.env`:

- `ADMIN_GROUP_CLAIM_TYPE`: mapped to `CloudflareAccess__AdminGroupClaimType` for `f1-api`. Sets the primary/custom claim used to read Cloudflare group membership; the middleware also falls back to common group claims (`groups`, `group`, and `ClaimTypes.GroupSid`) when present.
- `ADMIN_GROUPS`: mapped to `CloudflareAccess__AdminGroups` for `f1-api`. Any matching group value from the inspected claims grants the `Admin` role.
- `ADMIN_EMAILS`: mapped to `CloudflareAccess__AdminEmails` for `f1-api`. Any matching email value also grants the `Admin` role, using case-insensitive matching.

Optional development toggle in `.env`:

- `DEV_MOCK_EMAIL`: mapped to `DevSettings__MockEmail` for `f1-api`. Sets the mock user identity used when simulating Cloudflare locally.
- `DEV_MOCK_GROUPS`: mapped to `DevSettings__MockGroups` for `f1-api`. Sets the mock group memberships used for local Admin/non-Admin testing.
- `DEV_ENABLE_DEBUG_ENDPOINTS`: mapped to `DevSettings__EnableDebugEndpoints` for `f1-api`. When `true`, enables the test-only `/api/users/debug/me` diagnostics endpoint in allowed environments.

Notes:

- `CLOUDFLARE_ISSUER` is mapped in `docker-compose.yml` to `CloudflareAccess__Issuer` for `f1-api`.
- `API_BASE_URL` is set to `/api/` directly in `docker-compose.yml` for `f1-web` and is not read from `.env`.
- `/api/users/debug/me` returns sanitized post-auth claims, groups, and role resolution data only when `DEV_ENABLE_DEBUG_ENDPOINTS=true` and the API is running in `Development` or `Test`.
- `TAG` applies to API, Web, and Data Sync worker images. Use `TAG=test` for the test host, `TAG=stable` for production, and `TAG=sha-<shortsha>` for rollback or pinning a specific build.
- Run `./scripts/deploy-preflight.sh` before VM-targeted compose deployment to validate required env keys, writable log path, and minimum free disk.
- Run `./scripts/deploy-smoke-check.sh` after deployment to validate API health, web reachability, and expected worker state.
- Container health checks are available on API `GET /health` and web root `/` for deploy validation.

#### B. Data Sync Worker (`src/F1.DataSyncWorker/appsettings*.json`)
The worker reads `ConnectionStrings:Postgres` and the `DataSyncWorker` section. Default config includes the three baseline competitions for this epic:

- Philip 2025
- David 2025
- Main 2026

Run locally:

```bash
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=f1competition;Username=<user>;Password=<password>'
dotnet run --project src/F1.DataSyncWorker/F1.DataSyncWorker.csproj
```

Set `DataSyncWorker__IntervalMinutes=0` for a one-shot run or any positive value for scheduled mode.

When running the worker directly (outside Docker Compose), set `DataSyncWorker__*` environment variables directly rather than `DATA_SYNC_*`.

For Docker Compose runtime, `f1-data-sync-worker` runs automatically and reads values from `.env`.

#### C. Azure Function (`local.settings.json`) - Legacy Path
This file configures the legacy Cosmos ingestion service and is no longer the canonical baseline seed path. It remains for migration fallback only.

```
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "FUNCTIONS_INPROC_NET8_ENABLED": 1,
    "Functions:Worker:HostEndpoint": "http://127.0.0.1:5001",
    "UpdateDatabaseCronSchedule": "0 */1 * * * *",
    "Environment": "Dev",
    "CompetitionYear": "2025",
    "CosmosDbConnectionString": "your-cosmos-db-connection-string",
    "CosmosDbDatabaseId": "your-database-id",
    "CosmosDbDriversContainer": "Drivers",
    "CosmosDbPreSeasonQuestionsContainer": "PreSeasonQuestions",
    "CosmosDbRacesContainer": "Races",
    "CosmosDbResultsContainer": "Results",
    "CosmosDbSprintsContainer": "Sprints",
    "CosmosDbUsersContainer": "Users",
    "JolpicaBaseUrl": "https://api.jolpi.ca/ergast/f1/",
    "CosmosDbRetryCount": 1,
    "CosmosDbRetryTime": 30,    
    "JolpicaRateLimitDelayMs": 500
  }
}
```

### 3. Running the Application
The recommended way to run the application locally is using the provided build script. This workflow **runs tests first** and will abort the build if they fail, ensuring a stable environment.

```bash
# Make the script executable (only needs to be done once)
chmod +x build.sh
```

### 4. Build Script Modes

`build.sh` now supports dedicated quality and CI flows:

- `./build.sh`  
  Runs API/Web tests with coverage and then builds/starts Docker containers.

- `./build.sh --debug`  
  Skips tests and runs debug Docker configuration.

- `./build.sh --quality`  
  Runs quality gate only (restore, `dotnet format whitespace --verify-no-changes`, and strict CI-style Release builds), then exits.

- `./build.sh --ci`  
  Runs quality gate and then tests with coverage, then exits (no Docker compose).

### 4.1 Post-Deploy E2E Gate (Story #82)

The `Build and Push F1 API` workflow now includes a post-deploy Selenium gate:

1. `build-and-push` publishes immutable `:sha-<shortsha>` images and updates the moving `:test` aliases used by the test environment.
2. `run-e2e-test` executes Selenium flows against the deployed test environment.
3. `deploy-prod` is blocked unless `run-e2e-test` succeeds, then manually promotes the exact tested images to the moving `:stable` aliases.

Required GitHub Environment (`test`) secrets for the E2E job:

- `E2E_BASE_URL`: base URL for the deployed test web app.
- `E2E_API_BASE_URL`: API base URL (optional if `E2E_BASE_URL + /api` works for your routing).
- `E2E_CF_CLIENT_ID`: Cloudflare Access service token client ID.
- `E2E_CF_CLIENT_SECRET`: Cloudflare Access service token secret.

Optional E2E tuning:

- `E2E_TIMEOUT_SECONDS`: defaults to 20 locally and 30 in CI.
- `E2E_HEADLESS`: defaults to true.
- `E2E_RACE_ID`: defaults to `2025-24-yas_marina`.
- `E2E_STEP_TRACE_PATH`: optional override for always-on Selenium step logs. Defaults to `TestResults/e2e/step-traces`.

Test-only service-token fallback controls (use only when Cloudflare service-token JWTs omit email claims):

- `CLOUDFLARE_ENABLE_TEST_SERVICE_TOKEN_FALLBACK`: default `false`; only active when API environment is `Test`.
- `CLOUDFLARE_TEST_SERVICE_TOKEN_SUBJECT_ALLOWLIST`: required allowlist of service-token identifiers (`sub`, `nameidentifier`, or `common_name`) permitted to use fallback identity.
- `CLOUDFLARE_TEST_SERVICE_TOKEN_ADMIN_SUBJECT_ALLOWLIST`: optional subset of allowlisted identifiers that should receive Admin role.
- `CLOUDFLARE_TEST_SERVICE_TOKEN_EMAIL_DOMAIN`: default `test.local`; used for synthesized fallback email addresses.

GitHub Actions artifact behavior:

- CI writes E2E output to `TestResults/e2e` on the runner.
- Selenium step traces are always written to `TestResults/e2e/step-traces` (or `E2E_STEP_TRACE_PATH` if set).
- Failure screenshots and HTML are written to `TestResults/e2e/failure-artifacts` when capture runs.
- The workflow uploads `TestResults/e2e/**` and `chromedriver.log` as the `e2e-results` artifact.
- Runner-local files are discarded after the job completes, so GitHub Actions artifacts are the persisted copy for CI runs.

### 4.2 VM Selenium Debug Helper (Option B)

For SSH-based debugging, use the checked-in helper script:

```bash
./scripts/e2e-debug-vm.sh
```

This script sets safe VM defaults and runs one focused E2E test by default:

- `E2E_BASE_URL=http://localhost:5001`
- `E2E_API_BASE_URL=http://localhost:5000`
- `E2E_HEADLESS=true`
- `E2E_TIMEOUT_SECONDS=30`

Run a specific test filter:

```bash
./scripts/e2e-debug-vm.sh "FullyQualifiedName~SubmitSelection_ShouldPersistServerSide"
```

Run the full E2E project:

```bash
./scripts/e2e-debug-vm.sh all
```

To inspect the active Selenium Chrome target from your laptop, open an SSH tunnel first:

```bash
ssh -L 9222:localhost:9222 <user>@<vm>
```

Then open:

- `http://localhost:9222/json/list`

Notes:

- Keep Cloudflare service token values out of the script. Set `E2E_CF_CLIENT_ID` and `E2E_CF_CLIENT_SECRET` in your shell when needed.
- If API verification returns 404, verify `E2E_API_BASE_URL` is correct for your route path.

### 4.3 Deployment Environment Tag Settings

Set deployment host `.env` files to match the image promotion flow:

- Test host: `TAG=test`
- Production host: `TAG=stable`
- Rollback or pinning a known build: `TAG=sha-<known-good-sha>`

After changing `TAG` or any other deployment environment variable, recreate the containers. Watchtower updates images but does not apply changed container environment settings on its own.

### 4.4 Proxmox ZFS Log Storage

For persistent API logs on Proxmox, keep test and production isolated with separate ZFS datasets and mount each dataset into its matching LXC.

On the Proxmox host:

```bash
zfs create tank/f1/test
zfs create tank/f1/test/logs
zfs create tank/f1/test/selenium

zfs create tank/f1/prod
zfs create tank/f1/prod/logs
zfs create tank/f1/prod/selenium

chown -R 101654:101654 /tank/f1/test /tank/f1/prod
chmod -R 0775 /tank/f1/test /tank/f1/prod

pct set 101 -mp0 /tank/f1/test/logs,mp=/mnt/f1-logs
pct set 101 -mp1 /tank/f1/test/selenium,mp=/mnt/f1-sel

pct set 103 -mp0 /tank/f1/prod/logs,mp=/mnt/f1-logs
pct set 103 -mp1 /tank/f1/prod/selenium,mp=/mnt/f1-sel
```

Notes:

- `101654:101654` matches the `app` user (UID/GID `1654:1654`) used by the `mcr.microsoft.com/dotnet/aspnet:8.0` runtime image inside the current unprivileged LXCs (`f1-test` and `f1-prod`). Confirm with `docker exec <container> id`.
- Restart the containers after adding `mp` mounts.
- Inside each LXC, set `HOST_LOG_PATH=/mnt/f1-logs` in the deployment `.env` before recreating the API container.
- The `selenium` mount is reserved for manual or self-hosted test runs. GitHub Actions still uploads CI artifacts to the workflow run rather than writing them to Proxmox storage.

### 4.5 SSH Log Cheat Sheet

After the mount is in place and the API container has been recreated, use the following commands inside `f1-test` or `f1-prod`.

List log files:

```bash
ls -lah /mnt/f1-logs
```

Follow the newest log file:

```bash
tail -f "$(ls -1t /mnt/f1-logs | head -n 1 | sed 's#^#/mnt/f1-logs/#')"
```

Filter auth failures by reason code:

```bash
grep 'reasonCode' /mnt/f1-logs/*.log | grep 'missing_jwt_header\|missing_email_claim\|token_invalid\|token_expired'
```

Pretty-print compact JSON logs with `jq`:

```bash
jq -c 'select(.eventName == "auth_failure") | {time: .["@t"], reason: .reasonCode, path: .Path, statusCode: .StatusCode, requestId: .RequestId, traceId: .TraceId}' /mnt/f1-logs/*.log
```

Find a single request by request ID:

```bash
grep 'YOUR_REQUEST_ID' /mnt/f1-logs/*.log
```

Inspect manual Selenium artifacts when they exist:

```bash
ls -lah /mnt/f1-sel
find /mnt/f1-sel -type f | sort
```

### 5. Quality Gate Scope (F1-Only)

Quality and analysis gates are intentionally scoped to F1 app code:

- Included: `src/F1.Api`, `src/F1.Core`, `src/F1.Infrastructure`, `src/F1.Services`, `src/F1.Web`, `tests/F1.Api.Tests`, `tests/F1.Web.Tests`
- Excluded from quality/build gates: `src/PopulateF1Database*`

The PopulateF1Database projects remain in the repository for ingestion workflows but are not part of the F1 API/Web quality gate path.

Development & Running the App

This project supports two primary ways to run the application on a Linux VM via Docker. Both modes utilize the F1Competition.sln but handle code execution differently.
1. Normal Mode (Testing/Standard Run)

Use this mode to run the app as a "release candidate." This uses the multi-stage Dockerfile and runs the compiled binaries. It is the closest representation of the production environment.

How to run:
./build.sh

    Behavior: Compiles the app once during the build phase.

    Environment: Defaults to Development (via docker-compose.yml) to enable Cloudflare header mocking.

    Use Case: Verifying overall system stability, UI/UX testing, and preparing for deployment.

2. Debug Mode (Active Development)

Use this mode when you need to set breakpoints, inspect variables, or use "Hot Reload." This mode mounts your source code directly into the container and runs dotnet watch.

How to run:
./build.sh --debug

    Behavior: Uses dotnet watch run to restart the app whenever you save a file on the VM.

    Debugger: Requires vsdbg (installed automatically via Dockerfile.debug).

    Use Case: Fixing bugs, developing new features, and step-through debugging.

Then ctrl+shft+d (or f5) and .Net COre Docker Attach

Troubleshooting Tips

    Container Crashing? Check the logs immediately: docker logs -f f1-local.

    Environment Mismatch? If the logs say Hosting environment: Production, the identity mock is disabled. Ensure ASPNETCORE_ENVIRONMENT=Development is set in your docker-compose.yml.

    Process Picker Empty? Ensure the C# Dev Kit and Docker extensions are installed on the Remote SSH host, not just your local machine.

    Stale DLLs? If the app is acting like old code, the build.sh script handles the clean automatically, but you can run it manually:
    find . -type d −name"bin"−o−name"obj" -exec rm -rf {} +

Once running, the services will be available at:
- **API**: `http://localhost:5000`
- **Web App**: `http://localhost:5001`

## ⚠️ Troubleshooting
### .env Changes Not Applying
Symptom: You updated `.env` (for example `CLOUDFLARE_AUDIENCE`, Postgres settings, or `TAG`) but the app still uses the old value.
Cause: Docker Compose only reads .env when creating containers. Watchtower updates the image but reuses the existing container configuration. 
Fix: Manually recreate the container to apply changes: 
bash +docker-compose up -d


### Why did BLAZOR_ENVIRONMENT affect both API and Web?
`docker-compose.yml` currently maps API `ASPNETCORE_ENVIRONMENT` from `BLAZOR_ENVIRONMENT`.

- `f1-api`: `ASPNETCORE_ENVIRONMENT=${BLAZOR_ENVIRONMENT:-Development}`
- `f1-web`: `BLAZOR_ENVIRONMENT=${BLAZOR_ENVIRONMENT:-Development}`

If you need independent values, split these into separate env vars (for example `API_ENVIRONMENT` and `BLAZOR_ENVIRONMENT`).

## 📄 License

This project is licensed under the Unlicense. See the LICENSE file for details.
