# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Ash and Ember** is a magic overhaul for Mount & Blade II: Bannerlord (~62K lines, ~200 C# files under `src/`). It adds a unified elemental spell system, three alternative caster paths (Grace/miracles, the Living Ember/nature, and the Dark Gifts), NPC mages, campaign events, multiple questlines, covert operations (schemes), sea trade, market speculation, taverns, clan orders, and a reworked faction/culture layer (Templars, Tribes, Northmen, Duneborn, the Forest Clans, and the Ashen). Target framework: .NET Framework 4.7.2.

The current player-facing casting model (v0.35+) is the **unified element system**: hold Focus, load a learned element (Fire default; Wind/Earth/Water/Spirit), draw a charge by standing still, and release it as an attack cone or a wall. The older two-phase form/effect "Inner Fire" pipeline still exists underneath and drives NPC mage casts. Player-facing details live in `README.md`; the release history is in `CHANGELOG.md`.

## Commands

**Build** (requires `BannerlordPath` env var or Steam default):
```bash
dotnet build src/TheWitheringArt.csproj
```
Post-build automatically copies the DLL to `<BannerlordPath>/Modules/AshAndEmber/bin/<BannerlordBin>/`.

**Run all tests:**
```bash
dotnet test tests/AshAndEmber.Tests.csproj
```

**Run a single test:**
```bash
dotnet test tests/AshAndEmber.Tests.csproj --filter "PureLogicTests.<TestMethodName>"
```

**Install pre-built release:**
```powershell
.\install.ps1                         # auto-detect Bannerlord path
.\install.ps1 -BuildFirst             # build then install
.\install.ps1 -BannerlordPath "D:\..." # explicit path
```

## Architecture

### Entry Point and Wiring

`SubModule.xml` registers `AshAndEmber.MainSubModule` as the mod entry point. `MagicSystem.cs` contains `MainSubModule`, which on `OnGameStart()`:
- Resets **all** in-mission static state (a long block of `SpellEffects.Clear*`, `Element*.ClearBattleState`, `Nature*`, `Miracle*`, `ColourLordAI`, etc.) so a save-load in the same process cannot carry stale state.
- Registers `AshenDiplomacyModel` (permanent war override).
- Registers ~15 `CampaignBehaviorBase` subclasses: `MagicCampaignBehavior`, `SchemeCampaignBehavior`, `SanctuaryCampaignBehavior`, `AshenAltarsCampaignBehavior`, `SeaCampaignBehavior`, `CrystallinesCampaignBehavior`, `ExchangeCampaignBehavior`, `TavernCampaignBehavior`, `AshenRuinCampaignBehavior`, `MiracleCampaignBehavior`, `NatureCampaignBehavior`, `ClanOrdersCampaignBehavior`, `ElementalWildsBehavior`, `TribalKingdomBehavior`, `CreationBackstoryRework`.
- Registers the dialogue systems (`AshenDialogue`, `ArenicosDialogue`, `TempleDialogue`, `TribesDialogue`, `NorthmenDialogue`, `DunebornDialogue`) and calls per-system reset/init (`SchemeSystem.Initialize`, `ExchangeCampaignBehavior.ResetState`, `SeaCampaignBehavior.ResetForNewGame`, `ClanOrdersCampaignBehavior.ResetForNewGame`).

`OnGameInitializationFinished` re-applies the culture-text overrides (Vlandia→The Holy Temple, Khuzait→Tribes, Sturgia→Northmen, Aserai→Duneborn, Battania→The Forest Clans) after the engine reloads its XML texts. `OnApplicationTick` skips intro videos, drives the splash/loading screens, polls the three map-magic input handlers, and handles map hotkeys: **Alt+L** codex, **Shift+L** litany, and debug keys **Ctrl+Shift+F10/F11/F12** (scheme debug, spawn combat, grant-all).

`OnMissionBehaviorInitialize` injects `MagicMissionBehavior` per battle. Its `OnMissionTick` fans out to every combat subsystem (element input/effects/ultimates, crystals, miracles, nature, dark gifts, the legacy `SpellEffects.Tick*` family, and the NPC AIs); `OnAgentHit`/`OnAgentRemoved`/`OnAgentBuild` route reflect/sunder/dark-gift/crystal/nature-resist hooks.

Each registration is wrapped in its own try/catch for mod-conflict safety.

### Major Systems (folder map under `src/`)

