# Ash and Ember — v0.9

A Mount & Blade II: Bannerlord magic overhaul centred on the Inner Fire: a single, versatile force shaped by the caster's will. Lords who carry it fight differently. Bandits who steal it burn.

---

## Package Structure

```
AshAndEmber/
├── SubModule.xml                    mod manifest
├── ModuleData/
│   ├── items.xml                    (reserved)
│   └── troops.xml                   (reserved) 
├── src/                             ~5 000 lines across 20 source files
│   ├── MagicSystem.cs               module entry point + mission behaviour
│   ├── MageKnowledge.cs             gift tracking, grimoire UI, talent menu
│   ├── SpellBuilder.cs              two-phase input parser → SpellCast
│   ├── TalentSystem.cs              15 talents, learning logic, map spells
│   ├── AgingSystem.cs               casting cost (days of life), Blight path
│   ├── MagicInputHandler.cs         keyboard/gamepad combo detection
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
│   │   └── BanditMageAI.cs          rare bandit unit spellcasters
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

Start a new campaign. If the **"The Inner Fire"** prompt appears during character creation, the mod is installed correctly.

---

## Getting the Gift

The Inner Fire must be *found*, not chosen at a menu.

- During character creation a prompt may appear asking if the fire has always been there. Accepting it grants the Gift.
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

`WW SS X UUU` — Blast (5 m) + Burst (5 m), simultaneously, 36 flame damage. 6 inputs = 2 days cost.

---

## Effects (after Break)

Multiple effect types may be combined freely. Each key press adds one count.

| Key | Arrow | Effect | Per count |
|-----|-------|--------|-----------|
| W | ↑ | **Flame** | 12 damage (+3 per Surge count, +4 per Smoulder count as side damage) |
| A | ← | **Surge** | 4 m push (away from caster) + 3 side damage per count |
| D | → | **Smoulder** | 7 morale drained + 4 side damage per count |
| S | ↓ | **Reverse** | Flips all effects (damage → heal, push → pull, morale drain → boost) |

### Combined fires

When you use two effect types together their interaction changes what you see in the log:

| Combination | Name |
|-------------|------|
| Flame + Smoulder | **Scorch** |
| Surge + Flame | **Cinder** |
| Smoulder + Surge | **Ember Surge** |

Combined effects still apply both individual results — the names are cosmetic.

---

## Ward Sigil (no Break required)

Hold the focus key and press **S (↓) twice or more without pressing Break**. Release to cast the ward immediately.

| Input | Effect | Cost |
|-------|--------|------|
| ↓↓ | Ward — self only | 1 day |
| ↓↓↓ | Ward — 2 m radius (protects nearby allies) | 2 days |
| ↓↓↓↓ | Ward — 4 m radius | 3 days |

A ward makes the protected agent **immune to all magic effects for 10 seconds**. Warded agents cannot be hit by the player's spells, NPC lord spells, or wave/barrier node impacts. They are also not nudged by the wave's avoidance system.

NPC lords cast wards reactively when their HP drops below 40% or when they detect a magic cast nearby. Honorable or merciful lords extend the ward to allies within 6 m; others protect only themselves.

---

## Aging Cost

Every spell draws on your lifespan. The cost is based on the **total input count** (form presses + effect presses):

| Total inputs | Cost |
|--------------|------|
| Below 4 | Free |
| 4–5 | 1 day |
| 6–7 | 2 days |
| 8–9 | 3 days |
| 10–11 | 4 days |
| 12–13 | 5 days |
| 14–15 | 6 days |
| +2 inputs | +1 day |

The **Tempered** talent raises the free threshold from 4 to 5. The **Resonance** talent gives a 1-in-4 chance that any cast costs nothing.

Ward sigils cost `N − 1` days (↓↓ = 1 day, ↓↓↓ = 2 days, etc.).

### Ashen

At age 100 a prompt appears: *The Last Ember*. You may:

- **Take the cold** — become Ashen. Aging stops permanently. Every cast instead raises your criminal rating by `cost × 5`. You are expelled from your kingdom. Your appearance changes: ash-white hair.
- **Let it end** — die of old age.

Ashen mages are immune to the aging cost but accumulate notoriety with every cast.

### Tournament

Casting **any** spell during a tournament **kills and disqualifies you instantly**.

---

## Talents

Talents are learned through the grimoire (Alt+X → *Talents*). The **Gift** is free. Each subsequent talent costs **focus points**, capped at 3 per talent (Gift = 0, 2nd talent = 1 point, 3rd = 2 points, 4th onward = 3 points).

### Passive talents

| Talent | Effect |
|--------|--------|
| **Gift** | You carry the fire. Battle casting enabled. |
| **Tempered** | Free casting threshold raised from 4 inputs to 5. |
| **Resonance** | 1-in-4 campaign map casts cost no days. |
| **Ember** | 5% chance per battle kill to restore 1 day of youth. |
| **Harvest** | Executing a captured lord restores 100 days of youth. |
| **Reap** | Raiding a village restores 5 days (7-day cooldown between raids). Each discarded prisoner has a 5% chance to restore 1 day. Marks you (−1 Mercy, −1 Honor, +30 criminal rating). |
| **Kinship** | +10 relations with other mages; relation cannot fall below −10 with them. |

### Campaign map spells

These are cast from the grimoire on the campaign map. Each costs 1 day (or criminal rating if Ashen). Resonance applies.

| Talent/Spell | Effect |
|--------------|--------|
| **Subjugate** | One prisoner yields and joins your roster. |
| **Rejuvenate** | Several wounded soldiers recover. |
| **Kindle** | Party morale +20. |
| **Quicken** | 20 grain grows. |
| **Unsettle** | Nearest enemy party loses 20 morale. |
| **Wither** | An enemy village loses 20% of its hearth. |
| **Clairvoyance** | +20 influence (requires kingdom membership). |
| **Curse** | 2–4 soldiers in the nearest enemy party are wounded or killed. |

---

## NPC Mage Lords

At campaign start a proportion of lords are seeded as mages by `ColourLordRegistry`. A smaller fraction are **Ashen lords** — they cast with no aging cost, have a much shorter cooldown, and use more aggressive spell combinations.

### Ashen City Clans

Three clans — from the settlements of **Tyal**, **Sibir**, and **Baltakhand** — are marked Ashen at the start of every campaign. Their heroes:

- Are removed from their kingdoms permanently (rejoin attempts are blocked).
- Do not age (birth day is reset daily to keep them near age 35).
- Bear the title **Ashen Lord** or **Ashen Lady**.
- Start with maximum relations with the player.
- Carry both the gift and the Ashen path — they will never die of old age.

### Battle AI

NPC lords follow a priority order each tick:

1. **Ward** themselves if HP < 40% or a magic cast was detected within 20 m recently.
2. **Heal** (spawn a healing zone) if HP < 30%.
3. **Heal zone** for allies who are below 50% HP within 15 m.
4. **Attack** — Burst (when surrounded by 3+ enemies) or Blast (when enemies are in the forward cone). Blight lords use heavier recipes and roll from a wider attack set.

Cooldowns by personality:

| Lord type | Cooldown |
|-----------|----------|
| Default | 25 s |
| Impulsive (Calculating < 0) | 15 s |
| Calculating (Calculating > 0) | 35 s |
| Ashen lord | 6 s |

Lords wait 12 seconds at battle start before their first cast. They do not cast during the first 12 seconds even if endangered.

---

## Bandit Mages

About 4% of eligible bandit units carry a stolen fragment of the fire. They cast once per 18 seconds using simple blast or burst recipes.

**The fire punishes those who borrow it without the gift.** After each cast there is a 25% chance the bandit is consumed and dies instantly.

### Fire Worshippers & Ashen Spawn

Roughly 10% of newly created Looter and forest bandit parties are renamed **Fire Worshippers**. Roughly 10% of sea raider and mountain bandit parties become **Ashen Spawn**. Both types are guaranteed to have at least one bandit mage.

Each troop type has a title:

| Troop | Title |
|-------|-------|
| Looter | Fire Prophet |
| Forest bandit | Hedge Witch |
| Sea raider | Ashen Caller |
| Mountain bandit | Ash Shaman |
| Steppe bandit | Wind Binder |
| Desert bandit | Ember Prophet |

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
