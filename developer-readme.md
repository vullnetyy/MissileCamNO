# MissileCamNO — Developer Notes

Quick reference for getting back into development on this repo. For the full end-to-end story
(environment setup, API verification, publishing to NOMM), see
[NuclearOption-MissileCam-Mod-Guide.md](NuclearOption-MissileCam-Mod-Guide.md).

## Prerequisites

- **.NET SDK 8+** — `winget install --id Microsoft.DotNet.SDK.8 -e` (verify with `dotnet --version`).
- **Nuclear Option** installed via Steam, with **BepInEx 5** installed (via NOMM). See
  [NuclearOption-MissileCam-Mod-Guide.md § 6](NuclearOption-MissileCam-Mod-Guide.md#6-install-bepinex-5-via-nomm-you-have-nomm).
- **Game DLLs must be present locally.** The build references `Assembly-CSharp.dll` and the
  `UnityEngine.*` assemblies directly from your install. The default `GameDir` is set in
  [MissileCamNO.csproj](MissileCamNO.csproj#L14):

  ```
  C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option
  ```

  If your install lives elsewhere, edit the `<GameDir>` line in the `.csproj`. (CI overrides this
  with the `GAME_MANAGED` env var — you don't need to touch that for local dev.)

## Restore & build

```powershell
dotnet restore
dotnet build -c Debug     # or -c Release
```

Output lands in `bin\Debug\MissileCamNO.dll` (or `bin\Release\...`).

> **Auto-deploy:** the `.csproj` has a `DeployToGame` target
> ([MissileCamNO.csproj](MissileCamNO.csproj#L61)) that runs after every build and copies the DLL to
> `<GameDir>\BepInEx\plugins\MissileCamNO\`. So a plain `dotnet build` already installs the mod into
> the game — no manual copy needed.

## Test loop

1. `dotnet build` (auto-deploys the DLL to the game's plugins folder).
2. Launch the game — `steam://run/2168680` or through Steam/NOMM.
3. Watch the BepInEx console for the `MissileCamNO v<version> loaded.` line and any exceptions.
   (Enable the console in `<GameDir>\BepInEx\config\BepInEx.cfg` → `[Logging.Console] Enabled = true`.)
4. Start a Quick Mission / Instant Action, fire a missile, and exercise the `[` `]` `;` keys.

All game-specific calls are isolated in [GameBridge.cs](GameBridge.cs) — if a game update breaks
something, that's the file to fix. Entry point, config, and input handling live in
[Plugin.cs](Plugin.cs).

## Versioning

Bump `<Version>` in [MissileCamNO.csproj](MissileCamNO.csproj#L11). This value flows into the DLL
metadata **and** the BepInEx plugin version. When publishing, the NOMNOM manifest `modVersion` must
match it exactly.

## Releasing (self-hosted runner)

Releases are built and published by [.github/workflows/release.yml](.github/workflows/release.yml),
which runs on a **self-hosted Windows runner** (your PC) because the proprietary game DLLs never
leave your machine. The workflow triggers on any pushed `v*` tag.

**One-time runner setup** — follow
[NuclearOption-MissileCam-Mod-Guide.md § 19a — Option 1 (self-hosted runner)](NuclearOption-MissileCam-Mod-Guide.md#19a-option-1--selfhosted-runner-recommended).
In short: GitHub repo → **Settings → Actions → Runners → New self-hosted runner (Windows)**, then
run the shown install/start commands on this PC. Because the game is installed at the default
`GameDir`, `dotnet build` finds `Assembly-CSharp.dll` and the `UnityEngine.*` DLLs locally with no
extra configuration.

**To release (automated — recommended):**

Run the [releaseUpdate.ps1](releaseUpdate.ps1) helper. It bumps the patch number in
[MissileCamNO.csproj](MissileCamNO.csproj#L11), commits `Update version to <ver>`, tags the commit
`v<ver>`, pushes the commit and tag to origin, then starts the self-hosted runner at
`C:\actions-runner\run.cmd` so it picks up the pushed tag:

```powershell
./releaseUpdate.ps1
```

**To release (manual):**

```powershell
git tag v1.0.6      # must match <Version> in the .csproj
git push origin v1.0.6
```

The workflow builds Release, zips `MissileCamNO-<ver>.dll` + README into
`MissileCamNO-<ver>.zip`, writes a `sha256:` file, and creates the GitHub Release with both assets.
The self-hosted runner must be online (running) for the workflow to pick up the job.

## Publishing to NOMM

After the release exists, register/update the manifest on NOMNOM. See
[NuclearOption-MissileCam-Mod-Guide.md § Part G](NuclearOption-MissileCam-Mod-Guide.md#part-g--publish-to-nomm-via-nomnom).
