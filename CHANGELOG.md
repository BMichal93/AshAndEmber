# Ash and Ember — Changelog

---

## v0.14.0

### New mechanic: Aging milestones

Surviving as a mage to old age now pays out. At ages 50, 60, 70, 80, and 90 the player receives a narrative event and a permanent boon.

| Age | Boon |
|---|---|
| 50 | +75 renown |
| 60 | +2 relations with all mage lords |
| 70 | +150 renown, party morale +30 |
| 80 | All wounded troops instantly healed |
| 90 | +300 renown |

Milestones are persisted — they do not re-fire on reload.

### New mechanic: Scheme minigame — Press-on system

The scheme minigame has been redesigned. Instead of a single hidden draw, the player now chooses how their operative approaches each development:

- **Push Hard** — aggressive, hidden roll +1 to +7
- **Tread Carefully** — balanced, hidden roll −3 to +3
- **Pull Back** — defensive, always reduces exposure, hidden roll −7 to −1

The exact value is never shown before committing. Each choice costs a round.

**Rounds are limited** and scale with Roguery (base 5, +1 per 100 Roguery, cap 10). When rounds run out without extraction: 50% bust, 50% quiet fail.

**Field abilities** (one use each per operation):
- **SIDESTEP** (Roguery) — skip the current development entirely. Failure: ±8 exposure, round consumed.
- **TALK IT DOWN** (Charm) — spend social grace to cool the heat. Success: −5 exposure. Failure: +5 exposure. Does not consume a round.

RECON has been removed.

**Success thresholds** have been rebalanced upward to match the new round economy (12–19, up from 7–16). All schemes now require meaningful decisions across most available rounds.

### Balance: Clairvoyance — scheme detection

Clairvoyance now reveals any pending NPC scheme targeting the player. A prompt appears offering to cancel it for 2,000 gold.

### Balance: Unsettle — influence drain added

Unsettle now also drains 10 influence from the target clan leader on hit (in addition to the existing −40 morale). NPC Unsettle: when cast against a fellow mage lord, threads interfere — the target mage ages 1 day.

### Balance: NPC Extinguish — mage interference

When NPC Extinguish hits a fellow mage lord, threads interfere — the target mage ages 1 day.

### Balance: Ashen war minimum duration reduced

Minimum war duration before peace is possible: 80 days → 60 days (~2 in-game months).

### Fix: Temple join executed inside UI callback

"Join The Temple" kingdom action was called directly inside an inquiry callback, which is not safe during Bannerlord's campaign state. The action is now deferred to the next daily tick. Save/load persisted.

### Fix: Player can only be targeted by schemes as a hero

NPC scheme targeting logic no longer allows the player to be targeted as a settlement owner — only directly as a hero. Prevents unintended scheme resolution against player-owned settlements.

### Removed: Encounter pool pruning

A large number of settlement, battle, and siege encounters have been removed from the random pool. The remaining events are higher quality and less repetitive.

---

## v0.13.1

- **Fix:** Scheme success/failure messages shortened to 1–2 sentences.
- **Fix:** Reap — executing a lord could drop player age to 18. The executed-lord guard set was not persisted across save/load, causing the rejuvenation to fire again on reload. Set is now saved and never cleared.
- **Fix:** "Join The Temple" event had no effect when player was already in a kingdom. Handler now leaves the current kingdom first before joining.

---

## v0.13.0 — The Burning Laboratory

- **New:** Major multi-branch questline triggered by a siege victory (day 80+, fires once per campaign). Discover a forbidden ritual tome and choose its fate — destroy it, keep it, sell it, or give it to a faction.
  - **Path A — The Resurrection of Arenicos:** An imperial faction performs the ritual. A dead emperor possesses a living lord and seizes control of the empire. May unite the empires or go to war with everyone, including the Ashen.
  - **Path B — The Faction's Gambit:** A non-imperial faction uses the tome. Equal chance of a boon (weekly troop reinforcements) or catastrophe (settlements flip to Ashen one by one).
  - **Path C — Personal Rites:** Player keeps the tome and performs weekly rites for renown, XP, and a growing chance of becoming Ashen.
- **Balance:** Campaign map spells toned down — Fade 2 days → 1 day; Unsettle −60 morale/100 m → −40/75 m; Extinguish kills and range reduced; Clairvoyance influence and gold rewards reduced.
- **Balance:** All eight battle enchantments rescaled upward to compensate for the geometric spell cost curve — higher input counts now continue to provide meaningful returns for both player and NPC mages.

---

## v0.12.0

### Balance: Battle spell cost — geometric scaling

Spell aging cost changed from `ceil(n/2)` (linear) to `round(1.4^(n−1))`, capped at 84 campaign days (1 Bannerlord year).

| Inputs | Old cost | New cost |
|---|---|---|
| 1–2 | 1 day | 1 day |
| 5 | 3 days | 4 days |
| 7 | 4 days | 8 days |
| 10 | 5 days | 21 days |
| 12 | 6 days | 41 days |
| 14 | 7 days | 80 days |
| 16+ | 8 days | 84 days (cap) |

Small spells remain affordable. Large spells become a meaningful sacrifice.

### Balance: NPC mage AI — larger spells

All NPC battle spell form sizes bumped up by one tier. Ashen lords cast one tier larger than regular mage lords. Detection and friendly-fire check ranges updated to match the larger impact zones.

### New mechanic: Ashen lords auto-escape captivity

Ashen lords cannot be held prisoner for long — the cold does not yield to chains. After 3 days in captivity, any Ashen lord automatically escapes. Escape days are tracked per lord and persisted across save/load.

### New mechanic: Ashen lords cannot have children

Children born to at least one Ashen parent die at birth. The cold preserves; it does not create.

### New mechanic: Mage overexertion → Ashen conversion

NPC mage lords who overexert their power in battle (heavy battle spell usage) have a chance (8%) to be pulled toward the cold — converting to Ashen over time.

### Balance: Schemes — influence costs reduced ~30%

All scheme influence costs reduced by approximately 30% to make mid-game scheming more accessible.

### Balance: Schemes — Hire Assassin removed; Assassinate near-miss added

The standalone "Hire an Assassin (wound)" scheme was removed. Instead, a failed Assassination now has a 30% chance of a **near-miss**: the escort is bloodied and soldiers wounded even though the lord survives.

### Fix: RejuvenateHero minimum age floor

Added a 20-year minimum age clamp to `RejuvenateHero`. Reap and other rejuvenation effects can no longer push a mage hero below age 20.

### Content: Trinket quest — text variants

Each dream stage in the trinket settlement encounter now picks from 2–3 variants of its title, description, choice labels, and outcome text. First-dream choice labels are now specific to the trinket type rather than generic.

### Removed: The Lightened Purse campaign event

---

## v0.11.x

*(previous releases — no changelog recorded)*
