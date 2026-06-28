# Ash and Ember — Changelog

---

## Unreleased

---

## v0.24.0

### The Living Ember, reforged — you choose the element, and the land can die
- **You now choose which element to draw.** While focused (hold **Left Ctrl**), trace a direction to draw that element — **W = Wind · S = Earth · A = Water · D = Storm** (left stick on a pad), then stand still to gather the charge. The land no longer decides the element for you. On the campaign map, choose the element in the litany (**Shift+X**) before standing still to gather.
- **Living energy — every battlefield and stretch of country now holds a finite, hidden reserve of living warmth**, sized by how much grows there (a forest brims; a desert holds almost nothing). The reserve persists in your save, is shared by everyone who fights over that ground, and slowly heals when left in peace.
  - **Every nature draw _and_ every Inner Fire cast spends it** — for the player **and every NPC mage** alike.
  - You are never shown the number, but the land warns you as it thins: at the **half**, the **quarter**, and when it runs **dry**.
  - **Drawn past empty, the land turns on those who draw from it:** each further *nature* draw **bleeds the hearth of the nearest village**, and has a ~35% chance to **sour** — recoiling on the nature caster (≈22 damage; the working twists). NPC nature seers suffer the same.
  - **Inner Fire is immune to the backlash.** Fire burns the land rather than communing with it: a fire cast still strips the local reserve (leaving the ground exhausted and dangerous), but the land can never recoil on the fire mage. The point is to make the Living Ember harder and riskier to use on a battlefield full of fire-mages — not to punish fire.
  - **The backlash now takes many forms.** Forcing an exhausted land no longer means one flat recoil — in battle it may bite back as raw recoil, dead briars that root you, a hollowing that saps your speed, a gout of grey ash, or a slow wither; on the march as a blood-tithe, blighted (spoiled) provisions, a contagious despair (morale), or a creeping marsh-fever that wounds the weakest. Player and NPC nature casters draw from the same palette.
- **The Old Green — a tavern option for the land-attuned.** Any tavern now offers Nature-attuned heroes a pouch of rare weeds (150 denars). Smoking it costs you **−10% of your health** and a few drowsy hours, but for **24 hours** every nature draw has a **30% chance to cost the land nothing at all** — the living world counts you, briefly, as one of its own. Saved with the campaign.
- **Terrain now governs _cost_, not choice.** Each land favours certain elements (Wind on mountains/steppes, Earth in forest, Water by water, Storm on open plain/desert). Drawing a favoured element spends little of the reserve; drawing against the land (water in a desert, say) spends far more.
- **Talents brought in line with the new system:**
  - **Still Draw** — the channel bar now fills **twice as fast** (was an unimplemented HP-cost reduction).
  - **Deep Earth** — now **halves the living energy** each of your draws spends, sparing the land (was an unimplemented siege cooldown).
  - **Living Root**, **Open Grip**, **Dawn Call**, and **Wildsworn** descriptions corrected to match what they actually do.
- Journal ("Notes for the Adventurer") and README rewritten to teach the new draw, the four direction keys, and the living-energy economy.

### Crystals — chambers and stock now actually reach everyone
- **Crystalline Chambers now appear for any visitor.** The "Visit the Crystalline Chamber" town option was gated behind being an Inner Fire mage, so Nature/Grace/non-magic players never saw it — even though crystals need no magical path to form or use. The gate is removed; the chamber shows in all eight towns (Sargot, Marunath, Ortysia, Revyl, Husn Fulq, Dunglanys, Tyal, Epicrotea) regardless of path.
- **NPC lords now actually carry crystals.** `EstablishForNewCampaign` was an empty stub, and the only seeding hook (`OnHeroCreated`) fires solely for heroes spawned *after* a campaign begins — so no lord alive at game start ever carried one. New campaigns now seed ~5 % of existing lords with a crystal in a free weapon slot, mirroring the per-creation chance. (Town markets already restock every crystal weekly, so the player side was working.)

