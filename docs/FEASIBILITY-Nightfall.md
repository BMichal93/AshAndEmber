# Feasibility Study — "Nightfall" concept mod

*A survival-horror / dark-magic total conversion for Mount & Blade II: Bannerlord,
assessed against the Ash and Ember codebase as a source of reusable machinery.*

> Working title used throughout: **Nightfall**. Swap for whatever lore-name you settle on.

---

## 1. Executive summary

**Verdict: feasible, and unusually so — because roughly 70% of the hard engine
plumbing already exists in this repo.** None of your six core ideas requires a
capability Ash and Ember hasn't already shipped in some form. The concept is not
"can Bannerlord do this" (mostly answered — yes) but "how much content and
balance work sits on top of proven systems" (a lot, but that is the fun part).

The one genuinely *hard* pillar is **NPC night-avoidance behaviour** — Bannerlord's
campaign-AI travel planner is baked deep and cannot be cleanly rewritten. It can
be *approximated* convincingly (herd parties into settlements at dusk, penalise
night speed), but a true "NPCs reason about night" planner is out of reach without
Harmony-patching core AI, which is fragile across game patches.

**Recommendation: build Nightfall as a *new* mod that harvests Ash and Ember's
modular subsystems**, rather than reskinning this mod in place. The folder-per-system,
pure-`*Math.cs`, static-reset architecture here is genuinely portable, and the
thematic gulf (warring epic → survival horror) is wide enough that a clean project
avoids dragging along a decade of fire-mage lore you'd only have to strip out.

| # | Core idea | Feasibility | Reuse in this repo | Net effort |
|---|-----------|-------------|--------------------|-----------|
| 1 | City-states instead of kingdoms | **High** | `AshenCitySystem`, `AshenDiplomacyModel`, `TribalKingdomBehavior`, culture-text overrides | Medium |
| 2 | Abandoned cities as dungeons | **High** (menu) / Low (walkable scenes) | `AshenRuinSystem` + `AshenRuinDefs` | Medium |
| 3 | Night demonic spawns, magic-only kills | **High** | `ElementalWildsBehavior`, `ElementalBeings` weakness system, `CampaignMapEvents.SpawnAshenAmbushNear` | Medium |
| 4 | NPCs avoid travelling at night | **Partial / approximate** | `ForestClansSpeedModel` (speed), party `SetMove*` herding | High + fragile |
| 5 | Rare resources / weapons / recruits | **High** | `AshenRecruitCatalog`/`Math`, recruitment & economy model overrides | Medium (content) |
| 6 | Rune-formula magic from dungeons | **Very High** | `SpellBuilder` (already a rune-buffer parser!), `MagicLearning` Codex, `GrimoireFragment` reward | Low–Medium |
| — | Thematic shift (tone/atmosphere) | **High** | `AshenMapTone`, `AmbientRemarks`, splash/loading overrides | Medium (content) |

---

## 2. Idea-by-idea assessment

### 2.1 City-states instead of kingdoms — **High**

**What already exists.** `AshenCitySystem.Setup.cs` is a full worked example of
*programmatically restructuring the political map*: it creates a kingdom at runtime
(`Kingdom.CreateKingdom` + `InitializeKingdom`), moves clans between kingdoms
(`ChangeKingdomAction.*`), reassigns settlement ownership
(`ChangeOwnerOfSettlementAction`), forces garrisons, and self-heals mis-assignments
on daily tick. `AshenDiplomacyModel` already overrides diplomacy globally (permanent
war). Culture-text overrides (Vlandia→Holy Temple, etc.) are re-applied on
`OnGameInitializationFinished`. `TribalKingdomBehavior` shows a second bespoke faction.

**How it maps.** "City-state with 1–3 clans" = at session launch, dissolve the five
vanilla kingdoms and re-found one micro-kingdom per major town, seeding it with the
town's owning clan plus a couple of neighbours. Every API call needed is demonstrated
in `AshenCitySystem`. A custom `DiplomacyModel` (pattern proven) sets the city-state
posture — wary neutrality, occasional raids — instead of vanilla's grand-campaign
war logic.

