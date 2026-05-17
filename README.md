# Colours of Calradia — v2.0

A Mount & Blade II: Bannerlord magic overhaul built around six colour schools, named mage lords, persistent magical units, and a personality system that shifts with every cast.

---

## Package Structure

```
ColoursOfCalradia/
├── SubModule.xml                    mod manifest
├── ModuleData/
│   ├── items.xml                    (reserved for future spell items)
│   └── troops.xml                   (reserved for future mage troops)
├── src/                             ~4 400 lines across 18 source files
│   ├── MagicSystem.cs               module entry point + mission behaviour
│   ├── SchoolData.cs                ColorSchool enum + ColorSchoolData (glow colours, traits, attributes)
│   ├── SpellDatabase.cs             SpellDatabase — 18 spell entries + Find/BySchool helpers
│   ├── ColourKnowledge.cs           player school knowledge, limitations, and madness tracking
│   ├── ActiveEffects.cs             ActiveEffectManager — timed callbacks for spell durations
│   ├── MagicInputHandler.cs         keyboard combo detection (U/L/R buffer → spell execution)
│   ├── CampaignBehavior.cs          MagicCampaignBehavior — school selection, daily effects, AI cast
│   ├── Spells/                      spell implementations (partial SpellEffects)
│   │   ├── SpellEffects.cs          core partial: fields, helpers, Execute switch, battle commands
│   │   ├── BlastSpells.cs           UU-prefix spells (Crimson Torrent … Grey Harvest)
│   │   ├── SelfSpells.cs            RL-prefix spells (Scarlet Ward … Grief's Veil)
│   │   └── CreateSpells.cs          LR-prefix spells (Cinder Burst … Hollow Gaze)
│   ├── Visual/                      visual + movement systems (partial SpellEffects)
│   │   ├── AreaEffects.cs           AreaEffect nested class, tick/clear, spawn area light
│   │   ├── GlowSystem.cs            per-school contour glow + cast sound
│   │   ├── MoveSystem.cs            smooth agent movement queue (push/pull lerp)
│   │   └── NamePrefixes.cs          [ROYGBP] hero name prefixes applied during battle
│   ├── AI/                          lord and unit AI
│   │   ├── ColourLordRegistry.cs    lord → school assignments (seeded per faction)
│   │   ├── ColourLordAI.cs          per-tick lord spell casting in battle
│   │   └── ColourUnitRegistry.cs    magical unit tracking and per-tick effects
│   └── TheWitheringArt.csproj       build project (outputs ColoursOfCalradia.dll)
├── tests/
│   ├── ColoursOfCalradia.Tests.csproj  NUnit test project (net472)
│   └── PureLogicTests.cs            pure logic tests (no game engine calls)
└── README.md                        this file
```

---

## Installation

### Requirements
- Mount & Blade II: Bannerlord (any platform — Steam or Xbox/Game Pass)
- .NET SDK 6 or later (for building) — download from https://dotnet.microsoft.com/download

### Step 1 — Locate your Bannerlord directory

| Platform | Default path |
|----------|-------------|
| Steam | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` |
| Xbox / Game Pass | `C:\XboxGames\<GameGUID>\Content` — open `C:\XboxGames\` in Explorer to find the folder with the GUID name |

This directory is referred to as `<BannerlordRoot>` below.

### Step 2 — Copy the mod folder

Copy the entire `ColoursOfCalradia` folder into:

```
<BannerlordRoot>\Modules\ColoursOfCalradia\
```

The result should look like:

```
<BannerlordRoot>\Modules\ColoursOfCalradia\
    SubModule.xml
    ModuleData\
    src\
    README.md