- `Magic/` — the unified element system: `ElementMagicInput` (battle input), `ElementSpellEffects`, `ElementWallWards`, `ElementUltimates`, `ElementMapSpells`, `MagicLearning` (the Codex), `MageElementKnowledge`, teacher dialogue, and pure `*Math.cs`.
- `Spells/` — the **legacy** two-phase Inner Fire (`SpellEffects.*` partials, `BlastSpells`, `SelfSpells`, `CreateSpells`, enchantments). Still drives NPC mage casts and the shared battle-effect ticks.
- `Nature/` — the Living Ember: charges, living-energy economy, seers, backlash. Pure math in `LivingEnergyMath`/`NatureCharge`.
- `Miracles/` — Grace: prayers, grace economy, priest troops, battle AI, talents. Pure math in `MiracleMath`. **Lore:** Grace is not bestowed by a deity — it is the same Fire the rest of the mod draws on, called through the caster's own emotional and intellectual alignment (expressed as a personality trait) rather than a drawn cone. Flavour/effect text must never write "the light" as a watching, judging, or granting party; the caster (or their own conviction) is always the one deciding. The Temple's priests may *describe* it as divine favor as their institutional gloss, but player-facing miracle text should stay in the caster's own voice.
- `DarkGifts/` — the Dark Gift path (battle effects + `DarkGiftSystem`).
- `Crystals/` — consumable crystal items (`CrystalCatalog`, `CrystalEffects`, `CrystalBattleAI`, `CrystalMath`).
- `Elementals/` — **The Kindled**: elemental beings (fire/water/stone/ice/sand/storm) that roam the wilds, are summoned by mages, or wake mid-battle. `ElementalFactory` builds one; `ElementalBeings` is the mission registry that drives the following aura + the element/physical weakness; `ElementalWildsBehavior` breeds roaming bands (persisted under `ELEM_*` keys); pure `ElementalMath` holds the weakness wheel. The Spirit Unbinding's champion (`ElementUltimates`) is unified onto this core. The `ElementalKind` enum lives in `ElementUltimateMath`.
- `Schemes/` — covert operations (`SchemeSystem.*`, `SchemeCampaignBehavior.*`, minigame).
- `Soldier/` — **Take the Lord's Coin**: hire your party out to a warring lord as a common soldier from clan level 0. `SoldierServiceCampaignBehavior` (state/tick/escort + weekly pay) and its `.Dialogue` partial; pure `SoldierServiceMath` (pay, desertion penalties). Attaches as a mercenary of the lord's realm (`ChangeKingdomAction.ApplyByJoinFactionAsMercenary`), escorts via `MobileParty.SetMoveEscortParty`, and treats a player-issued map order (escort broken while on the map) as leaving — desertion before the agreed term, clean release with a bonus after.
- `Sea/` — harbors, voyages, trade ventures, NPC sea lanes; pure `SeaMath`.
- `Markets/` — the Exchange / commodity speculation (`ExchangeCampaignBehavior.*`, pure `SpeculationMath`).
- `QuestSystems/` — Dragon main quest, Burning Lab questline, settlement encounters, world events (`CampaignMapEvents.*`), battlefield events.
- `AI/`, `Tribes/`, `ClanOrders/`, `Conclave/`, `AshenRuins/`, `Apprentice/`, `Tavern/`, `Campaign/`, `Visual/`, `Startup/` — supporting culture, faction, atmosphere, and UI-flow systems.

### Spell Cast Pipeline

**Current (player):** `ElementMagicInput.Tick` reads Focus + direction + a stand-still charge, then `ElementSpellEffects` / `ElementWallWards` / `ElementUltimates` resolve the attack, wall, or ultimate. Life-cost is **flat** (the charge buys power, not a cheaper cast); the Nature discipline lowers it, and the Ashen pay in criminal standing.

**Attack forms (per element — each has its own silhouette so they read apart):**