### Fixes
- **Sanctuaries and Ashen Altars no longer leak between campaigns started in one session.** `SanctuaryCampaignBehavior` and `AshenAltarsCampaignBehavior` each had a `ResetForNewGame` that clears their static state (`_permanentSanctuaryIds` / `_dynamicAltarIds`, use cooldowns, announcement flags), but it was never called — only `EstablishForNewCampaign` (which *adds* content) ran on a new game. Starting a second campaign without restarting Bannerlord therefore inherited the first game's sanctuary/altar sites and cooldowns. Both resets now run before their establish step (same static-leak class as the earlier `AgingSystem` fix).
- **Custom Ashen units no longer spawn with the infant ("baby") body.** The Rising battle event built its reinforcement agents without explicit body properties, so they defaulted to age 0 and rendered as babies. The spawn now generates proper adult body properties and equipment from the troop template before building the agent.
- **The Ember Conclave quest can be saved again.** Its five journal quest logs were `QuestBase` subclasses that were never registered with the save definer; once the Conclave fired, the live quest could not be serialized and the save broke. All five log types are now registered (`EmberConclaveMainLog`, `…EliminateLog`, `…VisitLog`, `…RuinLog`, `…ProtectLog`).

---

## v0.23.13

### Two new Grace miracles
- **Pyre of Judgement** (hold Ctrl + D-D-W-W-S-S in battle) — a pillar of consecrated fire falls where you are looking, searing **every** enemy beneath it (not only the Ashen) and hurling the survivors from the light. Grace's first true ranged smite; battle-only, requires full virtue.
- **Hallowed Ground** (hold Ctrl + A-A-D-D-W-W in battle) — consecrates the earth around you, **warding you and nearby allies against all magic** for 10 seconds (enemy spells and Dark Gifts cannot touch the warded) and closing some of their wounds. Battle-only, requires some virtue.
- Grace lords and priests may now invoke either miracle in battle, drawing from their own divine wellspring.

### Fixes
- **The culture card's feats panel now actually shows the Templar / Tribal feats.** The v0.23.12 attempt skipped its own replacement whenever the native feat count matched ours (both cultures have exactly three), so the card kept displaying the vanilla Vlandian/Khuzait bonuses. The feats are now relabelled on the culture's own feat objects (which the panel reads directly), so the swap is reliable and survives the view-model rebuilding itself. Gameplay effect amounts are untouched — only the displayed feat text changes.

---

## v0.23.12

### Character creation — Templar & Tribal backstories
- **Reworked culture-specific backstory options** for the Templar (Vlandia) and Tribal (Khuzait) cultures, with new lore-matching names and descriptions:
  - *Tribal* — "A noyan's kinsfolk" → **Apostles of the God-King** (the Polearm grant is replaced by one random **Dark Gift**); "studied with your private tutor" → **attended the religious school**; "a chieftain's servant" → **the God-King's bloodrider's servant**; "an envoy's entourage" → **the Tribe's emissary**.
  - *Templar* — "A baron's retainers" → **Lower-rank Templars**; "Mercenaries" → **Footmen**; "hung out with the gangs" → **denounced enemies of the faith with your friends**; "a baron's groom" → **a Lord Templar's squire** (the Charm grant is replaced by **+3 Grace and +1 Honour**).
- Backstory option effects now show in Bannerlord's **dedicated effect panel** (skills, attribute, and the squire's Honour) rather than being read from the description; Grace / Dark Gift, which the panel cannot express, are noted in the description and granted as the campaign begins.

### Character creation — culture cards
- **The Khuzait culture card now reads "Tribes of the East"** on the character-creation screen (matching the existing Templar rename), with its own lore description.
- **Cultural feats moved into Bannerlord's dedicated feats panel** for both renamed cultures (Dawn's Grace / Oath of the Vigil / The Order's Price; War Fever / Spoils of the Raid / No Quarter), instead of being listed inside the description text.

