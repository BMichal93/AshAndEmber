# Colours of Calradia — v1.2.0.0

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
│   ├── SpellDatabase.cs             SpellDatabase — 30 spell entries (18 battle + 12 campaign map) + Find/BySchool helpers
│   ├── ColourKnowledge.cs           player school knowledge, limitations, and madness tracking
│   ├── ActiveEffects.cs             ActiveEffectManager — timed callbacks for spell durations
│   ├── MagicInputHandler.cs         keyboard combo detection (U/L/R/D buffer → spell execution)
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
│   │   ├── ColourLordRegistry.cs    lord → school assignments (seeded per faction), Prism lord, relationships
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

- **OS:** Windows 10 or Windows 11
- **Game:** Mount & Blade II: Bannerlord — Steam or Xbox / Game Pass
- **Version compatibility:** built against Bannerlord's current `.NET Framework 4.7.2` runtime; compatible with any game version that uses the same runtime

No build tools are needed for a normal install.

---

### Step 1 — Download the mod

Download the latest release ZIP from the Releases page. The ZIP contains:

```
ColoursOfCalradia/
    SubModule.xml
    install.ps1
    README.md
    ModuleData/
    bin/
        Win64_Shipping_Client/
            ColoursOfCalradia.dll
        Gaming.Desktop.x64_Shipping_Client/
            ColoursOfCalradia.dll
```

Extract the ZIP to any temporary location (Desktop, Downloads, etc.). You should end up with a single `ColoursOfCalradia` folder.

---

### Step 2 — Install

Choose whichever method you prefer. Both produce the same result.

---

#### Option A — Script install (recommended)

The release ZIP includes `install.ps1`, a PowerShell script that finds your game automatically and copies all files to the correct location.

**2a. Open PowerShell in the extracted folder**

Right-click inside the extracted `ColoursOfCalradia` folder while holding **Shift**, then choose **Open PowerShell window here**. On Windows 11 you may need to select **Show more options** first.

Alternatively: open PowerShell from the Start Menu, then navigate to the folder:

```powershell
cd "$env:USERPROFILE\Downloads\ColoursOfCalradia"
```

**2b. Allow the script to run (first-time only)**

Windows blocks unsigned scripts by default. Run this once per PowerShell session to allow it — it does not permanently change your system policy:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

**2c. Run the installer**

```powershell
.\install.ps1
```

The script automatically checks:
1. The Steam registry key (`HKLM\SOFTWARE\WOW6432Node\Valve\Steam` and `HKCU\Software\Valve\Steam`)
2. The default Steam path (`C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord`)
3. All subdirectories of `C:\XboxGames\` (Xbox / Game Pass installs)

If detection succeeds you will see:

```
Detected Bannerlord at: C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord
Platform bin: Win64_Shipping_Client
Using DLL: ...\bin\Win64_Shipping_Client\ColoursOfCalradia.dll
Installed successfully to: ...\Modules\ColoursOfCalradia
```

**Non-standard install location**

If you installed Bannerlord outside the default Steam library (e.g. a second drive), pass the path explicitly:

```powershell
.\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"
```

For Xbox / Game Pass with a known GUID folder:

```powershell
.\install.ps1 -BannerlordPath "C:\XboxGames\Mount & Blade II- Bannerlord\Content"
```

---

#### Option B — Manual install

**2a. Find your Bannerlord root directory**

| Platform | How to find it |
|----------|---------------|
| Steam (default library) | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` |
| Steam (custom library) | Open Steam → right-click Bannerlord → Manage → Browse local files |
| Xbox / Game Pass | Open `C:\XboxGames\` in Explorer; Bannerlord's folder contains a `Content` subfolder that has a `bin` folder inside it |

The correct root is the folder that contains both a `bin` subfolder and a `Modules` subfolder.

**2b. Copy the mod folder**

Copy the entire `ColoursOfCalradia` folder (the one that contains `SubModule.xml`) into the `Modules` subfolder of your Bannerlord root.

The final structure on disk must look exactly like this:

```
<BannerlordRoot>\Modules\ColoursOfCalradia\
    SubModule.xml
    install.ps1
    README.md
    ModuleData\
        items.xml
        troops.xml
    bin\
        Win64_Shipping_Client\
            ColoursOfCalradia.dll
        Gaming.Desktop.x64_Shipping_Client\
            ColoursOfCalradia.dll
