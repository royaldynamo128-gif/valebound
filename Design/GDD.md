# VALEBOUND — Game Design Document (v1.1)

> **v1.1.2 changelog (second bugfix pass):** world map screen now shows the baked
> map with POI pins and a working fast-travel panel (was a blank white panel);
> mission board / garage / achievements lists now have row prefabs and populate
> (was empty due to missing prefab references); duplicate "PAUSED"/"VALEBOUND"
> ghost titles removed; daily-challenges panel resized to its corner so it no
> longer collides with the menu; modal screens no longer stack on top of each
> other; currency shown as "cr" (TMP default font lacks the ₡ glyph); world
> map/minimap sized to the real 6144 m world; **build pipeline now detects
> stale generated scenes and regenerates automatically** (PipelineVersion
> marker) — running "Build APK" alone will refresh an outdated world.
>
> **v1.1.1 changelog (bugfix pass):** fixed world-generation pipeline — city/road
> asset paths now resolve (roads, streetlights, parked cars and terrain splats
> actually appear); city pads flatten to ground level so settlements sit on the
> terrain instead of under it; day/night lighting initializes correctly (no more
> black worlds at 08:39); player gets a real AnimatorController (idle/walk/run/
> jump/fall blend); HUD cleaned up (no speedometer on foot, bars clear of the
> joystick, minimap/objective stack separated from buttons, MAP button on-screen);
> GameManager's player/UI prefab references are wired at build time.

**Genre:** 3D open-world action-adventure (mobile)
**Setting:** Aurora Valley — a contemporary valley region of seven settlements across four climate biomes, highways, forests and lakes
**Platform:** Android (URP, touch-first), 64-bit, min API 24
**Session shape:** 5–15 minute play loops; mission checkpoints; autosave; daily challenges

---

## 1. High Concept

You are **Rin Ashford**, a 24-year-old courier who returns to Aurora Valley after six years
away, taking over your grandfather's delivery round when he vanishes. The valley is on the
edge of transformation: a conglomerate called **Meridian Dynamics** is buying up land for a
giant automated logistics hub — and your grandfather's "Bounders" courier guild is the last
independent delivery network standing between Meridian and a total monopoly. You drive, run,
explore and uncover why he disappeared, one parcel at a time.

The core fantasy is **freedom + momentum**: an open valley where every road leads somewhere,
vehicles feel great on a touchscreen, and the story unfolds through the people and places
you deliver to — not through cutscene walls.

## 2. The World — Aurora Valley

Approx. **6 km × 6 km** continuous world (36 km² of streaming world cells) built from
modular districts on a 2 m street grid, spanning **four climate biomes**:

| District | Character | Key locations |
|---|---|---|
| **PORT MERIDIAN** (main metro) | Downtown skyscrapers, plaza, promenade | Bounders Guild HQ, Meridian Tower, Grand Plaza, harbor-view diner, Central Station |
| **FOUNDRY FLATS** (industrial city) | Factories, warehouses, freight rail, cranes | Steelworks, container yard, rail depot, construction site, junkyard |
| **WILLOWBROOK** (residential city) | Suburban homes, parks, school, market street | Willowbrook Park, Maple Elementary, Sunday Market, lakeside promenade |
| **PETAL VALE** (blossom biome, east) | Cherry-blossom lanes, pink petal drifts, spring meadow | Festival Plaza, Petal Garage, Guild Office, blossom forest |
| **MIRAGE BASIN** (desert biome, south-west) | Adobe town, cacti, dune bowl, high sun | Basin Depot, Mirage Garage, The Oasis |
| **FROSTPEAK** (winter biome, north-west) | Snowfields, alpine pines, frozen lake, ice roads | Clinic Square, Frostpeak Garage, Frozen Lake |
| **EMBERFALL** (volcano biome, south-east) | Volcano cone, ash streets, ember drift, observatory | Observatory, Emberfall Garage, Cinder Crater Rim |
| **Aurora countryside** | Highways M1/M2/M3, farmland, forests, lakes, hills | Vista Point, Old Mill, Cape Light lighthouse, Horseshoe Lake, Cedar Forest, winery, radio tower |

