# Ash and Ember — v0.14.1

A Mount & Blade II: Bannerlord magic overhaul centred on the Inner Fire: a single, versatile force shaped by the caster's will. Lords who carry it fight differently. Bandits who steal it burn. The Ashen march from the north and do not negotiate.

---

## Package Structure

```
AshAndEmber/
├── SubModule.xml                    mod manifest
├── ModuleData/
│   ├── items.xml                    (reserved)
│   └── troops.xml                   (reserved)
├── src/                             ~9 400 lines across 34 source files
│   ├── MagicSystem.cs               module entry point + mission behaviour
│   ├── MageKnowledge.cs             gift tracking, grimoire UI, talent menu
│   ├── SpellBuilder.cs              two-phase input parser → SpellCast
│   ├── SpellMinigame.cs             arcane sequence memory game for campaign map casting
│   ├── TalentSystem.cs              21 talents (7 passive, 8 enchantment, 6 spell)
│   ├── AgingSystem.cs               casting cost (days of life), Blight path
│   ├── MagicInputHandler.cs         keyboard/gamepad combo detection
│   ├── CampaignBehavior.cs          new-game setup, aging, map event hooks
│   ├── CampaignMapEvents.cs         27 world events across two independent weekly slots
│   ├── BattleEvents.cs              per-battle battlefield events with atmospheric visuals
│   ├── DragonQuestSystem.cs         main quest — The Last Flight of the Dragons
│   ├── BurningLabQuestSystem.cs     questline — The Burning Laboratory (3 branching arcs)
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

**Free hand required.** You cannot cast while wielding anything — weapon or shield. Sheathe everything first (**X** on keyboard, or the sheathe button on gamepad). Both hands must be empty.

---

## Spell Forms (before Break)

| Key | Arrow | Form | What it does |
|-----|-------|------|--------------|
| W | ↑ | **Blast** | Forward cone. Range = max(4, formCount × 2.5) m. Cone visuals scale to match. |
| A | ← | **Missile** | Fast projectile that travels forward then explodes. Range = max(8, missileCount × 3) m. Explosion radius = 1 + missileCount m. |
| D | → | **Barrier** | Wall of stationary fire nodes perpendicular to facing. One node per press, 1.5 m apart. Cast again to release. |
| S | ↓ | **Burst** | Circle centred on caster. Radius = max(2, formCount × 2.5) m. |

### Multi-form example

`WW SS X UUU` — Blast (5 m) + Burst (5 m) simultaneously, 75 fire damage to all units in range including allies. 7 inputs = 8 days cost.

---

## Effects (after Break)

| Key | Arrow | Effect | Per count | Targets |
|-----|-------|--------|-----------|---------|
| W | ↑ | **Damage** | 25 fire damage | All units — friendly fire included |
| A | ← | **Damage** | 25 fire damage | All units — friendly fire included |
| D | → | **Damage** | 25 fire damage | All units — friendly fire included |
| S | ↓ | **Restore** | 15 healing | Allies (Burst also heals caster) |

Damage and Restore may be combined in the same cast.

---

## Aging Cost

Every spell draws on your lifespan. Cost scales **geometrically** with total inputs — weak spells are cheap; powerful spells become very expensive. Hard cap: 84 campaign days (1 Bannerlord year = 4 seasons × 21 days).

| Total inputs | Cost | With BattleMage |
|--------------|------|-----------------|
| 1–2 | 1 day | 1 day |
| 3 | 2 days | 1 day |
| 4 | 3 days | 2 days |
| 5 | 4 days | 3 days |
| 6 | 5 days | 4 days |
| 7 | 8 days | 7 days |
| 8 | 11 days | 10 days |
| 9 | 15 days | 14 days |
| 10 | 21 days | 20 days |
| 12 | 41 days | 40 days |
| 14 | 80 days | 79 days |
| 16+ | 84 days (cap) | 83 days |

**Tempered** talent reduces the cost by 1 day (minimum 1 — battle casts are never free), plus up to 30% age-based reduction after age 40.

### Campaign map casting cost

Campaign map spells escalate in cost with each use per calendar day:

| Cast # that day | Cost |
|-----------------|------|
| 1st | 1 day |
| 2nd | 7 days |
| 3rd | 14 days |
| 4th | 21 days |
| … | +7 per additional cast |

The counter resets at midnight — a notification appears in the message log. **Resonance** talent gives a 25% chance to skip the cost entirely on any cast. Ashen players pay criminal rating instead of days (see below).

### Becoming Ashen

At age 100 a prompt appears: *The Last Ember*. You may:

- **Take the cold** — become Ashen. Aging stops permanently (the cost of casting is now crime rating, not years). Your appearance changes: ash-white, grey-tinged. Your lords and the Ashen kingdom treat you as one of their own.
- **Let it end** — die of old age.

Ashen mages are completely immune to all magical aging — both the per-cast aging and the daily age check.

### Possession (Ashen players)

Ashen mages do not age, but repeated casting each day risks the cold stirring against them. After the first working each day, each further cast has a **33% chance** to trigger the **Possession** event:

**The Flame Turns** — Dark instincts and cold flame flood your body. You must choose:

| Choice | Outcome |
|--------|---------|
| **Surrender to it** | Death — the cold claims what it wants. |
| **Focus your will** | Leadership test. Success chance = skill × 0.3% (max 90%). Fail → death. |
| **Overwhelm it with your body** | Athletics test. Success chance = skill × 0.3% (max 90%). Fail → death. |

This is the balancing cost of immortality — spamming map spells as an Ashen carries real risk.

### Tournament

Casting **any** spell during a tournament kills and disqualifies you instantly.

---

## Talents

Talents are learned through the grimoire (Alt+X → *Talents*). The **Gift** is free. The first 9 purchased cost 1 focus point each; 10th onward costs 2 points.

### Passive

| Talent | Effect |
|--------|--------|
| **Gift** | You carry the fire. Battle casting enabled. |
| **Tempered** | Each battle cast costs 1 fewer day (minimum 1). Beyond age 40, each year reduces cost by an additional 0.5%, up to 30% total. |
| **Resonance** | One in four campaign map castings costs no days. |
| **Kinship** | +10 relations with mage lords; relation cannot fall below 0 with them. |
| **Reap** | Executing a captured lord restores 100 days. Raiding a village restores 5 days (7-day cooldown). Each discarded prisoner: 5% chance to restore 1 day. Learning this marks you. |
| **Ember** | 5% chance per battle kill to restore 1 day of youth. |
| **Flashfire** | Each battle spell has a 10% chance to echo — firing again instantly at no aging cost. |

### Enchantment

Enchantments fire automatically on every qualifying cast in battle.

**Damage enchantments** (trigger: Damage effect — applies to all hit units, allies included):

| Talent | Effect |
|--------|--------|
| **Scatter** | Blasts enemies backward (5 m per Damage input) and slows movement 25% per input (max 75%) for 4 s + 1.5 s per input. |
| **Smoulder** | Scorches enemy morale (−15 per input) and bewilders non-hero enemies with a random effect: rout, charge, dismount, or morale fracture. |
| **Sunder** | Increases all damage enemies receive and reduces their attack power. Damage vulnerability = 5% per Damage input (max 50%). Attack reduction = 10% per Damage input (max 50%). Duration = 8 s + 1.5 s per Damage input. |
| **Immolate** | Sets enemies alight — bonus burn damage (10 per Damage input). Guaranteed kills scale with Damage inputs: 3 = 1 kill, 6 = 2 kills, 9 = 3 kills. |

**Restore enchantments** (trigger: Restore effect on allies):

| Talent | Effect |
|--------|--------|
| **Ashveil** | Brief magic immunity for healed allies. Duration = 4 s per Restore input. |
| **Cinder Shell** | Reduces incoming damage (5% per input, max 50%) for 6 s + 1.5 s per input. Near-full-health allies also gain a 15 HP damage shield per input for 5 s. |
| **Hearthlight** | Lifts allied morale. Boost = 15 per Restore input. |
| **Reflect** | Healed allies reflect 8% of melee damage per input (max 50%) back at attackers for 3 s + 1.5 s per input. Ranged hits do not trigger the reflection. |

### Spell (campaign map)

Cast from the grimoire on the campaign map. Costs 1 aging day for the first cast each day; escalates sharply for repeated use. Crime rating instead of days if Ashen. NPC mage lords also cast these on the campaign map.

#### Arcane sequence

When you cast a campaign spell, three arcane words drawn from that spell's word pool appear on screen. You are then asked to identify each word in order from a shuffled list of four options. Your recall score scales the spell's output power — the aging cost is always paid regardless of score.

| Correct | Multiplier | Flavour |
|---------|-----------|---------|
| 3 / 3 | **1.25×** | Resonance — the incantation was perfect. |
| 2 / 3 | **1.00×** | The working takes hold. *(baseline)* |
| 1 / 3 | **0.85×** | The words blur — the fire catches unevenly. |
| 0 / 3 | **0.75×** | The words scatter — the fire finds its own shape. |

A **"Cast without the rite"** button on the sequence screen skips the minigame and fires the spell at 1.00×.

The values in the table below are baseline (2/3 recall, 1.00×).

| Talent | Effect |
|--------|--------|
| **Kindle** | Party morale +40 and up to 8 wounded soldiers per troop type recover. |
| **Unsettle** | Nearest enemy party within 75 map-units loses 40 morale and −10 influence. |
| **Wither** | Nearest enemy village loses 20% of its hearth. |
| **Clairvoyance** | +25 influence, or +700 gold if not in a kingdom. |
| **Extinguish** | 5–12 soldiers in the nearest enemy party within 60 map-units are wounded or killed; −30 morale. |
| **Fade** | Your party is concealed from enemy scouts for 2 days. A perfect recall (3/3) extends this to 3 days. |

---

## NPC Mage Lords

At campaign start roughly 20% of lords are seeded as mages. A subset are **Ashen lords** — they cast with no aging cost, shorter cooldown, and heavier spell recipes.

### Campaign map casting

NPC lords cast on the campaign map independently. Ashen lords cast approximately every 3–7 days; regular mage lords every 5–10 days. Older lords cast less frequently. At most one Ashen lord and one regular mage lord produce a visible cast notification per in-game day.

### Battle AI priority

1. **Defensive burst** if HP < 40% and enemies within 8 m — clears close threats instead of warding.
2. **Heal burst** if HP < 30%.
3. **Heal burst** for allies below 50% HP within 15 m (non-Ashen lords only — Ashen fight on regardless).
4. **Attack** based on lord personality:
   - *Ashen*: rotating heavy Blast/Burst recipes (up to 6 inputs), never idle.
   - *Calculating*: prefers Burst when multiple enemies are in the area; precise Blast otherwise.
   - *Impulsive*: forward Blast-heavy, high tempo.
   - *Default*: balanced Blast/Burst mix.

Ward is no longer castable by NPC lords — it is now a Restoration talent available only to the player.

NPC mage lords briefly sheathe their weapon immediately before casting. The cast fires roughly 0.7 seconds later, giving the sheath animation time to complete. The AI re-wields automatically after the spell resolves.

Ashen lords skip the no-enemies early exit and cast proactively at all times. First cast is delayed 12 seconds; subsequent casts use the lord's trait-modified cooldown. Ashen spells — both NPC and player — display cold-blue and grey visuals.

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

A cooldown of 6 days prevents back-to-back encounters. Six new dark-themed events have been added:

| Event | Trigger | Description |
|-------|---------|-------------|
| **Darkness in the Roots** | Enter village | Signs of Ashen cultists. Burn the village (crime +50, 50% −60 relations) or spare them (50% nothing, 50% 200 Ashen Spawn raid anyway). |
| **The Pyre** | Enter village | A girl is bound to a stake. Let her burn (Calculating +1), watch for fun (Mercy −1), stop them (Mercy +1; 50% she was Ashen and casts a curse), or ride past. |
| **The Priest at the Gate** | Enter town | A priest asks for funding to build a Sanctuary. Donate 10 000g (guaranteed), 5 000g (50%), 500g (5%), decline, or have him beaten (Mercy −1). |
| **The Circle Closes** | Leave village | Ashen Spawn surround you. Embrace the cold (become Ashen), run (Athletics check), fight (best blade skill check), or burn them with magic (age 3 days). |
| **Ash in the Dream** | Leave village | A dream reaches out to you. Accept (become Ashen), refuse, or inquire (30% wounded / 20% become Ashen / 50% free focus point). |
| **Three Figures at the Crossroads** | Leave village | Three witches invite you. Join (−2 years, Honor/Mercy −2), ride past (nothing), or scatter them (free focus point; 50% cursed: +1 year). | Encounter chance: 10% per settlement transition; 14% per field battle; 22% per siege or raid.

---

## Schemes and Betrayals

When visiting any city, talk to the **Tavern Keeper** and choose *"I have some shadier business that needs arranging."* A scheme menu opens letting you plan covert operations against lords or settlements.

### How it works

1. **Choose a scheme** — pick from the list, which shows your success chance and cost.
2. **Choose a target** — lord (for lord-targeted schemes) or settlement (for city/castle schemes).
3. **Confirm** — pay gold and influence upfront. The scheme executes in **1–3 campaign days**.
4. **Result** — success applies the effect; silent failure leaves no trace; exposed failure hits relations hard.

### Personality cost

Requesting **any** scheme costs **Honor −1 (Dishonorable)** and **Calculating −1 (Devious)** — paid immediately on confirm, before the scheme resolves. Ordering an **assassination** also costs **Mercy −1 (Merciless)**. All costs are shown on the confirmation screen before you commit.

### Failure outcomes

- **70% of failures** — **Agent fled**: brief notification only. No trace, no consequences.
- **30% of failures** — **Agent caught**:
  - Crime rating +30–60 in the target's kingdom (only if the kingdom is not eliminated).
  - Relations −60 to −80 with the target or settlement owner (only if they are alive).
  - Assassination and Stage a Coup caught: **40% chance of war declaration** (only if both kingdoms exist, are not eliminated, and are not already at war).

**Viper's Counsel always exposes on failure** — court intrigue has no silent slip. Relations −50 to −70 with the target lord and −30 to −50 with the king, regardless of whether an agent was literally caught.

### Success formula

`baseChance + (skill / 600 × 30%) − (security / 400) − (clanTier × 2.5%)` — capped at 5–85%.

**Ashen targets**: additional −30%. Near-impossible without max Roguery/Charm.

### Gold cost

Base × `(1 + target clan tier × 0.4)` — tier 0 = 1×, tier 6 = 3.4×. Shown exactly in the target-selection UI before you commit.

### Repeat-use penalty

| Scheme | Cooldown after any attempt | Repeat within window |
|--------|---------------------------|---------------------|
| Assassinate a Lord | **14-day hard block** — cannot be queued at all | — |
| All other schemes | **7 days** | **5× base cost** |

When a cooldown expires the player receives a notification: *"Contacts reset — the path to [target] is open again"* or *"Network cooled — [scheme] may be repeated at normal cost."*

### Scheme list

| Scheme | Skill | Base gold | Influence | Base % | Effect on success |
|--------|-------|-----------|-----------|--------|-------------------|
| **Assassinate a Lord** | Roguery | 6 000 | 120 | 25% | Target lord dies. |
| **Hire an Assassin (wound)** | Roguery | 2 500 | 65 | 33% | ~20% of target's party troops wounded. |
| **Forge Documents** | Charm | 2 000 | 55 | 40% | Target lord −55 relations with their faction leader (if alive). |
| **False Accusations** | Charm | 1 500 | 40 | 45% | Target clan loses 5% renown (min 50). |
| **Stage a Coup** | Charm | 4 500 | 100 | 20% | Loyalty −40, security −35. Rebellion likely. |
| **Poison a Well** | Roguery | 2 200 | 60 | 38% | 20–60 garrison militia killed. |
| **Bribe Soldiers** | Charm | 2 200 | 60 | 32% | 20–50 garrison troops desert. |
| **Burn a Storage** | Roguery | 2 000 | 45 | 40% | Food −50%, prosperity −15%. |
| **Spread Terror** | Roguery | 1 500 | 35 | 40% | City security −25–45. |
| **Spread Rumors** | Charm | 1 200 | 20 | 35% | Loyalty −15, prosperity −8%. |
| **Viper's Counsel** ★ | Charm | 1 800 | 60 | 40% | Target clan loses 7% renown (min 50). Your clan gains 30–50 renown. **Same-kingdom lords only.** Failure always exposes. |
| **Scatter the Wolves** | Roguery | 2 500 | 50 | 35% | Spawns 5–8 bandit/deserter parties across the target lord's entire kingdom, each anchored to a hideout. |

★ *Viper's Counsel can only target lords within your own kingdom.*

### Arrange covert business (city menu)

In any town, a direct **"Arrange some covert business"** option is available in the main town menu — no tavern dialogue required. Conditions and costs are identical to the tavern route.

### Debug mode

Press **Ctrl + Shift + F10** on the campaign map to toggle scheme debug mode. While active, all schemes cost nothing and always succeed. When toggled **on**, this also queues **The Temple** event to fire on the next weekly tick (if it hasn't fired yet). Toggle again to restore normal mode.

### Balance notes

- One scheme in flight at a time. Schemes and campaign map spells share the same gold and influence pool — using one limits what you have for the other.
- The UI shows exact tier-scaled cost and any active repeat penalties before committing.
- Crash safety: eliminated kingdoms cannot receive crime rating or war declarations; dead heroes cannot receive relation changes. All checked before applying.

### NPC lords

A random NPC lord may initiate a scheme each day — at most one new scheme launches per day globally. Each lord has a 20–35 day personal cooldown between schemes. NPCs never target the player directly. NPC scheme results appear in the campaign message log (not as popup notifications).

- **Standard lord and settlement schemes** can target lords from any foreign kingdom — not just current enemies. Schemes work in peacetime too (intelligence operations, sabotage, court intrigue).
- **Ashen targets** are valid but uncommon (15% weighting when non-Ashen targets exist) and face an additional −30% success penalty.
- **Viper's Counsel** (NPC) targets a rival clan within the same kingdom — court intrigue runs both ways.
- **Scatter the Wolves** (NPC) targets a lord in any foreign kingdom, flooding it with bandits.

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

## Questline — The Burning Laboratory

*"Someone was experimenting with creating life from the fire."*

### Trigger

Win a siege as the attacking side. The event cannot fire before **campaign day 80**, and becomes very likely by day 300. It fires **at most once per campaign**.

### Discovery

Inside the captured keep you find a hidden laboratory stocked with scrolls describing forbidden experiments — creating life from ash and fire. Whoever built this place came close to finishing it.

You are given eleven options (minus those whose faction or leader has been eliminated):

| Choice | Effect |
|--------|--------|
| **Destroy it** | +Honour. Quest ends. |
| **Keep it** | Starts **Questline C**. |
| **Sell it** | +10 000 gold, −Honour. 50 % chance the buyer is an imperial contact → starts **Questline A** with a random imperial faction. |
| **Give to Rhagea** *(empire_s alive)* | Starts **Questline A** with the Southern Empire. |
| **Give to Lucorn** *(empire_n alive)* | Starts **Questline A** with the Northern Empire. |
| **Give to Gairos** *(empire_w alive)* | Starts **Questline A** with the Western Empire. |
| **Give to Sturgians / Khuzaites / Battanians / Aserai / Vlandians** | Starts **Questline B** with that faction. |

---

### Questline A — The Resurrection of Arencios

The receiving faction's scholars attempt to revive the dead Emperor Arencios.

**Timeline:**

| Delay | Event |
|-------|-------|
| +3 days | The rituals begin in secret. |
| +10 days | Arencios is revived — he possesses a random male lord of the receiving empire and is made its faction leader. His nature (true emperor or Ashen spirit) is determined secretly. |
| +3 days | Each of the two other empire factions has a **50% chance** to submit — they make peace and share wars, but keep their own kingdoms. |
| +3 days | Arencios declares war on all non-imperial factions. |

**True Emperor:** Fights all non-imperial factions and the Ashen.

**False Emperor (Ashen spirit):** After 30 more days, the empire secretly allies with the Ashen — peace is enforced daily.

**If Arencios dies:** His empire's settlements are distributed randomly among the surviving imperial factions (empire_s, empire_n, empire_w).

---

### Questline B — The Faction's Gambit

The receiving faction studies the scrolls and attempts the rite. After 3 days, one of three outcomes is rolled (equal probability):

| Outcome | Effect |
|---------|--------|
| **They discard it** | Nothing further. Quest ends. |
| **Goes badly** | The faction is consumed by the Ashen. Every town and castle flips to the Ashen kingdom — one settlement every 3 days until the faction is gone. |
| **Goes well** | Every week, each lord army in that faction gains **30 tier-4 troops**. However, each week there is a **20% chance** the gift collapses into the "goes badly" outcome. |

---

### Questline C — Personal Rites

You keep the scrolls. Every **7 days** a prompt appears:

| Choice | Effect |
|--------|--------|
| **Discard the book** | Quest ends peacefully. |
| **Perform a rite** | +50 Renown · large XP gain (Athletics, Medicine, Roguery, Leadership, Charm) · −Honour · **5% chance of becoming Ashen** |

The weekly prompts continue indefinitely until you discard the book or the Ashen transformation occurs.

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
- Cast spells with no aging cost; 6-second cooldown. Spells display cold-blue and grey visuals.
- Always carry Scatter + Extinguish + BreakWills + Plague. 50% chance of Smoulder. 50% chance of Sunder. 40% chance of Immolate.
- Personality traits locked to Merciless, Closefisted, and Deceitful.
- Captured Ashen lords and Ashen Spawn party leaders refuse all dialogue. Encounters with them end with silence.

### Becoming Ashen (player)

When the player takes the cold at age 100 or surrenders to Ashen captors, their clan is automatically moved into the Ashen kingdom. Their spells change to cold-blue visuals. Criminal rating in their old kingdom spikes on departure. The Ashen kingdom is permanently at war with everyone — joining it means joining that war.

### Criminal status

Non-Ashen players are permanent criminals in Ashen lands. Ashen players have their crime rating cleared daily.

### Permanent war

Peace with the Ashen is impossible — the diplomacy AI scores it at −10 000, and any peace that does get forced through is re-declared within one in-game day. This applies to both the Ashen kingdom and any individual Ashen clan temporarily outside it. All other faction diplomacy is unmodified vanilla behavior.

---

## Campaign World Events

27 events spread across two independent weekly slots. Every 14 days (after the last event fired) the tick opens both slots simultaneously:

- **General slot** — at most one Ashen, political, or seasonal event fires.
- **War slot** — at most one inter-faction war event fires (independent of the general slot, so both can fire the same cycle).

A separate weekly safety net checks every 21 days: if no non-Ashen inter-faction wars exist at all, it directly seeds one.

### General events

| Event | Chance/week | Effect |
|-------|-------------|--------|
| **Ashen Plague** | 8% | Wounds entire garrison of a random city/castle. Spawns 3 Ashen Spawn hordes nearby. |
| **Great Withering** | 10% | Destroys 80% of a village's hearth or halves a city's prosperity. |
| **Ashen March** | 5% | Spawns 6 Ashen Spawn hordes across a random non-Ashen kingdom. |
| **Long Night** | 3% | Forces mod light-level to Dark for 7 days. Each day drains prosperity from every non-Ashen town. |
| **Ashen Tide** | 3% | A random non-Ashen castle is seized by an Ashen lord. Loyalty/security set to max immediately. |
| **Fire Fades** | 1.5% | 2–4 non-Ashen lords aged 25–55 (not clan leaders) die. Their home settlement weakens. |
| **Darkened Roads** | 6% | All caravans of a random kingdom vanish. Town prosperity drops 15%. 2 Ashen ambush parties arrive. |
| **Seeds of Betrayal** | 1.3% | A faction leader is poisoned at their own feast. The clan behind it is expelled from the realm. |
| **Broken Will** | 1% | Once or twice per campaign (after day 60): a faction is drawn into the cold and declares war on all others. |
| **The Long March** | 4% | 4 massive Ashen warbands (100+ troops each) march into Vlandia, Aserai, Khuzait, or Sturgia. |
| **Whispers from the Ash** | 1.5% | 1–3 mage lords abandon their factions and join the Ashen — gaining Ashen title, traits, and cold-fire magic. |
| **Tyranny** | 2% | A faction leader executes all tier-5/6 clan heads. Ruling clan loses all influence. One clan defects. |
| **Stolen Heirloom** | 2% | A rival clan seizes the faction seal overnight — a new ruling clan takes power without a blade drawn. |
| **Peasant Unrest** | 6% | The people of a random kingdom revolt. Three parties of 50 looters spawn near a lord's settlement. |
| **A Wolf in Sheep's Clothing** | 3% | A minor lord in a random kingdom is accused of serving the Ashen. Player gets a choice if in that kingdom (tier 4+ = 4 options; tier <4 = Charm-modified accusation risk). |
| **Mage Fatwa** | 2.5% | Religious fear sweeps a kingdom. 0–3 mage lords (non-Ashen) are hunted and killed by the mob. |
| **The Temple Rises** | 4% (after day 100, once only) | Diathma, Makeb, or Omor breaks from its faction. The city's owner clan founds The Temple — a militant holy order sworn to fight the Ashen. One more clan joins automatically. Player may join. |
| **Iron Winter** | 4% (winter only) | One random northern kingdom (Sturgia or Northern Empire) loses 50% hearth in villages and 50% prosperity/food in cities. |
| **Scorching Sun** | 4% (summer only) | One random desert kingdom (Aserai or Southern Empire) loses 50% hearth in villages and 50% prosperity/food in cities. |
| **Game of Thrones** | 5% on leader death | When a qualifying faction leader dies, the kingdom fractures: all non-ruling clans leave and become independent, keeping their fiefs. Requires 4+ clans; never fires for the Ashen. |

### Inter-faction war events (war slot — independent of general slot)

Each event picks two non-Ashen kingdoms currently at peace and rolls a war chance based on their leaders' relations (hostile: 85%; neutral: 45%; friendly: 10%). If the roll fails, relations drop instead.

| Event | Chance/cycle | What tips the balance |
|-------|--------------|-----------------------|
| **A Slight at Court** | 2.5% | An envoy is publicly turned away; the insult demands an answer. |
| **Border Torches** | 2.5% | Villages near a shared border burn; each crown blames the other. |
| **A Debt in Blood** | 2% | An old grievance resurfaces; the aggrieved party demands satisfaction. |
| **The Broken Betrothal** | 2% | A marriage alliance collapses; the spurned faction answers with steel. |
| **The Treasonous Scroll** | 2% | Documents implicating a lord in treachery surface at court. |

The Ashen are exempt from all betrayal and political-fracture events — their will is cold, singular, and does not break or scheme against itself.

### The Sanctuary

Cities owned by **The Temple** and **two randomly chosen Empire towns** (selected at new-game start, saved with the campaign) have a **Sanctuary** accessible from the town menu.

**Access requirement:** Honor ≥ 1 (Honourable) AND Mercy ≥ 1 (Merciful). Non-qualifying characters cannot use the Sanctuary.

**Temple member discount:** Temple faction members pay 40% less gold and age 40% less for all rites.

**Livestock payment:** The Sanctuary flame values living offerings above coin. Animals from the player's party inventory cover more rite cost than their market value — livestock is the cheaper option. The menu header shows your current livestock value.

| Animal | Gold covered toward rite |
|--------|--------------------------|
| Cow | 150 g |
| Sheep | 40 g |

When the player has both gold and sufficient livestock, a dialog asks which payment to use. When only one option is available, payment resolves automatically.

| Rite | Cost | Effect |
|------|------|--------|
| **Prayer of Strength** | 500g | Party morale +40 |
| **Protective Rites** | 1 000g + 30 days older | Blocks all Ashen world events for 14 days |
| **Turn the Ashen** | 1 500g + 45 days older | Wounds 12–20 soldiers in up to 3 Ashen parties within 200 map units; breaks their morale |
| **Prayer of Healing** | 800g + 20 days older | Fully heals all wounded troops in the party |
| **Prayer for a Blessing** | 5 000g | Rejuvenates the player by ~10 years (hard floor: age 20) |

When Protective Rites are active, any Ashen world event that would fire instead shows a notification that the ward held. The counter ticks down daily and a notification fires when it expires.

**NPC behavior:**
- Honourable + Merciful lords currently in a sanctuary city: **0.3% chance per day** to receive a miracle (healing or morale). A notification appears: *"Miracle — [lord] prayed at the sanctuary in [city]."*
- Temple faction lords: **3% chance per day** to partially heal their wounded; **2% chance** to wound troops in the nearest Ashen party within 100 map units.

### The Temple

Sometime after campaign day 100, **The Temple Rises** fires once and permanently. One of three canonical cities (Diathma, Makeb, or Omor) breaks away from its parent faction, its owner clan founding a new militant kingdom dedicated to ending the Ashen. A second clan joins immediately. The player is offered the choice to join as well.

**The Temple** is always at war with the Ashen (the war is re-declared daily if peace is somehow imposed). It never initiates war on other factions; other factions may declare war on it.

The founding city has its loyalty and security immediately set to 100 to prevent instant rebellion. The Temple is a small kingdom — one city, two clans — and will need allies to survive long-term.

If none of the three canonical cities are eligible (already Ashen-owned, under siege, or their owner clan is unavailable), a fallback city from the Empire, Khuzait, or Sturgian factions is used instead.

### The Ashen Altars

In **Tyal** and one additional Ashen city chosen randomly at campaign start (from Sibir, Baltakhand, or Amprela), a grey stone altar stands in the town. These altars are announced at game start.

**Access requirement:** Mercy ≤ −1 (Merciless) AND Honor ≤ −1 (Devious). The Ashen do not kneel to the virtuous.

**Sacrifice mechanic:** Every rite costs only lives — no gold. Prisoners are drained first (lowest-tier first), then healthy party members if more points are still needed. A tier-N troop is worth N sacrifice points; the altar takes the minimum number needed to cover the cost. Party morale drains proportional to points spent. The menu header shows total available sacrifice points.

| Rite | Sacrifice pts | Effect |
|------|---------------|--------|
| **Blood Tribute** | 5 | Each surviving non-hero troop type gains 75 XP |
| **The Ashen Solstice** | 10 | Call down an Iron Winter (north) or Scorching Sun (south) — the season check is waived by the sacrifice |
| **Carrion Gift** | 8 | Wounds 30–60 % of the garrison in a random non-Ashen town |
| **Break Hearts and Wills** | 6 | A random non-Ashen town loses 15–25 loyalty and 15–25 security |
| **Rite of Cold Fire** | 7 | Wounds 8–15 soldiers in the nearest non-Ashen lord party within 150 map units; −30 morale |

**NPC behavior:**
- Ashen lords currently in an altar city: **0.5% chance per day** to perform a dark rite (partial healing, morale boost, or nearby curse). A campaign-map notification appears: *"Dark Rite — [lord] made an offering at the Ashen Altar in [city]."*

### Player-interactive world events

Three events prompt the player for a choice if their clan is **tier 4 or higher** and **in the affected kingdom**. The dialog appears before effects are applied.

| Event | Support the schemers | Oppose the schemers |
|-------|---------------------|---------------------|
| **Stolen Heirloom** | +50 relations with the usurper clan, −100 with the displaced ruling clan. | −100 with the usurper clan, +20 with the old ruling clan. **33% chance** the coup fails outright. |
| **Seeds of Betrayal** | +50 with the conspiring clan, −100 with the old ruling clan. | −100 with the conspiring clan, +20 with the old ruling clan. **33% chance** the plot is stopped and the leader survives. |
| **Tyranny** | +100 with the tyrant, −50 with every condemned clan. | **33% chance** the player is added to the execution list (game over). |

If the player's clan is below tier 4, or is the direct party in the event (the ruling clan being displaced, etc.), the event fires silently as normal.

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

**"Both hands are full. Free a hand to shape the fire."**  
You are wielding a weapon or shield. Press **X** to sheathe everything, then cast.

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

---

## Changelog

### v0.14.1

**New mechanic — Arcane sequence minigame for campaign map casting**

Casting a campaign spell now triggers a short memory game: three arcane words appear on screen, then you are asked to identify each one in order from a shuffled list of four options. Your recall score scales the spell's output power. The aging cost is always paid.

| Correct | Multiplier |
|---------|-----------|
| 3 / 3 | 1.25× |
| 2 / 3 | 1.00× (baseline — unchanged from previous behaviour) |
| 1 / 3 | 0.85× |
| 0 / 3 | 0.75× |

A "Cast without the rite" button skips the minigame at 1.00× for players who prefer the direct route.

All six campaign spell effects now scale with the multiplier: morale deltas, influence, gold, hearth reduction, troop count, and Fade duration (3/3 extends concealment by one extra day).

---

### v0.12.0

**Balance — Spell aging cost is now geometric (applies to player AND mage lords)**

Battle spell cost now follows a geometric curve. Weak spells stay cheap; powerful spells become meaningfully expensive. Hard cap: 84 campaign days (1 Bannerlord year = 4 seasons × 21 days). Mage lords now pay the same geometric rate — previously they were undercharged. Off-screen battles also now apply a small random aging to mage lords who participated.

| Total inputs | Cost | With BattleMage |
|---|---|---|
| 1–2 | 1 day | 1 day |
| 5 | 4 days | 3 days |
| 7 | 8 days | 7 days |
| 10 | 21 days | 20 days |
| 14 | 80 days | 79 days |
| 16+ | 84 days (cap) | 83 days |

**Bug fix — Minimum mage age is now 20**

Rejuvenation effects (Reap talent, Ember kills) can no longer push hero age below 20.

**Bug fix — Reap lord execution no longer fires twice**

Added deduplication guard against HeroKilledEvent double-firing.

**AI — Enemies scatter after AOE spells**

Surviving non-hero enemies near a Burst, Blast, or Missile explosion scatter outward. Units just outside the hit radius also react.

**AI — Barrier warning zone extended**

Enemies avoid a fire wall from 5 m beyond the barrier edge (was 3.5 m). Hero-tier enemies also nudge away.

**Schemes — "Hire an Assassin (wound)" removed**

Replaced: a failed Assassination now has a 30% chance to bloody the target's escort before the blade breaks off (near-miss outcome instead of a separate scheme).

**World — Ashen lords escape captivity after 3 days**

The cold does not yield to chains. Any Ashen lord held prisoner for 3 days automatically escapes at midnight.

**World — Ashen lords cannot have children**

The cold preserves; it does not create. Births to Ashen parents no longer occur. Instead, mage lords aged 80+ now have a small daily chance of hearing the cold's call and converting to Ashen (chance scales with age). A mage lord who ages 15+ days in a single battle also has an 8% chance of conversion.

**World — NPC mage lords age from all battles, not just player battles**

Mage lords now receive small random aging (1–3 days, 40% chance) from off-screen battles where the player was not present.

### v0.12.2

**AI — Larger NPC blast and burst spells; cost scales automatically**

All mage lord and Ashen lord combat spells now fire at larger form counts, increasing range and radius. Cost adjusts automatically because `RecordCast` feeds each cast through the same geometric aging formula the player uses.

| Situation | Old form | New form | Old range/radius | New range/radius |
|---|---|---|---|---|
| Non-Ashen lord — standard blast/burst | 2 | 3 | 5 m | 7.5 m |
| Non-Ashen lord — near-death defensive burst | 2 | 3 | 5 m | 7.5 m |
| Non-Ashen lord — surrounded (3–4 enemies) | 2 | 3 | 5 m | 7.5 m |
| Non-Ashen lord — surrounded (5+ enemies) | 3 | 4 | 7.5 m | 10 m |
| Ashen lord — standard blast/burst | 2–3 | 3–4 | 5–7.5 m | 7.5–10 m |
| Ashen lord — heavy cast (many targets) | 3 | 4 | 7.5 m | 10 m |
| Ashen lord — near-death defensive burst | 3 | 4 | 7.5 m | 10 m |
| Ashen lord — surrounded (5+) | 3 | 5 | 7.5 m | 12.5 m |

Detection ranges used for the friendly-fire check updated to match (`blastRange` 6→8 m for lords, 8→10 m for Ashen; burst-check radius 5→7.5 m for lords, 5→10 m for Ashen).

Aging cost examples (auto-computed, no manual change needed):
- Standard lord cast: 6 inputs → **5 days** (was 4 inputs → 3 days)
- Ashen heavy cast: 8 inputs → **11 days** (was 6 inputs → 5 days)
- Ashen surrounded 5-cast: 10 inputs → **21 days** (was 6 inputs → 5 days)

---

### v0.12.1

**World events — Whispers from the Ash fires twice as often**

Chance per week raised from 1.5% to 3% (~every 33 weeks instead of ~every 67 weeks). Mage lords defecting to the Ashen are now a more regular part of a long campaign.

**World events — The Temple is nearly guaranteed by day 250**

After day 250 the Temple founding chance jumps from 4%/week to 85%/week, so it fires within 1–2 weeks past that threshold. The normal 4%/week rate still applies between day 100 and day 250.

---

### v0.11.2

NPC spell AI: improved friendly fire avoidance and target-density scaling.

### v0.11.0

Ashen Altars, Sanctuary, Schemes, Dragon Quest, and 27 world events.
