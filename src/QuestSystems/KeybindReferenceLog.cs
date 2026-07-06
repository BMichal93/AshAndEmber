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
                // A quest that deserialised finalized (the engine can conclude/fail a
                // custom log across a save/load) must be treated as absent so a fresh,
                // live entry is created — otherwise the Journal is left with only the
                // dead "failed" copy of Notes for the Adventurer.
                bool present = _log != null
                    && !_log.IsFinalized
                    && Campaign.Current.QuestManager?.Quests?.Contains(_log) == true;
                if (!present) { _log = null; EnsureLog(); return; }
                // Present already: an older save may hold a shorter/outdated copy of
                // the reference text — rewrite it so the controls are always current.
                _log.RefreshEntriesIfStale();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            try { WriteEntries(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            AddLog(new TextObject(
                "Every art has its grammar — the hand must learn the shapes before the fire will answer. " +
                "What follows is set down so it is never forgotten."), true);

            AddLog(new TextObject(
                "MAGIC — Hold Left Alt to FOCUS (gamepad: hold LB). FIRE is loaded by default — the " +
                "physical-and-spiritual root of the art. To draw another element you have LEARNED, tap " +
                "W (Wind) · S (Earth) · A (Water) · D (Spirit) — gamepad: flick the left stick up / down / " +
                "left / right. Stand STILL, with a hand free and your armour light, and DRAW — the " +
                "longer you hold, the HARDER the working strikes, to full strength at about ten seconds. " +
                "There is no minimum: release at once for a weak, instant cast. Then ATTACK (left mouse / " +
                "right trigger) looses its cone, or BLOCK (right mouse / left trigger) raises its wall. " +
                "Hold the full ten seconds without releasing and the gathered power DISPERSES — begin " +
                "again. Release Alt to stop."), true);

            AddLog(new TextObject(
                "MAGIC · THE FIVE ELEMENTS (in battle) — FIRE: a bolt that bursts on impact / a wall of fire. " +
                "WIND: a forward gust that hurls and slows / a wall of wind that turns arrows aside and bogs " +
                "down all who cross. EARTH: a forward line of rooting stone / a stone wall. WATER: a forward " +
                "slowing wave / a barrier of mist. SPIRIT: strike fear into men and horses all around and shout " +
                "a stray order into their ranks / a wall that heartens your own and mends them a little."), true);

            AddLog(new TextObject(
                "MAGIC · THE UNBINDING — Draw an element to its FULLEST (about seven seconds), then press " +
                "ATTACK and BLOCK TOGETHER to unbind it: each element's ultimate working, once per element " +
                "per battle, at a steep flat toll (twelve days). FIRE: a nova — everything around you burns, " +
                "horses bolt, siege timber chars, a burning ring remains. WIND: the gale carries YOU — fly " +
                "where you look; any hit knocks the wind out and you FALL. EARTH: the Sundering — the ground erupts " +
                "around you, hurling foes back and leaving churned rubble that bogs the field. WATER: the sky weeps over a wide field — " +
                "burns quenched, fire halved, horses mired, bowstrings soaked (there is only one sky; a new " +
                "casting tears the old down). SPIRIT: the land sends a champion — an elemental of frost, sand " +
                "or stone fights at your side, then comes apart. Enemy lords know the Unbinding too — when one " +
                "begins the long channel, break it: any blow that lands on him kills the working."), true);

            AddLog(new TextObject(
                "MAGIC · THE COST — Every cast shortens your LIFE EXPECTANCY: it does not make you older " +
                "here and now, but you will die sooner (watch the death age in your grimoire's Ledger). " +
                "The toll is FLAT — a longer draw buys power, never a cheaper cast. Cast outside battle " +
                "and it is paid in days too, rising with each working in the same day. The Ashen pay not " +
                "in life but in criminal standing. The NATURE discipline lowers the flat toll; the BLOOD " +
                "discipline gives life back when you take a lord's head."), true);

            AddLog(new TextObject(
                "MAGIC · LEARNING THE CRAFT — Fire is innate; everything else is learned. The elements " +
                "Wind, Earth, Water and Spirit, and three DISCIPLINES — STEEL (cast with a weapon still " +
                "in hand, and bear twice the armour) · BLOOD (taking a lord's head gives back the years " +
                "the fire has burned) · NATURE (lowers the flat life-cost of every working) — are studied on the map " +
                "with Left Alt + L, the Codex of the Inner Fire. Each costs one more focus point than the " +
                "last. A TEACHER who carries a craft teaches it for one point less — seek out the " +
                "attuned, those the land speaks through, and ask them."), true);

            AddLog(new TextObject(
                "MAGIC · IF YOU ARE ASHEN — the cold reshapes each element: Fire becomes COLD, Wind " +
                "becomes STORM, Earth becomes ASH, Water becomes SNOW, Spirit becomes VOID. The workings " +
                "are the same; only their colour and their price (criminal standing, never years) change."), true);

            AddLog(new TextObject(
                "THE GRIMOIRE — Alt + X (gamepad: LB + RB) opens your spellbook and quest record. From it, " +
                "choose Cast to work an element's CAMPAIGN-MAP spell through its memory-rite: Fire's " +
                "Emberfall (fire rains on a hostile settlement), Wind's Scattering Gale (a hostile host " +
                "thrown into disorder), Earth's Deeproot Blight (a hostile village's hearth withers), " +
                "Water's Tidewash (your own column mended and heartened), and Spirit's Farsight (the " +
                "currents of power — and the knives set behind your back — revealed). Recall the rite " +
                "truly and the working answers in full."), true);

            AddLog(new TextObject(
                "MIRACLES — Each of your PERSONALITY TRAITS grants two prayers once it stands at +1 or " +
                "higher: one for battle, one for the road. In battle, hold Left Ctrl and trace the prayer's " +
                "six-stroke sequence with W / A / S / D, then release (gamepad: hold RB, flick the left " +
                "stick). On the map, Shift + X (gamepad: RB + L3) opens the litany — pick a prayer and " +
                "recall its rite. Each prayer spends 1 Grace; replenish Grace at a Sanctuary. " +
                "THE LITANY OF DEVOTIONS — Left Shift + L on the map opens a talent list that REFINES " +
                "your prayers: a devotion for each virtue (learnable once that virtue stands at +1) " +
                "deepens the two prayers it grants, and Abundant Grace widens the well itself. Each " +
                "devotion costs focus points, one more than the last."), true);

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
                "CRYSTALS · STUDY THE LATTICE — at any Crystalline Chamber you may spend focus points on " +
                "the lapidary's craft: Lasting Lattice (a crystal shatters far less often), Waking Light " +
                "(crystals answer at night as well as by day), and Swift Kindling (the charge kindles in " +
                "half the time). Each costs one focus point more than the last."), true);

            AddLog(new TextObject(
                "THE LIVING WORLD — what was once a separate art of the land is now woven into the one " +
                "magic: its elements are the Wind, Earth and Water you learn (see MAGIC, above), and the " +
                "seers attuned to the living world are now your TEACHERS — speak with them to learn the " +
                "craft they carry for a focus point less than the lonely road would cost."), true);

            AddLog(new TextObject(
                "THE DARK GIFTS — Permanent boons bought at a Dark Altar (in the Empire and the " +
                "old cold lands) with blood AND will: each gift costs a growing tithe of prisoners, " +
                "then prisoners AND captured lords, AND focus points (one more for each gift you " +
                "already bear). They are passive and forever — but renounce-able at any Dark Altar. " +
                "Bearing even one gift bars you from Grace and from Nature."), true);

            AddLog(new TextObject(
                "DARK GIFTS · THE PRICE OF DARKNESS — Gifts only work while you are Merciless or " +
                "Devious (Mercy ≤ −1 or Honour ≤ −1). If your heart is still too warm, the altar " +
                "offers two roads down: spill a prisoner's blood to harden your heart (Mercy), or " +
                "swear a false oath over the dead to break your honour (Honour). Lose both dark " +
                "traits and your gifts sleep until you return to the dark. No keys to press — their " +
                "power is woven into you. Your grimoire (Alt+X) lists the gifts you bear and whether " +
                "they are active."), true);

            AddLog(new TextObject(
                "DARK GIFTS · THE BOONS — Iron Veil (−10% damage taken) · Dark Strike (+20 dark " +
                "damage on each melee hit) · Soul Mirror (reflect 20% of melee damage) · Dark Spirit " +
                "(a shade hunts the enemy each battle; up to three) · Pale Rider's Curse (horses " +
                "near you die) · Soul Drain (your hits sap enemy morale) · Blood Pact (each kill " +
                "heals you) · Dread Presence (nearby foes lose heart and may rout)."), true);
        }
    }
}
