# Missile Follow Cam

A mod for **Nuclear Option** that lets you watch your own missiles fly, then jump right back into your cockpit. Works in single-player missions.

## What it does

Fire a missile, hit the "\[" or "\]" key, and the camera follows it. Cycle through all your missiles in the air, then snap back to your plane whenever you like — or just wait, and the camera returns to your cockpit on its own a few seconds after your last missile detonates. Your missile-warning alarms keep sounding the whole time.

## Controls

| Key | Action |
|-----|--------|
| `]` | Follow your **next** missile |
| `[` | Follow your **previous** missile |
| `;` | **Return to your aircraft** |
| `L` | (while watching a missile) cycle the missile's camera angles |

*The camera also returns to your cockpit automatically ~3 seconds after your last in-flight missile detonates.*

## Installing

1. Make sure the mod is installed through **NOMM** (Nuclear Option Mod Manager) and enabled.
2. Launch the game. That's it.

## Changing the keys

Prefer different keys? Two easy ways:

- **Config file:** Open `Nuclear Option\BepInEx\config\MissileCamNO.cfg`, find the `[Controls]` section, change the key names (e.g. `ReturnToAircraft = Semicolon`), save, and restart the game.
- **In game:** Install the **BepInEx Configuration Manager** plugin, press **F1** in game, find *Missile Follow Cam*, and click a key to rebind it.

## Auto-return to cockpit

By default the camera returns to your cockpit **3 seconds** after your last in-flight missile detonates. Tune it in the same `MissileCamNO.cfg` file (or via the F1 config manager) under the `[Behavior]` section:

- `AutoReturnToCockpit = true` — set to `false` to disable and only return on the `;` key.
- `AutoReturnDelaySeconds = 3` — change the delay (in seconds) before the camera snaps back.

## Notes

- Single-player / offline missions only.
- Not sure what to type for a key name? Use Unity key names like `Semicolon`, `LeftBracket`, `RightBracket`, `L`, `Keypad0`.

Enjoy the show. 🚀
