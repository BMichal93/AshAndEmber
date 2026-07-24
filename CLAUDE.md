# CLAUDE.md

Map for Claude Code working in this repo. It points at where things live and the
non-obvious rules that break saves or crash the game if you miss them — it does
**not** restate what the code already says. Player-facing behaviour is in
`README.md`; release history is in `CHANGELOG.md`.

**Ash and Ember** is a magic overhaul for Mount & Blade II: Bannerlord (~76K
lines, ~254 C# files under `src/`). Target framework: .NET Framework 4.7.2. No
Harmony — everything is layered through the game's `CampaignBehavior` /
`MissionBehavior` hooks.

## Commands

```bash
dotnet build src/TheWitheringArt.csproj        # auto-copies the DLL into <BannerlordPath>/Modules/AshAndEmber/bin/<BannerlordBin>/
dotnet test  tests/AshAndEmber.Tests.csproj    # 237 pure-logic NUnit tests
dotnet test  tests/AshAndEmber.Tests.csproj --filter "PureLogicTests.<Name>"
bash tools/checks/check-conventions.sh         # empty-catch + version-sync guardrails (also run in CI)
```

With a local Bannerlord install the projects reference the real game DLLs (set
`BannerlordPath`/`BannerlordBin` — see `README.md`); **without one they fall back
to the `Bannerlord.ReferenceAssemblies.*` NuGet packages**, so build/test work
headless (that's what `.github/workflows/ci.yml` runs). `.\install.ps1` installs a
pre-built release. Build/test details and the four-file version-bump checklist
are in **`behaviour.md`** (imported below).

> **Naming quirk:** the main project file is still `src/TheWitheringArt.csproj`
> (a retired working title). The assembly, root namespace, and `SubModule.xml`
> module id are all `AshAndEmber`. Renaming the csproj is deferred — it has
> save-compatibility fan-out — so don't assume file name == module name.

## Start here: entry point

`SubModule.xml` → `AshAndEmber.MainSubModule` (in `MagicSystem.cs`). On
`OnGameStart()` it: (1) **resets all in-mission static state** — a long block of
`*.Clear*` / `ClearBattleState` calls; (2) registers `AshenDiplomacyModel` and
~15 `CampaignBehaviorBase` subclasses; (3) registers the dialogue systems and
per-system reset/init. Every registration is in its own try/catch for
mod-conflict safety. `OnMissionBehaviorInitialize` injects `MagicMissionBehavior`
per battle, whose `OnMissionTick` fans out to every combat subsystem.

If you add a behavior/system, register it here and add its state reset to the
`OnGameStart` block — otherwise a save-load in the same process carries stale
state (see below).

## The rule that breaks saves: static vs. serialized state

- **In-mission state** = static fields on `SpellEffects`, `Element*`, `Nature*`,
  `Miracle*`, `Crystal*`, `ColourLordAI`, etc. Cleared in **both**
  `MainSubModule.OnGameStart()` and `MagicMissionBehavior.OnEndMission()`. New
  static battle state MUST be cleared in both places.
- **Persistent hero state** = `MageKnowledgeData` (serialized via TaleWorlds'
  `CampaignObject` extension API): talents, aging ledger, grimoire unlocks,
  whisper tiers, pending flags.
- **Other persistent state** = per-behavior `SyncData`, mostly parallel lists
  keyed by prefixed strings (`SEA_*`, `ELEM_*`, scheme/exchange/clan-order keys);
  custom savedata types register in `SaveDefiner.cs`. Voyage-in-progress state is
  intentionally **not** serialized (a mid-crossing reload refunds the fare).

## Folder map (`src/`)

| Folder | What's there |
|---|---|
| `Magic/` | **Current** unified element system: input, effects, walls, ultimates, map spells, the Codex (`MagicLearning`), teachers, pure `*Math.cs`. |
| `Spells/` | **Legacy** two-phase "Inner Fire" pipeline (`SpellEffects.*` partials). Still drives NPC mage casts + shared battle-effect ticks. |
| `Nature/` `Miracles/` `DarkGifts/` | The three alternate caster paths (Living Ember / Grace / Dark Gifts). |
| `Crystals/` | Consumable crystal items + battle AI. |
| `Elementals/` | **The Kindled** — roaming/summoned elemental beings; `ElementalVisuals` binds particles once per skeleton (don't re-stamp per tick). |
| `Schemes/` `Sea/` `Markets/` `Soldier/` `Tavern/` `ClanOrders/` | Campaign systems: covert ops, sea trade, speculation, mercenary soldiering, taverns, clan orders. |
| `QuestSystems/` | Dragon main quest, Burning Lab, settlement encounters, world/battle events. |
| `AI/` `Tribes/` `AshenRuins/` `Conclave/` `Apprentice/` `Visual/` `Startup/` | Culture/faction, NPC mage AI, atmosphere, and UI-flow support. |

## Spell cast pipeline

**Player (current):** `ElementMagicInput.Tick` reads Focus + direction + a
stand-still charge → `ElementSpellEffects` / `ElementWallWards` /
`ElementUltimates`. Life-cost is **flat**; Nature lowers it, the Ashen pay in
criminal standing.

`CastAttack(el, caster, power)` is the **single dispatch choke point** — player,
NPC lords (`ColourLordAI`), and the Kindled (`ElementalBeings`) all route through
it, so changing an attack shape there is automatically NPC-parity-correct.
Wind/Earth/Water delegate to the shared `NatureEffects` (Gale/Entangle/Torrent),
which the nature discipline also casts — reshaping them there reshapes both, by
design.

**Legacy (NPC + underlying effects):** `MagicInputHandler` → `SpellBuilder.Parse`
→ `SpellCast` → `AgingSystem.ComputeBattleAgingCost` → `SpellEffects.Execute*()`.
NPC mage lords still cast through this. Add new spell-form logic to the right
`*Spells.cs` partial, never to `SpellEffects.cs` itself.

Per-element attack shapes, mastery scaling, and numeric tuning are documented in
the code near `CastAttack` and in each system's `*Math.cs` — those files are the
source of truth; don't duplicate their numbers here.

## Conventions

- **Naming:** PascalCase public / `_camelCase` private / PascalCase enum values.
- **One system per folder**; large classes split into **partials by concern**
  (`SchemeSystem.Execution.cs`, `SpellEffects.Battlefield.cs`, …). Follow the
  existing split when a file grows.
- **Numeric logic goes in a pure `*Math.cs`** (no TaleWorlds types) so
  `PureLogicTests` can cover it. If a "pure" method needs a game value, pass it
  in as a parameter — do not read `Hero.MainHero` inside. (.NET resolves types at
  JIT time, so a `try/catch` won't make the method loadable in the test runner —
  see `behaviour.md`.) Keep new tests pure.
- **Null-guard + try/catch** every TaleWorlds singleton access (`Campaign.Current`,
  `Mission.Current`) for mod-conflict safety.
- **Never swallow silently:** a safety `catch` records the failure —
  `catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }` (see
  `src/ModLog.cs`; crash-proof, no TaleWorlds types, de-duped per site). No bare
  `catch { }`.
- **`_deferredInquiry` is a single slot.** It holds one pending blocking popup.
  Guard with `if (MageKnowledge._deferredInquiry != null) return;` before setting
  it, or you clobber another system's queued event. Plain
  `InformationManager.DisplayMessage` log lines can post directly.
- **`TalentId` keeps retired enum values** (Reaper, Pyrelord, the discipline
  classes, the Nature rites) **for save compatibility.** An enum member is not
  proof of a live, purchasable talent — check `TalentSystem`'s definition table.
- **Grace lore:** Grace is not bestowed by a deity — it is the same Fire, called
  through the caster's own emotional/intellectual alignment (a personality
  trait). Player-facing miracle text must never write "the light" as a watching,
  judging, or granting party; the caster (or their own conviction) always
  decides. Temple priests may *describe* it as divine favour as their
  institutional gloss.

## Working behaviour

How to build, verify the TaleWorlds API against the real DLLs, bump the version
(four files, kept in sync), and avoid mod-conflict crashes:

@behaviour.md
