# V1 FFXIVDownloader-Compatible Thaliak API

## Summary

- Add a new `net8.0` ASP.NET Core service at `v1/Thaliak.Service.Api`, separate from the existing V1 poller.
- The API reads the live V1 SQLite database and never runs migrations or writes data.
- The public contract is V2-style REST under `/api/v2beta`, plus a tiny `/graphql/2022-08-14` compatibility shim so current `FFXIVDownloader.ThaliakClient` works after only changing its base URL.
- Return upstream patch URLs from the database; do not serve local downloaded patch files in this first version.

## Architecture Decision

Use a separate ASP.NET Core read-only API process beside the deployed V1 poller.

This keeps the poller responsible for writes, downloads, alerts, scheduling, and migrations. The API opens the same SQLite database in read-only mode, projects database entities into explicit wire DTOs, and exposes only the metadata endpoints needed by FFXIVDownloader and the V2-style REST contract.

## Service Layout

- Project: `v1/Thaliak.Service.Api`
- Target framework: `net8.0`
- Solution: `v1/Thaliak.sln`
- Reference: `Thaliak.Common.Database`
- Default connection string: `Data Source=/data/thaliak.db;Mode=ReadOnly;Cache=Shared`
- EF Core options:
  - SQLite provider
  - `UseSnakeCaseNamingConvention()`
  - split queries
  - no tracking by default

The API must not call `Database.Migrate()`, `EnsureCreated()`, or any write path at startup.

## REST Contract

Expose these endpoints under `/api/v2beta`:

- `GET /repositories`
- `GET /repositories/{slug}`
- `GET /repositories/{slug}/patches?from=&to=&all=&active=`
- `GET /repositories/{slug}/patches/{version}`

Repository responses match the `RepositoryV2` wire shape expected by FFXIVDownloader:

- `service_id`
- `slug`
- `name`
- `description`
- `latest_patch.version_string`
- `latest_patch.first_offered`
- `latest_patch.last_offered`

V1 numeric service ids map to V2 ids as:

- `1 => jp`
- `2 => kr`
- `3 => cn`
- `4 => tw`

Patch responses map V1 `XivPatch` fields as:

- `RemoteOriginPath => remote_url`
- `LocalStoragePath => local_path`
- `FirstSeen => first_seen`
- `LastSeen => last_seen`
- `FirstOffered => first_offered`
- `LastOffered => last_offered`
- `Size => size`
- `IsActive => is_active`
- `HashType`, `HashBlockSize`, and `Hashes => hash`

## GraphQL Compatibility Shim

Expose `POST /graphql/2022-08-14`.

Support only the query shapes currently used by `FFXIVDownloader.ThaliakClient`:

- Repository metadata:
  - `repository(slug: $repoId) { name description latestVersion { versionString } }`
- Repository versions:
  - `repository(slug: $repoId) { versions { versionString isActive prerequisiteVersions { versionString } patches { url size } } }`

GraphQL behavior:

- `patches.url` returns `XivPatch.RemoteOriginPath`.
- `patches.size` returns `XivPatch.Size`.
- `prerequisiteVersions` is built from `XivUpgradePath.PreviousRepoVersion`.
- Version `isActive` is true when that repo version has at least one active patch.
- Unsupported query shapes return a GraphQL-style `errors` response.

## Data Behavior

- Repository latest patch is the highest active repo version that has at least one active patch.
- REST patch chains use the V1 upgrade path graph and return upstream URLs.
- Local patch-file serving is intentionally out of scope for this first pass.
- Unknown repositories or versions return `404`.

## Tests And Acceptance

Add API integration tests with a temporary SQLite database seeded with repositories, versions, patches, and upgrade paths.

Verify:

- Exact JSON casing for `GET /api/v2beta/repositories/{slug}` against the `RepositoryV2` contract.
- GraphQL metadata deserializes through the current `Repository`/`RepositoryResponse` shape.
- GraphQL patch-chain data is sufficient for current `GetPatchChainAsync`.
- Unknown slug/version returns `404`.

Run:

- `dotnet build v1/Thaliak.sln`
- `dotnet test v1/Thaliak.sln`

Manual VPS acceptance:

- Publish API as a second service bound to localhost, for example `http://127.0.0.1:5080`.
- Reverse proxy `/api/v2beta/*` and `/graphql/*` to the API.
- Point `FFXIVDownloader.ThaliakClient` at the VPS base URL.
- Confirm `GetRepositoryV2Async`, `GetRepositoryMetadataAsync`, and `GetPatchChainAsync` all succeed for `4e9a232b`.

## Assumptions

- The V1 poller remains unchanged and continues owning writes, downloads, alerts, and migrations.
- The first version is compatibility-focused, not a full public GraphQL server.
- Local patch-file serving is intentionally out of scope for this first pass.
- No authentication is required for these read-only metadata endpoints.
