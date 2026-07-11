# Ash and Ember — Changelog

---

## Unreleased

### Questlines can no longer break saving
- **Guarded the bug that used to make a started quest un-saveable.** A quest journal that reaches the game's quest manager without a registered save definition cannot be written to disk — and the failure never shows at load or during play, only the moment you press Save, long after the quest triggered. That is what repeatedly broke saving on the larger questlines. All thirteen quest journals are registered, and two guards now keep it that way: the mod audits its own quest types at startup and writes any unregistered one to `errors.log`, and the test suite fails outright if a quest is ever added without a save definition. A questline can no longer ship with a broken save.
- **Fixed the Great Awakening's "Prisoners Sacrificed" counter freezing after a reload.** The quest's progress bar was bound to a handle that isn't carried in the save, so after loading a game the count stopped advancing even as the altar kept drinking. It is now re-bound from the restored journal, as the other questlines already did.

### The rise of the Ashen realm — three start-of-game fixes
- **Fixed the Ashen sometimes never rising at all.** The realm's cities were found by *display name* ("Tyal", "Sibir", …) — but the mod renames those very settlements to their Ashen names ("The Heart of Winter", "The Pale Throne") earlier in the session. Whenever the rename won the race, the search for "Tyal" found nothing, no clan was ever claimed, and the north stayed ordinary Sturgia for the whole campaign. Every settlement in the realm is now matched by its immutable id, so a rename can never hide a city from the cold.
- **Fixed Makeb and Chaikand starting Ashen.** Monchug's clan holds Tepes Castle (Ashen ground) alongside his two Tribes capitals, and the guard meant to spare him judged his other holdings by name, from a list the engine has not always filled in yet — leaving him looking like a pure lord of the cold, his whole clan and both capitals dragged in with him. The "does this clan hold anything outside the realm" test and the "is this clan seated in an Ashen city" test now both read stable settlement ids. Tepes still falls; Monchug keeps his cities. (The same guard already shielded Raganvad; it now holds for both.)
- **A new campaign no longer inherits the last one's Ashen rolls.** Starting a fresh game without restarting Bannerlord left the previous campaign's Ashen clan list in memory, which renamed the new world's settlements before the cold had claimed anything in it. The rolls are now cleared when the campaign is created.

### Ashen Spawn are grown men and women again
- **Fixed some Ashen Spawn spawning as children.** Fourteen of the mod's troops (the Ashen Spawn tree, the Fire Worshippers, the Wandering Circle and both priests) carried face definitions whose body-property keys were 16 characters where the engine demands 128. Bannerlord rejected them silently and fell back to a default body of age zero — so the risen marched to war as child-sized figures. They now draw their faces from the game's own body-property templates: bandit faces for the fire-cultists, Khuzait for the Ashen, Battanian for the Circle, and weathered veteran faces for the wardens, revenants and priests. (The Kindled carried a valid key, which is why only *some* of a horde looked wrong.)

### Element tuning & visuals
- **Earth is now a close, almost-melee crush.** Where Earth's attack used to reach out ~11 m as a narrow ridge of roots for moderate damage, it now bursts as a short, wide fan of erupting rock (~5 m) that hits far harder (~85 vs ~32) while still pinning what it catches — reach traded for raw force, giving Earth its own melee-brawler identity apart from the ranged elements. NPC casters and the Living-Ember Entangle share the change.
- **Fixed the Spirit ward flickering out of existence.** The Spirit wall (an ally heal-and-hearten ward) only spawned a 0.7 s point light and a single smoke puff, so it read as a bug — a blink and gone. It now raises a standing column of spectral smoke and holds its light for the length of the blessing (~3.5 s).

### Ashen Spawn no longer look — or freeze the battle — like a peasant swarm
- **Fixed Ashen Spawn troops rendering as peasant women.** The ashen look swapped a hood onto every `ashen_thrall`/`ashen_invoker` by hunting for the *cheapest* native item matching fuzzy hints — which resolved to `head_wrapped` ("Female Rural Headwrap") and, for the cape, female costume cloaks. Since the big Ashen Spawn hordes are made almost entirely of thralls, the whole warband read as a swarm of grey-skinned peasant women. The ashen wardrobe now picks explicit, menacing hooded gear (a blackened cultist hood and a dark hooded cloak) and the fallback search skips any female-coded piece.
- **Fixed the massive frame-drop / freeze when Ashen Spawn cast in large battles.** The legacy NPC blast/burst visuals spawned point lights and particle entities per cast *and per struck target* with no ceiling; a 300-strong horde seeded a dozen bandit mages that all fired on the same beat, each blast lighting dozens of foes, asking the renderer for thousands of dynamic lights in a single frame. Three fixes together: a hard cap on live effect entities (magic degrades to "less shiny" rather than locking up), a cap on how many struck targets get the full impact flourish, and a ceiling of four simultaneous bandit casters with staggered first casts so they never all fire on the same tick. Damage and gameplay are unchanged.
- **Ashen units now cast cold ashfire, not real fire.** Every Ashen caster — Ashen Spawn thralls and invokers, Ashen-kingdom troops, and Ashen lords — now looses grey-blue frost with driven snow instead of orange flame, matching the cold they actually draw on. The cast message, wind-up focus aura, glow, cone, impact and detonation visuals all read cold. Ordinary bandit fire-cultists still burn red.
- **Ashen Spawn are dressed and armed again.** The whole Ashen Spawn troop tree (thrall → invoker → warrior → warden → revenant) shipped with weapon and armour item ids that don't exist in the current game build, so the risen came to battle stark naked but for a hood and completely unarmed. They now wear ragged robes and grave-cloth and carry proper improvised arms — pitchforks, hatchets, sickles, seaxes and thrown stones for the thralls, up through looted axes, maces, falchions and battered round shields for the wardens and revenants.
- **Ashen cold actually hits now.** Ashen Spawn casters were still firing through the legacy short-range blast/burst, whose footprint fell as a frost puddle at their own feet and almost never reached a foe. They now draw the cold through the *same* unified element path the player and the Ashen lords cast on — a bolt of ashen fire that flies at the nearest enemy and bursts, or a nova of cold dread when they're hemmed in — so their magic has real reach and lands.

### Take the Lord's Coin — pay and honourable partings
- **Your weekly soldiering wage is actually paid.** When a hired soldier's commander (and his realm's ruler) were both too poor to cover the week's coin, the pay quietly transferred nothing — the game refuses to let a giver's purse go negative — so you got the "X pays your week's soldiering" notice with no money behind it. A broke commander's wage is now covered from the realm's coffers so the coin always lands.
- **A commander disbanding his host is no longer your desertion.** Lords now break up their armies from time to time; being kicked out of a dispersing host used to read as you abandoning the banner and threaten the desertion penalty. The game now waits for the army teardown to settle, then tells the two apart: walk out on a living host and it's still desertion, but when the commander's host breaks up on its own you're offered a clean choice — hold to your oath until it musters again, or take your leave with no dishonour.

### The Great Awakening — a new Duneborn questline
- **Added a new capstone questline: The Great Awakening.** From day 50, a rising weekly chance (10% at day 50, +10% every further 20 days, capped at 100%) announces that Duneborn has reached beyond the Sands and touched something ancient and dark. Regardless of which side of it you're on, the quest can't move forward until you ask the Duneborn kingdom's leader about it in person — a new dialogue line reveals the plot: Duneborn means to drag the thing into Calradia on ten thousand sacrificed prisoners.
- **Two mirror-image paths, decided live by your own kingdom membership** — no separate flag to track, so leaving (or joining) Duneborn mid-quest swaps which quest is "yours" automatically. As Duneborn, feed the Dark Altar at the one Great Summoning city (the southernmost of the four existing Dark Altar towns) via a new "Prepare for a Great Summoning" option — contributing pours out your whole current prisoner roster at once. Duneborn's own lords occasionally feed it too, in the background. As anyone else, the goal is to destroy Duneborn's kingdom before the count is paid; if their kingdom is destroyed outright, the summoning is stopped cold. If the altar city itself changes hands, contribution simply stops — the running count never resets.
- **Once ten thousand prisoners are sacrificed, a 50/50 roll decides the ending.** Controlled: The Great Other — a nearly unkillable, ceaselessly-casting entity wreathed in a pitch-black aura — enters Duneborn's service, escorting their kingdom's leader, summoning fresh revenants weekly (capped at 100), and starving a rival city's stores, defenses and garrison to nothing every 30 days; Duneborn declares war on the whole of Calradia and cannot sue for peace while it lives. Uncontrolled: the same horror answers to no one and roams the map freely, preying on any city not already Ashen.

### The Scholar's Bargain
- **Added a new settlement questline: The Scholar's Bargain.** Gated on clan tier ≥ 2, a wandering scholar (Ambrose Voss) may approach when you enter or leave a town/castle your clan owns, offering research that could give you an edge over your rivals in exchange for a patron's protection — a 20% roll per qualifying visit (on top of the usual settlement-encounter odds), so he doesn't corner you the first time you qualify but is all but certain to show up eventually. The whole questline is a one-shot: once he's approached, win or lose, he never approaches again in that campaign. Accept and he settles in your castle; refuse, review his papers first (an Engineering check), or have him arrested and interrogated (Honour/Mercy cost, and he does not survive it).
- **Taking him in escalates over several real-time weeks** — a "breakthrough" that needs a test subject, a first revenant joining your ranks, and a choice to keep feeding him subjects (Honour, Mercy, and crime rating all pay for it) — ending, 50/50, in either a permanent Ashen recruiting muster in that settlement (usable without being Ashen yourself, at a crime cost per unit raised) or the settlement besieged by three Ashen Spawn warbands.
- **Turning him away at any point (initial refusal or a later "no further")** doesn't end the story — 7 to 30 days later, word arrives of what became of him and of one other lord: their castle turns Ashen, their muster gains twenty revenants, or the scholar is hanged and nothing comes of it.

### Other lords are scheming too, and the cheap plots got cheaper
- **The lower-impact schemes (Spread Rumors, Spread Terror, Burn a Storage, Poison a Well, Bribe Soldiers, Forge Documents, False Accusations) now cost noticeably less gold and influence** — these were priced close to the genuinely permanent schemes (assassination, coups) despite their effects being temporary and recoverable. Assassinate a Lord and Stage a Coup are untouched; they stay expensive on purpose.
- **NPC lords now actually use the scheme system.** Daily roll chance is up (5%/1% for schemer/non-schemer lords, from 3%/0.5%), the world-wide daily cap on new NPC schemes rose from 1 to 3, and per-lord cooldown shortened from 20–35 days to 15–25. Also fixed a gap where a lord could roll a scheme, pick a target too rich for that target's tier-scaled cost, and simply give up for the day instead of trying an affordable target — NPCs now only ever pick from targets they can actually pay for.