### The Gift prompt
- **Removed the Grace ("I devoted myself to faith") and Dark Gift ("I bargained with the dark") choices** from the opening gift prompt. Both paths remain reachable in play (Grace at Sanctuaries and the new squire backstory; Dark Gifts at Dark Altars and the new apostle backstory).
- **Templars (Vlandia) can no longer choose the Living Ember (Nature) path** — it collides with the Order's Grace, which they already carry. The option is disabled for them with an in-fiction explanation.

### Fixes
- **The Ashen no longer spread to ordinary frontier towns.** Towns the Ashen seize that are not part of their designated set (e.g. Rovalt, Ocs Hall, Car Banseth, which sit a short ride from the Ashen capital) are now handed back to a defensible kingdom of their own culture, enforcing the long-standing rule that the Ashen realm is exactly its renamed set.
- Corrected a settlement assignment that referenced a non-existent Northern Empire id (`empire_n`).
- **The journal no longer references Cold miracles**, which players cannot use (Cold became an NPC-only effect when Dark Altars switched to permanent Dark Gifts).

---

## v0.23.11

### Vlandia → Templar (character-creation card)
- **The Vlandian culture card now reads "Templars" on the character-creation screen.** The card caches its name when built, so the text/language overrides never reached it; a new, strictly-bounded runtime pass now finds the live culture card and rewrites its name and description directly. Fully guarded — if it can't find the card it simply leaves the vanilla text, never crashing character creation.

---

## v0.23.10

### Nature magic — blended terrain elements
- **Transitional terrains now offer one of two fitting elements**, rolled per charge, instead of a single fixed element or a fully-random four-way roll: Steppe/Dune → Wind or Storm, Plain/RuralArea → Earth or Storm, Snow/Beach → Water or Wind, Swamp/Fording → Water or Earth, Canyon/Cliff → Earth or Wind. Iconic terrains stay pure (Forest → Earth, Mountain → Wind, open water → Water, Desert → Storm); unknown ground still rolls any of the four.
- Corrected the terrain lookup to use the real `TerrainType` names (the old list referenced names like Hill/Shore/Arctic/Meadow that the engine never emits).
- The channel hint now names the available element(s) — e.g. "Wind / Storm (random)" — instead of a vague "mixed ground".

---

## v0.23.9

### Controller hints
- **Miracle menu now shows the controller sequence too.** Each miracle's hover hint lists both the keyboard chord (hold Ctrl + W/A/S/D) and the controller chord (hold RB + flick the left stick ↑/←/→/↓), and the window body mentions the controller path. The actual input already supported the controller — only the on-screen hint was keyboard-only. (Spells, Alchemy, and Nature already read the controller; Dark Gifts are passive with no input.)

---

## v0.23.8

### UI / UX
- **Nature draw now tells you why it won't channel.** Holding Ctrl with a weapon drawn, in heavy armour, or while moving now shows a specific reason ("Sheathe your weapon…", "Too much iron…", "stop moving…") instead of failing silently.
- **Grace miracle tooltips explain why an option is greyed out** — wrong context (battle vs. map) or insufficient virtue (with the requirement), shown at the top of the hover hint.
- **Nature campaign-map spell buttons fit now** — the long sentence labels were shortened to a name + a few words, with the full effect/cost moved to the hover tooltip.
- **New-game manual is now a short pointer.** Instead of dumping the full controls codex, the start popup points to the journal entry "Notes for the Adventurer," where the gestures live permanently.

### Vlandia → Templar (attempt)
- Enriched the English `language_data.xml` to mirror Native's attributes, in another attempt to make the Vlandian culture read as "Templars" on the character-creation card. (Pending in-game confirmation.)

---

## v0.23.7

### Nature magic visuals
- **Attack spells now visibly erupt.** Gale and Entangle throw a ring of bright element-coloured light bursts; Torrent throws a forward cone of them. Previously the strikes used one-shot debris particles that flashed too briefly to read as an eruption (the damage always applied — it was purely visual). Applies to NPC casters too (shared cast path).
- **Barrier walls (Thornwall etc.) hold a stronger, lasting glow.** Each wall node now pulses a coloured light every tick and carries a wider glow column, so the wall stays clearly visible for its full 7-second duration instead of fading after the initial puff.