**The real constraint (engine, not code).** Bannerlord's settlement *set* is baked
into the campaign map: you can rename, recolour, re-own, and empty towns, but you
cannot easily add/remove settlements or reshape borders without custom map scenes.
So "city-states" is a **reflavour of the existing ~120 settlements**, not a redrawn
map. That's fine — Ash and Ember never adds a settlement either. Kingdom count and
membership are fully yours to script.

**Risk:** the campaign AI will try to wage vanilla-style expansionist war between
your city-states unless a diplomacy/AI model suppresses it. `AshenDiplomacyModel`
is the template; expect iteration to stop city-states from snowballing back into
empires (the classic "one faction eats the map" problem). *Medium effort.*

### 2.2 Abandoned cities as dungeons — **High (as menu crawls)**

**What already exists.** `AshenRuinSystem.cs` is, almost exactly, your dungeon
system already built. It is a **menu-driven, room-by-room "choose-your-adventure"
crawl** over settlements: `AshenRuinDefs` defines each ruin's tier, entry lore, an
ordered `Challenges[]` array and rewards; `RunRoom`/`DispatchChallenge` walks the
rooms; ~25 challenge types (`BloodLock`, `RiddleGate`, `SpectralGuardian`,
`CollapsingChamber`, `MirrorGate`…) each resolve via `InquiryData` popups with
skill/resource checks, branching outcomes, retreat-with-partial-reward, guard
parties spawned nearby (`SpawnAshenAmbushNear`), per-ruin cooldowns, and even **rival
NPC lords racing you to the loot**. This is a shippable dungeon framework today.

**How it maps.** Point the same framework at your "abandoned cities": a `DungeonDef`
per ruined settlement, new challenge types themed to the horror (traversal, sanity,
demonic wards), and dungeon-only rewards — crucially **rune-formula fragments**
(see 2.6), for which the `RewardType.GrimoireFragment` path already exists.

**The ceiling.** This gives you *narrative* dungeons (menus + the occasional spawned
ambush battle), not *walkable* ones. A true explorable dungeon interior is a whole
separate, much heavier workstream: authoring custom scenes in the Bannerlord editor
and driving `MissionState`/scene transitions. Ash and Ember never does this — every
"interior" is abstracted to menus. **Recommendation:** ship menu-dungeons first
(low risk, proven), treat 1–2 hand-built walkable "signature" dungeons as a stretch
goal. *Medium effort for menu tier; High + art-heavy for walkable.*

### 2.3 Night demonic nightmares, near-immune to steel — **High**

Three sub-problems, each already solved somewhere in this repo:

1. **Time-of-day gating.** Available and already used: `CampaignTime.Now.CurrentHourInDay`
   (see `CrystalBattleAI.cs:55`) and `CrystalMath.IsDaylight(hourOfDay)`
   (`CrystalMath.cs:32`). An hourly/daily behaviour can spawn at dusk and despawn at dawn.

2. **Spawning roaming hostile bands.** `ElementalWildsBehavior.SpawnBand` is a
   complete, crash-safe spawner: `BanditPartyComponent.CreateBanditParty` with a
   mandatory home hideout (a null one crashes the loot screen — a gotcha already
   learned here), custom troop rosters, custom names, and `SetMoveGoToSettlement`
   to send the band roaming the roads. `CampaignMapEvents.SpawnAshenAmbushNear`
   spawns near a point. Reuse directly: spawn N demon bands per night ringing the
   towns, set to intercept the nearest party.

3. **"Almost unkillable with normal weapons."** This is the elegant part — the
   **Kindled** already implement exactly this. `ElementalBeings` gives each spawned
   being an element/physical **weakness wheel** (`ElementalMath`): they shrug off
   ordinary weapon damage and take real damage only from the matching magic. Retune
   that so demons are *broadly* steel-resistant and magic-vulnerable, and your
   "bring runes, not swords, to the night" fantasy falls out of an existing system.
   `OnAgentHit`/`OnAgentBuild` hooks in `MagicMissionBehavior` are where damage is
   re-weighted.

