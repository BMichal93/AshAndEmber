// =============================================================================
// ASH AND EMBER — Schemes/SchemeMinigame.cs
// Push-your-luck operation minigame for player scheme resolution.
// NPC schemes use the original RNG path — this file does not affect them.
//
// Each phase: a field report arrives. The operative decides HOW to push:
//   · PUSH HARD (Aggressive): hidden roll +1 to +7 — volatile, high reward
//   · TREAD CAREFULLY:        hidden roll −3 to +3 — balanced, unpredictable
//   · PULL BACK (Defensive):  hidden roll −7 to −1 — always reduces exposure
// Or: EXTRACT to stand down, SIDESTEP (Roguery) to skip, TALK IT DOWN (Charm).
//
// Rounds are limited — count scales with Roguery (3–8). When rounds run out:
//   50% → Bust (blown), 50% → Fail (quiet retreat, no consequences).
//
// The exact roll is always hidden until after the choice is made.
// Blown if exposure > 21. Success if extraction at or above the threshold.
//
//   Roguery 0   → 3 rounds, Sidestep ~20%
//   Roguery 300 → 6 rounds, Sidestep ~56%
//   Roguery 500 → 8 rounds (cap), Sidestep ~80%
//   Charm drives Talk It Down on the same 20–80% curve.
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
        SmallLoss,  // exposure < threshold (extracted short or rounds exhausted with no bust)
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
        }

        private static SchemeConfig GetConfig(SchemeType type)
        {
            switch (type)
            {
                case SchemeType.SpreadRumors:     return new SchemeConfig { RiskSum = 12 };
                case SchemeType.FalseAccusations: return new SchemeConfig { RiskSum = 12 };
                case SchemeType.SpreadTerror:     return new SchemeConfig { RiskSum = 13 };
                case SchemeType.BurnStorage:      return new SchemeConfig { RiskSum = 13 };
                case SchemeType.BribeSoldiers:    return new SchemeConfig { RiskSum = 14 };
                case SchemeType.PoisonWell:       return new SchemeConfig { RiskSum = 15 };
                case SchemeType.ForgeDocuments:   return new SchemeConfig { RiskSum = 15 };
                case SchemeType.VipersCounsel:    return new SchemeConfig { RiskSum = 16 };
                case SchemeType.ScatterWolves:    return new SchemeConfig { RiskSum = 17 };
                case SchemeType.HireAssassin:     return new SchemeConfig { RiskSum = 17 };
                case SchemeType.StageCoup:        return new SchemeConfig { RiskSum = 18 };
                case SchemeType.Assassinate:      return new SchemeConfig { RiskSum = 19 };
                default:                          return new SchemeConfig { RiskSum = 13 };
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
        private static int              _exposure;
        private static int              _phase;
        private static int              _roundsLimit;
        private static int              _roundsRemaining;
        private static string           _currentReport;
        private static bool             _sidestepUsed;
        private static bool             _charmUsed;
        private static List<string>     _reportPool;

        // ── Entry point ───────────────────────────────────────────────────────
        internal static void Begin(SchemeDefinition def, Hero targetHero, Settlement targetSett)
        {
            _def             = def;
            _targetHero      = targetHero;
            _targetSett      = targetSett;
            _exposure        = 0;
            _phase           = 0;
            _roundsLimit     = ComputeRoundsLimit();
            _roundsRemaining = _roundsLimit;
            _currentReport   = "";
            _sidestepUsed    = false;
            _charmUsed       = false;

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

        // Roguery → rounds: base 5, +1 per 100 points, cap 10.
        private static int ComputeRoundsLimit()
        {
            int roguery = 0;
            try { roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0; } catch { }
            return Math.Min(10, 5 + roguery / 100);
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

            ShowPhase(_currentReport);
        }

        private static void ShowPhase(string report)
        {
            var cfg = GetConfig(_def.Type);
            bool isFinalRound = _roundsRemaining == 1;

            string status = _exposure >= cfg.RiskSum
                ? "threshold reached — extract for success"
                : $"need {cfg.RiskSum - _exposure} more to reach threshold";

            string finalWarning = isFinalRound
                ? "\n\n⚠  FINAL ROUND — after this, the operation ends (50% bust / 50% fail if not extracted)."
                : "";

            string body = $"Exposure: {_exposure}  |  Threshold: ≥{cfg.RiskSum}  |  Blown at: 21"
                        + $"  |  Round {_phase}/{_roundsLimit}  [{status}]"
                        + $"\n\nField report:\n\"{report}\""
                        + $"\n\nHow does your operative respond?"
                        + finalWarning;

            var options = new List<InquiryElement>();

            // EXTRACT — always available
            string extractStatus;
            if (_exposure >= cfg.RiskSum)    extractStatus = "threshold reached — operation succeeds";
            else if (_exposure > 0)          extractStatus = "below threshold — quiet retreat";
            else                             extractStatus = "nothing achieved — quiet retreat";
            options.Add(new InquiryElement("extract",
                $"EXTRACT — Stand down  [{extractStatus}]",
                null, true,
                _exposure >= cfg.RiskSum
                    ? $"Commit to exposure {_exposure}. Operation succeeds."
                    : $"Commit to exposure {_exposure}. Agent retreats cleanly — costs spent, no consequences."));

            // SIDESTEP (Roguery) — one use per operation
            if (!_sidestepUsed)
            {
                int pct = AbilitySuccessPct(isCharm: false);
                options.Add(new InquiryElement("sidestep",
                    $"[SIDESTEP] Navigate around this complication  [{pct}% Roguery]",
                    null, true,
                    $"Bypass this development entirely. Success ({pct}%): advance to the next phase without any exposure cost. "
                    + "Failure: chaos — exposure shifts ±8 and the round is consumed. One use per operation."));
            }

            // TALK IT DOWN (Charm) — one use per operation
            if (!_charmUsed)
            {
                int pct = AbilitySuccessPct(isCharm: true);
                options.Add(new InquiryElement("charm",
                    $"[TALK IT DOWN] Smooth over the complication  [{pct}% Charm]",
                    null, true,
                    $"Use social grace to reduce the heat. Success ({pct}%): −5 exposure. "
                    + "Failure: +5 exposure. You remain in this phase to make your press decision. One use per operation."));
            }

            // PRESS ON — three hidden-roll options
            options.Add(new InquiryElement("aggressive",
                "PUSH HARD — Bold, aggressive move",
                null, true,
                "Go in hard. Could rack up serious exposure or barely any. You won't know until you commit. Range: +1 to +7."));

            options.Add(new InquiryElement("careful",
                "TREAD CAREFULLY — Measured approach",
                null, true,
                "Cautious and controlled. Small gain, small loss — or anything in between. Range: −3 to +3."));

            options.Add(new InquiryElement("defensive",
                "PULL BACK — Defensive positioning",
                null, true,
                "Give ground to reduce heat. Always lowers exposure, but costs a round. Range: −7 to −1."));

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

        // ── Skill-based ability chance ─────────────────────────────────────────
        private static float ComputeAbilityChance(bool isCharm)
        {
            int skill = 0;
            try
            {
                skill = isCharm
                    ? (Hero.MainHero?.GetSkillValue(DefaultSkills.Charm)   ?? 0)
                    : (Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0);
            }
            catch { }
            return Math.Max(0.20f, Math.Min(0.80f, 0.20f + (skill / 500f) * 0.60f));
        }

        private static int AbilitySuccessPct(bool isCharm)
            => (int)(ComputeAbilityChance(isCharm) * 100f);

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
                    if (_rng.NextDouble() < ComputeAbilityChance(isCharm: false))
                    {
                        try { MBInformationManager.AddQuickInformation(
                            new TextObject("Sidestep — your operative slipped through cleanly. No exposure taken.")); } catch { }
                        AdvanceRound();
                    }
                    else
                    {
                        ApplyRandomShift("Sidestep");
                    }
                    break;

                case "charm":
                    _charmUsed = true;
                    if (_rng.NextDouble() < ComputeAbilityChance(isCharm: true))
                    {
                        _exposure = Math.Max(0, _exposure - 5);
                        try { MBInformationManager.AddQuickInformation(
                            new TextObject("Talk It Down — smooth words defused the situation. Exposure −5.")); } catch { }
                        ShowPhase(_currentReport);
                    }
                    else
                    {
                        _exposure += 5;
                        try { MBInformationManager.AddQuickInformation(
                            new TextObject("Talk It Down failed — they grew suspicious. Exposure +5.")); } catch { }
                        if (_exposure > BlownThreshold) { OnBust(); return; }
                        ShowPhase(_currentReport);
                    }
                    break;

                case "aggressive":
                {
                    int roll = 1 + _rng.Next(7); // 1–7
                    try { MBInformationManager.AddQuickInformation(
                        new TextObject($"Pushed hard — exposure +{roll}.")); } catch { }
                    ApplyPressRoll(roll);
                    break;
                }

                case "careful":
                {
                    int roll = _rng.Next(7) - 3; // −3 to +3
                    string rollStr = roll >= 0 ? $"+{roll}" : $"{roll}";
                    try { MBInformationManager.AddQuickInformation(
                        new TextObject($"Careful approach — exposure {rollStr}.")); } catch { }
                    ApplyPressRoll(roll);
                    break;
                }

                case "defensive":
                {
                    int roll = -(1 + _rng.Next(7)); // −1 to −7
                    try { MBInformationManager.AddQuickInformation(
                        new TextObject($"Pulled back — exposure {roll}.")); } catch { }
                    ApplyPressRoll(roll);
                    break;
                }
            }
        }

        // Apply a press-on roll and advance the round.
        private static void ApplyPressRoll(int delta)
        {
            _exposure = Math.Max(0, _exposure + delta);
            if (_exposure > BlownThreshold) { OnBust(); return; }
            AdvanceRound();
        }

        // Decrement rounds, then bust on exhaustion or continue.
        private static void AdvanceRound()
        {
            _roundsRemaining--;
            if (_roundsRemaining <= 0) { OnRoundsExhausted(); return; }
            NextPhase();
        }

        // Applies a random ±8 exposure shift on ability failure, then advances.
        private static void ApplyRandomShift(string abilityName)
        {
            int delta = _rng.Next(2) == 0 ? 8 : -8;
            _exposure = Math.Max(0, _exposure + delta);

            string msg = delta > 0
                ? $"{abilityName} failed — drew unwanted attention. Exposure +{delta}."
                : $"{abilityName} failed — confusion bought cover. Exposure {delta}.";
            try { MBInformationManager.AddQuickInformation(new TextObject(msg)); } catch { }

            if (_exposure > BlownThreshold) { OnBust(); return; }
            AdvanceRound();
        }

        // ── Rounds exhausted ──────────────────────────────────────────────────
        private static void OnRoundsExhausted()
        {
            if (_rng.NextDouble() < 0.50)
            {
                try { MBInformationManager.AddQuickInformation(new TextObject(
                    "Time ran out. Your operative overstayed — the network collapsed. Operation blown.")); } catch { }
                try { SchemeSystem.ApplyBreakConsequence(_def.Type, Hero.MainHero, _targetHero, _targetSett); } catch { }
                try { SchemeSystem.SetPlayerCooldown(_def.Type, _targetHero, _targetSett); } catch { }
            }
            else
            {
                try { MBInformationManager.AddQuickInformation(new TextObject(
                    "Time ran out. Your operative withdrew before the net closed — the operation accomplished nothing.")); } catch { }
                try { SchemeSystem.ApplyPlayerSchemeOutcome(_def.Type, Hero.MainHero, _targetHero, _targetSett, SchemeOutcome.SmallLoss); } catch { }
                try { SchemeSystem.SetPlayerCooldown(_def.Type, _targetHero, _targetSett, days: 2); } catch { }
            }
        }

        // ── Resolve (EXTRACT) ─────────────────────────────────────────────────
        private static void Resolve()
        {
            var cfg = GetConfig(_def.Type);
            SchemeOutcome outcome = _exposure >= cfg.RiskSum ? SchemeOutcome.Success : SchemeOutcome.SmallLoss;
            try { SchemeSystem.ApplyPlayerSchemeOutcome(_def.Type, Hero.MainHero, _targetHero, _targetSett, outcome); } catch { }
            // SmallLoss extract: same 2-day cooldown as Abort — deliberate quiet retreat
            // shouldn't be penalised more than panic-aborting immediately.
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
