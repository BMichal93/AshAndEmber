// =============================================================================
// ASH AND EMBER — Schemes/SchemeMinigame.cs
// Push-your-luck interactive minigame for player scheme operations.
//
// DESIGN OVERVIEW
// ───────────────
// When the player commits a scheme, they play a field-report minigame instead
// of waiting 1–3 days for an RNG result. Each turn an agent field report
// arrives; the player chooses how to respond. A skill check resolves it:
//
//   PASS  → score +passPts   (small, safe)
//   FAIL  → score +failPts   (large, risky — heat climbs toward bust)
//
//   GOAL : reach score ≥ riskSum without busting
//          → SUCCESS   (full scheme effects applied)
//   BUST : score > 21
//          → BUST      (agent caught, heavy consequences)
//   ABORT: player retreats, or runs out of turns without reaching riskSum
//          → SMALL LOSS (agent fled cleanly, no consequences, coin lost)
//
// PASS CHANCE FORMULA
// ────────────────────
//   passChance = 30% + (skillLevel / 600) × 55%
//   Clamped to [30%, 85%].
//   Uses the scheme's relevant skill (Roguery or Charm, per SchemeDefinition.Skill).
//
// ONE-USE SKILL ABILITIES (reset each operation)
// ────────────────────────────────────────────────
//   Roguery 150+   SLIP PAST       Skip this event entirely (0 pts, no risk)
//   Roguery 300+   SCOUT AHEAD     Preview the NEXT event before deciding this turn
//   Charm   150+   SMOOTH IT OVER  Reduce fail penalty by 3 pts for this event
//   Charm   300+   SILVER TONGUE   Guarantee a pass (bypass the skill check)
//
// UI FLOW
// ───────
//   BeginOperation()
//     └─► ShowTurn()          [MultiSelectionInquiry — event text + choices]
//           ├─► EXECUTE / ability → ResolveSkillCheck() → ShowEventResult()
//           │     ├─► "Continue" → ShowTurn()
//           │     └─► "Abort"    → FinishOperation(SmallLoss)
//           ├─► SCOUT AHEAD → ShowScoutAheadScreen() → ShowTurn() (with peeked text)
//           └─► ABORT → FinishOperation(SmallLoss)
//
//   FinishOperation(outcome)  [InquiryData — final result]
//     └─► callback: SchemeSystem.ApplyPlayerSchemeOutcome()
//
// All UI text is framed from the commander's perspective (receiving field
// reports from an agent on the ground). {0} in event strings = target name.
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
    // ── Outcome enum ──────────────────────────────────────────────────────────
    internal enum SchemeOutcome
    {
        SmallLoss,  // Agent retreated cleanly — no consequences (abort or out of turns)
        Success,    // Score reached riskSum — full scheme effects applied
        Bust,       // Score exceeded 21 — agent caught, full exposure consequences
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SchemeMinigame
    //
    // Static class — only one player operation can be active at a time because
    // Bannerlord processes inquiries sequentially. All state fields represent
    // the single live operation. They are fully reset by BeginOperation().
    // ─────────────────────────────────────────────────────────────────────────
    internal static class SchemeMinigame
    {
        // =====================================================================
        // OPERATION STATE
        // Fields persist across async UI callbacks (MultiSelectionInquiry and
        // InquiryData both invoke their callbacks after the inquiry closes).
        // =====================================================================
        #region Operation State

        private static SchemeDefinition _def;          // active scheme definition
        private static Hero             _targetHero;   // lord target (or null for settlements)
        private static Settlement       _targetSett;   // settlement target (or null for lords)
        private static string           _targetName;   // cached display string

        private static int _turn;       // current turn, 1-based
        private static int _maxTurns;   // total turns for this scheme
        private static int _score;      // accumulated heat (bust threshold = 21)
        private static int _riskSum;    // score needed for success
        private static int _passPts;    // pts added on a skill-check pass
        private static int _failPts;    // pts added on a skill-check fail (before modifiers)

        // One-use ability flags — true = still available this operation
        private static bool _slipPastAvail;      // Roguery 150+
        private static bool _scoutAheadAvail;    // Roguery 300+
        private static bool _smoothItOverAvail;  // Charm   150+
        private static bool _silverTongueAvail;  // Charm   300+

        // SCOUT AHEAD peek: stores the text for the NEXT event so ShowTurn()
        // can display it immediately instead of drawing a new card.
        private static bool   _hasPeekedNext;
        private static string _peekedEventText;

        // Shuffled event pool for this operation (8 strings per scheme type,
        // formatted with target name). _eventIndex cycles through them.
        private static List<string> _events;
        private static int          _eventIndex;

        private static readonly Random _rng = new Random();

        // Bust hard limit — all schemes share this ceiling
        private const int BustLimit = 21;

        #endregion

        // =====================================================================
        // PER-SCHEME CONFIGURATION
        // Inline struct — one GetConfig() call per BeginOperation.
        // =====================================================================
        #region Per-Scheme Config

        private struct SchemeConfig
        {
            internal int MaxTurns;
            internal int RiskSum;  // score needed for success (< BustLimit)
            internal int PassPts;  // pts on pass (small)
            internal int FailPts;  // pts on fail (large, risky)
        }

        // Balance rationale:
        //   - Soft intel schemes (SpreadRumors, FalseAccusations): short, forgiving —
        //     high pass pts / low fail pts let skilled players finish in few turns.
        //   - Medium schemes: 3 turns, moderate risk. Fail pts climb noticeably.
        //   - Hard schemes (Assassinate, StageCoup): 5 turns, tight margins —
        //     one or two fails near the end can push past 21.
        private static SchemeConfig GetConfig(SchemeType type)
        {
            switch (type)
            {
                case SchemeType.SpreadRumors:
                case SchemeType.FalseAccusations:
                    return new SchemeConfig { MaxTurns = 3, RiskSum = 7,  PassPts = 1, FailPts = 4 };

                case SchemeType.SpreadTerror:
                case SchemeType.BurnStorage:
                case SchemeType.BribeSoldiers:
                    return new SchemeConfig { MaxTurns = 3, RiskSum = 9,  PassPts = 2, FailPts = 5 };

                case SchemeType.PoisonWell:
                case SchemeType.ForgeDocuments:
                    return new SchemeConfig { MaxTurns = 4, RiskSum = 12, PassPts = 2, FailPts = 5 };

                case SchemeType.VipersCounsel:
                    return new SchemeConfig { MaxTurns = 4, RiskSum = 12, PassPts = 2, FailPts = 6 };

                case SchemeType.ScatterWolves:
                    return new SchemeConfig { MaxTurns = 4, RiskSum = 13, PassPts = 2, FailPts = 6 };

                case SchemeType.HireAssassin:
                    return new SchemeConfig { MaxTurns = 5, RiskSum = 15, PassPts = 3, FailPts = 7 };

                case SchemeType.StageCoup:
                    return new SchemeConfig { MaxTurns = 5, RiskSum = 16, PassPts = 3, FailPts = 7 };

                case SchemeType.Assassinate:
                default:
                    return new SchemeConfig { MaxTurns = 5, RiskSum = 16, PassPts = 3, FailPts = 8 };
            }
        }

        #endregion

        // =====================================================================
        // ENTRY POINT
        // Called by SchemeCampaignBehavior.CommitScheme AFTER:
        //   - Gold and influence costs have been deducted
        //   - Per-target and global cooldowns have been stamped
        //   - Trait penalties have been applied
        // =====================================================================
        #region Entry Point

        /// <summary>
        /// Initialises all operation state and shows the first turn screen.
        /// </summary>
        internal static void BeginOperation(SchemeDefinition def, Hero targetHero, Settlement targetSett)
        {
            try
            {
                // ── Cache operation parameters ────────────────────────────────
                _def        = def;
                _targetHero = targetHero;
                _targetSett = targetSett;
                _targetName = targetHero?.Name?.ToString()
                           ?? targetSett?.Name?.ToString()
                           ?? "the target";

                // ── Apply per-scheme config ───────────────────────────────────
                var cfg   = GetConfig(def.Type);
                _turn     = 1;
                _maxTurns = cfg.MaxTurns;
                _riskSum  = cfg.RiskSum;
                _score    = 0;
                _passPts  = cfg.PassPts;
                _failPts  = cfg.FailPts;

                // ── Determine ability availability ────────────────────────────
                // Abilities unlock at skill thresholds and are expended once used.
                int roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0;
                int charm   = Hero.MainHero?.GetSkillValue(DefaultSkills.Charm)   ?? 0;
                _slipPastAvail     = roguery >= 150;
                _scoutAheadAvail   = roguery >= 300;
                _smoothItOverAvail = charm   >= 150;
                _silverTongueAvail = charm   >= 300;

                // ── Reset peek state ──────────────────────────────────────────
                _hasPeekedNext   = false;
                _peekedEventText = null;

                // ── Shuffle event pool ────────────────────────────────────────
                // 8 events per scheme type, formatted with the target name,
                // then Fisher-Yates shuffled so draw order is unpredictable.
                _events     = BuildEventPool(def.Type, _targetName);
                _eventIndex = 0;

                ShowTurn();
            }
            catch { }
        }

        #endregion

        // =====================================================================
        // TURN DISPLAY
        // Shows the MultiSelectionInquiry with the current field report and
        // the available choices for this turn.
        // =====================================================================
        #region Turn Display

        private static void ShowTurn()
        {
            try
            {
                // ── Determine event text ──────────────────────────────────────
                // If SCOUT AHEAD was used last turn, use the peeked text.
                string eventText;
                if (_hasPeekedNext && _peekedEventText != null)
                {
                    eventText        = _peekedEventText;
                    _hasPeekedNext   = false;
                    _peekedEventText = null;
                }
                else
                {
                    eventText = DrawNextEvent();
                }

                // ── Pass chance ───────────────────────────────────────────────
                float passChance    = ComputePassChance(Hero.MainHero, _def);
                int   passChancePct = (int)(passChance * 100f);

                // ── Build body text ───────────────────────────────────────────
                string bar          = BuildStatusBar(_score, _riskSum);
                string abilityHints = BuildAbilityHints();

                string body =
                    $"A courier from the field ({_def?.Name ?? "?"}):\n\n"
                    + $"\"{eventText}\"\n\n"
                    + $"Heat: {bar}\n"
                    + $"Target: {_targetName}   Skill check: {passChancePct}% pass chance\n\n"
                    + abilityHints
                    + "\nSelect an action:";

                string header = $"Field Report — Turn {_turn}/{_maxTurns} [{_def?.Name ?? "?"}]";

                // ── Build choice list ─────────────────────────────────────────
                var elements = new List<InquiryElement>();

                // Always available: EXECUTE (core skill check action)
                elements.Add(new InquiryElement(
                    "execute",
                    "EXECUTE — Send the agent forward.",
                    null, true,
                    $"Roll skill check ({passChancePct}%). Pass: +{_passPts} heat. Fail: +{_failPts} heat."));

                // Roguery 150+: SLIP PAST — zero-pts skip (expends ability)
                if (_slipPastAvail)
                    elements.Add(new InquiryElement(
                        "slippast",
                        "SLIP PAST — Your agent vanishes into the shadows. [+0 heat, no risk]",
                        null, true,
                        "One-use. Skips this event entirely. No skill check, no heat gained."));

                // Charm 150+: SMOOTH IT OVER — reduces fail penalty by 3 (expends ability)
                if (_smoothItOverAvail)
                    elements.Add(new InquiryElement(
                        "smoothitover",
                        $"SMOOTH IT OVER — Your agent talks their way around the worst of it. [fail adds only +{Math.Max(1, _failPts - 3)} heat]",
                        null, true,
                        "One-use. Execute with reduced fail penalty. Still rolls a normal skill check."));

                // Charm 300+: SILVER TONGUE — guaranteed pass (expends ability)
                if (_silverTongueAvail)
                    elements.Add(new InquiryElement(
                        "silvertongue",
                        $"SILVER TONGUE — Your agent charms the contact directly. [guaranteed pass, +{_passPts} heat]",
                        null, true,
                        "One-use. Bypasses the skill check — automatically passes this turn."));

                // Roguery 300+: SCOUT AHEAD — peeks next event (expends ability, not on last turn)
                if (_scoutAheadAvail && _turn < _maxTurns)
                    elements.Add(new InquiryElement(
                        "scoutahead",
                        "SCOUT AHEAD — Your agent observes before acting. [preview next event, no heat]",
                        null, true,
                        "One-use. Does not advance the turn. Returns to this screen with next event revealed."));

                // Always available: ABORT (clean retreat, small loss)
                elements.Add(new InquiryElement(
                    "abort",
                    "ABORT — Pull the agent back. The operation is abandoned.",
                    null, true,
                    "Safe retreat. 2-day global cooldown. No exposure, no consequences. Costs already spent."));

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        header, body, elements,
                        true, 1, 1,      // allowMultiple=true required, min=max=1 = radio-select
                        "Confirm", null,
                        ProcessChoice, null),
                    true);
            }
            catch { }
        }

        #endregion

        // =====================================================================
        // CHOICE PROCESSING
        // Dispatches the player's selection to the appropriate handler.
        // =====================================================================
        #region Choice Processing

        private static void ProcessChoice(List<InquiryElement> chosen)
        {
            try
            {
                if (chosen == null || chosen.Count == 0) return;
                string action = chosen[0].Identifier as string ?? "execute";

                switch (action)
                {
                    // ── Abort: clean retreat, no consequences ─────────────────
                    case "abort":
                        FinishOperation(SchemeOutcome.SmallLoss, "abort");
                        return;

                    // ── Slip Past: skip event, zero heat ─────────────────────
                    case "slippast":
                        _slipPastAvail = false;
                        ShowEventResult(
                            passed:       true,
                            ptsAdded:     0,
                            resultLine:   "Your agent slipped through the shadows undetected. The contact never saw them.",
                            abilityNote:  "SLIP PAST used — ability expended for this operation.");
                        return;

                    // ── Scout Ahead: peek next event, return to turn screen ───
                    case "scoutahead":
                        _scoutAheadAvail = false;
                        _peekedEventText = PeekNextEvent();
                        _hasPeekedNext   = true;
                        ShowScoutAheadScreen(_peekedEventText);
                        return;

                    // ── Smooth It Over: skill check with reduced fail pts ─────
                    case "smoothitover":
                        _smoothItOverAvail = false;
                        ResolveSkillCheck(reducedFail: true, guaranteedPass: false);
                        return;

                    // ── Silver Tongue: guaranteed pass, skip skill check ───────
                    case "silvertongue":
                        _silverTongueAvail = false;
                        ResolveSkillCheck(reducedFail: false, guaranteedPass: true);
                        return;

                    // ── Execute: standard skill check ─────────────────────────
                    default:
                        ResolveSkillCheck(reducedFail: false, guaranteedPass: false);
                        return;
                }
            }
            catch { }
        }

        // Rolls the skill check and dispatches to ShowEventResult.
        private static void ResolveSkillCheck(bool reducedFail, bool guaranteedPass)
        {
            float passChance = ComputePassChance(Hero.MainHero, _def);
            bool  passed     = guaranteedPass || (_rng.NextDouble() < passChance);

            int ptsAdded = passed
                ? _passPts
                : (reducedFail ? Math.Max(1, _failPts - 3) : _failPts);

            string resultLine  = passed ? DrawPassLine() : DrawFailLine();
            string abilityNote = guaranteedPass ? "SILVER TONGUE secured the pass — ability expended."
                               : reducedFail    ? $"SMOOTH IT OVER reduced the fail penalty — ability expended."
                               : "";

            ShowEventResult(passed, ptsAdded, resultLine, abilityNote);
        }

        #endregion

        // =====================================================================
        // RESULT DISPLAY
        // Shows the outcome of one event turn via InquiryData, then offers
        // Continue (next turn) or Abort (SmallLoss resolution).
        // Immediate resolution (bust / success) fires FinishOperation directly.
        // =====================================================================
        #region Result Display

        private static void ShowEventResult(bool passed, int ptsAdded, string resultLine, string abilityNote)
        {
            _score += ptsAdded;

            // ── Immediate resolution checks ───────────────────────────────────
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

            // ── Build result text ─────────────────────────────────────────────
            bool isLastTurn = (_turn >= _maxTurns);
            string bar = BuildStatusBar(_score, _riskSum);

            var sb = new StringBuilder();
            sb.AppendLine(passed ? "[PASS]  " + resultLine : "[FAIL]  " + resultLine);
            if (!string.IsNullOrEmpty(abilityNote))
                sb.AppendLine(abilityNote);
            sb.AppendLine();
            sb.AppendLine($"Heat added this turn: +{ptsAdded}");
            sb.AppendLine($"Heat: {bar}");
            sb.AppendLine($"Turns remaining: {_maxTurns - _turn}");
            if (isLastTurn)
                sb.AppendLine("\nThis was the final field report. Your agent withdraws. The objective was not reached.");

            _turn++; // advance before showing the screen so Continue shows correct "Turn N"

            string resultText  = sb.ToString();
            string resultTitle = $"Field Report — Turn {_turn - 1}/{_maxTurns} Complete";

            if (isLastTurn)
            {
                // Out of turns: SmallLoss on acknowledge
                InformationManager.ShowInquiry(
                    new InquiryData(
                        resultTitle, resultText,
                        true, false, "Return", null,
                        () => FinishOperation(SchemeOutcome.SmallLoss, "outoftime"),
                        null),
                    true);
            }
            else
            {
                // Offer push-your-luck decision: continue or bail
                InformationManager.ShowInquiry(
                    new InquiryData(
                        resultTitle, resultText,
                        true, true,
                        "Continue", "Abort Operation",
                        () => ShowTurn(),
                        () => FinishOperation(SchemeOutcome.SmallLoss, "abort")),
                    true);
            }
        }

        // Shows the peeked event and returns the player to the current turn screen.
        private static void ShowScoutAheadScreen(string nextEventPreview)
        {
            string body =
                "Your agent scouted the approach before committing.\n\n"
                + "The next field report will read:\n\n"
                + $"\"{nextEventPreview}\"\n\n"
                + "SCOUT AHEAD ability expended for this operation.\n"
                + "Return to make your choice for the current turn.";

            InformationManager.ShowInquiry(
                new InquiryData(
                    "Scout Ahead — Next Event Preview",
                    body,
                    true, false, "Return to Turn", null,
                    () => ShowTurn(),
                    null),
                true);
        }

        #endregion

        // =====================================================================
        // OPERATION RESOLUTION
        // Shows the final outcome screen. Delegates effect application to
        // SchemeSystem.ApplyPlayerSchemeOutcome via an OnClose callback so the
        // inquiry is fully closed before any game-state changes fire.
        // =====================================================================
        #region Operation Resolution

        private static void FinishOperation(SchemeOutcome outcome, string reason)
        {
            try
            {
                // ── Build final screen text ───────────────────────────────────
                string title, body;

                switch (outcome)
                {
                    case SchemeOutcome.Success:
                        title = "Operation Complete — SUCCESS";
                        body  = $"Your agent completed the operation against {_targetName}.\n\n"
                              + $"Final heat: {_score} / {_riskSum} (objective reached).\n\n"
                              + "The scheme takes effect.";
                        break;

                    case SchemeOutcome.Bust:
                        title = "Operation Blown — AGENT CAPTURED";
                        body  = $"Heat exceeded the limit. Your agent was seized near {_targetName}.\n\n"
                              + $"Final heat: {_score} / {BustLimit} — BUSTED.\n\n"
                              + "Crime rating rises. Relations collapse. The agent is lost.";
                        break;

                    default: // SmallLoss
                        title = "Operation Aborted";
                        body  = reason == "outoftime"
                              ? $"Your agent exhausted every opening near {_targetName} and withdrew quietly.\n\nNo exposure. The coin is spent."
                              : $"Your agent pulled back from {_targetName} on your order.\n\nNo exposure. The coin is spent.";
                        break;
                }

                // ── Set global cooldown ───────────────────────────────────────
                // Abort = 2 days (quick retry deterrent).
                // Success or Bust = 3 days (network needs to cool down fully).
                int cooldownDays = (outcome == SchemeOutcome.SmallLoss && reason == "abort") ? 2 : 3;
                try { SchemeSystem.SetPlayerCooldown(cooldownDays); } catch { }

                // Capture state for the callback closure — static fields will
                // be cleared or reused by the time the callback fires.
                SchemeOutcome    capturedOutcome = outcome;
                SchemeDefinition capturedDef     = _def;
                Hero             capturedHero    = _targetHero;
                Settlement       capturedSett    = _targetSett;

                InformationManager.ShowInquiry(
                    new InquiryData(
                        title, body,
                        true, false, "Close", null,
                        () =>
                        {
                            try
                            {
                                SchemeSystem.ApplyPlayerSchemeOutcome(
                                    capturedOutcome,
                                    capturedDef?.Type ?? SchemeType.Assassinate,
                                    capturedHero,
                                    capturedSett);
                            }
                            catch { }
                        },
                        null),
                    true);
            }
            catch { }
        }

        #endregion

        // =====================================================================
        // HELPERS
        // Pure utility — no side effects.
        // =====================================================================
        #region Helpers

        /// Pass chance: 30% base + (skill / 600) × 55%, clamped to [30%, 85%].
        private static float ComputePassChance(Hero hero, SchemeDefinition def)
        {
            if (hero == null || def == null) return 0.30f;
            try
            {
                int   skill = hero.GetSkillValue(def.Skill);
                float pct   = 0.30f + (skill / 600f) * 0.55f;
                return Math.Max(0.30f, Math.Min(0.85f, pct));
            }
            catch { return 0.30f; }
        }

        /// Builds a 20-character progress bar.
        /// = positions filled to score (within riskSum zone)
        /// + positions filled beyond riskSum (if somehow still running — won't happen normally)
        /// - positions between score and riskSum (remaining path to success)
        /// · positions between riskSum and BustLimit (danger zone)
        /// Example at score=8, riskSum=12, bust=21:
        ///   [========----········] 8/21  (need 12 to succeed)
        private static string BuildStatusBar(int score, int riskSum)
        {
            const int Width = 20;
            var sb = new StringBuilder("[");
            for (int i = 1; i <= Width; i++)
            {
                // Map bar position i to a score threshold in range [0, BustLimit]
                float threshold = i / (float)Width * BustLimit;

                if (threshold <= score)
                    sb.Append(score <= riskSum ? '=' : '+');  // filled (success or overshoot)
                else if (threshold <= riskSum)
                    sb.Append('-');                            // remaining path to success
                else
                    sb.Append('·');                            // danger zone (riskSum < pos <= 21)
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
            bool any = _slipPastAvail || _smoothItOverAvail || _silverTongueAvail
                    || (_scoutAheadAvail && _turn < _maxTurns);
            if (!any) return "";
            var sb = new StringBuilder("Available one-use abilities:\n");
            if (_slipPastAvail)
                sb.AppendLine("  [Roguery] SLIP PAST — skip encounter, +0 heat");
            if (_scoutAheadAvail && _turn < _maxTurns)
                sb.AppendLine("  [Roguery] SCOUT AHEAD — preview next event");
            if (_smoothItOverAvail)
                sb.AppendLine($"  [Charm]   SMOOTH IT OVER — fail adds only +{Math.Max(1, _failPts - 3)} heat");
            if (_silverTongueAvail)
                sb.AppendLine($"  [Charm]   SILVER TONGUE — guaranteed pass (+{_passPts} heat)");
            return sb.ToString();
        }

        /// Draws the next event from the shuffled pool (cycles if exhausted).
        private static string DrawNextEvent()
        {
            if (_events == null || _events.Count == 0)
                return "Your agent signals readiness. The path is unclear.";
            string e = _events[_eventIndex % _events.Count];
            _eventIndex++;
            return e;
        }

        /// Returns the NEXT event text without advancing _eventIndex (used by SCOUT AHEAD).
        private static string PeekNextEvent()
        {
            if (_events == null || _events.Count == 0)
                return "Your agent signals readiness. The path is unclear.";
            return _events[_eventIndex % _events.Count];
        }

        private static string DrawPassLine()
        {
            var lines = new[]
            {
                "The contact was handled without incident. Your agent moved through cleanly.",
                "Quick thinking — the cover held. No suspicion raised.",
                "Smooth passage. The agent read the situation perfectly.",
                "Favourable timing. The moment was seized before anyone noticed.",
                "The approach worked. Your agent is one step closer.",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string DrawFailLine()
        {
            var lines = new[]
            {
                "Something went wrong. The agent improvised, but not without leaving traces.",
                "The contact grew suspicious. Your agent retreated under scrutiny.",
                "A near miss. The agent escaped but drew attention. Heat is building.",
                "The timing was off. The approach drew more eyes than intended.",
                "The situation slipped. The target is now more alert than before.",
            };
            return lines[_rng.Next(lines.Length)];
        }

        #endregion

        // =====================================================================
        // EVENT POOLS
        // Eight thematic field-report strings per scheme type.
        // {0} = target name (lord's name or settlement name), inserted at build time.
        // Text is framed as intelligence reports arriving at the player's camp.
        // =====================================================================
        #region Event Pools

        private static List<string> BuildEventPool(SchemeType type, string targetName)
        {
            string[] raw  = GetRawEvents(type);
            var      pool = raw.Select(e => string.Format(e, targetName)).ToList();

            // Fisher-Yates shuffle — unpredictable draw order each operation
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int    j   = _rng.Next(i + 1);
                string tmp = pool[i];
                pool[i]    = pool[j];
                pool[j]    = tmp;
            }
            return pool;
        }

        private static string[] GetRawEvents(SchemeType type)
        {
            switch (type)
            {
                // ── Assassinate ───────────────────────────────────────────────
                // Target = lord's name. Roguery-based: guards, routes, infiltration.
                case SchemeType.Assassinate:
                    return new[]
                    {
                        "Guard rotations near {0}'s tent have been mapped. The midnight gap holds for three more nights.",
                        "Your agent infiltrated {0}'s escort posing as a merchant's aide. Daily routine is now confirmed.",
                        "A serving woman inside {0}'s hall passed the weekly schedule to your contact at the market.",
                        "{0} dined alone this evening — a rare gap in the usual crowd, noted by your man in the hall.",
                        "City watch near {0}'s quarters doubled after a rumoured threat reached the garrison commander.",
                        "{0}'s steward proved incorruptible. The bribe attempt nearly exposed your agent's purpose entirely.",
                        "A loyalist household guard questioned your agent's story near the postern gate at length.",
                        "The blade handler went silent for a full day. Your agent suspects he was followed from {0}'s camp.",
                    };

                // ── Spread Terror ─────────────────────────────────────────────
                // Target = settlement name. Roguery-based: incitement, unrest, watch.
                case SchemeType.SpreadTerror:
                    return new[]
                    {
                        "Market brawls in {0} are escalating on their own. The timing is right to fan the flames.",
                        "Your agent bribed a local thug gang to keep tension burning near {0}'s gates after dark.",
                        "Rumours of bandit incursions near {0} are spreading through the merchant quarter unaided.",
                        "The city watch in {0} is stretched thin — patrols are sparse along the southern docks at night.",
                        "A merchant guild in {0} threatened a public strike. Your agent nudged the talks toward collapse.",
                        "One of your contacts in {0} was pulled aside for questioning by a guardsman who noticed too much.",
                        "Watch officers in {0} tightened night inspections after an unrelated incident near the harbour.",
                        "Citizens of {0} have grown wary of unfamiliar faces. Your agent's cover is wearing thin.",
                    };

                // ── Poison Well ───────────────────────────────────────────────
                // Target = settlement name. Roguery-based: access, timing, guards.
                case SchemeType.PoisonWell:
                    return new[]
                    {
                        "The water source for {0}'s barracks has been found. Access is possible during the change of watch.",
                        "A sewer worker near {0}'s garrison accepted coin and asked no questions about the delivery.",
                        "Your agent confirmed the garrison at {0} draws from the eastern well — unguarded before dawn.",
                        "Night patrol routes near {0}'s well have a reliable gap of nearly an hour between circuits.",
                        "An unexpected militia exercise brought extra guards directly to {0}'s water stores today.",
                        "A guard recognised your agent near {0}'s postern gate and raised a brief, noisy alarm.",
                        "The eastern well at {0} was sealed temporarily following a supply irregularity report.",
                        "Garrison officers at {0} tightened access to the stores after an unexplained missing-stock report.",
                    };

                // ── Stage Coup ────────────────────────────────────────────────
                // Target = settlement name. Charm-based: bribery, loyalty, officers.
                case SchemeType.StageCoup:
                    return new[]
                    {
                        "Three garrison officers at {0} accepted a preliminary offer. The negotiation is proceeding well.",
                        "The constable of {0} expressed sympathy for the cause over wine at a private supper.",
                        "Your agent mapped the shift schedules of {0}'s key officers. The patterns are confirmed.",
                        "Resentment toward the current lord runs high among {0}'s garrison after two missed pay cycles.",
                        "A loyalist captain at {0} rejected the coin outright and summoned a senior officer at once.",
                        "One of the bought officers at {0} got cold feet and is demanding considerably more to stay bought.",
                        "The castellan of {0} dispatched investigators after a bribery rumour reached his office.",
                        "City guard at {0} arrested a go-between. Your agent's chain of contacts is now compromised.",
                    };

                // ── Spread Rumors ─────────────────────────────────────────────
                // Target = settlement name. Charm-based: whispers, trust, rumour spread.
                case SchemeType.SpreadRumors:
                    return new[]
                    {
                        "Whispers about corruption in {0}'s council are already circulating without any outside help.",
                        "A popular tavern in {0} proved fertile ground — the stories are spreading remarkably fast.",
                        "Your agent planted doubts about {0}'s last tax collector. Locals are incensed and talking.",
                        "Merchants passing through {0} are now carrying your rumours to settlements further north.",
                        "A local priest at {0} addressed the rumours from the pulpit this morning — damaging the effect.",
                        "A sharp-eyed notary at {0} began quietly investigating the origin of the stories.",
                        "One of your contacts in {0} was seen speaking at length with a garrison officer yesterday.",
                        "Citizens of {0} have grown more wary of unfamiliar travellers bearing unsolicited gossip.",
                    };

                // ── Burn Storage ──────────────────────────────────────────────
                // Target = settlement name. Roguery-based: warehouse access, night guards.
                case SchemeType.BurnStorage:
                    return new[]
                    {
                        "Your agent confirmed the layout of the warehouse district in {0}. Entry points are mapped.",
                        "Night shift guards at {0}'s granary rotate predictably — the window is confirmed and reliable.",
                        "A disgruntled dockworker at {0} agreed to look the other way during a specific night shift.",
                        "Dry weather in {0} this week makes the storage district exceptionally vulnerable to fire.",
                        "A fire inspector toured {0}'s warehouses this morning. Increased scrutiny is expected for days.",
                        "An alert guard at {0}'s granary discovered a torch cache left by your agent's contact.",
                        "City watch in {0} increased patrols near the warehouses after a fire in a neighbouring town.",
                        "Your agent's contact at {0} went dark after being questioned by the watch at the main gate.",
                    };

                // ── Bribe Soldiers ────────────────────────────────────────────
                // Target = settlement name. Charm-based: morale, debts, loyalty.
                case SchemeType.BribeSoldiers:
                    return new[]
                    {
                        "Three soldiers in {0}'s garrison expressed genuine interest in a more generous arrangement.",
                        "Your agent confirmed low morale in {0}'s garrison after the second consecutive missed pay cycle.",
                        "The garrison sergeant at {0} is known to be deeply in debt — a useful point of leverage.",
                        "Pay disputes at {0} have left a dozen soldiers ready to walk for the right offer and timing.",
                        "A loyalist captain at {0} grew suspicious of your agent's frequent contacts inside the barracks.",
                        "One soldier at {0} confessed the approach to his sergeant in exchange for a promised promotion.",
                        "A garrison sweep at {0} uncovered a coin cache left by your agent's contacts in the barracks.",
                        "The lord of {0} raised garrison pay by decree this week. Your agent's leverage has weakened.",
                    };

                // ── Forge Documents ───────────────────────────────────────────
                // Target = lord's name. Charm-based: seals, couriers, court officials.
                case SchemeType.ForgeDocuments:
                    return new[]
                    {
                        "Your agent secured a wax impression of {0}'s personal seal from a bribed courier.",
                        "A skilled scrivener in the capital agreed to replicate {0}'s official letterhead for gold.",
                        "Your agent intercepted a genuine dispatch from {0} and studied the style and handwriting closely.",
                        "The fabricated letters implicating {0} are nearly indistinguishable from authentic correspondence.",
                        "A court official grew suspicious of letters bearing {0}'s seal on unfamiliar messenger routes.",
                        "One of the hired couriers grew nervous and is now demanding considerably more gold to continue.",
                        "A royal archivist flagged minor inconsistencies in a recent letter attributed to {0}.",
                        "Your court contact reports heightened scrutiny of all correspondence bearing {0}'s seal.",
                    };

                // ── Hire Assassin ─────────────────────────────────────────────
                // Target = lord's name. Roguery-based: hired blade, escort, roads.
                case SchemeType.HireAssassin:
                    return new[]
                    {
                        "Your agent located a capable blade willing to target {0}'s escort for the agreed price.",
                        "The hired blade spent three days studying {0}'s party route and daily riding habits.",
                        "Your agent confirmed {0}'s patrol route passes through an isolated stretch of road at dusk.",
                        "The hired blade reports {0}'s escort is smaller than expected — the timing looks favourable.",
                        "A rival faction's scouts are watching {0}'s camp. The hired blade spotted them and held off.",
                        "The assassin grew cautious after {0}'s guard was unexpectedly doubled without apparent cause.",
                        "Your agent's contact reports that {0} received a private tip about a possible ambush on the road.",
                        "The hired blade went quiet for a full day after a thorough inspection at {0}'s camp perimeter.",
                    };

                // ── False Accusations ─────────────────────────────────────────
                // Target = lord's name. Charm-based: slander, court rumour, witnesses.
                case SchemeType.FalseAccusations:
                    return new[]
                    {
                        "Your agent planted evidence of {0}'s dealings with a rival clan at the king's court.",
                        "A gossiping noble eagerly repeated your agent's insinuations about {0} to exactly the right ears.",
                        "Your agent confirmed that {0}'s rivals at court are willing to believe almost any accusation.",
                        "Whispers against {0} have already reached the king's inner circle without further prompting.",
                        "A loyal ally of {0} began quietly and effectively rebutting the accusations in private meetings.",
                        "A court scribe grew curious about the precise origin of the accusations circulating about {0}.",
                        "One of your planted witnesses recanted under direct pressure from {0}'s clan members.",
                        "{0}'s standing at court proved more resilient than expected — the smear is not gaining traction.",
                    };

                // ── Viper's Counsel ───────────────────────────────────────────
                // Target = lord's name (same-kingdom rival). Charm-based: court intrigue.
                case SchemeType.VipersCounsel:
                    return new[]
                    {
                        "Your agent secured a private word with the king's chancellor regarding {0}'s recent conduct.",
                        "Doubts about {0}'s loyalty were already circulating at court — your agent merely amplified them.",
                        "Your agent arranged for fabricated correspondence implicating {0} to reach the king's hand.",
                        "The king listened far more attentively than expected when {0}'s past missteps were raised.",
                        "A close confidant of {0} sits near the king at every council. Your agent must tread carefully.",
                        "The king expressed pointed scepticism about the allegations against {0} after a private meeting.",
                        "A rival lord spoke up in {0}'s defence before the king, buying them unexpected goodwill.",
                        "{0}'s supporters at court outmanoeuvred your agent's contacts in the chancellor's circle this week.",
                    };

                // ── Scatter the Wolves ────────────────────────────────────────
                // Target = lord's name (their kingdom suffers). Roguery-based: bandits.
                case SchemeType.ScatterWolves:
                default:
                    return new[]
                    {
                        "Your agent contacted a bandit chieftain willing to flood {0}'s kingdom's roads this season.",
                        "Word reached your agent that deserters near {0}'s territory are ready to be hired and pointed.",
                        "Your agent confirmed the roads through {0}'s kingdom are lightly patrolled this time of year.",
                        "Three bandit bands have been paid and assigned their territories throughout {0}'s realm.",
                        "A patrol from {0}'s kingdom stumbled on your agent meeting with one of the bandit leaders.",
                        "One of the hired chieftains sold the plan to {0}'s lords in exchange for a pardon.",
                        "Your agent's courier was stopped and searched at a border checkpoint near {0}'s kingdom.",
                        "The lords of {0}'s kingdom assembled a hunting party — some paid bands may scatter and break.",
                    };
            }
        }

        #endregion
    }
}