**World layout (world coords, X east / Z north, origin center):**
- Port Meridian: north-center (z ∈ [400, 1000])
- Foundry Flats: south-east (x ∈ [400, 1000], z ∈ [-1000, -400])
- Willowbrook: north-west (x ∈ [-1000, -400], z ∈ [400, 1000])
- Petal Vale: east (x ∈ [1540, 2060], z ∈ [40, 560]) — rolling blossom meadow
- Mirage Basin: south-west (x ∈ [-2180, -1620], z ∈ [-1680, -1120]) — flat dune bowl
- Frostpeak: north-west (x ∈ [-2140, -1660], z ∈ [1160, 1640]) — raised snowy plateau
- Emberfall: south-east (x ∈ [1640, 2160], z ∈ [-1760, -1240]) — volcanic cone with crater
- Countryside fills the center & edges: forests west, lakes east-center, mountains ringing the rim
- Highway M1 runs N–S (x = 0) connecting the three core cities; M2 loops west; **M3 ring road**
  loops the outer map through all four biome cities; rural roads link POIs

**Biome ambiance (BiomeZone trigger volumes):** each biome city carries a weather zone that
overrides global weather while inside — falling **cherry petals** (Petal Vale), **dust haze**
(Mirage Basin), **snow flurries** (Frostpeak), **ember drift + warm fog** (Emberfall) — with
matching ground splats (snow, ash), terrain shaping (cone, plateau, dunes) and themed props.

**Day/night:** 24 min full cycle (adjustable). Weather: clear → cloudy → rain → fog (+ thunder
at night in rain); biome zones suspend global weather for their themed ambiance.

## 3. Characters

| Character | Role | Voice color |
|---|---|---|
| **Rin Ashford** | Player, courier | Determined, dry humor |
| **Theo Ashford** (grandfather) | Missing founder of the Bounders | Appears in notes/recordings |
| **Mara Vex** | Guild manager, gives main missions | Brisk, kind, overworked |
| **Cass Silva** | Mechanic & street racer, side activities | Cocky, loyal |
| **Dr. Imogen Hale** | Meridian defector scientist, story key | Precise, haunted |
| **Officer Dana Ruiz** | Community police officer, side missions | By-the-book, warm |
| **"Fixer" Vo** | Gray-market fixer, high-pay risky jobs | Slick, unreadable |
| **Juno** | Guild's AI dispatch assistant | Playful, hints at mechanics |

Character home locations: Mara — Bounders HQ (Port Meridian); Cass — Foundry Flats garage; Dana — Willowbrook police post; Dr. Hale — Meridian Tower; Vo — Port Meridian docks; Juno — HUD only; Theo — found through environmental storytelling. Biome arcs reuse the cast: Mara runs the Founders' Relay, Cass the Mirage Run, Dana the Cold Chain, Vo the Ember Watch, Dr. Hale the Blossom Picnic survey.

## 3.1 Story arc (10 story missions)

- **Act 1 — Homecoming (M1–M3):** Tutorial movement → first deliveries → meet Mara → car keys.
- **Act 2 — Squeeze (M4–M7):** Meridian undercuts the guild; highway races for contracts; warehouse raid for stolen parcels; Dr. Hale's warning.
- **Act 3 — The Long Deliveries (M8–M10):** Lakeside relay, radio tower broadcast of evidence, finale chase & confrontation at Meridian Tower; Theo found alive; guild saved. Epilogue unlocks free-roam "Valebound" mode.
- **Biome side arcs (S1–S6, any time after level 2):** festival delivery in Petal Vale, heat-run to Mirage Basin, medical escort in Frostpeak, sensor swap on the Emberfall volcano, Dr. Hale's four-biome climate survey, and the Founders' Relay charter run to all five city halls (unlocks the Ion X).

## 4. Gameplay Systems (summary)