---

## v0.23.6

### Loading screen (black title card, safely)
- The ASH & EMBER black card now covers all loading screens — including the map↔battle transition — via an additive overlay driven by the engine's loading-window flag. This is NOT a prefab override (that approach wedged the boot), it never covers the main menu, and a hard time cap guarantees it can never block the game.

---

## v0.23.5

### Boot fix (root cause)
- **Removed `GUI/Prefabs/LoadingWindow.xml`.** This file shared the name of Native's loading-window prefab, so the engine swapped in a static black ASH & EMBER card for its loading screen. On boot that card covered the menu and never tore down, blocking the game. Deleting the override restores the vanilla loading screen.
- Re-enabled the C# title-card overlays (opening splash, lore intro, on-load card), which were never the problem — they are restored exactly as before.

---

## v0.23.3

### Boot fix
- **Disabled all custom title-card overlays** (opening splash, lore intro, loading-screen card). On some installs they failed to tear down and left a black ASH & EMBER card stuck over the main menu, blocking the game at boot. The game now boots straight to the vanilla menu.

---

## v0.23.2

### Loading screen
- **Fix:** disabled the ASH & EMBER loading-screen overlay entirely — it could persist over the main menu after the first load and block access to it. Loads now show the vanilla Bannerlord screen; the one-time opening title card on the menu is unaffected.

---

## v0.23.1

### Loading screen
- The black **ASH & EMBER** title card now also covers the map↔battle transition loading screen (the splash art), not just save loads — driven by the engine's own loading-window flag.
- **Fix:** the title card could stick on screen after a load finished and block access to the menu. It now tears down reliably (removed from whichever screen holds it) and has a hard safety cap so it can never wedge the game.

---

## v0.23

