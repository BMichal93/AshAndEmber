# Ash and Ember — v0.9.1

A Mount & Blade II: Bannerlord magic overhaul centred on the Inner Fire: a single, versatile force shaped by the caster's will. Lords who carry it fight differently. Bandits who steal it burn. The Ashen march from the north and do not negotiate.

---

## Package Structure

```
AshAndEmber/
├── SubModule.xml                    mod manifest
├── ModuleData/
│   ├── items.xml                    (reserved)
│   └── troops.xml                   (reserved)
├── src/                             ~8 500 lines across 32 source files
│   ├── MagicSystem.cs               module entry point + mission behaviour
│   ├── MageKnowledge.cs             gift tracking, grimoire UI, talent menu
│   ├── SpellBuilder.cs              two-phase input parser → SpellCast
│   ├── TalentSystem.cs              22 talents (7 passive, 8 enchantment, 8 spell)
│   ├── AgingSystem.cs               casting cost (days of life), Blight path
│   ├── MagicInputHandler.cs         keyboard/gamepad combo detection
│   ├── CampaignBehavior.cs          new-game setup, aging, map event hooks
│   ├── CampaignMapEvents.cs         seven rare world events (weekly tick)
│   ├── BattleEvents.cs              per-battle battlefield events with atmospheric visuals
│   ├── DragonQuestSystem.cs         main quest — The Last Flight of the Dragons
│   ├── SettlementEncounters.cs      40+ random events on settlement enter/leave/battle
│   ├── SchoolData.cs                colour school definitions and visual data
│   ├── SpellDatabase.cs             spell definition registry
│   ├── ActiveEffects.cs             per-frame effect state tracking
│   ├── Spells/
│   │   ├── SpellEffects.cs          core partial: helpers, effects, targeting, death queue
│   │   ├── AffectSpells.cs          affect form execution
│   │   ├── BlastSpells.cs           Blast form execution
│   │   ├── SelfSpells.cs            Missile + Ward forms
│   │   └── CreateSpells.cs          Barrier + Burst forms
│   ├── Visual/
│   │   ├── AreaEffects.cs           persistent area effect engine + light management
│   │   ├── GlowSystem.cs            agent glow outlines + cast sound
│   │   ├── MoveSystem.cs            smooth push/pull lerp movement
│   │   ├── AshenSceneTone.cs        cold atmospheric fog in Ashen battles
│   │   └── NamePrefixes.cs          title/prefix management for mage lords
│   └── AI/
│       ├── ColourLordRegistry.cs    marks lords as mages or Ashen lords; save/load
│       ├── ColourLordAI.cs          priority-driven battle AI for mage lords
│       ├── ColourUnitRegistry.cs    unit-level mage tracking (stub)
│       ├── BanditMageAI.cs          rare bandit unit spellcasters with burnout
│       ├── BlightSystem.cs          Ashen blight mechanics (stub)
│       ├── AshenDialogue.cs         silences all dialogue with Ashen lords and Spawn
│       ├── FireWorshippersSystem.cs renames qualifying bandit parties
│       └── AshenCitySystem.cs       Ashen kingdom, settlements, war, resurgence
├── tests/
│   ├── AshAndEmber.Tests.csproj
│   └── PureLogicTests.cs
└── README.md
```

---

## Installation

### Requirements

- **OS:** Windows 10 or Windows 11
- **Game:** Mount & Blade II: Bannerlord — Steam or Xbox / Game Pass
- **Version compatibility:** built against Bannerlord's `.NET Framework 4.7.2` runtime

### Step 1 — Download

Download the latest release ZIP. Extract it anywhere. You get a single `AshAndEmber` folder.

### Step 2 — Install

#### Option A — Script (recommended)

