# AtO-ShufflePeek: BepInEx plugin that reveals the card layout after the shuffle

## Overview

This project starts from an empty repo that holds only LICENSE and README. It creates a read-only BepInEx 5 plugin for Across the Obelisk v1.7.7.1 (Unity 2022.3, Mono). The plugin hooks the end of the shuffle animation in both face-down card minigames, `CardPlayerManager` and `CardPlayerPairsManager`. At that point it reads the final card positions and draws an IMGUI overlay that lists which card sits at which screen position.

- Injury cards are shown in red with ✗, and boons in green with ✓.
- If the active font lacks those glyphs, the overlay uses the ASCII markers `x` and `+` instead.
- In the pairs game, cards that are already face up are drawn in grey.

The plugin never changes game state, so multiplayer sync is unaffected.

## Context

- Files involved (all new):
  - `AtO-ShufflePeek.csproj`, `Directory.Build.props` and `.gitignore` at the repo root
  - `src/Plugin.cs`: BepInEx entry point, config, logger, the persistent overlay GameObject and the scene-change hook
  - `src/Patches.cs`: Harmony postfixes, applied manually so a missing target is skipped instead of crashing the load
  - `src/Layout.cs`: pure layout model (row grouping, ordering, numbering, text formatting, marker choice). It uses no Unity or game types.
  - `src/OverlayBehaviour.cs`: MonoBehaviour that draws with OnGUI, handles the hotkey, tracks the manager's lifetime and detects glyph support
  - `tests/AtO-ShufflePeek.Tests/AtO-ShufflePeek.Tests.csproj` and `tests/AtO-ShufflePeek.Tests/LayoutTests.cs`
  - `README.md` (rewrite)
- Related patterns: none in the repo yet. Follow standard BepInEx 5 conventions: `BaseUnityPlugin`, `[BepInPlugin]`, `Config.Bind`, `Logger`, and `Harmony` with `AccessTools`.
- Dependencies are resolved by path with `Private=false` and are never committed:
  - From `$(AtOGameDir)\AcrossTheObelisk_Data\Managed`: Assembly-CSharp, UnityEngine, UnityEngine.CoreModule, UnityEngine.IMGUIModule and UnityEngine.TextRenderingModule
  - From `$(BepInExDir)`: BepInEx.dll and 0Harmony.dll
- The test project uses xunit and Microsoft.NET.Test.Sdk from NuGet. These are test-only packages; the ticket bans NuGet packages only for game and loader assemblies.

## Development Approach

- **Testing approach**: Regular (code first, then tests).
- The only logic that can be tested without the game is layout extraction:
  - row grouping by y with a tolerance
  - top-to-bottom and left-to-right ordering
  - numbering
  - line formatting, including both the Unicode and ASCII marker variants
  - marker and colour classification

  This logic lives in `src/Layout.cs` and works on plain values (x, y, name, card type as int, revealed flag). The test project links that single file and runs on net10.0 with no Unity or game assemblies.
- Unity- and game-dependent code (patches, overlay, plugin) is checked by a warning-free `dotnet build -c Release`. In-game behaviour is checked manually (see Post-Completion).
- The main csproj sits at the repo root and must exclude `tests/**` from compilation. Sources live under `src/`.
- The plugin is strictly read-only: no `UnityEngine.Random`, and no writes to manager fields or card lists.
- Every patch body is wrapped in a try/catch that logs through the plugin's `ManualLogSource` and swallows the exception. No `Debug.Log`.
- Commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Complete each task fully before moving to the next.
- **CRITICAL: every task MUST include new/updated tests**
- **CRITICAL: all tests must pass before starting next task**

## Implementation Steps

### Task 1: Project scaffolding and build

**Files:**
- Create: `Directory.Build.props`
- Create: `AtO-ShufflePeek.csproj`
- Create: `.gitignore`
- Create: `src/Plugin.cs` (minimal stub)

- [x] `Directory.Build.props`: define `AtOGameDir` (default `D:\SteamLibrary\steamapps\common\Across the Obelisk`) and `BepInExDir` (default `C:\git\_deps\BepInExPack_AcrossTheObelisk\BepInEx\core`). Guard each with `Condition="'$(X)' == ''"` so it can be overridden.
- [x] `AtO-ShufflePeek.csproj`:
  - target netstandard2.1 (changed from netstandard2.0: Unity 2022 CoreModule references netstandard 2.1, CS1705 otherwise)
  - AssemblyName `AtO-ShufflePeek` and RootNamespace `AtOShufflePeek`
  - `LangVersion` latest
  - HintPath references to the 7 DLLs, all with `Private=false`
  - `Compile Remove="tests/**"`
  - no post-build copy step
- [x] `.gitignore`: `bin/`, `obj/`, `.vs/`, `*.user`.
- [x] `src/Plugin.cs`: class `Plugin : BaseUnityPlugin` with `[BepInPlugin("karmanov.ato.shufflepeek", "ShufflePeek", "1.0.0")]`. It logs the load in `Awake`.
- [x] Run `dotnet build -c Release`. Confirm it produces `bin/Release/netstandard2.1/AtO-ShufflePeek.dll` with no MSB3245 or other missing-reference warnings, and that the output folder contains no game or BepInEx DLLs.
- [x] Tests: none yet. The test project arrives in Task 2 with the first testable code; the warning-free build is this task's gate.