```

### Step 3 — Build the DLL

See the **Build** section below. This produces `ColoursOfCalradia.dll`.

### Step 4 — Place the DLL

Copy the built DLL into the correct subfolder inside the mod:

| Platform | Destination |
|----------|-------------|
| Steam | `<BannerlordRoot>\Modules\ColoursOfCalradia\bin\Win64_Shipping_Client\ColoursOfCalradia.dll` |
| Xbox / Game Pass | `<BannerlordRoot>\Modules\ColoursOfCalradia\bin\Gaming.Desktop.x64_Shipping_Client\ColoursOfCalradia.dll` |

Create the `bin\<subfolder>\` directory if it does not exist.

### Step 5 — Enable in the launcher

1. Launch Mount & Blade II: Bannerlord.
2. Click **Mods** in the launcher.
3. Find **Colours of Calradia** in the list and tick the checkbox.
4. Click **Play**.

> **Load order:** no special position required. The mod has no dependencies on other mods.

---

## Build

Set two environment variables before building:

| Variable | Steam | Xbox / Game Pass |
|----------|-------|-----------------|
| `BannerlordPath` | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` | `C:\XboxGames\<GameGUID>\Content` — the GUID varies; find it under `C:\XboxGames\` |
| `BannerlordBin` | `Win64_Shipping_Client` | `Gaming.Desktop.x64_Shipping_Client` |

`BannerlordBin` defaults to `Win64_Shipping_Client` if omitted, so Steam users only need to set `BannerlordPath`.

```powershell
# Steam
$env:BannerlordPath = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
dotnet build src\TheWitheringArt.csproj

