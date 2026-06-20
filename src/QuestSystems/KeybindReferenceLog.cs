// =============================================================================
// ASH AND EMBER — QuestSystems/KeybindReferenceLog.cs
// A permanent, never-completing Journal entry that records every control the
// mod adds — spellcasting, the grimoire, miracles, and alchemy. It is created
// once per campaign (new or existing save) and re-linked on load, exactly like
// the Dragon Quest log. It is pure reference: no goals, no progress, no events.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static class KeybindReferenceSystem
    {
        internal static KeybindReferenceLog _log = null;

        // Called from MagicCampaignBehavior.OnDailyTick. On a fresh campaign or an
        // older save the quest is absent, so _log is null and we create it. On a
        // save that already holds it, InitializeQuestOnGameLoad has set _log first,
        // so we leave it alone.
        public static void DailyTick()
        {
            try
            {
                if (Campaign.Current == null) return;
                if (_log == null) EnsureLog();
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

        // Each AddLog is one line in the Journal. Written once when the quest is
        // first created; QuestBase persists the entries thereafter.
        internal void WriteEntries()
        {
            AddLog(new TextObject(
                "Every art has its grammar — the hand must learn the shapes before the fire will answer. " +
                "What follows is set down so it is never forgotten."));

            AddLog(new TextObject(
                "SPELLCASTING — Hold Left Alt and trace the Form with W / A / S / D (Up / Left / Right / Down). " +
                "Press X to Break, then trace the Effect with W / A / S / D. Release Alt to loose the spell. " +
                "Gamepad: hold LB, flick the left stick, click L3 to Break, release LB to cast."));

            AddLog(new TextObject(
                "THE FOUR STROKES — W (Up): Blast / Sear.  A (Left): Missile / Force.  " +
                "D (Right): Barrier / Shred.  S (Down): Burst / Restore. " +
                "Up to five strokes each side; the fifth Form stroke Breaks on its own."));

            AddLog(new TextObject(
                "THE GRIMOIRE — Alt + X, before any Form is traced, opens your spellbook and quest record. " +
                "Gamepad: LB + RB."));

            AddLog(new TextObject(
                "MIRACLES — Shift + X opens the litany of Grace and Cold, on the map or in battle; " +
                "each entry shows its battle keys and what it does. " +
                "In battle you may also cast directly: hold Left Ctrl and trace the six-stroke sequence " +
                "with W / A / S / D, then release. Gamepad: RB + L3."));

            AddLog(new TextObject(
                "ALCHEMY — Ctrl + X opens your satchel, in the field or in the thick of battle. " +
                "Gamepad: RB + R3."));

            AddLog(new TextObject(
                "THE LIVING EMBER — For those attuned to the living world (not the inner fire). " +
                "Hold Right Alt, then: S = draw a charge from the land.  W = release and cast. " +
                "Gamepad: hold R3 (right stick), L-stick Down = draw, L-stick Up = cast. " +
                "Both hands must be empty. Armour weight must not exceed 25. " +
                "Verdant terrain (forest) draws for free; all other elements cost HP. " +
                "Hermits in Battania, Strugia, and Khuzait teach those who are ready."));
        }
    }
}