```

> If `SubModule.xml` ends up one level too deep (e.g. `Modules\ColoursOfCalradia\ColoursOfCalradia\SubModule.xml`) the game will not see the mod. Flatten the folder so `SubModule.xml` sits directly inside `Modules\ColoursOfCalradia\`.

---

### Step 3 — Enable the mod in the launcher

1. Launch **Mount & Blade II: Bannerlord**.
2. In the launcher, click **Mods** in the left panel.
3. Find **Colours of Calradia** in the list and tick the checkbox to enable it.
4. Click **Play**.

> **Load order:** no special position is required. Colours of Calradia has no dependencies on other mods and does not conflict with mods that do not touch the magic or personality systems.

---

### Step 4 — Verify the install worked

Once in-game, start a new campaign. Shortly after the character creation screen you will be presented with **"The Colours of Calradia"** school selection menu. If this screen appears, the mod is installed correctly.

If the screen does not appear:

- Confirm the mod is ticked in the launcher mod list.
- Confirm `SubModule.xml` exists at `<BannerlordRoot>\Modules\ColoursOfCalradia\SubModule.xml`.
- Confirm `ColoursOfCalradia.dll` exists under the correct platform bin folder (see the folder tree above).

---

### Troubleshooting

**Script reports "Could not auto-detect your Bannerlord installation"**

Your game is installed in a non-standard location. Pass the path manually:

```powershell
.\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"
```

**Script reports "DLL not found"**

You are running the script from inside the repository rather than from a release ZIP, and the project has not been built yet. Either download the release ZIP (which includes the pre-built DLL) or build from source first (see *Building from Source* below).

**Script reports "cannot be loaded because running scripts is disabled"**

Run the execution policy bypass command before calling the script:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\install.ps1
```

**The mod list in the launcher does not show Colours of Calradia**

The game scans `<BannerlordRoot>\Modules\` for folders that contain a valid `SubModule.xml` at their root. Verify:

1. The folder is named exactly `ColoursOfCalradia` (no spaces, capital C, capital of C).
2. `SubModule.xml` is directly inside that folder, not in a subfolder.
3. Restart the launcher after copying files — it does not hot-reload.

**Game crashes on load**

The DLL must match the game version's .NET runtime. If the game was recently patched and the crash started afterward, check the Releases page for a compatibility update.

---

### Updating the mod

Re-run `install.ps1` from the new release ZIP. It overwrites all existing mod files automatically. No manual cleanup is needed between versions.

---

### Uninstalling

Delete the folder:

```
<BannerlordRoot>\Modules\ColoursOfCalradia\
```

No registry entries, no save-game data, and no other files are created outside this folder. Removing it is a complete uninstall. Existing saves created with the mod active will continue to load but will not present the school selection screen and will have no magic effects.

---

## Building from Source

Required only if you are contributing or packaging a new release. End users do not need this.

### Requirements

- .NET SDK 6 or later — https://dotnet.microsoft.com/download
- A local Bannerlord installation (the build references the game's DLLs)

### Environment variables

| Variable | Steam | Xbox / Game Pass |
|----------|-------|-----------------|
| `BannerlordPath` | `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord` | Path to the `Content` folder — find it under `C:\XboxGames\` |
| `BannerlordBin` | `Win64_Shipping_Client` *(default, can be omitted)* | `Gaming.Desktop.x64_Shipping_Client` |

```powershell
# Steam (BannerlordBin defaults to Win64_Shipping_Client)
$env:BannerlordPath = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
dotnet build src\TheWitheringArt.csproj