### The Gambit is now a choice, not a toll
- **Player schemes no longer force the full Gambit minigame** — before it begins, you can now Trust to Instinct instead: a single Roguery gamble (the same 20–80% curve as the Gambit's own Sidestep) decides the operation outright, with no field reports and no rounds to run out.
- **Playing the Gambit in full is now the better-paying path.** Extracting well past the bare success threshold (instead of the instant you cross it) earns a potency bonus of up to 1.5× — scaling the operation's damage/effect, the skill XP earned, and shortening the cooldown before you can strike the same target again. Trusting to Instinct always resolves at baseline (1.0×): quick and safe, but never the strongest outcome.
- Both paths apply the same bust consequences on failure, with a dedicated notification for each.

### Companions speak in more than one voice
- **Companion map-entry remarks now draw on skills and attributes, not just traits** — a companion with standout Medicine, Roguery, Scouting, Tactics, Engineering, Steward, Trade, Charm, Leadership, Crafting, Riding, Athletics, weapon proficiency, or a dominant Vigor/Control/Endurance/Cunning/Social/Intelligence now occasionally speaks from that specialty, each with its own cool/neutral/warm tone depending on your standing with them.
- **A companion who qualifies for more than one trait voice (say, both Valorous and Merciful) no longer always defaults to the same one** — every trait pool they currently qualify for, plus any standout skill or attribute line, is now rolled as an equal candidate each time a remark fires, so their comments read as more varied instead of one fixed personality slot.
- **Companion remarks fire noticeably less often and stopped repeating themselves** — the trigger chance drops from ~25% to ~12% and the cooldown lengthens from 3 to 6 days, and the last ~10 lines heard are now remembered so a fresh remark is re-rolled rather than saying the same thing again; the same companion also won't speak twice in a row while another is available to talk instead.

### Campfire vignettes read the road, not just the dice
- **Campfire vignettes (the ambient "the men don't like the wind" style lines that fire while camped between settlements) now fire much less often** — an 8-day cooldown and a 7% daily roll instead of 3 days and 20%, so they read as a rare aside rather than a nightly habit.
- **Added dozens more lines across every pool** (general travel, mage, aged, cold-touched) so repeats are far less noticeable.
- **Added situational pools that can pre-empt the generic lines when the world actually warrants it**: a wounded column, a kingdom at war, every season of the year (not just winter), riding without company, and leading a large host. Each only sometimes wins out over the flat pools, so the vignettes stay a mix of plain colour and lines that actually track your situation.

### Settlement encounter choices actually follow through
- **Fixed a choice in "The Knight Without Fortune" that promised news "two days later" but fired it as an immediate follow-up prompt instead of actually waiting** — it now runs through the same day-countdown machinery every other deferred encounter consequence uses.
- **"The Fever Road"'s detour option now actually costs the week of travel it claims** (a morale hit for the men, scaling with how many times the road has made them detour), instead of being a free, strictly-better alternative to the other three choices.
- **"The Merchant's Ledger"'s "decline to arbitrate" option now actually resolves** — the six-week third-party review it promises now concludes and tells you the outcome, instead of silently going nowhere.
- **"The Poisoned Well"'s "delay your departure" choice is now a real wait** — choosing it blocks the party for the night at a new settlement-encounter wait menu (mirroring the sea-voyage and inn-stay wait menus) instead of resolving the whole night in a single click.
- **"The Fever Road"'s detour and "The Camp Sickness"'s stop-and-treat options (healer, Inner Fire, living world) are now real waits too** — choosing to hole up and let the sickness pass now actually holes you up, instead of resolving days of claimed downtime in the same click. Options that explicitly mean staying in the saddle ("ride through it") or claim to work instantly ("let the cold still the fever") are left as they were.

### A push now looks like a push
- **Wind's gust, Water's torrent, wall barriers, and every other knockback now visibly shove their target instead of snapping them straight to the landing spot.** The underlying move was always a teleport; it's now played out over a fifth of a second of eased motion (the same primitive Wind's Unbinding flight already glides on), so a foe driven back by a gale or bounced off a wall of stone reads as a body crossing the distance — with no measurable cost, since a push only lives a fraction of a second even in a large melee.

### NPC and companion casters actually aim now
- **Fixed enemy lords, companion mages, the Living Ember's seers, and the Kindled loosing forward workings (Fire's bolt/wall, Wind's gust, Earth's roots, Water's wave, and every fusion) with no real aim.** Every one of these read the caster's raw, momentary facing — whatever the base game's own movement/combat AI happened to leave it at — instead of actually looking for a target, so a companion could burst fire harmlessly among its own allies or into empty ground. They now aim at the nearest actual enemy; the player's own casting (still steered by the crosshair) is untouched. A lord's pre-cast safety read (whether a lane is clear of allies) and ward probe now check the same bearing the cast will actually fly, instead of disagreeing with it.
- **Fixed bandit-tier casters (looters, raiders, and the Fire Worshipper cult troops including the Fire Zealot) being able to blast or burst their own allies.** The legacy Blast/Burst forms were built to let the *player's* imprecise aim hit friend and foe alike as a deliberate risk — but the same code path was reused for every NPC caster, so a Fire Zealot fighting in a packed melee usually caught its own side instead of the enemy. NPC casts (not the player's) now only ever damage a genuine enemy; healing still only ever reaches allies.

## v0.45.0

### Magic grows with the one who wields it
- **Spell, miracle and crystal damage now scale slightly with the caster — so a gift that felt keen at the start no longer falls behind late in a long campaign.** Each path deepens along the stat that fits its nature, and each is deliberately gentle: it starts at no bonus and caps at **+30%** at full mastery, lifting *damage and healing only* — never the tuned reach, radius, or crowd-control of any working.
  - **Spells scale with character Level** — magic is an inborn gift, so it deepens as the caster themselves grows (+1% per level). The bonus rides the shared cast path, so **enemy mage lords grow with their level too** — late fights stay tense rather than turning into a walkover.
  - **Miracles scale with Conviction** — the summed strength of your aligned virtues (Mercy, Valor, Honor, Generosity, Calculating). Grace is your own resolve made fire, so an unwavering heart calls a fiercer pyre; a fickle one, a thin flame. (+3% per point of virtue.)
  - **Crystals scale with Medicine** — the deeper your grasp of the stones' lattices, the more of their fire your hand can draw (+0.1% per point).

### The Kindled look like living element now
- **Elemental beings no longer read as bald men wearing a coloured glow.** Each Kindled is now wreathed in **continuous element bound to its own body** — real fire, freezing vapour, water-mist, wind, dust or grit rising up its trunk and streaming off both hands as it walks and swings — lit in its element's colour and edged with a bleeding contour, so a faceless thing of raw magic finally looks like one.
- **…and they cost far less to draw.** The old aura re-stamped a fistful of short-lived particles (and a fresh light) at every being, every third of a second — churning dozens of throwaway objects a second across a field of them, the mod's heaviest battlefield cost. The new shroud is **built once and carried by the engine for free**, with only a single light nudged along per body — so a whole warband of the Kindled is lighter on frames than one used to be.

### Forest Clans — reworked faction skills
- **"The Wilds Remember" is gone**, replaced by **The Green Roads** — a proper Battanian strength: Forest Clan parties know the wood's hidden paths and move **+20% faster while crossing forest ground** (applied to the player's clan parties by a new party-speed model).
- **"Debt of the Deep Wood" (a flat 5 gold/day upkeep on every bound Kindled) is gone** — the old drain was more nuisance than flavour. In its place, **Wild and Few** keeps the culture's one true penalty in step with the others: as untamed forest folk close to the land and slow to the drilled ways of great armies, your Forest Clan warbands **run leaner (party size −5%) and cost more to keep (troop wages +5%)**.

## v0.44.1

### Ashen Ruins now actually appear
- **Fixed the newly-added ruins pointing at villages that don't exist.** The six ruins introduced last release — the Salt-Blind Well, the Ember Toll House, the Hollow Minaret, the Wraith's Toll, the Weighing Chamber and the Reckoning Hall — were bound to made-up village names and so never surfaced on any map. They now sit at real settlements (Bir Seif, Uqba, Qidnar, Lochana, Marabrot and Druimmor), verified against the live game data.

## v0.44.0

### Take the lord's coin — soldier for hire
- **You can now hire your whole party out to a warring commander as a common soldier — from clan level 0**, no renown or clan tier needed (unlike a mercenary contract, which still wants tier 1). Any lord leading a party will take you on **if his realm is at war with a real rival** (not merely the Ashen), so long as that realm isn't at war with your own faction — and never the Ashen themselves.
- **Ask in conversation** ("I would take your coin and march under your banner…") and agree a term: a season (21 days), a half-year (42), or a full campaign year (84). While sworn, you **march as a true member of the commander's host** — your party falls in under his banner exactly like a lord answering a gathering-army call, moved with the column and pulled into his battles on his side, earning renown and spoils. If he keeps no host of his own, one is raised under his banner for the length of your service. You count as a mercenary of his realm, with no lasting allegiance once it ends.
- **You are paid every week** — your party's full upkeep plus a small bounty that grows with your clan's standing — from the commander's own coffers (or the realm's, if he's short).
- **Abandon the host — the army screen's "Abandon Army" — to end the service.** Before your term is up that's **desertion**: you're warned first, then branded (rising crime against the realm and lost standing with the commander), heavier the more of the term you leave unserved. **Serve the full term** and you're released cleanly with a parting bonus and their thanks. You can also settle up face-to-face with your commander in conversation.

