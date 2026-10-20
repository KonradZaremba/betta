---
name: betta-release
description: Use when bumping the Betta version, packing Betta NuGet packages (Betta.Abstractions and friends), building the Yak package, or cutting a Betta release via a v* tag.
---

# Releasing Betta

## Version

The version is set ONCE in the repo-root `Directory.Build.props` (`<Version>`); individual csproj files must not set it. Bump it there before each release.

A version bump also needs: the template defaults in `templates/Betta.Templates/content/*/.template.config/template.json` (`TestTemplateVersions` fails until they match), and the `PackageReference` versions in `README.md`, `index.html`, `Betta.Abstractions/README.md`, `Betta.Preview/README.md` and every file under `.claude/skills/betta-plugin/` (`SKILL.md` and `references/`).

## NuGet packages

Packable projects: `Betta.Abstractions`, `Betta.Preview`, `Betta.Generators`, `Betta.Docs`, `Betta.Mcp`, `templates/Betta.Templates`. For `Betta.Abstractions`, the core SDK package, `GeneratePackageOnBuild` is **off** — packaging is opt-in:

```bash
dotnet pack Betta.Abstractions/Betta.Abstractions.csproj -c Release
```

Output: `./artifacts/Betta.Abstractions.<Version>.nupkg` + matching `.snupkg` (the artifacts folder is gitignored). The package readme is the local `Betta.Abstractions/README.md`, separate from the repo-root README — its `PackageReference` version must be bumped too, since it becomes the nuget.org page.

`Betta.Abstractions`, `Betta.Preview`, `Betta.Templates` and the `Betta.Mcp` dotnet tool are published by the release workflow (below) via nuget.org Trusted Publishing — no API key is stored. A manual `dotnet nuget push` needs an API key, and nuget.org keys now last at most 30 days; the nuget.org website's Upload page needs none.

Don't pack `Betta.gha` or `Betta.Strings` — they aren't designed as NuGet payloads (Betta.gha is a Grasshopper plugin, Betta.Strings is sample art).

## Yak package and GitHub Release

```bash
dotnet build Betta/Betta.sln -c Release -p:BuildYak=true   # .yak + drop-in .zip in artifacts/
```

Pushing a `v*` tag runs `.github/workflows/release.yml` on `windows-latest`. It does the same `BuildYak` Release build, packs `Betta.Abstractions`, `Betta.Preview`, `Betta.Templates` and `Betta.Mcp` and fails unless the tag matches `<Version>` (`v0.7.2.1` ↔ `Betta.Abstractions.0.7.2.1.nupkg`), creates a GitHub Release with the `.zip`/`.yak`, pushes all four to nuget.org in one wildcard push (`--skip-duplicate`, so re-runs are safe), then runs `yak push`. The workflow file must be in the tagged commit — GitHub runs the version at the tag.
