# Ash and Ember — Changelog

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
