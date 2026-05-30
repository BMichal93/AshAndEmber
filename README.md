# Ash and Ember — v0.9

A Mount & Blade II: Bannerlord magic overhaul centred on the Inner Fire: a single, versatile force shaped by the caster's will. Lords who carry it fight differently. Bandits who steal it burn. The Ashen march from the north and do not negotiate.

---

## Package Structure

```
AshAndEmber/
├── SubModule.xml                    mod manifest
├── ModuleData/
│   ├── items.xml                    (reserved)
│   └── troops.xml                   (reserved)
├── src/                             ~6 000 lines across 25 source files
│   ├── MagicSystem.cs               module entry point + mission behaviour
│   ├── MageKnowledge.cs             gift tracking, grimoire UI, talent menu
│   ├── SpellBuilder.cs              two-phase input parser → SpellCast
│   ├── TalentSystem.cs              21 talents (7 passive, 6 enchantment, 8 spell), learning logic
│   ├── AgingSystem.cs               casting cost (days of life), Blight path
│   ├── MagicInputHandler.cs         keyboard/gamepad combo detection
│   ├── CampaignBehavior.cs          new-game setup, aging, map event hooks
│   ├── CampaignMapEvents.cs         seven rare world events (weekly tick)
│   ├── BattleEvents.cs              rare per-battle battlefield events
│   ├── Spells/
│   │   ├── SpellEffects.cs          core partial: helpers, effects, magic memory
│   │   ├── BlastSpells.cs           Blast form execution
│   │   ├── SelfSpells.cs            Wave + Ward + Burst self-effects
│   │   └── CreateSpells.cs          Barrier form execution
│   ├── Visual/
│   │   ├── AreaEffects.cs           persistent area effect engine
│   │   ├── GlowSystem.cs            agent glow outlines + cast sound
│   │   └── MoveSystem.cs            smooth push/pull lerp movement
│   ├── AI/
│   │   ├── ColourLordRegistry.cs    marks lords as mages or blight lords
│   │   ├── ColourLordAI.cs          priority-driven battle AI for mage lords
│   │   ├── BanditMageAI.cs          rare bandit unit spellcasters
│   │   ├── FireWorshippersSystem.cs renames qualifying bandit parties
│   │   └── AshenCitySystem.cs       Ashen kingdom, settlements, war maintenance
│   └── TheWitheringArt.csproj       build project (outputs AshAndEmber.dll)
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

When starting a new Sandbox campaign, a short lore screen appears before play begins. It describes the world state:

- The fire gives life. The Ashen chose the cold instead.
- Three Empire factions fight over Calradia's bones while ash moves south.
- Some mages, tempted by unliving, may answer the cold's call.

The prompt closes immediately on the button press. The gift selection follows on the next frame.

---

## Getting the Gift

The Inner Fire must be *found*, not chosen at a menu.

- At campaign start a prompt appears asking if the fire has always been there. Accepting grants the Gift.
- The Gift can also arrive through certain in-game events (aging, bloodline, encounters).

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
2. **Input form keys** — each press adds one count of the chosen form (e.g. three W presses = Blast, formCount 3).
3. **Press Break (X / L3).** The input switches to the effect phase.
4. **Input effect keys** after Break.
5. **Release the focus key.** The spell fires.

Different form types may be mixed freely before Break — all of them fire simultaneously when you release the focus key.

The buffer is shown in brackets in the message log while you hold the focus key: `[ UUU ▷ UU ]` means Blast formCount=3, Flame effect count=2.

---

## Spell Forms (before Break)

Each key press adds one count. More counts = stronger or larger effect. Mix form types freely — all fire at once.

| Key | Arrow | Form | What it does |
|-----|-------|------|--------------|
| W | ↑ | **Blast** | Forward cone. Range = formCount × 2.5 m (min 4 m). |
| A | ← | **Wave** | A gridSize × gridSize wall of fire advancing forward. Range = max(3, formCount × 2 − 1) m. GridSize = 3 + max(0, (formCount − 5) / 5). |
| D | → | **Barrier** | A wall of stationary fire nodes perpendicular to your facing. One node per press, spaced 1.5 m apart. Cast again to release (dispelling costs no days). |
| S | ↓ | **Burst** | A circle centred on the caster. Radius = formCount × 2.5 m (min 2 m). |

### Multi-form example

`WW SS X UUU` — Blast (5 m) + Burst (5 m) simultaneously, 75 fire damage to enemies. 7 inputs = 2 days cost.

---

## Effects (after Break)

Two effect types. Each key press adds one count. Damage and Restore may be combined.

| Key | Arrow | Effect | Per count | Targets |
|-----|-------|--------|-----------|---------|
| W | ↑ | **Damage** | 25 fire damage | Enemies |
| A | ← | **Damage** | 25 fire damage | Enemies |
| D | → | **Damage** | 25 fire damage | Enemies |
| S | ↓ | **Restore** | 15 healing | Allies (Burst also heals caster) |

### Mixed effect

You can use both Damage and Restore in the same cast. Each form fires its effect against the appropriate team: enemies take damage, allies receive healing.

---

## Aging Cost

Every spell draws on your lifespan. The cost scales with total inputs (form + effect presses combined), capped at 2 days:

| Total inputs | Cost |
|--------------|------|
| 1–3 | 1 day |
| 4+ | 2 days |

The **Tempered** talent reduces the cost by 1 day (minimum 0). The **Resonance** talent gives a 1-in-4 chance that any cast costs nothing.

### Ashen

At age 100 a prompt appears: *The Last Ember*. You may:

- **Take the cold** — become Ashen. Aging stops permanently. Every cast instead raises your criminal rating by `cost × 5`. You are expelled from your kingdom. Your appearance changes: ash-white hair.
- **Let it end** — die of old age.

Ashen mages are immune to the aging cost but accumulate notoriety with every cast.

### Tournament

Casting **any** spell during a tournament **kills and disqualifies you instantly**.

---

## Talents

Talents are learned through the grimoire (Alt+X → *Talents*). The **Gift** is free. Each subsequent talent costs **focus points**: the first 3 purchased cost 1 point each; 4th onward costs 2 points. Maximum cost per talent is 2.

The talent menu groups talents into three categories: **Passive**, **Enchantment**, and **Spell**.

### Passive

| Talent | Effect |
|--------|--------|
| **Gift** | You carry the fire. Battle casting enabled. |
| **Tempered** | Each battle cast costs 1 fewer day (minimum 0). |
| **Resonance** | 1-in-4 campaign map casts cost no days. |
| **Ember** | 5% chance per battle kill to restore 1 day of youth. |
| **Harvest** | Executing a captured lord restores 100 days of youth. |
| **Reap** | Raiding a village restores 5 days (7-day cooldown). Each discarded prisoner has a 5% chance to restore 1 day. Marks you. |
| **Kinship** | +10 relations with other mages; relation cannot fall below 0 (neutral) with them. |

### Enchantment

Enchantments add automatic side effects to Damage or Restore casts. They fire every time the trigger effect is used in battle.

**Damage enchantments** (trigger: Damage effect on enemies):

| Talent | Effect |
|--------|--------|
| **Scatter** | Blasts enemies backward. Push distance = 4 m per Damage input. |
| **Smoulder** | Scorches enemy morale. Morale loss = 12 per Damage input. |
| **Bewilder** | Random effect on non-hero enemies — instant rout, force charge, dismount (cavalry only), or morale fractured to 25%. |

**Restore enchantments** (trigger: Restore effect on allies):

| Talent | Effect |
|--------|--------|
| **Ashveil** | Grants allies brief magic immunity. Duration = 3 s per Restore input. |
| **Cinder Shell** | Hardens allies, reducing incoming damage for 8 s. Protection = 5% per Restore input, max 50%. |
| **Hearthlight** | Lifts allied morale. Morale boost = 12 per Restore input. |

### Spell (campaign map)

These are cast from the grimoire on the campaign map. Each costs 1 day (or criminal rating if Ashen). Resonance applies.

| Talent/Spell | Effect |
|--------------|--------|
| **Subjugate** | Your largest prisoner group yields and joins your ranks. |
| **Rejuvenate** | Up to 8 wounded soldiers per type across your roster recover. |
| **Kindle** | Party morale +40 and up to 5 wounded soldiers are roused. |
| **Quicken** | Grain grows proportional to party size (50–200 measures). |
| **Unsettle** | Nearest enemy party within 100 units loses 35 morale. |
| **Wither** | Nearest enemy village loses 20% of its hearth. |
| **Clairvoyance** | +40 influence, or +1000 gold if not in a kingdom. |
| **Curse** | 5–12 soldiers in the nearest enemy party are wounded or killed, and their morale breaks. |

---

## NPC Mage Lords

At campaign start roughly 20% of lords are seeded as mages by `ColourLordRegistry`. A smaller fraction are **Ashen lords** — they cast with no aging cost, have a much shorter cooldown, and use more aggressive spell combinations.

### Battle notifications

When an enemy mage lord casts in battle, a message appears in the combat log describing the working. Colour is purple for ordinary lords, blue-grey for Ashen.

### Aging cost (NPC)

NPC lords age after every battle in which they cast. The cost scales with the power of spells used:

```
aging days = max(1, total inputs / 4)
```

`total inputs` is the sum of all form and effect presses across all spells cast in the battle. A single blast at formCount 2 + 2 damage effects (4 inputs) costs 1 day. Two heavy Ashen blasts (6 inputs each, 12 total) cost 3 days. Ashen lords do not age regardless.

Lords die of old age when they reach 100. Their deaths are announced in the campaign log.

### Battle AI

NPC lords follow a priority order each tick:

1. **Heal burst** if HP < 30% (Restore burst centred on self; also heals caster).
2. **Heal burst** for allies below 50% HP within 15 m.
3. **Attack** — Burst (when surrounded by 3+ enemies) or Blast (enemies in forward cone). Ashen lords use heavier recipes and roll from a wider attack set.

Lords who have been assigned enchantment talents apply them automatically — a lord with Scatter will fling enemies backward on every damage hit; one with Hearthlight boosts allied morale on every heal.

Spell power by lord type:

| Lord type | Typical formCount | Notes |
|-----------|-------------------|-------|
| Regular mage lord | 2 | Blast or Burst, 1–2 damage counts |
| Ashen lord | 2–3 | Heavier combos; wider attack set; always has Scatter |

Cooldowns by personality:

| Lord type | Cooldown |
|-----------|----------|
| Default | 25 s |
| Impulsive (Calculating < 0) | 15 s |
| Calculating (Calculating > 0) | 35 s |
| Ashen lord | 6 s |

Lords wait 12 seconds at battle start before their first cast.

---

## Bandit Mages

About 4% of eligible bandit units carry a stolen fragment of the fire. They cast once per 18 seconds. **The fire punishes those who borrow it without the gift** — after each cast there is a chance the caster burns out and dies instantly.

Spell power and burnout chance scale with the unit's training:

| Tier | Troop types | formCount | Burnout chance |
|------|-------------|-----------|----------------|
| Untrained | Looter | 1 | 35% |
| Bandit | forest_bandit, sea_raider, mountain_bandit, steppe_bandit, desert_bandit | 2 | 25% |
| Cultist | Fire Worshippers / Ashen Spawn | 3 | 15% |

Each bandit mage type has a title shown in the combat log:

| Troop | Title |
|-------|-------|
| Looter | Fire Prophet |
| Forest bandit | Hedge Witch |
| Sea raider | Ashen Caller |
| Mountain bandit | Ash Shaman |
| Steppe bandit | Wind Binder |
| Desert bandit | Ember Prophet |

### Fire Worshippers & Ashen Spawn

Roughly 10% of newly created Looter and forest bandit parties are renamed **Fire Worshippers**. Roughly 10% of sea raider and mountain bandit parties become **Ashen Spawn**. Both are guaranteed at least one mage caster and use the strongest bandit-tier spells (formCount 3, 15% burnout).

---

## Battlefield Events

Occasionally a battle begins under cursed conditions. Each event rolls independently; most battles have none.

| Event | Chance | Effect |
|-------|--------|--------|
| **Cinder Rain** | 10% | Every non-Ashen agent takes 5 damage every 20 seconds. |
| **Ember Tithe** | 7% | Every Ashen agent takes 5 damage every 20 seconds. |
| **The Rising** | 12% | Spawns 4 units on the Ashen side every 30 seconds (only if Ashen side present). |
| **Dread** | 8% | All non-Ashen agents lose 30 morale (fires once, 5 s after battle start). |
| **Last Light** | 5% | Sets time-of-day to midnight (fires once). |
| **Ashen Ground** | 7% | All mounted agents are dismounted every 20 seconds. |
| **Frenzy** | 7% | Charge order issued to every formation on both sides every 20 seconds. |

Expected events per battle: ~0.5 on average. About 60% of battles have no events; having two at once is rare (~5%).

Active events are announced in the message log at battle start.

---

## The Ashen Kingdom

The Ashen are a faction of lords who refused to let their fire die. They chose the cold instead. They do not age, they do not negotiate, and they are permanently at war with every other kingdom.

### Ashen settlements

At campaign start the following settlements are Ashen. Their clans are moved into the **Ashen Kingdom** automatically. Any settlement lost to another faction is reclaimed on the next daily tick.

**Core cities:** Tyal, Sibir, Baltakhand, Amprela

**Ashen castles and towns:** Urikskala, Kaysar, Dinar, Vladiv, Varnovapol, Tepes, Epinosa, Takor, Khimli, Lochana, Syratos

### Ashen lords

Every hero in an Ashen clan:

- Bears the title **Ashen Lord** or **Ashen Lady**.
- Does not age — birth day is reset daily to keep them near age 35.
- Casts spells with no aging cost and on a short 6-second cooldown.
- Uses heavier, damage-focused spell recipes in battle.
- Always carries the **Scatter** enchantment (cold fire flings enemies backward); 50% chance of **Smoulder** as well.
- Carries **Curse**, **Break Wills**, and **Plague** as map-cast talents.

### Criminal status

Non-Ashen players are treated as permanent criminals (max crime rating) in Ashen lands. Ashen players have their crime rating cleared daily.

### Permanent war

The Ashen never make peace. Any peace deal involving the Ashen kingdom is immediately revoked and war is re-declared.

### NPCs in Ashen settlements

Hero-type NPCs (lords, wanderers) currently residing in an Ashen settlement gradually take on the Ashen appearance. The effect applies to their body properties the next time a scene with them loads.

---

## Campaign World Events

Seven rare events fire randomly on the weekly tick. Each has an independent chance and fires at most once per week.

| Event | Chance/week | Effect |
|-------|-------------|--------|
| **Ashen Plague** | 8% | Wounds the entire garrison of a random settlement; spawns 3 Ashen Spawn parties nearby. |
| **Great Withering** | 10% | Destroys 80% of a random village's hearth, or halves the prosperity of a random city. |
| **Ashen March** | 5% | Spawns 6 Ashen Spawn parties (strength ≥ 70) across a random non-Ashen kingdom. |
| **Long Night** | 3% | Forces the mod's light-level logic to Dark for 7 days. |
| **Ashen Tide** | 3% | A random non-Ashen castle is seized by a random Ashen lord. |
| **Fire Fades** | 1.5% | 50% of non-Ashen lords under age 18 die. |
| **Darkened Roads** | 6% | All caravans of a random kingdom are destroyed. Ashen caravans are immune. |

---

## Building from Source

### Requirements

- .NET SDK 6 or later
- A local Bannerlord installation (the build references the game's DLLs)

### Environment variables

| Variable | Value |
|----------|-------|
| `BannerlordPath` | Path to your Bannerlord root (the folder containing `bin` and `Modules`) |
| `BannerlordBin` | `Win64_Shipping_Client` (Steam, default) or `Gaming.Desktop.x64_Shipping_Client` (Xbox) |

```powershell
$env:BannerlordPath = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
dotnet build src\TheWitheringArt.csproj
```

Output DLL: `src\bin\Debug\AshAndEmber.dll`

The build copies the DLL to `<BannerlordRoot>\Modules\AshAndEmber\bin\<platform>\` automatically on each successful compile.

### Creating a release package

```powershell
$env:BannerlordPath = "..."
.\tools\pack.ps1
```

Produces `dist\AshAndEmber_v<version>.zip` with DLLs for both platforms.

---

## Troubleshooting

**"The fire does not stir in you."**
You do not carry the Gift. Start a new campaign and accept the prompt during character creation.

**Spells fire but nothing happens**
You may be in a tournament (casting kills you), in a prisoner state ("You are bound"), or the spell fumbled (mixed form keys before Break).

**Script reports "Could not auto-detect your Bannerlord installation"**
Pass the path manually: `.\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"`

**The mod list does not show Ash and Ember**
Verify `SubModule.xml` is at `<BannerlordRoot>\Modules\AshAndEmber\SubModule.xml` exactly. Restart the launcher after copying.

**Game crashes on load**
The DLL must match the game's .NET runtime. Check the Releases page for a compatibility update if the crash started after a game patch.

**Ashen settlements show as unclaimed for the first day**
This is expected. Ownership is asserted on the first daily tick (~24 in-game hours). Settlements show Ashen colours from day 2 onward.
