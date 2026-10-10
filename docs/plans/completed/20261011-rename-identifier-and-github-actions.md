# Rename the plugin identifier, build without a game install, and add GitHub Actions

## Overview

This plan lands four changes on one branch and in one PR:

1. **Rename the plugin GUID and the Harmony instance id** from `karmanov.ato.shufflepeek` to `hawkeyepierce89.ato.shufflepeek`. The GUID is defined once.
2. **Make the project buildable from a clean clone** with only the .NET SDK and NuGet:
   - Drop the compile-time `Assembly-CSharp` reference and reach `CardItem` and its card data through cached `AccessTools` reflection.
   - Take Unity, BepInEx and Harmony from the BepInEx NuGet feed as compile-only package references.
   - Delete `Directory.Build.props`.
3. **Keep the plugin version in one hand-edited place**, `<Version>` in `AtO-ShufflePeek.csproj`. An MSBuild target generates a `PluginInfo` constants class from it, and the `BepInPlugin` attribute, the Harmony id and the "loaded" log line all use that class. The assembly version attributes come from the same property.
4. **Add GitHub Actions:**
   - A CI workflow that runs restore, a warnings-as-errors Release build and the tests, then uploads the DLL as an artifact.
   - A tag-triggered release workflow. It fails fast through one shared version-check script, then publishes the DLL and a zip with auto-generated notes.

## Context