### Task 2: Layout model and unit tests

**Files:**
- Create: `src/Layout.cs`
- Create: `tests/AtO-ShufflePeek.Tests/AtO-ShufflePeek.Tests.csproj`
- Create: `tests/AtO-ShufflePeek.Tests/LayoutTests.cs`

- [x] `Layout.cs` (namespace `AtOShufflePeek`, no Unity or game types):
  - `CardSlot` holds x, y, name, card type (int), a revealed flag, and a source index back into the manager's cards list so revealed state can be refreshed every frame.
  - `CardLayout.Build(IEnumerable<CardSlot>, float rowTolerance = 0.5f)`:
    - groups slots by y within the tolerance
    - orders rows by descending y and cards by ascending x
    - numbers positions 1..N from the top-left
  - `CardKind` classification: Injury when type == 18, Boon when type == 21, otherwise Normal.
  - `CardLayout.MarkerFor(CardKind kind, bool useUnicodeMarkers)`:
    - Unicode: `✗` for Injury and `✓` for Boon
    - ASCII: `x` for Injury and `+` for Boon
    - empty for Normal
  - `FormatRow(row, bool useUnicodeMarkers)` produces plain text: `<n> <Name>` plus ` <marker>` when the marker is not empty, with entries joined by ` · `. Logging calls it with `useUnicodeMarkers: false`; the overlay passes its detected glyph support.
- [x] Test project: net10.0 with xunit. It links `../../src/Layout.cs` via `<Compile Include>` and must not reference any game DLL.
- [x] Tests:
  - A single row in shuffled index order comes out sorted by x.
  - A 12-card pairs grid with indices 0–3 on the bottom row comes out as 3 rows, top first, numbered 1–12 from the top-left.
  - Small y jitter within the tolerance stays in one row; a y gap larger than the tolerance splits rows.
  - `FormatRow` with `useUnicodeMarkers: true` renders `✗` and `✓`; with `false` it renders `x` and `+`. Normal cards get no marker in either mode, and the ` · ` separator is correct.
  - The fallback name is used when given, and empty input yields an empty layout.
- [x] Run `dotnet test tests/AtO-ShufflePeek.Tests`; all tests must pass. The plugin's `dotnet build -c Release` must still be warning-free.

### Task 3: Config and plugin wiring

**Files:**
- Modify: `src/Plugin.cs`

- [x] Bind the config entries:
  - `General.Enabled` (bool, default true)
  - `Overlay.PositionX` and `Overlay.PositionY` (int, default 20 each)
  - `Overlay.FontSize` (int, default 18, `AcceptableValueRange<int>(10, 48)`)
  - `Hotkey.Toggle` (`KeyboardShortcut`, default `KeyCode.F8`)
- [x] Expose a static `Log` (`ManualLogSource`) and the config entries to the other classes.
- [x] Create a persistent `GameObject("ShufflePeek")` with `DontDestroyOnLoad` and `hideFlags = HideAndDontSave`, and add `OverlayBehaviour` to it (an empty stub for now).
- [x] Subscribe to `SceneManager.activeSceneChanged` to hide the overlay.
- [x] Tests: no new unit-testable logic. Re-run `dotnet test` and the warning-free Release build.

### Task 4: Overlay behaviour

**Files:**
- Create: `src/OverlayBehaviour.cs`
- Modify: `src/Layout.cs` (only if colour selection is extracted)
- Modify: `tests/AtO-ShufflePeek.Tests/LayoutTests.cs`

- [ ] State:
  - the captured `CardLayout`
  - a reference to the source manager (`MonoBehaviour`)
  - the cards list reference, for the per-frame `cardrevealed` reads
  - a `visible` flag
  - a cached `bool? useUnicodeMarkers`
- [ ] Public API: `Show(MonoBehaviour manager, IList<CardItem> cards, CardLayout layout)` and `Hide()`.
- [ ] Glyph detection, run once on the first `OnGUI` call (when `GUI.skin` is available):
  - Take the active label style's font: `style.font ?? GUI.skin.font`.
  - Call `font.RequestCharactersInTexture("✓✗", fontSize)`, then check `font.HasCharacter('\u2713') && font.HasCharacter('\u2717')`.
  - Cache the result in `useUnicodeMarkers`. If the font is null or either glyph is missing, use ASCII markers (`+` for Boon, `x` for Injury) so no tofu boxes are drawn.
  - Log the chosen marker set once at Info.
- [ ] `Update`:
  - If the manager is set but now destroyed (Unity null check), hide the overlay.
  - If `Hotkey.Toggle.Value.IsDown()` and a layout has been captured, toggle `visible`.
