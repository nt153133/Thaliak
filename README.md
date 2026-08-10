<p align="center" style="margin-bottom: 0px;">
  <img width="200" src="assets/logo.svg" alt="Thaliak logo" align="center" />
</p>
<h1 align="center" style="margin-top: 0px;">Thaliak</h1>

Thaliak is a Final Fantasy XIV patch tracking, notification, and artifact service.

This repository is [nt153133's public fork](https://github.com/nt153133/Thaliak) of the
[original CrystallineTools/Thaliak project](https://github.com/CrystallineTools/Thaliak).
It is now maintained as a C#/.NET server implementation. The upstream Rust v2 services have
been intentionally removed; the retained web client is TypeScript/React.

A public instance is available at [thaliak.xiv.dev](https://thaliak.xiv.dev).

## Build availability

The source is public, but this fork is not currently a turnkey third-party build.

The C# projects target .NET 10 and reference `FFXIVDownloader 1.9.0`. That package is supplied
through the maintainer's package infrastructure and is not available as a public NuGet dependency.
A third party must provide access to that package, or supply a compatible replacement, before
restore and build will succeed. The deployment scripts also assume maintainer-owned hosts,
credentials, service accounts, and filesystem layout.

For an environment with the required package available:

```powershell
dotnet restore v1/Thaliak.sln
dotnet test v1/Thaliak.sln --configuration Release
```

The retained web client can be built separately:

```powershell
npm ci --prefix web
npm run build --prefix web
```

## Repository layout

- `v1/` — maintained C#/.NET poller, API, shared database/messages projects, and tests. The
  directory name is retained for deployment compatibility.
- `web/` — retained React/Vite web client.
- `ops/` — maintainer-oriented Linux deployment, health-check, and operational scripts.
- `assets/` — project artwork and README assets.

## Features

- Tracks FFXIV patch versions for Global, Korea, China, and Traditional Chinese regions.
- Uses a Square Enix service account to discover Global patch lists; the other regional
  launchers can be polled without an account.
- Scrapes Lodestone maintenance and notice topics to increase polling frequency around
  maintenance windows.
- Reconciles patch chains, regional installation state, patch archives, and generated
  CLUT/artifact data.
- Exposes the REST API under `/api/v2beta`, plus the compatibility endpoint used by
  `FFXIVDownloader.ThaliakClient`.
- Retains the public web interface and Discord webhook notifications.

## Self-hosted Global accounts

The poller supports a `Routine` Square Enix account for normal checks and a separately
configured `Expansion` account for one-shot expansion patch discovery. On the maintained
Linux deployment, configure the accounts without placing credentials in shell history:

```bash
sudo thaliak-set-sqex-account routine
sudo thaliak-set-sqex-account expansion
```

Expansion discovery runs automatically once for a newly offered Global base patch during
active maintenance. To request one manual sweep, inspect its status, or cancel an unconsumed
request:

```bash
sudo thaliak-expansion-sweep arm
sudo thaliak-expansion-sweep status
sudo thaliak-expansion-sweep cancel
```

## Upstream and license

This fork remains derived from
[CrystallineTools/Thaliak](https://github.com/CrystallineTools/Thaliak) and retains the
project's [AGPL-3.0 license](LICENSE). See Git history for the original authorship and the
changes made in this fork.

FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd.
FINAL FANTASY XIV © 2010-2026 SQUARE ENIX CO., LTD. All Rights Reserved.
This project is not affiliated with SQUARE ENIX CO., LTD.
