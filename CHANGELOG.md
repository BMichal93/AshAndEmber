# Ash and Ember — Changelog

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

*(previous release — no changelog recorded)*

---

## v0.11.x

*(previous releases — no changelog recorded)*
