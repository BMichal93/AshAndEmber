// =============================================================================
// ASH AND EMBER — QuestSystems/KeybindReferenceLog.cs
// A permanent, never-completing Journal entry that records every control the mod
// adds — spellcasting, the grimoire, miracles, crystals, and the Living Ember
// (nature). Created once per campaign (new or existing save), re-linked on load,
// and rewritten in place when the listed controls change between builds. Pure
// reference: no goals, no progress, no events.
// =============================================================================

using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static class KeybindReferenceSystem
    {
        internal static KeybindReferenceLog _log = null;

        // Called from MagicCampaignBehavior.OnDailyTick. On a fresh campaign or an
        // older save the quest is absent, so we create it. On a save that already
        // holds it, InitializeQuestOnGameLoad has set _log first, so we leave it.
        //
        // NOTE: _log is a static and survives between save loads in the same process.
        // Loading a second campaign without restarting can leave _log pointing at the
        // PREVIOUS campaign's quest, so a plain "_log == null" check would wrongly
        // skip creation and the journal entry would never appear. Guard instead on
        // whether the handle actually belongs to the current campaign's quest list.
        public static void DailyTick() => EnsureForSession();

        // Creates the journal entry if it is not already part of the current
        // campaign. Safe to call from new-game setup (so it is there from the
        // start) and from the daily tick (so loaded saves that lack it get it).
        public static void EnsureForSession()
        {
            try
            {
                if (Campaign.Current == null) return;
                bool present = _log != null
                    && Campaign.Current.QuestManager?.Quests?.Contains(_log) == true;
                if (!present) { _log = null; EnsureLog(); return; }
                // Present already: an older save may hold a shorter/outdated copy of
                // the reference text — rewrite it so the controls are always current.
                _log.RefreshEntriesIfStale();
            }
            catch { }
        }

        private static void EnsureLog()
        {
            if (_log != null) return;
            // Assign _log only after the quest fully starts, so a transient failure
            // leaves _log null and we retry next tick rather than stranding a
            // half-created quest that never appears in the Journal.
            var log = new KeybindReferenceLog();
            log.StartQuest();
            log.WriteEntries();
            _log = log;
        }

        public static void ResetForNewGame()
        {
            _log = null;
        }
    }

    public sealed class KeybindReferenceLog : QuestBase
    {
        public KeybindReferenceLog()
            : base("ae_keybind_reference", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("Notes for the Adventurer");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            KeybindReferenceSystem._log = this;
        }

        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        // Not serialized, so it defaults to false each time the quest is loaded —
        // giving us exactly one rewrite per session. That keeps the listed controls
        // current on every load (even text-only changes) without rewriting daily.
        private bool _refreshedThisSession = false;

        // Rewrites the reference text once per session so it always matches the build.
        internal void RefreshEntriesIfStale()
        {
            if (_refreshedThisSession) return;
            _refreshedThisSession = true;
            try { WriteEntries(); } catch { }
        }

        // Each AddLog is one line in the Journal. Clears any prior entries first so
        // this doubles as a refresh; hideInformation:true avoids a wall of toast
        // pop-ups when the (reference-only) entries are written or rewritten.
        internal void WriteEntries()
        {
            try
            {
                if (JournalEntries != null)
                    foreach (var e in JournalEntries.ToList())
                        RemoveLog(e);
            }
            catch { }

            AddLog(new TextObject(
                "Every art has its grammar — the hand must learn the shapes before the fire will answer. " +
                "What follows is set down so it is never forgotten."), true);

            AddLog(new TextObject(
                "SPELLCASTING — Hold Left Alt and trace the Form with W / A / S / D (Up / Left / Right / Down). " +
                "Press X to Break, then trace the Effect with W / A / S / D. Release Alt to loose the spell. " +
                "Gamepad: hold LB, flick the left stick, click L3 to Break, release LB to cast."), true);

            AddLog(new TextObject(
                "THE FOUR STROKES — W (Up): Blast / Sear.  A (Left): Missile / Force.  " +
                "D (Right): Barrier / Shred.  S (Down): Burst / Restore. " +
                "Up to five strokes each side; the fifth Form stroke Breaks on its own."), true);

            AddLog(new TextObject(
                "INNER FIRE · THE FORMS (traced before Break) — W Blast: a cone, 2.5 m longer per " +
                "stroke. A Missile: a projectile that flies and bursts (range and blast grow per " +
                "stroke). D Barrier: a wall, one segment per stroke. S Burst: a ring centred on you, " +
                "2.5 m wider per stroke. You may mix forms in one cast — they all loose together."), true);

            AddLog(new TextObject(
                "INNER FIRE · THE EFFECTS (traced after Break, up to five) — W Sear: ~35 fire damage " +
                "and a shove per stroke. A Force: ~22 damage and a lingering vulnerability. D Shred: " +
                "~22 damage and morale loss. S Restore: ~15 healing and morale to allies (a Burst " +
                "also mends you). Stack and mix freely — damage and restore in one breath if you wish."), true);

            AddLog(new TextObject(
                "INNER FIRE · THE COST — Every cast spends years of your life: the more strokes in " +
                "total (forms + effects), the steeper the aging, up to 84 days for the largest workings. " +
                "Cast outside battle and the cost is paid in campaign days, rising with each casting in " +
                "the same day. Talents soften the toll and unlock the Lost Forms."), true);

            AddLog(new TextObject(
                "THE GRIMOIRE — Alt + X, before any Form is traced, opens your spellbook and quest record. " +
                "Gamepad: LB + RB."), true);

            AddLog(new TextObject(
                "MIRACLES — Each of your PERSONALITY TRAITS grants two prayers once it stands at +1 or " +
                "higher: one for battle, one for the road. In battle, hold Left Ctrl and trace the prayer's " +
                "six-stroke sequence with W / A / S / D, then release (gamepad: hold RB, flick the left " +
                "stick). On the map, Shift + X (gamepad: RB + L3) opens the litany — pick a prayer and " +
                "recall its rite. Each prayer spends 1 Grace; replenish Grace at a Sanctuary."), true);

            AddLog(new TextObject(
                "MIRACLES · MERCY — Radiant Mending [W W S S A D] (battle): heal yourself and nearby allies. · " +
                "The Mending Road [D W A D W W] (map): the party's wounded mend faster. " +
                "MIRACLES · VALOR — Light of Valour [W S W S W S] (battle): courage and speed surge through your " +
                "line. · The Long March [W A W A W A] (map): morale lifts and the miles fall away."), true);

            AddLog(new TextObject(
                "MIRACLES · HONOUR — Aegis of the Oath [A A W W D D] (battle): a golden ward returns damage as " +
                "healing. · The Sworn Word [A D A D A D] (map): steady a wavering town, or warm a lord toward you. " +
                "MIRACLES · GENEROSITY — Shared Light [A A D D W W] (battle): consecrate the ground — ward and " +
                "mend nearby allies. · The Open Hand [S S W W A A] (map): the stores are fuller, the column eats well."), true);

            AddLog(new TextObject(
                "MIRACLES · CALCULATING — Pyre of Judgement [D D W W S S] (battle): a pillar of holy fire falls " +
                "where you look. · Far-Sight [D S D S D S] (map): the light shows the roads and what moves on them. " +
                "A prayer you are not yet virtuous enough to bear is greyed in the litany; raise the granting trait " +
                "to +1 to earn it."), true);

            AddLog(new TextObject(
                "CRYSTALS — Six mineral formations, each focused on a different light. " +
                "Equip one in a weapon slot (Sunstone, Embershard, Rimeshard, Veilstone, " +
                "Stormcrystal, Duskstone). Strike with it during daylight (06:00–20:00) to " +
                "begin a 2-second charge — a coloured glow will rise around you — then the " +
                "effect fires in an area. Each activation carries a 10 % chance to shatter the crystal. " +
                "Crystals are useless at night. No key binding required: simply equip and attack."), true);

            AddLog(new TextObject(
                "CRYSTALS · WHERE TO FIND THEM — Crystalline Chambers operate in eight towns: " +
                "Sargot, Marunath, Ortysia, Revyl, Husn Fulq, Dunglanys, Tyal, and Epicrotea. " +
                "Visit the Chamber to form a crystal from Silver Ore and one trade good " +
                "(chance scales with Medicine + Engineering). Crystals are also sold at those " +
                "towns' markets (expensive, restocked weekly) and can be looted from lords who carry them."), true);

            AddLog(new TextObject(
                "THE LIVING EMBER — the art of those attuned to the living world rather than the " +
                "inner fire. It shares the Left Ctrl key with miracles, but a hero walks only one " +
                "of these paths: Grace, Nature, and the Dark Gifts are all mutually exclusive."), true);

            AddLog(new TextObject(
                "LIVING EMBER · DRAW AN ELEMENT — Hold Left Ctrl and CHOOSE what to draw by tracing a " +
                "direction:  W = Wind · S = Earth · A = Water · D = Storm  (left stick on a pad). Then " +
                "STAND STILL (both hands empty, armour weight ≤ 25) and a bar fills over about six " +
                "seconds, coloured by your chosen element; when it completes you hold one charge, which " +
                "lasts about thirty seconds. Moving interrupts the gathering. The land no longer decides " +
                "the element — you do."), true);

            AddLog(new TextObject(
                "LIVING EMBER · CAST (in battle) — While holding Left Ctrl and carrying a charge:  " +
                "Attack (left mouse) looses the element's ATTACK,  Block (right mouse) calls its BARRIER. " +
                "Each cast spends the charge. Gamepad: hold R3 to gather, Right Trigger = attack, " +
                "Left Trigger = barrier. On the campaign map open the litany (Shift+X) to spend your " +
                "charge on the element's MARCH POWER instead — attacks need enemies, but the land " +
                "still answers in other ways."), true);

            AddLog(new TextObject(
                "LIVING EMBER · THE FOUR ELEMENTS — You choose the element (W Wind · S Earth · A Water · " +
                "D Storm). The land no longer decides WHICH element answers — only how dearly it costs. " +
                "Each terrain FAVOURS certain elements: WIND on mountains, hills and steppes; EARTH in " +
                "forests; WATER by rivers, shores, rain and snow; STORM on open plains and deserts. " +
                "Drawing a favoured element spends little of the place's living energy; drawing against " +
                "the land (water in a desert, say) spends far more."), true);

            AddLog(new TextObject(
                "LIVING EMBER · THE LIVING ENERGY — Every battlefield and every stretch of country holds " +
                "a hidden reserve of living warmth, set by how much grows there (a forest brims; a desert " +
                "holds almost nothing). EVERY draw of nature magic AND every cast of Inner Fire — yours " +
                "or any NPC mage's — spends some of it. You are never told the exact figure, but the land " +
                "warns you as it thins: at the half, at the quarter, and when it runs dry. Drained past " +
                "empty, the land turns on those who DRAW from it: each further nature draw bleeds the " +
                "hearth of the nearest village, and may SOUR — recoiling on the nature caster (damage; a " +
                "working twists). Inner Fire does not commune with the land, only burns it — a fire mage " +
                "is never bitten back, but their casting still strips the ground bare, which is precisely " +
                "what makes the living world treacherous to draw from on a battlefield full of fire. " +
                "The souring takes many shapes — dead briars that root you, a hollowing weakness, a flare " +
                "of grey ash, a slow wither; on the march, spoiled stores, a creeping fever, or a " +
                "contagious despair. Left in peace, a place heals a little each day. THE OLD GREEN: a " +
                "tavern will sell the land-attuned a pouch of rare weeds — smoke it to lose a tenth of " +
                "your health and a few drowsy hours, but for a day a third of your draws will cost the " +
                "land nothing at all."), true);

            AddLog(new TextObject(
                "LIVING EMBER · THE POWERS — WIND: Gale (Attack — a ring of wind, knockback + damage) · " +
                "Windwall (Block — a howling wall that hurls foes back). " +
                "EARTH: Entangle (Attack — roots that hold foes fast and wound them) · " +
                "Thornwall (Block — erupting thorns: roots and bleeds all who press against it). " +
                "WATER: Torrent (Attack — a cone that scatters and breaks formations) · " +
                "Mistwall (Block — a churning water curtain that pushes and slows). " +
                "STORM: Thunderclap (Attack — a bolt that chains between foes) · " +
                "Stormwall (Block — a crackling lightning field that sears all who enter). " +
                "All barriers last several seconds and pulse every heartbeat; NPCs who walk the living path raise them in defence."), true);

            AddLog(new TextObject(
                "LIVING EMBER · ON THE CAMPAIGN MAP — First open the litany (Shift+X / RB+L3) to CHOOSE " +
                "which element to draw; the window shows whether the ground you stand on favours it (a " +
                "gentle draw) or not (a costly one). Then halt in open country — standing still a few " +
                "hours fills a charge of your chosen element. Open the litany again to spend it on that " +
                "element's MARCH POWER. Each has a real cost — the land " +
                "gives nothing for free. " +
                "WIND — Windward: the wind presses the march forward — if you have a destination set, " +
                "the column advances several leagues toward it. If not, the wind goes ahead and comes " +
                "back with word of enemies within reach. +10 morale. BUT supplies scatter in the gust " +
                "(~15 food lost). " +
                "EARTH — Root-Mend: the roots go deep and swell the hearth of the nearest village " +
                "(+50 hearth). BUT the roots take a tithe from the nearest living vessel — you lose 15 HP. " +
                "WATER — Still Waters: you must stand on water — a river, shore, or coast. A current " +
                "runs beneath your feet and shows you every harbour at once; pick one and the water " +
                "carries the whole column there. BUT soldiers arrive cold and unsure of where they are " +
                "(-20 morale on arrival). Only coastal harbour towns appear as destinations. " +
                "STORM — Thunder's Edge: three bolts crack the sky and your soldiers roar; nearby " +
                "enemies on the map falter (-20 morale to them). +35 morale. BUT the lightning does " +
                "not ask which side you fight for — 2-3 of your weakest soldiers are struck and wounded. " +
                "NPC seers draw on the land daily in the same way, favouring Root-Mend when their " +
                "column carries many wounded."), true);

            AddLog(new TextObject(
                "LIVING EMBER · DEEPER ATTUNEMENT — The hermits' teachings refine the art. " +
                "Living Root: hold two charges (draw two elements at once). Still Draw: the bar fills " +
                "twice as fast. Open Grip: your charge never fades. Deep Earth: you draw gently — each " +
                "charge spends only HALF the land's living energy. Dawn Call: the land yields its charge " +
                "an hour sooner on the march. Hermits in Battania, Sturgia, the Khuzait steppe, the " +
                "mountain retreat of Marunath, and the Aserai fringe teach those who come ready — " +
                "empty-handed and lightly armoured."), true);

            AddLog(new TextObject(
                "THE DARK GIFTS — Permanent boons bought at a Dark Altar (in the Empire and the " +
                "old cold lands) with blood: each gift costs a growing tithe of prisoners, then " +
                "prisoners AND captured lords. They are passive and forever — but renounce-able at " +
                "any Dark Altar. Bearing even one gift bars you from Grace and from Nature."), true);

            AddLog(new TextObject(
                "DARK GIFTS · THE PRICE OF DARKNESS — Gifts only work while you are Merciless or " +
                "Devious (Mercy ≤ −1 or Honour ≤ −1). Lose both and your gifts sleep until you " +
                "return to the dark. No keys to press — their power is woven into you. " +
                "Your grimoire (Alt+X) lists the gifts you bear and whether they are active."), true);

            AddLog(new TextObject(
                "DARK GIFTS · THE BOONS — Iron Veil (−10% damage taken) · Dark Strike (+20 dark " +
                "damage on each melee hit) · Soul Mirror (reflect 20% of melee damage) · Dark Spirit " +
                "(a shade hunts the enemy each battle; up to three) · Pale Rider's Curse (horses " +
                "near you die) · Soul Drain (your hits sap enemy morale) · Blood Pact (each kill " +
                "heals you) · Dread Presence (nearby foes lose heart and may rout)."), true);
        }
    }
}
