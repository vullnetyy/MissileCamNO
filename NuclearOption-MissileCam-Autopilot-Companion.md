# Companion: Optional Autopilot / Level‑Hold While in Missile Cam

> **Read the main guide first** (`NuclearOption-MissileCam-Mod-Guide.md`). This document is an
> **optional add‑on**: while you're watching an in‑flight missile, engage a "level‑hold" so your
> aircraft flies straight and level (or holds altitude/heading) instead of drifting, then release it
> when you return to the cockpit.
>
> **Honesty up front (important):** In the public Nuclear Option mod source I based the main guide
> on, there is **no confirmed, ready‑made "autopilot" API** you can just call. So this feature is
> **more research‑heavy** than the main mod. What the game *does* expose (confirmed) gives us solid
> hooks to build a level‑hold ourselves:
>
> | Member | Status | Why it matters here |
> |---|---|---|
> | `Aircraft.flightAssist` | **[CONFIRMED]** exists | The game's built‑in stability assist; inspect it — it may already expose a hold/assist flag we can toggle. |
> | `Aircraft.GetControlsFilter()` | **[CONFIRMED]** exists | Most likely the point where control inputs are collected/filtered — the ideal place to inject "level‑hold" inputs. |
> | `Aircraft.GetAircraftParameters()` | **[CONFIRMED]** exists | Flight parameters (limits, rates) to tune your controller. |
> | `Aircraft.rb` / `Aircraft.CockpitRB()` | **[CONFIRMED]** exist | The `Rigidbody` — read velocity/orientation for the control law. |
> | `Aircraft.radarAlt`, `.speed`, `.airDensity` | **[CONFIRMED]** exist | Feedback signals for altitude/speed hold. |
> | `GameManager.playerInput` (Rewired `IPlayer`) | **[CONFIRMED]** exists | Where player axes (pitch/roll/yaw/throttle) originate; relevant to overriding input. |
>
> Everything tagged **[RESEARCH]** below is something you must pin down in ILSpy for your game
> version — the doc tells you exactly what to look for.

---

## 1. Design choices to make first

Decide these before coding (they change the control law):

1. **Hold mode.** Simplest → most useful:
   - **Wings‑level + pitch‑hold** (freeze current pitch attitude, roll to 0°). Easiest, robust.
   - **Altitude‑hold** (maintain the altitude you had at engage). Adds an outer loop.
   - **Heading‑hold** (maintain heading). Optional third loop.
   Recommended first version: **wings‑level + altitude‑hold**, throttle left as the player set it.
2. **How inputs reach the aircraft.** Two implementation strategies (pick after §3):
   - **Strategy A — write into the controls filter** each physics tick (cleanest if the filter
     exposes settable pitch/roll/yaw fields).
   - **Strategy B — Harmony‑patch** the method that reads player input, substituting your values
     while the hold is engaged (use if inputs aren't directly settable).
3. **Config.** Add a toggle (default **off**) so users opt in.

---

## 2. Add the config toggle (in `Plugin.cs`)

In `Awake()` of the main mod, add:

```csharp
private ConfigEntry<bool> _autoLevelHold = null!;
// ...
_autoLevelHold = Config.Bind("Autopilot", "LevelHoldWhileInMissileCam", false,
    "When true, engage a wings-level/altitude hold while you're following a missile, " +
    "and release it when you return to the cockpit. EXPERIMENTAL.");
```

You'll read `_autoLevelHold.Value` when engaging (below).

---

## 3. Investigate the flight‑control pipeline in ILSpy  **[RESEARCH]**

Open `Assembly-CSharp.dll` (as in the main guide, Part C) and answer these:

1. **Inspect `flightAssist` first.** Find the type of `Aircraft.flightAssist`. Open it. Look for a
   public method/flag like `SetEnabled(bool)`, `hold`, `stabilize`, `level`, or an assist "mode."
   *If it already offers a hold you can toggle, you may be done — jump to §5 and just toggle it.*
2. **Find the control‑input struct.** Look at `Aircraft.GetControlsFilter()`'s return type (call it
   `ControlsFilter`). Inspect its fields — you're hoping to find settable inputs such as `pitch`,
   `roll`, `yaw`, `throttle`, `pitchInput`, `rollInput` (float, typically −1..1).
3. **Find where inputs are applied.** In `Aircraft`, find the physics update (`FixedUpdate` or a
   method it calls) that reads those inputs and applies forces/torques. Right‑click a control‑input
   field → **Analyze → Used By** to locate it. This tells you:
   - whether writing the field each `FixedUpdate` (Strategy A) will "stick," or
   - whether inputs are re‑read from `GameManager.playerInput` every tick and you must patch that
     read (Strategy B).
