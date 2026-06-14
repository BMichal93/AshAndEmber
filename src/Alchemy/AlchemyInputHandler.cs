// =============================================================================
// ASH AND EMBER — Alchemy/AlchemyInputHandler.cs
//
// Opens the satchel screen with Ctrl+X — in the field or in the thick of battle.
// Deliberately independent of the spell handler's Alt-focus loop: it only fires
// on a fresh X press WHILE Left Control is held and Left Alt is NOT, so it never
// shadows a movement key or interrupts spell-casting. No input is suppressed; it
// merely raises a selection popup, exactly like the grimoire does.
//
// The same ShowSatchel() screen is reachable from the Alchemical Lab menu. It
// lists every elixir carried, the count of each, and the satchel's fill against
// capacity (= Intelligence). Picking one drinks it: clean elixirs fire their
// effect, tainted ones backfire.
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class AlchemyInputHandler
    {
        private static bool _prevCombo;

        public static void Tick(bool inMission)
        {
            bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool alt  = Input.IsKeyDown(InputKey.LeftAlt);
            bool combo = ctrl && !alt && Input.IsKeyPressed(InputKey.X);

            // Edge-detect: act only on the frame the chord newly completes.
            if (combo && !_prevCombo)
                ShowSatchel(inMission);

            _prevCombo = combo;
        }

        public static void ResetInputState() => _prevCombo = false;

        // Lists the satchel and lets the player drink one vial. Shared by the
        // Ctrl+X chord and the Alchemical Lab menu option.
        public static void ShowSatchel(bool inMission)
        {
            int cap  = AlchemyInventory.Capacity();
            int held = AlchemyInventory.Count;

            if (held == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Your satchel is empty. (0/{cap} vials)", new Color(0.8f, 0.78f, 0.6f)));
                return;
            }

            var elements = new List<InquiryElement>();
            foreach (var type in AlchemyInventory.HeldTypes())
            {
                var def   = AlchemyCatalog.Get(type);
                int count = AlchemyInventory.CountOf(type);
                bool usableHere = inMission ? def.UsableInBattle : def.UsableOnMap;
                elements.Add(new InquiryElement(
                    type,
                    $"{def.Name}  ×{count}  [{def.Context}]",
                    null,
                    usableHere,                       // grey out elixirs that can't be used here
                    $"{def.Effect}  {def.Flavour}"));
            }

            string title = $"Satchel  ({held}/{cap})";
            string body  = inMission
                ? "You fumble for a vial mid-fight. Choose one to drink now."
                : "You take stock of your elixirs. Choose one to use.";

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    title, body, elements, true, 1, 1, "Drink", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var type = (ElixirType)chosen[0].Identifier;
                        AlchemyEffects.TryConsumePlayer(type, inMission);
                    },
                    null, "", false), false, true);
            }
            catch
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The satchel will not open just now.", new Color(0.8f, 0.78f, 0.6f)));
            }
        }
    }
}
