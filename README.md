# Ash and Ember — v0.48

> **A large gameplay-systems mod for Mount & Blade II: Bannerlord — and the codebase behind my context-engineering write-up.**
> **Scale:** ~76,000 lines of C# across ~254 files. **Stack:** C# / .NET Framework 4.7.2, built against the Bannerlord (TaleWorlds) API — no Harmony; behaviour is layered through the game's `CampaignBehavior`/`MissionBehavior` hooks. **Tests:** 237 pure-logic NUnit tests in `tests/PureLogicTests.cs` (no engine types). **Working with an agent here:** [`CLAUDE.md`](CLAUDE.md) is the map I hand Claude Code — entry points, state-reset rules, and the conventions that keep changes save-compatible.

---

A Mount & Blade II: Bannerlord magic overhaul centred on the Inner Fire: a single, versatile force shaped by the caster's will. Lords who carry it fight differently. Bandits who steal it burn. The Ashen march from the north and do not negotiate.

This is not a small spell-pack. Ash and Ember rebuilds Bannerlord's magic from the ground up (~76,000 lines across ~254 source files) — one unified elemental casting system for player and NPC lords alike, three alternate paths to power, a five-faction culture and faction rework, roaming elemental beings, and a long tail of campaign systems: covert schemes, sea trade, market speculation, mercenary soldiering, clan orders, explorable ruins, a secret society of mages, and three multi-stage questlines.

---

## Package Structure

