# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Ash and Ember** is a magic mod for Mount & Blade II: Bannerlord (~28K lines, 45 C# files). It adds a full spell system (Inner Fire), NPC mages, campaign events, questlines, and covert operations. Target framework: .NET Framework 4.7.2.

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
- Registers `AshenDiplomacyModel` (permanent war override)
- Registers five `CampaignBehaviorBase` subclasses: `MagicCampaignBehavior`, `SchemeCampaignBehavior`, `SanctuaryCampaignBehavior`, `AshenAltarsCampaignBehavior`, `SeaCampaignBehavior`
- Injects `MagicMissionBehavior` per battle

Each registration is wrapped in its own try/catch for mod-conflict safety.

### Spell Cast Pipeline (Battle)

```
MagicInputHandler (Alt+Direction buffers)
  → SpellBuilder.Parse(formBuffer, effectBuffer) → SpellCast
  → AgingSystem.ComputeBattleAgingCost(totalInputs) → days cost
  → SpellEffects.Execute*() → dispatches by spell type
```

`SpellEffects.cs` is the core partial class; `BlastSpells.cs`, `SelfSpells.cs`, `CreateSpells.cs`, and `AffectSpells.cs` are partial-class files that extend it by spell form.

### State: Static vs. Serialized

- **In-mission state** lives in static fields on `SpellEffects`, `ActiveEffects`, `GlowSystem`, etc. `MainSubModule.OnGameStart()` clears all of it to avoid save-reload carry-over.
- **Persistent hero state** is stored in `MageKnowledgeData` (serialized into the campaign save via TaleWorlds' `CampaignObject` extension API). This holds talent purchases, aging ledger, grimoire unlocks, whisper tiers, Rival Shadow counter, and pending event flags.

### Campaign Tick Architecture

`MagicCampaignBehavior` hooks three tick rates:
- **Daily:** aging decay, Whisper tier decay, Ashen resurgence logic
- **Weekly (14+ day slots):** independent general-event and war-event queues in `CampaignMapEvents`
- **On settlement enter/leave:** `SettlementEncounters`, gated by 6-day cooldown + renown + mage status

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

The six purchasable fire paths (Reaper, Seer, Warden, Heartfire, Pyrelord, Ashbinder) use escalating cost: 1 fp for the first path you own, 2 fp for the second, 3 fp for the third, and so on. Discipline classes (Coldsworn, Gracebound, AshenAlchemist, NatureSage) cost a flat 2 fp each and are purchased at their ritual sites. Campaign (non-battle) spells cost 1 day for the first cast per day, then escalate (1 → 7 → 14 → 21 …).

### Key Numerical Constants

| Thing | Value |
|---|---|
| Max form inputs per cast | 5 (reaching 5 auto-breaks to effect phase) |
| Max effect inputs per cast | 5 |
| Aging cost formula | round(1.5^(n−1)), capped at 84 days |
| Aging cost at max (10 inputs) | 38 days |
| Sear base damage per input | 35 HP + 1 m push |
| Force base damage per input | 22 HP + 5% vulnerability 6 s |
| Shred base damage per input | 22 HP + 12 morale drain |
| Restore base heal per input | 15 HP + 6 morale |
| Blast radius per input | 2.5 m |
| Burst radius per input | 2.5 m |
| Missile range per input | 3 m |
| Mage lord fraction | ~20% of lords |
| Ashen lord fraction | ~10% of lords |
| Settlement encounter cooldown | 6 days |
| World event slot interval | 14+ days |

## Conventions

- **Naming:** PascalCase for public members and classes; `_camelCase` for private fields; enum values are PascalCase (e.g., `TalentId.Gift`, `ColorSchool.Red`).
- **Large classes are split into partial classes** by concern — never add new spell-form logic directly to `SpellEffects.cs`; add it to the appropriate `*Spells.cs` partial file or create a new one.
- **Static utility classes** (`AgingSystem`, `SchoolData`, `SpellDatabase`) have no instance state — keep them that way.
- **Null-guard pattern:** always check `Campaign.Current == null` / `Mission.Current == null` before accessing singletons in behavior methods.
- **Tests live in `tests/PureLogicTests.cs`** and cover only pure (no-TaleWorlds-runtime) logic. Keep new tests pure — do not reference game engine types.

## Working behaviour

Process and working-style guidance (how to build, verify the TaleWorlds API, bump
the version, and avoid mod-conflict crashes) lives in a separate file:

@behaviour.md

## Pending task: lifespan migration

A ready-to-implement spec for replacing the forward-aging spellcasting cost
with a shrinking life-expectancy model lives in a separate file. Implement it
exactly as written (it is a specification, not a discussion), run the
build/test/verify steps it lists, then follow its cleanup step: delete the
spec file and remove this section (including this import) from CLAUDE.md.

@lifespan-migration.md

## Pending task: charge-power casting

A ready-to-implement spec for making draw/charge length scale a battle
cast's power (instead of its aging cost) lives in a separate file. Implement
it exactly as written (it is a specification, not a discussion), run the
build/test/verify steps it lists, then follow its cleanup step: delete the
spec file and remove this section (including this import) from CLAUDE.md.
This task is independent of the lifespan migration above — implement both,
in either order.

@charge-power-casting.md