**Risk:** performance and pacing — too many strong parties each night will grind
the campaign to a halt and wreck weak NPC parties (feeds back into 2.4). Cap living
bands (as `ElementalMath.WildMaxLivingBands` already does) and tune aggression.
*Medium effort.*

### 2.4 NPCs avoid night travel — **Partial / the hard pillar**

This is the only idea that fights the engine rather than riding it.

**Why it's hard.** Bannerlord's party movement and the campaign-AI decision layer
(`AiBehavior`, party "thinking") are deeply baked and *not* meant to be paused for a
day/night cycle. There is no supported "don't travel at night" hook. You cannot
cleanly rewrite the global planner; you can only nudge it.

**What's achievable (approximation), using patterns already here:**

- **Dusk herding.** An evening tick that, for parties caught in the open, issues
  `SetMoveGoToSettlement(nearestSafeTown)` — park them in town overnight, release at
  dawn. Ash and Ember already drives parties with `SetMove*` (Sea lanes, Soldier
  service `ReassertArmy`). Fragile point: you are overriding the AI's own goal each
  evening and must yield it back cleanly at dawn, or you deadlock a lord in a town.
- **Night speed penalty.** Subclass the speed model (proven: `ForestClansSpeedModel`)
  so open-field night movement is punishingly slow — a soft economic disincentive.
- **Fear cost.** Let the night demons (2.3) actually maul parties that ignore the
  curfew, so the *emergent* incentive teaches NPCs-as-simulated to stay in — the
  penalty does the storytelling even where the AI can't.

**What's not achievable cheaply:** a genuine planner where lords *reason* "it's
getting dark, I'll delay this campaign." That needs Harmony patches into core AI and
breaks on patch updates. **Recommendation:** ship the herding + speed + fear stack;
market it as "the wise stay behind walls after dark," not "perfect AI." *High effort,
highest risk, plan for iteration.*

### 2.5 Rarer resources / weapons / recruits — **High (mostly content)**

**What already exists.** `AshenRecruitCatalog` / `AshenRecruitMath` /
`AshenRecruitCampaignBehavior` already implement a *bespoke, gated recruitment pool*
distinct from vanilla volunteers — the exact shape of "recruits are rare and
special." Scarcity of gear/loot is a matter of overriding the relevant models
(item spawn, loot, recruitment slot counts) — Ash and Ember already subclasses
several campaign models cleanly.

**How it maps.** Slash vanilla recruitment slots and reroute recruitment through a
scarce catalog; thin loot tables; make good weapons dungeon/vendor-gated. This is
**balance and content work on proven scaffolding**, not new tech. *Medium effort,
front-loaded on tuning.*

### 2.6 Rune-formula magic found in dungeons — **Very High (best fit of all)**

**This is the single most reusable idea.** `SpellBuilder.cs` is *already a
rune-formula parser.* It reads a sequence of directional "runes" (`U/L/R/D`) split
across a form buffer and an effect buffer, and assembles them into a `SpellCast`
(forms × effects × counts), with combos gated behind learned talents. That is
precisely "assemble runes into a formula." The `MagicLearning` Codex handles
unlock/discovery gating, and `MageKnowledge` persists what you've learned.

**How it maps.** Define named formulas as rune strings; make each formula an item of
lore **discovered in dungeons** — and the reward plumbing already exists
(`RewardType.GrimoireFragment` in `AshenRuinSystem.GrantReward`). Lengthen the
buffers, give runes horror-flavoured names, and gate the strong ones behind deep
dungeon clears. The Miracles system (catalog of discrete "prayers" with a resource
economy) is an alternative/complementary model if you prefer selecting a known
formula over typing runes live.

**Design choice to make early:** *live rune-entry in combat* (à la the current
`MagicInputHandler` buffer) vs. *pre-learned formula slots* (à la `MiracleCatalog`).
The first is more diegetic to "casting a formula"; the second is far friendlier on
controllers and less fumble-prone. Both are already prototyped in this repo, so you
can A/B them. *Low–Medium effort.*

