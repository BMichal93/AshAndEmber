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
using TaleWorlds.MountAndBlade;

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

            // Keyboard shortcut: Shift+X opens the miracle menu mid-battle — the same
            // window the map uses, and consistent with Alt+X (grimoire) / Ctrl+X (satchel).
            // Guarded against Ctrl/Alt so it never shadows the Ctrl sequence or spell focus.
            bool shiftHeld = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
            bool ctrlHeld  = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool altHeld   = Input.IsKeyDown(InputKey.LeftAlt);
            bool shiftX    = shiftHeld && !ctrlHeld && !altHeld && Input.IsKeyPressed(InputKey.X);
            if (shiftX && !_prevShiftX)
            {
                _prevShiftX = true;
                ShowMiracleMenu();
                return;
            }
            _prevShiftX = shiftX;

            bool holdKb  = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            // Controller: hold RBumper alone (not with LBumper which is spells).
            bool holdPad = rbHeld;
            // Only heroes carrying Grace react to the Ctrl modifier.
            // Without Grace, holding Ctrl must do nothing — no focus light, no buffer —
            // so it does not bleed into Nature's casting or glow for heroes with no miracles.
            bool holding = (holdKb || holdPad) && MiracleInventory.HasGrace;

            if (holding)
            {
                if (!_wasHolding)
                {
                    try
                    {
                        if (Agent.Main != null)
                            SpellEffects.BeginFocusVisual(Agent.Main, ColorSchool.Yellow);
                    }
                    catch { }
                }
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

                // Show buffer with remaining placeholder underscores (gold — Grace only).
                string display = _seqBuffer + new string('_', MiracleMath.SequenceLength - _seqBuffer.Length);
                if (display != _lastDisplay)
                {
                    _lastDisplay = display;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[ " + display + " ]", new Color(0.95f, 0.82f, 0.35f)));
                }
            }
            else if (_wasHolding)
            {
                _wasHolding  = false;
                _lastDisplay = "";
                _prevLUp = _prevLDown = _prevLLeft = _prevLRight = false;
                try { SpellEffects.EndFocusVisual(Agent.Main); } catch { }
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

            if (!MiracleInventory.HasGrace)
            {
                Fizzle("You carry no Grace. Pray at a Sanctuary first.");
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
            MiracleInventory.SpendGrace();
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
            // Grace, Cold and Nature are mutually exclusive. A hero attuned to the
            // living world uses this same key (Shift+X / RB+L3) to invoke nature.
            if (NatureKnowledge.IsAttuned)
            {
                NatureInputHandler.ShowNatureMenu();
                return;
            }

            if (!MiracleInventory.HasGrace)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "You carry no Grace. Pray at a Sanctuary first.",
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

            // The same window serves the map and the battlefield. In a mission we gate
            // by battle-usability and invoke the battle effect; on the map, the reverse.
            bool inBattle = false;
            try { inBattle = Mission.Current != null; } catch { }

            var elements = new List<InquiryElement>();

            foreach (var def in MiracleCatalog.GraceAll)
            {
                bool gateMet = MiracleMath.MeetsGraceGate(def.Gate, honor, mercy, generosity);
                bool usable  = (inBattle ? def.UsableInBattle : def.UsableOnMap) && gateMet;

                // Show the actual battle key combo (Ctrl + W/A/S/D), the place it can
                // be used, and any virtue gate — all on the always-visible label.
                string keys  = SequenceToKeys(def.Sequence);
                string gate  = string.IsNullOrEmpty(def.GateNote) ? "" : "  " + def.GateNote;
                string label = $"{def.Name}   [Ctrl + {keys}]   ({def.Context}){gate}";
                // Full description lives in the hover hint.
                string hint  = $"{def.Effect}\n\n{def.Flavour}";
                elements.Add(new InquiryElement(def.Type, label, null, usable, hint));
            }

            string title = $"Miracles  [Grace: {MiracleInventory.Grace}/{MiracleMath.GraceColdCap}]";
            string body  = inBattle
                ? "Choose a miracle to invoke now. Each costs 1 Grace. " +
                  "In battle you may also cast by holding Ctrl and tracing the keys shown."
                : "Choose a miracle to invoke. Each costs 1 Grace. " +
                  "The keys shown are the battle sequence: hold Ctrl and press them in order.";

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    title, body, elements, true, 1, 1, "Invoke", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var type = (MiracleType)chosen[0].Identifier;
                        MiracleEffects.TryUseMiracle(type, inMission: inBattle);
                    },
                    null, "", false), false, true);
            }
            catch
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The miracle window will not open just now.", new Color(0.7f, 0.7f, 0.7f)));
            }
        }

        // Translates a stored U/L/R/D miracle sequence into the keys the player
        // actually presses (W/A/S/D), matching the battle-casting bindings above.
        private static string SequenceToKeys(string seq)
        {
            if (string.IsNullOrEmpty(seq)) return "";
            var sb = new System.Text.StringBuilder(seq.Length * 2);
            foreach (char c in seq)
            {
                switch (c)
                {
                    case 'U': sb.Append('W'); break;
                    case 'L': sb.Append('A'); break;
                    case 'R': sb.Append('D'); break;
                    case 'D': sb.Append('S'); break;
                    default:  sb.Append(c);   break;
                }
                sb.Append(' ');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