4. **Confirm Rewired axis names** (only needed for Strategy B). `GameManager.playerInput` is a
   Rewired `IPlayer`; find the exact action names the aircraft reads (e.g. `"Pitch"`, `"Roll"`,
   `"Yaw"`, `"Throttle"` — confirmed sibling names include `"Pan View"`, `"Switch View"`).

Write down the concrete type/field/method names you find; you'll plug them into §4.

---

## 4. Implement the level‑hold controller

Create `LevelHold.cs`. This is a self‑contained PD controller that computes normalized pitch/roll
(and optional throttle) commands from the `Rigidbody` state, then hands them to the aircraft via the
strategy you chose in §3.

```csharp
using UnityEngine;

namespace MissileFollowCam
{
    /// <summary>Experimental wings-level + altitude hold. All game-specific writes are in ApplyInputs().</summary>
    internal sealed class LevelHold
    {
        internal bool Engaged { get; private set; }

        private Aircraft? _ac;
        private float _targetAltitude;   // metres, captured at engage (from radarAlt or world Y)
        private float _targetSpeed;      // optional: hold speed at engage

        internal void Engage(Aircraft aircraft)
        {
            _ac = aircraft;
            if (_ac == null) { Engaged = false; return; }
            _targetAltitude = _ac.radarAlt;   // [CONFIRMED] field exists; swap for world Y if you prefer
            _targetSpeed    = _ac.speed;      // [CONFIRMED]
            Engaged = true;
            Plugin.Log.LogInfo("Level-hold ENGAGED.");
        }

        internal void Disengage()
        {
            if (!Engaged) return;
            Engaged = false;
            // Zero any injected inputs so the player regains clean control (see ApplyInputs).
            ApplyInputs(0f, 0f, 0f, releaseControl: true);
            Plugin.Log.LogInfo("Level-hold DISENGAGED.");
        }

        /// <summary>Call every FixedUpdate while engaged.</summary>
        internal void Tick()
        {
            if (!Engaged || _ac == null) return;

            Rigidbody rb = _ac.rb;                 // [CONFIRMED] Aircraft.rb
            Transform t  = _ac.transform;

            // --- Attitude feedback ---
            // Roll error: how far right-wing-down we are (want 0). Using world up projected to body.
            float rollDeg  = Vector3.SignedAngle(t.right, Vector3.ProjectOnPlane(t.right, Vector3.up), t.forward);
            // Pitch error toward altitude hold: climb rate + altitude error → desired pitch.
            float vSpeed   = rb.velocity.y;                            // m/s vertical
            float altErr   = _targetAltitude - _ac.radarAlt;          // +ve = need to climb

            // --- Simple PD laws (TUNE these gains in-game; start small) ---
            const float kRoll = 0.02f, kRollRate = 0.004f;
            const float kAlt  = 0.01f, kVspeed   = 0.03f;

            float rollRate  = rb.angularVelocity.z;   // body roll rate (verify axis in-game)
            float rollCmd   = Mathf.Clamp(-(kRoll * rollDeg + kRollRate * rollRate), -1f, 1f);
            float pitchCmd  = Mathf.Clamp(kAlt * altErr - kVspeed * vSpeed, -1f, 1f);
            float yawCmd    = 0f;   // coordinate turn optional; leave 0 for a basic hold

            ApplyInputs(pitchCmd, rollCmd, yawCmd, releaseControl: false);
        }

        // ============================================================================
        // [RESEARCH] The ONLY game-specific part. Fill using what you found in §3.
        //
        // Strategy A (controls filter exposes settable inputs):
        //   var f = _ac!.GetControlsFilter();
        //   if (releaseControl) { /* let the filter read player input again if it has such a flag */ return; }
        //   f.pitch = pitch; f.roll = roll; f.yaw = yaw;     // <-- real field names from §3.2
        //
        // Strategy B (inputs re-read each tick from Rewired): do NOT write here — instead
        //   Harmony-patch the input-read method (see §6) and have the patch pull these values
        //   from a shared static (e.g. LevelHoldState.Pitch/Roll/Yaw) when Engaged.
        // ============================================================================
        private void ApplyInputs(float pitch, float roll, float yaw, bool releaseControl)
        {
            // TODO: implement per the strategy you confirmed in §3.
            throw new System.NotImplementedException(
                "Wire LevelHold.ApplyInputs to the real control-input API (see §3).");
        }
    }
}
```

> The controller intentionally depends only on **[CONFIRMED]** members (`aircraft.rb`, `radarAlt`,
> `speed`, `transform`). The only unknown is *how you deliver* `pitch/roll/yaw` — that's the single
> `ApplyInputs` method you fill from §3.

---