# Xbox / Game Pass (replace the GUID with the folder under C:\XboxGames\)
$env:BannerlordPath = "C:\XboxGames\<GameGUID>\Content"
$env:BannerlordBin  = "Gaming.Desktop.x64_Shipping_Client"
dotnet build src\TheWitheringArt.csproj
```

Output DLL: `src/bin/Debug/ColoursOfCalradia.dll`

The project targets `.NET Framework 4.7.2` to match the game's runtime. Logic is split across 18 source files; `MagicSystem.cs` is the module entry point.

---

## Starting the Mod — Colour Selection

When a new campaign starts you will see a multi-selection screen titled **"The Colours of Calradia"**. Hover each colour to read its school flavour text, attribute penalty, and two permanent limitations.

- Select any combination of colours (including none — you walk an uncoloured path).
- Your chosen colours are **permanent** for that playthrough.
- Each colour you pick reduces one attribute by 1 and locks in two permanent limitations.
- You may select as many as all six colours simultaneously.

### Adjacency and Madness

The six colours form a ring: **Red — Orange — Yellow — Green — Blue — Purple** (wrapping back to Red).

If you pick two or more colours that are **not contiguous** on the ring (e.g. Red and Yellow, skipping Orange), the incompatible natures fracture your sense of self. **Madness** is applied at game start: two random personality traits shift by ±1, chosen from Mercy, Valor, Honor, Generosity, and Calculating.

Contiguous selections (e.g. Red + Orange + Yellow, or Blue + Purple + Red wrapping around) carry no madness penalty from adjacency — but a second rule applies regardless:

**Picking more than 4 colours** triggers **The Fracture** in addition to any adjacency madness:
- Two further random traits shift at game start (stacking on top of adjacency madness if applicable).
- Every **week** thereafter, all five personality traits are randomised completely (each set to a random value between −2 and +2) with no recovery. No mind can hold five colours whole.

### Madness and Battle Orders

Madness does not stay inside your head — it bleeds into command. Whenever you issue a formation order in battle, there is a chance it fires differently from what you intended:

| Cause | Scramble chance |
|-------|-----------------|
| Non-contiguous colour selection | 5 % |
| 5 colours chosen | 10 % |
| 6 colours chosen | 20 % |

When an order is scrambled, the formation receives a random command (Charge or Halt) instead of the one you issued. A message appears in the log. These chances stack independently of each other — a player with 5 non-contiguous colours is subject to the 10 % rule (the higher threshold applies).

---

## The Six Colour Schools

| Colour | Identity | Attribute penalty | Personality drift |
|--------|----------|-------------------|-------------------|
| **Red** | Blood Price — violent, fiery | Control | Calculating −1 |
| **Orange** | Generous Hunger — joyful, open-handed | Intelligence | Generosity +1 |
| **Yellow** | The Fearful Eye — dread, revulsion | Social | Mercy −1 |
| **Green** | Gentle Burden — kind, restorative | Endurance | Mercy +1 |
| **Blue** | Scholar's Weight — cold, ordered | Vigor | Calculating +1 |
| **Purple** | The Waning Art — melancholic, fading | Cunning | Valor −1 |

### Limitations by School

Each school carries two permanent limitations. The first category (A) applies every cast in battle; the second (B) may add a passive daily effect or a further cast cost.

**Red — Blood Price**
- **(A) Furious:** Each Red spell automatically issues a Charge order to all your formations.
- **(B) Blood Price:** Each Red spell opens a wound on the caster — 8 HP self-damage.

**Orange — Generous Hunger**
- **(A) Overindulgent:** Your party consumes food faster and army upkeep is higher. 2 food units are drained daily.
- **(B) Generous Flood:** Each Orange spell briefly seizes your body — you stagger in random directions for a moment, lurching unpredictably across the field.

**Yellow — The Fearful Eye**
- **(A) Paranoia:** Each Yellow spell costs your party 8 morale. The fear bleeds inward.
- **(B) Blurred Judgment:** Each Yellow spell increases your criminal rating by 3 in the relevant kingdom. You begin to see threats everywhere.

**Green — Gentle Burden**
- **(A) Pacifist:** You cannot use Green magic while wielding a weapon. Sheathe it first.
- **(B) Gentle Burden:** Each killing blow you land costs you 8 HP — Green magic does not forgive the taking of life.

**Blue — Scholar's Weight**
- **(A) Scholar's Weight:** Each Blue spell makes your equipment feel heavier — your maximum movement speed decreases with every cast and does not recover until the battle ends. Up to 6 stacks; at the cap you slow to a crawl.
- **(B) Heavy Knowledge:** Cerulean Mirror shields you from spells and magic effects for 40 seconds — but steel still finds you.

**Purple — The Waning Art**
- **(A) Waning Cost:** Each Purple spell ages the caster by approximately 2 days — the grey draws time inward, quietly.
- **(B) The Slow Unravelling:** Each Purple cast quietly reduces the caster's fertility by 5 percentage points (minimum 1%). The current level is shown in the message log after every cast. It never reaches zero — but it never comes back. This value persists across saves.

---

## Casting — Controls

### Requirement: Light

**Magic is bound to available light.** The strength of light in your current environment determines whether a spell can be woven at all.

| Light level | When | Effect |
|-------------|------|--------|
| **Bright** | 07:00–19:00 outdoors | Normal casting |
| **Dim** | 05:00–07:00 (dawn) and 19:00–22:00 (dusk); hideout interiors | 33 % chance the spell unravels on cast with no effect |
| **Dark** | 22:00–05:00 (deep night); caves, mines, dungeons | Cannot cast at all |

NPC mage lords require full daylight and will not cast during dim or dark conditions.

### Keyboard

1. Hold **Left Alt** to enter spell mode.
2. Input any 4-key combo using **W / A / S / D**, which map to:
   - **W** → U (Up)
   - **A** → L (Left)
   - **D** → R (Right)
   - **S** → opens the spellbook (whether the buffer is empty or not). **S never appears in a spell combo.**
3. Release **Left Alt** to fire.

### Gamepad

1. Hold **Left Trigger** to enter spell mode.
2. Push the **right stick** in a direction:
   - Stick Up → U
   - Stick Down → D
   - Stick Left → L
   - Stick Right → R
3. Press **L3 (left stick click)** to open the spellbook (instead of casting).
4. Release **Left Trigger** to fire.

### Spellbook

Press **S** (keyboard, empty buffer) or **L3** (gamepad, while holding Alt/LT) to cycle through your known spells. The spellbook shows each spell's name, combo, and effect.

---

## The Spell System

All 18 battle spells follow a strict **Form + Colour** combo structure:

- The **first two characters** encode the *form* (how the spell behaves).
- The **last two characters** encode the *colour school*.

### Forms

| Prefix | Form | Shape |
|--------|------|-------|
| `UU` | **Blast** | Cone in front of the caster |
| `RL` | **Self** | Aura or effect centred on the caster |
| `LR` | **Create** | Persistent area effect placed on the battlefield |

### Colour Suffixes

| Suffix | School |
|--------|--------|
| `RR` | Red |
| `RU` | Orange |
| `LU` | Yellow |
| `LL` | Green |
| `UL` | Blue |
| `UR` | Purple |

---

## Spell List

### Blast Spells (UU prefix) — Cone effects

| Spell | Combo | School | Effect |
|-------|-------|--------|--------|
| **Crimson Torrent** | `UURR` | Red | 40 damage to all enemies in a forward cone (15 m); pushes them back 6 m (smooth lerp over 0.4 s). |
| **Golden Tide** | `UURU` | Orange | 12 damage to cone enemies (15 m); forces all enemy formations to Charge. |
| **Tide of Dread** | `UULU` | Yellow | 14 damage to cone enemies (15 m); drains 55 morale from each. |
| **Verdant Surge** | `UULL` | Green | Heals allies in the cone (15 m) for up to 15 HP each. Player and enemies are not affected. |
| **Azure Arrest** | `UUUL` | Blue | 12 damage to cone enemies (15 m); halts all enemy formations; dismounts riders. |
| **Grey Harvest** | `UUUR` | Purple | Instantly kills one random creature in the cone (15 m). |

### Self Spells (RL prefix) — Caster-centred effects

| Spell | Combo | School | Effect |
|-------|-------|--------|--------|
| **Scarlet Ward** | `RLRR` | Red | Absorbs the next single physical blow — one strike, then the ward shatters. Expires after 8 s if nothing hits. |
| **Warm Beacon** | `RLRU` | Orange | Pulls all allies within 18 m to a ring around the caster (smooth lerp). |
| **Nausea Bloom** | `RLLU` | Yellow | Persistent 30 s aura (radius 8 m) that deals 15 damage every 2 s to all nearby creatures. |
| **Verdant Touch** | `RLLL` | Green | Heals the caster for 20 HP. |
| **Cerulean Mirror** | `RLUL` | Blue | 40 s magic immunity — spells and magical area effects cannot harm you. Physical attacks still connect. |
| **Grief's Veil** | `RLUR` | Purple | The grey folds you from sight for 12 s — you become invulnerable and nearby enemy formations halt momentarily. They do not flee; they simply lose track of you. |

### Create Spells (LR prefix) — Persistent battlefield effects

| Spell | Combo | School | Effect |
|-------|-------|--------|--------|
| **Cinder Burst** | `LRRR` | Red | Instant 45 damage to all creatures within 10 m. |
| **Golden Snare** | `LRRU` | Orange | Places a golden patch (radius 10 m) at the caster's feet. The first enemy formation to step into it receives one random command — **Halt**, **Charge**, **Dismount**, or **Scatter** — then the trap vanishes. Expires after 60 s if untriggered; cast again to dismiss early. |
| **Creeping Dread** | `LRLU` | Yellow | Toggle: releases a wandering cloud (radius 7 m) that roams the field, dealing 25 damage every 2 s to creatures it passes through. Changes direction randomly every ~3 s. Cast again to dismiss. |
| **Emerald Font** | `LRLL` | Green | Toggle: creates a healing circle (radius 8 m) that restores 10 HP every 2 s to all within it — friend and foe alike. Cast again to dismiss. |
| **Sapphire Bastion** | `LRUL` | Blue | Raises three pillars of force (radius 3 m each) in a line perpendicular to your facing, forming a wall ~13 m wide. Creatures that cross into any pillar are pushed outward every 0.5 s. Fades after 2 minutes. Cast again to dismiss early. |
| **Hollow Gaze** | `LRUR` | Purple | Pins one random nearby non-hero enemy (within 15 m) into a catatonic state — they stand still and do nothing. The effect is maintained until cancelled. Cast again to release them. |

### Notes on Create Spells

- **Toggled effects** (Golden Snare, Creeping Dread, Emerald Font, Hollow Gaze) are cancelled by casting the same spell again.
- **Timed effects** (Nausea Bloom at 30 s, Sapphire Bastion at 2 minutes) expire automatically and display a message when they fade.
- **Area glows**: Affected agents pulse in the school's colour on each effect tick (approximately every 2 s for most effects, every 0.5 s for Sapphire Bastion). Glow is not applied every frame to avoid performance cost.
- **Smooth movement**: Push and pull effects (Crimson Torrent, Warm Beacon, Sapphire Bastion) move agents using a smoothstep lerp over 0.4 s rather than instant teleportation.

---

## Visual Effects

### Known Limitations

The mod operates at the game-logic layer only and cannot access the engine's animation or particle systems. As a result:

- **No cast animations** — casting a spell produces no hand, body, or weapon animation on the player or NPCs.
- **No hit-block animations** — Scarlet Ward absorbing a blow, or Cerulean Mirror deflecting a spell, has no dedicated animation.
- **No area-effect visuals** — Creeping Dread, Nausea Bloom, and other area effects have no visible cloud or ground texture. Only the agents inside the area glow in the school's colour.
- **No projectile effects** — Blast spells fire instantly; there is no visible projectile.

All visual feedback is limited to **agent glow outlines** (colour pulses on affected characters) and **message log text**.

---

Each school has a distinct colour used for both combat messages and agent glow:

| School | Glow / Message Colour |
|--------|-----------------------|
| Red | Bright red |
| Orange | Orange |
| Yellow | Bright yellow |
| Green | Green |
| Blue | Blue |
| Purple | Purple |

When a spell affects an agent, that agent is outlined in the casting school's colour for 1.5–3 s depending on the effect. The caster themselves glow when activating Self or Create spells.

---

## Personality Drift

Every **25 casts** of the same school shifts one personality trait — but only if the trait actually has room to move (clamped at ±2). A message appears only when the trait moves.

| School | Trait | Direction |
|--------|-------|-----------|
| Red | Calculating | −1 |
| Orange | Generosity | +1 |
| Yellow | Mercy | −1 |
| Green | Mercy | +1 |
| Blue | Calculating | +1 |
| Purple | Valor | −1 |

---

## Tournament Rule

Casting **any** spell during a tournament **immediately disqualifies you** — your character is killed and removed from the match with a warning message.

---

## Level-Up Learning

At character levels 10, 20, 30 … (while you have at least one school but fewer than six), a prompt appears offering you one new colour to learn. You may decline. Accepting applies the new school's attribute penalty and limitations immediately.

---

## Mage Lords (NPC)

### Seeding

- At campaign start, one lord per faction is designated as the **archmage** and receives 4 colour schools. Remaining lords each have a random chance of receiving 1–3 schools (10 % for 3, 10 % for 2, 15 % for 1; otherwise none).
- Companion heroes have a **10 % chance** to be granted 1–2 colours when they join your party.
- When a mage lord dies, their colours are extinguished. After **7 days**, the colours pass to a randomly selected younger lord (under 50) in the same kingdom. If no suitable candidate exists, the timer retries every day until one becomes available — every kingdom retains at least one mage lord.

### Battle AI

NPC mage lords cast in battle using a priority-driven AI:

1. **Self-heal** with Verdant Touch if below 35 % HP (Green lords only, if not wielding a weapon).
2. **8 % random wild cast** — fires a random applicable school spell.
3. **Swarm response** — if 3+ enemies within 8 m, prefer Cinder Burst (Red) or Grey Harvest (Purple).
4. **Cone attack** — Crimson Torrent (Red) or Azure Arrest (Blue) if enemies are in the forward arc.
5. **Ally healing** — Verdant Surge (Green) if allies in the forward arc are wounded.
6. **Morale drain** — Tide of Dread (Yellow).
7. **Reinforcement** — Calling (Orange) if outnumbered; Warm Beacon as fallback.

All NPC battle spells require light — the same three-tier rule as the player applies. In dim conditions NPCs have a 33 % chance of their cast failing silently. NPC lords have a 12-second cast cooldown between spells.

### Campaign Map Spells

Each mage lord has a **20 % daily chance** to cast a campaign-map spell. At most **3 lord map-casts fire per day** across the entire world to keep the log readable. Each lord has a cooldown of 6–9 days after casting.

| School | Effect A (50 %) | Effect B (50 %) |
|--------|-----------------|-----------------|
| **Red** | Own party morale +10 (*Bloodlust*) | Random enemy village hearth ×0.8 (*Carnage*) |
| **Orange** | Nearest friendly village hearth ×1.05 (*Celebrate*) | Transfer 1–2 troops from rival lord to own party (*Bribe*) |
| **Yellow** | Random enemy lord renown −10 (*Fade*) | Random enemy party morale −15 (*Melancholy*) |
| **Green** | Heal up to 3 wounded in own party per troop type (*Rejuvenate*) | Add 5 grain to own inventory (*Crops*) |
| **Blue** | Own clan influence +8 (*Schemes*) | Enemy clan influence −8 (*Plots*) |
| **Purple** | Enemy influence −5, enemy morale −10 (*Curse*) | Add 10 recruits to own party (*Bind*) |

---

## Named Magical Units

### Seeding

Named magical soldiers are seeded sparingly to keep large-battle performance stable:

| Party Type | Condition | Units | Daily reseed chance |
|------------|-----------|-------|---------------------|
| Lord party | 200–349 troops | 1 unit (1 colour) | 5 % |
| Lord party | 350+ troops | 2 units (1 colour each) | 5 % |
| Bandit party | 40+ troops | 1 unit (1 colour) | 1 % |

The named lord or companion in a party is always the primary magic user; the unit soldiers are rare additions who only appear in the largest armies.

When a unit dies in battle, they re-enter a **3–5 day respawn queue** and re-emerge in a party of the same type: lord-party units respawn in the next qualifying large lord party (200+ troops); bandit units respawn in the next qualifying bandit party (40+ troops). If no suitable party is available, the queue retries every 5 days until one appears.

Each named unit receives 1 colour school. Their names are generated from a pool of 20 first names combined with school-specific suffixes:

| School | Suffixes |
|--------|----------|
| Red | the Ember, Bloodhanded, Pyremark |
| Orange | the Bright, Goldenvoiced, the Warm |
| Yellow | the Pale, of Dread, the Craven |
| Green | the Tender, Root-spoken, the Verdant |
| Blue | the Still, Coldwater, the Patient |
| Purple | the Hollow, the Grey, the Fading |

### Battle Behaviour

Named magical units cast spells in battle using the same AI rules as mage lords, with a **20-second cooldown** between casts and a **0.5 s AI evaluation tick**. The same light-level restriction applies — dim conditions carry a 33 % silent failure chance.

### Campaign Map Effects

Each named unit has a **10 % daily chance** to trigger a minor campaign map effect. At most **2 unit map-casts fire per day** total. After casting, a unit cannot cast on the map again for 3–5 days. Effects are generally positive for the unit's own party (morale, experience, food).

---

## Children & Companions

### Children

Children of the main hero born during the campaign have a **30 % chance** to inherit magical colours. When they do:

- They always inherit at least one of the parent's schools.
- Their total number of schools equals the parent's count ±1, clamped to 1–6.
- Any remaining slots are filled from the pool of colours the parent does not already hold.

### Companions

When a companion joins your party they have a **10 % chance** to carry colour gifts:

- 10 % of that chance: 2 colours.
- 20 % of that chance: 1 colour.
- Otherwise: no colours.

A message announces how many colours they carry when they join.

---

## Colour Ring Reference

```
         Red (0)
        /        \
  Purple (5)    Orange (1)
      |               |
  Blue (4)      Yellow (2)
        \        /
         Green (3)
