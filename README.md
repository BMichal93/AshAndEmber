# Colours of Calradia — v2.0

A Mount & Blade II: Bannerlord magic overhaul built around six colour schools, named mage lords, persistent magical units, and a personality system that shifts with every cast.

## Package Structure

```
ColoursOfCalradia/
├── SubModule.xml               mod manifest
├── ModuleData/
│   ├── items.xml               (reserved for future spell items)
│   └── troops.xml              (reserved for future mage troops)
├── src/
│   ├── MagicSystem.cs          all C# logic (~3 000 lines, one file)
│   └── TheWitheringArt.csproj  build project (outputs ColoursOfCalradia.dll)
└── README.md                   this file
```

## Installation

1. Copy the entire folder into `Modules/ColoursOfCalradia/` inside your Bannerlord directory.
2. Build the project (see **Build** below). The DLL output is `ColoursOfCalradia.dll`.
3. Place the DLL in `Modules/ColoursOfCalradia/bin/Gaming.Desktop.x64_Shipping_Client/` (Xbox/Game Pass) or `bin/Win64_Shipping_Client/` (Steam).
4. Enable **Colours of Calradia** in the Bannerlord launcher.

## Starting the Mod — Colour Selection

When a new game starts you will see a multi-selection screen listing all six colour schools. Hover each colour to read its flavour text, attribute penalty, and two cast limitations.

- Select any combination of colours (including none).
- Your chosen colours are permanent for that playthrough.
- Each colour you pick reduces one attribute by 1 and locks in two permanent limitations.

## The Six Colour Schools

| Colour | Theme | Attribute penalty | Limitation I | Limitation II |
|--------|-------|-------------------|--------------|---------------|
| **Red** | Violent, chaotic | Cunning | Forced charge after cast | Recoil damage |
| **Orange** | Joyful, generous | Intelligence | Extra food consumed daily | Coin cost per cast |
| **Yellow** | Strategic, tactical | Control | Dismounts after cast | Cooldown |
| **Green** | Kind, healing | Endurance | No weapon in hand | Sunwalker: cannot cast at night |
| **Blue** | Cold, scholarly | Social | No horseback | Ages caster slightly |
| **Purple** | Sinister, sacrificial | Vigour | Delicate Art: cannot cast in daylight | Sacrifices a random ally |

## Casting

1. Hold **Left Alt** (keyboard) or **Left Trigger** (controller).
2. Type any 6-key WASD combo shown in your spellbook.
3. Release Alt / LT to fire the spell.

Press **S** (or **L3**) while holding Alt to cycle through your spellbook.

## Spell List (18 battle spells + 12 campaign map effects for lords)

### Red — Violent
| Combo | Spell | Effect |
|-------|-------|--------|
| UUURRR | Crush | Instantly kill one random enemy in the forward cone |
| LRLRLU | Vortex | Pull enemies toward you |
| URUURR | Fury | Issue charge to all friendly formations |

### Orange — Generous
| Combo | Spell | Effect |
|-------|-------|--------|
| RLLRLL | Bound Together | Pull allies within 20 m to your side; hurl enemies in that radius outward |
| UULRLU | Calling | Add imperial recruits to your party (join after battle) |
| RRLLUU | March | Surge 12 m forward instantly |

### Yellow — Strategic
| Combo | Spell | Effect |
|-------|-------|--------|
| LURLUR | Hold Arrows | Order enemy formations to cease fire |
| ULUURR | Repel | Periodic knockback pulse for 60 s |
| RRUULL | Dismount | Force enemy riders off their horses |

### Green — Healing
| Combo | Spell | Effect |
|-------|-------|--------|
| UULLUR | Restore | Heal self for 40 HP |
| ULLRUU | Aid | Heal nearby allies for 25 HP each |
| UURLUL | Nurture | Wide-area morale + HP refresh |

### Blue — Scholarly
| Combo | Spell | Effect |
|-------|-------|--------|
| LULURU | Shield | 8 s invulnerability ward |
| LLRRLU | Stasis | Halt all enemy formations |
| RULRUL | Stun | Drain the nerve of all enemies within 30 m (morale collapse) |

### Purple — Sinister
| Combo | Spell | Effect |
|-------|-------|--------|
| UURRLL | Severe Life | Instantly kill one random enemy (non-hero) |
| RLLURR | Wither | Kill every agent (allies included) within 10 m — player is spared |
| LURRUL | Subjugate | Shatter a nearby enemy's will — they flee |

## Personality Drift

Every 10 casts of the same school shifts one personality trait:

| School | Trait | Direction |
|--------|-------|-----------|
| Red | Calculating | −1 |
| Orange | Generosity | +1 |
| Yellow | Valor | −1 |
| Green | Mercy | +1 |
| Blue | Calculating | +1 |
| Purple | Mercy | −1 |

The message only appears when the trait actually moves (stops at ±2).

## Tournament Rule

Casting any spell during a tournament **immediately disqualifies you** — your character is killed and removed from the match with a warning message.

## Mage Lords (NPC)

- Each faction has several lords seeded with 1–3 colour schools.
- They cast in battle using the same school rules (AI-controlled).
- Every day they have a chance to cast a campaign-map spell: morale buffs, renown effects, healing, or influence gains.
- Up to 3 lord map-casts fire per day total to keep the log readable.

## Named Magical Units

- Roughly 1 % of each lord army contains a named magical soldier with 1–2 schools.
- Bandit groups of 15+ troops have a small seeding chance.
- Named units cast in battle (AI-driven, school-appropriate effects).
- They also have a 10 % daily chance to trigger a minor campaign-map effect (max 2 per day).
- When killed, a unit enters a 3–5 day respawn queue and then reappears in their original or a fallback lord party.

## Children & Companions

- Children of the main hero are born with a colour gift: they share at least one colour with the parent and may have ±1 schools total.
- Companions have a 30 % chance to be granted 1–2 colours on joining.

## Level-Up Learning

At levels 10, 20, 30 … (while you have at least one school but fewer than 6), a prompt appears offering one new colour to learn. You can decline. Accepting applies the new school's attribute penalty and limitations normally.

## Build

```powershell
# Xbox / Game Pass
$env:BannerlordPath = "C:\XboxGames\Mount & Blade II- Bannerlord\Content"

# Steam
# $env:BannerlordPath = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"

cd src
dotnet build TheWitheringArt.csproj
```

Output DLL: `src/bin/Debug/ColoursOfCalradia.dll`

The project targets `.NET Framework 4.7.2` to match the game's runtime. All logic lives in the single file `MagicSystem.cs`.