## 5. Wire engage/disengage into the main mod

In `Plugin.cs`:

```csharp
private readonly LevelHold _hold = new LevelHold();

// In Cycle(...), right after GameBridge.FollowUnit(missiles[_cursor]);
if (_autoLevelHold.Value)
{
    var ac = GameBridge.GetLocalAircraft();
    if (ac != null) _hold.Engage(ac);
}

// Add a FixedUpdate to run the controller on the physics clock:
private void FixedUpdate()
{
    if (_hold.Engaged) _hold.Tick();
}

// In Update(), disengage as soon as we're back in the cockpit view:
if (_hold.Engaged && GameBridge.InCockpitView())
    _hold.Disengage();
```

Add the cockpit‑view check to `GameBridge` (relies on the confirmed camera enum):

```csharp
internal static bool InCockpitView()
{
    // [CONFIRMED] CameraStateManager.cameraMode is a CameraMode enum with value 'cockpit'.
    return CameraStateManager.cameraMode == CameraMode.cockpit;
}
```

Now: pressing `]`/`[` follows a missile **and** engages the hold (if enabled); pressing the native
**L** returns you to the cockpit, `InCockpitView()` flips true, and the hold releases automatically.

---

## 6. Strategy B detail (only if inputs are re‑read each tick)

If §3.3 showed the aircraft reads `GameManager.playerInput` every physics tick, writing a filter
field won't stick. Instead, Harmony‑patch the read and substitute your values while engaged:

```csharp
using HarmonyLib;

internal static class LevelHoldState   // shared bus between the controller and the patch
{
    internal static volatile bool Active;
    internal static float Pitch, Roll, Yaw;
}

// [RESEARCH] Replace TYPE/METHOD with the real input-read method you found in §3.3.
// Postfix rewrites the just-read inputs when the hold is active.
[HarmonyPatch(typeof(/* e.g. Aircraft */), "/* e.g. ReadControlInputs */")]
internal static class Patch_ControlInputs
{
    static void Postfix(/* ref float pitch, ref float roll, ref float yaw  — match the real signature */)
    {
        // if (!LevelHoldState.Active) return;
        // pitch = LevelHoldState.Pitch; roll = LevelHoldState.Roll; yaw = LevelHoldState.Yaw;
    }
}
```

Then in `Plugin.Awake()` enable Harmony:
```csharp
private HarmonyLib.Harmony _harmony = null!;
// in Awake():
_harmony = new HarmonyLib.Harmony(MyPluginInfo.PLUGIN_GUID);
_harmony.PatchAll();
```
And in `LevelHold.ApplyInputs`, instead of writing a filter, set `LevelHoldState.Pitch/Roll/Yaw`
and `LevelHoldState.Active = !releaseControl;`.

---

## 7. Testing & tuning

1. Enable `[Autopilot] LevelHoldWhileInMissileCam = true` in
   `<GAME_DIR>\BepInEx\config\<your plugin>.cfg` (generated after first run).
2. Fly straight, fire a missile, press `]`. The aircraft should hold roughly level while you watch.
3. **Tune gains** (`kRoll`, `kAlt`, …) from small values upward. If it oscillates, lower the
   proportional gain or raise the rate (derivative) gain. Verify the **roll/pitch axis signs** match
   your aircraft — if it diverges immediately, flip a sign.
4. Press **L**: confirm the hold releases and you have full manual control instantly.
5. Test across a couple of different airframes — flight models differ, so gains may need per‑type
   scaling via `GetAircraftParameters()`.

---

## 8. Safety notes

- Keep the feature **off by default** (done via the config).
- Always release cleanly on return to cockpit (`Disengage()` zeroes inputs).
- A PD hold is **not** terrain‑aware — it holds attitude/altitude, not obstacle avoidance. Document
  that for users.
- Because this overrides flight inputs, re‑verify it after every game update (flight‑model changes
  can shift the right gains or the input pipeline).

---

## 9. If you'd rather not build a controller

If §3.1 reveals that `Aircraft.flightAssist` already has a toggle that flies straight/holds attitude,
the entire feature collapses to:

```csharp
// Pseudocode — use the real member you find on flightAssist:
// on engage:   _ac.flightAssist.SetHold(true);
// on disengage:_ac.flightAssist.SetHold(false);
```
That's the ideal outcome — check `flightAssist` before writing the PD controller.

---

*This companion is intentionally discovery‑driven: the control‑input pipeline is the one part not
verifiable from public mod source, so §3 has you confirm it in the decompiler. Everything the
controller reads (`rb`, `radarAlt`, `speed`, `transform`, `CameraMode.cockpit`) is confirmed from
real mods. Build the main mod first; add this only once that works.*
