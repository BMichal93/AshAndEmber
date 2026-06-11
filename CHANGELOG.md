# Ash and Ember — Changelog

---

## Unreleased

### Sea travel, sea trade, and sea battles (new system)
- **Harbors** — 16 coastal towns (Sturgian, Vlandian, Imperial, and Aserai coasts) gain a *Visit the harbor* entry in the town menu.
- **Charter passage** — pay a distance-scaled fare and cross open water in a fraction of the marching time. The crossing plays out as a wait-menu voyage with real hours passing; arrival drops the party at the destination's gate.
- **Sea battles** — corsairs prowl every route (12–40% by distance). Boardings are resolved as abstracted battles weighing party size, troop quality, and Tactics: repel them for loot and renown, pay tribute, run, or — for mages — **Sear the Tide** (3 days aging) to swing the odds hard.
- **Storms** — cost hours and leave soldiers battered; a mage who has called the wind sails through untouched.
- **Sea trade ventures** — stake 500 / 2,000 / 8,000 denars on a route; a factor returns after the round trip with distance-scaled margins, Trade XP — or salvage money, if the sea took the cargo. Up to 3 ventures at once.
- **Mage hooks** — *Call the Emberwind* (2 days aging) halves the next crossing and wards it against storms; *Bless the Cargo* (1 day aging) halves a venture's loss chance and sweetens its margin. The sea, like everything else, burns days.
- **NPCs sail too** — lords leaving a harbor whose AI destination lies across the water take ship 35% of the time (you're notified when it's your kingdom's banner); caravans — including the player's — hop between trade ports opportunistically (20%, legs of 150–700 map units). NPC crossings face the same corsair odds, resolved off-screen against their rosters, and harbor towns gain a small daily prosperity trickle from the traffic.
- A save made mid-crossing reloads safely at the origin port with the fare refunded.

### Rival Shadow — clan tier gate
- The Shadow is no longer designated at campaign start. The cold ignores nobodies: designation now waits until the player's clan reaches **tier 3**, and arrives with a popup (*A Cold Attention*) announcing that the dark forces of the Ashen have taken a personal interest.

### Whisper system — less noise, more signal
- **Killing an Ashen lord now adds +1 whisper (was +3).** Fighting the Ashen is the mod's core loop; it should not be the fastest road to corruption.
- **Quiet-conduct decay** — after 10 consecutive days without gaining a whisper, roughly 1 whisper fades every 3 days regardless of traits. Whispers now reflect recent conduct rather than a permanent stain. The existing virtue decay (Mercy + Honor ≥ 2) is unchanged.
- **Ambient whispers tuned** — they fire less often (tier/20 per day instead of tier/12), never repeat the same line twice in a row, and one in three now carries real intelligence: the compass bearing of the nearest Ashen lord's warband.

### Restore enchantments — rebalanced against damage enchantments
Damage enchantments are split across the Sear/Force/Shred natures, so one cast only feeds the natures it carries. Restore has a single key — every restore enchantment the caster owns fires together on one Restore cast. Each is now tuned weaker individually; the stack is the build:
- **Ashveil** — magic immunity 2 s per Restore input, capped at 10 s (was 4 s per input, uncapped — 20 s blanket immunity at 5 inputs).
- **Cinder Shell** — protection 6% per input, max 30% (was 10%/50%); duration 4 s + 1 s per input (was 6 s + 1.5 s); overheal shield 10 HP per input and only above 90% health (was 15 HP above 80%).
- **Hearthlight** — morale +10 per input (was +15).
- **Reflect** — 5% per input, capped at 25% (was 8%/50%; stacked with Cinder Shell it reached ~70% effective damage reduction).
- Grimoire talent descriptions and README updated to match.

### Deferred dialog queue — story beats are no longer lost
- The map-layer popup slot (`MageKnowledge._deferredInquiry`) was a single Action shared by nine daily systems; when two events queued the same day, one vanished silently — including main-quest beats. It is now a real queue: pending dialogs line up and fire in order instead of overwriting each other. The Rival Shadow duel and the Cold Calls event, which could previously be lost forever to a busy day, now always arrive.

### Immolate — kill cap
- One kill slot per 3 Sear inputs, as before, but only the **first** kill of a cast is certain; each further slot connects at 50%. Unbounded guaranteed kills (3 per cast at 9 Sear) deleted units with no counterplay — for the player and for Ashen / False Emperor AI alike. 1–2 Sear probabilities unchanged (33% / 50%).

### False Emperor — cooldown 3 s → 6 s
- At 3 s, a single False Emperor cast ~100 max-power spells in a five-minute battle. He now casts at the Ashen cadence — still the most dangerous caster in the mod, no longer unanswerable.

### Reap — execution reward scales with the victim
- Executing a captured lord restores **20 days + 10 per tier of their clan (20–80)** instead of a flat 100. A flat 100 bought ~20 large battle spells per execution and trivialised the aging economy.

### Campaign map casting — escalation softened
- Repeat casts per day now cost 1 → 4 → 8 → 12 (+4 each) instead of 1 → 7 → 14 → 21. The 2nd map cast used to cost more than most battle spells, which made map magic read as a punishment rather than a tool.

### Tempered — flat −1 replaced with −25%
- Battle casts cost 25% fewer days (rounded, minimum 1 — never free). The flat −1 was irrelevant on large spells and strictly worse than Kinship's −10% per allied mage; the percentage keeps Tempered competitive solo. Age-based reduction (up to 30% past 40) unchanged.

### Possession — two-strike rule
- The first failed Leadership/Athletics test no longer kills: you are left broken (wounded to near-death, −20 party morale) and **strained for 21 days**; failing again while strained is death. Surrender is still always death. One bad roll should hurt, not end a 100-hour campaign.

### Lost Forms — 3 → 2 focus points
- Lost Forms are sidegrades, not upgrades; at 3 points they were never worth taking over a core talent. At 2 they are a cheap experiment.

### Quality of life
- **Dragon Quest final prompt** now states explicitly that rekindling ends the campaign (hero dies, game over) and that refusing closes the quest but the campaign continues.
- **Sanctuary ↔ Altar cross-interference** (using one halves the other's yield for 30 days) is now shown in both sub-menu headers with the remaining days, instead of silently eating rituals.

### Documentation
- README arcane-sequence table corrected to match the code: recall multipliers are 1.50× / 1.20× / 0.80× / 0.50× (the doc previously claimed 1.00×/0.75× for 2/3 and 1/3). "Cast without the rite" remains 1.00× — blind guessing averages worse than skipping; genuine recall beats both.
- README battle-cost table previously claimed Tempered's minimum was 1 day while the code allowed free casts; code and docs now agree (minimum 1).

---

## v0.18

### Bug fixes
- **Barrier light leak** — area effects that expired naturally only removed one of their three lights; Fading Ward barriers leaked two column lights per node for the rest of the battle. All three are now removed on expiry.
- **Scheme costs lost on reload** — gold, influence, and trait costs are paid when an operation is committed, but the Gambit minigame state did not survive a save/load: reloading mid-operation silently ate the costs. Committed operations are now persisted and re-launched after a reload.
- **Stale daily map-cast counter** — the escalating campaign-cast cost counter was static and unsaved; loading a save mid-day (or a different campaign in the same session) inherited the old counter and overcharged. It now persists with the save.
- **Missile vs teamless agents** — missile detection and explosion treated agents with no team as enemies; they are now neutral (matching Blast).
- **Ashen resurgence partial application** — if the chosen Ashen lord had no clan, the target settlement changed owner but garrison top-up and tracking were skipped. The resurgence now aborts cleanly up front.
- **The Rising false report** — the battlefield event announced reinforcements even when none spawned (missing troop type or no valid anchor). It now reports the actual count or stays silent.
- **New-game static leak** — `AgingSystem.ResetForNewGame` was never called, so aging milestones (and now ledger counters) carried over into a new campaign started in the same game session.
- Removed dead `ModifiedSacrificePoints` helper; fixed a stale clan-tier formula comment in the scheme header.

### Battle magic — damage natures (W/A/D differentiated)
- Every damage key still deals 25 fire damage per press, but each now carries a nature on player casts:
  - **W = Sear** — innate +5 burn per press; the **Immolate** talent amplifies it (kill thresholds now count Sear inputs).
  - **A = Force** — innate 1.5 m concussive push; **Scatter** amplifies it (5 m throw + slow now keyed to Force inputs).
  - **D = Shred** — innate +4%-per-press damage vulnerability for 4 s; **Sunder** amplifies it (full shred keyed to Shred inputs).
  - **S = Restore** — unchanged healing, plus an innate +4-per-press morale lift; **Hearthlight** amplifies it.
- **Smoulder** still triggers on any damage input — fear of fire is universal.
- Owning a key's talent replaces its weak innate effect (no double-dipping). NPC lord casts keep their original all-trigger behaviour. Twin Bolt and Directed Burst preserve the nature split when scaling.

### The Ledger of Years
- The grimoire now opens with a running account of the aging economy: current age, time until the fire burns out at 100, days the fire has taken, days reclaimed, and workings cast in battle and on the map. Ashen players see a closed ledger. Persists with the save.

### Whisper system — tiers
- The whisper counter now expresses itself before 100:
  - **25+ (noticed)** — occasional ambient whisper flavour on the daily tick.
  - **50+ (favoured)** — Ashen Altar rituals gain +1 point per round; Sanctuary meditation loses 1 point per round (never below 1).
  - **75+ (close)** — the bonus/drag deepens to 2.
- Crossing a tier shows a one-time warning; the Ledger of Years carries a vague status line. The exact count stays hidden.
- Dark settlement events now feed or starve the cold: burning the village in *Darkness in the Roots* (+4); *The Pyre* — letting her burn (+2), watching for sport (+3), saving her (−3); *The Priest at the Gate* — funding the sanctuary (−5), beating him (+3); *The Circle Closes* — scattering them with magic (+1); *Ash in the Dream* — dismissing it (−2), reaching back (+5); *Three Figures* — joining the dance (+8), scattering the rite (−3).

### Schemes — counter-intelligence
- New scheme-menu option: **Sweep the city for hostile agents** (500g). If an NPC scheme targets you or one of your fiefs, a Roguery check (40–85%) cancels it and names the instigator (+300 Roguery XP). With no plot in motion the coin buys only rumours.
- The vague warning whisper when a plot is queued against you now also covers fief-targeted schemes, and its chance scales with Roguery (30% base → 75%).

### Sanctuary & Ashen Altars — ritual stances
- Each ritual round now offers a choice of pace: **steady/measured** (unchanged) or **fervent/heedless** — progress builds half again as fast, but one round in three the flame/stone takes the round's cost twice.

### The Temple — covenant and anathema
- Once The Temple rises, it reacts to non-member players:
  - **Covenant** (clan tier 2+, whisper tier ≤ 1): an envoy offers a pact. While sworn, battle casts cost 1 fewer day of life (min 1), and every ~3–5 weeks the Temple calls for aid — ride with the strike (bloodies up to 2 Ashen warbands, +50 renown, +10 relation), send 800g, or stand aside (−5 relation). Declining the envoy closes the offer for good.
  - **Anathema** (whisper tier 3): any covenant is revoked, relations with the High Templar collapse, and zealot ambushes periodically wound the player's column until the whispers fade below tier 2.

---

## v0.17

### Rival Shadow system
- One Ashen lord is designated as the player's personal antagonist at campaign start.
- Every 14–21 days the Shadow schemes against a player-owned settlement: loyalty −10 or security −15.
- After five schemes **The Shadow Approaches** event fires: Leadership or Athletics duel or withdraw (−30 renown).
- Victory: +5 focus points, +200 renown, nearest Ashen lord converts to regular mage.
- Loss: −5 days, Shadow heals before the next engagement (ConsumedShadowHealPending flag for ColourLordAI).
- Shadow designation, scheme count, and pending events all persist through save/load.

### Mage Companion System
- Companions with the gift are now tracked as **companion mages** separately from NPC lords.
- Companion mages age 25% faster than regular lords after battle (the fire burns more personally).
- Improved join narrative: three variant messages drawn at random on companion recruitment.
- `RegisterCompanionMage` wires companion mages into both `_mageIds` and `_companionMageIds`.

### Persistent Spell Aftermath
- **Missile + Damage** leaves a `spell_firepatch` area effect (3 m radius, 8 s) at the explosion point, damaging enemies who walk through it.
- **Burst + Restore** (player only) leaves a `spell_holyzone` area effect at the burst centre, healing allies within the burst radius for 5 seconds.
- Both effects respect team affiliation — no friendly fire from fire patches, no healing enemies from holy zones.

### Whisper System
- Hidden counter tracking how deeply the cold has entered the player's fire.
- Hooks: Ashen lord killed by player (+3), any lord executed by player (+5), dark rite completed (+5), failed sanctuary prayer (+2), battle lost (+1).
- Passive decay: honourable and merciful players (Mercy + Honor ≥ 2) have a 1-in-7 daily chance to shed 1 whisper.
- At 100+ whispers a 7-day countdown fires **The Cold Calls Your Name**: Resist (−10 days, −30 whispers), Bargain (−30 days, −60 whispers), or Accept (become Ashen).
- Whisper count and countdown persist through save/load.

### Grimoire of Lost Forms
- Four new talent-tier entries at a fixed cost of 3 focus points each (separate Lost Form category in talent menu, ◈ icon):
  - **Widened Blast** — blast cone expands from ~49° to ~60°.
  - **Twin Bolt** — missile fires two bolts side by side at 60% power each.
  - **Fading Ward** — barrier nodes expire after 60 seconds rather than persisting indefinitely.
  - **Directed Burst** — burst is asymmetric: full power forward, 40% power in the rear arc.
- `TalentDef.FocusCost` field added; `TryPurchase` uses it when non-zero.
- Lost Form flags (`UsingLostBlast` etc.) set by `SpellBuilder.Parse` when the talent is owned.

---

## v0.16

### Spell Minigame — overhaul
- Ritual text reworked from single words to full two-sentence descriptions per step.
- Each step now has **10 variant phrasings** (was 3); recall screen always shows 3 options (correct + 2 random draws from the pool).
- New multipliers: 0/3 = **0.50×**, 1/3 = **0.80×**, 2/3 = **1.20×**, 3/3 = **1.50×**.
- Fixed dialog re-entrance bug where clicking *Continue* refreshed the current screen instead of advancing.
- Fixed per-encounter multi-screen dialog re-entrance bug in Settlement Encounters.

### Sanctuary — iterative ritual system
- Replaced flat-fee troop/aging costs with a per-round hero HP self-sacrifice mechanic.
- Each rite now has a hidden target; the player meditates round by round and chooses when to stop.
- Per-rite choices on success: Prayer of Healing offers *Heal the Wounded* or *Steady the Line*; Prayer for a Blessing offers *Shed a Year* or *Flame Mark*.
- Added per-rite cooldowns, location depletion (5 uses → 30-day rest), and cross-system interference with Ashen Altars.
- Expanded to **4 permanent Sanctuaries** in Empire towns.
- Fixed sanctuary announcement firing on every game load; fixed announcement showing wrong settlement names.

### Ashen Altars — iterative ritual system
- Mirrored refactor: per-round sacrifice cost, hidden target, player chooses when to stop.
- Altars now in all four starting cities: Tyal, Sibir, Baltakhand, Amprela.
- Added Carrion Gift and Break Wills target-selection screen on success.
- Added per-rite cooldowns, location depletion, and NPC daily dark-rite effects.
- Fixed altar announcement firing on every game load.

### Settlement Encounters
- New encounter: **An Insult at the Gate** — provocation that can escalate to field combat.
- **LV_ColdEmbrace**: replaced dice-roll outcome with a real field battle.

### Troops & spells
- Added **Wandering Circle** troop tree: Acolyte → Druid → Ember Sorcerer (renamed from Ember Shaman).
- Added **Ashstorm** as a campaign-map siege spell; rebalanced to standard map spell cost.
- Added companion magic abilities.

### Balance
- Rebalanced talents: Sunder, Cinder Shell, Reflect, Smoulder, Kinship, Extinguish, Immolate, Resonance, Ember.

### Schemes
- Added Scheme Whispers feature.

---

## v0.14.1

### New mechanic: Arcane sequence minigame for campaign map spell casting

Casting a campaign spell now opens a short memory game before the spell fires.

**Phase 1 — The Rite.** A 3-step ritual description appears, two sentences per step. Each spell has its own ritual text; each step has three variant phrasings, and one is drawn at random each cast.

**Phase 2 — Recall.** The description disappears. The player is asked to identify each step's exact phrasing from its three variants — one dialog per step.

**Score → power multiplier:**

| Correct | Multiplier | Message |
|---------|-----------|---------|
| 3 / 3 | 1.50× | Resonance — the rite was perfect. |
| 2 / 3 | 1.00× | The working takes hold. |
| 1 / 3 | 0.75× | The words blur — the fire catches unevenly. |
| 0 / 3 | 0.50× | The words scatter — the fire finds its own shape. |

The aging cost is always paid. A "Cast without the rite" button on the ritual screen skips the minigame and fires at 1.00×.

All six spell `Cast*` methods accept a power multiplier and scale their numerical outputs accordingly: morale deltas, influence, gold, hearth reduction percentage, troop count, and Fade concealment duration (perfect recall grants one extra day).

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