- Files involved:
  - Modify: `AtO-ShufflePeek.csproj`, `src/Plugin.cs`, `src/Patches.cs`, `src/OverlayBehaviour.cs`, `README.md`, `CLAUDE.md`
  - Delete: `Directory.Build.props`
  - Create: `nuget.config`, `scripts/check-version.ps1`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`
  - Untouched: `src/Layout.cs`, `tests/**`, `LICENSE`, `docs/plans/completed/**`
- Related patterns:
  - Game types are already resolved with `AccessTools.TypeByName`.
  - Missing members are logged and their patches skipped. Fields are cached per manager type in `Patches`.
  - Every patch body and Unity callback has a try/catch that logs through `Plugin.Log`.
- Dependencies:
  - Packages from the BepInEx feed `https://nuget.bepinex.dev/v3/index.json`: `UnityEngine.Modules` 2022.3.62 and `BepInEx.Core` 5.4.21 (which brings in HarmonyX transitively).
  - GitHub-hosted runner tools: `pwsh` and the `gh` CLI, both preinstalled on `ubuntu-latest`.
  - Remote: `https://github.com/HawkeyePierce89/AtO-ShufflePeek`. The default branch is `master`.

## Development Approach

- **Testing approach**: Regular (code first, then verification).
- **Why there are no new unit tests**: the code being changed (reflection over game types, MSBuild, workflows) depends on Unity, the game or GitHub, so xUnit cannot reach it. The test project deliberately links only `src/Layout.cs`. Each task therefore ends with:
  - the existing 32-test suite;
  - a Release build that must have zero warnings;
  - concrete, scripted verification commands (output directory listing, assembly reference inspection, a version-check dry run).

  The layout model does not change.
- Complete each task fully before moving to the next.
- Follow the CLAUDE.md rules: try/catch in every patch body and Unity callback, `Layout.cs` free of Unity and game types, no writes to game state, no `UnityEngine.Random`.
- Shell-neutral commands: the executor runs bash on Windows and CI runs on Linux, so verification commands avoid pwsh-only syntax unless they invoke `pwsh` explicitly.
- **CRITICAL: every task MUST include new/updated tests or explicit verification steps.**
- **CRITICAL: all tests must pass before starting the next task.**

## Implementation Steps

### Task 1: Single-source version and GUID through a generated PluginInfo class

**Files:**
- Modify: `AtO-ShufflePeek.csproj`
- Modify: `src/Plugin.cs`

- [x] In the csproj `PropertyGroup`, add `<Version>1.0.0</Version>`, `<PluginGuid>hawkeyepierce89.ato.shufflepeek</PluginGuid>` and `<PluginName>ShufflePeek</PluginName>`. Also add `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>` so the informational version stays exactly `1.0.0`. `AssemblyVersion`, `FileVersion` and `InformationalVersion` then come from `Version` through the SDK's default assembly-info generation.
- [x] Add a target `GeneratePluginInfo` that runs `BeforeTargets="CoreCompile"`, with `Inputs` set to `$(MSBuildProjectFullPath)` and `Outputs` set to the generated file.
  - It writes `$(IntermediateOutputPath)PluginInfo.g.cs` with `WriteLinesToFile` (`Overwrite="true"`, `WriteOnlyWhenDifferent="true"`).
  - The file holds `namespace AtOShufflePeek { internal static class PluginInfo { public const string Guid = "$(PluginGuid)"; public const string Name = "$(PluginName)"; public const string Version = "$(Version)"; } }`.
  - `WriteLinesToFile` splits its `Lines` item list on semicolons, so a literal `;` would end the item and be dropped. Write every semicolon in the generated code as `%3B` inside the target. The simplest form is one `Line` item per statement, each ending in `%3B`, for example `<_PluginInfoLine Include="    public const string Guid = &quot;$(PluginGuid)&quot;%3B" />`.
  - The target adds the file to `Compile` and to `FileWrites`.
  - Because the file lives under `obj/`, it is never hand-edited or committed.
- [x] In `src/Plugin.cs`:
  - Change the attribute to `[BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]`.
  - Use `new Harmony(PluginInfo.Guid)`.
  - Change the log line to `$"{PluginInfo.Name} {PluginInfo.Version} loaded"`.
  - This leaves no literal GUID, name or version in the C# source.
- [x] Verify:
  - `dotnet build -c Release` succeeds with zero warnings. This proves that `obj/Release/netstandard2.1/PluginInfo.g.cs` compiles.
  - `obj/Release/netstandard2.1/PluginInfo.g.cs` contains the new GUID and `1.0.0`, and every statement in it ends with a `;`.
  - `git grep -n "1\.0\.0" -- . ':!docs/plans/completed'` shows the version only in `AtO-ShufflePeek.csproj`.
- [x] Run `dotnet test tests/AtO-ShufflePeek.Tests`. All 32 tests must pass.

### Task 2: Replace compile-time CardItem use with cached reflection and drop Assembly-CSharp

**Files:**
- Modify: `src/Patches.cs`
- Modify: `src/OverlayBehaviour.cs`
- Modify: `AtO-ShufflePeek.csproj`

- [x] In `Patches`, add cached static members: the `CardItem` type, the `GetCardData` `MethodInfo`, and accessors for `CardName` and `CardType`.
  - Resolve `CardItem` with `AccessTools.TypeByName("CardItem")`.
  - Resolve `GetCardData` with `AccessTools.Method(cardItemType, "GetCardData", Type.EmptyTypes)`.
  - Take the card-data type from the method's `ReturnType`.
  - For each of `CardName` and `CardType`, resolve `AccessTools.Property(dataType, name)`. If there is no such property, fall back to `AccessTools.Field(dataType, name)`, and store a small getter (`Func<object, object>`) for whichever was found.
- [x] Add a `ResolveCardMembers()` method that returns false and logs the first unresolved member with `Plugin.Log.LogError`.
  - Call it at the top of `Apply`, before the manager loop.
  - If it fails, log `0 patches applied` and return without patching anything. This mirrors today's handling of missing types and methods.
- [x] In `EnableCardsPostfix`, read the `cards` field as non-generic `System.Collections.IList`.
  - Cast each element to `UnityEngine.Component` for `transform.position`.
  - Keep the null check with Unity's overloaded `==` on the `Component`, so destroyed cards are still skipped.
  - Pass the `IList` to `Overlay.Show`.
- [x] Change `ReadSlot` to take `object card`.
  - Get `data` by invoking the cached `GetCardData`.
  - Read the name through the cached getter, cast to string.
  - Read the type through the cached getter with `Convert.ToInt32` on the boxed enum.
  - Take `gameObject.name` from `((Component)card)`.
  - The `CardSlot` fields, the face-up logic and `allFaceUp` handling stay the same.
- [x] In `OverlayBehaviour`, change the `cards` field and the `Show` parameter to `System.Collections.IList`.
  - In `Reread`, take `cards[slot.SourceIndex] as Component` and do the null check with Unity's `==`, as before.
  - No change to drawing, the toggle or hiding.
- [x] Remove the `Assembly-CSharp` `<Reference>` from the csproj.
- [x] Verify:
  - `git grep -n CardItem src/` shows only string literals and comments.
  - `dotnet build -c Release` finishes with zero warnings.
- [x] Run `dotnet test tests/AtO-ShufflePeek.Tests`. All 32 tests must pass.

### Task 3: Switch Unity and BepInEx references to compile-only NuGet packages

**Files:**
- Create: `nuget.config`
- Modify: `AtO-ShufflePeek.csproj`
- Delete: `Directory.Build.props`

- [x] Create `nuget.config` with `<clear />`, `nuget.org` (`https://api.nuget.org/v3/index.json`) and `BepInEx` (`https://nuget.bepinex.dev/v3/index.json`). The test project restores from the same file and only needs nuget.org.
- [x] Replace every by-path `<Reference>` and the `ManagedDir` property with these package references, pinned exactly with NuGet's bracket syntax:
  - `<PackageReference Include="UnityEngine.Modules" Version="[2022.3.62]" IncludeAssets="compile" PrivateAssets="all" />`
  - `<PackageReference Include="BepInEx.Core" Version="[5.4.21]" IncludeAssets="compile" PrivateAssets="all" />`
- [x] Add `<GenerateDependencyFile>false</GenerateDependencyFile>` so no `.deps.json` is produced. Keep `CopyLocalLockFileAssemblies` at its library default of false, so no package DLLs are copied.
- [x] Keep the `tests/**` `Compile`/`None`/`EmbeddedResource` removals.
- [x] Delete `Directory.Build.props`.
- [x] Verify the build no longer depends on the old properties:
  - `git grep -n -e AtOGameDir -e BepInExDir -- . ':!docs/plans/completed'` returns only README/CLAUDE.md lines (fixed in Task 6).
  - First delete `bin/` and `obj/` of both projects (the root project and `tests/AtO-ShufflePeek.Tests`). Then run `env -u AtOGameDir -u BepInExDir dotnet build -c Release -warnaserror`. It must succeed with zero warnings.
- [x] Verify that `bin/Release/netstandard2.1/` contains only `AtO-ShufflePeek.dll` and `AtO-ShufflePeek.pdb`.
- [x] Inspect the built assembly's references with a one-off pwsh snippet using `System.Reflection.Metadata` (`PEReader` → `GetMetadataReader().AssemblyReferences`). Check that:
  - `Assembly-CSharp` is absent;
  - `UnityEngine.CoreModule`, `UnityEngine.IMGUIModule`, `BepInEx` and `0Harmony` are present.
- [x] With the same reader, decode the `BepInPlugin` custom attribute's fixed arguments. They must read `hawkeyepierce89.ato.shufflepeek`, `ShufflePeek` and `1.0.0`.
- [x] Run `dotnet test tests/AtO-ShufflePeek.Tests`. All 32 tests must pass, and the test project restores without any BepInEx-feed package.

### Task 4: Shared version-check script

**Files:**
- Create: `scripts/check-version.ps1`

- [x] Write a pwsh script with a mandatory `-Tag` parameter:
  - It reads the plugin version with `dotnet msbuild <repo>/AtO-ShufflePeek.csproj -getProperty:Version`, using a path resolved from `$PSScriptRoot`.
  - If `-Tag` is not `v<version>`, it fails with `Write-Error` and `exit 1`, naming both values.
  - If the tag matches, it prints the version and, when `$env:GITHUB_OUTPUT` is set, appends `version=<version>` to it.
  - It sets `$ErrorActionPreference = 'Stop'` and checks `$LASTEXITCODE` after the `dotnet` call.
- [x] Dry run locally:
  - `pwsh scripts/check-version.ps1 -Tag v9.9.9` must exit non-zero with a mismatch message.
  - `pwsh scripts/check-version.ps1 -Tag v1.0.0` must exit 0 and print `1.0.0`.
  - Also confirm that `-Tag 1.0.0` (without the `v`) is rejected.
- [x] Run `dotnet test tests/AtO-ShufflePeek.Tests`. All tests must still pass.

### Task 5: GitHub Actions workflows

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release.yml`

- [x] `ci.yml`:
  - Name: `CI`.
  - Triggers: `on: push: branches: [master]` and `pull_request`.
  - Permissions: top-level `permissions: contents: read`.
  - One job on `ubuntu-latest` with these steps:
    1. `actions/checkout@v7`
    2. `actions/setup-dotnet@v6` with `dotnet-version: 10.0.x`
    3. `dotnet restore AtO-ShufflePeek.csproj` and `dotnet restore tests/AtO-ShufflePeek.Tests`
    4. `dotnet build AtO-ShufflePeek.csproj -c Release --no-restore -warnaserror`
    5. `dotnet test tests/AtO-ShufflePeek.Tests -c Release --no-restore`
    6. `actions/upload-artifact@v7`, artifact name `AtO-ShufflePeek`, path `bin/Release/netstandard2.1/AtO-ShufflePeek.dll`
- [x] `release.yml`:
  - Triggers: `on: push: tags: ['v*']`.
  - Permissions: top-level `permissions: contents: read`, and `contents: write` only on the release job.
  - Job on `ubuntu-latest` with these steps:
    1. `actions/checkout@v7` and `actions/setup-dotnet@v6` (`dotnet-version: 10.0.x`), as in `ci.yml`.
    2. Step `id: version` that runs `pwsh scripts/check-version.ps1 -Tag "$GITHUB_REF_NAME"`. It runs before any build, so a wrong tag fails fast.
    3. The same restore, build (`-warnaserror`) and test steps as `ci.yml`.
    4. Stage the DLL, `README.md` and `LICENSE`, and build `AtO-ShufflePeek-v${{ steps.version.outputs.version }}.zip` with `zip -j`.
    5. Run `gh release create "$GITHUB_REF_NAME" --generate-notes bin/Release/netstandard2.1/AtO-ShufflePeek.dll AtO-ShufflePeek-v<version>.zip` with `GH_TOKEN: ${{ github.token }}`.
- [x] Confirm that no CI logic lives outside `.github/workflows/` apart from `scripts/check-version.ps1`, and that neither workflow references a Windows path or a game directory.
- [x] Locally, run the exact CI command sequence from `ci.yml` (restore, `build -warnaserror`, test). It must pass. If `actionlint` is available, validate both YAML files with it; otherwise, if Python is present, parse them with `python -c "import yaml"`, since `pwsh` has no built-in `ConvertFrom-Yaml`. This is a syntax sanity check only.

### Task 6: Update README.md and CLAUDE.md

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`

- [x] README:
  - Add a CI badge under the title: `[![CI](https://github.com/HawkeyePierce89/AtO-ShufflePeek/actions/workflows/ci.yml/badge.svg)](https://github.com/HawkeyePierce89/AtO-ShufflePeek/actions/workflows/ci.yml)`.
  - Rewrite "Building":
    - The only prerequisite is the .NET SDK (10).
    - Unity, BepInEx and Harmony reference assemblies come from the BepInEx NuGet feed declared in `nuget.config`, as compile-only references.
    - The game assembly is not referenced. The game install is needed only to run the plugin.
    - Keep the build and test commands and the output path. Remove the property table and the override examples.
  - Add a short "Releases" section: download `AtO-ShufflePeek.dll` (or the zip) from the GitHub Releases page; CI artifacts are available for untagged builds. Leave the manual install steps unchanged.
  - Change the configuration section to `BepInEx/config/hawkeyepierce89.ato.shufflepeek.cfg`.
  - Note that the version lives in `<Version>` in the csproj, and that a release is published by pushing a `v<version>` tag.
- [x] CLAUDE.md:
  - Replace the `AtOGameDir`/`BepInExDir` rule with: "Unity and BepInEx assemblies come from the BepInEx NuGet feed (`nuget.config`) as compile-only references; the game assembly is never referenced, all game types (including `CardItem` and its card data) are reached through `AccessTools` at runtime. Nothing is copied to the output except the plugin DLL and pdb."
  - Add a rule that the version, GUID and name are set only in the csproj (`Version`, `PluginGuid`, `PluginName`) and reach code through the generated `PluginInfo` class.
  - Update the Layout section: add `scripts/check-version.ps1`, `.github/workflows/`, `nuget.config` and the reflection-based `Patches`.
  - Update Commands: `dotnet build -c Release -warnaserror`.
- [x] Verify (only matches are in this active plan file, which moves to completed in Task 8):
  - `git grep -n -i karmanov -- . ':!LICENSE' ':!docs/plans/completed'` returns nothing.
  - `git grep -n -e AtOGameDir -e BepInExDir -e Directory.Build.props -- . ':!docs/plans/completed'` returns nothing.
- [x] Run `dotnet test tests/AtO-ShufflePeek.Tests`. All 32 tests must pass.

### Task 7: Verify acceptance criteria

- [x] First delete `bin/` and `obj/` of both projects (the root project and `tests/AtO-ShufflePeek.Tests`). Then:
  - `env -u AtOGameDir -u BepInExDir dotnet build -c Release -warnaserror` must report zero warnings;
  - `dotnet test tests/AtO-ShufflePeek.Tests` must pass 32 tests.
- [x] Check that the output directory listing is only `AtO-ShufflePeek.dll` and `AtO-ShufflePeek.pdb`.
- [x] Re-run the metadata inspection from Task 3:
  - references: no `Assembly-CSharp`; `UnityEngine.CoreModule`, `BepInEx` and `0Harmony` present;
  - `BepInPlugin` arguments: GUID `hawkeyepierce89.ato.shufflepeek`, name `ShufflePeek`, version `1.0.0`;
  - assembly version `1.0.0.0`.
- [x] Re-run the version-check dry run. `v9.9.9` must be rejected and `v1.0.0` accepted.
- [x] Re-run the `karmanov` grep with the ticket's exclusions (`LICENSE`, `.git`, `bin`, `obj`, `docs/plans/completed`). It must be empty. (only hits are in this active plan file, which moves to completed in Task 8)
- [x] Confirm that the version string `1.0.0` is hand-written only in `AtO-ShufflePeek.csproj`. (README mentions `v1.0.0` only as an example tag in prose)

### Task 8: Final documentation check

- [x] Re-read README.md and CLAUDE.md against the final csproj, workflows and script. The build, release and configuration text must match the implementation.
- [x] Move this plan to `docs/plans/completed/`.

## Post-Completion (manual, owner)

- Open the PR and confirm that the CI workflow is green on `ubuntu-latest`.
- Download the `AtO-ShufflePeek` artifact from the CI run and copy it into `<game>/BepInEx/plugins`. You can delete the old `karmanov.ato.shufflepeek.cfg` if you like; nothing uses it any more. Then check:
  - the log shows "ShufflePeek 1.0.0 loaded" and five applied patches;
  - the overlay works in the single-pick and the pairs minigames, including pair swaps and the end-of-rounds reveal;
  - the new config file `hawkeyepierce89.ato.shufflepeek.cfg` is created.
- After the squash-merge, decide whether to push `v1.0.0`, which triggers the release workflow. Then check that the GitHub Release has auto-generated notes, `AtO-ShufflePeek.dll` and `AtO-ShufflePeek-v1.0.0.zip` (DLL + README.md + LICENSE).
