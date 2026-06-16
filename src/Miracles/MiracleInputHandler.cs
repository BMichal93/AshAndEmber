// =============================================================================
// ASH AND EMBER — Miracles/MiracleInputHandler.cs
//
// TWO input modes for miracles:
//
// BATTLE — two paths:
//   Keyboard: hold Left Ctrl, press W/A/S/D (= U/L/R/D) to build a 6-character
//   sequence, release Ctrl to cast. The buffer shows in the combat log.
//   A wrong or incomplete sequence still spends 1 Grace or Cold.
//
//   Controller: RB + L3 (Right Bumper held, Left Stick clicked) → opens the
//   miracle selection menu. Or hold RB and flick the left stick 6 times for the
//   sequence if you know it — but the menu is easier in the heat of battle.
//
// CAMPAIGN MAP — menu-based:
//   Keyboard: Shift+X. Controller: RB + L3 (same as battle — consistent).
//
// Left Ctrl does not conflict with spell input (which uses Left Alt / LB).
// Ctrl+X triggers Alchemy — but only when X is pressed, not W/A/S/D.
// RB + R3 is Alchemy; RB + L3 is Miracles — distinct thumbstick clicks.
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class MiracleInputHandler
    {
        // ── Battle sequence state ──────────────────────────────────────────────
        private static string _seqBuffer   = "";
        private static bool   _wasHolding  = false;
        private static string _lastDisplay = "";

        // ── Campaign combo state ───────────────────────────────────────────────
        private static bool _prevShiftX  = false;
        private static bool _prevPadBoth = false;

        // ── Controller stick edge-detect (battle) ──────────────────────────────
        private static bool _prevLUp, _prevLDown, _prevLLeft, _prevLRight;
        private static bool _prevPadMenu;

        public static void ResetInputState()
        {
            _seqBuffer   = "";
            _wasHolding  = false;
            _lastDisplay = "";
            _prevShiftX  = false;
            _prevPadBoth = false;
            _prevPadMenu = false;
            _prevLUp = _prevLDown = _prevLLeft = _prevLRight = false;
        }

        public static void Tick(bool inMission)
        {
            if (inMission)
                TickBattle();
            else
                TickCampaign();
        }

        // ── Battle: Ctrl + WASD 6-sequence ───────────────────────────────────
        private static void TickBattle()
        {
            // Controller shortcut: RB + L3 → menu (no sequence needed).
            bool rbHeld  = !Input.IsKeyDown(InputKey.ControllerLBumper)
                        &&  Input.IsKeyDown(InputKey.ControllerRBumper);
            bool padMenu = rbHeld && Input.IsKeyPressed(InputKey.ControllerLThumb);
            if (padMenu && !_prevPadMenu)
            {
                _seqBuffer   = "";
                _wasHolding  = false;
                _lastDisplay = "";
                _prevLUp = _prevLDown = _prevLLeft = _prevLRight = false;
                ShowMiracleMenu();
            }
            _prevPadMenu = padMenu;

            bool holdKb  = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            // Controller: hold RBumper alone (not with LBumper which is spells).
            bool holdPad = rbHeld;
            bool holding = holdKb || holdPad;

            if (holding)
            {
                _wasHolding = true;

                if (holdKb)
                {
                    if (Input.IsKeyPressed(InputKey.W)) Append("U");
                    else if (Input.IsKeyPressed(InputKey.A)) Append("L");
                    else if (Input.IsKeyPressed(InputKey.D)) Append("R");
                    else if (Input.IsKeyPressed(InputKey.S)) Append("D");
                }
                else
                {
                    // Controller: left stick directions while RBumper held.
                    bool lUp    = Input.IsKeyDown(InputKey.ControllerLStickUp);
                    bool lDown  = Input.IsKeyDown(InputKey.ControllerLStickDown);
                    bool lLeft  = Input.IsKeyDown(InputKey.ControllerLStickLeft);
                    bool lRight = Input.IsKeyDown(InputKey.ControllerLStickRight);
                    if (lUp    && !_prevLUp)    Append("U");
                    if (lDown  && !_prevLDown)  Append("D");
                    if (lLeft  && !_prevLLeft)  Append("L");
                    if (lRight && !_prevLRight) Append("R");
                    _prevLUp = lUp; _prevLDown = lDown; _prevLLeft = lLeft; _prevLRight = lRight;
                }

                // Show buffer with remaining placeholder underscores.
                string display = _seqBuffer + new string('_', MiracleMath.SequenceLength - _seqBuffer.Length);
                if (display != _lastDisplay)
                {
                    _lastDisplay = display;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[ " + display + " ]",
                        new Color(MiracleInventory.HasGrace ? 0.95f : 0.35f,
                                  MiracleInventory.HasGrace ? 0.82f : 0.55f,
                                  MiracleInventory.HasGrace ? 0.35f : 0.90f)));
                }
            }
            else if (_wasHolding)
            {
                _wasHolding  = false;
                _lastDisplay = "";
                _prevLUp = _prevLDown = _prevLLeft = _prevLRight = false;
                TryCastBattle();
                _seqBuffer = "";
            }
        }

        private static void Append(string dir)
        {
            if (_seqBuffer.Length < MiracleMath.SequenceLength) _seqBuffer += dir;
        }

        private static void TryCastBattle()
        {
            if (_seqBuffer.Length == 0) return;

            if (!MiracleInventory.HasGrace && !MiracleInventory.HasCold)
            {
                Fizzle("You carry neither Grace nor Cold.");
                return;
            }

            if (_seqBuffer.Length < MiracleMath.SequenceLength)
            {
                SpendAndFizzle($"The form is incomplete ({_seqBuffer.Length}/{MiracleMath.SequenceLength}). The power slips away.");
                return;
            }

            if (!MiracleMath.TryMatchSequence(_seqBuffer, out MiracleType type))
            {
                SpendAndFizzle($"No miracle answers the sequence '{_seqBuffer}'. The power slips through your fingers.");
                return;
            }

            MiracleEffects.TryUseMiracle(type, inMission: true);
        }

        private static void SpendAndFizzle(string msg)
        {
            if (MiracleInventory.HasGrace) MiracleInventory.SpendGrace();
            else if (MiracleInventory.HasCold) MiracleInventory.SpendCold();
            Fizzle(msg);
        }

        private static void Fizzle(string msg) =>
            InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.6f, 0.6f, 0.6f)));

        // ── Campaign: Shift+X (keyboard) or RB+L3 (controller) → menu ──────────
        private static void TickCampaign()
        {
            bool shiftHeld = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
            bool altHeld   = Input.IsKeyDown(InputKey.LeftAlt);
            bool shiftX    = shiftHeld && !altHeld && Input.IsKeyPressed(InputKey.X);

            if (shiftX && !_prevShiftX)
                ShowMiracleMenu();
            _prevShiftX = shiftX;

            // Controller: RB + L3 (consistent with the battle shortcut; RB+R3 is Alchemy).
            bool padCamp = !Input.IsKeyDown(InputKey.ControllerLBumper)
                        &&  Input.IsKeyDown(InputKey.ControllerRBumper)
                        &&  Input.IsKeyPressed(InputKey.ControllerLThumb);

            if (padCamp && !_prevPadBoth)
                ShowMiracleMenu();
            _prevPadBoth = padCamp;
        }

        public static void ShowMiracleMenu()
        {
            if (!MiracleInventory.HasGrace && !MiracleInventory.HasCold)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "You carry neither Grace nor Cold. Visit a Sanctuary or Ashen Altar.",
                    new Color(0.7f, 0.7f, 0.7f)));
                return;
            }

            int honor = 0, mercy = 0, generosity = 0;
            try
            {
                var h = TaleWorlds.CampaignSystem.Hero.MainHero;
                if (h != null)
                {
                    honor      = h.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Honor);
                    mercy      = h.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy);
                    generosity = h.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Generosity);
                }
            }
            catch { }

            var elements = new List<InquiryElement>();
            var source   = MiracleInventory.HasGrace ? MiracleCatalog.GraceAll : MiracleCatalog.ColdAll;

            foreach (var def in source)
            {
                bool gateMet = def.IsGrace
                    ? MiracleMath.MeetsGraceGate(def.Gate, honor, mercy, generosity)
                    : MiracleMath.MeetsColdGate(def.Gate, honor, mercy, generosity);
                bool usable = def.UsableOnMap && gateMet;

                string label = $"{def.Name}  [{def.Sequence}]  [{def.Context}]  {def.GateNote}";
                elements.Add(new InquiryElement(def.Type, label, null, usable, $"{def.Effect}  {def.Flavour}"));
            }

            string counter = MiracleInventory.HasGrace
                ? $"Grace: {MiracleInventory.Grace}/{MiracleMath.GraceColdCap}"
                : $"Cold: {MiracleInventory.Cold}/{MiracleMath.GraceColdCap}";
            string title = $"Miracles  [{counter}]";
            string body  = "Choose a miracle to invoke. Each costs 1 point. Sequences shown for battle casting.";

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    title, body, elements, true, 1, 1, "Invoke", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var type = (MiracleType)chosen[0].Identifier;
                        MiracleEffects.TryUseMiracle(type, inMission: false);
                    },
                    null, "", false), false, true);
            }
            catch
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The miracle window will not open just now.", new Color(0.7f, 0.7f, 0.7f)));
            }
        }
    }
}