### 2.7 Thematic shift — **High (atmosphere is content)**

`AshenMapTone`, `AmbientRemarks`, the splash/loading-screen overrides in
`MainSubModule`, and the culture-text override pass are all levers for tone. The
codebase already swings a "warring kingdoms" base game toward a specific mood
(the cold, the Ashen). Re-pointing that toward survival-horror is content-and-copy
work on existing hooks, not new systems.

---

## 3. Engine-level hard limits (know these going in)

| Limit | Consequence | Mitigation |
|---|---|---|
| Settlement set is baked into the campaign map | Can't add/remove towns or redraw borders | Reflavour existing settlements (Ash and Ember never adds one) |
| Campaign-AI travel planner isn't hookable | No clean "avoid night" behaviour | Herd + speed-penalty + fear (2.4) |
| Walkable dungeon interiors need custom scenes | Menu dungeons only, unless art invested | Ship menu tier; hand-build 1–2 signature scenes as stretch |
| Vanilla diplomacy re-expands factions | City-states snowball back into empires | Custom `DiplomacyModel` (proven pattern) + tuning |
| Save compatibility & static state | Mid-process reloads carry stale state; enum values can't be freely deleted | Follow this repo's reset-on-`OnGameStart` + "keep retired enum values for save-compat" discipline |
| Harmony patches are patch-fragile | Deep AI overrides break on game updates | Prefer supported model/behaviour subclassing over Harmony where possible |

---

## 4. Recommended architecture

Mirror Ash and Ember's proven structure — it's the reason this is feasible:

- **One system per folder** under `src/`, large behaviours split into **partial
  classes by concern**.
- **All numeric logic in pure `*Math.cs`** (no TaleWorlds types) so it's unit-testable
  under `PureLogicTests`. Every balance number for night spawns, dungeon odds,
  scarcity, and rune costs should live here.
- **Reset all static in-mission state on `OnGameStart`**; persist hero/campaign state
  through the `CampaignObject`/`SyncData` pattern; guard every singleton access with
  null-checks + logged try/catch (`ModLog`).
- **Register a `DiplomacyModel`** for city-state posture and a **speed model** for the
  night penalty at `OnGameStart`, exactly as this mod registers `AshenDiplomacyModel`.

**Harvest list (lift-and-adapt from this repo):**
`AshenRuinSystem`/`AshenRuinDefs` → dungeon framework · `ElementalWildsBehavior` +
`ElementalBeings`/`ElementalMath` → night demons + steel-immunity · `SpellBuilder` +
`MagicLearning` → rune formulas · `AshenCitySystem` + `AshenDiplomacyModel` →
city-states · `AshenRecruitCatalog` → scarce recruitment · `ForestClansSpeedModel` →
night speed · `AshenMapTone`/`AmbientRemarks` → tone.

---

## 5. Suggested phasing (de-risks the hard pillar early)

1. **Vertical slice — the night.** Time-gated demon spawns (2.3) with steel-immunity
   + the dusk-herding/speed prototype (2.4). This is the concept's soul *and* its
   biggest risk; prove it fun before anything else.
2. **Rune magic + first menu-dungeon (2.6, 2.2).** Give the player the tool to fight
   the night and a reason to explore — reward loop closed.
3. **City-states + scarcity (2.1, 2.5).** Reshape the political and economic world
   around the survival loop.
4. **Content pass — tone, more dungeons, more formulas, more demon kinds (2.7).**
   Breadth on proven systems.

---

## 6. Bottom line

The idea is **well-matched to what this codebase already proves Bannerlord can do.**
Five of six pillars ride systems that exist here in shippable form; the sixth
(NPC night-avoidance) is achievable as a convincing approximation but not as a
"true" AI planner, and should be scoped and iterated accordingly. Build it as a new
mod that harvests these subsystems, front-load the night vertical slice to de-risk,
and keep the pure-math + static-reset + logged-guard discipline that makes this
project stable.