- [ ] `OnGUI` returns early unless `Enabled` is on, `visible` is true and a layout exists. Otherwise it:
  - Builds a `GUIStyle` with `richText`, using the live `FontSize`.
  - Picks a colour per entry: grey if `cardrevealed` (read live from `CardItem`), otherwise red for Injury, green for Boon, white for anything else.
  - Composes rich-text lines with `CardLayout.MarkerFor(kind, useUnicodeMarkers)` and measures them with `CalcSize`.
  - Draws a semi-transparent dark box (`GUI.color` with alpha, `GUI.DrawTexture` with `Texture2D.whiteTexture`) at the live `PositionX`/`PositionY`, then draws the labels on top.
- [ ] Wrap the `OnGUI` and `Update` bodies in a try/catch that logs once and hides the overlay, to avoid log spam.
- [ ] Tests: if the rich-text colour selection is pure logic, move it into `Layout.cs` as `CardLayout.ColorFor(kind, revealed)` and cover it in `LayoutTests`. The marker variants are already covered by Task 2. Run `dotnet test` and the Release build.

### Task 5: Harmony patches

**Files:**
- Create: `src/Patches.cs`
- Modify: `src/Plugin.cs`

- [ ] `Patches.Apply(Harmony h)` loops over the type names `"CardPlayerManager"` and `"CardPlayerPairsManager"`:
  - Resolve each type with `AccessTools.TypeByName(name)`. Never use `typeof(...)`, so a renamed or removed class cannot throw `TypeLoadException` on plugin load. If the type is null, log a warning and skip both of its patches.
  - Resolve `AccessTools.Field(type, "cards")` at patch time and store it in a `Dictionary<Type, FieldInfo>`. If the field is null, log a warning and skip only the `EnableCards` patch for that type.
  - Resolve `AccessTools.Method(type, "EnableCards")` and `AccessTools.Method(type, "FinishSelection")`. If either is null, log a warning and skip it.
  - Otherwise call `h.Patch(original, postfix: new HarmonyMethod(...))` inside a try/catch and log "applied" at Info.
  - Expected log on load: 4 applied patches.
- [ ] One shared `EnableCardsPostfix(object __instance)` serves both managers:
  - Look up the `FieldInfo` for `__instance.GetType()` in the dictionary and read the list as `IEnumerable`.
  - Cast each element to `CardItem` and read data through the strongly typed API: `GetCardData()` (`CardName`, `(int)CardType`), falling back to `gameObject.name` when the data is null. Also read `transform.position.x/y` and `cardrevealed`.
  - Build the `CardLayout` and log each row at Info with `FormatRow(row, useUnicodeMarkers: false)`, so log lines are always ASCII.
  - Call `Overlay.Show(__instance as MonoBehaviour, cards, layout)`.
  - The body is wrapped in a try/catch, and the postfix only reads.
- [ ] One shared `FinishSelectionPostfix()` calls `Overlay.Hide()` inside a try/catch.
- [ ] Call `Patches.Apply` from `Plugin.Awake` with `new Harmony("karmanov.ato.shufflepeek")`.
- [ ] Grep the sources to confirm there is no `UnityEngine.Random`, no `Debug.Log`, no `typeof(CardPlayerManager)` or `typeof(CardPlayerPairsManager)`, and no field writes on game objects.
- [ ] Tests: building slots from game objects can't be unit-tested without Unity, so check it with the build. The ASCII log formatting is already covered by the `FormatRow` tests. Re-run `dotnet test` and the warning-free `dotnet build -c Release`.

### Task 6: Verify acceptance criteria

- [ ] Delete `bin/` and `obj/`, then run `dotnet build -c Release`. Expect zero warnings, and `bin/Release/netstandard2.1/AtO-ShufflePeek.dll` present with no game or BepInEx DLLs next to it.
- [ ] Run `dotnet test tests/AtO-ShufflePeek.Tests`: all tests pass, and `Layout.cs` is fully covered by tests.
- [ ] Check `git status` / `git ls-files`: no DLLs, `bin/` or `obj/` are tracked.
- [ ] Confirm the build has no copy-to-game-folder target.

### Task 7: Update documentation

- [ ] Rewrite `README.md` in English, covering:
  - what the plugin does, for both minigames
  - the ASCII marker fallback when the game font lacks ✓/✗
  - why it is multiplayer-safe (read-only, and every peer replays the same deterministic shuffle)
  - how to build: the `AtOGameDir` / `BepInExDir` properties with override examples, `dotnet build -c Release`, and `dotnet test`
  - manual install: copy the single DLL into `BepInEx/plugins`
  - the config options with their defaults and the live-reload behaviour
- [ ] No CLAUDE.md exists. Skip it unless the patterns warrant one.

## Post-Completion (manual, in-game)

- Install the DLL into `BepInEx/plugins` and check that the BepInEx log shows the plugin load, 4 applied patches and the chosen marker set.
- Single-pick minigame: after the shuffle, the overlay shows one line in true left-to-right order with the correct red ✗ / green ✓ markers (or `x` / `+`, with no tofu boxes). The picked card matches the overlay.
- Pairs minigame: the three rows, top to bottom, match the screen. Flipped cards confirm their positions, and face-up cards turn grey.
- The overlay disappears on finish and on scene change, and F8 toggles it.
- Editing the position or font size in the config file applies live.
- Multiplayer as a client: the overlay matches what the host reveals.