| Element | Attack shape | Implemented in |
|---|---|---|
| Fire | **Flying bolt that explodes on impact** (bursts on first foe reached or at range's end) | `ElementSpellEffects.FireMissile` + `TickBolts`/`ExplodeBolt` (the `_bolts` list, ticked from `Tick`) |
| Wind | **Forward gust/stream** (broad wedge, knockback drives foes ahead) | `NatureEffects.BattleGale` (shared source) |
| Earth | **Forward line of erupting roots** (narrow ridge, damage + root) | `NatureEffects.BattleEntangle` (shared source) |
| Water | **Forward slowing wave** (cone) | `NatureEffects.BattleTorrent` (shared source) |
| Spirit | **Nova** (radial panic + random enemy order) | `ElementSpellEffects.SpiritPanic` |

`CastAttack(el, caster, power)` is the single dispatch choke point — the player (`ElementMagicInput`), NPC lords (`ColourLordAI`), and the Kindled (`ElementalBeings`) all cast through it, so changing an attack shape there is automatically NPC-parity-correct. **Wind/Earth/Water still delegate to the shared `NatureEffects` (Gale/Entangle/Torrent), which the Living Ember nature discipline also casts** (`NatureSeerAI`, the nature input handler — both still live in `MagicSystem`), so reshaping them there deliberately reshapes the nature-discipline versions too (consistent with the "one magic" unification). The fire bolt is a self-contained projectile (no legacy `SpellCast`/`Agent.Main` dependency) so it works for any caster; it trails fire each tick and is cleared with the rest of battle state via `ElementSpellEffects.ClearBattleState`.

**Legacy (NPC and underlying effects):**
```
MagicInputHandler (Alt+Direction buffers)
  → SpellBuilder.Parse(formBuffer, effectBuffer) → SpellCast
  → AgingSystem.ComputeBattleAgingCost(totalInputs) → days cost
  → SpellEffects.Execute*() → dispatches by spell type
```
`SpellEffects.cs` is the core partial class; `BlastSpells.cs`, `SelfSpells.cs`, `CreateSpells.cs`, and `AffectSpells.cs` extend it by spell form. NPC mage lords still cast through this path.

### State: Static vs. Serialized

- **In-mission state** lives in static fields on `SpellEffects`, `ActiveEffects`, the `Element*`/`Nature*`/`Miracle*`/`Crystal*` classes, `ColourLordAI`, etc. `MainSubModule.OnGameStart()` and `MagicMissionBehavior.OnEndMission()` both clear all of it to avoid save-reload / mission carry-over.
- **Persistent hero state** is stored in `MageKnowledgeData` (serialized into the campaign save via TaleWorlds' `CampaignObject` extension API). This holds talent purchases, aging ledger, grimoire unlocks, whisper tiers, Rival Shadow counter, and pending event flags.
- **Other persistent state** is saved per-behavior, mostly as parallel lists keyed by prefixed strings (`SEA_*`, scheme, exchange, clan-order keys) via each behavior's `SyncData`, plus custom savedata types registered in `SaveDefiner.cs`. Nature reserves, Grace, and Dark Gifts persist through their own knowledge/inventory objects. Voyage-in-progress state is intentionally **not** serialized (a mid-crossing reload refunds the fare).

### Campaign Tick Architecture

`MagicCampaignBehavior` hooks three tick rates:
- **Daily:** aging decay, Whisper tier decay, Ashen resurgence logic
- **Weekly (14+ day slots):** independent general-event and war-event queues in `CampaignMapEvents`
- **On settlement enter/leave:** `SettlementEncounters`, gated by cooldown + renown + mage status

The other behaviors register their own daily/weekly/enter-leave hooks (e.g. `NatureCampaignBehavior`, `MiracleCampaignBehavior`, `SchemeCampaignBehavior`, `SeaCampaignBehavior`, `ExchangeCampaignBehavior`, `ClanOrdersCampaignBehavior`). Keep new tick logic in the behavior that owns the concern rather than piling it onto `MagicCampaignBehavior`.

### NPC Mage AI

`ColourLordAI.TryCast()` runs on cooldowns that vary by personality:
- Ashen lords: 6 s, no aging cost, cast proactively
- Calculating: 35 s; Impulsive: 15 s; default: 25 s

AI priority: defensive burst (<40% HP) → heal burst (<30% HP) → attack (school-specific). `BanditMageAI` adds burnout risk scaled to bandit tier (35% → 15%).

### Ritual Systems (Sanctuary / Ashen Altars)

Both `SanctuaryCampaignBehavior` and `AshenAltarsCampaignBehavior` share the same hidden-accumulation pattern:
- Player sacrifices a resource per round (HP or prisoner)
- A hidden target is rolled; player decides to continue or stop
- Alignment multiplier scales yield (flipped sign between the two systems)
- NPC lords simulate 3–4 rounds automatically

### Sea Systems (Harbors / Voyages / Ventures)

`SeaCampaignBehavior` (in `src/Sea/`) adds harbor menus to 16 coastal towns, matched **by town name** at session launch (a failed match silently drops the port). Voyages run inside a wait game menu (`sea_voyage`): hazards (one storm roll, one corsair roll) are scheduled at voyage start and fire mid-crossing as inquiries; arrival teleports the party to the destination gate. Trade ventures persist in the save (`SEA_*` keys, parallel lists) and resolve on daily tick. NPC lords and caravans also use the sea lanes: on `OnSettlementLeftEvent` from a port they may be teleported to another port (lords only toward their existing AI target; caravans opportunistically), after an off-screen corsair resolution against their roster. All formulas — fares, travel hours, hazard odds, abstract boarding-battle resolution, venture margins, NPC sail gates — live in `SeaMath.cs`, which is pure (no TaleWorlds types) and covered by `PureLogicTests`. Voyage state is intentionally not serialized: a reload mid-crossing refunds the escrowed fare.

### Talent and Focus Point Costs

Elements and disciplines (Steel, Blood, Nature) are learned in the **Codex** (`MagicLearning`) with focus points. Cost escalates by how many powers you already hold: `TalentCostCurve.Cost(LearnedCount)` — 1 fp for the first power, 2 for the second, and so on (Fire is free from day one). Learning from a **teacher** costs one point less (min 1). `TalentId` still carries retired class/path enum values (Reaper, Pyrelord, the discipline classes, the Nature rites) kept **for save compatibility** — do not assume an enum member is still a live, purchasable talent; check `TalentSystem`'s definition table.

Campaign-map (non-battle) spells cost 1 aging day for the first cast per calendar day, then escalate. Battle casts pay the flat life-cost described in the pipeline section above.

### Key Numerical Constants

Numeric tuning lives in the pure `*Math.cs` files (each system has its own); those files are the source of truth. A few stable, cross-cutting values:

| Thing | Value |
|---|---|
| Mage lord fraction | ~20% of lords |
| Ashen lord fraction | ~10% of lords |
| Settlement encounter cooldown | ~6–7 days |
| World event slot interval | 14+ days |
| Bandit-unit mage fraction | ~4% of eligible units |

**Legacy two-phase values (NPC casts / underlying effects only — verify against code before relying on them):** max 5 form + 5 effect inputs per cast; aging cost `round(1.5^(n−1))` capped at 84 days; Sear/Force/Shred base ~22–35 HP per input; Restore ~15 HP per input; blast/burst radius 2.5 m per input; missile range 3 m per input. The current player casting model is flat-cost, charge-scaled (see the pipeline section), not per-input.

## Conventions

- **Naming:** PascalCase for public members and classes; `_camelCase` for private fields; enum values are PascalCase (e.g., `TalentId.Gift`, `ColorSchool.Red`).
- **One system per folder** under `src/`; large behaviors/classes are split into **partial classes by concern** across several files (e.g. `SchemeSystem.Execution.cs`, `ExchangeCampaignBehavior.Rounds.cs`, `SpellEffects.Battlefield.cs`). Never add new spell-form logic directly to `SpellEffects.cs`; add it to the appropriate `*Spells.cs` partial or a new one. Follow the existing split when a file grows.
- **Numeric logic goes in a pure `*Math.cs` file** (no TaleWorlds types) so it can be unit-tested. If a "pure" method needs a game value, pass it in as a parameter rather than reading `Hero.MainHero` inside — see `behaviour.md` for why (JIT type resolution defeats a `try/catch`).
- **Static utility classes** (`AgingSystem`, `SchoolData`, `SpellDatabase`, the `*Math` classes) have no instance state — keep them that way.
- **Null-guard pattern:** always check `Campaign.Current == null` / `Mission.Current == null` before accessing singletons in behavior methods, and wrap TaleWorlds singleton access in try/catch (mod-conflict safety).
- **Never swallow silently:** a mod-conflict-safety `catch` must record the failure, not drop it. Use `catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }` (see `src/ModLog.cs`). `ModLog` is crash-proof, references no TaleWorlds types (safe from pure `*Math.cs`), de-duplicates per failure site so a per-tick throw is logged once, and writes to `Documents\Mount and Blade II Bannerlord\AshAndEmber\errors.log`. The log self-limits: entries older than 30 days are pruned once at session start, and the file is archived to `.old` if it passes 5 MB. Do not reintroduce bare `catch { }`.
- **Tests live in `tests/PureLogicTests.cs`** and cover only pure (no-TaleWorlds-runtime) logic. Keep new tests pure — do not reference game engine types.

## Working behaviour

Process and working-style guidance (how to build, verify the TaleWorlds API, bump
the version, and avoid mod-conflict crashes) lives in a separate file:

@behaviour.md
