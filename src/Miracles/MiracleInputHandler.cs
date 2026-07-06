// =============================================================================
// ASH AND EMBER — Miracles/MiracleInputHandler.cs
//
// BATTLE — the prayer SEQUENCE, shaped like a fire spell:
//   Keyboard: hold Left Ctrl, press W/A/S/D (= U/L/R/D) to build a 6-character
//   sequence, release Ctrl to cast. The buffer shows in the log. A wrong or
//   incomplete sequence still spends 1 Grace.
//   Controller: hold RB and flick the left stick 6 times in the same directions.
//   There is NO menu in battle — the battlefield answers the gesture alone.
//   The Undivided Flame answers a longer, 8-character gesture instead of 6 —
//   keep holding past the sixth tap and the buffer grows to fit it. It still
//   only answers once all five traits are held at once; anyone else's Grace is
//   simply spent for nothing, the same as any other unanswered sequence.
//
// CAMPAIGN MAP — the litany + the rite (mirrors fire magic's map casting):
//   Open the litany (Shift+X / RB + L3) — it lists ONLY the prayers that answer
//   on the march — choose one, then recall its three-step rite (MiracleMinigame).
//   Recall it truly and the light answers; let the words scatter and the Grace is
//   spent for nothing.
//
// Left Ctrl does not conflict with spell input (which uses Left Alt / LB).
// While focusing (Ctrl held, Grace in hand), Ctrl+X — or RB+Y on a controller —
// opens a READ-ONLY reference of the prayers you can offer and their sequences
// (the miracle counterpart to the element grimoire on Alt+X); it never traces a
// direction, so it cannot start or corrupt a sequence.
// RB + L3 is the map miracle menu — distinct from the alchemist's RB + R3.
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

        // ── Controller stick edge-detect ──────────────────────────────────────
        private static bool _prevLUp, _prevLDown, _prevLLeft, _prevLRight;

        public static void ResetInputState()
        {
            _seqBuffer   = "";
            _wasHolding  = false;
            _lastDisplay = "";
            _prevShiftX  = false;
            _prevPadBoth = false;
            _prevLUp = _prevLDown = _prevLLeft = _prevLRight = false;
        }

        public static void Tick(bool inMission)
        {
            if (inMission)
                // Battle: cast directly by tracing the prayer's sequence (key combos).
                TickSequence(inMission: true);
            else
                // Campaign map: choose a prayer from the litany (Shift+X), then recall its
                // rite — the memory minigame — exactly as fire magic is cast on the map.
                TickCampaignMenu();
        }

        // ── Prayer sequence: Ctrl + W/A/S/D (or RB + left stick) ──────────────
        // Hold the focus key, trace the miracle's sequence, release to cast. Works on
        // the campaign map and in battle alike; the context only changes which miracle
        // answers (a battle prayer fizzles on the map, and the reverse).
        private static void TickSequence(bool inMission)
        {
            bool rbHeld  = !Input.IsKeyDown(InputKey.ControllerLBumper)
                        &&  Input.IsKeyDown(InputKey.ControllerRBumper);
            bool holdKb  = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool holdPad = rbHeld;
            // Only heroes carrying Grace react to the focus modifier. Without Grace,
            // holding Ctrl does nothing — no focus light, no buffer — so it never bleeds
            // into Nature's casting or glows for heroes with no miracles.
            bool holding = (holdKb || holdPad) && MiracleInventory.HasGrace;

            if (holding)
            {
                if (!_wasHolding)
                {
                    try { if (inMission && Agent.Main != null)
                            SpellEffects.BeginFocusVisual(Agent.Main, ColorSchool.Yellow); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                _wasHolding = true;

                // Spellbook: Ctrl + X (keyboard) or RB + Y (controller), only before
                // any direction has been traced — mirrors the element grimoire on
                // Alt + X. A read-only list of the prayers you can offer here and the
                // sequences that call them; casting is still the gesture itself.
                bool bookKey = holdKb ? Input.IsKeyPressed(InputKey.X)
                                      : Input.IsKeyPressed(InputKey.ControllerRUp);
                if (bookKey && _seqBuffer.Length == 0)
                {
                    ShowMiracleReference(inMission);
                    return;
                }

                if (holdKb)
                {
                    if      (Input.IsKeyPressed(InputKey.W)) Append("U");
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
                // Past the sixth tap the target grows to the Undivided Flame's 8, so the
                // padding never goes negative and the player sees they've crossed over.
                int displayTarget = _seqBuffer.Length <= MiracleMath.SequenceLength
                    ? MiracleMath.SequenceLength : MiracleMath.UltimateSequenceLength;
                string display = _seqBuffer + new string('_', displayTarget - _seqBuffer.Length);
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
                try { if (inMission) SpellEffects.EndFocusVisual(Agent.Main); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                TryCastSequence(inMission);
                _seqBuffer = "";
            }
        }

        private static void Append(string dir)
        {
            // Capped at the Undivided Flame's longer length — a normal miracle is cast
            // the moment 6 taps release, but holding past that lets the buffer keep
            // growing to fit the rarer 8-character gesture.
            if (_seqBuffer.Length < MiracleMath.UltimateSequenceLength) _seqBuffer += dir;
        }

        private static void TryCastSequence(bool inMission)
        {
            if (_seqBuffer.Length == 0) return;

            if (!MiracleInventory.HasGrace)
            {
                Fizzle("You carry no Grace. Pray at a Sanctuary first.");
                return;
            }

            // Between the two known lengths (7) can only ever be an incomplete
            // Undivided Flame — the normal sequences already resolved at 6.
            if (_seqBuffer.Length < MiracleMath.SequenceLength)
            {
                SpendAndFizzle($"The form is incomplete ({_seqBuffer.Length}/{MiracleMath.SequenceLength}). The power slips away.");
                return;
            }
            if (_seqBuffer.Length > MiracleMath.SequenceLength)
            {
                // Anything past the sixth tap is a deliberate reach for the greater
                // working, which asks its own (heavier) toll up front.
                int ultimateCost = UltimateGraceCost;
                if (MiracleInventory.Grace < ultimateCost)
                {
                    Fizzle($"The greater working asks {ultimateCost} Grace; you carry {MiracleInventory.Grace}. The power slips away untouched.");
                    return;
                }
                if (_seqBuffer.Length < MiracleMath.UltimateSequenceLength)
                {
                    SpendAndFizzle($"The greater working is incomplete ({_seqBuffer.Length}/{MiracleMath.UltimateSequenceLength}). The power slips away.", ultimateCost);
                    return;
                }
            }

            if (!MiracleMath.TryMatchSequence(_seqBuffer, out MiracleType type))
            {
                int cost = _seqBuffer.Length == MiracleMath.UltimateSequenceLength ? UltimateGraceCost : 1;
                SpendAndFizzle($"No miracle answers the sequence '{_seqBuffer}'. The power slips through your fingers.", cost);
                return;
            }

            // A miracle answers only where it is meant to — the wrong ground still
            // spends the Grace, as a botched battle sequence always has.
            foreach (var def in MiracleCatalog.GraceAll)
            {
                if (def.Type != type) continue;
                if (inMission && !def.UsableInBattle)
                {
                    SpendAndFizzle($"{def.Name} answers only on the open road, not amid the clash of battle.", def.GraceCost);
                    return;
                }
                if (!inMission && !def.UsableOnMap)
                {
                    SpendAndFizzle($"{def.Name} answers only in the heat of battle.", def.GraceCost);
                    return;
                }
                break;
            }

            MiracleEffects.TryUseMiracle(type, inMission);
        }

        private static int UltimateGraceCost => MiracleCatalog.Get(MiracleType.UndividedFlame).GraceCost;

        private static void SpendAndFizzle(string msg) => SpendAndFizzle(msg, 1);

        private static void SpendAndFizzle(string msg, int amount)
        {
            MiracleInventory.SpendGrace(amount);
            Fizzle(msg);
        }

        private static void Fizzle(string msg) =>
            InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.6f, 0.6f, 0.6f)));

        // ── Campaign map menu: Shift+X (keyboard) or RB+L3 (controller) ────────
        // A map-only convenience listing the prayers that answer on the march; the
        // same prayers can also be cast directly with the Ctrl-sequence above.
        private static void TickCampaignMenu()
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

            // This window is a campaign-map convenience only (the battle path is the
            // Ctrl-sequence). So it lists ONLY the prayers that answer on the march —
            // battle-only miracles are left off entirely rather than shown greyed.
            var elements = new List<InquiryElement>();

            foreach (var def in MiracleCatalog.GraceAll)
            {
                if (!def.UsableOnMap) continue;   // map menu: skip battle-only prayers

                // Each prayer is granted by a personality trait; it answers only once
                // that trait stands at +1 or higher.
                bool gateMet = MiracleEffects.PlayerMeetsTrait(def);

                string keys  = SequenceToKeys(def.Sequence);
                string stick = SequenceToStick(def.Sequence);
                string gate  = string.IsNullOrEmpty(def.GateNote) ? "" : "  " + def.GateNote;
                string costNote = def.GraceCost != 1 ? $"  [{def.GraceCost} Grace]" : "";
                string label = $"{def.Name}   [Ctrl + {keys}]   ({def.Context}){gate}{costNote}";
                string controls = $"Keyboard: hold Ctrl + {keys}\nController: hold RB + flick left stick {stick}";
                string hint  = $"{controls}\n\n{def.Effect}\n\n{def.Flavour}";
                if (!gateMet)
                    hint = $"✗  The light does not yet answer you here — {def.GateExplanation}.\n\n{hint}";
                elements.Add(new InquiryElement(def.Type, label, null, gateMet, hint));
            }

            string title = $"Miracles  [Grace: {MiracleInventory.Grace}/{MiracleMath.GraceCap()}]";
            string body  = "Choose a prayer to offer on the march. Most cost 1 Grace — the rarer " +
                  "workings cost more, shown beside their name. Recall its rite truly and the " +
                  "light answers in full; let the words scatter and the Grace is spent for nothing.";

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    title, body, elements, true, 1, 1, "Invoke", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var type = (MiracleType)chosen[0].Identifier;
                        // Cannot open an inquiry from inside this callback — defer to the next
                        // layer flush, then run the prayer's memory-rite (mirrors fire magic).
                        MageKnowledge._deferredInquiry = () => MiracleMinigame.Begin(type);
                    },
                    null, "", false), false, true);
            }
            catch
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The miracle window will not open just now.", new Color(0.7f, 0.7f, 0.7f)));
            }
        }

        // ── Battle reference: Ctrl + X (keyboard) or RB + Y (controller) ───────
        // A READ-ONLY list of the prayers you can offer here and the sequences that
        // call them — the miracle counterpart to the element grimoire (Alt + X).
        // There is still no battle "menu"; casting stays the gesture itself. This
        // just lets you see the combinations without leaving the fight.
        private static void ShowMiracleReference(bool inMission)
        {
            if (!MiracleInventory.HasGrace)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "You carry no Grace. Pray at a Sanctuary first.", new Color(0.7f, 0.7f, 0.7f)));
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Hold Left Ctrl, tap the sequence, then release Ctrl to pray.");
            sb.AppendLine("(Controller: hold RB and flick the left stick.)");
            sb.AppendLine();

            int shown = 0;
            foreach (var def in MiracleCatalog.GraceAll)
            {
                bool usableHere = inMission ? def.UsableInBattle : def.UsableOnMap;
                if (!usableHere) continue;
                bool gateMet = MiracleEffects.PlayerMeetsTrait(def);
                string keys  = SequenceToKeys(def.Sequence);
                string costNote = def.GraceCost != 1 ? $"   ({def.GraceCost} Grace)" : "";
                sb.AppendLine(gateMet
                    ? $"{def.Name}   [Ctrl + {keys}]{costNote}"
                    : $"{def.Name}   [locked — {def.GateExplanation}]{costNote}");
                if (!string.IsNullOrEmpty(def.Effect)) sb.AppendLine("   " + def.Effect);
                sb.AppendLine();
                shown++;
            }
            if (shown == 0) sb.AppendLine("No prayer answers here.");

            string title = $"Miracles  [Grace: {MiracleInventory.Grace}/{MiracleMath.GraceCap()}]";
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    title, sb.ToString(), true, false, "Close", "",
                    () => { }, null), true, true);
            }
            catch
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The miracle list will not open just now.", new Color(0.7f, 0.7f, 0.7f)));
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

        // The same sequence as left-stick directions, for controller players. U/L/R/D
        // map to the cardinal flicks ↑/←/→/↓ — the keyboard and stick orders match.
        private static string SequenceToStick(string seq)
        {
            if (string.IsNullOrEmpty(seq)) return "";
            var sb = new System.Text.StringBuilder(seq.Length * 2);
            foreach (char c in seq)
            {
                switch (c)
                {
                    case 'U': sb.Append('↑'); break; // ↑
                    case 'L': sb.Append('←'); break; // ←
                    case 'R': sb.Append('→'); break; // →
                    case 'D': sb.Append('↓'); break; // ↓
                    default:  sb.Append(c);        break;
                }
                sb.Append(' ');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