- **Player:** 3rd-person move/run/sprint/jump, stamina, health (fall damage, no combat death spiral — respawn at last checkpoint).
- **Player:** full on-foot mode — exit/enter any vehicle anywhere (sidewalk-safe exit placement), run/sprint/jump, stamina, health (fall damage; respawn at last checkpoint).
- **Vehicles:** **17-vehicle roster** across 6 classes — sedans (Valley Sedan, Springs Hatch, Meridian Cab, Patrol Interceptor, Aurora GT), vans/trucks (Cargo Van, Boxline Delivery, Binhorse, Haulmaster, Rescue Rig), SUVs (Ridge SUV, Foundry Executive), emergency (Patrol Interceptor, Rescue Rig, Emberwatch Pumper), race (Comet Racer, Ion X) and **2 bikes** (Trailblazer, Swift Line) with visible rider rigs and lean-into-corner bike physics. Enter/exit anywhere, arcade physics w/ steering assist + bike-specific grip/yaw/drag, damage model (visual + handling penalty), horn, radio toggle, headlight toggle.
- **Paint shop:** 14 factory colors per vehicle (12 free + 2 premium); applies at the garage, persists per vehicle in the save, re-applied on spawn.
- **Traffic:** pooled AI cars (8-model variety) on road network lanes, density scales with device tier & time of day; kerbside parked cars dress city streets.
- **NPCs:** pedestrians walking real sidewalk routes (road-graph navigation, not random wander), daily schedules, dialogue barks, dodge-vehicle reactions, **taxi passengers** that hail the player's vehicle for fare jobs, 12 named quest-givers, ambient animals (dogs, birds, farm animals).
- **Missions:** 10 story + 4 side arcs (Cass races, Dana patrol, Vo courier runs, collector) + **6 biome side missions** (Petals on the Wind, Mirage Run, Cold Chain, Ember Watch, The Blossom Picnic, The Founders' Relay) + endless **Procedural Courier** generator + **taxi passenger jobs** + 60 collectibles (signal shards) + 8 vista photo points.
- **Daily challenges:** 3 rotating challenges per calendar day (driving, collecting, social), clean-sweep bonus, shown in the pause menu, persisted in the save.
- **Stunts:** air-time tracker — credits & XP for jumps, "Big Air" for 6 m+ drops, feeds the airtime daily challenge.
- **Economy:** credits (₡), delivery/taxi/stunt payouts, vehicle unlocks (story progression + purchases), paint shop, XP levels 1–30 with perks (stamina, sprint, discount, radar range).
- **World systems:** day/night, weather w/ wet-road audio, biome ambiance zones, full world map with discovered POI pins; fast travel to unlocked stops; minimap with mission routing arrow.
- **UI:** main menu, save slots (3), settings (graphics/audio/controls/language-en), pause with daily-challenges panel, mission board, world map, garage with paint swatches, achievements, loading with tips.

## 5. Art & Audio Direction

- **Look:** Kenney low-poly city kits + Quaternius modular streets; flat-shaded palettes; warm days, teal dawns, sodium-lamp nights. Consistent low-poly aesthetic — NO mixing with realistic PBR packs.
- **UI:** clean flat 2D UI (Kenney UI pack colors), custom-built in uGUI, safe-area aware, 3 graphic presets.
- **Audio:** CC0 city ambience beds, rain loop, engine start/loop sets per vehicle class, UI clicks, jingles, 4 chill loops as radio stations + adaptive exploration score (single layer, day/night variations via low-pass at night).
## 5.1 Art direction & content budget

- **Style rule:** ONE family — Kenney city/nature kits + Quaternius streets. Flat colors, simple PBR (URP/Lit with palette textures). No style mixing.
- **World object budget (mid device):** ≤ 2,500 rendered objects per frame after culling; ≤ 350 draw calls target; tris ≤ 500 k/frame.
- **Streaming:** 500 m world cells (7×7 grid around player, far 3×3 loaded high detail).

## 6. Monetization & compliance

- Free, NO ads, NO IAP (keeps Data Safety form trivially "no data collected" and avoids Play policy risk). Optional donation link on website only.
- No user data collected, no internet permission required. Offline single-player.
- Content rating: ESRB E / PEGI 3 expected (no violence beyond cartoon vehicle damage).

## 7. Technical architecture (Unity 2022.3 LTS + URP)

```
Assets/VALEBOUND/
  Art/Materials|Models|Textures     (imported CC0 assets, curated)
  Audio/Music|SFX|Ambience
  Prefabs/Characters|Vehicles|World|UI|Systems
  Scenes/Boot.unity, Main.unity, MenuScene.unity
  Scripts/Core|Player|Vehicles|NPC|Systems|Missions|UI|Editor
  Settings/ (URP assets, quality tiers)
  Data/ (missions, dialogue, vehicles, NPC schedules — ScriptableObjects built by editor tools)
  StreamingAssets/ (map colors, credits)
Scripts structure mirrors the same modules.
```

**Runtime assembly strategy:** one runtime asmdef (Valebound.Runtime) + one editor asmdef (Valebound.Editor).
**World generation:** deterministic — `WorldBuilder` editor window generates the terrain (biome shaping + splats), 7 settlements, ring highway, roads, POIs, biome zones, parked cars and NavMesh from a seeded layout; `VehicleRosterBuilder` builds the 17-vehicle roster with paint skins and bike rider rigs. Scene ships pre-baked. Runtime never generates geometry.

---

*This GDD is the single source of truth for VALEBOUND. All systems below implement this document.*