Open PowerShell in the extracted folder, then:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\install.ps1
```

The script finds your Bannerlord installation automatically (Steam registry, default paths, Xbox paths). For a non-standard location:

```powershell
.\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"
```

#### Option B — Manual

Copy the `AshAndEmber` folder (the one containing `SubModule.xml`) into:

```
<BannerlordRoot>\Modules\AshAndEmber\
```

`SubModule.xml` must be directly inside `Modules\AshAndEmber\`, not one level deeper.

### Step 3 — Enable in launcher

Open the Bannerlord launcher → Mods → tick **Ash and Ember** → Play.

### Step 4 — Verify

Start a new Sandbox campaign. A lore introduction screen appears. If the **"The Inner Fire"** gift prompt appears shortly after, the mod is installed correctly.

---

## Lore Introduction

When starting a new Sandbox campaign, a short lore screen appears before play begins:

- The fire gives life. The Ashen chose the cold instead.
- Three Empire factions fight over Calradia's bones while ash moves south.
- Some mages, tempted by unliving, may answer the cold's call.

The gift selection follows immediately after.

---

## Getting the Gift

The Inner Fire must be *found*, not chosen at a menu.

- At campaign start a prompt appears asking if the fire has always been there. Accepting grants the Gift.
- The Gift can also arrive through certain in-game events (aging, bloodline, encounters, companions).

Once you carry the Gift, the grimoire is available at any time. Without it, spellcasting does nothing.

---

## Controls

### Keyboard

| Action | Input |
|--------|-------|
| Enter spell mode | Hold **Left Alt** |
| Shape form / effect | **W** (↑)  **A** (←)  **D** (→)  **S** (↓) while holding Alt |
| Switch to effect phase | Press **X** (Break) |
| Cast | Release **Left Alt** |
| Open grimoire | **Alt + X** (only when no form has been started) |

### Gamepad

| Action | Input |
|--------|-------|
| Enter spell mode | Hold **Left Bumper (LB)** |
| Shape form / effect | Push **left stick** (↑/←/↓/→) |
| Break | Press **L3** (left stick click) |
| Cast | Release **LB** |
| Open grimoire | **LB + Right Bumper (RB)** |

### How casting works

1. **Hold the focus key.** The buffer is empty.
2. **Input form keys** — each press adds one count (e.g. three W presses = Blast, formCount 3).
3. **Press Break (X / L3).** The input switches to the effect phase.
4. **Input effect keys** after Break.
5. **Release the focus key.** The spell fires.

Different form types may be mixed freely before Break — all fire simultaneously on release.

The buffer shows in the message log while held: `[ UUU ▷ UU ]` = Blast ×3, Damage ×2.

---

## Spell Forms (before Break)

| Key | Arrow | Form | What it does |
|-----|-------|------|--------------|
| W | ↑ | **Blast** | Forward cone. Range = max(4, formCount × 2.5) m. Cone visuals scale to match. |
| A | ← | **Missile** | Fast projectile that travels forward then explodes. Range = max(8, missileCount × 3) m. Explosion radius = 1 + missileCount m. |
| D | → | **Barrier** | Wall of stationary fire nodes perpendicular to facing. One node per press, 1.5 m apart. Cast again to release. |
| S | ↓ | **Burst** | Circle centred on caster. Radius = max(2, formCount × 2.5) m. |

### Multi-form example

`WW SS X UUU` — Blast (5 m) + Burst (5 m) simultaneously, 75 fire damage to enemies. 7 inputs = 2 days cost.

---

## Effects (after Break)

| Key | Arrow | Effect | Per count | Targets |
|-----|-------|--------|-----------|---------|
| W | ↑ | **Damage** | 25 fire damage | Enemies |
| A | ← | **Damage** | 25 fire damage | Enemies |
| D | → | **Damage** | 25 fire damage | Enemies |
| S | ↓ | **Restore** | 15 healing | Allies (Burst also heals caster) |

Damage and Restore may be combined in the same cast.

---

## Aging Cost

Every spell draws on your lifespan. Cost scales with total inputs (form + effect combined), capped at 2 days:

| Total inputs | Cost |
|--------------|------|
| 1–3 | 1 day |
| 4+ | 2 days |

**BattleMage** talent reduces the cost by 1 day (minimum 0). **Sorcerer** talent reduces it further.

### Becoming Ashen

At age 100 a prompt appears: *The Last Ember*. You may:

- **Take the cold** — become Ashen. Aging stops permanently (the cost of casting is now crime rating, not years). Your appearance changes: ash-white, grey-tinged. Your lords and the Ashen kingdom treat you as one of their own.
- **Let it end** — die of old age.

Ashen mages are completely immune to all magical aging — both the per-cast aging and the daily age check.

### Tournament

Casting **any** spell during a tournament kills and disqualifies you instantly.

---

## Talents

Talents are learned through the grimoire (Alt+X → *Talents*). The **Gift** is free. The first 7 purchased cost 1 focus point each; 8th onward costs 2 points.

### Passive

| Talent | Effect |
|--------|--------|
| **Gift** | You carry the fire. Battle casting enabled. |
| **BattleMage** | Each battle cast costs 1 fewer day (minimum 0). |
| **Sorcerer** | Further aging cost reduction. |
| **Camaraderie** | +10 relations with mage lords; relation cannot fall below 0 with them. |
| **Reap** | Executing a captured lord restores 100 days. Raiding a village restores 5 days (7-day cooldown). Each discarded prisoner: 5% chance to restore 1 day. |
| **Ember** | 5% chance per battle kill to restore 1 day of youth. |
| **DevourLife** | (DevourLife passive) absorbs lifeforce on execution. |

### Enchantment

Enchantments fire automatically on every qualifying cast in battle.

**Damage enchantments** (trigger: Damage effect on enemies):

| Talent | Effect |
|--------|--------|
| **Scatter** | Blasts non-mounted enemies backward. Push = 4 m per Damage input. |
| **Smoulder** | Scorches enemy morale. Loss = 12 per Damage input. |
| **Bewilder** | Random effect on non-hero enemies: instant rout, force charge, dismount, or morale fracture. |
| **Waver** | 12% chance per hit on a tier 1–2, non-mounted, non-hero enemy to convert them to your team. |

**Restore enchantments** (trigger: Restore effect on allies):

| Talent | Effect |
|--------|--------|
| **Ashveil** | Brief magic immunity for healed allies. Duration = 3 s per Restore input. |
| **CinderShell** | Reduces incoming damage for 8 s. Protection = 5% per Restore input, max 50%. |
| **Hearthlight** | Lifts allied morale. Boost = 12 per Restore input. |
| **Rouse** | With 3+ Restore inputs, each healed ally has a 15% chance to summon a soldier near you. |

### Spell (campaign map)

Cast from the grimoire on the campaign map. Each costs 1 aging day (or crime rating if Ashen). NPC mage lords also cast these on the campaign map.

| Talent | Effect |
|--------|--------|
| **Subjugate** | Your largest prisoner group yields and joins your ranks. |
| **Rejuvenate** | Up to 8 wounded soldiers per type recover across your roster. |
| **Inspire** | Party morale +40 and up to 5 wounded soldiers roused. |
| **PlantGrowth** | Grain grows proportional to party size (50–200 measures). |
| **BreakWills** | Nearest enemy party within 100 map-units loses 35 morale. |
| **Plague** | Nearest enemy village loses 20% of its hearth. |
| **Clairvoyance** | +40 influence, or +1000 gold if not in a kingdom. |
| **Curse** | 5–12 soldiers in the nearest enemy party are wounded or killed; morale breaks. |

---

## NPC Mage Lords

At campaign start roughly 20% of lords are seeded as mages. A subset are **Ashen lords** — they cast with no aging cost, shorter cooldown, and heavier spell recipes.

### Campaign map casting

NPC lords cast on the campaign map independently. Ashen lords cast approximately every 3–7 days; regular mage lords every 5–10 days. Older lords cast less frequently.

### Battle AI priority

1. **Ward self** if HP < 40% or magic was recently used nearby.
2. **Heal burst** if HP < 30%.
3. **Heal burst** for allies below 50% HP within 15 m.
4. **Burst** (when 3+ enemies within 8 m) or **Blast** (enemies in forward cone).

Ashen lords skip the no-enemies early exit and cast proactively at all times. First cast is delayed 12 seconds; subsequent casts use the lord's trait-modified cooldown.

### Aging (NPC)

NPC lords age after every battle in which they cast: `max(1, totalInputs / 4)` days. Ashen lords are immune. NPC lords die at age 100 (5% chance to become Ashen instead).

### Cooldowns

| Lord type | Cooldown |
|-----------|----------|
| Default | 25 s |
| Impulsive | 15 s |
| Calculating | 35 s |
| Ashen | 6 s |

---

## Bandit Mages

About 4% of eligible bandit units carry a stolen fragment of the fire. After each cast, there is a chance the caster burns out and dies.

| Tier | Troop types | formCount | Burnout |
|------|-------------|-----------|---------|
| Untrained | Looter | 1 | 35% |
| Bandit | forest/sea/mountain/steppe/desert bandit | 2 | 25% |
| Cultist | Fire Worshippers / Ashen Spawn | 3 | 15% |

Each type has a title shown in the combat log: Fire Prophet, Hedge Witch, Ashen Caller, Ash Shaman, Wind Binder, Ember Prophet.

### Fire Worshippers & Ashen Spawn

~10% of Looter and forest bandit parties become **Fire Worshippers**. ~10% of sea raider and mountain bandit parties become **Ashen Spawn**. Both are guaranteed at least one mage caster.

**Ashen Spawn parties are very large** — spawned by world events, they arrive as hordes of 120–200 troops. Talking to their leader is impossible; they only answer with silence.

---

## Battlefield Events

Occasionally a battle begins under cursed conditions. Each event rolls independently; most battles have none. Active events are announced in the message log.

| Event | Chance | Effect |
|-------|--------|--------|
| **Cinder Rain** | 10% | Every non-Ashen agent takes damage every 20 s. Burning-sky fog, ground fire field, aerial amber glow. |
| **Ember Tithe** | 7% | Every Ashen agent takes damage every 20 s but gains +10 morale. Amber pulse above their formation. |
| **The Rising** | 12% | Spawns units on the Ashen side every 20 s (Ashen battle only). Ground eruption burst + ghostly aerial lights at spawn. |
| **Dread** | 8% | All non-Ashen agents lose 30 morale (one-shot). Sky darkens to deep dusk, cold dark fog, grey fire field across the whole battle area. |
| **Last Light** | 5% | Sets time-of-day to midnight (one-shot). Fire-lit night fog, wide ground fire, burning-sky aerial glow. |
| **Ashen Ground** | 7% | All mounted agents are dismounted every 20 s. Ash-grey fog, grey ground particles. |
| **Frenzy** | 7% | Charge order to every formation every 20 s. Blood-red fog, chaotic fire field, aerial crimson glow. |

Expected events per battle: ~0.5. ~60% of battles are clean.

---

## Settlement Encounters

When entering or leaving a settlement, or after a battle, the mod may trigger a short narrative encounter — a short piece of text with a choice that has a mechanical consequence (gold, relations, morale, troop changes). The encounter pool has over 40 unique events gated by mage status, Ashen status, renown, and settlement type.

A cooldown of 3 days prevents back-to-back encounters. Encounter chance: 35% per settlement transition, 35–55% per battle type.

---

## Main Quest — The Last Flight of the Dragons

*"There is a way to rekindle the world. One great burning — everything, at once."*

### Trigger

Defeat an Ashen lord's party for the first time. A dying old mage approaches you. He has been looking for someone for forty years.

### Goals (active after accepting)

| Goal | Condition |
|------|-----------|
| Establish dominion | Reach Clan Tier 6 |
| Enter the cold heart | Capture Tyal |
| Gain the power | Reach Hero Level 25 |

Progress is tracked in the grimoire (Alt+X).

### Resolution

When all three goals are met, a final prompt appears. You may:

- **Rekindle the world** — pour everything into a single great burning. All Ashen lords, all mage lords, and all mage companions die. All Ashen settlements are redistributed. World map events cease. The player hero dies — game over.
- **Refuse** — the chance passes forever. The game continues.

Refusing the old man at the initial encounter permanently closes the quest.

---

## The Ashen Kingdom

The Ashen chose cold over death. They do not age, they do not negotiate, and they are permanently at war with every other kingdom.

### Ashen settlements

At campaign start the following settlements are assigned to the Ashen Kingdom. Their garrisons are filled with high-tier troops immediately. All stats are locked daily to maximum.

**Core cities:** Tyal, Sibir, Baltakhand, Amprela  
**Castles and towns:** Urikskala, Kaysar, Dinar, Vladiv, Varnovapol, Tepes, Epinosa, Takor, Khimli, Lochana, Syratos  
**Additional (v1.0):** Ostican (Vlandian, with nearby castles)

**Settlement health (locked daily):**

| Stat | Value |
|------|-------|
| Loyalty | 100 (never rebel) |
| Security | 100 |
| Food stocks | Maximum |
| Prosperity | 5 000 |
| Militia | 1 500 (cities) / 600 (castles) |

**Garrisons:** 1 500 troops minimum for cities, 800 for castles. Filled with the highest-tier troop available from that settlement's culture.

### Conquest behaviour

**Ashen cities do not auto-return.** If you besiege and capture an Ashen city, it stays yours. Loyalty and security are immediately set to 100 so there is no instant rebellion. The Ashen system removes that city from its managed list.

**Extinction resurgence.** If all Ashen settlements are taken, the Ashen claim a random non-player city without warning. The cold always finds new ground.

### Ashen lords

- Do not age (birth day reset daily to ~35).
- Cast spells with no aging cost; 6-second cooldown.
- Always carry Scatter + Curse + BreakWills + Plague. 50% chance of Smoulder.
- Captured Ashen lords and Ashen Spawn party leaders refuse all dialogue. Encounters with them end with silence.

### Criminal status

Non-Ashen players are permanent criminals in Ashen lands. Ashen players have their crime rating cleared daily.

### Permanent war

Peace with the Ashen is revoked within 1–2 days and war re-declared.

---

## Campaign World Events

Seven rare events fire on the weekly tick. Multiple may fire the same week.

| Event | Chance/week | Effect |
|-------|-------------|--------|
| **Ashen Plague** | 8% | Wounds entire garrison of a random city/castle. Spawns 3 Ashen Spawn hordes (120–140 troops each) nearby. |
| **Great Withering** | 10% | Destroys 80% of a village's hearth or halves a city's prosperity. |
| **Ashen March** | 5% | Spawns 6 Ashen Spawn hordes (200 troops each, strength ≥ 70) across a random non-Ashen kingdom. |
| **Long Night** | 3% | Forces mod light-level to Dark for 7 days. Each day drains prosperity from every non-Ashen town. |
| **Ashen Tide** | 3% | A random non-Ashen castle is seized by an Ashen lord. Loyalty/security set to max immediately. |
| **Fire Fades** | 1.5% | 2–4 non-Ashen lords aged 25–55 (not clan leaders) die. Their home settlement weakens. |
| **Darkened Roads** | 6% | All caravans of a random kingdom vanish. Town prosperity drops 15%. 2 Ashen ambush parties arrive. |

---

## New-Game Settlement Reassignments

At campaign start, several settlements are moved to better reflect the world state:

| Settlement | From | To |
|------------|------|----|
| Marunath + castle_B5, castle_B2 | Battania | Northern Empire |
| Jaculan + castle_V2, castle_V7 | Vlandia | Western Empire |
| Seonon + nearby Battanian castles | Battania | Northern Empire |
| Razih + nearby Aserai castles | Aserai | Southern Empire |
| Ostican + nearby Vlandian castles | Vlandia | Ashen Kingdom |

All transferred settlements have loyalty and security set to 100 immediately.

---

## Building from Source

### Requirements

- .NET SDK 6 or later
- A local Bannerlord installation

### Environment variables

| Variable | Value |
|----------|-------|
| `BannerlordPath` | Path to your Bannerlord root (folder containing `bin` and `Modules`) |
| `BannerlordBin` | `Win64_Shipping_Client` (Steam) or `Gaming.Desktop.x64_Shipping_Client` (Xbox) |

```powershell
$env:BannerlordPath = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
dotnet build src\TheWitheringArt.csproj
```

Output: `src\bin\Debug\AshAndEmber.dll`. The build copies it to the Modules folder automatically.

---

## Troubleshooting

**"The fire does not stir in you."**  
You do not carry the Gift. Start a new campaign and accept the prompt.

**Spells fire but nothing happens**  
You may be in a tournament (casting kills you), in a prisoner state, or you mixed form keys incorrectly.

**Ashen settlements show as unclaimed for the first day**  
Expected. Ownership is asserted on the first daily tick.

**A conquered Ashen city keeps getting taken back**  
This was fixed in v1.0. Conquered Ashen cities now stay conquered permanently. If you are still seeing this, verify the DLL version matches this README.

**The Ashen have disappeared entirely**  
The extinction resurgence fires automatically — a random city will fall to the Ashen within a few in-game days.

**Script reports "Could not auto-detect your Bannerlord installation"**  
Pass the path manually: `.\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"`
