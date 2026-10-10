# AtO-ShufflePeek

A read-only BepInEx 5 plugin for Across the Obelisk (v1.7.7.1, Unity 2022.3, Mono). After the shuffle animation in a face-down card minigame finishes, it shows where every card ended up.

## What it does

The plugin hooks the end of the shuffle in both face-down card minigames:

- Single-pick minigame (`CardPlayerManager`): the overlay shows one line that lists the cards in their real left-to-right order on screen.
- Pairs minigame (`CardPlayerPairsManager`): the overlay shows one line per row, top to bottom, with positions numbered 1..N from the top-left. Cards that are face up (picked or matched) are drawn in grey. After a missed pair the game swaps the two cards with other face-down cards, and the overlay follows those swaps.

In both minigames:

- Injury cards are drawn in red with `✗`.
- Boons are drawn in green with `✓`.
- All other cards are drawn in white.

The overlay disappears when the selection finishes or the scene changes. While a layout is captured, the hotkey (F8 by default) toggles it. The layout rows captured after the shuffle are also written to the BepInEx log, always with the ASCII markers `+` and `x`.

### Marker fallback

The first time the overlay is drawn, the plugin checks whether the active IMGUI font contains the `✓` and `✗` glyphs. If the font lacks either glyph, the overlay uses the ASCII markers `+` (boon) and `x` (injury) instead, so it never shows empty boxes. The BepInEx log records which marker set was chosen.

## Multiplayer safety

- The plugin is strictly read-only. It reads card positions, card data and the pairs game's list of face-up cards, but never writes game state and never calls `UnityEngine.Random`, so it cannot desync a session.
- Every peer replays the same deterministic shuffle, so a client's overlay matches what the host sees. No network traffic is involved.

## Building

Requirements: .NET SDK (the tests target net10.0), a local install of the game, and a BepInEx 5 pack.

The game and BepInEx assemblies are referenced by path and are never committed or copied to the output. Two MSBuild properties, set in `Directory.Build.props`, control where they are found:

| Property     | Default                                                      | Used for |
|--------------|--------------------------------------------------------------|----------|
| `AtOGameDir` | `D:\SteamLibrary\steamapps\common\Across the Obelisk`        | `AcrossTheObelisk_Data\Managed\*.dll` (Assembly-CSharp and the UnityEngine modules) |
| `BepInExDir` | `C:\git\_deps\BepInExPack_AcrossTheObelisk\BepInEx\core`     | `BepInEx.dll` and `0Harmony.dll` |

To override them, either pass them on the command line:

```sh
dotnet build -c Release -p:AtOGameDir="E:\Games\Across the Obelisk" -p:BepInExDir="E:\Games\Across the Obelisk\BepInEx\core"
```

or set them as environment variables before building:

```sh
set AtOGameDir=E:\Games\Across the Obelisk
dotnet build -c Release
```

Build the plugin:

```sh
dotnet build -c Release
```

This produces `bin/Release/netstandard2.1/AtO-ShufflePeek.dll`.

Run the unit tests for the layout model. They need no game or Unity assemblies:

```sh
dotnet test tests/AtO-ShufflePeek.Tests
```

## Installing

1. Install BepInEx 5 for Across the Obelisk.
2. Copy `AtO-ShufflePeek.dll` into `<game folder>/BepInEx/plugins`. The build does not copy it for you.
3. Start the game. The BepInEx log should show the plugin loading, four applied patches and the chosen marker set.

## Configuration

The config file `BepInEx/config/karmanov.ato.shufflepeek.cfg` is created on first launch.

| Section   | Key         | Default | Description |
|-----------|-------------|---------|-------------|
| `General` | `Enabled`   | `true`  | Show the overlay after the shuffle. |
| `Overlay` | `PositionX` | `20`    | Left edge of the overlay, in screen pixels. |
| `Overlay` | `PositionY` | `20`    | Top edge of the overlay, in screen pixels. |
| `Overlay` | `FontSize`  | `18`    | Font size, from 10 to 48. |
| `Hotkey`  | `Toggle`    | `F8`    | Toggles the overlay while a layout is captured. |

The overlay reads every value on each frame, so a change made in-game takes effect immediately, for example through BepInEx ConfigurationManager. BepInEx 5 does not watch the `.cfg` file, so edits made to the file directly while the game is running are picked up on the next launch.

## License

See [LICENSE](LICENSE).