# Xbox / Game Pass
$env:BannerlordPath = "C:\XboxGames\<GUIDFolder>\Content"
$env:BannerlordBin  = "Gaming.Desktop.x64_Shipping_Client"
dotnet build src\TheWitheringArt.csproj
```

The build auto-deploys `ColoursOfCalradia.dll` into `<BannerlordRoot>\Modules\ColoursOfCalradia\bin\<platform>\` on every successful compile.

Output DLL: `src\bin\Debug\ColoursOfCalradia.dll`

### Creating a release package

```powershell
$env:BannerlordPath = "..."   # must point to a valid Bannerlord install
.\tools\pack.ps1
```

This builds in Release mode and produces `dist\ColoursOfCalradia_v<version>.zip` — the file you upload to Nexus Mods or GitHub Releases. The ZIP includes DLLs for both platforms and the `install.ps1` script.

The project targets `.NET Framework 4.7.2` to match the game's runtime. Logic is split across 18 source files; `MagicSystem.cs` is the module entry point.

---

## Starting the Mod — Colour Selection

When a new campaign starts you will see a multi-selection screen titled **"The Colours of Calradia"**. Hover each colour to read its school flavour text, attribute penalty, and permanent limitation(s).

- Select any combination of colours (including none — you walk an uncoloured path).
- Your chosen colours are **permanent** for that playthrough.
- Each colour you pick reduces one attribute by 1 and locks in a permanent limitation.
- You may select as many as all six colours simultaneously.

### Adjacency and Madness

The six colours form a **spectrum**: **Red — Orange — Yellow — Green — Blue — Purple**. This is a line, not a ring — Red and Purple are at opposite ends and are not adjacent to each other.

If you pick two or more colours that are **not contiguous** on the spectrum (e.g. Red and Yellow, skipping Orange), the incompatible natures fracture your sense of self. **Madness** is applied at game start: two random personality traits shift by ±1, chosen from Mercy, Valor, Honor, Generosity, and Calculating.

Contiguous selections (e.g. Red + Orange + Yellow, or Yellow + Green + Blue) carry no madness penalty from adjacency — but a second rule applies regardless:

**Picking more than 4 colours** triggers **The Fracture** in addition to any adjacency madness:
- Two further random traits shift at game start (stacking on top of adjacency madness if applicable).
- Every **week** thereafter, all five personality traits are randomised completely (each set to a random value between −2 and +2) with no recovery. No mind can hold five colours whole.

### Madness and Battle Orders

Madness does not stay inside your head — it bleeds into command. Whenever you issue a formation order in battle, there is a chance it fires differently from what you intended:

| Cause | Scramble chance |
|-------|-----------------|
| Non-contiguous colour selection | 3 % |
| 5 colours chosen | 5 % |
| 6 colours chosen | 7 % |

When an order is scrambled, the formation receives a random command (Charge or Halt) instead of the one you issued. A message appears in the log. These chances stack independently of each other — a player with 5 non-contiguous colours is subject to the 5 % rule (the higher threshold applies).

---

## The Six Colour Schools

| Colour | Identity | Attribute penalty | Personality drift |
|--------|----------|-------------------|-------------------|
| **Red** | Blood Price — violent, fiery | Cunning | Calculating −1 |
| **Orange** | Generous Hunger — joyful, open-handed | Intelligence | Generosity +1 |
| **Yellow** | The Fearful Eye — dread, revulsion | Social | Mercy −1 |
| **Green** | Gentle Burden — kind, restorative | Control | Mercy +1 |
| **Blue** | Scholar's Weight — cold, ordered | Vigor | Calculating +1 |
| **Purple** | The Waning Art — melancholic, fading | Endurance | All traits → 0 |

### Limitations by School

Each school carries a permanent limitation that activates on every cast.

**Red — Blood Price**
- **Furious:** Each Red spell automatically issues a Charge order to all your formations.
- **Blood Price:** Each Red spell opens a wound on the caster — 2 HP self-damage.

**Orange — Generous Hunger**
- **Joyful Cast:** You cannot cast Orange magic if your party morale is below 45. The warmth will not flow through misery.

**Yellow — The Fearful Eye**
- **Animal Fear:** You cannot cast Yellow magic from horseback. Animals sense the wrongness and refuse to carry you while you channel it.

**Green — Gentle Burden**
- **Pacifist:** You cannot use Green magic while wielding a weapon. Sheathe it first.

**Blue — Scholar's Weight**
- **Scholar's Weight:** Blue spells take **5 seconds** to wind up in battle. The stillness must settle through your limbs before it can reach the world — you stand motionless during the delay.

**Purple — The Waning Art**
- **The Slow Unravelling:** Each Purple cast reduces your fertility by 1% and ages you by 1 day — both are permanent. The future, sacrificed piece by piece.

---

## Casting — Controls

### Requirement: Light

**Magic is bound to available light.** The strength of light in your current environment determines whether a spell can be woven at all.

| Light level | When | Effect |
|-------------|------|--------|
| **Bright** | 07:00–20:00 outdoors | Normal casting |
| **Dim** | 05:00–07:00 (dawn) and 20:00–22:00 (dusk); hideout interiors | 33 % chance the spell unravels on cast with no effect |
| **Dark** | 22:00–05:00 (deep night); caves, mines, dungeons | Cannot cast at all |

NPC mage lords require full daylight and will not cast during dim or dark conditions.

### Keyboard

1. Hold **Left Alt** to enter spell mode.
2. Input a **5-key combo** using **W / A / S / D**, which map to:
   - **W** → U (Up)
   - **A** → L (Left)
   - **D** → R (Right)
   - **S** with an empty buffer → opens the spellbook instantly
   - **S** with a non-empty buffer → appends D to the combo (used in Yellow, Green, and Purple suffixes)
3. Release **Left Alt** to fire.

### Gamepad

1. Hold **Left Trigger** to enter spell mode.
2. Push the **left stick** in a direction:
   - Stick Up → U
   - Stick Down with empty buffer → Open spellbook; with non-empty buffer → D
   - Stick Left → L
   - Stick Right → R
3. Press **L3 (left stick click)** to open the spellbook (instead of casting).
4. Release **Left Trigger** to fire.

### Spellbook

Press **S** (keyboard) or **Left stick Down / L3** (gamepad) while holding the focus key to open the spellbook. The spellbook shows each spell's name, combo (with arrow symbols), and effect, grouped by school and context (Battle / Campaign).

A **Guide** button on the spellbook opens the full mechanics reference. A **Back** button returns to the spellbook from the guide.

---

## The Spell System

All 30 spells follow a strict **Form + Colour** combo structure:

- The **first two characters** encode the *form* (how the spell behaves).
- The **last three characters** encode the *colour school*.

### Forms

| Prefix | Form | Shape / Context |
|--------|------|-----------------|
| `UU` | **Blast** | Cone in front of the caster (battle) |
| `RL` | **Self** | Aura or effect centred on the caster (battle) |
| `LR` | **Create** | Persistent area effect placed on the battlefield (battle) |
| `UL` | **Affect** | Campaign map — situational, resource-based |
| `LU` | **Invoke** | Campaign map — advanced, targets heroes and rosters |

### Colour Suffixes

| Suffix | School |
|--------|--------|
| `RRR` | Red |
| `RLR` | Orange |
| `DLD` | Yellow |
| `LDL` | Green |
| `ULU` | Blue |
| `DDD` | Purple |

Note: **D** (S key) is valid mid-combo. It cannot appear as the first character of a combo (holding S with an empty buffer opens the spellbook instead).

---

## Spell List

### Blast Spells (UU prefix) — Cone effects

| Spell | Combo | School | Effect |
|-------|-------|--------|--------|
| **Crimson Torrent** | `UURRR` | Red | 55 damage to all enemies in a forward cone (15 m); pushes them back 6 m (smooth lerp over 0.4 s). |
| **Golden Tide** | `UURLR` | Orange | 18 damage to cone enemies (15 m); forces all enemy formations to Charge. |
| **Tide of Dread** | `UUDLD` | Yellow | 20 damage to cone enemies (15 m); drains 45 morale from each. |
| **Verdant Surge** | `UULDL` | Green | Heals allies in the cone (15 m) for up to 22 HP each. Player and enemies are not affected. |
| **Azure Arrest** | `UUULU` | Blue | 18 damage to cone enemies (15 m); drains 35 morale; halts all enemy formations; dismounts riders. |
| **Grey Harvest** | `UUDDD` | Purple | Instantly kills 1–3 random creatures in the cone (15 m); kill count scales with Vigor. |

### Self Spells (RL prefix) — Caster-centred effects

| Spell | Combo | School | Effect |
|-------|-------|--------|--------|
| **Scarlet Ward** | `RLRRR` | Red | Absorbs the next single physical blow — one strike, then the ward shatters. Expires after 12 s if nothing hits. |
| **Warm Beacon** | `RLRLR` | Orange | Pulls all allies within 18 m to a ring around the caster (smooth lerp). |
| **Nausea Bloom** | `RLDLD` | Yellow | Persistent 30 s aura (radius 8 m) that deals 22 damage every 2 s to all nearby creatures. |
| **Verdant Touch** | `RLLDL` | Green | Heals the caster for 28 HP. |
| **Cerulean Mirror** | `RLULU` | Blue | 18 s missile deflection — up to 4 volleys blocked; shatters after the 4th block or when time expires. Physical attacks still connect. |
| **Grief's Veil** | `RLDDD` | Purple | The grey folds you from sight for 12 s — you become invulnerable and nearby enemy formations lose their nerve. Shorter but stronger than the Mirror. |

### Create Spells (LR prefix) — Persistent battlefield effects

| Spell | Combo | School | Effect |
|-------|-------|--------|--------|
| **Cinder Burst** | `LRRRR` | Red | Instant 50 damage to all creatures within 10 m. |
| **Golden Recoil** | `LRRLR` | Orange | Toggle: zone of retribution — any creature that strikes while inside returns the blow upon themselves. Cast again to dismiss. |
| **Creeping Dread** | `LRDLD` | Yellow | Toggle: nine wandering clouds (3×3 grid, radius 7 m each) that roam the field, dealing 30 damage every 2 s to creatures they pass through. Cast again to dismiss. |
| **Emerald Font** | `LRLDL` | Green | Toggle: two healing pools that restore 15 HP every 2 s to all within — friend and foe alike. Cast again to dismiss. |
| **Sapphire Bastion** | `LRULU` | Blue | Toggle: four pillars of force in a line perpendicular to your facing, pushing creatures outward every 0.5 s. Cast again to dismiss. |
| **Hollow Gaze** | `LRDDD` | Purple | Toggle: pins one random nearby non-hero enemy into a catatonic state — they stand still and do nothing. Cast again to release them. |

### Notes on Create Spells

- **Toggled effects** are cancelled by casting the same spell again.
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
| Purple | *All traits* | → 0 (each trait moves one step toward zero) |

---

## Tournament Rule

Casting **any** spell during a tournament **immediately disqualifies you** — your character is killed and removed from the match with a warning message.

---

## Level-Up Learning

At character levels 10, 20, 30 … (while you have at least one school but fewer than six), a prompt appears offering you one new colour to learn. You may decline. Accepting applies the new school's attribute penalty and limitations immediately.

---

## Mage Lords (NPC)

### Seeding

At campaign start, one lord per faction is designated as the **archmage** and receives 4 colour schools. Remaining lords each have a random chance of receiving 1–3 schools:

| Schools | Probability |
|---------|-------------|
| 3 | 5 % |
| 2 | 7 % |
| 1 | 10 % |
| None | 78 % |

Companion heroes have a **10 % chance** to be granted 1–2 colours when they join your party.

When a mage lord dies, their colours are extinguished. After **7 days**, the colours pass to a randomly selected younger lord (under 50) in the same kingdom. If no suitable candidate exists, the timer retries every day until one becomes available — every kingdom retains at least one mage lord.

### The Prism

One lord in the entire world always carries **all six colour schools** — this is The Prism. They are selected randomly at campaign start from any living non-player lord. The Prism casts in battle at **3× the normal frequency** and their personality shifts randomly every week regardless of cast count. They are feared and distrusted by all other colour lords.

When The Prism dies, if you carry all six colours and are not already the Prism, there is a **30 % chance** the mantle seeks you next. A prompt appears the following day — you may accept or refuse. If you refuse (or the chance doesn't trigger), a new Prism rises within **one month**.

**Becoming the Prism grants immunity to Madness and Oversaturation.** Your personality will not fracture weekly, and your Saturation cap cannot be depleted.

You may also choose **"I am a Prism"** at character creation (easy mode) to begin with all six colours and full immunity immediately, with no attribute penalties.

### Colour Lord Relationships

Lords who share colour schools are drawn together; those who carry too many schools become feared by their peers.

- Lords with **1–2 schools** who share at least one colour with another 1–2-school lord gain **+5 relations** with them (applied at seeding and whenever a new lord receives colours).
- Lords with **5 or more schools** (including The Prism) take **−5 relations** with every other colour lord, regardless of shared schools.

### Battle Bonus

Colour lords maintain a **morale floor** for their party. Each day, if the party's recent-events morale is below the floor, it is raised to meet it:

| Schools held | Morale floor |
|-------------|-------------|
| 1 | 10 |
| 2 | 20 |
| 3 | 30 |
| 4 | 40 |
| 5 | 50 |
| 6 | 60 (cap) |

After each battle resolves, colour lords also **heal a fraction of their wounded troops**. The heal fraction is `colours × 5 %` — a 4-colour lord heals 20 % of each troop type's wounded count.

### Battle AI

NPC mage lords cast in battle using a priority-driven AI:

1. **Self-heal** with Verdant Touch if below 35 % HP (Green lords only, if not wielding a weapon).
2. **8 % random wild cast** — fires a random applicable school spell.
3. **Swarm response** — if 3+ enemies within 8 m, prefer Cinder Burst (Red) or Grey Harvest (Purple).
4. **Cone attack** — Crimson Torrent (Red) or Azure Arrest (Blue) if enemies are in the forward arc.
5. **Ally healing** — Verdant Surge (Green) if allies in the forward arc are wounded.
6. **Morale drain** — Tide of Dread (Yellow).
7. **Reinforcement** — Calling (Orange) if outnumbered; Warm Beacon as fallback.

All NPC battle spells require light — the same three-tier rule as the player applies. In dim conditions NPCs have a 33 % chance of their cast failing silently. NPC lords have a **12-second base cooldown** between spells, modified by their Calculating trait — Impulsive lords cast 25 % more often; Calculating lords cast 50 % less often. The Prism uses a **4-second base cooldown**.

NPC lords apply their school limitations: Green lords cannot cast while wielding a weapon; Yellow lords cannot cast from horseback; Orange lords cannot cast if their party morale is below 45. After each cast, non-Blight non-Prism lords have a **5 % chance of a 3-second knockdown** from Oversaturation.

### Campaign Map Spells

Each mage lord has a **5 % daily chance** to cast a campaign-map spell. At most **1 lord map-cast fires per day** across the entire world. Each lord has a cooldown of **12–16 days** after casting.

| School | Effect A (50 %) | Effect B (50 %) |
|--------|-----------------|-----------------|
| **Red** | Own party morale +10 (*Bloodlust*) | Random enemy village hearth ×0.8 (*Carnage*) |
| **Orange** | Nearest friendly village hearth ×1.05 (*Celebrate*) | Transfer 1–2 troops from rival lord to own party (*Bribe*) |
| **Yellow** | Random enemy lord renown −10 (*Fade*) | Random enemy party morale −15 (*Terror*) |
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

Each named unit has a **3 % daily chance** to trigger a minor campaign map effect. At most **1 unit map-cast fires per day** total. After casting, a unit cannot cast on the map again for **6–9 days**. Effects are generally positive for the unit's own party (morale, experience, food).

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

## Colour Spectrum Reference

```
Red (0) — Orange (1) — Yellow (2) — Green (3) — Blue (4) — Purple (5)
```

The colours form a **spectrum**, not a ring. Adjacent colours on the spectrum do not cause Madness. Non-adjacent combinations (e.g. Red + Yellow, or Red + Purple) do. Red and Purple are at opposite ends — they are never adjacent regardless of what else you pick.

---

## Campaign Map Spells

Two additional spell forms work exclusively on the campaign map and require the same daytime light conditions as battle spells. They cannot be cast during battles or while in a settlement menu.

### Forms

| Prefix | Form | Keys |
|--------|------|------|
| `UL` | **Affect** | W then A (U then L) |
| `LU` | **Invoke** | A then W (L then U) |

### Affect Spells (UL prefix) — situation-based

Each Affect spell is tied to a specific situation or resource. No cooldowns — all costs are mechanical.

| Spell | Combo | School | Effect | Cost / Limiter |
|-------|-------|--------|--------|----------------|
| **Ember Drive** | `ULRRR` | Red | +130×power gold during a village raid or hideout assault | −10% current HP per cast; blocked at ≤5 HP |
| **Shared Feast** | `ULRLR` | Orange | Consume food → party morale +12×power | Food cost doubles each cast within the day (1→2→4→8…), resets at midnight |
| **Dread Whisper** | `ULDLD` | Yellow | Nearest enemy party loses 22×power morale | No self-cost (limitation is Animal Fear: no horseback) |
| **Verdant Hour** | `ULLDL` | Green | Produce 1–4 grain | −5% current HP per cast; blocked at ≤5 HP |
| **Scholar's Gaze** | `ULULU` | Blue | Reveals all allied and enemy parties within 80 map units — shows direction, leader, and troop count in a popup | No cost |
| **Grey Veil** | `ULDDD` | Purple | Scatter nearby enemy parties (within 2 map units); enemies lose your trail | −1% fertility + 1 day aging per cast |

### Invoke Spells (LU prefix) — advanced campaign effects

Invoke spells target heroes, rosters, and rival lords directly. No cooldowns — all costs are mechanical.

| Spell | Combo | School | Effect | Cost / Limiter |
|-------|-------|--------|--------|----------------|
| **Crimson March** | `LURRR` | Red | Sustains party morale above Bannerlord's march-speed threshold (≥78) each hour for the duration, keeping the engine's built-in +3% speed bonus active continuously | −8% current HP on cast; −2 HP per hour; 4–8 h duration scaled by power; blocked at ≤5 HP |
| **Golden Word** | `LURLR` | Orange | Spend gold as patronage → +20×power influence | Gold cost 100→200→400 (capped at 400), resets at midnight; kingdom required |
| **Whispered Ruin** | `LUDLD` | Yellow | Nearest enemy lord (at war) clan renown −8 | −2 own clan renown per cast |
| **Tend the Fallen** | `LULDL` | Green | Heal 5+(power×3) wounded troops in own party | −5% current HP per cast; blocked at ≤5 HP |
| **Scholar's Blueprint** | `LUULU` | Blue | Advances siege engine construction progress (+150×power) on all machines currently being built | Renown −2 per cast; requires active siege; no effect if nothing is under construction |
| **Wither's Touch** | `LUDDD` | Purple | Nearest enemy lord: party morale −10, clan renown −8 | −1% fertility + 1 day aging per cast |

### Notes

- **Orange Golden Word** gold cost is capped at 400 — once at the cap, each cast costs 400 gold. The cap resets each campaign day. Kingdom membership required.
- **Yellow Whispered Ruin** and **Dread Whisper** require an active war with the target's faction. **Scholar's Blueprint** requires an active siege by the player's party. **Wither's Touch** works against any non-player faction.
- **Scholar's Blueprint** requires no kingdom but does require an ongoing siege.
- **Red** HP costs apply to campaign HP, which carries into the next battle. Ember Drive during a raid means you fight the battle with reduced health.

---

## Saturation

Every cast generates **0–3 Saturation** at random. Saturation represents how much absorbed light your body holds at once. The cap scales with your level: **max = hero level + 10** (hard cap 30).

| Condition | Effect |
|-----------|--------|
| Saturation reaches max | **Oversaturation** |
| Darkness falls (22:00–05:00 or dark location) | Saturation resets to 0 |

### Oversaturation

When you hit the cap, the light tears through you:
- Knocked down for **3 seconds** (in battle)
- A random personality trait shifts by ±1
- Your **max Saturation permanently decreases by 1**

### Max Depletion

When max Saturation reaches **0**, you must choose:

| Choice | Consequence |
|--------|-------------|
| **Surrender your colours** | All colour schools are lost permanently. Others will inherit them in time. |
| **Embrace the Blight** | You keep your colours and become immune to all future Oversaturation — but every living lord's opinion of you collapses by −100 immediately and permanently. |

### Immunity

- **Blights** are immune to all Oversaturation effects.
- **The Prism** (NPC or player) is immune to Madness and Oversaturation.

### NPC Saturation

- Every NPC colour lord has a **5 % chance of a 3-second knockdown** after each battle cast.
- Every week there is a **2 % chance** a random NPC colour lord oversaturates:
  - **80 % outcome:** they lose all colours. Another lord in the same kingdom inherits them within 7 days.
  - **20 % outcome:** they die and a Blight of one of their colours spawns immediately. **Blights no longer auto-respawn** — each Blight that is slain is gone until the next oversaturation event spawns one.
