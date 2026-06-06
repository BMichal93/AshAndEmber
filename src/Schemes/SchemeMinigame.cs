// =============================================================================
// ASH AND EMBER — Schemes/SchemeMinigame.cs
// Push-your-luck operation minigame for player scheme resolution.
// NPC schemes use the original RNG path — this file does not affect them.
//
// Each phase: a field report arrives with an exposure rating. The operative
// decides to PRESS ON (accept the exposure, continue) or EXTRACT (commit
// current exposure and resolve the operation). Blown if exposure > 21.
//
// SIDESTEP and RECON are available to everyone — Roguery determines success
// chance. A failed attempt causes a random ±10 exposure adjustment.
//
//   Roguery 0   → ~20% ability success
//   Roguery 250 → ~50% ability success
//   Roguery 500 → ~80% ability success (cap)
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
    public enum SchemeOutcome
    {
        SmallLoss,  // exposure < threshold (extracted short)
        Success,    // threshold ≤ exposure ≤ 21
        Bust        // exposure > 21 (operation blown)
    }

    internal static class SchemeMinigame
    {
        private const int BlownThreshold = 21;

        private static readonly Random _rng = new Random();

        // ── Per-scheme config ─────────────────────────────────────────────────
        private struct SchemeConfig
        {
            internal int RiskSum;   // exposure threshold for success
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

        // Exposed for the confirmation dialog.
        internal struct PublicConfig { internal int RiskSum; }
        internal static PublicConfig GetPublicConfig(SchemeType type)
        {
            var c = GetConfig(type);
            return new PublicConfig { RiskSum = c.RiskSum };
        }

        // ── Field report pools (6 per scheme type) ────────────────────────────
        private static string[] GetReports(SchemeType type)
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

        // ── Operation state ───────────────────────────────────────────────────
        private static SchemeDefinition _def;
        private static Hero             _targetHero;
        private static Settlement       _targetSett;
        private static int              _exposure;       // running exposure total
        private static int              _phase;
        private static int              _currentRating;  // exposure rating of the current report
        private static string           _currentReport;  // text of the current report (for recon redisplay)
        private static int              _peekedRating;   // -1 = no peek; >=0 = peeked next rating
        private static bool             _sidestepUsed;
        private static bool             _reconUsed;
        private static List<string>     _reportPool;

        // ── Entry point ───────────────────────────────────────────────────────
        internal static void Begin(SchemeDefinition def, Hero targetHero, Settlement targetSett)
        {
            _def           = def;
            _targetHero    = targetHero;
            _targetSett    = targetSett;
            _exposure      = 0;
            _phase         = 0;
            _currentReport = "";
            _peekedRating  = -1;
            _sidestepUsed  = false;
            _reconUsed     = false;

            var pool = GetReports(def.Type);
            _reportPool = pool.OrderBy(_ => _rng.Next()).ToList();

            if (SchemeSystem.DebugFree)
            {
                _exposure = GetConfig(def.Type).RiskSum;
                Resolve();
                return;
            }

            NextPhase();
        }

        // ── Draw next report and show the phase ───────────────────────────────
        private static void NextPhase()
        {
            if (_reportPool.Count == 0)
            {
                var pool = GetReports(_def.Type);
                _reportPool = pool.OrderBy(_ => _rng.Next()).ToList();
            }

            _phase++;
            _currentReport = _reportPool[0];
            _reportPool.RemoveAt(0);

            var cfg = GetConfig(_def.Type);

            _currentRating = (_peekedRating >= 0) ? _peekedRating : DrawRating(cfg);
            _peekedRating  = -1;

            ShowPhase(_currentReport, cfg);
        }

        private static void ShowPhase(string report, SchemeConfig cfg)
        {
            int projectedExposure = _exposure + _currentRating;

            string warning = projectedExposure > BlownThreshold
                ? $"\n⚠  Pressing on would blow the operation! ({projectedExposure} > 21)"
                : projectedExposure > BlownThreshold - cfg.CardMin
                    ? $"\n   Caution: pressing on brings exposure to {projectedExposure} — close to the limit."
                    : "";

            string body = $"Exposure: {_exposure}  |  Threshold: ≥{cfg.RiskSum}  |  Blown at: 21  |  Phase {_phase}"
                        + $"\n\nField report:\n\"{report}\""
                        + $"\n\nThis development is rated {_currentRating} exposure."
                        + $"\nPressing on raises total exposure to {projectedExposure}."
                        + warning;

            var options = new List<InquiryElement>();

            // EXTRACT — always available
            string extractStatus;
            if (_exposure >= cfg.RiskSum)    extractStatus = $"threshold reached — operation succeeds (exposure: {_exposure})";
            else if (_exposure > 0)          extractStatus = $"below threshold — quiet retreat (exposure: {_exposure}, need {cfg.RiskSum})";
            else                             extractStatus = "nothing achieved — quiet retreat";
            options.Add(new InquiryElement("extract",
                $"EXTRACT — Stand down with current exposure  [{extractStatus}]",
                null, true,
                $"Commit to current exposure of {_exposure}. " +
                (_exposure >= cfg.RiskSum ? "Operation succeeds." : "Agent retreats cleanly — costs spent, no consequences.")));

            // SIDESTEP — available to all, success by Roguery, one use per operation
            if (!_sidestepUsed)
            {
                int pct = AbilitySuccessPct();
                options.Add(new InquiryElement("sidestep",
                    $"[SIDESTEP] Navigate around this development  [{pct}% chance]",
                    null, true,
                    $"Attempt to bypass this complication cleanly. Success ({pct}%): it is avoided at no cost. "
                    + "Failure: chaotic outcome — exposure shifts by a random ±10. One use per operation."));
            }

            // RECON — available to all, success by Roguery, one use per operation
            if (!_reconUsed && _peekedRating < 0)
            {
                int pct = AbilitySuccessPct();
                options.Add(new InquiryElement("recon",
                    $"[RECON] Scout ahead to preview the next development  [{pct}% chance]",
                    null, true,
                    $"Attempt to get advance intelligence on what comes next. Success ({pct}%): see the next report's "
                    + "exposure rating before deciding here. Failure: chaotic outcome — exposure shifts by ±10. One use per operation."));
            }

            // PRESS ON — accept this development
            string pressLabel = projectedExposure > BlownThreshold
                ? $"PRESS ON — Accept (+{_currentRating}) — exposure: {projectedExposure}  ⚠  BLOWN!"
                : $"PRESS ON — Accept (+{_currentRating}) — new exposure: {projectedExposure}";
            options.Add(new InquiryElement("advance", pressLabel, null, true,
                projectedExposure > BlownThreshold
                    ? "Pressing on blows the operation. Your agent is exposed — consequences will follow."
                    : $"Accept this development. Exposure becomes {projectedExposure}."));

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"The Gambit — {_def.Name}",
                        body,
                        options, false, 1, 1,
                        "Confirm", "Abort Operation",
                        chosen => { try { ProcessChoice(chosen?[0]?.Identifier as string ?? "extract"); } catch { } },
                        _      => { try { OnAbort(); } catch { } }),
                    true);
            }
            catch { }
        }

        private static int DrawRating(SchemeConfig cfg)
            => cfg.CardMin + _rng.Next(cfg.CardMax - cfg.CardMin + 1);

        // ── Ability success chance (Roguery-based, open to all) ───────────────
        private static float ComputeAbilityChance()
        {
            int roguery = 0;
            try { roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0; } catch { }
            return Math.Max(0.20f, Math.Min(0.80f, 0.20f + (roguery / 500f) * 0.60f));
        }

        private static int AbilitySuccessPct()
            => (int)(ComputeAbilityChance() * 100f);

        // ── Choice processing ─────────────────────────────────────────────────
        private static void ProcessChoice(string choiceId)
        {
            switch (choiceId)
            {
                case "extract":
                    Resolve();
                    break;

                case "sidestep":
                    _sidestepUsed = true;
                    if (_rng.NextDouble() < ComputeAbilityChance())
                    {
                        // Success: bypass this development cleanly
                        NextPhase();
                    }
                    else
                    {
                        // Failure: chaotic ±10 exposure
                        ApplyRandomShift("Sidestep");
                    }
                    break;

                case "recon":
                    _reconUsed = true;
                    if (_rng.NextDouble() < ComputeAbilityChance())
                    {
                        // Success: preview next report's rating
                        _peekedRating = DrawRating(GetConfig(_def.Type));
                        ShowReconResult(_peekedRating);
                    }
                    else
                    {
                        // Failure: chaotic ±10 exposure
                        ApplyRandomShift("Recon");
                    }
                    break;

                case "advance":
                    _exposure += _currentRating;
                    if (_exposure > BlownThreshold) { OnBust(); return; }
                    NextPhase();
                    break;
            }
        }

        // Applies a random ±10 exposure shift and continues. Handles bust.
        private static void ApplyRandomShift(string abilityName)
        {
            int delta = _rng.Next(2) == 0 ? 10 : -10;
            _exposure = Math.Max(0, _exposure + delta);

            string msg = delta > 0
                ? $"{abilityName} failed — the attempt drew unwanted attention. Exposure +10."
                : $"{abilityName} failed — but the confusion bought cover. Exposure −10.";
            try { MBInformationManager.AddQuickInformation(new TextObject(msg)); } catch { }

            if (_exposure > BlownThreshold) { OnBust(); return; }
            NextPhase();
        }

        // Shows the recon success screen, then returns player to the current phase.
        private static void ShowReconResult(int nextRating)
        {
            string body =
                "Your scout slipped through and returned with advance intelligence.\n\n"
                + $"The next field report will be rated {nextRating} exposure.\n\n"
                + "RECON ability expended for this operation.\n"
                + "Return to decide on the current development.";

            InformationManager.ShowInquiry(
                new InquiryData(
                    "Recon — Intelligence Received",
                    body,
                    true, false, "Return to Briefing", null,
                    () => { try { ShowPhase(_currentReport, GetConfig(_def.Type)); } catch { } },
                    null),
                true);
        }

        // ── Resolve (EXTRACT) ─────────────────────────────────────────────────
        private static void Resolve()
        {
            var cfg = GetConfig(_def.Type);
            SchemeOutcome outcome = _exposure >= cfg.RiskSum ? SchemeOutcome.Success : SchemeOutcome.SmallLoss;
            try { SchemeSystem.ApplyPlayerSchemeOutcome(_def.Type, Hero.MainHero, _targetHero, _targetSett, outcome); } catch { }
            // SmallLoss extract: same 2-day cooldown as Abort so players aren't penalised
            // more for a deliberate quiet retreat than for panic-aborting immediately.
            // Success/Bust use default (7/14-day per-target, 3-day global) — network disrupted.
            int cooldownDays = outcome == SchemeOutcome.SmallLoss ? 2 : -1;
            try { SchemeSystem.SetPlayerCooldown(_def.Type, _targetHero, _targetSett, cooldownDays); } catch { }
        }

        // ── Blown ─────────────────────────────────────────────────────────────
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
                    new TextObject("Your agent stands down. The operation is abandoned — costs are spent."));
            }
            catch { }
            try { SchemeSystem.SetPlayerCooldown(_def.Type, _targetHero, _targetSett, days: 2); } catch { }
        }
    }
}