### Elements answer in twos
- **The five element keys are now peers — chord two together and they FUSE.** X (gamepad: click R3) draws on Fire the same way W/S/A/D draw Wind/Earth/Water/Spirit; press a second, different element key within a heartbeat of the first and the two blend into a sixth working instead of the second simply overwriting it. Every fusion has its own silhouette:
  - **Fire + Wind → Lightning** — an instant bolt that chains between foes and stuns (the dormant Storm working, finally reachable).
  - **Fire + Water → Fog** — a standing, blinding cloud that never damages a soul directly, but slows whoever it swallows, dampens the bite of any shot loosed from inside it (a bowman can't find the mark true through his own cloud), and every so often scrambles the orders of a formation caught blind inside it.
  - **Fire + Earth → Magma** — a thrown glob of molten ground that burns and bogs down everyone who crosses it, and keeps doing both.
  - **Wind + Water → Ice** — a forward freeze with zero damage: every point of hurt is traded for a total, hard root.
  - **Wind + Earth → Sandstorm** — a long blinding gust, no knockback, but any mount it catches bolts off-line — it breaks a cavalry charge outright, which nothing else in the mod does.
  - **Earth + Water → Mire** — ground that keeps giving way underfoot, and the bog itself SPREADS wider the longer it stands.
  - **Spirit + any other element → a battle COMMAND to your own ranks.** Spirit is the mind: paired with another element it throws nothing at the foe — it steadies and drives the warriors within reach of you (about 18 m) for roughly twelve seconds, each pairing with its own distinct identity. **Spirit + Fire → Onslaught** (they charge as one, past all fear); **Spirit + Wind → Quicken** (a burst of movement speed and an ordered advance — close the gap or reposition, ranks kept); **Spirit + Earth → Steadfast** (their nerve is locked high — they will not break or rout, though they hold where they stand); **Spirit + Water → Hold the Line** (the ranks halt and lock in place, immovable). A command is loaded like any other fusion and answers to Attack OR Block alike (there is no separate wall to raise), so it draws a charge exactly like the rest — and can carry its own Unbinding chord too.
  - Every fusion and command costs the same flat toll as any other attack cast — a command touches no foe, so it provokes no counter-wall from enemy mages.
  - **Each of the six blended WORKINGS now carries its own Unbinding**, mirroring the base five — draw it to its fullest and press Attack+Block together. The Storm's Judgment (Lightning) strikes and stuns every foe in reach at once; the Devouring Mist (Fog) drops one vast, long-lived fog bank; the Ground Ignites (Magma) erupts underfoot and leaves a huge burning field; the Absolute Stillness (Ice) freezes every foe in reach solid without a point of damage; the Devouring Dunes (Sandstorm) bolts every mount in reach at once; the Swallowing Ground (Mire) opens one vast, spreading bog. The Spirit commands carry no Unbinding of their own — Spirit's already is the battlefield-scale seizure.
  - **Two known elements together also grant a fusion campaign-map rite**, cast through the grimoire exactly like the base five: Storm's Reckoning, Scorched Earth, The Hidden Road, Shifting Dunes, The Sinking Road, and The Long Stillness. Hostile mage lords cast these too — the existing NPC map-spell system (previously five workings) now draws from all eleven.
  - NPC mage lords fuse in battle too — a lord who has studied a second element occasionally blends it into the attack he was about to loose, into the Unbinding, or (with Spirit) into a battle command rallying his own line exactly as the player rallies theirs.
  - The Journal's "Notes for the Adventurer" explains the chord, the fusion table, the Unbindings, and the map rites.

### The Kindled read as storm, not skin
- **A Kindled's whole silhouette now carries its own element** — shin, waist, chest, and head each roil with a wisp of fire, frost, water, storm-wind, or earth alongside the existing following aura, so no bare human shape is left to read at a glance, not just the face. Woken from the first breath (the spawn burst) and sustained by every aura tick after.
- **The veil keeps up with a moving body.** Each stamp now leads the being's ground velocity by half an aura tick (instead of always landing squarely on its last position) and the chest line gets a pair of shoulder-width stamps either side, so a charging Kindled doesn't out-run its own coating or bare an arm mid-swing.

### Six new Ashen Ruins, and the trials get stranger
- **Six new ruins join the twenty-eight**: The Salt-Blind Well and The Ember Toll House (Easy), The Hollow Minaret and The Wraith's Toll (Standard), The Weighing Chamber (Brutal), and The Reckoning Hall (Legendary) — thirty-four sites total.
- **Seven new trial rooms.** Ember Wraith (a guardian drawn by the caster's own renown — the more famous you are, the harder it presses), Wardstone Gate (a straight Roguery check), Hollow Choir (reads your whisper tier — the deeper into the cold you've gone, the easier it lets you through), Ember Toll (asks for a spent focus point instead of aging or troops, falling back to a flat toll if you have none to give), Weight of Ash (a mandatory toll that scales with the ruin's own danger tier), Shifting Hall (a three-way table of renown, desertion, or whispers), and Triune Reckoning — a room that answers differently depending on whether the fire you carry is Ashen, Grace, or the Living Ember, and rolls a plain proficiency check if it's none of the three.
- **Three new rewards.** Gold Cache (a flat denar payout), Skill Tome (experience into a random worldly skill — Roguery, Charm, Leadership, Medicine, or Steward), and Ember Boon, which reacts to your own path the same way Triune Reckoning does. The Magic Crystal reward can now grant more than one crystal at a clear.
- **A misnamed ruin no longer fails silently.** If a `RuinDef`'s village name doesn't match any settlement on the current map, a warning is now logged instead of the ruin simply never appearing.


### Sacred Site tuning and a new discipline
- **The Bent Knee now seizes a random will, not the strongest, and holds it for half as long** (60s → 30s). It no longer scans for the toughest nearby enemy — just whoever the working happens to catch — so the reach never whiffs, but the borrowed will doesn't linger either.
- **Standing Stones now teach three learnable talents** ("Study the Old Ways," bought with focus points, escalating cost — the same pattern as the Litany of Devotions and the lapidary's craft): **Deeper Binding** (+20% binding success), **Sparing Rite** (a failed binding returns its Iron Ore and Charcoal), and **Kindred Ease** (halves the daily upkeep of every Kindled you've bound).

### The Ashen dead can be recruited directly
- **Any Ashen-owned town or castle now hosts a muster yard.** Recruiting an Ashen Thrall, Warrior, Invoker, Warden, or Revenant no longer requires waiting on a roaming Spawn band or a garrison transfer — the settlement's own menu ("Muster the Ashen Dead") will raise one for you. The price is prisoners, not gold: turning a captive into one of the cold-fire dead is the whole transaction, and the prisoner's own tier must meet the Ashen rank you're raising (an Ashen Revenant demands a genuinely worthy captive, not three recruits). This is a deliberate limiter — you can only raise as many high-rank Ashen as you've actually captured worthy prisoners for.

### Battania becomes the Forest Clans
- **Forest Clans lords never carry the mage gift.** The Forest Clans don't master fire themselves — they live alongside the creatures of it instead. Battanian lords are excluded from the mage-lord seeding and population regulator entirely (0% chance), whether at campaign start or afterward. The one exception is the Ashen corruption: an Ashen Forest Clans lord still casts, since the cold's grip is forced on him, not a mastery his culture chose.
- **Battania is renamed the Forest Clans.** Culture, kingdom, and troop names are rebranded the same way Vlandia became the Holy Temple and Sturgia the Northmen — a new faction blurb, ruler title (High Chieftain), and two new faction skills replace the vanilla Battanian feats: **Kinship of Root and Stone** (sacred-site Kindled bindings cost 15% less and succeed 10% more often for the clan-born), **The Wilds Remember** (wild-band Kindled deal half damage to a Forest Clans player), and **Debt of the Deep Wood** (each bound Kindled costs 5 gold a day in upkeep).
- **New: Sacred Sites.** Dunglanys and Pen Cannoc — the Forest Clans' two remaining cities — each host standing stones where, for gold, Iron Ore and Charcoal, a Smithing-gated working can bind a Kindled of your chosen kind (Stone, Frost, Sand, Flame, Tide, or Gale) as a permanent unit in your own army. Binding is exclusive with Dark Gifts and Grace — the old ways do not share a caster.
- **Forest Clans lords now sometimes march with Kindled of their own** — a chance, seeded per lord, that mirrors the sacred-site bond the player can now form.
- **The Spirit Unbinding no longer summons an elemental.** The ultimate is reworked into **The Bent Knee** (**The Hollow Oath**, Ashen face): it seizes the strongest enemy will within reach, turning them to the caster's side for a minute before the working lets go — the thrall staggers back to their own line, dazed (the Ashen working leaves a parting frost-bite besides).

### The cursed field is more alive
- **The battlefield events are reworked, and four new ones join them.** The randomised curses that can grip a battle have been overhauled for less "everyone takes damage at once" and more terrain you have to fight around.
  - **Cinder Rain** no longer smites every fighter at once. Cinders now fall in scattered, lingering patches of fire that burn *whoever stands in them* — friend or foe — so the field becomes a hazard to read and move around, not a blanket toll.
  - **Ember Tithe is retired.**
  - **Last Light works again.** The "midnight" darkness leaned on a scene setter that did nothing mid-battle; the dark now falls as a deep, dense fog, with the scattered fires and the lit Ashen the only things you can see by.
  - **Frenzy loses its flames.** The all-charge bloodlust no longer scatters fire textures across the ground — it reads as crimson chaos (red glow, red haze), not a blaze.
  - **Storm** (new): a gale sweeps the field and arrows, bolts and thrown weapons flop out of the air — ranged is all but useless while it holds. Gusts shove fighters down-wind.
  - **Tremor** (new): the ground heaves. Quakes erupt across the field, blunting and staggering everyone caught in them, and churn the broken earth to bogging mud.
  - **Deluge** (new): a drowning rain quenches every fire on the field (a cold counter to Cinder Rain and any fire-caster) and drags everyone down to a wading pace.
  - **Madness** (new): reason breaks — every few seconds one common soldier on each side turns and takes up the enemy's cause. Heroes and lords keep their wits.
  - The Rising, Dread, Ashen Ground and The Kindling are unchanged.
- **You can't retreat to shake off a curse.** The rolled event set is now locked for a full game day: retreating and re-engaging the same battle re-applies the exact same events instead of rerolling them away. A new day (or a new/loaded game) rolls fresh.

### The fire blast reads as a hit
- **Magic strikes now show they landed.** Spell damage was applied by silently draining a foe's health — no flinch, no blood, no cry, no floating damage number — so a fire blast that *did* connect read as a clean miss. The fire bolt's burst now lands as a real blow: the struck foe reacts, bleeds, and the damage number floats up. Heroes are still never killed by a spell.
- **The fire bolt bursts on a more forgiving reach** (2.4 m → 3.2 m), so a near miss still detonates on the enemy instead of sailing past.

### The Ashen Spawn are truly Ashen
- **Ashen Spawn now wear the Ashen colours, not the looters'.** The roaming Spawn bands were born as ordinary looter bandits and so carried the *Looters* culture on their banner and encyclopedia page. Once the Ashen have risen, a new Spawn band is now adopted into an Ashen clan — taking the Ashen faction and culture. (Bands that spawn before the Ashen exist stay ordinary bandits, exactly as before.)
- **Ashen Spawn no longer turn on their own kind.** Because they now share the Ashen faction, Spawn bands and Ashen lords will never fight each other. They remain every bit as hostile to the living — the Ashen are at war with all the realms of the world.

### You can talk to lords again
- **Duneborn, Northmen, Tribes, and Holy Temple lords now hold a normal conversation.** Their flavourful faction greetings had hijacked the whole dialogue: the opening led to three custom lines, two of which looped back into nothing and the third dumped you at the encounter (pre-battle) screen — which still named them "Aserai". The greeting now flows straight into the standard lord options hub, so every normal interaction (tasks, barter, marriage, war and peace, recruitment, mercenary service) is available again — only the opening line keeps its faction flavour. First-meeting introductions once again run through the vanilla flow untouched, so these lords are properly "met". (The silent, unbargaining Ashen are unchanged — their wordless "..." is intentional.)

### Fixes
- **The Duneborn realm is named the Duneborn from the first frame.** The kingdom rename (Aserai → the Duneborn, and likewise Sturgia → Northmen, Khuzait → Tribes, Vlandia → the Holy Temple) was tied to the Ashen having already risen, so early in a campaign menus and war declarations still read "Aserai". The realm renames now run every session regardless of the Ashen, so the correct names show immediately.

---

## v0.43.0

### The Ashen hold their walls
- **Ashen garrisons are larger and deadlier.** Their cities now keep a floor of **750** defenders (up from 500) and castles **450** (up from 300), and the standing mix is reweighted toward veterans — **30% Warrior / 40% Warden / 30% Revenant** (was 50/35/15). Imperial armies that used to grind an Ashen town open in a single push will now bleed for every metre of wall. NPC-owned and player-facing sieges both feel the change.

---

## v0.42.0

### Magic charges faster, hits harder, and lingers in the hand
- **A full-power draw now peaks in 5 seconds instead of 7**, so a fully-charged cone, wall, or Unbinding is far easier to land in the churn of a fight. The power ceiling for a *normal* draw is unchanged — you simply reach it sooner. Everything that scales off the draw (damage, cone reach, wall depth, the ultimate's full-charge gate) follows automatically.
- **Overchannel — the doubled working.** Keep pouring past **~10 seconds** and the draw *overchannels*: the working strikes **twice as hard**. You then have until ~15 s to loose it before the gathered power disperses. **NPC mage-lords overchannel too** — recklessly-tempered and desperate lords most, the old and cautious rarely, and never when their remaining years are too short to spend (unless survival demands it). A whisper announces both the full charge and the overchannel.
- **The charge lingers after you release Focus.** Let go of Alt/LB with a charge still drawn and it now stays in your hand for **~4 seconds** — loose it with a lone Attack or Block, or re-take Focus to keep drawing where you left off. You no longer have to release in the same instant you stop drawing.

### Spells that actually land
- **Fire now hits what you aim at.** The fire bolt flew at chest height but only burst when a foe came within a tight sphere measured against their feet — so it routinely sailed over looters and detonated in empty ground at the end of its flight. It now bursts on a generous horizontal reach as it *reaches* a foe, and the blast catches the cluster.
- **The Wind, Earth and Water cones no longer miss when you look at the ground.** Without a crosshair you naturally aim down at nearby foes; the cones were measured off your full look direction (pitch included), so aiming down tilted the whole wedge off the enemies in front of you — Entangle's narrow ridge whiffed with barely 20° of downward look. The wedges are now flat ground fans decided by facing alone. NPC nature-casters share the fix.
- **Embershard crystals fly level**, so a downward-aimed throw no longer plunges short of distant foes.

### Fixes
- **The Kindling is now rarer.** Raw magic waking three charged elementals mid-battle could swing a small fight hard; its per-battle chance is cut from 3% to ~1.2% so it stays a memorable surprise rather than a balance-warping regular.

### The Undivided Flame — the fifth-trait ultimate
- Two new Grace miracles answer only once all five personality traits (Mercy, Valor, Honor, Generosity, Calculating) are held at +1 or higher **at once**, rather than a single trait: **The Undivided Flame** (battle) — a nova that wards and mends your line while burning through the Ashen's cold and the Kindled's untempered wildness far harder than anything else it touches — and **The Reckoning** (map) — strikes the nearest Ashen banners and wild elemental bands within reach. The Undivided Flame is cast with a longer, deliberate 8-direction gesture instead of the usual 6; The Reckoning is chosen from the map litany like any other prayer. Both cost **2 Grace** instead of the usual 1, win or fizzle — they're strong enough to ask double the toll.

### Keep the Long Vigil
- Sanctuaries now offer a third rite: **Keep the Long Vigil** — kneel and pay a tribute of gold or blood to raise one of the five personality traits that gate the Grace miracles (Mercy, Valor, Honor, Generosity, Calculating). The tribute scales with the level being reached, the rite is capped at the engine's usual +2 trait ceiling, and it shares a 7-day cooldown so it deepens virtue rather than trivially maxing it in an afternoon.

### Five new crystals, and the ruins remember them
- **Five new crystals**: **Thornveil** (roots one foe near-solid), **Aegisstone** (heals you and hurls nearby enemies back), **Willowisp** (shatters one foe's nerve from range), **Bloodstone** (AoE damage that feeds half of it back to you as healing), and **Zephyrglass** (hastens you and nearby allies). Formed at the Crystalline Chamber and seeded onto lords exactly like the original six.
- **Ashen Ruins now sometimes yield a formed crystal** as a retreat/partial reward — seven of the twenty-eight ruins carry one.
- **Void Crystal** and the **Poisoned Air** trial — both fully built but never actually placed in any ruin — are now live (The Heart of the Void; The Bone Labyrinth, replacing a duplicated trap room there).
- Fixed a stale item description: Veilstone's in-game text described a speed buff it has never actually cast; it now matches its real "veil grasp" effect.

### Fixes
- **The Ashen no longer swallow a faction's ruler on a reload.** Raganvad's ruling clan holds Mazhadan Castle (a rightful Ashen keep) alongside his own capital towns Varcheg and Balgard. The cold-city setup, reading the clan's still-forming holdings, could sweep the whole ruling house into the Ashen — and on the next load his towns fell grey and Raganvad found himself locked in the Ashen's endless war against everyone. A faction's **ruling clan is now barred from conversion by identity** (never by the fragile timing of its holdings list), and a **self-healing pass on every load** frees any ruler an earlier build already dragged in, restoring his towns and his throne. The pass is a strict no-op on a healthy save.
- **"Notes for the Adventurer" survives a reload.** If the engine concluded the reference journal entry across a save/load, a fresh, live copy is now written in its place rather than leaving only the dead one.

---

## v0.40.0

### The Kindled — a true elemental body, and magic that bites
- **No more looters.** The Kindled are now a dedicated bare, weaponless, armourless **"Elemental"** troop — no looter clothes, no stolen kit, and **they no longer hurl stones**. Wild bands and every summon build from it, so a Kindled reads as walking fire or stone, not a ragged bandit.
- **They fight with their element.** Instead of a club, a Kindled looses a **small cone of its own element** on a cooldown — a Flame-Born throws fire, a Stone-Born heaves earth, a Frost-Born a wave of cold. A whole band is a storm of it.
- **No parley.** Wild elemental bands **cannot be talked to** on the map — like the Ashen, every conversation is answered with silence and closed. You meet them with magic or you leave.
- **Fixed a hard crash when marching on a wild band.** Engaging the Kindled on the map crashed the game the instant the encounter began: the Kindled troop (and the Fire Worshippers) carried an invalid culture id, and the silenced caught-party conversation crashed the moment it tried to read it. The culture is corrected and the Kindled now wear a single drab, weaponless garment beneath their aura, so the meeting resolves and the fight begins.

### Magic that lands
- **Fire's burn is real now.** A fully-drawn cone leaves its mark **burning harder (18/s for 5s)** with a visible pillar of flame, and the burn is **element-typed** — it keeps melting a Frost-Born (×2.2) and drowns a Flame-Born when thrown as Ashen cold, rather than ignoring the weakness wheel.
- **Bigger cones.** The base fire cone reaches **further (13m) and sweeps a broader arc**, so a cast reads as a sheet of flame, not a thin dart.

### Spirit Unbinding — one champion at a time
- The player's summoned champion is now capped to **one alive at once**: raise another only after the first falls or its time runs out. Resets each battle. No more stacking an elemental army.

### Crystals
- Every crystal now looses an **unmistakable pulse of light on use**, even swinging into empty air — a crystal is never a silent no-op.

### Balance — no gift too good, no crystal too poor
A cross-system pass so each path pulls its weight against the others (applies to NPCs and player alike):
- **Dark Strike** no longer adds a flat +20 to every blow — it now adds **+25% of the hit's damage**. A fast weapon was turning the old flat bonus into runaway DPS for a one-time cost; scaling it off the blow (like Iron Veil and Soul Mirror already do) keeps it strong without breaking the melee build.
- **Duskstone** now drains **25 morale** (was 18) — the crystal is now a genuine morale-breaker, its own role distinct from Stormcrystal's damage.
- **Rimeshard** now slows by **40%** (was 30%) — as the only pure-control crystal, it should be the *best* slow rather than a lesser one; Duskstone keeps the shallower 20% slow so the two don't overlap.
- **Aegis of the Oath** wards **25%** of incoming damage (was 30%) — a reusable Grace ward at 30% was quietly the strongest defensive option in the game.
- **Bribe Soldiers** now succeeds **38%** of the time (was 32%), matching the other settlement-denial schemes for the same price.

---

## v0.39.0

### The Kindled — elemental beings walk the world
Where raw magic pools too thick and too long, the land wakes it into a body. **The Kindled** are elemental beings — fire, water, stone, ice, sand and storm given a shape that fights — and they now appear four ways, all built through one shared core so they look and fight the same however they were called:
- **Roaming the remote wilds.** Small bands wander the far country, each shaped by its land: **Frost-Born** in the northern snows, **Sand-Born** in the deep desert, **the Risen Tide** in the old forests and wetlands, **the Gathered Storm** on the open steppe, **Stone-Born** in the mountain roots. March on one and its bodies wake into elementals for the fight.
- **Summoned against you.** An enemy mage's **Spirit Unbinding** already calls a champion of the land — it is simply a Kindled, and now shares the same aura and weakness as all the rest.
- **Waking mid-battle.** A new rare battlefield working, **The Kindling**, lets the ground itself wake a few elementals and take a side.
- **At your side.** The player's own Spirit Unbinding champion is unified into the same system.

**A living coat.** Each Kindled is wreathed in its element — fire, water, driven snow, storm or churned earth clinging to it as it moves — with a body-hugging glow to match.

**Aggressive by nature.** A being of pooled magic hunts; it never stands and waits. Enemy Kindled advance with their line and are re-roused if their charge lapses, so they close and fight rather than idling.

### Elemental weakness — the old myths were right
The Kindled **drink their own element and buckle to the one that unmakes them**:
- **The wheel:** fire is drowned by water, water is drunk by earth, earth is worn by wind, wind is burned by fire. **Ice fears fire above all.** A being shrugs off magic of its own element and takes far more from its counter.
- **Steel tells too:** **stone and ice shatter under blunt force** but turn a blade; **flame, tide and storm** let weapons pass half-harmless through them and must be unmade with magic instead.

### Balance — the Spirit Unbinding costs more
The Spirit Unbinding does not spend itself — it leaves a **living champion** fighting for a full minute, worth several men. That standing power now costs **more days of life** than a one-moment nova or gale (Nature still halves it; the Ashen still pay in criminal standing).

### Making sure you actually meet them
- Wild bands are now **large enough to persist and roam** (they carry lesser thralls alongside the true Kindled) and are **set travelling toward the nearest town** the moment they wake, so they cross the roads where you can find them rather than sitting in the deep wilds. A band's birth is announced as a rumour in the message feed.
- In a fight, only a capped number of a band's bodies (up to 8) wake into full elementals — the rest march as ordinary thralls, so a large band is dangerous, not an unbeatable wall of 400-HP beings.
- **Debug:** `Ctrl+Shift+F9` spawns a random Kindled band right beside you, ready to engage — the quick way to see them in action.

### Notes
- Fully backward compatible: all Kindled state is mission-scoped (cleared with the rest of battle state) except the roaming-band roster, which is persisted under new `ELEM_*` save keys and prunes itself. Old saves load untouched.

## v0.38.5

### Lapidary talents — the two dead ones repurposed
- With crystals now firing instantly (no charge) and under no daylight gate, two of the three Crystalline Chamber talents did nothing. They are reworked in place — **a save that owned the old talent keeps the new effect in the same slot**, at the same focus-point cost:
  - **Waking Light → Mending Light.** (The old night-casting unlock was moot — there is no daylight gate.) Now: **every crystal you loose also mends you for 25 HP** — the stored light looks after its bearer.
  - **Swift Kindling → Brilliant Lattice.** (The old half-charge cut was moot — there is no charge.) Now: **a crystal's damage and healing strike about a third harder** (Sunstone, Embershard, Veilstone, Stormcrystal).
  - **Lasting Lattice is unchanged** — a crystal still shatters far less often when you draw from it.
- Both new talents are the player's alone: an NPC crystal-bearer never reads your lapidary craft.

### Fix — clan orders are now obeyed, and loyalty is earned (merged from the clan-orders branch)
- **Parties you order to ride or to hunt now actually hold the course.** An ordered party was re-pointed only once per day, so between ticks its own AI took back over and wandered off — travel orders crawled and hunt orders barely followed. While an order is active the party's AI is now **pinned** (it stops making its own movement decisions and holds the road you set), and the hold is released the instant the order ends, is cancelled, or completes. The pin is re-asserted on session load and on every daily tick, so orders survive a save-and-reload rather than dissolving on the next campaign think.
- **A poorly-led commander can no longer command distant captains at will.** Each day, every standing order faces a loyalty check against your **Leadership**: a captain under a leader with no standing may abandon the task and return to their own counsel (~10%/day at zero Leadership, falling to nothing as your Leadership grows). Well-earned Leadership keeps your house to its orders; a weak hand does not.
- Fixed the acknowledgement line addressing the player by an unresolved name token (it now uses your first name).

## v0.38.4

### Crystals — used like a weapon, spent for good
- **No more charge — the crystal looses its light the instant you attack.** The two-second charge-up is gone; a crystal now fires the moment you strike with it in hand, like loosing a weapon (one press = one use).
- **A spent crystal is now truly destroyed.** When the lattice fractures ("the crystal is spent"), the crystal is struck from your hand for the rest of the battle AND cleared from your battle loadout so it does not return — fixing the bug where a "spent" crystal stayed wielded and kept firing.
- **Every use blasts its light, targets or not.** A crystal now always looses a visible pulse of its coloured light (and its cast report) at you when used, exactly like any other magic — it no longer looked inert when no one stood in range.

## v0.38.3

### Fix — crystals wake on the attack button itself
- **Crystals now activate the moment you attack with one in hand — no connecting blow required.** The v0.38.2 fix routed activation through a landed hit, but that only fires when the swing actually strikes an agent; testing a crystal on an empty swing (or against a target the blow doesn't register on) still did nothing. Activation now edge-detects the raw **attack input** (left mouse / right trigger) while a crystal is wielded, so a press starts the charge regardless of the weapon's form or whether the swing connects. The landed-hit and swing-time signals remain as backstops, and all three share one guard so a single attack only ever starts one charge. (Activation is suppressed only while you are focusing an element spell, where that button releases a cone.)

## v0.38.2

### Fixes — crystals fire, and Grace shows its hand
- **Crystals now reliably wake on a landed blow.** A crystal was roused only by the MissionTick swing detector (`LastMeleeAttackTime`), and that signal never fires for these weapons in this build — so striking an enemy with a crystal in hand did nothing at all, not even the "drawing light…" message. A connecting strike now begins the charge directly through the hit path (which is reliably wired), so: **swing the crystal into a foe → "…drawing light…" → about two seconds later the effect fires.** (It is a swing-then-wait, not a hold-to-charge.)
- **You can now see your Grace prayers in battle.** Holding Left Ctrl (with Grace in hand) and pressing **X** — or **RB + Y** on a controller — opens a read-only list of the prayers you can offer here and the Ctrl+W/A/S/D sequences that call them, exactly mirroring the element grimoire on Alt+X. Casting is still the gesture itself; this only lets you read the combinations without leaving the fight. (Corrected a stale note that claimed Ctrl+X was bound to alchemy — it was not; the alchemist uses RB+R3.)

## v0.38.1

### Fix — the cold cannot steal your house
- **Your own clan can no longer be dragged into the Ashen kingdom behind your back.** When any member of the player's clan turned Ashen — a child taken by the cold in the "child of the cold" encounter, or an aged companion mage who becomes Ashen instead of dying — the shared conversion path (`OnHeroSetAshen`) moved that hero's whole CLAN into the Ashen kingdom. It only ever guarded the main hero, not the rest of the player's clan, so the player would suddenly find their entire house ejected from its kingdom and sworn to the Ashen. The player's clan is now never auto-moved by a member's conversion; encounters that turn a player-clan member relocate that one hero themselves (the child event already moves the child to an Ashen clan of its own).

## v0.38.0

### The Northmen and Duneborn take their thrones — faction renames
- **The Sturgian and Aserai KINGDOMS now read Northmen and Duneborn**, not just their cultures. The culture rename already showed (your character read "Northmen"), but the faction/kingdom still read "Sturgia" everywhere the realm was named — because only Vlandia→The Holy Temple and Khuzait→Tribes of the East had kingdom renames. Sturgia now becomes the **Northmen** (ruler: Jarl) and Aserai the **Duneborn** (ruler: Sheikh), matching the existing pattern. The Ashen realm is a separate kingdom and is untouched.

### The cold keeps to its own — the Ashen no longer swallow the Northern Empire
- **Eight Northern Empire settlements are returned to the Northern Empire.** The Ashen "target" list was claiming the towns **Amprela** and **Argoron** and the castles **Epinosa, Lochana, Syratos, Atrion, Mecalovea and Rhesos** — all of them Northern Empire holdings (`clan_empire_north_*`). The world-birth setup runs two passes: the first deliberately SKIPS clans that own land outside the Ashen set (so a great Empire house is never dragged into the cold for one border castle), but the second pass force-grabbed every remaining target settlement regardless — overriding that very protection and handing the Empire's cities to the Ashen. Those eight settlements are removed from the Ashen realm entirely; they now stay with the Northern Empire as they should. The Ashen realm is now the fallen Sturgian heartland, the Khuzait marches the cold reached, and the one Vlandian port it took — and no Imperial ground.

### The Sundering — the Earth Unbinding, remade
- **Heart of the Mountain is gone; The Mountain's Wrath takes its place** *(Ashen: The Barrow Wakes)*. The old Earth ultimate was a stone mantle worn ON the caster — a self-buff whose repeated glow read as a shimmering, "blinging" suit of armour. In its place is a working that answers on the battlefield itself: the ground **erupts in a ring around the caster**, striking every foe caught, **hurling them off their feet** (mount-safe — the horse is thrown, never the rider from the saddle), leaving them staggering on broken footing, and shaking apart any wooden siege engine or gate in the ring. The torn earth is left as **rings of churned rubble that bog anyone who crosses them**, friend or foe. Instantaneous — no lingering buff, no armour glow.
- **NPC lords wield it the same way**, and still reach for it when wounded and pressed — now to heave their attackers back and buy space rather than to turtle up in stone.

### Fixes — character creation reads true
- **The Duneborn culture card now shows the Duneborn bargain.** The card's feats panel still read the vanilla caravan bonus ("Caravans are 30% cheaper…") even though the mechanic was already replaced: the culture-card view-model caches its feat text before the mod's relabel lands, and the card fixer was only rewriting the name and lore. The card now shows the true feats — **Blood Tithe** (Dark Altar sacrifices −20%), **Children of the Sand** (no desert speed penalty), **Hungry Knives** (daily troop wages +5%).
- **Hungry Knives tells the truth about its price.** Its text claimed "+10% mercenary wages"; the actual (vanilla, kept) effect is +5% daily wages for all party troops. The text now matches the effect.
- **Backstory options no longer need a hover to show their reworked names.** The character-creation option list caches its labels when the screen is built and only re-reads them on hover — so the Templar/Tribal/Duneborn narrative rewrites (and the culture-gated flavour that flips with your culture pick) showed vanilla text by default. A new bounded sweep keeps the visible labels synced with the live options for as long as the creation screen is up.
- **Culture names on the creation screen correct themselves without a hover too.** The card fixer's screen sweep was starving its node budget in the UI's widget and sprite graphs before it ever reached the culture view-models, so "Vlandia"/"Sturgia" lingered until a hover forced a refresh. Both fixers now read the live stage view-model straight off the screen (three hops, verified against the game's own assemblies) and fall back to the old sweep only if that path ever moves.

### Fixes — the Unbinding reviewed and bound tighter
- **The wind no longer tears a rider from the saddle.** On the Wings of the Gale flew a MOUNTED caster by teleporting the rider out of the saddle every tick — the horse/rider desync this mod has sworn off. The wind now refuses horse and rider: the player is told to take wing on their own feet (the chord is NOT spent — dismount and unbind), and a mounted NPC lord simply never picks the leap.
- **The Weeping Sky now truly gutters fire out.** The rain removed only a fire wall's invisible warding; the burning ground itself kept scorching the very men the rain had just quenched. The rain now sweeps the burning patches to steam along with their wards, every tick.
- **The First Flame Remembered obeys the standing water.** Every fire dies against a mist wall — except the nova, which reached through one untouched. A wall of standing water between the caster and a foe now drinks the nova's reach to that mark, like any other fire.
- **No blow can heal more than it dealt.** A mantled caster shot by a rain-soaked archer was healed back 75% (stone) plus 40% (wet string) of the same arrow — a net GAIN of health from being shot. The heal-backs now share one cap: never more than the whole blow.

## v0.37.0

### Fixes — the weave untangled (interactive-effects repair)
- **The screen-wide "glitching textures" on interactive casts are gone.** The Ashen cold's snow and every wind working were drawn with the engine's *ambient weather* emitters (`psys_env_snow_dust`, `psys_dust_env` — systems that scatter sprites over a 70–100 m box), which smeared stray snow and dust across the entire battlefield from a single spell point. Every element visual now uses a true point-scale burst, each name verified against this game build's own particle data: kicked snow for the cold, kicked dust for the gale, and real blown sand — not flung stone — for the desert dust-devil.
- **A wave now quenches EVERY burning patch in its reach.** The steam of the first quenched patch broke the quenching sweep itself, so water put out one patch and silently skipped the rest.
- **The sands never read as snow-bound.** In campaign winter every battlefield counted as snowy — deserts included — so steam rose from dry dunes and creeping fire died everywhere. Snow terrain is always snow-bound, desert and dune never are; winter still whitens the lands that truly whiten.
- **Fire on snow no longer buys its steam with frame hitches.** A charged fire wall's per-node steam and the per-second steam over burning ground now rise as single wisps instead of full three-plume clusters (one-shot blooms and quenchings keep their full clusters).
- **A working that dies against a wall always says why.** The block log throttled ALL reasons to one line per four seconds, so repeated fizzles read as bugs. Only repeats of the same reason are throttled now — a new wall's answer always speaks.

### The Unbinding — each element's ultimate working
- **A new release for a full draw: the CHORD.** Draw an element to its fullest (~7 s), then press **Attack and Block together** to UNBIND it — the element's once-per-battle ultimate. A lone press now waits a quarter-second for its partner before committing as a normal cone/wall (imperceptible in play). The toll is flat and steep — **12 days** of life expectancy (Nature halves it; the Ashen pay criminal standing, as ever). Each element can be unbound **once per battle**.
- **FIRE — The First Flame Remembered** *(Ashen: The Long Winter)*: a nova centred on the caster — heavy damage in a wide ring, every survivor set alight (the cold grips as deep frost instead), horses panic and bolt, siege timber and gates char, and a burning ring remains where the world remembers you stood. Cast under someone's rain, the nova is damped like any fire.
- **WIND — On the Wings of the Gale** *(Ashen: Carried by the Howl)*: the wind bears YOU — ~12 s of flight above the field, steered by your gaze. Any hit while aloft knocks the wind out and you **fall**, with real falling damage; fly out your welcome and it sets you down gently. NPC lords, who cannot pilot free flight, are instead **carried out of an encirclement** in a straight wind-leap.
- **EARTH — Heart of the Mountain** *(Ashen: The Cairn-Shell)*: stone flows over the caster for ~25 s — **three quarters of every blow is shrugged off**, weapon and magic alike, but the mountain walks a quarter slower.
- **WATER — The Weeping Sky** *(Ashen: The White Silence)*: rain over a wide stretch of field (~35 m, ~90 s) — every burn continuously quenched, standing fire (and its warding) gutters out, **fire magic works at half strength inside**, horses mire, men slog, and soaked bowstrings cost every ranged shot part of its bite. The rain is **impartial**, like the walls; only the Ashen blizzard picks a side, gnawing at the foe's morale. **There is only one sky** — a new casting tears the standing one down.
- **SPIRIT — The Land's Answer** *(Ashen: What Sleeps Beneath)*: the battlefield sends a champion — ONE towering elemental shaped by the ground you fight on (a Frost-Born on snowfields, a Sand-Born in the deserts, a Stone-Born elsewhere) fights at your side for a minute, then comes apart into the land it rose from.
- **Enemy lords know the Unbinding too.** Once per battle, in battles worth the working (70+ men), a mage lord reads the field and unbinds — stone when wounded, the wind out of an encirclement when desperate, the nova when swarmed, the weeping sky against a cavalry wedge or a fire-mage, the land's champion as a calculating lord's opener. Every NPC ultimate channels behind a **long, loud windup** (~4.5 s of swelling light): land any blow on the caster and the working **breaks — and stays spent**. The cost is recorded against the lord's years like every other cast.
- The journal's "Notes for the Adventurer" now records the chord and all five Unbindings.
- Nothing new is serialized — all Unbinding state is battle-scoped, so existing saves are untouched.

### The Imperial Marches
- **Qasira now answers to the Southern Empire** from the world's birth, joining Razih on the Empire's desert border (Sanala already flies the Western Empire's banner). Assigned through the same border-reassignment pass that hands the other frontier cities to their Empires, with the same nearby-castle sweep.
- **A few border lords now swear to the Empire that took their castle.** When Battania, Vlandia or Aserai lose a frontier holding to the Northern, Western or Southern Empire at world's birth, a handful of the stripped clans (the least powerful among them) now swear fealty to their castle's new liege instead of sitting landless in a kingdom that no longer holds their home — 3 Battanian clans toward the Northern Empire, 2 Vlandian toward the Western, 2 Aserai toward the Southern. Most stripped clans still stay put, so no home kingdom is gutted. The Tribes of the East are untouched by this: Akkalat still falls to the Southern Empire, but no Khuzait lord ever changes allegiance for it.

### Fixes — the production-readiness pass
- **An NPC's Embershard now belongs to its bearer.** The flying shard credited every detonation to the *player* — kills, combat-log announcements, even the blame for friendly scorching — and its flight could never strike the player directly. The missile now carries its true caster: an enemy crystal-bearer's shard can hit you, its kills are his, and the log names him.
- **Off-screen magic advantage can no longer corrupt a party roster.** The new casualty swing for mage-heavy sides could push a stack's wounded count above its troop count (e.g. 9 wounded of 10 troops × 1.3). Wounded are now clamped to the stack's size.
- **Dark Altars now rise on a NEW campaign's first day, not its first reload.** The three dynamic Ashen-city altars were rolled at session launch — before the Ashen kingdom owns any city on a fresh start — then wiped by new-game setup, so a new campaign had no dynamic altars (and no announcement) until the save was reloaded. The roll now retries each day until the Ashen hold their cities, and the herald speaks once the full set stands.
- **A wall of fire scorches only its own footprint.** The eruption's contact hit was a circle around the wall's centre wide enough to reach beside — even behind — the caster. It now strikes only inside the wall's actual rectangle.
- **The Wasting and the Camp Sickness answer the merged art.** Both encounters still gated their living-world options on the retired Living-Ember attunement (unreachable in new campaigns) — they now answer any living mage, as sea travel already does. The Wasting's dark ritual likewise accepts a Dark Gift borne from the altars, not only the retired fire-path talents.
- **Only lords speak the factions' lord-dialogue.** The Temple / Tribes / Northmen / Duneborn line pools matched any hero of the faction, so town notables and wanderers could greet you as blood-sworn riders. All four pools now require a lord.
- **The opening cinematic reads cleanly** — grammar and flow polish across the lore paragraphs ("Where will it lead you, O Firelord?").
- Housekeeping: the ended battle's mission object is no longer kept alive while waiting on the map (the Ashen cast-tally now holds it weakly).

### Blood inherits the Reaper's due
- **The BLOOD discipline is the retired Reap talent's heir everywhere.** The Branded's fire-harvest, the Wasting's dark ritual, and the reaper's yields (life given back for a raided village or discarded prisoners) all now answer to Blood — legacy saves that own Reap keep every right. The two encounters that once granted Reap (the Sealed Archive's margin note, joining the reapers at the roadside) now teach Blood itself.
- **Every harvest repays the fire's debt, not the body's years.** The Branded harvest, the reaper's yields and the legacy Ember on-kill spark now restore *life expectancy* (the v0.35 ledger) instead of literally de-aging the hero — one cost model, one currency.

### The walls learn to ward — elemental interception
- **A standing elemental wall now STOPS things, beyond its bite.** The elements answer one another the way the world does:
  - **Fire** — its updraft devours any gale that crosses it, and **horses will not face open flame**: mounts shy off every burning line (fire walls and lingering fire patches alike), whichever banner their rider carries.
  - **Wind** — turns **arrows and bolts** aside and scatters flung stone (blocks Earth magic). The seers' Stormwall wards as wind does.
  - **Earth** — a standing dam: stops **missiles** dead and breaks the water's wave (blocks Water magic).
  - **Water** — quenches **fire** to steam (a burning crystal shard fizzles without its blast) and drinks the **wind's** force.
  - **Spirit** — a ward of the unseen: it stops nothing physical, and no earthly wall stops *it* — dread passes stone and steam alike.
- **The elements are impartial.** A wall wards against every working and shot that crosses it — friend's, foe's, and the caster's own. Raising a mist wall in front of your own fire line is now a real mistake, and a real tactic.
- **Every fire obeys**: the unified cones, the bandits' crude old-path blasts, and crystal shards all die against the same walls. Wherever a working is drunk by a wall, it dies visibly — steam, scattered dust, a burst of spray — with a short log line so you know *why* it failed.
- **NPC mages read the exchange.** A lord who has seen what the enemy throws raises the wall that answers it — water against fire, wind against stone — if he has learned that element; and his own workings are subject to the same wards as yours.
- **A lord does not throw a cone into a wall that drinks it** — before loosing fire or the wave he probes the forward lane against the standing wards and picks a different element instead. Recklessness is courage, not blindness.
- A recast wall's warding falls with it — replacing a standing barrier never leaves an invisible ward behind.

### The elements react — an interactive battlefield
- **Fire takes to timber.** Siege engines and castle gates are wooden, and the fire finally treats them so: a fire cone scorches every machine in its throat (150 × draw-power against the same hit-point pool a catapult stone chews), and a standing burn — fire walls, lingering fire patches, an Embershard blast — gnaws at rams, towers, throwing machines and gates for as long as it burns. The Ashen cold splits the frozen grain just as surely. Stone wall segments do not burn.
- **Water puts out fire.** A torrent quenches burning ground along its path to hanging steam (opening a real gap in a fire wall — its warding dies with it), a standing mist wall smothers flame beneath it, and a burning man hit by the wave or caught in the mist is doused with a hiss.
- **A wave broken on stone churns the ground to MUD** — a bogging patch (~3.5 m, 10 s) that slows everyone who crosses it, and charging cavalry worst of all: the horse wades too. Mud, like the walls, is impartial.
- **Where magic dies against a wall, the world answers**: quenched fire boils into a cloud of steam, a devoured gale makes the flame flare, the broken wave leaves mud at the stone's foot.
- **Crystals join the warding.** Crystal power is shard-force — walls of wind and stone bar the Rimeshard's frost, the Stormcrystal's clap and the Veilstone's grasp from foes behind them (the Embershard missile already died on walls; water quenches it without a blast). **Duskstone's despair, the Spirit's dread and every Grace miracle pass all walls** — no earthly wall stops the unseen. Crystal-bearing NPCs count only foes a wall doesn't bar before breaking a stone.
- **The ground itself answers** (terrain-aware, via the engine's battle terrain type):
  - **Fire creeps.** A burning patch may seed a child flame a stride away — eagerly through grass and brush (plains, steppe, forest), barely at all on sand, snow-bound or sodden ground. Two generations and a hard cap, so a fire line smoulders outward without consuming the field.
  - **Fire melts snow.** On snow-bound ground (snow terrain, or winter beyond the deserts) living flame stands in rising steam — the drifts slump around every burning patch, cone-strike and fire wall. **The Ashen cold does the opposite**: it does not melt the snow, it *deepens* it — thicker drifts where the cold fire lands.
  - **Wind raises the sand.** On desert and dune a gale whips up a ring of stinging dust, and a wall of wind stands inside its own dust-devil.
- **Not done, honestly:** individual trees cannot burn — Bannerlord's scene flora isn't entity-backed, so there is nothing there for the fire to damage. The creeping ground-fire above is the nearest true mechanic.

### Dead rewards live again — the retired talents no longer masquerade as prizes
- **Two encounters were paying in ash.** The temple-square rescue and the coin-flip merchant granted the retired spell/enchantment talents (Pyrelord's brands, the old fire map-spells) — non-functional since the magic merge. Both now pay in the LIVING craft: a Codex power (element or discipline) you do not yet hold — the merchant up to four of them, fitting the stake of losing your magehood on the flip. All powers held → an attribute point instead.
- Confirmed by sweep: nothing else invokes the retired paths — the archetype tables persist only for save compatibility, the old map-spell engine and two-phase input have no callers, and the legacy enchantments fire solely on the bandits' crude old-path casts, as intended.

### Hit-safety sweep — mounts, riders and machines cannot break the game
- **No spell ever teleports a rider out of the saddle again.** Gale and Torrent knockback and the elemental walls' bounce were teleporting mounted RIDERS directly — the horse/rider desync class this mod has been burned by before (the freeze system already refused to do it). All knockback now moves the HORSE the same distance and lets the rider follow; unmounted targets move as before.
- **The alchemist's yellow cloud obeyed no law** — it could kill enemy lords outright with a direct health write. It now flows through the one spell-damage pipeline (heroes floored, wards and brands apply), like everything else.
- Re-audited after the interactivity pass: every damage/heal loop skips mounts; siege machines are only ever touched through their own destructible component (never as agents, never with a null attacker); speed tokens hold agent references so reused agent indexes can't slow the wrong man; all mission-scoped state is cleared on both cleanup paths.

### Parity fixes caught by the cone/wall verification pass
- **Wind, Earth and Water could kill enemy heroes outright** where Fire correctly leaves them at death's door — the nature-routed damage bypassed the canonical spell-damage path. All element damage (attacks and wall bites, player and NPC alike) now flows through one pipeline: heroes are floored, Cinder Shell and Sunder apply, kill credit flows.
- **The golden ward now holds against every element** — Gale, Entangle and Torrent were ignoring it; only Fire respected it.

### The full draw sets its marks alight — fire crosses the kill threshold
- **A deeply-drawn fire cone now IGNITES everyone it strikes.** The burn scales with the charge — nothing on a snap flick, up to **12/s for 5 s** at a full draw — so a fully-charged cone (44 on the strike + 60 burning after) finally finishes an unarmoured man over the seconds that follow, and leaves an armoured one crippled, where before even the fullest draw left every looter standing. Spam gains nothing: weak casts still ignite nothing, and re-igniting only refreshes the fiercer burn.
- **The Ashen cold clings on as deep frost** — the same numbers under the colder mask.
- **NPC parity is automatic:** a mage lord's full-power cone ignites exactly as the player's does (and his near-burnout half-power casts barely smoulder); the boss tier burns at full.

### Balance — the borrowed fire and the crossed ward
- **Bandit mages no longer out-burn the boss tier.** A Fire Worshipper's blast dealt 70 true damage — harder than the False Emperor's fullest cone (52.8) and a trained lord's full draw (44). The borrowed fire now burns at one crude flat heat (35, crystal-tier) for every bandit rank; skill widens the working's reach instead.
- **An enemy lord's Spirit ward no longer heartens YOUR men.** The ward's party-morale lift was hard-wired to the player's party whoever cast it; it now belongs to the caster alone.

## v0.36.0

### Mage lords, priests and crystal-bearers fight like people, not scripts
- **NPC mages now wield exactly the same magic the player does** — the five unified elements (Fire, Wind, Earth, Water, Spirit) and their Ashen cold variants, and nothing of the retired fire tradition. Their battle fire moved off the old pre-merge engine onto the very cone the player throws (same shape, damage, power-scaling), and the old spell-brands (the cold that takes, the sundering, the smoulder) are gone from their casting just as they are from the player's.
- **On the campaign map they cast the element workings too** — Emberfall, Scattering Gale, Deeproot Blight, Tidewash and Farsight (Coldfall, Stormfront, Ashrot, Snowmelt and the Void's Sight for the Ashen) — drawn from the elements each lord has learned, in place of the old fire-path map spells.
- **They choose the element that fits the moment** instead of throwing at random: a gale or a rooting stone when surrounded, a slowing wave or roots to break a cavalry charge, a forward fire cone at a lone target, cold dread to unmake a formation. A lord only uses what he has actually learned; Fire is always there as a fallback.
- **A lord spends his life like a person spends a purse.** Casting costs him years of life expectancy, so a young lord with time to burn casts big and often, while one near his burning-out grows careful — weaker workings, cast rarely — and pours out full power only when his life is on the line. Temperament tilts it: **impulsive** lords spend recklessly, **calculating** lords hoard their years (in battle *and* on the campaign map).
- The **Ashen and the False Emperor** keep their menace through raw power, not old brands: they pay no life, know all five elements, and cast at full boss strength.
- **Crystal-bearers use crystals when it helps** — a warmth-stone when they are actually hurt, an offensive stone only when enemies are in reach — instead of a blind roll.
- **The devout answer the moment** — grace lords and priests now mend the wounded, raise a shield under a press, call judgement down on the Ashen, or rally a steady line, rather than praying at random. Grace remains an unlimited wellspring.

### The Duneborn
- **Aserai is now presented as the Duneborn** — the same treatment as Vlandia → The Holy Temple, Khuzait → Tribes of the East, and Sturgia → Northmen. The culture reads "Duneborn" on the character sheet, the encyclopedia, troop culture, and the character-creation card. They remain mechanically ordinary Aserai — only the name and the blurb change.
- **New lore:** the Duneborn once kept the same fire-covenant as every culture, until a generations-long drought broke it along with their wells. What the first Duneborn found in the black-glass caverns beneath the dunes asked no devotion — only blood — and cares nothing for what is done with the power it grants. This finally gives lore grounding to the existing (and previously unexplained) tendency of Aserai lords to be quietly seeded with Dark Gifts.

### Lords speak in their own tongue — flavourful dialogue for every hall
- **Tribes of the East** lords now answer with the fanatical, blood-hungry voice of the God-King's riders, in place of vanilla Khuzait lines.
- **Northmen** lords now answer bluntly and bravely, as befits the folk holding the cold line against the Ashen.
- **Duneborn** lords now answer with the distant, calculating menace of the desert's debt-collectors and scholars.
- Each faction speaks from its own pool of lines across greetings, barter, defeat, special requests, and captivity — the same lord always says the same line, mirroring the existing Holy Temple dialogue. The Ashen's wordless "..." remains untouched.

### The grimoire's Talents open the true Codex
- The grimoire's **Talents** button (Left Alt + X / LB + RB) now opens the **Codex of the Inner Fire** — the living elemental paths (Fire, Wind, Earth, Water, Spirit and the Steel / Blood / Nature disciplines) — instead of the retired fire classes. (Legacy saves keep whatever they already learned.)

### The elements answer in earnest — real effects for every working
- **Fire** now erupts in true flame again — a full burst of fire along the cone and a standing curtain of flame the length of the wall, not a single flickering light.
- **Earth** breaks the ground in real **stone** — boulder, gravel, and torn roots — beneath an earthen glow.
- **Spirit** wells up as a **spectral haze**, clinging to each foe it unmans.
- **The Ashen cold** answers in driven **snow and frost** where the living would loose flame.

### The five elements, balanced against one another (and against the crystals)
- Each element now holds a clear identity in a tighter power band — a working costs you years of life, so at full draw it outreaches the near-free crystals:
  - **Fire — the bruiser.** The heaviest single strike (cone 44) and a **wall that keeps burning** — a line of fire that scorches any who hold it, for five seconds after the cast.
  - **Wind — the sweeper.** Widest reach of all (a full circle) with a stronger gust (30) that hurls and slows every foe around you.
  - **Earth — the jailer.** Its damage eased (32) but its **hard root** kept — the price of the strongest lockdown in the art.
  - **Water — the breaker.** A heavier forward wave (34) that scatters formations with knockback and a lingering chill.
  - **Spirit — the dread.** Unchanged: no wound, but fear, a scattered order, and a ward that heartens and mends your own.
- **The draw now governs your crowd control, too.** A snap cast roots and slows only briefly; a full draw holds a foe for the whole duration — no more full-length locks from an instant flick.

### The charge reaches its peak — and walls that truly stand
- **The gathering now peaks at a full draw.** Hold the charge to its zenith (~7 s) and a whisper tells you *the element is fully gathered* — a generous grace window before it slips your grip at ~15 s. Full strength is now actually attainable, where before the charge always dispersed the instant it maxed.
- **A fully-drawn cone lances far further** — its reach grows with the charge, so a patient cast strikes across the field where a snap flick barely leaves the hand.
- **A fully-drawn wall thickens into a filled rectangle** — a single thin curtain when thrown weakly, several rows deep at full draw, for both fire and the elemental walls.
- **Walls now truly stop what crosses them.** Stone and mist walls were shoving foes only a nudge — a running looter simply out-paced it and walked through unscratched. They now bounce any foe firmly back beyond the wall's edge, and the **mist wall bites** as well as chills (it was doing no damage at all).

---

## v0.35.0

### One magic, five elements — fire and nature, merged
- **Fire and nature magic are now a single art.** Hold **Left Alt** (gamepad **LB**) to focus. Fire is loaded by default — the physical-and-spiritual root of the craft; tap **W / S / A / D** (or flick the left stick) to draw a learned element — **Wind / Earth / Water / Spirit**. **Attack** looses the element's cone, **Block** raises its wall.
  - **Fire** — a cone of fire / a wall of fire. **Wind** — a hurling, slowing blast / a wall that turns arrows and bogs down. **Earth** — a rooting stone burst / a stone wall. **Water** — a slowing wave / a mist barrier. **Spirit** — strike fear into men and horses and shout a stray order into their ranks / a wall that heartens and mends your own.
- **The draw now sets POWER, not price.** There is no minimum — release at once for a weak, instant cast, or hold (up to ~10 s) to strengthen the working to full. Hold the full ten seconds without releasing and the gathered power **disperses** — begin again. The life-cost is **flat**, the same however long you drew.
- **Learn the craft with focus points or from a teacher.** Fire is innate; Wind, Earth, Water, Spirit and three disciplines are learned in the **Codex of the Inner Fire** (**Left Alt + L** on the map), each costing one focus point more than the last — or one less from a **teacher** (the attuned seers you meet). The disciplines: **Steel** (cast with a weapon in hand, bear twice the armour), **Blood** (taking a lord's head gives back the years the fire has burned — clan-tier × 25 days), **Nature** (lowers the flat life-cost of every working).
- **Each element grants a campaign-map working**, cast through the memory-rite from the grimoire's *Cast* menu: **Emberfall**, **Scattering Gale**, **Deeproot Blight**, **Tidewash**, and **Farsight**.
- **The Ashen wear a colder mask** — Fire→Cold, Wind→Storm, Earth→Ash, Water→Snow, Spirit→Void — and pay in criminal standing, not years.
- **NPC mage lords now wield the full elemental kit** (fire, and one element each), alongside the nature seers.

### The Litany of Devotions — miracle talents
- **Grace now has a talent list.** **Left Shift + L** on the map opens the Litany of Devotions: a focus-point devotion for each virtue (learnable once that virtue stands at +1) that **deepens the two prayers it grants**, plus **Abundant Grace**, which widens the Grace well itself.

### Dark Gifts — a price in will, and two roads down
- Dark Gifts now cost **focus points** on top of the blood tithe (one more for each gift already borne). If your heart is still too warm, the altar offers **two** roads to the gate: spill a prisoner's blood to harden your heart (**Mercy** down), or swear a false oath over the dead to break your **Honour**.

### Crystals — study the lattice
- At any Crystalline Chamber you may spend focus points on the lapidary's craft: **Lasting Lattice** (shatter far less often), **Waking Light** (crystals answer at night), and **Swift Kindling** (charge in half the time).

### The cost is your life expectancy, not your age
- Casting no longer makes the player **older** — it **shortens how long they will live**. The fire's toll lowers the age at which it finally burns out (shown in the grimoire's Ledger as your life expectancy), while your current age is left untouched. **Blood** now gives that expectancy back. NPC mages are unchanged (they burn out at 100 or turn Ashen).

### NPC mage lords wield a learned repertoire
- Each NPC mage lord now knows Fire plus a **variable number of other elements (0–4)**, fixed by identity and scaled by standing — a great magnate throws stone, mist and gale where a landless knight only burns. The Ashen (and the false emperor) keep their high cold-fire.

### The Living-Ember start option is retired
- The separate "the world beneath me…" start-of-game choice is removed — the living-world elements are part of the one magic now (its seers remain as your teachers). Existing nature-attuned saves are unaffected.

### Learning costs — a gentler ramp (all talent trees)
- Every focus-point talent tree — the element Codex, the Grace devotions, the crystal lattice, the fire paths and disciplines, and the dark gifts' will-price — now shares one gentler cost curve. Instead of climbing 1 → 2 → 3 → 4 with every purchase, the price holds at each tier for several buys: **1, 1, 2, 2, 2, 3, 3, 3, 3, 4, …**

### Housekeeping
- Removed the orphaned old fire map-spell memory-rite (`SpellMinigame`), replaced by the unified element rite. Journal keybind reference rewritten throughout for the merged magic.

## v0.34.0

### Grace, reforged around your character
- **Your virtues now grant your prayers.** The old per-miracle virtue gates are gone. Instead, each of the five personality traits — Mercy, Valour, Honour, Generosity, Calculating — grants TWO Grace prayers once that trait stands at +1 or higher: one for battle, one for the campaign map. A crueller, more cowardly, or more impulsive hero simply has fewer prayers to call on.
  - **Mercy** — Radiant Mending (heal allies, battle) · The Mending Road (heal the party's wounded, map)
  - **Valour** — Light of Valour (courage + speed, battle) · The Long March (party morale + speed, map)
  - **Honour** — Aegis of the Oath (golden ward, battle) · The Sworn Word (steady a town / warm a lord, map)
  - **Generosity** — Shared Light (consecrate + mend allies, battle) · The Open Hand (party food + morale, map)
  - **Calculating** — Pyre of Judgement (pillar of fire, battle) · Far-Sight (scout the roads, map)
- **Casting and Grace-gathering are unchanged** — the Ctrl-sequence in battle, the Shift+X litany and its memory-rite on the map, and praying for Grace at a Sanctuary all work exactly as before. The map prayers' rites are rewritten to match (a march, an oath, a blessing of the open hand, alongside the healing and guidance prayers).

---

## v0.33.0

### The Northmen
- **Sturgia is now presented as the Northmen** — the same treatment as Vlandia → The Holy Temple and Khuzait → Tribes of the East. The culture reads "Northmen" on the character sheet, the encyclopedia, troop culture, and the character-creation card. They remain mechanically ordinary Sturgia — only the name and the blurb change.
- **Their story is the war that never ends.** The culture description now tells of a hard northern folk who hold the cold edge of the world and stand in the gap against the Ashen pressing down out of the deeper north — "they do not expect to break the cold; they expect to hold the line."

---

## v0.32.0

### Sturgia restored — the Ashen are a fate, not a starting point
- **Sturgia is ordinary Sturgia again.** All of the character-creation rework tied to the northern culture is removed: the injected "forgotten-past" backstory options (which were crashing the game when picked), the "Northerner" opt-out, the culture-card rename to "The Ashen", and the renamed feats are all gone. Choosing the northern culture now behaves exactly like vanilla Sturgia.
- **You no longer START as the Ashen.** Picking the northern culture once forced (or offered) full Ashen status from the first day. That start is removed entirely — every culture, Sturgia included, simply takes the ordinary Gift prompt and chooses its own path. The Ashen are now only ever something you *become* in play: the Last Ember at a century's age, captivity among them, or the cold's darker turns. (The Templar and Tribal culture reworks, and the Ashen kingdom in the world, are unchanged.)

---

## v0.31.0

### Crystals
- **A crystal now answers the swing itself.** It used to require landing a blow on an enemy, in daylight, before it would begin its charge — so waving it in the air did nothing and it felt broken. Now any swing rouses it (hit or miss, day or night); after the brief charge the power releases as before. One charge gathers at a time.
- **Crystals look like stones, not wands.** Bannerlord has no gem mesh, so they now carry a rough held mineral (the closest stock visual to a raw crystal) instead of the wand-like shape they had. The "Crystals" name and the Crystalline Chambers stay as they were.

---

## v0.30.2

### Critical fix
- **The Ashen "Came from nowhere", the Northerner origin, the "I don't know how old I am" age, and the God-King's-Apostle backstories no longer crash.** When such an option was picked, the engine builds its effects by calling `.ToList()` on the option's skill AND trait lists with no null check — and these options set skills but left the trait list null, throwing the instant the option was chosen. Every custom backstory option now provides both lists (empty where unused).

---

## v0.30.1

### Fix
- **Nature roots and slows now actually hold.** When the drained land snared a caster ("dead briars… seize your legs"), or when Entangle/Thunderclap/Gale/Torrent slowed a foe, the message fired but movement was unaffected — the speed limit was set once and the engine wiped it the very next frame. The limit is now re-applied for the whole duration, so roots root and slows slow.

---

## v0.30.0

### Crash fixes
- **The Ashen "Came from nowhere" and "Northerners of the unbroken North" backstories no longer crash the game.** Those custom options were built without the select/consequence handlers the engine expects and threw the instant they were picked. They now carry proper (empty) handlers.
- **Equipping a crystal no longer crashes.** The crystal items were defined without a collision body, a wield animation, or a valid weapon mesh — the game faulted the moment one was equipped. They are now built on a known-good one-handed weapon template.

### World & creation fixes
- **The Ashen keep the Ashen Crown — and their other cities.** The realm's confinement guard identified its own holdings by their original names, but the cities are renamed on session start ("Amprela" → "The Ashen Crown"), so the renamed cities looked like outside conquests and were slowly handed to neighbouring kingdoms. Ashen holdings are now recognised by identity, not by their (changed) name.
- **Backstory flavour stays with its own culture.** Shared youth/education backstory options carried Tribal/Templar wording for every culture, so an Empire youth could read "served as the Tribe's emissary." The flavour now shows only for the matching culture, and the Templar Grace / God-King's Dark Gift those options grant are likewise culture-gated (no more Grace for a non-Templar who picks "groom").

---

## v0.29.2

### Critical fix
- **The faction names rename again — and "Next" still works.** The previous fix stopped the card-renamer too early, so on a slower-loading screen the cards never got corrected. The renamer now keeps working until the cards are fixed (however long the screen takes to build), while the "Next" button is only briefly held and then always freed.

---

## v0.29.1

### Critical fix
- **You can start the game again.** On the culture-selection screen the "Next" button could stay disabled forever, even after the faction names corrected themselves — the renamed-card check waited on a card it could never tick off and held the gate shut. The gate now always releases (within a moment), so character creation can never trap you.

---

## v0.29.0

### Event fixes
- **A purse you don't have can no longer buy a favour.** Several settlement-encounter choices that cost coin granted their reward even when you couldn't pay — the bribe, the hired official, the freed prisoner, the saved children all happened for free, with only a "not enough gold" note. Those choices now fail honestly when your purse is short. (Penalties you suffer involuntarily — theft, fines, what the cold takes — are unchanged; you still can't lose coin you don't have.)
- **Court intrigues no longer drag your own throne into someone else's war.** The scripted diplomatic incidents (a slighted envoy, torched border villages, a murdered emissary, and the like) could pick your own kingdom as one of the quarreling crowns and force you into a war you had no part in. They now always spare the player's faction, the same way the succession-crisis and "Broken Will" events already do.

---

## v0.28.0

### The prayer is the casting
- **Miracles on the campaign map are now spoken as prayers — a memory rite, the way fire magic is recalled.** Open the litany (**Shift+X**), choose a prayer, and the light shows you its three verses. Recall them truly when asked and the miracle answers in full; let the words scatter and the Grace is spent for nothing. (Battle is unchanged: there, prayers are still cast by tracing their sequence.)
- **The four field prayers are written in the cadence of the old devotions** — deliverance for *Repel the Ashen* ("stand between me and the cold"), *Domine non sum dignus* for *Radiant Mending*, *Lead, kindly Light* for *Light of Guidance*, and *Cor mundum crea in me* for the *Cleansing Rite* — kept plain enough to remember, no harder than the fire-rites.

---

## v0.27.0

### Prayers, shaped like spells
- **Miracles are now cast by tracing their form — in battle and on the march alike.** Hold **Ctrl** and trace the prayer's sequence (controller: hold **RB** and flick the left stick), exactly as fire magic is shaped. There is no longer any menu-casting in battle; the battlefield answers the gesture alone.
- **The miracle menu is a campaign-map convenience, and shows only what belongs there.** Opened with **Shift+X** (or RB + L3) on the map, it now lists only the prayers that answer on the march — battle-only miracles are no longer shown greyed-out among them. A prayer offered on the wrong ground fizzles (and still spends its Grace), just as a botched sequence always has.

### Balance
- **Fire no longer burns through your years so fast.** The aging cost of a battle spell was eased across the whole curve (geometric base 1.65 → 1.5): a full ten-input working now costs **38 days instead of 84**, and mid-sized castings roughly halve. The fire returns more for what it takes.

### Fixes
- **The Ashen keep their cities at game start.** A change in the previous version established the Ashen kingdom too early in world-creation, and the engine promptly undid it — the cold realm's cities snapped back to Sturgia the moment the game began. The realm is now left to form in its proper order, and holds.

### A note on age
- Reaching a great age through fire and *not* dying is intended: the reckoning — the Last Ember, where you choose to pass or to take the cold and become Ashen — comes at **100**, not before.

---

## v0.26.0

### Crystals restored, and a fistful of fixes
- **Crystals work again.** A packaging error shipped an empty item list, so no crystal ever existed in the world — none spawned, none could be granted, and the Crystalline trade was dead on arrival. The full crystal set is restored. *(If you build from source, note the module's ModuleData is now deployed alongside the DLL — the old process copied only the binary.)*
- **A failure in one world-system can no longer swallow your magic.** New-game setup now guards each world establishment on its own, so a fault in one (the crystals, a sanctuary, an altar) can never abort the rest — above all the choice of your fire, without which no magic could be invoked at all.
- **The Living Ember answers the right key on the march.** Reaching for the battle gesture (Ctrl + a direction) on the campaign map now points you to the litany — **Shift+X** — instead of failing in silence.
- **The northern card reads true.** The character-creation culture card for the north now shows **The Ashen** with its proper rites and description, instead of the engine's bare "Sturgians".
- **The Holy Temple and the Tribes of the East are named from the first breath.** Their banners and titles are now set the instant the map loads, rather than snapping into place after the first day passes.
- **The Ashen Crown keeps its own.** The cold realm's city and the castles around it are no longer bled away to the Northern Empire at the start of the game by the empires' border sweep.

### Balance & ritual
- **"Call to the Tribes" is once a week, not once a town.** The God-King's champion could pull free tribesmen from every Tribal town in turn; the tribes now answer only once every seven days, wherever the call goes out.
- **The Dark Altar will harden a warm heart.** A player whose nature is still too gentle for the dark gifts may now spill a prisoner's blood at the altar to drive their mercy toward the cruelty the gifts demand — one offering at a time.

### Lore
- **The opening myth leans on the fire, not the crystals.** The intro no longer foregrounds the crystals; they remain to be discovered, not recited.

---

## v0.25.1

### The grey look belongs to the Ashen alone
- **A mortal Northerner can no longer be mistaken for the Ashen.** The cold appearance — grey skin, ash hair, cold-blue eyes — is now barred at the source for any character who is not truly Ashen, so a Sturgian who kept the living North stays warm-eyed until (and unless) they take the cold themselves. The Ashen start, the Northerner's ordinary Fire/Mortal/Nature choice, and the later paths to becoming Ashen (the Last Ember at a century's age, captivity, and the world's darker turns) are all unchanged.

---

## v0.25.0

### A second northern start — walk as the living, not the Ashen
- **New character-creation origin: the Northerners of the unbroken North.** Choosing the northern (Sturgian) culture no longer commits you to the Ashen. In the family stage of your backstory you may now declare that the grey fire never touched your people — you remain a **mortal Northerner**: you age and die as men do, you bear no Mark, you stay among the living of Sturgia, and you are free to seek (or refuse) the inner fire by the ordinary Gift as any other commander. The default northern origin is still the Ashen; this is the path for those who would face them rather than join them. With it, both northern powers — the Ashen and the living North — now stand at the start of every game.

### Bug fixes
- **"Broken Will" can no longer turn on your own throne.** The event that drives a kingdom mad and raises its banners against all of Calradia could pick the player's own faction, forcing you to war with every realm at once. It now always spares your kingdom.
- **The Branded no longer swallows the week's omens.** For commanders who walk no fire-path, the rite could quietly claim the weekly portent slot and fall silent, starving the world of every other event for a fortnight. Its eligibility is now weighed before the slot is spent.
- **Ashen ruin guards stop multiplying on reload.** Each time a save was loaded, a fresh warband was conjured at every ruin, piling up without end. The wardens now honour their respawn rest and hold their numbers.
- **The cold cannot unmake an Ashen lord's fire.** The mage-population balancer could strip a permanent Ashen lord of their casting while leaving them Ashen in name — they are now exempt from the cull.
- **An apprentice can no longer fall to the shadow unseen.** If another popup was pending, the corruption was latched in silently and the "continue or dismiss" choice was never offered. The choice is now always presented.

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