### Mage Classes (talent tree simplification)
- **The talent tree is now a class tree.** Instead of buying dozens of single talents one focus point at a time, a mage now picks **Classes** — each costs 2 focus points and grants a themed bundle of the older talents at once.
- **Seven combat/spell classes:** *Dark Mage* (life-eater: Ember, Reap, Wither, Extinguish), *Seer* (foresight: Clairvoyance, Tempered, Fade, Unsettle), *Battle-Sworn* (war-caster: Warcast, Flashfire, Pale Comet, Widened Blast), *Ward-Keeper* (shields: Ashveil, Cinder Shell, Reflect, Warden's Ring), *Heartfire* (healer: Hearthlight, Kindle, Dirge), *Pyrelord* (ruin: Immolate, Scatter, Sunder, Ashstorm), and *Ashbinder* (control: Smoulder, Kinship, Resonance).
- **Three discipline classes** replace the nine separate rites, learned at their own sites: *Coldsworn* (the Ashen Altar's three Cold rites), *Gracebound* (the Sanctuary's three Grace rites), and *Ashen Alchemist* (the Lab's three Alchemy rites).
- **Backward compatible.** Existing saves keep any single talents already purchased — they still function; they are simply no longer listed for purchase. Buying a class fires every member's side-effects (e.g. Dark Mage darkens you as Reap once did, Ashbinder warms mage relations as Kinship did).
- Consolidated-out forms (Scorch, Chain Ignite, Ashmark, Anchor Ward, Twin Bolts, Lost Burst, Lost Barrier) are **not** revived by the bundles — only live talents are granted.

---

## v0.22

### The Ember Conclave (new questline)
- **A secret society of mage lords** who believe the Ashen can be harnessed and turned into a weapon for human dominion. Their plan ends in ruin — the cold does not negotiate; it consumes.
- **Five phases** driven by a hidden power score: *Silent* (the Conclave forms, three mage lords seeded as members) → *Stirring* (first contact — the player chooses to ally, oppose, or stay silent) → *Rising* (missions offered on a 21-day cooldown) → *Ascendant* (a puppet candidate is chosen; corruption warnings begin) → *Hubris* (the tragic culmination fires) → *Ended* (the journal records the outcome).
- **Three missions** in the Rising phase (one active at a time): *The First Binding* (eliminate a named lord within 30 days), *The Sealed Accord* (visit a named settlement within 21 days), and *The Kindling Pact* (keep the puppet candidate alive for 21 days).
- **Power ebbs and flows** with your choices: members present feed it over time, completing a mission swells it, killing a member or declining a mission starves it.
- **The culmination converts the whole Conclave to Ashen** — those who would master the cold are taken by it. Enemy players are given a clear resistance path through the Conclave's rise.

### Quality of life & immersion
- **Live aging-cost preview** — the battle input buffer now shows the projected cost (e.g. `[ UU ▷ U ] (~3d)`) while you shape a spell, so the price is visible before you commit. Ashen casters see their criminal rating instead.
- **Fizzle colours** — each failure type now has its own colour: grey-tan for lost focus, warm orange for fumbles, muted yellow for hand-blocked casts, cool blue for captivity.
- **Encounter readiness hint** — a brief atmospheric line ("Something stirs as you enter the village.") fires just before a settlement encounter, flagging that something is about to happen.
- **Aging ambient comments** — NPCs near settlements occasionally remark on the mage's accelerated age (age 50+, scaling with the age bracket).
- **Rival Shadow ambient schemes** — three new schemes: a failed assassination attempt, a stolen shipment, and a dead informant. Direct settlement harm is now rarer.
- **Portents** — atmospheric warnings now precede the Ashen Gambit, Undying Host, and Broken Will events, granting 7–14 days of dread before they fire. They do not consume the weekly event slot.
- **Battlefield echoes** — casting 3+ spells in a single battle leaves a residual trace; a small Ashen spawn party may appear near the battle site the next day.
- **Whisper network intel** — at Whisper Tier 3, your cold-touched network surfaces early warnings about approaching major events.

### Fixes & tweaks
- **Miracles** — controller controls for invoking miracles made easier to use.

---

## v0.21

### Miracles (Grace & Cold)
- **Sanctuaries and Ashen Altars are now charging stations.** Each building offers just two rites. At a **Sanctuary**: *Pray for Grace* (gain Grace, scaled by Honour/Mercy/Generosity) and *Take the Warding Seal* (ward the world against Ashen events). At an **Ashen Altar**: *Embrace the Cold* (gain Cold, scaled inversely) and *Invoke the Dark Tide* (unleash Ashen influence on the world). Grace and Cold are mutually exclusive — a vessel holds one or the other, capped at 10.
- **Ten miracles.** Five of **Grace** (golden light) — *Repel the Ashen*, *Radiant Mending*, *Light of Guidance*, *Sacred Flame*, *Aegis of Faith* — and five of **Cold** (blue) — *Ashen Curse*, *Dreadmending*, *Dread Presence*, *Frost Brand*, *Shadow Shroud*. Each costs 1 point and is gated behind virtue (the strongest need all three virtues; lesser ones need one). Some work in battle, some on the map, some both.
- **Casting.** On the campaign map, open the miracle window with **Shift+X** (or both thumbsticks clicked). In battle, hold **Ctrl** and type the six-direction sequence shown for each miracle.
- **Aegis of Faith** now grants true bonus life *over the limit* — a golden ward that absorbs the next 40 damage taken, lasting until spent or the battle ends.
- **Priest troops.** *Priest of the Flame* (Grace) and *Ashen Priest* (Cold) now walk the world, garrisoning Sanctuary and Ashen towns and invoking their miracles in battle.
- **NPC miracle use is unified.** Lords and priests use miracles through one system — in battle and off-screen on the map — choosing miracles that fit their virtue. The old per-building NPC rite simulation has been removed; no more parallel systems.

### Onboarding
- **The Disciplines of Hand and Voice** — a new campaign now opens with a single, tidy controls codex covering every key combo at a glance: working spells (Alt + W/A/D/S), invoking miracles (Shift + X on the field, Ctrl + sequence in battle), and opening the alchemy satchel (Ctrl + X) — each with its controller chord. The grimoire keeps the deeper spell craft and points to the codex for the gestures.

### Fixes & tweaks
- **Grace/Cold reset on a new game** — starting a fresh campaign in the same session no longer inherits the previous campaign's Grace or Cold.
- Removed the superseded multi-rite engine and its dead state for a leaner, clearer Sanctuary/Altar codebase. Fully backward-compatible: obsolete save keys are silently ignored on load.

---

## v0.20

### Alchemy (new system)
- **Alchemical Labs** — every Aserai town plus a few Imperial towns gain a *Visit the Alchemical Lab* entry in the town menu. The network is announced when it is established (new campaign or first load on an existing save).
- **Brewing** — pick a formula and make a Medicine test for 200 denars of ingredients. The elixir is added to your satchel **whatever the result** — but a failed test taints it, and a tainted brew backfires when drunk. Brewing trains Medicine.
- **The satchel** — you carry elixirs equal to your **Intelligence**. Open it any time — in the field or mid-battle — with **Ctrl+X** (or **RB + R3** on a controller, independent of the spell controls), or from the lab menu. It lists each elixir, how many you hold, and your fill against capacity.
- **Twelve elixirs** — *Healing Draught* (restore 25% HP, anywhere), *Ember Brew* (battle berserk — heavier blows, fury that dulls pain), *Oath-Wine* (party morale), *Hearthsmoke Censer* (swells the nearest village's hearth), *Caustic Vial* (a searing burst around you in battle), *Stoneblood Tonic* (battle damage resistance), *Field Surgeon's Philtre* (mends your wounded column), *Veil of Ash* (a brief untouchable ward in battle), *Hoarfrost Draught* (a cold burst that slows and softens nearby foes in battle), *Pyreblood Philtre* (a battle second wind — heals and hardens the skin), *Marrowmend Tincture* (full self-heal and mends your wounded on the map), and *Kindling Censer* (steadies the nearest town's loyalty and security).
- **Backfires** — a tainted brew turns on the drinker: self-wound, a burst that scalds your own troops, a morale collapse, a creeping poison (damage over time), or enfeeblement (leaden limbs and a soft guard).
- **NPCs brew too** — lords and companions (the **Aserai** especially, scaled by Medicine and personality) brew and use elixirs both off-screen on the map and **in battle**, bound by the same taint rules. Enemy battle uses are posted to the combat log exactly like enemy spell casts.
- Fully backward-compatible: pre-Alchemy saves load with an empty satchel and freshly-picked labs.

### Skill progression
- **Brewing trains Medicine** — every elixir brewed at an Alchemical Lab grants Medicine XP.
- **Drinking trains Athletics** — holding your drink is slow conditioning: a small trickle of Athletics XP per round (a touch more for stronger fare).
- **Speculating trains Trade** — funding and selling stock-exchange ventures grants Trade XP scaled to the profit.
- **Rites train Leadership** — performing a Sanctuary or Ashen Altar rite grants a little Leadership XP.
- **Casting trains the body and will** — each successful spell (battle or campaign map) grants a little Athletics *or* Leadership XP, chosen at random.

### Fixes & tweaks
- **Brewing keeps its secrets** — the lab no longer tells you outright whether a brew came out clean. After bottling, a separate test against your **Intelligence** decides what you believe: read it true and you know if it is sound; read it poorly and you are left guessing until you drink it; read it badly and you walk away *certain* of the opposite. A sharper mind reads true more often and is misled less.
- **The Exchange — bigger stakes** — speculation now offers **10,000** and **50,000** denar positions alongside the smaller tiers, for those with the coin and the nerve.
- **The Circle Closes** — choosing to fight the Ashen circle now draws actual **Ashen Spawn** (thralls and invokers), not a band of looters.
- **Controllers** — the satchel now opens on **RB + R3** as well as Ctrl+X.

---

## v0.19

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
