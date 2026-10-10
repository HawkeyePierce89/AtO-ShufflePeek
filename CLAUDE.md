# CLAUDE.md

BepInEx 5 plugin for Across the Obelisk that shows where face-down cards ended up after the minigame shuffle.

## Layout

- `src/Plugin.cs`: entry point, config bindings, overlay host object, scene-change hide.
- `src/Patches.cs`: resolves `CardItem`, `GetCardData` and the card data's `CardName`/`CardType` through cached `AccessTools` reflection, then applies Harmony postfixes on `EnableCards` (capture) and `FinishSelection` (hide) of `CardPlayerManager` and `CardPlayerPairsManager`, plus `PreFinishGame` (reveal all) of `CardPlayerPairsManager`.
- `src/OverlayBehaviour.cs`: IMGUI overlay; re-reads the manager's live `cards` list every frame.
- `src/Layout.cs`: pure layout model (rows, numbering, formatting, colours).
- `tests/AtO-ShufflePeek.Tests`: xUnit tests; the project links `../../src/Layout.cs` directly.
- `nuget.config`: nuget.org plus the BepInEx feed for the compile-only Unity/BepInEx packages.
- `scripts/check-version.ps1`: checks that a `v<version>` tag matches the csproj `Version`; used by the release workflow.
- `.github/workflows/`: `ci.yml` (restore, build, test, DLL artifact) and `release.yml` (tag-triggered GitHub Release).

## Commands

- `dotnet build -c Release -warnaserror` must succeed.
- `dotnet test tests/AtO-ShufflePeek.Tests`

## Rules

- `Layout.cs` stays plain C# with no Unity or game types, because the test project compiles it without them.
- Resolve game types with `AccessTools.TypeByName`, never `typeof`.
- Wrap every patch body and Unity callback in try/catch that logs through `Plugin.Log`.
- Strictly read-only: never write game state and never call `UnityEngine.Random`.
- `CardItem.cardrevealed` is set on every card during the shuffle and never cleared, so it does not mean face up. Use the pairs manager's `cardsSelectedList`, plus a `PreFinishGame` postfix, because that method reveals every remaining card when the rounds run out without adding it to that list.
- Unity and BepInEx assemblies come from the BepInEx NuGet feed (`nuget.config`) as compile-only references; the game assembly is never referenced, all game types (including `CardItem` and its card data) are reached through `AccessTools` at runtime. Nothing is copied to the output except the plugin DLL and pdb.
- The version, GUID and name are set only in the csproj (`Version`, `PluginGuid`, `PluginName`) and reach code through the generated `PluginInfo` class.
