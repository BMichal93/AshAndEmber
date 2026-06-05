# Ash and Ember — Changelog

---

## v0.13.1

### Fix: Scheme notification messages shortened

All scheme success and failure messages condensed to 1–2 sentences, matching the length of spell effect descriptions.

### Fix: Reap — age crash on lord execution

Executing a lord with the Reap talent could drop the player's age to 18 (the Bannerlord hero minimum). Two causes fixed:
- `_executedLordIds` was not persisted to the save file. After a save/load the guard set was empty, so Bannerlord's duplicate `HeroKilledEvent` (an engine quirk on reload) fired the 100-day rejuvenation a second time — or more if the player had saved and loaded several times around an execution.
- `_executedLordIds` was cleared every daily tick, further widening the window for the duplicate event to slip through.

Both are now resolved: the set is saved/loaded with the campaign and is never cleared (executed lords are permanently dead; their IDs accumulate at negligible memory cost).

A secondary hard-floor safety net was also added to `RejuvenateHero`: after `SetBirthDay`, if floating-point drift somehow pushed age below 20, it snaps back.

### Fix: "Join The Temple" event did not switch kingdom

Selecting "Join The Temple" in the Temple founding event had no effect when the player was already in another kingdom. `ChangeKingdomAction.ApplyByJoinToKingdom` silently ignores the call if the clan has an existing kingdom. The handler now calls `ApplyByLeaveKingdom` first (matching the Ashen join pattern in `OnPlayerBecameAshen`), then joins the Temple.

### Version bump: v0.13.0 → v0.13.1

Both `SubModule.xml` files updated.

---

## v0.13.0 — The Burning Laboratory

### Balance: Campaign map spell rebalance

| Spell | Change |
|---|---|
| **Fade** | Duration 2 days → **1 day**. Was near-free permanent stealth at 0.5 aging days/day. |
| **Unsettle** | Morale −60 → **−40**, range 100 m → **75 m**. −60 could instant-route any party from far off the map. |
| **Extinguish** | Kills 5–12 → **3–8**, range 60 m → **45 m**, morale penalty −25 → **−20**. |
| **Clairvoyance** | Influence +40 → **+25**, gold alternative 1 000 → **700**. Influence is the scarcest campaign resource. |

Kindle and Wither unchanged.

---

### Balance: Enchantment rebalance (geometric spell cost follow-up)

All eight battle enchantments updated to provide continuous scaling value at higher input counts, compensating for the geometric aging cost curve introduced in the previous rebalance. Changes apply to both player and NPC mage lords.

| Enchantment | Change |
|---|---|
| **Scatter** | Push 4 m → **5 m** per Damage input. Slow duration 1 s → **1.5 s** per input. |
| **Smoulder** | Morale drain −12 → **−15** per Damage input. |
| **Sunder** | Attack reduction 8% → **10%** per input, cap 40% → **50%**. Damage vulnerability cap 40% → **50%**. Duration fixed 8 s → **8 s + 1.5 s per input**. |
| **Immolate** | Guaranteed kills now scale: **1 kill per 3 Damage inputs** (3 = 1, 6 = 2, 9 = 3). |
| **Ashveil** | Immunity duration 3 s → **4 s** per Restore input. |
| **Cinder Shell** | Duration fixed 8 s → **6 s + 1.5 s per input**. |
| **Hearthlight** | Morale boost 12 → **15** per Restore input. |
| **Reflect** | Reflection cap 40% → **50%**. Duration 1 s → **1.5 s** per input. |

---

## v0.13.0 — The Burning Laboratory

### New feature: Questline — The Burning Laboratory

A major multi-branch questline seeded by a siege victory on or after campaign day 80.

**Trigger**
- Player must win a siege as the attacking side.
- Cannot fire before campaign day 80. Probability scales from ~2 % per siege at day 80 to ~85 % per siege at day 300+.
- Fires at most once per campaign.

**Initial discovery (11 choices, pruned to available factions)**
- Destroy → +Honour, quest ends.
- Keep → Questline C begins.
- Sell → +10 000 gold, −Honour; 50 % chance book reaches a random living imperial court → Questline A.
- Give to Rhagea / Lucorn / Gairos (imperial leaders, if alive) → Questline A.
- Give to Sturgians / Khuzaites / Battanians / Aserai / Vlandians → Questline B.

**Questline A — The Resurrection of Arencios**
- Imperial court performs the ritual. 3-day → 10-day → 3-day → 3-day phase sequence.
- Arencios possesses a random male clan-leader lord of the receiving empire; that lord's clan becomes the ruling clan.
- Secretly rolled: True Emperor (wars everyone including Ashen) or False Emperor (after 30 days, enforces peace with Ashen daily).
- Other two empire factions each have 50 % chance to submit: make peace and share wars (kingdoms intact).
- Arencios declares war on all non-imperial factions.
- On Arencios's death: his empire's fiefs redistribute to surviving empire factions.

**Questline B — The Faction's Gambit**
- 3-day delay then equal-probability outcome roll.
  - Discard (1/3): narrative end.
  - Bad (1/3): all faction towns and castles flip to Ashen, one every 3 days.
  - Good (1/3): weekly — each lord party in the faction gains 30 tier-4 troops; 20 % per week chance to collapse into the bad outcome instead.

**Questline C — Personal Rites**
- Weekly prompt while player holds the scrolls.
- Discard: quest ends.
- Perform rite: +50 Renown, large XP (Athletics/Medicine/Roguery/Leadership/Charm), −Honour, 5 % chance to become Ashen.

**Save/load safety**
- All quest state saved under `BLQ_*` keys. Fully compatible with existing saves (treats missing keys as fresh start).

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
