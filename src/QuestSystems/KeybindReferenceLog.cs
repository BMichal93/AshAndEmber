// =============================================================================
// ASH AND EMBER — QuestSystems/KeybindReferenceLog.cs
// A permanent, never-completing Journal entry that records every control the mod
// adds — spellcasting, the grimoire, miracles, alchemy, and the Living Ember
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
                "MIRACLES — Shift + X opens the litany of Grace and Cold, on the map or in battle; " +
                "each entry shows its battle keys and what it does. " +
                "In battle you may also cast directly: hold Left Ctrl and trace the six-stroke sequence " +
                "with W / A / S / D, then release. Gamepad: RB + L3. Each miracle spends 1 Grace or 1 " +
                "Cold; replenish Grace at a Sanctuary, Cold at an Ashen Altar."), true);

            AddLog(new TextObject(
                "MIRACLES OF GRACE (golden) — Repel the Ashen [W W W W D D]: scorch every Ashen near " +
                "you and break their resolve. · Radiant Mending [W W S S A D]: heal yourself and nearby " +
                "allies. · Light of Guidance [W S W S W S]: steady your soldiers — clearer aim, quieter fear."), true);

            AddLog(new TextObject(
                "MIRACLES OF GRACE (cont.) — Sacred Flame [W W D D S A]: your blade burns with holy fire " +
                "(battle only). · Aegis of Faith [A A W W D D]: a golden ward grants more life than your " +
                "body should hold (battle only). · Cleansing Rite [D W A D W W]: lift cold and dread from " +
                "those around you."), true);

            AddLog(new TextObject(
                "MIRACLES OF COLD (blue) — Ashen Curse [S S S S A A]: cold dark light tears at all nearby " +
                "enemies. · Dreadmending [S S W W D D]: drain enemy life into yourself. · Dread Presence " +
                "[S A S A S D]: nearby enemies flinch, slow, and back away."), true);

            AddLog(new TextObject(
                "MIRACLES OF COLD (cont.) — Frost Brand [A A S D W W]: each blow you land chills and slows " +
                "(battle only). · Shadow Shroud [D D S S A A]: darkness blunts the blows that reach you " +
                "(battle only). · Pale Rigor [S S S A A A]: absolute cold freezes every enemy in reach for " +
                "a few seconds (battle only)."), true);

            AddLog(new TextObject(
                "MIRACLES · VIRTUE — Some rites are gated: an entry marked [some virtue] or [full virtue] " +
                "requires standing in Honour, Mercy, and Generosity. The litany window greys out any you " +
                "cannot yet invoke, so check there to see what your character has earned."), true);

            AddLog(new TextObject(
                "ALCHEMY — Ctrl + X opens your satchel, in the field or in the thick of battle. " +
                "Gamepad: RB + R3."), true);

            AddLog(new TextObject(
                "THE LIVING EMBER — the art of those attuned to the living world rather than the " +
                "inner fire. It shares the Left Ctrl key with miracles, but a hero walks only one " +
                "of these paths: Grace, Cold, Nature, and the Dark Gifts are all mutually exclusive."), true);

            AddLog(new TextObject(
                "LIVING EMBER · GATHER A CHARGE — Hold Left Ctrl and STAND STILL (both hands empty, " +
                "armour weight ≤ 25). A bar fills over about six seconds, coloured by the element of " +
                "the ground beneath you; when it completes you hold one charge, which lasts about " +
                "thirty seconds. Moving interrupts the gathering."), true);

            AddLog(new TextObject(
                "LIVING EMBER · CAST — While holding Left Ctrl and carrying a charge:  Attack " +
                "(left mouse) looses the element's ATTACK,  Block (right mouse) calls its SUPPORT. " +
                "Each cast spends the charge. Gamepad: hold R3 to gather, Right Trigger = attack, " +
                "Left Trigger = support."), true);

            AddLog(new TextObject(
                "LIVING EMBER · THE FOUR ELEMENTS — The land decides, not you: " +
                "WIND on mountains, hills and steppes; EARTH in forests; WATER by rivers, shores, " +
                "rain and snow; STORM on open plains and deserts. Mixed or unfamiliar ground gives " +
                "a random element."), true);

            AddLog(new TextObject(
                "LIVING EMBER · THE POWERS — WIND: Gale (Attack — a ring of wind, knockback + " +
                "damage) · Tailwind (Block — haste for you and allies). EARTH: Entangle (Attack — " +
                "roots that hold foes fast and wound them) · Bulwark (Block — heavy damage resistance). " +
                "WATER: Torrent (Attack — a cone that scatters and breaks formations) · Renewal " +
                "(Block — heal). STORM: Thunderclap (Attack — a bolt that chains between foes) · " +
                "Stormstep (Block — a sudden dash)."), true);

            AddLog(new TextObject(
                "LIVING EMBER · ON THE CAMPAIGN MAP — Halt in open country; standing still a few " +
                "hours lets the land fill a charge of the local element. Spend it through the litany " +
                "window (Shift+X / RB+L3): Wind eases the march, Earth and Water mend your wounded, " +
                "Storm quickens the column."), true);

            AddLog(new TextObject(
                "LIVING EMBER · DEEPER ATTUNEMENT — The hermits' teachings refine the art. " +
                "Living Root: hold two charges. Open Grip: your charge never fades. " +
                "Still Draw / Deep Earth: the bar fills faster. Dawn Call: the land gives more " +
                "readily on the march. Hermits in Battania, Sturgia, and the Khuzait steppe teach " +
                "those who come ready — empty-handed and lightly armoured."), true);

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
