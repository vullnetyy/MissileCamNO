# Nuclear Option — "Missile Follow Cam" Mod: Complete Build Guide

> A single, sequential, copy‑paste‑able guide that takes you from an empty Windows 11 machine to
> a **published** Nuclear Option mod that lets you **cycle through and follow your own in‑flight
> missiles while flying**, keep hearing incoming‑missile warnings the whole time, and snap the
> camera back to your aircraft's cockpit with a **configurable key (default `;`)**.
>
> **Reader:** senior software engineer, comfortable with the terminal, new to Unity/BepInEx.
> **Toolchain:** VS Code + .NET SDK CLI. **OS:** Windows 11. **Game:** Steam. **NOMM:** already installed.
> **Mod scope:** single‑player / offline missions only (no multiplayer / netcode).

---

## Table of Contents

- [0. Read this first (5 minutes that save you hours)](#0-read-this-first-5-minutes-that-save-you-hours)
  - [The feature is basically a "select unit on map → follow it" call, invoked from the cockpit](#the-feature-is-basically-a-select-unit-on-map--follow-it-call-invoked-from-the-cockpit)
  - [Study these real mods (your best reference material)](#study-these-real-mods-your-best-reference-material)
  - [Legend](#legend)
- [Part A — Environment setup (Windows 11)](#part-a--environment-setup-windows-11)
  - [1. Install the .NET SDK](#1-install-the-net-sdk)
  - [2. Install VS Code + C#](#2-install-vs-code--c)
  - [3. Install a decompiler (you'll live in this to verify class names)](#3-install-a-decompiler-youll-live-in-this-to-verify-class-names)
  - [4. Install Git](#4-install-git)
  - [5. Find your game folder](#5-find-your-game-folder)
  - [6. Install BepInEx 5 via NOMM (you have NOMM)](#6-install-bepinex-5-via-nomm-you-have-nomm)
  - [7. Configure BepInEx (console + a required setting)](#7-configure-bepinex-console--a-required-setting)
- [Part B — Create the project](#part-b--create-the-project)
  - [8. Scaffold](#8-scaffold)
  - [9. The `.csproj` (edit only the two `<GameDir>` lines)](#9-the-csproj-edit-only-the-two-gamedir-lines)
  - [10. `.gitignore`](#10-gitignore)
- [Part C — Verify the game API (mandatory, ~20–30 min)](#part-c--verify-the-game-api-mandatory-2030-min)
- [Part D — Write the mod](#part-d--write-the-mod)
  - [11. `Plugin.cs` — entry point, config, input loop](#11-plugincs--entry-point-config-input-loop)
  - [12. `GameBridge.cs` — the single file that touches the game](#12-gamebridgecs--the-single-file-that-touches-the-game)
  - [13. Behavior recap (what you built)](#13-behavior-recap-what-you-built)
  - [14. Why the warnings keep playing (confirmed — no code needed)](#14-why-the-warnings-keep-playing-confirmed--no-code-needed)
- [Part E — Local build + test automation](#part-e--local-build--test-automation)
  - [15. First build](#15-first-build)
  - [16. One‑command build → deploy → launch → tail logs](#16-onecommand-build--deploy--launch--tail-logs)
  - [17. Test loop](#17-test-loop)
- [Part F — CI: automated release pipeline (GitHub Actions)](#part-f--ci-automated-release-pipeline-github-actions)
  - [18. Create the public repo](#18-create-the-public-repo)
  - [19a. Option 1 — self‑hosted runner (recommended)](#19a-option-1--selfhosted-runner-recommended)
  - [19b. Option 2 — hosted runner + private reference DLLs](#19b-option-2--hosted-runner--private-reference-dlls)
  - [20. Cut a release](#20-cut-a-release)
- [Part G — Publish to NOMM (via NOMNOM)](#part-g--publish-to-nomm-via-nomnom)
  - [21. Requirements checklist](#21-requirements-checklist)
  - [22. Get the release SHA256](#22-get-the-release-sha256)
  - [23. Add the manifest to NOMNOM](#23-add-the-manifest-to-nomnom)
  - [24. Submit](#24-submit)
- [Part H — Troubleshooting](#part-h--troubleshooting)
- [Part I — Appendix: verified API this mod uses](#part-i--appendix-verified-api-this-mod-uses)

---

## 0. Read this first (5 minutes that save you hours)

Nuclear Option (Steam AppID **2168680**, Shockfront Studios) has **no official mod API**. The
community stack is:

| Layer | What it is | Role for you |
|---|---|---|
| **BepInEx 5** | Unity Mono plugin loader/injector | Loads your compiled `.dll` at game start |
| **HarmonyX (`0Harmony`)** | Runtime method patching (ships with BepInEx 5) | Only needed if you patch game methods (we won't need to) |
| **`Assembly-CSharp.dll`** | The game's compiled C# code | The classes you read/drive |
| **NOMM** | Combat787's *Nuclear Option Mod Manager* (desktop app) | Installs BepInEx; installs/toggles mods |
| **NOMNOM** | KopterBuzz's manifest registry NOMM reads | Where you publish so users see your mod |

**Pipeline you'll build:**

```
 VS Code + dotnet CLI ─build─► MissileCamNO.dll
      │                              │
      │ (local test)                 │ (release)
      ▼                              ▼
 copy to BepInEx\plugins ─► Steam    GitHub Actions ─► tagged Release (.zip + sha256)
                                          │
                                          ▼
                                NOMNOM manifest PR ─► shows in NOMM ─► users install
```

### The feature is basically a "select unit on map → follow it" call, invoked from the cockpit

Verified in real community mod source: clicking a unit on the tactical map makes the game camera
orbit/follow it via `CameraStateManager.SetFollowingUnit(unit)` + `SwitchState(...)`. Your mod
calls that **same API** for your own in‑flight missiles, on a keypress, while you fly. Because you
reuse the game's own camera path:
- while following a missile, the native camera key (**L**) still cycles through that missile's
  camera angles; a **dedicated configurable key (default `;`)** points the camera back at your
  aircraft's cockpit, and
- **RWR / missile‑warning audio keeps playing** (those alarms are 2D HUD audio driven by the
  aircraft's sensors, not tied to the camera — confirmed below).

### Study these real mods (your best reference material)

You are essentially writing an enhanced sibling of an existing mod. Read these before/while coding:

| Repo | Why it matters |
|---|---|
| **`Mursisru/MissileHoldCam`** | **The closest existing mod**: hold a key → camera follows your launched missile → release restores. Uses the exact `CameraStateManager` + `onRegisterMissile` + `GameManager.GetLocalAircraft` API you need. Your version differs: two keys to cycle forward/back through *multiple* in‑flight missiles, and a dedicated key (default `;`) to return to your aircraft. |
| **`AlEX-FRiT/My-NO-Mods`** | ThirdEyeMod (orbit cam), MouseAimMod — great `CameraStateManager` / camera‑state patch examples. |
| **`mkualquiera/MKModsNO`** | Missile voice warnings — shows `ThreatList`, `MissileWarning`, `InterfaceAudio` (RWR audio). |
| **`lunaboards-dev/Nuclear-Option-Extensions`** | Custom RWR display, MAW sound override — RWR audio internals. |
| **`9138noms/FrontlineMap`**, **`ChiefRatcliff/Radar-Data-Export-Mod`** | `UnitRegistry`, `DynamicMap`, radar/threat data. |

### Legend

- **[CONFIRMED]** — verified in real public mod source (repos above; and KopterBuzz **NOBlackBox**).
- **[VERIFY]** — re‑confirm the exact name against *your* installed game version in the decompiler.
  Modding is unofficial and any game update can rename things — that's why Part C exists.

> **Namespace note (confirmed):** the game's gameplay types (`Aircraft`, `Missile`, `Unit`,
> `CameraStateManager`, `GameManager`, `MissionManager`, `UnitRegistry`, `SceneSingleton<T>`,
> `GameState`, `Faction`, …) are all in the **global (root) namespace** — no `using` needed.
> Only `Player` / `BasePlayer` live in `NuclearOption.Networking`.

---

## Part A — Environment setup (Windows 11)

Do these in order; each ends with a verification so you never build on a broken step.

### 1. Install the .NET SDK

BepInEx 5 plugins target **.NET Framework 4.7.2 (`net472`)** on Unity's Mono runtime. You do **not**
need the old .NET Framework developer pack — NuGet reference assemblies let the modern CLI build it.

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e
```
Open a **new** terminal → `dotnet --version` → expect `8.0.x`+.

### 2. Install VS Code + C#

```powershell
winget install --id Microsoft.VisualStudioCode -e
code --install-extension ms-dotnettools.csharp
code --install-extension ms-dotnettools.csdevkit   # optional: project/solution tooling
```

### 3. Install a decompiler (you'll live in this to verify class names)

```powershell
winget install --id icsharpcode.ILSpy -e
```
> Alternatives: **dnSpyEx** (inline browsing/debugging), or **`Mursisru/NuclearOptionSDK`**
> (a community decompiler/studio tool built specifically for this game). ILSpy is enough here.

### 4. Install Git

```powershell
winget install --id Git.Git -e
```
Verify in a new terminal: `git --version`.

### 5. Find your game folder

Steam → right‑click Nuclear Option → **Manage → Browse local files.** That's `<GAME_DIR>`, usually:
```
C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option
```
Inside:
```
<GAME_DIR>\
├─ NuclearOption.exe
└─ NuclearOption_Data\Managed\   ← Assembly-CSharp.dll + all UnityEngine.*.dll + Mirage.dll, Rewired_Core.dll, UniTask.dll
```
📌 **Copy your exact `<GAME_DIR>` now** — you paste it into the `.csproj` and the build script.

### 6. Install BepInEx 5 via NOMM (you have NOMM)

1. Open NOMM; point it at `NuclearOption.exe` if asked.
2. Bottom‑left big button → **Install BepInEx.**
3. Same button → **launch the game once**, reach the main menu, then **quit** (this generates BepInEx's folders).

Verify:
```
<GAME_DIR>\BepInEx\
├─ core\          (BepInEx.dll, 0Harmony.dll)
├─ plugins\       ← your DLL goes here
├─ config\        (BepInEx.cfg)
└─ LogOutput.log  ← your logs
```

### 7. Configure BepInEx (console + a required setting)

Open `<GAME_DIR>\BepInEx\config\BepInEx.cfg` and set:

```ini
[Logging.Console]
Enabled = true

[Chainloader]
; Recommended by the NO modding community — prevents BepInEx's manager object
; from interfering with the game. (Verified guidance from AlEX-FRiT / NOBlackBox.)
HideManagerGameObject = true
```
Now a console window shows your plugin's log lines live. This is your primary feedback loop.

---

## Part B — Create the project

### 8. Scaffold

```powershell
mkdir C:\dev\MissileCamNO
cd C:\dev\MissileCamNO
dotnet new classlib -n MissileCamNO -o .
git init
code .
```
Delete the generated `Class1.cs`.

### 9. The `.csproj` (edit only the two `<GameDir>` lines)

Replace `MissileCamNO.csproj` entirely with this:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AssemblyName>MissileCamNO</AssemblyName>
    <Product>Missile Follow Cam</Product>
    <!-- Flows into DLL metadata AND BepInPlugin version. NOMNOM requires the manifest
         modVersion to MATCH this exactly. -->
    <Version>1.0.0</Version>

    <!-- EDIT THIS to your Part A/5 path. CI can override via the GAME_MANAGED env var. -->
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option</GameDir>
    <ManagedDir>$(GameDir)\NuclearOption_Data\Managed</ManagedDir>
    <ManagedDir Condition="'$(GAME_MANAGED)' != ''">$(GAME_MANAGED)</ManagedDir>

    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Core" Version="5.4.21" />
    <!-- Lets net472 build with the modern dotnet CLI (no dev pack needed). -->
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
    <!-- Auto-generates MyPluginInfo (GUID/NAME/VERSION) from csproj metadata. -->
    <PackageReference Include="BepInEx.PluginInfoProps" Version="2.1.0" PrivateAssets="all" />
    <!-- Publicizes private/internal game members so you can read them without reflection.
         (Harmless if members are already public.) -->
    <PackageReference Include="Krafs.Publicizer" Version="2.2.1" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <!-- Publicize the game's private/internal members so we can read them without reflection.
         IncludeCompilerGeneratedMembers="false" is REQUIRED: several game members are "field-like
         events" (e.g. Unit.onRegisterMissile / onDeregisterMissile). Each compiles to a public
         event PLUS a hidden compiler-generated backing field of the SAME name. Publicizing that
         backing field makes `unit.onRegisterMissile` ambiguous with the public event and the build
         fails with CS0229. Excluding compiler-generated members keeps the backing fields private;
         the events stay public, which is all the mod needs. -->
    <Publicize Include="Assembly-CSharp" IncludeCompilerGeneratedMembers="false" />
  </ItemGroup>

  <!-- Reference the game DLLs directly. Private=false => NEVER copy proprietary DLLs to output/repo. -->
  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ManagedDir)\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
      <Publicize>true</Publicize>
    </Reference>

    <!-- Some public members of Aircraft/Missile/Unit reference these assemblies in their
         signatures, so the compiler needs them referenced even if you don't call them directly. -->
    <Reference Include="Mirage">        <HintPath>$(ManagedDir)\Mirage.dll</HintPath>        <Private>false</Private></Reference>
    <Reference Include="Rewired_Core">  <HintPath>$(ManagedDir)\Rewired_Core.dll</HintPath>  <Private>false</Private></Reference>
    <Reference Include="UniTask">       <HintPath>$(ManagedDir)\UniTask.dll</HintPath>       <Private>false</Private></Reference>

    <Reference Include="UnityEngine">                   <HintPath>$(ManagedDir)\UnityEngine.dll</HintPath>                   <Private>false</Private></Reference>
    <Reference Include="UnityEngine.CoreModule">        <HintPath>$(ManagedDir)\UnityEngine.CoreModule.dll</HintPath>        <Private>false</Private></Reference>
    <Reference Include="UnityEngine.PhysicsModule">     <HintPath>$(ManagedDir)\UnityEngine.PhysicsModule.dll</HintPath>     <Private>false</Private></Reference>
    <Reference Include="UnityEngine.InputLegacyModule"> <HintPath>$(ManagedDir)\UnityEngine.InputLegacyModule.dll</HintPath> <Private>false</Private></Reference>
    <Reference Include="UnityEngine.AudioModule">       <HintPath>$(ManagedDir)\UnityEngine.AudioModule.dll</HintPath>       <Private>false</Private></Reference>
  </ItemGroup>

</Project>
```

### 10. Add BepInEx nuget feed

Create `nuget.config` next to the `.csproj` to add the official BepInEx feed:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <!-- BepInEx.Core and BepInEx.PluginInfoProps are NOT on nuget.org; they live here. -->
    <add key="BepInEx" value="https://nuget.bepinex.dev/v3/index.json" />
  </packageSources>
</configuration>
```

Restore:
```powershell
dotnet restore
```

### 11. `.gitignore`

```gitignore
bin/
obj/
.vscode/
# NEVER commit proprietary game assemblies to a public repo:
*Assembly-CSharp*.dll
UnityEngine*.dll
Mirage.dll
Rewired_Core.dll
UniTask.dll
lib/game/
Thumbs.db
.DS_Store
```

---

## Part C — Verify the game API (mandatory, ~20–30 min)

The APIs below are **[CONFIRMED]** from real mod source, but you must confirm they still exist in
*your* game version. Open ILSpy → **File → Open** →
`<GAME_DIR>\NuclearOption_Data\Managed\Assembly-CSharp.dll`, then verify each row (use search +
right‑click **Analyze**):

| What you need | Exact member to confirm | Status |
|---|---|---|
| Camera singleton | `SceneSingleton<CameraStateManager>.i` (equivalently `CameraStateManager.i`) | **[CONFIRMED]** |
| Follow a unit | `CameraStateManager.SetFollowingUnit(Unit)` | **[CONFIRMED]** |
| Switch camera state | `CameraStateManager.SwitchState(CameraBaseState)` | **[CONFIRMED]** |
| Orbit state field | `CameraStateManager.orbitState` (a `CameraOrbitState`) | **[CONFIRMED]** |
| Camera state used by map | `CameraSelectionState` (the map's "unit selected" camera) | **[CONFIRMED]** |
| Local aircraft | `GameManager.GetLocalAircraft(out Aircraft)` → `bool` | **[CONFIRMED]** |
| Is-local-aircraft check | `GameManager.IsLocalAircraft(Unit)` | **[CONFIRMED]** |
| Mission live | `MissionManager.IsRunning` (static) | **[CONFIRMED]** |
| Single vs multi | `GameManager.gameState` → `GameState.SinglePlayer` / `.Multiplayer` | **[CONFIRMED]** |
| Missile launched/expired events | `Unit.onRegisterMissile` / `Unit.onDeregisterMissile` (both `Action<Missile>`) | **[CONFIRMED]** |
| Missile owner | `Missile.owner` (`Unit`) and `Missile.ownerID` (`PersistentID`) | **[CONFIRMED]** |
| Missile alive flag | `Unit.disabled` (bool; true once detonated) | **[CONFIRMED]** |
| All units (fallback enum) | `UnitRegistry.allUnits` (iterable of `Unit`; contains missiles) | **[CONFIRMED]** |
| RWR alarm audio | `ThreatList` + inner `ThreatList.MissileAlarm.alarmSource` (2D HUD audio) | **[CONFIRMED]** |
| Cockpit camera state | `CameraStateManager.cockpitState` (a `CameraCockpitState`) | **[CONFIRMED]** |
| Follow-the-cockpit switch | `SetFollowingUnit(localAircraft)` + `SwitchState(cockpitState)` | **[CONFIRMED]** |

> **Return to the aircraft:** while following a missile, the game's native "Switch View" key
> (default **L**) only cycles camera angles of the *currently followed unit* — i.e. the missile —
> so it never gets you back to the plane. To return, the mod explicitly calls
> `SetFollowingUnit(localAircraft)` + `SwitchState(cockpitState)` on a dedicated configurable key
> (default `;`). Both `cockpitState` and `orbitState` are confirmed named fields on
> `CameraStateManager`.

If any **[CONFIRMED]** row is missing/renamed in your version, note the new name — you'll only ever
edit it in **one file** (`GameBridge.cs`, Part D).

---

## Part D — Write the mod

Two files. **All game‑specific calls are isolated in `GameBridge.cs`** so a future game update is a
one‑file fix.

### 11. `Plugin.cs` — entry point, config, input loop

```csharp
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace MissileCamNO
{
    // MyPluginInfo.* is auto-generated by BepInEx.PluginInfoProps from the .csproj.
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInProcess("NuclearOption.exe")]   // single-player exe only (our scope)
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log = null!;

        // Two cycle keys plus a return-to-aircraft key. All are configurable in the game's BepInEx
        // config menu. The game's native "Switch View" key (L) is left untouched, so it still cycles
        // through the followed missile's camera angles.
        private ConfigEntry<KeyboardShortcut> _cycleNext = null!;
        private ConfigEntry<KeyboardShortcut> _cyclePrev = null!;
        private ConfigEntry<KeyboardShortcut> _returnToAircraft = null!;

        private readonly MissileTracker _tracker = new MissileTracker();
        private int _cursor = -1;

        private void Awake()
        {
            Log = Logger;

            _cycleNext = Config.Bind("Controls", "CycleNextMissile",
                new KeyboardShortcut(KeyCode.RightBracket),   // default ]
                "Follow the NEXT of your in-flight missiles.");
            _cyclePrev = Config.Bind("Controls", "CyclePreviousMissile",
                new KeyboardShortcut(KeyCode.LeftBracket),    // default [
                "Follow the PREVIOUS of your in-flight missiles.");
            _returnToAircraft = Config.Bind("Controls", "ReturnToAircraft",
                new KeyboardShortcut(KeyCode.Semicolon),      // default ;
                "Return the camera to your aircraft's cockpit view.");

            Log.LogInfo($"{MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} loaded. " +
                        $"Next=[{_cycleNext.Value}] Prev=[{_cyclePrev.Value}] " +
                        $"Return=[{_returnToAircraft.Value}].");
        }

        private void Update()
        {
            // Single-player, live mission only. Otherwise stay out of the way.
            if (!GameBridge.InSinglePlayerMission())
            {
                _tracker.DetachIfNeeded(null);
                _cursor = -1;
                return;
            }

            // Keep the tracker subscribed to the CURRENT local aircraft (handles respawns/aircraft swaps).
            var aircraft = GameBridge.GetLocalAircraft();
            _tracker.EnsureAttached(aircraft);

            if (_cycleNext.Value.IsDown()) Cycle(+1);
            else if (_cyclePrev.Value.IsDown()) Cycle(-1);
            else if (_returnToAircraft.Value.IsDown()) ReturnToAircraft();

            // NOTE: we do NO per-frame camera override. We only switch on a keypress, using the
            // game's own follow path. The native "Switch View" key (L) is left free to cycle the
            // followed missile's camera angles, and we never touch the (aircraft-driven, 2D)
            // RWR/warning audio path.
        }

        private void Cycle(int direction)
        {
            var missiles = _tracker.LiveMissiles();   // pruned, in launch order
            if (missiles.Count == 0)
            {
                Log.LogInfo("No in-flight missiles of yours to follow.");
                _cursor = -1;
                return;
            }

            _cursor = ((_cursor + direction) % missiles.Count + missiles.Count) % missiles.Count;
            GameBridge.FollowUnit(missiles[_cursor]);
            Log.LogInfo($"Following missile {_cursor + 1}/{missiles.Count}. " +
                        $"Press [{_returnToAircraft.Value}] to return to your aircraft.");
        }

        private void ReturnToAircraft()
        {
            GameBridge.ReturnToAircraft();
            _cursor = -1;   // next cycle starts from the first missile again
            Log.LogInfo("Returned to your aircraft.");
        }
    }
}
```

### 12. `GameBridge.cs` — the single file that touches the game

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MissileCamNO
{
    /// <summary>All game-version-specific calls live here. Fix game updates in this file only.</summary>
    internal static class GameBridge
    {
        internal static bool InSinglePlayerMission()
        {
            // [CONFIRMED] MissionManager.IsRunning ; GameManager.gameState == GameState.SinglePlayer
            return MissionManager.IsRunning && GameManager.gameState == GameState.SinglePlayer;
        }

        internal static Aircraft? GetLocalAircraft()
        {
            // [CONFIRMED] GameManager.GetLocalAircraft(out Aircraft)
            return GameManager.GetLocalAircraft(out Aircraft ac) ? ac : null;
        }

        internal static void FollowUnit(Unit unit)
        {
            // [CONFIRMED] the exact API the map uses to follow a selected unit.
            var cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null || unit == null) return;
            cam.SetFollowingUnit(unit);        // point the camera at this missile
            cam.SwitchState(cam.orbitState);   // orbit/follow it, just like a map selection
            // While following the missile, the game's native "Switch View" key (L) still cycles
            // through the missile's camera angles. Returning to the aircraft is done explicitly
            // via ReturnToAircraft() below (bound to a configurable key), because the native key
            // only cycles views of whatever unit is currently followed.
        }

        internal static void ReturnToAircraft()
        {
            // Point the camera back at the local player's aircraft and snap to the cockpit view.
            // [CONFIRMED] CameraStateManager.cockpitState (CameraCockpitState) + SetFollowingUnit/SwitchState.
            var cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return;
            var aircraft = GetLocalAircraft();
            if (aircraft == null) return;
            cam.SetFollowingUnit(aircraft);      // follow our own aircraft again
            cam.SwitchState(cam.cockpitState);   // restore the normal cockpit view
        }
    }

    /// <summary>Tracks the local player's own in-flight missiles via the aircraft's launch events.</summary>
    internal sealed class MissileTracker
    {
        private Aircraft? _aircraft;
        private readonly List<Missile> _owned = new List<Missile>();

        internal void EnsureAttached(Aircraft? aircraft)
        {
            if (ReferenceEquals(aircraft, _aircraft)) return;
            Detach();
            _aircraft = aircraft;
            if (_aircraft == null) return;

            // [CONFIRMED] canonical "my missiles" API (MissileHoldCam pattern):
            _aircraft.onRegisterMissile += OnRegister;
            _aircraft.onDeregisterMissile += OnDeregister;

            // Seed with any missiles already in flight that this aircraft fired
            // (covers the case where we attach after a launch). [CONFIRMED] UnitRegistry.allUnits, Missile.owner
            foreach (var u in UnitRegistry.allUnits)
                if (u is Missile m && !m.disabled && ReferenceEquals(m.owner, _aircraft) && !_owned.Contains(m))
                    _owned.Add(m);
        }

        internal void DetachIfNeeded(Aircraft? aircraft)
        {
            if (!ReferenceEquals(aircraft, _aircraft)) Detach();
        }

        private void Detach()
        {
            if (_aircraft != null)
            {
                _aircraft.onRegisterMissile -= OnRegister;
                _aircraft.onDeregisterMissile -= OnDeregister;
            }
            _aircraft = null;
            _owned.Clear();
        }

        private void OnRegister(Missile m)   { if (m != null && !_owned.Contains(m)) _owned.Add(m); }
        private void OnDeregister(Missile m) { _owned.Remove(m); }

        /// <summary>Live, in-launch-order snapshot; prunes any that detonated/despawned.</summary>
        internal List<Missile> LiveMissiles()
        {
            _owned.RemoveAll(m => m == null || m.disabled);
            return _owned.ToList();
        }
    }
}
```

### 13. Behavior recap (what you built)

- `]` / `[` cycle forward/back through **your** in‑flight missiles and follow the selected one via
  the game's own camera path — identical to selecting it on the map.
- While following a missile, the game's native **L** ("Switch View") cycles through that missile's
  camera angles.
- `;` (configurable) snaps the camera back to **your aircraft's cockpit** via
  `SetFollowingUnit(localAircraft)` + `SwitchState(cockpitState)`.
- Your aircraft keeps flying under its current controls the whole time (nothing extra — matches
  "works exactly as it does when selecting on the map").
- **Incoming‑missile / RWR warnings keep sounding** while you watch a missile (see §14).
- Everything is gated on a live single‑player mission, so keys do nothing in menus.

### 14. Why the warnings keep playing (confirmed — no code needed)

The RWR/missile‑warning alarms are `AudioSource`s inside `ThreatList.MissileAlarm`, a **HUD (2D)**
component, and they're triggered by the **aircraft's** radar/missile‑warning sensors
(`aircraft.onRadarWarning`, `aircraft.GetMissileWarningSystem()` events) — **not** by the camera's
position. Moving the camera to a missile does not move or mute them. Since we never touch the audio
path, warnings continue exactly as they do when you open the map today. **You still verify this
in‑game (§16, step 5).**

---

## Part E — Local build + test automation

### 15. First build

```powershell
cd C:\dev\MissileCamNO
dotnet build -c Debug
```
Expect `bin\Debug\MissileCamNO.dll`. Fix any missing‑reference errors by re‑checking `<GameDir>`.

### 16. One‑command build → deploy → launch → tail logs

Create `build-and-launch.ps1` (edit `$GameDir` if needed):

```powershell
param([string]$Configuration = "Debug", [switch]$NoLaunch)
$ErrorActionPreference = "Stop"

$GameDir    = "C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option"
$PluginName = "MissileCamNO"
$PluginDir  = Join-Path $GameDir "BepInEx\plugins\$PluginName"
$LogFile    = Join-Path $GameDir "BepInEx\LogOutput.log"

Write-Host "==> Building ($Configuration)..." -ForegroundColor Cyan
dotnet build -c $Configuration

$dll = Join-Path $PSScriptRoot "bin\$Configuration\$PluginName.dll"
if (-not (Test-Path $dll)) { throw "Build output not found: $dll" }

Write-Host "==> Deploying to $PluginDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
Copy-Item $dll $PluginDir -Force

if ($NoLaunch) { Write-Host "==> Skipping launch (-NoLaunch)."; return }

Write-Host "==> Launching via Steam..." -ForegroundColor Cyan
Start-Process "steam://run/2168680"

Write-Host "==> Tailing BepInEx log (Ctrl+C to stop):" -ForegroundColor Cyan
Start-Sleep -Seconds 8
if (Test-Path $LogFile) {
    Get-Content $LogFile -Wait -Tail 20 | Where-Object { $_ -match "$PluginName|Error|Exception" }
} else { Write-Host "Log not found yet at $LogFile" -ForegroundColor Yellow }
```

Run:
```powershell
powershell -ExecutionPolicy Bypass -File .\build-and-launch.ps1
```

> Prefer auto‑deploy on every build (no script)? Add to the `.csproj`:
> ```xml
> <Target Name="DeployToGame" AfterTargets="Build">
>   <PropertyGroup><PluginsDir>$(GameDir)\BepInEx\plugins\$(AssemblyName)</PluginsDir></PropertyGroup>
>   <MakeDir Directories="$(PluginsDir)" />
>   <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(PluginsDir)" />
> </Target>
> ```

### 17. Test loop

1. Run the script → BepInEx console prints `MissileCamNO v1.0.0 loaded.`
2. Start a **Quick Mission / Instant Action**, take off, fire a missile.
3. Press `]` → camera follows your missile (like a map selection).
4. Press `]` / `[` to cycle when several are airborne.
5. Provoke your RWR (fly near a threat that locks you) → confirm you **still hear the warning** while following.
6. While following a missile, press **L** → confirm it cycles the missile's camera angles.
7. Press `;` → confirm the camera snaps back to your aircraft's cockpit.
8. Watch the console for exceptions — every game‑call issue points back to `GameBridge.cs`.

Edit → re‑run the script to iterate. No need to reinstall BepInEx between runs.

---

## Part F — CI: automated release pipeline (GitHub Actions)

**Constraint:** CI must never use or commit the proprietary `Assembly-CSharp.dll`. Two clean options:

- **Option 1 (recommended): self‑hosted runner on your PC** — it already has the game, so
  `dotnet build` finds the DLLs locally; nothing proprietary leaves your machine or enters the repo.
- **Option 2: hosted runner + private reference DLLs** pulled at build time via a secret token.

### 18. Create the public repo

NOMNOM requires a **public** repo with releases for **exactly one mod**, a parseable tag (`v1.0.0`),
and the download as the release's **first asset**.

```powershell
cd C:\dev\MissileCamNO
git add .
git commit -m "Initial commit: Missile Follow Cam"
# Create an EMPTY public repo 'MissileCamNO' on GitHub, then:
git remote add origin https://github.com/<YOUR_GITHUB_USERNAME>/MissileCamNO.git
git branch -M main
git push -u origin main
```

### 19a. Option 1 — self‑hosted runner (recommended)

1. GitHub → repo **Settings → Actions → Runners → New self‑hosted runner (Windows)**; run the shown install/start commands on your PC.
2. Add `.github/workflows/release.yml`:

```yaml
name: Release
on:
  push:
    tags: [ "v*" ]
jobs:
  build-and-release:
    runs-on: [ self-hosted, windows ]   # your PC; game DLLs are local
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
      - name: Build (Release)
        run: dotnet build -c Release
      - name: Package
        shell: pwsh
        run: |
          $ver = "${{ github.ref_name }}".TrimStart('v')
          $out = "MissileCamNO-$ver"
          New-Item -ItemType Directory -Force -Path dist/$out | Out-Null
          Copy-Item "bin/Release/MissileCamNO.dll" "dist/$out/"
          Copy-Item "README.md" "dist/$out/" -ErrorAction SilentlyContinue
          Compress-Archive -Path "dist/$out/*" -DestinationPath "dist/$out.zip" -Force
          $hash = (Get-FileHash "dist/$out.zip" -Algorithm SHA256).Hash.ToLower()
          "sha256:$hash" | Tee-Object "dist/$out.zip.sha256.txt"
          "ZIP=dist/$out.zip"               | Out-File -Append $env:GITHUB_ENV
          "SHA=dist/$out.zip.sha256.txt"    | Out-File -Append $env:GITHUB_ENV
      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: |
            ${{ env.ZIP }}
            ${{ env.SHA }}
          generate_release_notes: true
```

### 19b. Option 2 — hosted runner + private reference DLLs

1. Create a **private** repo (e.g. `NuclearOption-Refs`) containing just the DLLs your `.csproj`
   references (from `NuclearOption_Data\Managed`). Private = no public redistribution.
2. Create a fine‑grained PAT with read access to it; add it to the mod repo as secret `GAME_REFS_TOKEN`.
3. Workflow (differences from Option 1):

```yaml
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Fetch private game reference DLLs
        uses: actions/checkout@v4
        with:
          repository: <YOUR_GITHUB_USERNAME>/NuclearOption-Refs
          token: ${{ secrets.GAME_REFS_TOKEN }}
          path: gamerefs
      - name: Point build at reference DLLs
        shell: pwsh
        run: "GAME_MANAGED=${{ github.workspace }}\\gamerefs" | Out-File -Append $env:GITHUB_ENV
      - name: Build (Release)
        run: dotnet build -c Release
      # ... same Package + Create GitHub Release steps as Option 1 ...
```
(The `.csproj` already honors `GAME_MANAGED` via the `<ManagedDir Condition="'$(GAME_MANAGED)' != ''">` line.)

### 20. Cut a release

```powershell
git tag v1.0.0
git push origin v1.0.0
```
The workflow builds, zips `MissileCamNO-1.0.0.zip`, writes the `sha256:` file, and creates the
Release. **The DLL version must equal the tag** — NOMNOM enforces manifest `modVersion` == DLL
metadata, which you set via `<Version>` in the `.csproj`.

---

## Part G — Publish to NOMM (via NOMNOM)

NOMM lists mods from **NOMNOM**. Register once with `autoUpdate:"True"` and NOMNOM auto‑detects your
**future** releases.

### 21. Requirements checklist
- ✅ Public repo, releases for **only this mod**.
- ✅ One downloadable asset per release (a `.zip`; NOMNOM pulls the **first** asset).
- ✅ Parseable tag (`v1.0.0`/`1.0.0`).
- ✅ Works with **BepInEx 5** (you target exactly that).

### 22. Get the release SHA256
On the GitHub release, use the asset's **copy digest** button, or read your `*.sha256.txt` asset.
Format: `sha256:<64 hex>`.

### 23. Add the manifest to NOMNOM
Fork **https://github.com/KopterBuzz/NOMNOM**. In `modManifests/`, add `MissileCamNO.json`
(filename should match the `id`, which should equal your plugin DLL's AssemblyName):

```json
{
  "id": "MissileCamNO",
  "name": "MissileCamNO",
  "description": "Cycle through and follow your own in-flight missiles while flying. RWR/missile warnings keep playing; press ; (configurable) to snap the camera back to your aircraft's cockpit.",
  "tags": ["QoL"],
  "urls": [
    { "name": "info", "url": "https://github.com/<YOUR_GITHUB_USERNAME>/MissileCamNO" }
  ],
  "authors": ["<YOUR_GITHUB_USERNAME>"],
  "githubUser": "<YOUR_GITHUB_USERNAME>",
  "githubRepo": "MissileCamNO",
  "autoUpdate": "True",
  "artifacts": [
    {
      "type": "plugin",
      "fileName": "MissileCamNO-1.0.0.zip",
      "downloadUrl": "https://github.com/<YOUR_GITHUB_USERNAME>/MissileCamNO/releases/download/v1.0.0/MissileCamNO-1.0.0.zip",
      "gameVersion": "0.32",
      "modVersion": "1.0.0",
      "releaseChannel": "Release",
      "hash": "sha256:<PASTE_YOUR_DIGEST_HERE>"
    }
  ]
}
```
Field notes (from NOMNOM `SCHEMA.md`): `id` REQUIRED (≈ AssemblyName; filename matches it);
`githubUser`/`githubRepo`/`autoUpdate` REQUIRED for auto‑tracking; `type` = `plugin`;
`gameVersion` = the NO version you tested (main‑menu build number); `modVersion` **must match** the
DLL metadata; `hash` = full `sha256:` digest.

### 24. Submit
1. Commit the JSON on your fork and open a **Pull Request to NOMNOM's `main`.**
2. NOMNOM's CI validates schema/content.
3. A maintainer merges. Afterward your mod appears in NOMM, and future tagged releases are picked up
   automatically (because `autoUpdate` is `True`). *(Alternatively, open a NOMNOM Issue requesting a
   new mod — the PR route is the documented, faster one.)*

---

## Part H — Troubleshooting

| Symptom | Fix |
|---|---|
| `NU1101: Unable to find package BepInEx.Core` / `BepInEx.PluginInfoProps` | Missing BepInEx feed. Add the `nuget.config` from Part 9, then re‑run `dotnet restore`. |
| No `MissileCamNO ... loaded.` in console | DLL not in `BepInEx\plugins`; or BepInEx not installed (re‑run NOMM install + launch once). |
| `dotnet build` can't find `Assembly-CSharp` | Wrong `<GameDir>`/`<ManagedDir>` in `.csproj`. |
| Compile error: "type from assembly 'Mirage'/'Rewired_Core'/'UniTask' not referenced" | Those `<Reference>` lines are missing/paths wrong — they're in the provided `.csproj`. |
| Build errors on `internal`/`private` members | Ensure `<Publicize Include="Assembly-CSharp" />` and `<Publicize>true</Publicize>` are present. |
| Camera won't switch | Re‑verify `SetFollowingUnit`/`SwitchState`/`orbitState`/`cockpitState` names in ILSpy (Part C) and update `GameBridge.cs`. |
| `;` doesn't return to the aircraft | Re‑verify `cockpitState` in ILSpy; make sure the `ReturnToAircraft` bind isn't shared with another control. |
| Cycles include allied AI missiles | Shouldn't happen — the tracker uses your aircraft's own launch events + `Missile.owner`. If it does, tighten `EnsureAttached`'s seed to `m.owner == _aircraft` (already done). |
| Warnings go silent while following | You'd only see this if you moved the main AudioListener — this mod doesn't. Re‑verify you're on the game's follow path (§14). |
| CI fails on `Assembly-CSharp` | Hosted runner has no game DLLs — use self‑hosted (19a) or private refs (19b). |
| NOMNOM PR validation fails | `modVersion` ≠ DLL version, bad `hash`, non‑parseable tag, or a missing required field. |

---

## Part I — Appendix: verified API this mod uses

**[CONFIRMED] (from `Mursisru/MissileHoldCam`, `AlEX-FRiT/My-NO-Mods`, `mkualquiera/MKModsNO`,
`lunaboards-dev/Nuclear-Option-Extensions`, `9138noms/FrontlineMap`, KopterBuzz `NOBlackBox`):**

- Namespaces: gameplay types are **global**; `Player`/`BasePlayer` in `NuclearOption.Networking`.
- Camera: `SceneSingleton<CameraStateManager>.i`; `SetFollowingUnit(Unit)`; `SwitchState(CameraBaseState)`;
  fields `orbitState` (`CameraOrbitState`) and `cockpitState` (`CameraCockpitState`); `mainCamera`;
  `cameraMode` (`CameraMode`: `cockpit`, `orbit`);
  states `CameraCockpitState`/`CameraChaseState`/`CameraOrbitState`/`CameraSelectionState`/… .
- Player/aircraft: `GameManager.GetLocalAircraft(out Aircraft)`; `GameManager.IsLocalAircraft(Unit)`;
  `GameManager.gameState` (`GameState.SinglePlayer`/`.Multiplayer`); `GameManager.flightControlsEnabled`.
- Mission: `MissionManager.IsRunning`.
- Missiles: `Aircraft.onRegisterMissile` / `onDeregisterMissile` (`Action<Missile>`); `Missile : Unit`;
  `Missile.owner` (`Unit`), `Missile.ownerID` (`PersistentID`), `Missile.disabled`, `Missile.seekerMode`.
- Units: `UnitRegistry.allUnits`; `Unit.NetworkHQ.faction` (`Faction.factionName`); `Unit.persistentID.Id`.
- RWR audio (why warnings persist): `ThreatList` + inner `ThreatList.MissileAlarm.alarmSource` (2D HUD);
  driven by `aircraft.onRadarWarning` and `aircraft.GetMissileWarningSystem()` events.
- BepInEx 5 mono: `[BepInPlugin]`, `[BepInProcess("NuclearOption.exe")]`; config keybinds via
  `ConfigEntry<KeyboardShortcut>` + `.Value.IsDown()`; `HideManagerGameObject = true` in `BepInEx.cfg`.

**[VERIFY] against your game version:** all of the above still exist under these names; the
`cockpitState` field on `CameraStateManager` (used by the return-to-aircraft key).

---

*Written against real, public Nuclear Option mod source and the NOMM/NOMNOM docs. Because modding is
unofficial, always re‑verify class names for your installed game version (Part C) before shipping.
See the companion doc `NuclearOption-MissileCam-Autopilot-Companion.md` for the optional
autopilot/level‑hold feature.*
