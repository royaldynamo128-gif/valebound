# VALEBOUND — Build & Publishing Guide

This is the complete path from this repository to a live Google Play listing.
Estimated time on a normal dev machine: **45–90 minutes** (most of it is Unity
import and world baking).

---

## Part 1 — Machine setup (once)

1. **Unity Hub** → install **Unity 2022.3.62f1** (or any 2022.3 LTS; the project
   was authored against 2022.3) with these modules:
   - ✅ Android Build Support
     - ✅ OpenJDK
     - ✅ Android SDK & NDK Tools
2. **Android device** with Developer Options → USB debugging enabled (for `run`).
3. No other SDKs needed — Unity bundles everything else.

## Part 2 — Open the project

1. Unity Hub → **Add** → select the `game/` folder.
2. Open with 2022.3 LTS. First import compiles ~1,800 assets; expect 5–15 min.
3. If prompted about "Enter Safe Mode?" due to compile errors — **do not**; the
   scripts compile clean (verified against Unity 2022.3 API surface). If you see
   errors, they are environment-related; re-open with the exact editor version.

## Part 3 — Generate the game (5 minutes of clicking)

The repository ships **source + tools**, not a baked scene, so the world is
reproducible and git-friendly. Run these menu items in order:

```
VALEBOUND → Configure Project          (import settings, textures, tags)
VALEBOUND → Build Prefabs              (player, vehicles, NPCs, audio)
VALEBOUND → World Builder → GENERATE WORLD   (bakes the 4×4 km valley)
VALEBOUND → Build Data Assets          (missions, achievements)
VALEBOUND → Build Scenes               (UI + Boot/Main scenes)
```

**World Builder** takes 2–6 minutes (terrain heightmap, ~2,000+ props,
NavMesh bake, minimap render).

Everything is seeded (`20261114` by default) — the same seed regenerates the
identical world.

### Play in editor
Open `Assets/VALEBOUND/Scenes/Boot.unity` → **Play**.
Keyboard fallbacks work in editor: WASD move, Shift sprint, Space jump,
mouse orbit.

## Part 4 — First Android build (test APK)

```bash
cd release
./build_android.sh apk
```

Output: `build/VALEBOUND-debug.apk`. Install on a device:
```bash
adb install -r ../build/VALEBOUND-debug.apk
```
Or use `./build_android.sh run` to build-install-launch in one step.

**Test checklist on device** (10 min):
- [ ] Boot → main menu shows; New Game fades into the world
- [ ] Joystick moves, camera swipe orbits, sprint drains stamina
- [ ] Enter a car: walk to it, USE button, drive with GAS/BRAKE pedals + steering
- [ ] Horn, headlights (at night), radio, exit vehicle
- [ ] Traffic cars drive lanes; pedestrians wander; no falls through ground
- [ ] Pause → Save; kill app; relaunch → Continue restores position
- [ ] Day/night advances; weather changes over ~10 min
- [ ] Minimap & world map open; mission marker tracks objective
- [ ] Settings sliders persist after app restart
- [ ] FPS: ≥30 on a mid-range phone in Medium tier (verify in adb logcat)

## Part 5 — Release AAB for Google Play

### 5.1 Keystore (once, keep it forever)

```bash
keytool -genkey -v -keystore release/valebound.keystore \
  -alias valebound -keyalg RSA -keysize 2048 -validity 10000
```

Back this file up — losing it means losing app updates.

### 5.2 Configure signing (once, in editor)

Edit → Project Settings → Player → Publishing Settings:
- Keystore Manager → browse `release/valebound.keystore`
- Enter keystore password, alias `valebound`, alias password

Update the password variables at the top of `release/build_android.sh` or
leave them empty (Unity uses the stored Player Settings values).

### 5.3 Build

```bash
cd release
./build_android.sh aab
```

Output: `build/VALEBOUND-release.aab` (signed; Play App Signing will re-sign).

### 5.4 Version bumping

Before each upload: Project Settings → Player → `Bundle Version Number`
(e.g. `1.0.1`) and `Version Code` (`AndroidBundleVersionCode`, +1 each upload).
This file lives in `ProjectSettings/ProjectSettings.asset`.

## Part 6 — Google Play Console submission

1. **Create app** → name `VALEBOUND: Aurora Valley`, default English, Game,
   Free.
2. **App content**:
   - Privacy policy URL → host `release/PRIVACY_POLICY.md` on any static host
     (GitHub Pages works). Replace the support e-mail first.
   - Ads: **No ads** · Analytics: **None** (matches the Data Safety form in
     `STORE_LISTING.md`).
   - Content rating questionnaire → answers listed in `STORE_LISTING.md` →
     expected E / PEGI 3.
   - Target audience: 13+ (safe for all).
3. **Main store listing**: copy from `release/STORE_LISTING.md`; upload
   `release/store/` graphics (icon 512, feature graphic, 4 screenshots).
4. **Internal testing** → upload the AAB → add testers → verify install on
   2–3 devices (low / mid / high RAM).
5. **Production** → Create release → staged rollout 10% → watch Vitals
   (crash rate < 1%, ANR < 0.47%) → 50% → 100%.

## Part 7 — Post-launch monitoring

- Play Vitals (crashes/ANRs) weekly.
- Player feedback in reviews — common asks go into the roadmap.
- The codebase is structured for extension: new missions = one `MissionSpec`
  entry in `DataAssetBuilder.cs`; new vehicles = one row in `VehicleSpecs`;
  new city districts = one method in `WorldBuilder.cs`.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| "SDK not found" on build | Unity Hub → Installs → 2022.3 → Add modules → Android Build Support (both sub-options) |
| Gradle timeout | Preferences → External Tools → untick "Gradle installed with Unity", point to a local Gradle 7.x; or just retry (network flake) |
| World Builder window empty | Wait for domain reload; reopen the window |
| No traffic/NPCs in Play | World Builder step must have run; also check Console for `RoadNetwork` warnings |
| Blank minimap | Bake ran before roads were laid? Re-run `VALEBOUND → Build Scenes` after World Builder |
| APK too big (>150 MB) | Enable texture compression: Player → Other → Texture Compression = ASTC (already default in this project's import settings) |