```
AshAndEmber/
├── SubModule.xml                    mod manifest
├── ModuleData/
│   ├── items.xml                    crystal item definitions
│   └── troops.xml                   culture-renamed troop trees
├── GUI/                             custom Gauntlet prefabs (splash, lore, nature bar)
├── bin/
│   ├── Win64_Shipping_Client/       Steam build
│   └── Gaming.Desktop.x64_Shipping_Client/   Xbox / Game Pass build
├── src/                             ~76 000 lines across ~254 source files (grouped by system folder)
│   ├── MagicSystem.cs               module entry point + mission behaviour
│   ├── AgingSystem.cs               life-cost of casting
│   ├── Magic/                       the unified element system — Codex, input, effects, walls, ultimates, map spells, teachers
│   ├── Spells/                      legacy two-phase Inner Fire pipeline (still drives NPC mage casts underneath)
│   ├── Nature/                      the Living Ember discipline — charges, living-energy economy, backlash
│   ├── Miracles/                    Grace — prayers, grace economy, priest troops, battle AI, talents
│   ├── DarkGifts/                   the Dark Gift path
│   ├── Crystals/                    the eleven consumable crystals, Crystalline Chambers, battle AI
│   ├── Elementals/                  the Kindled — roaming/summoned elemental beings
│   ├── Talents/                     talent tree, learning curve, map-spell talents
│   ├── Schemes/                     covert operations — system, campaign behaviour, minigame
│   ├── Sea/                         harbours, voyages, trade ventures, NPC sea lanes
│   ├── Markets/                     the Exchange — commodity speculation
│   ├── Tavern/                      tavern menus, rumours, "The Old Green"
│   ├── ClanOrders/                  travel and hunt orders for your own clan's lords
│   ├── AshenRuins/                  the 34 explorable Ashen ruins
│   ├── Conclave/                    the Ember Conclave — a secret society of mages
│   ├── Apprentice/                  taking on and training a young talent
│   ├── Soldier/                     Take the Lord's Coin — mercenary soldiering from clan level 0
│   ├── GreatAwakening/              The Great Awakening — the Duneborn's dark-forces questline
│   ├── QuestSystems/                Dragon main quest, Burning Lab questline, settlement encounters, world & battle events
│   ├── AI/                          Ashen kingdom/city, NPC mage AI, bandit mages, dialogue, Rival Shadow, culture renames
│   ├── Tribes/ AshenAltars/         Tribes of the East kingdom logic, Dark Altar rites
│   ├── Visual/                      glows, movement, atmospheric scene tone, battle whispers
│   └── Startup/                     splash, lore intro, loading screen
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

Start a new Sandbox campaign. A lore introduction screen appears, followed by the gift prompt. If **"The Inner Fire"** option appears, the mod is installed correctly.

---

## Lore Introduction

When starting a new Sandbox campaign, a short lore screen appears before play begins:

- The fire gives life. The Ashen chose the cold instead.
- Three Empire successor states fight over Calradia's bones while ash moves south.
- Some who carry the Gift are tempted to answer the cold's call instead of the flame's.

The gift selection follows immediately after.

---

## Getting the Gift

Four paths open at campaign start. Each is permanent — you walk one, and the others close.

- **The Inner Fire** — the default path. The fire must be *found*, not chosen at a menu: a prompt asks if the fire has always been there. Accepting grants the Gift. It can also arrive later through certain events (aging, bloodline, encounters, companions). Once carried, the grimoire is available at any time (**Alt + X**).
- **The Living Ember** — for those who hear the land instead of a fire within. At the gift prompt, choose *"The world beneath me has always been louder than the fire."* This is not a separate spell system anymore — it grants the same unified elements (Wind / Earth / Water / Spirit) at a lower life-cost per cast, with its teachers appearing as ordinary element teachers rather than a parallel discipline.
- **Grace** — devout characters may instead be offered the miracle path: prayers drawn from personality virtues rather than a channelled cone, replenished at a Sanctuary.
- **The Dark Gift** — for the cruel. If your hero is **Dishonourable**, the prompt also offers *"I bargained with the dark, and it marked me"* — starting you with one random Dark Gift. Visit a Dark Altar to buy more or renounce them.

Inner Fire, Grace, Nature, and the Dark Gifts are **mutually exclusive** — pick one road.

---

## Magic: The Unified Element System

Hold Focus, load a learned element, draw a charge by standing still, and release it as an attack or a wall. Casting is a physical act with a physical tell — readable by you and by anyone facing you across the field. Player, NPC lords, and the Kindled all cast through the same system, so an attack shape changes for everyone at once.

### Controls

| Action | Keyboard | Gamepad |
|--------|----------|---------|
| Focus (hold) | **Left Alt** | Hold **X** — bumpers are taken by the war-order radial and the miracle gesture; X leaves both triggers free |
| Load a learned element | **W** Wind · **S** Earth · **A** Water · **D** Spirit (Fire is default) | Flick the **left stick** ↑↓←→; click **L3** for Fire |
| Attack (cone) | **Left Mouse** | **Right Trigger** |
| Wall (barrier) | **Right Mouse** | **Left Trigger** |
| Open grimoire | **Alt + X** | **LB + RB** |
| Codex of the Inner Fire (learn, map) | **Alt + L** | — |
| Litany of Devotions (Grace talents, map) | **Shift + L** | — |

### How casting works

1. Hold Focus. Fire loads by default; tap a direction to load a learned element.
2. Stand still, hand free, armour light, and draw. Power builds to full strength at ~5 seconds — no minimum, so an instant release is allowed but weak.
3. **Overchannel:** keep pouring past ~10 seconds and the working strikes twice as hard; hold past ~15 seconds and the gathered power disperses.
4. Release Focus with a charge drawn and it lingers in your hand for a few seconds — loose it with a lone Attack or Block, or re-take Focus to keep drawing.

### The five elements

| Element | Attack | Wall |
|---------|--------|------|
| **Fire** | Flying bolt that bursts on impact | Wall of fire |
| **Wind** | Forward gust that hurls and slows | Wall that turns arrows aside and bogs down all who cross |
| **Earth** | Short, brutal cone of erupting rock — heavy damage and root | Stone wall |
| **Water** | Forward slowing wave | Barrier of mist |
| **Spirit** | Fear + a stray order flung into enemy ranks | Wall that heartens and mends your own — the only ranged healing in the game |

Fire is free from the start; the rest are learned in the **Codex** (Alt + L) with focus points, escalating in cost with each power already held, or one point cheaper from a teacher. Three disciplines are learned alongside them: **Steel** (cast with a weapon in hand, bear twice the armour weight), **Blood** (a lord's death gives back years the fire has burned), and **Nature** (lowers the flat life-cost of every working).

### Fusion — blending two elements

Fire is a fifth element key, not special-cased: press it, then press a second, different learned element within a heartbeat, and the two blend instead of replacing each other.

| Blend | Working |
|-------|---------|
| Fire + Wind | **Lightning** — a bolt that chains between foes |
| Fire + Water | **Fog** — a standing cloud that slows, dampens ranged shots, and occasionally scrambles a blinded formation |
| Fire + Earth | **Magma** — a molten patch that burns and bogs down all who cross it |
| Wind + Water | **Ice** — a forward freeze that roots utterly but deals no harm |
| Wind + Earth | **Sandstorm** — a long blinding gust that bolts any mount it catches, breaking a charge outright |
| Earth + Water | **Mire** — ground that keeps giving way, spreading wider the longer it stands |

**Spirit + any other element** is a command to your own ranks instead of an attack — it steadies and drives nearby warriors for ~12 seconds: **Onslaught** (Fire, charge past fear), **Quicken** (Wind, burst of ordered speed), **Steadfast** (Earth, nerve locked), **Hold the Line** (Water, ranks halt and lock).

Two known elements together also grant a shared **campaign-map fusion rite**, cast through the grimoire: Storm's Reckoning, Scorched Earth, The Hidden Road, Shifting Dunes, The Sinking Road, The Long Stillness — each strikes or protects at the scale of a whole host, not a single battle.

### The Unbinding — ultimates

Draw an element to its fullest, then press Attack and Block **together** to unbind it: a once-per-battle, per-element ultimate at a steep flat toll (12 days).

| Element | Unbinding |
|---------|-----------|
| Fire | A nova — everything nearby burns, horses bolt, a burning ring remains |
| Wind | The gale carries *you* — fly where you look; any hit knocks the wind out and you fall |
| Earth | The Sundering — ground erupts outward, hurling foes back, leaving churned, bogging rubble |
| Water | The sky weeps over a wide field — fire halved, horses mired, bowstrings soaked |
| Spirit | The land sends a champion — an elemental of frost, sand, or stone fights at your side, then comes apart |

Each blended fusion carries its own Unbinding too (The Storm's Judgment, The Devouring Mist, The Ground Ignites, The Absolute Stillness, The Devouring Dunes, The Swallowing Ground). Enemy lords channel the Unbinding the same visible way you do — interrupt with any landed blow to break it.

### The cost

Every cast shortens your **life expectancy**, not your current age — you will die sooner, tracked (but never spelled out to the day) in the grimoire's Ledger of Years. The toll is **flat**: a longer draw buys power, never a cheaper cast. Casting on the campaign map costs days too, escalating with each working in the same day. The Ashen pay in criminal standing instead of years. Magic deepens as you do: damage scales gently with character level (+1%/level, capped +30%), so it never falls behind in a long campaign.

### If you are Ashen

The cold reshapes each element in name and colour, not mechanics: Fire → **Cold**, Wind → **Storm**, Earth → **Ash**, Water → **Snow**, Spirit → **Void**. The workings are identical; only the visuals and the price (criminal standing, never years) change.

---

## Grace — the Miracle Path

Not divine intervention — the same Fire everyone else draws on, called through the caster's own conviction rather than a drawn cone. Each personality trait grants two prayers once it reaches +1: one for battle, one for the road.

- **In battle:** hold Focus (Left Ctrl / R3) and trace a prayer's six-stroke sequence with W/A/S/D (or a left-stick flick), then release. Each prayer spends 1 Grace, replenished at a Sanctuary.
- **On the map:** Shift + X (R3 + L3) opens the Litany — pick a prayer and recall its rite.
- **The Litany of Devotions** (Shift + L, map) is a talent tree that refines your prayers: a devotion per virtue deepens the two prayers it grants, and Abundant Grace widens the well itself.

| Virtue | Battle prayer | Map rite |
|--------|---------------|----------|
| Mercy | **Radiant Mending** — heal yourself and nearby allies | **The Mending Road** — the party's wounded mend faster |
| Valor | **Light of Valour** — courage and speed surge through your line | **The Long March** — morale lifts, the miles fall away |
| Honour | **Aegis of the Oath** — a golden ward returns damage as healing | **The Sworn Word** — steady a wavering town, or warm a lord toward you |
| Generosity | **Shared Light** — consecrate the ground, warding and mending allies | **The Open Hand** — fuller stores, a well-fed column |
| Calculating | **Pyre of Judgement** — a pillar of holy fire falls where you look | **Far-Sight** — the light shows the roads and what moves on them |

A prayer you aren't yet virtuous enough to bear is greyed out in the Litany until the granting trait reaches +1. Priest troops and NPC lords carry Grace into battle the same way, with AI tuned to answer the moment — a caster bleeding, allies falling, the press closing — rather than a blind periodic roll.

---

## Crystals

Eleven consumable focused-light stones, each a single-use battlefield working with its own lore entry and its name now stating its effect outright:

| Crystal | Working |
|---------|---------|
| **Sunstone — Warmth Pulse** | Heals you and nearby allies |
| **Embershard — Shard Burst** | AoE fire damage |
| **Rimeshard — Frost Pulse** | Slows nearby enemies |
| **Veilstone — Veil Grasp** | Strikes one random enemy at range, slowing them |
| **Stormcrystal — Thunderclap** | AoE damage + morale drain |
| **Duskstone — Despair Wave** | Morale drain + slow |
| **Thornveil — Root Grasp** | Strikes and roots one random enemy at range |
| **Aegisstone — Bulwark Pulse** | Heals you and knocks back nearby enemies |
| **Willowisp — Dread Whisper** | Shatters the morale of one random enemy at range |
| **Bloodstone — Vampiric Burst** | AoE damage, returns half as healing |
| **Zephyrglass — Quickening Light** | AoE haste for you and nearby allies |

Equip one in a weapon slot and strike with it during daylight (06:00–20:00) to begin a short charge, then the effect fires. Each activation carries a small chance to shatter the crystal (halved with the **Lasting Lattice** talent); **Waking Light** lets them answer at night too, and **Swift Kindling** halves the charge time.

**Crystalline Chambers** — five towns host the lapidary's craft: **Revyl** and **Varcheg** (Sturgia/Northmen), **Dunglanys** and **Car Banseth** (Battania/Forest Clans), and **Saneopa** (Northern Empire). Visit a Chamber to form a crystal from Silver Ore and a trade good (chance scales with Medicine + Engineering), or buy one at that town's market (restocked weekly). NPC lords of those cultures spawn carrying crystals too.

---

## The Dark Gifts

Grey stone **Dark Altars** stand in the cold cities and in scattered Empire cities. At an altar you don't cast or hoard cold — you buy **permanent** Dark Gifts with blood, will, and focus points (one more for each gift you already bear).

- **Who may bargain:** only the Merciless or Devious (Mercy ≤ −1 *or* Honour ≤ −1). If your heart is too warm, the altar offers a way down — spill a prisoner's blood, or swear a false oath over the dead.
- **The price:** each gift costs a geometrically growing tithe from your prison roster — prisoners first, then prisoners *and* captured lords, alongside the rising focus-point cost.
- **Exclusivity:** bearing even one Dark Gift bars you from Grace and from Nature. Renounce any gift at an altar to walk another road.
- Gifts you own are permanent but **sleep** if you stop being Merciless or Devious, waking again when you return to the dark.

| Gift | Effect |
|------|--------|
| **Iron Veil** | −10% incoming damage |
| **Dark Strike** | Each melee hit erupts for +20 dark damage |
| **Soul Mirror** | Reflects 20% of melee damage back at attackers |
| **Dark Spirit** | A dark shade hunts the enemy each battle (buy up to 3) |
| **Pale Rider's Curse** | Every horse near you dies |
| **Soul Drain** | Each melee hit saps enemy morale |
| **Blood Pact** | Each kill restores your HP |
| **Dread Presence** | Nearby enemies periodically lose heart and may rout |

Ashen lords carry 1–2 gifts by default (often Soul Drain and a Dark Spirit); other genuinely evil lords occasionally carry one.

---

## The Kindled — Elemental Beings

Six kinds of elemental roam the wilds, get summoned, or wake mid-battle: **Stone-Born**, **Frost-Born**, **Sand-Born**, **the Kindled** (flame), **the Risen Tide**, and **the Gathered Storm**. Each fights with its own element on a cooldown, wielding no weapon, wrapped in a continuous particle presence bound to its skeleton and a persistent coloured contour.

- **Roaming bands** breed where raw magic pools across the map — small wild groups you may stumble on.
- **The Kindling** — a rare mid-battle event wakes elemental bodies already on the field to fight alongside whichever side summoned or provoked them.
- **A mage's Spirit Unbinding** summons one as a temporary champion at your side.

Each kind has a visible elemental weakness wheel (Fire beats Water beats Earth beats Wind beats Fire, with Frost uniquely fearing Fire above all) and a physical one — Stone and Sand shatter under blunt weapons but shrug off blades; Flame, Tide, and Storm mostly resist steel and must be answered with magic.

---

## A Reworked World

### Culture and faction rework

Five of Calradia's kingdoms carry new names, new lore, and new leader titles, with matching culture text, troop names, and character-creation cards throughout:

| Vanilla | Now | Ruler |
|---------|-----|-------|
| Vlandia | **The Holy Temple** | High Templar |
| Khuzait | **Tribes of the East** | Priest-King / Priest-Queen |
| Sturgia | **The Northmen** | Jarl |
| Aserai | **The Duneborn** | Sheikh / Sheikha |
| Battania | **The Forest Clans** | High Chieftain |

The three Empire successor states keep their vanilla identity but fight each other for real — their war scoring is weighted so the map's largest realms genuinely bleed each other, not just the small kingdoms around them.

### The Ashen Kingdom

The Ashen chose cold over death. They do not age, they do not negotiate, and they are permanently at war with every other kingdom.

**Core cities:** Tyal (the Heart of Winter), Sibir, Baltakhand, Omor
**Castles and towns:** Urikskala, Kaysar, Dinar, Vladiv, Varnovapol, Tepes, Takor, Khimli, Ov Castle, Mazhadan, and Ostican (a Vlandian port claimed at world's start)

Their settlements are locked to full loyalty, security, food, and prosperity daily, with high-tier garrisons kept topped up. **Conquest is permanent** — take an Ashen city and it stays yours, no auto-return. If the Ashen are ever fully dispossessed, they claim a random non-player city without warning; the cold always finds new ground. Scripted events (**the Ashen Gambit**, **the Ashen Tide**) let the realm strike outward and seize ground beyond its usual borders, and that ground stays theirs the same way.

### NPC mage lords

Roughly 1 in 5 lords is seeded as a mage; roughly 1 in 10 lords is Ashen. In battle they read the tactical situation and throw the element that fits — a root against a charge, a gust to break a crowd, a bolt at a lone target — at a pace and power set by personality and remaining life expectancy. Ashen lords pay no life and cast on a much shorter cooldown. A lord's Unbinding is telegraphed with a long, breakable windup, the same as the player's. NPC casters age and can die from the cost of their own casting — or convert to Ashen under enough strain.

### Bandit mages

A small fraction of eligible bandit units carry a stolen fragment of the fire, with real burnout risk after casting. Two special warband types are guaranteed a mage caster: **Fire Worshippers** (looter and forest bandit descent) and **Ashen Spawn** (large, silent hordes of 120+ troops that arrive by world event and cannot be reasoned with).

---

## Campaign Systems

### Schemes and Betrayals

Talk to a city's Tavern Keeper (or use the town menu directly) and choose to arrange "shadier business" — covert operations against rival lords and settlements: assassination, sabotage, bribery, forged documents, coups, and more. Each scheme runs as a risk-vs-reward push-your-luck operation: field reports, hidden press-on odds, one-use field abilities (Sidestep, Talk It Down), and a real chance of exposure that hits relations, crime rating, and sometimes triggers war. NPC lords run the same schemes against each other — and against you, with counter-intelligence tools (warning whispers, a city sweep, the Clairvoyance spell) to catch them first.

### Take the Lord's Coin — Soldiering

You don't need a great name to make war pay. From **clan level 0**, hire your whole party out to a warring lord as a common soldier — a step below the mercenary contract and available far earlier. Your party folds into his host, marches with him, and auto-joins his battles; you're paid weekly, with a bounty scaling to your clan's standing. Leaving before your agreed term is desertion; serving it out earns a clean release and a bonus.

### Sea trade

Roughly 18 named coastal towns operate harbors. **Charter passage** to sail between ports (fare paid, hazards possible mid-crossing — storms, corsairs, sea fog, flotsam, even something in the deep), or **fund a trade venture** (three tiers of stake, a factor sails and returns days later with profit or loss; up to 3 ventures at once). A mage can bless the cargo to cut a venture's risk, and Wind/Water castings can ward or speed a crossing. Ashen-held ports are noticeably more dangerous to sail into.

### The Exchange

A push-your-luck market speculation game in select towns: pick a stake and a commodity class (staple, crafted, or luxury goods), then each round choose to sell, hold, or speculate hard as your position drifts and crash risk climbs. Trade skill lowers the risk; running out of rounds forces a sale below book value.

### Clan Orders

Any lord leading one of your own clan's parties can be given a standing order — **Travel** to a settlement, or **Hunt** a named enemy lord — freeing you from micromanaging every party in a growing clan. Risky hunts against a stronger target need a Leadership roll to accept.

### The Ashen Ruins

34 named ruin sites across four difficulty tiers, open only to mages. Each is a sequence of narrative challenge rooms — riddle gates, blood locks, sleeping giants, mirror halls, and more — costing aging, troops, or whispers, with skill- and talent-gated odds of passing. A full clear can reward grimoire fragments, reclaimed years, whisper purges, focus points, or renown; NPC mage lords race you to the richer ones.

### The Ember Conclave

A secret society of mage lords who believe the Ashen can be harnessed rather than merely fought — *"their plan ends in ruin; the cold does not negotiate, it consumes."* The Conclave rises through six phases as its influence grows, offering the player contact, then repeatable missions, before its ambitions overreach and the story turns.

### Apprentices

A mage player can occasionally discover a young talented noble worth training — a rare find, capped at three per campaign. Training takes weeks, and a corrupting influence has a small daily chance to turn the apprentice before it finishes.

### The Tavern

Beyond the vanilla drinking game: **listen for rumours** (world-state intel — wars, decline, captive lords, Ashen ground), spend a quiet evening, or — for the land-attuned — smoke **the Old Green**, rare weeds that cost health but make nature magic draw free for a day.

---

## Rival Shadow

The cold ignores nobodies. Once your clan reaches tier 3, one Ashen lord is designated your **Shadow** — a personal antagonist who moves against your holdings every couple of weeks and eventually rides out to confront you directly, in an event you can meet with will, endurance, or a costly withdrawal.

---

## Whisper System

The cold watches. Executing lords, completing dark rites, and losing battles all crack the door — whispers accumulate quietly, decaying with virtuous or quiet conduct, and expressing themselves in rising tiers (ambient warnings, altered altar/sanctuary yields, the Temple's anathema) before a final choice at the threshold: resist, bargain, or accept the cold outright.

---

## Battlefield Events

Occasionally a battle begins under cursed or blessed conditions — Cinder Rain, Ember Tithe, The Rising, Dread, Last Light, Ashen Ground, Frenzy — each with its own visual tell and mechanical effect. Most battles have none; roughly 4 in 10 carry at least one.

---

## Settlement Encounters

Entering or leaving a settlement, or finishing a battle, can trigger a short narrative encounter with a real mechanical consequence — over 40 unique events gated by mage status, Ashen status, renown, and settlement type.

---

## Campaign World Events

A living weekly calendar of general, political, seasonal, and inter-faction events layered on top of vanilla's own — plagues, marches, court intrigue, religious purges, kingdom fractures, and the world-defining beats of the Ashen's spread (the Ashen Gambit, the Ashen Tide, the Ashen March, and more).

### The Sanctuary and The Temple

Temple-owned cities and a handful of Empire towns host a **Sanctuary** — an open-access meditation rite where accumulated alignment (Mercy + Honour + Generosity) determines how quickly and how well a prayer succeeds, at a real cost of HP or aging per round. The Holy Temple itself rises from a single breakaway city sometime after day 100, permanently at war with the Ashen, and can offer a clean-handed player a covenant that cheapens casting — until the Temple names them anathema instead, if their whispers run too high.

---

## Main Quest — The Last Flight of the Dragons

*"There is a way to rekindle the world. One great burning — everything, at once."*

Triggered by defeating an Ashen lord's party for the first time. Three goals — clan dominion, capturing the cold heart of the north, and personal mastery — lead to a final, campaign-defining choice. What that choice does is for you to discover.

## Questline — The Burning Laboratory

*"Someone was experimenting with creating life from the fire."*

Triggered by winning a siege as the attacker (day 80+, likely by day 300, once per campaign). A hidden laboratory offers eleven branching choices, opening onto one of several multi-stage arcs involving a dead emperor, a faction's dangerous gamble, or a road you walk alone — each with real, lasting consequences for the world around you.

## The Great Awakening

Duneborn has reached beyond the sands and touched something ancient. From day 50 onward, a rising chance surfaces the plot — a Duneborn player feeds it, everyone else races to stop it. Where it leads, and what answers if it succeeds, is best found in play.

---

## New-Game Settlement Reassignments

At campaign start, several settlements are moved to better reflect the world state — border towns pulled into the Empire successor states they neighbour, and Ostican claimed for the Ashen. All transferred settlements start at full loyalty and security.

---

## Building from Source

### Requirements

- .NET SDK 6 or later
- A local Bannerlord installation (the build references the game's TaleWorlds DLLs)
- **Windows** to build directly; the project targets `net472`, so on Linux/macOS you need Mono for the .NET Framework reference assemblies

### Environment variables

Both variables must be set — the csproj falls back to a hard-coded Steam path and `Win64_Shipping_Client`, so Xbox / Game Pass users who skip `BannerlordBin` get silent reference-resolution failures.

| Variable | Value |
|----------|-------|
| `BannerlordPath` | Path to your Bannerlord root (folder containing `bin` and `Modules`) |
| `BannerlordBin` | `Win64_Shipping_Client` (Steam) or `Gaming.Desktop.x64_Shipping_Client` (Xbox / Game Pass) |

```powershell
$env:BannerlordPath = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
$env:BannerlordBin  = "Win64_Shipping_Client"   # or Gaming.Desktop.x64_Shipping_Client for Xbox / Game Pass
dotnet build src\TheWitheringArt.csproj
```

The build copies the DLL to your Modules folder automatically.

### Running the tests

The numeric logic lives in pure `*Math.cs` files (no engine types) and is covered by **237 NUnit tests** in `tests/PureLogicTests.cs`:

```powershell
dotnet test tests/AshAndEmber.Tests.csproj
```

You do **not** need a Bannerlord install to build or test. When a local install is present the projects reference the real game DLLs; when it isn't (CI, a fresh clone, an automated agent), they fall back automatically to the community `Bannerlord.ReferenceAssemblies.*` NuGet packages, so `dotnet build` and `dotnet test` work headless.

### Convention checks

A small script enforces two repo rules that prose alone lets slip — no empty `catch {}` blocks, and the version string in sync across the four files that must match:

```bash
bash tools/checks/check-conventions.sh
```

Both the tests and this script run in [CI](.github/workflows/ci.yml) on every pull request.

---

## Troubleshooting

**"The fire does not stir in you."**
You do not carry the Gift. Start a new campaign and accept the prompt.

**"Both hands are full. Free a hand to shape the fire."**
You are wielding a weapon or shield without the Steel discipline. Press **X** to sheathe everything, then cast.

**Casting kills me outright**
You are in a tournament — any spell there is fatal and disqualifying by design.

**Script reports "Could not auto-detect your Bannerlord installation"**
Pass the path manually: `.\install.ps1 -BannerlordPath "D:\Games\Mount & Blade II Bannerlord"`

---

## Changelog

Full, version-by-version release notes live in **CHANGELOG.md**, included with every release — this README covers what the mod *is*, not its history.
