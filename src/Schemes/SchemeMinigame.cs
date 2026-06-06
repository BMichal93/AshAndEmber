// =============================================================================
// ASH AND EMBER — Schemes/SchemeMinigame.cs
// Push-your-luck minigame for player scheme operations.
//
// CORE MECHANIC
// ─────────────
// Each turn:
//   1. A field report arrives (flavour — sets the scene).
//   2. Player chooses an ACTION (deterministic base progress).
//   3. FIELD CONDITIONS are rolled: a random -5 to +5 modifier applied on top.
//   4. Net progress this turn = action_base + field_conditions (floored at 0
//      so bad luck wastes a turn but never reverses overall score).
//   5. Score accumulates. Reach riskSum → SUCCESS. Exceed BustLimit → BUST.
//      Exhaust turns without reaching riskSum → SMALL LOSS.
//
// ACTIONS (available every turn)
// ───────────────────────────────
//   PRESS FORWARD       +5 base — aggressive, fast progress, high bust risk
//   ADVANCE CAREFULLY   +2 base — balanced, safe for most turns
//   HOLD POSITION        0 base — wait for conditions; use with abilities
//
// FIELD CONDITIONS (rolled after action is chosen)
// ─────────────────────────────────────────────────
//   Base range: -5 to +5 (uniform)
//   Relevant skill 150+ raises the floor to -3 (fewer terrible turns)
//   Relevant skill 300+ raises the floor to  0 (conditions always ≥ neutral)
//   Relevant skill = Roguery for Roguery schemes, Charm for Charm schemes.
//
// ONE-USE SKILL ABILITIES (reset each operation)
// ────────────────────────────────────────────────
//   Roguery 150+   SLIP PAST       Skip field conditions — pure action base guaranteed
//   Roguery 300+   SCOUT AHEAD     Reveal THIS turn's conditions BEFORE choosing action
//   Charm   150+   SMOOTH IT OVER  +3 bonus to field conditions this turn
//   Charm   300+   SILVER TONGUE   Field conditions become +5 (maximum) this turn
//
// UI FLOW
// ───────
//   BeginOperation()
//     └─► ShowTurn()          [MultiSelectionInquiry — event report + action choices]
//           ├─► PRESS/ADVANCE/HOLD (+ optional ability modifier)
//           │     └─► ExecuteAction() → ShowActionResult()  [InquiryData]
//           │           ├─► "Continue" → ShowTurn()
//           │           └─► "Abort"    → FinishOperation(SmallLoss)
//           ├─► SCOUT AHEAD → ShowScoutScreen() → ShowTurn() with conditions visible
//           └─► ABORT → FinishOperation(SmallLoss)
//
//   FinishOperation(outcome) [InquiryData — final outcome]
//     └─► OnClose callback: SchemeSystem.ApplyPlayerSchemeOutcome()
//
// All UI text is framed from the commander's perspective.
// {0} in event strings is replaced with the target's actual name.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    // ── Outcome ────────────────────────────────────────────────────────────────
    internal enum SchemeOutcome
    {
        SmallLoss,  // Agent retreated cleanly — no consequences (abort or out of turns)
        Success,    // Score reached riskSum — full scheme effects applied
        Bust,       // Score exceeded BustLimit — agent caught, heavy consequences
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SchemeMinigame — single active operation at a time (Bannerlord's inquiry
    // system is sequential, so static state is safe here).
    // ─────────────────────────────────────────────────────────────────────────
    internal static class SchemeMinigame
    {
        // =====================================================================
        // OPERATION STATE
        // All fields reset by BeginOperation(). They persist across async UI
        // callbacks because Bannerlord's callbacks fire after the inquiry closes.
        // =====================================================================
        #region Operation State

        private static SchemeDefinition _def;
        private static Hero             _targetHero;
        private static Settlement       _targetSett;
        private static string           _targetName;

        private static int _turn;        // 1-based current turn index
        private static int _maxTurns;
        private static int _score;       // accumulated progress (bust at >BustLimit)
        private static int _riskSum;     // progress needed for success

        // One-use ability flags — true = still available this operation
        private static bool _slipPastAvail;
        private static bool _scoutAheadAvail;
        private static bool _smoothItOverAvail;
        private static bool _silverTongueAvail;

        // SCOUT AHEAD state: when used, field conditions are revealed BEFORE
        // the player chooses their action for the same turn.
        private static bool _conditionsRevealed;   // true when SCOUT AHEAD was just used
        private static int  _revealedConditions;   // the revealed roll value

        // Per-turn ability activation flags (cleared inside ExecuteAction after use)
        private static bool _slipPastThisTurn;  // bypass field conditions entirely
        private static bool _smoothThisTurn;    // +3 to field conditions this turn
        private static bool _silverThisTurn;    // conditions become +5 this turn

        // Shuffled event pool for this operation
        private static List<string> _events;
        private static int          _eventIndex;

        // Field conditions for the current turn (set in ShowTurn, consumed in ExecuteAction)
        private static int _thisConditions;

        private static readonly Random _rng = new Random();

        private const int BustLimit = 21;

        // Action base values — fixed regardless of skill level.
        // Skills improve field conditions range instead (see GetConditionsFloor).
        private const int PressBase   = 5;
        private const int AdvanceBase = 2;
        private const int HoldBase    = 0;

        #endregion

        // =====================================================================
        // PER-SCHEME CONFIG
        // RiskSums are calculated so that:
        //   - "all ADVANCE CAREFULLY" falls short (forces some PRESS FORWARD turns)
        //   - "all PRESS FORWARD" risks bust (variance from field conditions matters)
        //   - Optimal play requires reading conditions and mixing actions
        // =====================================================================
        #region Per-Scheme Config

        private struct SchemeConfig
        {
            internal int MaxTurns;
            internal int RiskSum;
        }

        // Balance reference:
        //   PRESS avg = +5/turn, ADVANCE avg = +2/turn (before conditions).
        //   Field conditions avg = 0 (uniform -5 to +5).
        //   "All ADVANCE" total is always below riskSum → forces some PRESS turns.
        //   "All PRESS" expected total is near or over riskSum, but variance can bust.
        private static SchemeConfig GetConfig(SchemeType type)
        {
            switch (type)
            {
                // 3-turn schemes — short, forgiving risk window.
                // All ADVANCE avg = 6 < riskSum. Must use at least 1-2 PRESS turns.
                case SchemeType.SpreadRumors:
                case SchemeType.FalseAccusations:
                    return new SchemeConfig { MaxTurns = 3, RiskSum = 9 };

                case SchemeType.SpreadTerror:
                case SchemeType.BurnStorage:
                case SchemeType.BribeSoldiers:
                    return new SchemeConfig { MaxTurns = 3, RiskSum = 11 };

                // 4-turn schemes — medium length, tighter margins.
                // All ADVANCE avg = 8 < riskSum.
                case SchemeType.PoisonWell:
                case SchemeType.ForgeDocuments:
                    return new SchemeConfig { MaxTurns = 4, RiskSum = 13 };

                case SchemeType.VipersCounsel:
                    return new SchemeConfig { MaxTurns = 4, RiskSum = 13 };

                case SchemeType.ScatterWolves:
                    return new SchemeConfig { MaxTurns = 4, RiskSum = 15 };

                // 5-turn schemes — long, narrow margin before bust.
                // All PRESS avg = 25 >> bust at 21; mixed strategy is mandatory.
                // All ADVANCE avg = 10 < riskSum.
                case SchemeType.HireAssassin:
                    return new SchemeConfig { MaxTurns = 5, RiskSum = 16 };

                case SchemeType.StageCoup:
                    return new SchemeConfig { MaxTurns = 5, RiskSum = 18 };

                case SchemeType.Assassinate:
                    return new SchemeConfig { MaxTurns = 5, RiskSum = 19 };

                // 3-turn trade scheme — easier target, lower bust pressure.
                case SchemeType.TradeInShadows:
                    return new SchemeConfig { MaxTurns = 3, RiskSum = 8 };

                default:
                    return new SchemeConfig { MaxTurns = 4, RiskSum = 12 };
            }
        }

        #endregion

        // =====================================================================
        // ENTRY POINT
        // Called by SchemeCampaignBehavior.CommitScheme after costs are deducted
        // and per-target / global cooldowns have been stamped.
        // =====================================================================
        #region Entry Point

        internal static void BeginOperation(SchemeDefinition def, Hero targetHero, Settlement targetSett)
        {
            try
            {
                _def        = def;
                _targetHero = targetHero;
                _targetSett = targetSett;
                _targetName = targetHero?.Name?.ToString()
                           ?? targetSett?.Name?.ToString()
                           ?? "the target";

                var cfg    = GetConfig(def.Type);
                _turn      = 1;
                _maxTurns  = cfg.MaxTurns;
                _riskSum   = cfg.RiskSum;
                _score     = 0;

                // Ability availability — based on player's relevant skill level
                int roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0;
                int charm   = Hero.MainHero?.GetSkillValue(DefaultSkills.Charm)   ?? 0;
                _slipPastAvail     = roguery >= 150;
                _scoutAheadAvail   = roguery >= 300;
                _smoothItOverAvail = charm   >= 150;
                _silverTongueAvail = charm   >= 300;

                _conditionsRevealed = false;
                _revealedConditions = 0;
                _slipPastThisTurn   = false;
                _smoothThisTurn     = false;
                _silverThisTurn     = false;

                _events     = BuildEventPool(def.Type, _targetName);
                _eventIndex = 0;

                ShowTurn();
            }
            catch { }
        }

        #endregion

        // =====================================================================
        // TURN DISPLAY
        // Field conditions are rolled at the start of each ShowTurn call so the
        // SCOUT AHEAD ability can reveal them before the player acts.
        // =====================================================================
        #region Turn Display

        private static void ShowTurn()
        {
            try
            {
                // Roll field conditions for this turn (or use the SCOUT AHEAD revealed value)
                if (_conditionsRevealed)
                {
                    _thisConditions     = _revealedConditions;
                    _conditionsRevealed = false;
                }
                else
                {
                    _thisConditions = RollConditions(Hero.MainHero, _def);
                }

                string eventText = DrawNextEvent();
                string bar       = BuildStatusBar(_score, _riskSum);
                int    floor     = GetConditionsFloor(Hero.MainHero, _def);

                // Describe the conditions range so players understand skill's effect
                string conditionsDesc = floor == 0    ? "Field conditions: 0 to +5 (skill: always favourable)"
                                      : floor == -3   ? "Field conditions: −3 to +5 (skill: reduced downside)"
                                      :                 "Field conditions: −5 to +5 (standard)";

                // Show the revealed conditions value if SCOUT AHEAD was used
                string revealedLine = "";
                if (_conditionsRevealed == false && _thisConditions != int.MinValue)
                {
                    // Only show conditions if SCOUT AHEAD revealed them this turn
                    // (_conditionsRevealed was already reset above, so check if we had set them via scout)
                }

                string abilityHints = BuildAbilityHints();
                string header       = $"Field Report — Turn {_turn}/{_maxTurns} [{_def?.Name ?? "?"}]";

                string body =
                    $"A courier from the field:\n\n"
                    + $"\"{eventText}\"\n\n"
                    + $"Progress: {bar}\n"
                    + $"Target: {_targetName}   {conditionsDesc}\n\n"
                    + abilityHints
                    + "\nChoose how your agent proceeds:";

                // Build choices
                var elements = new List<InquiryElement>();

                // Core action choices
                elements.Add(new InquiryElement(
                    "press",
                    $"PRESS FORWARD   [+{PressBase} base progress]",
                    null, true,
                    $"Aggressive approach. High progress, higher bust risk. Net = {PressBase} + field conditions."));

                elements.Add(new InquiryElement(
                    "advance",
                    $"ADVANCE CAREFULLY   [+{AdvanceBase} base progress]",
                    null, true,
                    $"Balanced approach. Reliable mid-speed progress. Net = {AdvanceBase} + field conditions."));

                elements.Add(new InquiryElement(
                    "hold",
                    $"HOLD POSITION   [+{HoldBase} base progress]",
                    null, true,
                    $"Minimal exposure this turn. Best used with an ability. Net = {HoldBase} + field conditions."));

                // Abilities (shown when available)
                if (_slipPastAvail)
                    elements.Add(new InquiryElement(
                        "slippast",
                        "SLIP PAST — Skip field conditions entirely. [pure action base, no random]",
                        null, true,
                        "One-use. Choose an action; field conditions are bypassed. Combine with PRESS for guaranteed +5."));

                if (_smoothItOverAvail)
                    elements.Add(new InquiryElement(
                        "smoothitover",
                        "SMOOTH IT OVER — +3 bonus to field conditions this turn.",
                        null, true,
                        "One-use. Combine with any action. Shifts the conditions result +3 before it's applied."));

                if (_silverTongueAvail)
                    elements.Add(new InquiryElement(
                        "silvertongue",
                        "SILVER TONGUE — Field conditions become +5 (maximum) this turn.",
                        null, true,
                        "One-use. Guarantees optimal field conditions. Best paired with PRESS FORWARD."));

                if (_scoutAheadAvail)
                    elements.Add(new InquiryElement(
                        "scoutahead",
                        "SCOUT AHEAD — Reveal this turn's field conditions before choosing your action.",
                        null, true,
                        "One-use. Returns to this screen with conditions visible. No progress used."));

                elements.Add(new InquiryElement(
                    "abort",
                    "ABORT — Pull the agent back. Operation abandoned.",
                    null, true,
                    "2-day global cooldown. No exposure, no consequences. Costs already spent."));

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        header, body, elements,
                        true, 1, 1,
                        "Confirm", null,
                        ProcessChoice, null),
                    true);
            }
            catch { }
        }

        #endregion

        // =====================================================================
        // CHOICE PROCESSING
        // =====================================================================
        #region Choice Processing

        private static void ProcessChoice(List<InquiryElement> chosen)
        {
            try
            {
                if (chosen == null || chosen.Count == 0) return;
                string action = chosen[0].Identifier as string ?? "advance";

                switch (action)
                {
                    case "abort":
                        FinishOperation(SchemeOutcome.SmallLoss, "abort");
                        return;

                    case "scoutahead":
                        // Reveal this turn's conditions and return to the same turn
                        _scoutAheadAvail    = false;
                        _conditionsRevealed = true;
                        _revealedConditions = _thisConditions; // already rolled in ShowTurn
                        ShowScoutScreen(_thisConditions);
                        return;

                    case "slippast":
                        _slipPastAvail    = false;
                        _slipPastThisTurn = true;   // ExecuteAction will bypass conditions
                        ShowAbilityFollowUp("slippast");
                        return;

                    case "smoothitover":
                        _smoothItOverAvail = false;
                        _smoothThisTurn    = true;
                        ShowAbilityFollowUp("smoothitover");
                        return;

                    case "silvertongue":
                        _silverTongueAvail = false;
                        _silverThisTurn    = true;
                        ShowAbilityFollowUp("silvertongue");
                        return;

                    case "press":
                        ExecuteAction(PressBase);
                        return;

                    case "advance":
                        ExecuteAction(AdvanceBase);
                        return;

                    case "hold":
                        ExecuteAction(HoldBase);
                        return;
                }
            }
            catch { }
        }

        // Abilities that modify the action require a second selection (which action to use).
        private static void ShowAbilityFollowUp(string abilityKey)
        {
            string abilityName = abilityKey == "slippast"    ? "SLIP PAST"
                               : abilityKey == "smoothitover"? "SMOOTH IT OVER"
                               : "SILVER TONGUE";

            string desc = abilityKey == "slippast"
                ? "Field conditions bypassed. Your progress = action base only."
                : abilityKey == "smoothitover"
                ? "Field conditions shifted +3 this turn."
                : "Field conditions set to +5 this turn.";

            var elements = new List<InquiryElement>
            {
                new InquiryElement("press",
                    $"PRESS FORWARD   [{(abilityKey == "slippast" ? "guaranteed +" + PressBase : "+" + PressBase + " + modified conditions")}]",
                    null, true, "Aggressive action with ability modifier."),
                new InquiryElement("advance",
                    $"ADVANCE CAREFULLY   [{(abilityKey == "slippast" ? "guaranteed +" + AdvanceBase : "+" + AdvanceBase + " + modified conditions")}]",
                    null, true, "Balanced action with ability modifier."),
                new InquiryElement("hold",
                    $"HOLD POSITION   [{(abilityKey == "slippast" ? "guaranteed +" + HoldBase : HoldBase + " + modified conditions")}]",
                    null, true, "Minimal approach with ability modifier."),
            };

            MBInformationManager.ShowMultiSelectionInquiry(
                new MultiSelectionInquiryData(
                    $"{abilityName} — Choose Action",
                    $"Ability: {abilityName}\n{desc}\n\nNow choose your action:",
                    elements, true, 1, 1,
                    "Confirm", null,
                    OnAbilityActionChosen, null),
                true);
        }

        private static void OnAbilityActionChosen(List<InquiryElement> chosen)
        {
            try
            {
                if (chosen == null || chosen.Count == 0) return;
                int baseVal = chosen[0].Identifier as string == "press"   ? PressBase
                            : chosen[0].Identifier as string == "advance" ? AdvanceBase
                            : HoldBase;
                ExecuteAction(baseVal);
            }
            catch { }
        }

        #endregion

        // =====================================================================
        // ACTION EXECUTION
        // Applies field conditions (modified by any active ability flags),
        // computes net progress (floored at 0), updates score, shows result.
        // =====================================================================
        #region Action Execution

        private static void ExecuteAction(int actionBase)
        {
            int    conditions;
            string condNote;

            if (_slipPastThisTurn)
            {
                // SLIP PAST: no field conditions applied — pure action base guaranteed
                conditions        = 0;
                condNote          = "SLIP PAST — field conditions bypassed.";
                _slipPastThisTurn = false;
            }
            else if (_silverThisTurn)
            {
                conditions      = 5;
                condNote        = "SILVER TONGUE — field conditions: +5 (maximum).";
                _silverThisTurn = false;
            }
            else if (_smoothThisTurn)
            {
                int raw  = _thisConditions;
                conditions      = Math.Min(5, raw + 3);
                condNote        = $"SMOOTH IT OVER — conditions: {raw:+#;-#;0} → {conditions:+#;-#;0}.";
                _smoothThisTurn = false;
            }
            else
            {
                conditions = _thisConditions;
                condNote   = "";
            }

            // Net progress floored at 0 — bad luck wastes a turn, never reverses score
            int net = Math.Max(0, actionBase + conditions);

            string actionName = actionBase == PressBase   ? "PRESS FORWARD"
                              : actionBase == AdvanceBase ? "ADVANCE CAREFULLY"
                              : "HOLD POSITION";

            string condLine = string.IsNullOrEmpty(condNote)
                ? $"Field conditions: {conditions:+#;-#;0} ({DescribeConditions(conditions)})   Net: +{net}"
                : $"{condNote}   Net: +{net}";

            ShowActionResult(actionName, net, condLine);
        }

        #endregion

        // =====================================================================
        // RESULT DISPLAY
        // Shows what happened this turn, updates score, then offers Continue/Abort.
        // Immediate resolution (success / bust) fires FinishOperation directly.
        // =====================================================================
        #region Result Display

        private static void ShowActionResult(string actionName, int net, string condLine)
        {
            _score += net;

            // ── Immediate resolution ──────────────────────────────────────────
            if (_score > BustLimit)
            {
                FinishOperation(SchemeOutcome.Bust, "bust");
                return;
            }
            if (_score >= _riskSum)
            {
                FinishOperation(SchemeOutcome.Success, "success");
                return;
            }

            bool isLastTurn = (_turn >= _maxTurns);
            string bar = BuildStatusBar(_score, _riskSum);

            var sb = new StringBuilder();
            sb.AppendLine($"[{actionName}]");
            sb.AppendLine(condLine);
            sb.AppendLine();
            sb.AppendLine($"Progress: {bar}");
            sb.AppendLine($"Turns remaining: {_maxTurns - _turn}");

            if (isLastTurn)
                sb.AppendLine("\nFinal report. Your agent withdraws. The objective was not reached.");

            _turn++;
            string resultText  = sb.ToString();
            string resultTitle = $"Field Report — Turn {_turn - 1}/{_maxTurns} Complete";

            if (isLastTurn)
            {
                InformationManager.ShowInquiry(
                    new InquiryData(resultTitle, resultText,
                        true, false, "Return", null,
                        () => FinishOperation(SchemeOutcome.SmallLoss, "outoftime"), null),
                    true);
            }
            else
            {
                InformationManager.ShowInquiry(
                    new InquiryData(resultTitle, resultText,
                        true, true, "Continue", "Abort Operation",
                        () => ShowTurn(),
                        () => FinishOperation(SchemeOutcome.SmallLoss, "abort")),
                    true);
            }
        }

        // Shown when SCOUT AHEAD is used — reveals conditions, returns to same turn.
        private static void ShowScoutScreen(int conditions)
        {
            string condDesc = DescribeConditions(conditions);
            string body =
                $"Your agent scouted this turn's approach before committing.\n\n"
                + $"Field conditions this turn: {conditions:+#;-#;0} ({condDesc})\n\n"
                + "SCOUT AHEAD expended for this operation.\n"
                + "Return to choose your action with conditions revealed.";

            InformationManager.ShowInquiry(
                new InquiryData("Scout Ahead — Conditions Revealed", body,
                    true, false, "Return to Turn", null,
                    () =>
                    {
                        // Conditions already stored in _thisConditions; just show turn again
                        // with _conditionsRevealed set so ShowTurn uses the stored value.
                        _conditionsRevealed = true;
                        _revealedConditions = conditions;
                        ShowTurn();
                    }, null),
                true);
        }

        #endregion

        // =====================================================================
        // OPERATION RESOLUTION
        // Final outcome screen. Delegates game-state changes to SchemeSystem
        // via a callback so the inquiry is fully closed before effects fire.
        // =====================================================================
        #region Operation Resolution

        private static void FinishOperation(SchemeOutcome outcome, string reason)
        {
            try
            {
                string title, body;

                switch (outcome)
                {
                    case SchemeOutcome.Success:
                        title = "Operation Complete — SUCCESS";
                        body  = $"Your agent reached the objective against {_targetName}.\n\n"
                              + $"Final progress: {_score} / {_riskSum} — objective reached.\n\n"
                              + "The scheme takes effect.";
                        break;

                    case SchemeOutcome.Bust:
                        title = "Operation Blown — AGENT CAPTURED";
                        body  = $"Progress exceeded the safe limit. Your agent was seized near {_targetName}.\n\n"
                              + $"Final progress: {_score} / {BustLimit} — BUSTED.\n\n"
                              + "Crime rating rises. Relations collapse. The agent is gone.";
                        break;

                    default: // SmallLoss
                        title = "Operation Aborted";
                        body  = reason == "outoftime"
                              ? $"Your agent exhausted every opening near {_targetName} and withdrew quietly.\n\nNo exposure. The coin is spent."
                              : $"Your agent pulled back from {_targetName} on your order.\n\nNo exposure. The coin is spent.";
                        break;
                }

                // Set global cooldown: 2 days on abort, 3 days otherwise
                int cd = (outcome == SchemeOutcome.SmallLoss && reason == "abort") ? 2 : 3;
                try { SchemeSystem.SetPlayerCooldown(cd); } catch { }

                SchemeOutcome    capturedOutcome = outcome;
                SchemeDefinition capturedDef     = _def;
                Hero             capturedHero    = _targetHero;
                Settlement       capturedSett    = _targetSett;

                InformationManager.ShowInquiry(
                    new InquiryData(title, body, true, false, "Close", null,
                        () =>
                        {
                            try
                            {
                                SchemeSystem.ApplyPlayerSchemeOutcome(
                                    capturedOutcome,
                                    capturedDef?.Type ?? SchemeType.Assassinate,
                                    capturedHero, capturedSett);
                            }
                            catch { }
                        }, null),
                    true);
            }
            catch { }
        }

        #endregion

        // =====================================================================
        // FIELD CONDITIONS
        // =====================================================================
        #region Field Conditions

        // Returns the floor value for field conditions based on relevant skill.
        private static int GetConditionsFloor(Hero hero, SchemeDefinition def)
        {
            if (hero == null || def == null) return -5;
            try
            {
                int skill = hero.GetSkillValue(def.Skill);
                if (skill >= 300) return 0;
                if (skill >= 150) return -3;
            }
            catch { }
            return -5;
        }

        // Rolls field conditions for one turn. Range is [-5, +5] but floor is
        // raised by skill level: 150+ → -3, 300+ → 0.
        private static int RollConditions(Hero hero, SchemeDefinition def)
        {
            int floor = GetConditionsFloor(hero, def);
            int range = 5 - floor + 1;           // e.g. floor=-3: range = 5-(-3)+1 = 9, gives -3..+5
            return floor + _rng.Next(range);      // uniform over [floor, +5]
        }

        private static string DescribeConditions(int val)
        {
            if (val >= 4)  return "excellent — ideal timing";
            if (val >= 2)  return "favourable";
            if (val >= 0)  return "neutral";
            if (val >= -2) return "unfavourable";
            return "terrible — bad timing";
        }

        #endregion

        // =====================================================================
        // HELPERS
        // =====================================================================
        #region Helpers

        // [========----········] 8/21  (need 12 to succeed)
        // = filled (within riskSum zone), · danger zone (riskSum < pos ≤ 21)
        private static string BuildStatusBar(int score, int riskSum)
        {
            const int Width = 20;
            var sb = new StringBuilder("[");
            for (int i = 1; i <= Width; i++)
            {
                float threshold = i / (float)Width * BustLimit;
                if      (threshold <= score)   sb.Append('=');
                else if (threshold <= riskSum) sb.Append('-');
                else                           sb.Append('·');
            }
            sb.Append("] ");
            sb.Append(score);
            sb.Append('/');
            sb.Append(BustLimit);
            sb.Append("  (need ");
            sb.Append(riskSum);
            sb.Append(" to succeed)");
            return sb.ToString();
        }

        private static string BuildAbilityHints()
        {
            int floor = GetConditionsFloor(Hero.MainHero, _def);

            var sb = new StringBuilder();
            // Always show passive skill effect
            string passive = floor == 0  ? "(Skill: conditions always ≥ 0 this operation)"
                           : floor == -3 ? "(Skill: conditions ≥ −3 this operation)"
                           :               "(Skill: full −5 to +5 conditions range)";
            sb.AppendLine(passive);

            bool any = _slipPastAvail || _scoutAheadAvail || _smoothItOverAvail || _silverTongueAvail;
            if (any)
            {
                sb.AppendLine("One-use abilities available:");
                if (_slipPastAvail)     sb.AppendLine("  [Roguery] SLIP PAST — bypass field conditions");
                if (_scoutAheadAvail)   sb.AppendLine("  [Roguery] SCOUT AHEAD — reveal conditions before acting");
                if (_smoothItOverAvail) sb.AppendLine("  [Charm]   SMOOTH IT OVER — +3 to conditions");
                if (_silverTongueAvail) sb.AppendLine("  [Charm]   SILVER TONGUE — conditions become +5");
            }
            return sb.ToString();
        }

        private static string DrawNextEvent()
        {
            if (_events == null || _events.Count == 0)
                return "Your agent signals readiness.";
            string e = _events[_eventIndex % _events.Count];
            _eventIndex++;
            return e;
        }

        #endregion

        // =====================================================================
        // EVENT POOLS
        // Eight field-report strings per scheme type. {0} = target name.
        // Events provide flavour/context; they do not determine outcomes
        // (that is done by the action + field conditions mechanic).
        // =====================================================================
        #region Event Pools

        private static List<string> BuildEventPool(SchemeType type, string targetName)
        {
            string[] raw  = GetRawEvents(type);
            var      pool = raw.Select(e => string.Format(e, targetName)).ToList();
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                string tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp;
            }
            return pool;
        }

        private static string[] GetRawEvents(SchemeType type)
        {
            switch (type)
            {
                case SchemeType.Assassinate:
                    return new[]
                    {
                        "Guard rotations near {0}'s tent have been mapped. A midnight gap has been confirmed.",
                        "Your agent infiltrated {0}'s escort posing as a merchant's aide. The daily routine is known.",
                        "A serving woman inside {0}'s hall passed the weekly schedule to your contact at the market.",
                        "{0} dined alone this evening — a rare gap in the usual crowd, noted by your man.",
                        "City watch near {0}'s quarters doubled after a rumoured threat reached the garrison.",
                        "{0}'s steward proved incorruptible. The bribe attempt nearly exposed your agent's purpose.",
                        "A loyalist household guard questioned your agent's story near the postern gate.",
                        "The blade handler went silent for a full day. Your agent suspects he was followed from {0}'s camp.",
                    };

                case SchemeType.SpreadTerror:
                    return new[]
                    {
                        "Market brawls in {0} are escalating on their own. Timing is right to fan the flames.",
                        "Your agent bribed a thug gang to keep tension burning near {0}'s gates after dark.",
                        "Rumours of bandit incursions near {0} are spreading through the merchant quarter unaided.",
                        "The city watch in {0} is stretched thin — patrols are sparse along the southern docks.",
                        "A merchant guild in {0} threatened a public strike. Your agent nudged talks toward collapse.",
                        "One of your contacts in {0} was pulled aside for questioning by a guardsman.",
                        "Watch officers in {0} tightened night inspections after an incident near the harbour.",
                        "Citizens of {0} have grown wary of unfamiliar faces. Your agent's cover is thin.",
                    };

                case SchemeType.PoisonWell:
                    return new[]
                    {
                        "The water source for {0}'s barracks has been found. Access is possible during the watch change.",
                        "A sewer worker near {0}'s garrison took coin and asked no questions.",
                        "Your agent confirmed the garrison at {0} draws from the eastern well — unguarded before dawn.",
                        "Night patrol routes near {0}'s well have a gap of nearly an hour between circuits.",
                        "An unexpected militia exercise brought extra guards directly to {0}'s water stores.",
                        "A guard recognised your agent near {0}'s postern gate and raised a brief alarm.",
                        "The eastern well at {0} was sealed temporarily after a supply irregularity report.",
                        "Garrison officers at {0} tightened access following an unexplained missing-stock report.",
                    };

                case SchemeType.StageCoup:
                    return new[]
                    {
                        "Three garrison officers at {0} accepted a preliminary offer — the negotiation proceeds well.",
                        "The constable of {0} expressed sympathy for the cause over wine at a private supper.",
                        "Your agent mapped the shift schedules of {0}'s key officers. The patterns are confirmed.",
                        "Resentment toward the current lord runs high in {0}'s garrison after missed pay cycles.",
                        "A loyalist captain at {0} rejected the coin outright and summoned a senior officer.",
                        "One of the bought officers at {0} is demanding considerably more to stay bought.",
                        "The castellan of {0} dispatched investigators after a bribery rumour reached his office.",
                        "City guard at {0} arrested a go-between. Your chain of contacts is compromised.",
                    };

                case SchemeType.SpreadRumors:
                    return new[]
                    {
                        "Whispers about corruption in {0}'s council are already circulating without help.",
                        "A popular tavern in {0} proved fertile ground — the stories spread remarkably fast.",
                        "Your agent planted doubts about {0}'s last tax collector. Locals are incensed.",
                        "Merchants passing through {0} are now carrying your rumours further north.",
                        "A local priest at {0} addressed the rumours from the pulpit — damaging the effect.",
                        "A sharp-eyed notary at {0} began quietly investigating the origin of the stories.",
                        "One of your contacts in {0} was seen speaking at length with a garrison officer.",
                        "Citizens of {0} have grown wary of unfamiliar travellers bearing gossip.",
                    };

                case SchemeType.BurnStorage:
                    return new[]
                    {
                        "Your agent confirmed the warehouse layout in {0}. Entry points are mapped.",
                        "Night shift guards at {0}'s granary rotate predictably — the window is confirmed.",
                        "A disgruntled dockworker at {0} agreed to look the other way during a night shift.",
                        "Dry weather in {0} this week makes the storage district especially vulnerable.",
                        "A fire inspector toured {0}'s warehouses this morning — increased scrutiny expected.",
                        "An alert guard at {0}'s granary discovered a torch cache left by your contact.",
                        "City watch in {0} increased patrols near warehouses after a fire in a nearby town.",
                        "Your agent's contact at {0} went dark after being questioned at the main gate.",
                    };

                case SchemeType.BribeSoldiers:
                    return new[]
                    {
                        "Three soldiers in {0}'s garrison expressed genuine interest in a more generous arrangement.",
                        "Your agent confirmed low morale in {0}'s garrison after a missed pay cycle.",
                        "The garrison sergeant at {0} is known to be deeply in debt — useful leverage.",
                        "Pay disputes at {0} have left a dozen soldiers ready to walk for the right offer.",
                        "A loyalist captain at {0} grew suspicious of your agent's contacts in the barracks.",
                        "One soldier at {0} confessed the approach to his sergeant for a promised promotion.",
                        "A garrison sweep at {0} uncovered a coin cache left by your contacts.",
                        "The lord of {0} raised garrison pay by decree this week — your leverage weakened.",
                    };

                case SchemeType.ForgeDocuments:
                    return new[]
                    {
                        "Your agent secured a wax impression of {0}'s personal seal from a bribed courier.",
                        "A skilled scrivener agreed to replicate {0}'s official letterhead for gold.",
                        "Your agent intercepted a genuine dispatch from {0} to study the handwriting.",
                        "The fabricated letters implicating {0} are nearly indistinguishable from authentic ones.",
                        "A court official grew suspicious of letters bearing {0}'s seal on unusual routes.",
                        "One of the hired couriers grew nervous and is demanding considerably more gold.",
                        "A royal archivist flagged inconsistencies in a recent letter attributed to {0}.",
                        "Your court contact reports heightened scrutiny of all correspondence from {0}.",
                    };

                case SchemeType.HireAssassin:
                    return new[]
                    {
                        "Your agent located a capable blade willing to target {0}'s escort for the agreed price.",
                        "The hired blade spent three days studying {0}'s party route and daily habits.",
                        "Your agent confirmed {0}'s patrol route passes through an isolated stretch at dusk.",
                        "The hired blade reports {0}'s escort is smaller than expected — good timing.",
                        "A rival faction's scouts are watching {0}'s camp. The blade spotted them and held off.",
                        "The assassin grew cautious after {0}'s guard was doubled without apparent reason.",
                        "Your contact reports that {0} received a private tip about a possible ambush.",
                        "The hired blade went quiet after a thorough inspection at {0}'s camp perimeter.",
                    };

                case SchemeType.FalseAccusations:
                    return new[]
                    {
                        "Your agent planted evidence of {0}'s dealings with a rival clan at the king's court.",
                        "A gossiping noble eagerly repeated your agent's insinuations about {0} to the right ears.",
                        "Your agent confirmed that {0}'s rivals at court will believe almost any accusation.",
                        "Whispers against {0} have already reached the king's inner circle without prompting.",
                        "A loyal ally of {0} began quietly rebutting the accusations in private meetings.",
                        "A court scribe grew curious about the origin of the accusations circulating about {0}.",
                        "One of your planted witnesses recanted under pressure from {0}'s clan.",
                        "{0}'s standing at court proved more resilient than expected — the smear isn't sticking.",
                    };

                case SchemeType.VipersCounsel:
                    return new[]
                    {
                        "Your agent secured a word with the king's chancellor regarding {0}'s recent conduct.",
                        "Doubts about {0}'s loyalty were already circulating at court — your agent amplified them.",
                        "Your agent arranged for fabricated correspondence implicating {0} to reach the king.",
                        "The king listened attentively when {0}'s past missteps were raised.",
                        "A close confidant of {0} sits near the king every day — your agent must tread carefully.",
                        "The king expressed scepticism about the allegations against {0} after a private meeting.",
                        "A rival lord spoke up in {0}'s defence before the king, buying them unexpected goodwill.",
                        "{0}'s supporters at court outmanoeuvred your contacts in the chancellor's circle.",
                    };

                case SchemeType.ScatterWolves:
                    return new[]
                    {
                        "Your agent contacted a bandit chieftain willing to raid {0}'s kingdom's roads this season.",
                        "Deserters near {0}'s territory are ready to be hired and pointed at the right roads.",
                        "Your agent confirmed the roads through {0}'s kingdom are lightly patrolled this season.",
                        "Three bandit bands have been paid and assigned territories throughout {0}'s realm.",
                        "A patrol from {0}'s kingdom stumbled on your agent meeting with a bandit leader.",
                        "One of the hired chieftains sold the plan to {0}'s lords for a pardon.",
                        "Your courier was stopped and searched at a border checkpoint near {0}'s kingdom.",
                        "The lords of {0}'s kingdom assembled a hunting party — some bands may scatter.",
                    };

                case SchemeType.TradeInShadows:
                    return new[]
                    {
                        "Your agent made contact with {0}'s underworld network. Initial terms are promising.",
                        "A fence in {0} agreed to move the goods quietly — the merchant quarter is receptive.",
                        "Your agent confirmed the patrol schedule near {0}'s warehouse district. The gap holds.",
                        "Underground buyers in {0} are paying well this season — demand is high.",
                        "A city official at {0} noticed unusual cargo movements and is asking questions.",
                        "The fence at {0} grew nervous after a customs inspection — renegotiating the cut.",
                        "A competing smuggler at {0} is undercutting your agent's contacts. Prices dropped.",
                        "City guard at {0} swept the docks after an unrelated theft. Timing is awkward.",
                    };

                default:
                    return new[]
                    {
                        "Your agent signals readiness near {0}. The approach is being assessed.",
                        "Contacts near {0} have been established. The operation is underway.",
                        "Initial surveillance of {0} is complete. Patterns have been identified.",
                        "Your agent reports conditions near {0} are within expected parameters.",
                        "A complication arose near {0}. Your agent is adapting the approach.",
                        "Security near {0} increased briefly — your agent held position.",
                        "A contact near {0} went silent for a day. Communication restored.",
                        "Conditions near {0} shifted unexpectedly. Your agent is reassessing.",
                    };
            }
        }

        #endregion
    }
}
