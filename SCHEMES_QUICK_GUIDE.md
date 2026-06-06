# Schemes — Quick Guide

Schemes are a covert operations system that lets you (and rival lords) sabotage enemies from the shadows. Bribe garrisons, forge documents, poison wells, hire assassins — or risk it all on a coup. NPCs run their own plots independently, so watch your back.

---

## How to Start a Scheme

1. Enter any **town** you own or can freely enter.
2. Talk to the **Tavern Keeper** → *"I have some shadier business that needs arranging."*
3. Pick a scheme, choose your target, review the cost and odds, then confirm.

---

## The 12 Schemes

### Lord Schemes (target an enemy hero)

| Scheme | Skill | Gold (base) | Influence (base) | Base Odds | Effect on Success |
|---|---|---|---|---|---|
| **Assassinate a Lord** | Roguery | 6,000 | 80 | 25% | Target dies |
| **Hire an Assassin** | Roguery | 2,500 | 65 | 33% | ~20% of target's troops wounded |
| **Forge Documents** | Charm | 2,000 | 35 | 40% | Target −55 relations with their own faction leader |
| **False Accusations** | Charm | 1,500 | 25 | 45% | Target clan loses 5% renown |
| **Viper's Counsel** ★ | Charm | 1,800 | 40 | 40% | Target clan −7% renown; you gain 30–50 renown |
| **Scatter the Wolves** | Roguery | 2,500 | 35 | 35% | 5–8 bandit parties flood target's kingdom |

★ *Viper's Counsel can only target lords inside your own kingdom.*

### Settlement Schemes (target an enemy town or castle)

| Scheme | Skill | Gold (base) | Influence (base) | Base Odds | Effect on Success |
|---|---|---|---|---|---|
| **Stage a Coup** | Charm | 4,500 | 70 | 20% | Loyalty −40, Security −35 |
| **Poison a Well** | Roguery | 2,200 | 40 | 38% | 20–60 garrison militia killed |
| **Bribe Soldiers** | Charm | 2,200 | 40 | 32% | 20–50 garrison troops desert |
| **Burn a Storage** | Roguery | 2,000 | 30 | 40% | Food −50%, Prosperity −15% |
| **Spread Terror** | Roguery | 1,500 | 25 | 40% | Security −25 to −45 |
| **Spread Rumors** | Charm | 1,200 | 15 | 35% | Loyalty −15, Prosperity −8% |

---

## Costs Scale with Target Tier

Costs increase with the target's clan tier. The higher the tier, the more it costs to move against them.

- **Gold multiplier:** `1 + (tier × 0.40)` — up to 3.4× base cost at tier 6
- **Influence multiplier:** `1.4 ^ tier` — up to ~7.5× base cost at tier 6

*Example: Assassinating a tier-6 lord costs roughly 20,000 gold and 600 influence.*

---

## Odds Formula

```
Success chance = Base% + (Skill ÷ 600 × 30%) − (Settlement Security ÷ 400) − (Clan Tier × 2.5%)
```

- Clamped between **5% and 85%**
- Ashen targets impose an additional **−30%** penalty

High Roguery or Charm nudges your odds up; high-security settlements and powerful clans push them down.

---

## The Gambit (Minigame)

Once you confirm a scheme, you play **The Gambit** — a blackjack-style push-your-luck game.

**Each round:**
1. A **complication** is drawn with a point value.
2. Choose **DRAW** (add it to your total and continue) or **STAND** (lock in your total and resolve).

**Three outcomes:**

| Result | Condition | Consequence |
|---|---|---|
| **Bust** | Total > 21 | Catastrophic failure — see below |
| **Success** | Risk Sum ≤ Total ≤ 21 | Full scheme effects apply |
| **Small Loss** | Total < Risk Sum | Nothing happens; costs are still paid |

Each scheme has its own **Risk Sum** (your minimum target) and **card range**. Easier schemes have lower risk sums and smaller cards; harder ones demand high totals with wilder swings.

| Scheme | Risk Sum | Card Range |
|---|---|---|
| Spread Rumors / False Accusations | 7 | 1–5 |
| Spread Terror / Burn Storage / Bribe Soldiers | 9 | 1–6 |
| Poison Well / Forge Documents | 10 | 2–6 |
| Scatter Wolves / Viper's Counsel | 12 | 2–7 |
| Hire Assassin | 13 | 2–7 |
| Stage Coup | 14 | 3–8 |
| Assassinate | 16 | 3–8 |

### Roguery Unlocks

| Roguery | Ability | What it Does |
|---|---|---|
| 100+ | **SKIP** | Discard the current card and draw the next one (once per scheme) |
| 200+ | **PEEK** | See the next card's value before deciding (once per scheme) |

---

## Bust Consequences

Going over 21 doesn't just fail — it blows back on you hard. Each scheme has its own bust penalty:

| Scheme | Bust Result |
|---|---|
| **Assassinate** | Assassin captured; Crime +80, Relations −80 with target, 60% chance of war |
| **Hire Assassin** | Betrayed; your party takes 20% troop casualties, Crime +50 |
| **Forge Documents** | Traced back to you; Relations −60 with faction leader, Crime +40 |
| **False Accusations** | Rebounds; your clan loses 10% renown, −60 relations with target |
| **Viper's Counsel** | Your king turns on you; −80 relations with your ruler, −60 with target |
| **Scatter Wolves** | Bandits flood your roads; 3 bandit parties spawn in your kingdom, Crime +40 |
| **Stage Coup** | Riots traced to you; Crime +50, your settlement Loyalty −25, Security −20 |
| **Poison Well** | Wrong supply line; Crime +70, your settlement food ×0.60 |
| **Bribe Soldiers** | Soldiers report you; Crime +60, −70 relations with settlement owner |
| **Burn Storage** | Catastrophic fire; Crime +60, target food ×0.25, Prosperity ×0.70, −70 relations |
| **Spread Terror** | Too organized; Crime +70, −70 relations with settlement owner |
| **Spread Rumors** | Turns on you; Crime +40, your settlement Loyalty −20, Prosperity ×0.92 |

---

## When You're Caught (Without Busting)

If a scheme fails and your agent is caught (30% chance on failure):

- Crime rating +30–60 in the target's kingdom
- Relations −60 to −80 with the target
- Assassination or Coup caught: **40% chance of war declaration**
- **Viper's Counsel always exposes you on failure** — no silent option

70% of the time on failure, the agent simply retreats without consequence.

---

## Cooldowns & Repeat Use

- **Assassination:** Hard 14-day block on retargeting the same lord
- **All other schemes:** 7-day window; retrying the same target within that window costs **5× gold and influence**

### Retaliation Discount

If a rival lord runs a scheme against you (your hero or one of your settlements), a **1-day window** opens where all your schemes cost **50% less**. Strike back fast.

---

## NPC Schemes

Lords scheme on their own. Each day, eligible lords have a small chance (~3%) of launching a plot against a foreign kingdom. Personality matters — calculating, dishonourable, and merciless lords scheme readily; honourable, merciful lords rarely bother.

When an NPC scheme succeeds or fails, you'll see a notification in the campaign log. If one targets you directly, that triggers the retaliation discount above.

---

## Trait Costs

Every scheme you commit to costs:

- **Honor −1**
- **Calculating −1**
- **Mercy −1** (assassination only)

Plan accordingly if you're managing your character's traits.

---

## Skill Gains

Successfully completing a scheme awards Roguery or Charm XP (400–1,500 points depending on complexity). Running schemes is a solid way to train these skills passively.
