// =============================================================================
// ASH AND EMBER — Schemes/SchemeMinigame.cs
// Blackjack-style push-your-luck minigame for player scheme resolution.
// NPC schemes use the original RNG path — this file does not affect them.
//
// Each round: a complication card is drawn with a point value. Player chooses
// DRAW (add value, continue) or STAND (commit current total). Bust if > 21.
// Roguery 100+ unlocks SKIP (ignore one bad card).
// Roguery 200+ also unlocks PEEK (preview next card value).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal enum SchemeOutcome
    {
        SmallLoss,  // total < risk sum
        Success,    // risk sum <= total <= 21
        Bust        // total > 21
    }

    internal static class SchemeMinigame
    {
        private const int BustThreshold = 21;

        private static readonly Random _rng = new Random();

        // ── Per-scheme config ─────────────────────────────────────────────────
        private struct SchemeConfig
        {
            internal int RiskSum;
            internal int CardMin;
            internal int CardMax;
        }

        private static SchemeConfig GetConfig(SchemeType type)
        {
            switch (type)
            {
                case SchemeType.SpreadRumors:     return new SchemeConfig { RiskSum = 7,  CardMin = 1, CardMax = 5 };
                case SchemeType.FalseAccusations: return new SchemeConfig { RiskSum = 7,  CardMin = 1, CardMax = 5 };
                case SchemeType.SpreadTerror:     return new SchemeConfig { RiskSum = 9,  CardMin = 1, CardMax = 6 };
                case SchemeType.BurnStorage:      return new SchemeConfig { RiskSum = 9,  CardMin = 1, CardMax = 6 };
                case SchemeType.BribeSoldiers:    return new SchemeConfig { RiskSum = 9,  CardMin = 1, CardMax = 6 };
                case SchemeType.PoisonWell:       return new SchemeConfig { RiskSum = 10, CardMin = 2, CardMax = 6 };
                case SchemeType.ForgeDocuments:   return new SchemeConfig { RiskSum = 10, CardMin = 2, CardMax = 6 };
                case SchemeType.ScatterWolves:    return new SchemeConfig { RiskSum = 12, CardMin = 2, CardMax = 7 };
                case SchemeType.VipersCounsel:    return new SchemeConfig { RiskSum = 12, CardMin = 2, CardMax = 7 };
                case SchemeType.HireAssassin:     return new SchemeConfig { RiskSum = 13, CardMin = 2, CardMax = 7 };
                case SchemeType.StageCoup:        return new SchemeConfig { RiskSum = 14, CardMin = 3, CardMax = 8 };
                case SchemeType.Assassinate:      return new SchemeConfig { RiskSum = 16, CardMin = 3, CardMax = 8 };
                default:                          return new SchemeConfig { RiskSum = 10, CardMin = 2, CardMax = 6 };
            }
        }

        // Exposed for the confirmation dialog — returns only what the UI needs.
        internal struct PublicConfig { internal int RiskSum; }
        internal static PublicConfig GetPublicConfig(SchemeType type)
        {
            var c = GetConfig(type);
            return new PublicConfig { RiskSum = c.RiskSum };
        }

        // ── Complication pools (6 per scheme type) ────────────────────────────
        private static string[] GetComplications(SchemeType type)
        {
            switch (type)
            {
                case SchemeType.Assassinate: return new[]
                {
                    "A guard patrols the inner corridor — his route has changed tonight.",
                    "The target's chamberlain is still awake, candle lit in the study.",
                    "A dog in the eastern courtyard has caught a scent.",
                    "The blade-man reports the target moved sleeping chambers.",
                    "Two sentries swap posts early — the window collapses without warning.",
                    "A servant lingers at the water basin longer than expected.",
                };
                case SchemeType.ForgeDocuments: return new[]
                {
                    "The scribe's wax seal doesn't match the lord's private stamp.",
                    "A courier asks too many questions about recent correspondence.",
                    "The parchment quality gives away its provincial origin.",
                    "A clerk at the chancery cross-references the date with a ledger.",
                    "The delivery route passes through a gatehouse with a sharp-eyed guard.",
                    "The recipient's steward demands to inspect the seal before accepting.",
                };
                case SchemeType.FalseAccusations: return new[]
                {
                    "One of your planted witnesses is getting cold feet.",
                    "A loyal retainer of the target begins asking pointed questions in the square.",
                    "The rumor is spreading faster than expected — slipping out of control.",
                    "A scribe offered to write a rebuttal letter on the target's behalf.",
                    "The accusations have reached someone who actually knows the truth.",
                    "Your contact is being followed by the target's household guard.",
                };
                case SchemeType.StageCoup: return new[]
                {
                    "One of the bribed officers is having second thoughts over his morning meal.",
                    "A loyalist sergeant overheard part of the conversation at the barracks gate.",
                    "The garrison captain has begun inspecting the men more closely than usual.",
                    "Word of unusual coin spending among the soldiers reached the steward.",
                    "A door that should be unlocked is barred from the inside.",
                    "The officer contact demands more gold before the next step.",
                };
                case SchemeType.PoisonWell: return new[]
                {
                    "The target well is guarded tonight — the shift did not rotate as expected.",
                    "A water-bearer is filling buckets from an unexpected source.",
                    "The agent dropped part of the compound near the well wall.",
                    "A passing milkmaid notices your agent crouching in the shadows.",
                    "The dosing vessel leaks — the timing must be adjusted quickly.",
                    "A child playing near the well lingers far past sundown.",
                };
                case SchemeType.BribeSoldiers: return new[]
                {
                    "The contact soldier is being watched by a suspicious junior officer.",
                    "Word of loose gold in the garrison has already reached a sergeant.",
                    "Three of the soldiers refuse — and their silence costs extra.",
                    "A garrison inspection was called without notice this morning.",
                    "The agreed handoff location has a new sentry post established nearby.",
                    "One soldier pockets the coin but looks like he's calculating the reward for turning you in.",
                };
                case SchemeType.BurnStorage: return new[]
                {
                    "The warehouse doors were relocked — someone changed the routine.",
                    "A night watchman lingers near the oil stores without apparent reason.",
                    "Unexpected rain has dampened the kindling — the fire may not catch easily.",
                    "A sleeping guard stirs at the wrong moment.",
                    "The fire requires more time to set than anticipated — footsteps approach.",
                    "A second watchman was added to the eastern side of the granary.",
                };
                case SchemeType.SpreadTerror: return new[]
                {
                    "A city constable recognized one of your agents from a previous incident.",
                    "The disturbance drew more attention than planned — town watch is mobilizing.",
                    "An armed merchant guild patrol is making unscheduled rounds.",
                    "A witness is remembering too many faces from the fracas.",
                    "The panic is spreading in the wrong direction — toward the wrong quarter.",
                    "A local informant recognized the pattern as organized, not opportunistic.",
                };
                case SchemeType.SpreadRumors: return new[]
                {
                    "The tavern-keeper is skeptical — demands something more credible.",
                    "A loyal supporter of the target is present and growing visibly upset.",
                    "The rumor picked up an embellishment that could be disproven easily.",
                    "A scribe took notes — not on the rumor, but on who was spreading it.",
                    "The story is too similar to one spread three years ago; locals remember.",
                    "A town crier is already preparing a rebuttal on the lord's behalf.",
                };
                case SchemeType.HireAssassin: return new[]
                {
                    "The mercenary contact is asking uncomfortable questions about payment security.",
                    "The target's escort changed route unexpectedly — the ambush point is wrong.",
                    "A passing merchant party complicates the planned intercept location.",
                    "The hired blade wants double the original sum to proceed tonight.",
                    "One of the target's outriders is notably more alert than the others.",
                    "The handoff of final instructions drew an unwanted witness.",
                };
                case SchemeType.VipersCounsel: return new[]
                {
                    "A court attendant overheard part of the conversation near the king's hall.",
                    "The king's chamberlain seems unusually interested in who visited this afternoon.",
                    "A counter-narrative is already circulating — someone is defending the target.",
                    "The king asked a clarifying question your agent wasn't prepared for.",
                    "A scribe near the throne room was writing something throughout the audience.",
                    "The target was seen speaking privately with the king immediately after your audience.",
                };
                case SchemeType.ScatterWolves: return new[]
                {
                    "The bandit captain wants more upfront gold before committing his men.",
                    "One of the deserter bands already dispersed — replacements must be found.",
                    "A border patrol spotted the gathering near the crossing point.",
                    "The paymaster contact was robbed last night — the coin is gone.",
                    "A rival faction is competing for the same bandit groups with higher offers.",
                    "The agreed muster point is now too close to an enemy patrol route.",
                };
                default: return new[]
                {
                    "Something unexpected complicates the approach.",
                    "A contact goes silent at a critical moment.",
                    "The timing is off — a window is closing.",
                    "An unplanned observer appears near the operation.",
                    "The plan requires adjustment on the fly.",
                    "A loose thread threatens to unravel everything.",
                };
            }
        }

        // ── State ─────────────────────────────────────────────────────────────
        private static SchemeDefinition _def;
        private static Hero             _targetHero;
        private static Settlement       _targetSett;
        private static int              _total;          // running sum of drawn cards
        private static int              _round;
        private static int              _currentValue;   // value of the card currently on display
        private static int              _peekedValue;    // -1 = not peeked; >=0 = peeked next value
        private static bool             _skipUsed;
        private static bool             _peekUsed;
        private static List<string>     _compPool;

        // ── Entry point ───────────────────────────────────────────────────────
        internal static void Begin(SchemeDefinition def, Hero targetHero, Settlement targetSett)
        {
            _def          = def;
            _targetHero   = targetHero;
            _targetSett   = targetSett;
            _total        = 0;
            _round        = 0;
            _peekedValue  = -1;
            _skipUsed     = false;
            _peekUsed     = false;

            var pool = GetComplications(def.Type);
            _compPool = pool.OrderBy(_ => _rng.Next()).ToList();

            // Debug mode: auto-succeed at max value under 21.
            if (SchemeSystem.DebugFree)
            {
                _total = GetConfig(def.Type).RiskSum;
                Resolve();
                return;
            }

            DrawAndShow();
        }

        // ── Draw next card and show the round ─────────────────────────────────
        private static void DrawAndShow()
        {
            if (_compPool.Count == 0)
            {
                var pool = GetComplications(_def.Type);
                _compPool = pool.OrderBy(_ => _rng.Next()).ToList();
            }

            _round++;
            string comp = _compPool[0];
            _compPool.RemoveAt(0);

            var cfg = GetConfig(_def.Type);

            // Use peeked value if available, otherwise generate fresh
            _currentValue = (_peekedValue >= 0) ? _peekedValue : DrawValue(cfg);
            _peekedValue  = -1; // consumed

            int projectedTotal = _total + _currentValue;

            // Build body text
            string bustWarning = projectedTotal > BustThreshold
                ? $"\n⚠  Taking this would bust you! ({projectedTotal} > 21)"
                : projectedTotal > 21 - cfg.CardMin
                    ? $"\n   Caution: taking this puts you at {projectedTotal} — close to the bust line."
                    : "";

            string body = $"Running total: {_total}  |  Need: ≥{cfg.RiskSum}  |  Bust at: 21  |  Round {_round}"
                        + $"\n\n\"{comp}\"\nThis complication is worth {_currentValue} points."
                        + $"\nTaking it would bring your total to {projectedTotal}."
                        + bustWarning;

            // Build options
            var options = new List<InquiryElement>();

            // STAND — always available
            string standResult;
            if (_total >= cfg.RiskSum)       standResult = $"success secured (total: {_total})";
            else if (_total > 0)             standResult = $"short of target — small loss (total: {_total}, need {cfg.RiskSum})";
            else                             standResult = "nothing gained — small loss";
            options.Add(new InquiryElement("stand",
                $"STAND — Commit without this card  [{standResult}]",
                null, true,
                $"Lock in your current total of {_total}. " +
                (_total >= cfg.RiskSum ? "Scheme succeeds." : "Scheme fails — costs are spent, no consequences.")));

            // SKIP — Roguery 100+, one use
            int roguery = 0;
            try { roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0; } catch { }
            if (roguery >= 100 && !_skipUsed)
            {
                options.Add(new InquiryElement("skip",
                    "[Street Smarts] SKIP — Discard this complication, draw the next",
                    null, true,
                    "Use your Roguery expertise to sidestep this obstacle without cost. One use per scheme."));
            }

            // PEEK — Roguery 200+, one use, only if not already peeking
            if (roguery >= 200 && !_peekUsed && _peekedValue < 0)
            {
                options.Add(new InquiryElement("peek",
                    "[Cold Read] PEEK — Preview the next complication's value",
                    null, true,
                    "Your mastery of Roguery lets you read ahead. See what comes next before committing. One use per scheme."));
            }

            // DRAW — take this card
            string drawLabel = projectedTotal > BustThreshold
                ? $"DRAW — Take it (+{_currentValue}) — TOTAL: {projectedTotal}  ⚠  BUST!"
                : $"DRAW — Take it (+{_currentValue}) — new total: {projectedTotal}";
            options.Add(new InquiryElement("draw", drawLabel, null, true,
                projectedTotal > BustThreshold
                    ? "This will bust the scheme. The operation backfires catastrophically."
                    : $"Accept this complication. Your total becomes {projectedTotal}."));

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"The Gambit — {_def.Name}",
                        body,
                        options, false, 1, 1,
                        "Confirm", "Abort Scheme",
                        chosen => { try { ProcessChoice(chosen?[0]?.Identifier as string ?? "stand"); } catch { } },
                        _      => { try { OnAbort(); } catch { } }),
                    true);
            }
            catch { }
        }

        private static int DrawValue(SchemeConfig cfg)
            => cfg.CardMin + _rng.Next(cfg.CardMax - cfg.CardMin + 1);

        // ── Choice processing ─────────────────────────────────────────────────
        private static void ProcessChoice(string choiceId)
        {
            switch (choiceId)
            {
                case "stand":
                    Resolve();
                    break;

                case "skip":
                    _skipUsed = true;
                    DrawAndShow();
                    break;

                case "peek":
                    _peekUsed    = true;
                    _peekedValue = DrawValue(GetConfig(_def.Type));
                    RedisplayWithPeek();
                    break;

                case "draw":
                    _total += _currentValue;
                    if (_total > BustThreshold) { OnBust(); return; }
                    DrawAndShow();
                    break;
            }
        }

        // Redisplay the current card with peek information now visible.
        private static void RedisplayWithPeek()
        {
            var cfg = GetConfig(_def.Type);
            int projectedTotal = _total + _currentValue;

            string bustWarning = projectedTotal > BustThreshold
                ? $"\n⚠  Taking this would bust you! ({projectedTotal} > 21)"
                : "";

            string body = $"Running total: {_total}  |  Need: ≥{cfg.RiskSum}  |  Bust at: 21  |  Round {_round}"
                        + $"\n\nCurrent complication is worth {_currentValue} points."
                        + $"\nTaking it would bring your total to {projectedTotal}."
                        + bustWarning
                        + $"\n\n[Cold Read] The next complication after this will be worth {_peekedValue} points.";

            var options = new List<InquiryElement>();

            string standResult = _total >= cfg.RiskSum ? $"success (total: {_total})" : $"small loss (total: {_total})";
            options.Add(new InquiryElement("stand",
                $"STAND — Commit without this card [{standResult}]",
                null, true, $"Lock in {_total}."));

            int roguery = 0;
            try { roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0; } catch { }
            if (roguery >= 100 && !_skipUsed)
            {
                options.Add(new InquiryElement("skip",
                    "[Street Smarts] SKIP — Discard this complication, draw the next",
                    null, true, "One use per scheme."));
            }

            string drawLabel = projectedTotal > BustThreshold
                ? $"DRAW — Take it (+{_currentValue}) — TOTAL: {projectedTotal}  ⚠  BUST!"
                : $"DRAW — Take it (+{_currentValue}) — new total: {projectedTotal}";
            options.Add(new InquiryElement("draw", drawLabel, null, true,
                projectedTotal > BustThreshold ? "This busts the scheme." : $"Total becomes {projectedTotal}."));

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"The Gambit — {_def.Name}",
                        body,
                        options, false, 1, 1,
                        "Confirm", "Abort Scheme",
                        chosen => { try { ProcessChoice(chosen?[0]?.Identifier as string ?? "stand"); } catch { } },
                        _      => { try { OnAbort(); } catch { } }),
                    true);
            }
            catch { }
        }

        // ── Resolve (STAND) ───────────────────────────────────────────────────
        private static void Resolve()
        {
            var cfg = GetConfig(_def.Type);
            SchemeOutcome outcome = _total >= cfg.RiskSum ? SchemeOutcome.Success : SchemeOutcome.SmallLoss;
            try { SchemeSystem.ApplyPlayerSchemeOutcome(_def.Type, Hero.MainHero, _targetHero, _targetSett, outcome); } catch { }
            try { SchemeSystem.SetPlayerCooldown(_def.Type, _targetHero, _targetSett); } catch { }
        }

        // ── Bust ──────────────────────────────────────────────────────────────
        private static void OnBust()
        {
            try { SchemeSystem.ApplyBreakConsequence(_def.Type, Hero.MainHero, _targetHero, _targetSett); } catch { }
            try { SchemeSystem.SetPlayerCooldown(_def.Type, _targetHero, _targetSett); } catch { }
        }

        // ── Abort ─────────────────────────────────────────────────────────────
        private static void OnAbort()
        {
            try
            {
                MBInformationManager.AddQuickInformation(
                    new TextObject("Your agent retreats. The scheme is abandoned — costs are spent."));
            }
            catch { }
            try { SchemeSystem.SetPlayerCooldown(_def.Type, _targetHero, _targetSett, days: 2); } catch { }
        }
    }
}