```

Adjacent colours on the ring do not cause Madness. Non-adjacent combinations (e.g. Red + Yellow, or Green + Red) do.

---

## Campaign Map Spells

Two additional spell forms work exclusively on the campaign map and require the same daytime light conditions as battle spells. They cannot be cast during battles or while in a settlement menu.

### Forms

| Prefix | Form | Keys |
|--------|------|------|
| `UL` | **Affect** | W then A (U then L) |
| `LU` | **Invoke** | A then W (L then U) |

### Affect Spells (UD prefix) — situation-based

Each Affect spell is tied to a specific situation or resource. No cooldowns — all costs are mechanical.

| Spell | Combo | School | Effect | Cost / Limiter |
|-------|-------|--------|--------|----------------|
| **Ember Drive** | `ULRR` | Red | +100×power gold during a village raid or hideout assault | −10% current HP per cast; blocked at ≤5 HP |
| **Shared Feast** | `ULRU` | Orange | Consume food → party morale +8×power | Food cost doubles each cast within the day (1→2→4→8…), resets at midnight |
| **Dread Whisper** | `ULLU` | Yellow | Nearest enemy party loses 15×power morale | Self-morale drain escalates +5 per cast within the day (5→10→15…) plus Yellow limitation −8 |
| **Verdant Hour** | `ULLL` | Green | Produce 1–4 grain | −5% current HP per cast; blocked at ≤5 HP |
| **Scholar's Investment** | `ULUL` | Blue | Spend 500 gold → +15×power influence | −3 clan renown per cast; kingdom required |
| **Grey Veil** | `ULUR` | Purple | Scatter nearby enemy parties (radius 2); enemies lose your trail | Age scales per session: 7→14→21→… days |

### Invoke Spells (LU prefix) — advanced campaign effects

Invoke spells target heroes, rosters, and rival lords directly. No cooldowns — all costs are mechanical.

| Spell | Combo | School | Effect | Cost / Limiter |
|-------|-------|--------|--------|----------------|
| **Bloodprice** | `LURR` | Red | Party morale +20×power | −20% current HP; blocked at ≤5 HP |
| **Muster Call** | `LURU` | Orange | Recruit 2–4 tier-1 troops from nearest friendly settlement | Gold cost 100→200→400 (capped at 400), resets at midnight |
| **Whispered Ruin** | `LULU` | Yellow | Nearest enemy lord (at war) clan renown −8 | −2 own clan renown per cast |
| **Tend the Fallen** | `LULL` | Green | Heal 3+(power×2) wounded troops in own party | −5% current HP per cast; blocked at ≤5 HP |
| **Counter-Scheme** | `LUUL` | Blue | Nearest enemy lord clan influence −8 | −300 gold per cast; kingdom required |
| **Wither's Touch** | `LUUR` | Purple | Nearest enemy lord: party morale −15, clan renown −8 | 14 days aging (flat) per cast |

### Notes

- **Orange Muster Call** gold cost is capped at 400 — once at the cap, each cast costs 400 gold for as many recruits as your power allows. The cap resets each campaign day.
- **Yellow Whispered Ruin** and **Dread Whisper** require an active war with the target's faction. **Counter-Scheme** and **Wither's Touch** work against any non-player faction.
- **Blue** campaign spells both require kingdom membership — influence has no meaning outside one.
- **Purple Grey Veil** session scaling resets on load (intentional — in-memory only). **Wither's Touch** aging is flat at 14 days regardless of how many times you cast it in a session.
- **Red** HP costs apply to campaign HP, which carries into the next battle. Ember Drive during a raid means you fight the battle with reduced health.
