# Scheme Minigame — Implementation Guide

This document is a self-contained implementation guide. Read it fully before touching any file.

---

## What We're Building

A **Blackjack-style push-your-luck minigame** replacing passive RNG for player schemes only. Each round a complication is drawn with a point value. The player decides: **DRAW** (take the value, add to running total) or **STAND** (commit with current total). The scheme has a per-type **risk sum** (success threshold). Going over **21** busts the scheme and triggers a scheme-specific backfire. Roguery gives the player SKIP (ignore a bad card) and PEEK (preview the next card's value).

NPC schemes are completely unaffected.

**Three outcome states:**
| Situation | Result |
|---|---|
| Total > 21 | **BUST** — scheme backfires badly (scheme-specific consequence) |
| Risk sum ≤ total ≤ 21 | **SUCCESS** — standard scheme effects apply |
| Total < risk sum | **SMALL LOSS** — costs spent, agent retreated, no consequences |

**Three files total:**
- **Create:** `src/Schemes/SchemeMinigame.cs`
- **Edit:** `src/Schemes/SchemeSystem.cs` — add 4 methods, change 1 visibility modifier
- **Edit:** `src/Schemes/SchemeCampaignBehavior.cs` — replace confirmation text and CommitScheme

---

## Design Reference

### Per-Scheme Config

| Scheme | Risk Sum | Card Range | Notes |
|---|---|---|---|
| SpreadRumors | 7 | 1–5 | Easiest — 2 draws usually enough |
| FalseAccusations | 7 | 1–5 | |
| SpreadTerror | 9 | 1–6 | |
| BurnStorage | 9 | 1–6 | |
| BribeSoldiers | 9 | 1–6 | |
| PoisonWell | 10 | 2–6 | |
| ForgeDocuments | 10 | 2–6 | |
| ScatterWolves | 12 | 2–7 | |
| VipersCounsel | 12 | 2–7 | |
| HireAssassin | 13 | 2–7 | |
| StageCoup | 14 | 3–8 | |
| Assassinate | 16 | 3–8 | Hardest — needs 3+ draws, high bust risk |

### Roguery Abilities (each usable once per scheme run)

| Skill Level | Ability | Effect |
|---|---|---|
| < 100 | None | Raw luck only |
| 100–199 | **Street Smarts** (SKIP) | Discard the current complication, see the next one without adding any value |
| ≥ 200 | **Cold Read** (PEEK) + SKIP | Also: preview what the NEXT card's value will be before deciding |

### Bust Consequences

| Scheme | Backfire |
|---|---|
| Assassinate | Assassin captured, names you: crime +80, relations −80 with target, 60% war |
| ForgeDocuments | Traced back: YOUR relations −60 with your own faction leader, crime +40 |
| FalseAccusations | Rebounds: YOUR clan loses 10% renown, relations −60 with target |
| StageCoup | Riots trace to you: crime +50, your own settlement Loyalty −25 Security −20 |
| PoisonWell | Wrong supply line: crime +70, your own settlement FoodStocks × 0.60 |
| BribeSoldiers | Soldiers report you: crime +60, relations −70 with settlement owner |
| BurnStorage | Catastrophic fire: crime +60, target FoodStocks × 0.25 Prosperity × 0.70, relations −70 |
| SpreadTerror | Too organized: crime +70, relations −70 with settlement owner |
| SpreadRumors | Turns on you: crime +40, your own settlement Loyalty −20 Prosperity × 0.92 |
| VipersCounsel | King turns on you: relations −80 with YOUR king, −60 with target |
| HireAssassin | Betrayed: your party 20% troops wounded, crime +50 |
| ScatterWolves | Bandits flood YOUR roads: 3 bandit parties in your kingdom, crime +40 |

---

## STEP 1 — Create `src/Schemes/SchemeMinigame.cs`

```csharp
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

            string peekInfo = _peekedValue >= 0
                ? $"\n[Cold Read] The next complication will be worth {_peekedValue} points."
                : "";

            string body = $"Running total: {_total}  |  Need: ≥{cfg.RiskSum}  |  Bust at: 21  |  Round {_round}"
                        + $"\n\n\"{comp}\"\nThis complication is worth {_currentValue} points."
                        + $"\nTaking it would bring your total to {projectedTotal}."
                        + bustWarning
                        + peekInfo;

            // Build options
            var options = new List<InquiryElement>();

            // STAND — always available (but labelled with outcome context)
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
                    // Discard current card, draw next without adding value
                    DrawAndShow();
                    break;

                case "peek":
                    _peekUsed    = true;
                    _peekedValue = DrawValue(GetConfig(_def.Type));
                    // Redisplay same card, now with peek info visible
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
```

---

## STEP 2 — Edit `src/Schemes/SchemeSystem.cs`

### Change 2a — Make `SpawnBanditsInKingdom` internal

Find (around line 1010):
```csharp
private static int SpawnBanditsInKingdom(Kingdom kingdom, int partyCount)
```
Change `private` to `internal`.

### Change 2b — Add four new methods

Add these immediately before the closing `}` of the `SchemeSystem` class (after the `Save` method).

```csharp
        // ── Player scheme outcome (called by SchemeMinigame) ──────────────────
        internal static void ApplyPlayerSchemeOutcome(SchemeType type, Hero instigator,
            Hero targetHero, Settlement targetSett, SchemeOutcome outcome)
        {
            if (instigator == null) return;

            var fake = new PendingScheme
            {
                InstigatorId       = instigator.StringId ?? "",
                Type               = type,
                TargetHeroId       = targetHero?.StringId ?? "",
                TargetSettlementId = targetSett?.StringId  ?? "",
                DaysRemaining      = 0,
                IsPlayer           = true
            };

            switch (outcome)
            {
                case SchemeOutcome.SmallLoss:
                {
                    string tName = targetHero?.Name?.ToString()
                                ?? targetSett?.Name?.ToString() ?? "the target";
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"Your agent withdrew. The operation against {tName} dissolved without trace."));
                    break;
                }
                case SchemeOutcome.Success:
                    try { ApplySuccess(fake, instigator, targetHero, targetSett); } catch { }
                    break;

                case SchemeOutcome.Bust:
                    try { ApplyBreakConsequence(type, instigator, targetHero, targetSett); } catch { }
                    break;
            }
        }

        // ── Set player cooldown (called by SchemeMinigame after resolution) ───
        internal static void SetPlayerCooldown(SchemeType type, Hero targetHero,
            Settlement targetSett, int days = -1)
        {
            string targetId = targetHero?.StringId ?? targetSett?.StringId ?? "";
            if (string.IsNullOrEmpty(targetId)) return;
            string cdKey = CooldownKey(type, targetId);
            int cdDays = days >= 0 ? days
                       : type == SchemeType.Assassinate ? 14 : 7;
            _targetCooldowns[cdKey] = cdDays;
            _playerCooldownKeys.Add(cdKey);
        }

        // ── Break consequences ────────────────────────────────────────────────
        internal static void ApplyBreakConsequence(SchemeType type, Hero instigator,
            Hero targetHero, Settlement targetSett)
        {
            if (instigator == null) return;

            switch (type)
            {
                case SchemeType.Assassinate:
                {
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 80f, false); } catch { }
                    if (targetHero != null && targetHero.IsAlive && targetHero != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -80, false); } catch { }
                    var instigKingdom = instigator.Clan?.Kingdom;
                    if (_rng.NextDouble() < 0.60
                        && instigKingdom != null && targetKingdom != null
                        && instigKingdom != targetKingdom
                        && !instigKingdom.IsEliminated && !targetKingdom.IsEliminated
                        && !instigKingdom.IsAtWarWith(targetKingdom))
                        try { DeclareWarAction.ApplyByDefault(instigKingdom, targetKingdom); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — Your assassin was taken alive. Under interrogation, they spoke your name. War may follow."));
                    break;
                }
                case SchemeType.ForgeDocuments:
                {
                    Hero ownLeader = instigator.Clan?.Kingdom?.Leader;
                    if (ownLeader != null && ownLeader.IsAlive && ownLeader != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, ownLeader, -60, false); } catch { }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The forgery unraveled and traced back to you. Your own lord is asking questions."));
                    break;
                }
                case SchemeType.FalseAccusations:
                {
                    if (instigator.Clan != null)
                    {
                        float loss = Math.Max(80f, instigator.Clan.Renown * 0.10f);
                        try { instigator.Clan.Renown = Math.Max(0f, instigator.Clan.Renown - loss); } catch { }
                    }
                    if (targetHero != null && targetHero.IsAlive)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -60, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The slander was too clumsy. It circled back. Your own reputation is now in question."));
                    break;
                }
                case SchemeType.StageCoup:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 50f, false); } catch { }
                    var own = Settlement.All.FirstOrDefault(s =>
                        (s.IsTown || s.IsCastle) && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                    {
                        try { own.Town.Loyalty  = Math.Max(0f, own.Town.Loyalty  - 25f); } catch { }
                        try { own.Town.Security = Math.Max(0f, own.Town.Security - 20f); } catch { }
                    }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The coup collapsed into riots. Your involvement reached the wrong ears — your own holdings suffer."));
                    break;
                }
                case SchemeType.PoisonWell:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 70f, false); } catch { }
                    var own = Settlement.All.FirstOrDefault(s =>
                        s.IsTown && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                        try { own.Town.FoodStocks = Math.Max(10f, own.Town.FoodStocks * 0.60f); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The agent confused the supply lines. Your own stores were poisoned."));
                    break;
                }
                case SchemeType.BribeSoldiers:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 60f, false); } catch { }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The soldiers took your coin and marched straight to their captain."));
                    break;
                }
                case SchemeType.BurnStorage:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 60f, false); } catch { }
                    if (targetSett?.Town != null)
                    {
                        try { targetSett.Town.FoodStocks = Math.Max(10f, targetSett.Town.FoodStocks * 0.25f); } catch { }
                        try { targetSett.Town.Prosperity = Math.Max(10f, targetSett.Town.Prosperity * 0.70f); } catch { }
                    }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The fire leaped every wall. The destruction is total — and undeniably yours."));
                    break;
                }
                case SchemeType.SpreadTerror:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 70f, false); } catch { }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The violence was too organized. It left a trail straight to your doorstep."));
                    break;
                }
                case SchemeType.SpreadRumors:
                {
                    var own = Settlement.All.FirstOrDefault(s =>
                        s.IsTown && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                    {
                        try { own.Town.Loyalty    = Math.Max(0f,  own.Town.Loyalty    - 20f); } catch { }
                        try { own.Town.Prosperity = Math.Max(10f, own.Town.Prosperity * 0.92f); } catch { }
                    }
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The rumors warped in transit. Now they're about you. Your own people are whispering."));
                    break;
                }
                case SchemeType.VipersCounsel:
                {
                    Hero king = instigator.Clan?.Kingdom?.Leader;
                    if (king != null && king.IsAlive && king != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, king, -80, false); } catch { }
                    if (targetHero != null && targetHero.IsAlive)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -60, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The king saw through the veil and recognized the hand behind it. You are no longer welcome at court."));
                    break;
                }
                case SchemeType.HireAssassin:
                {
                    if (Hero.MainHero?.PartyBelongedTo?.MemberRoster != null)
                    {
                        foreach (var e in Hero.MainHero.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int toWound = Math.Max(1, (e.Number - e.WoundedNumber) / 5);
                            if (toWound <= 0) continue;
                            try { Hero.MainHero.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                        }
                    }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 50f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The blade turned around. Your own escort was ambushed."));
                    break;
                }
                case SchemeType.ScatterWolves:
                {
                    var ownKingdom = instigator.Clan?.Kingdom;
                    if (ownKingdom != null && !ownKingdom.IsEliminated)
                        try { SpawnBanditsInKingdom(ownKingdom, 3); } catch { }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The bandits took your coin and went wherever they pleased — including your own roads."));
                    break;
                }
            }
        }
```

---

## STEP 3 — Edit `src/Schemes/SchemeCampaignBehavior.cs`

### Change 3a — Update `ShowConfirmation`

In `ShowConfirmation`, find:
```csharp
                float chance     = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, targetHero, targetSett);
```
Delete this line (the `chance` variable is no longer used in the body text).

Then find the `body` string construction and `ShowInquiry` call and replace:

**Find:**
```csharp
                string body = $"Scheme: {_selectedDef.Name}\n"
                            + $"Target: {tName}\n"
                            + $"Cost: {goldCost}g  +  {infCost} influence{cdNote}\n"
                            + $"Success: {(int)(chance * 100)}%  |  Delay: 1–3 days\n"
                            + traitNote + "\n\n"
                            + failNote;

                InformationManager.ShowInquiry(
                    new InquiryData("Confirm Scheme", body, true, true, "Commit", "Cancel",
                        () => CommitScheme(targetHero, targetSett), null),
                    true);
```

**Replace with:**
```csharp
                var    cfg      = SchemeMinigame.GetPublicConfig(_selectedDef.Type);
                string body     = $"Scheme: {_selectedDef.Name}\n"
                                + $"Target: {tName}\n"
                                + $"Cost: {goldCost}g  +  {infCost} influence{cdNote}\n"
                                + $"Resolution: The Gambit  |  Need ≥{cfg.RiskSum}, bust at 21\n"
                                + traitNote + "\n\n"
                                + "Push your luck — draw complication cards and build a total. "
                                + "Stand when satisfied, or risk busting and facing consequences.\n\n"
                                + failNote;

                InformationManager.ShowInquiry(
                    new InquiryData("Confirm Scheme", body, true, true, "Proceed to Gambit", "Cancel",
                        () => CommitScheme(targetHero, targetSett), null),
                    true);
```

> **Note:** The `cfg.RiskSum` display in the confirmation dialog requires exposing `GetConfig` from `SchemeMinigame`. Add this small public wrapper method to `SchemeMinigame`:
>
> ```csharp
> internal struct PublicConfig { internal int RiskSum; }
> internal static PublicConfig GetPublicConfig(SchemeType type)
> {
>     var c = GetConfig(type);
>     return new PublicConfig { RiskSum = c.RiskSum };
> }
> ```
>
> If you prefer to avoid this, simply remove the `cfg` line and replace `≥{cfg.RiskSum}` with the literal word "target" in the body string.

### Change 3b — Replace `CommitScheme`

Replace the entire method (lines 473–499):

```csharp
        private static void CommitScheme(Hero targetHero, Settlement targetSett)
        {
            try
            {
                if (_selectedDef == null) return;

                if (SchemeSystem.IsHardBlocked(_selectedDef.Type, targetHero, targetSett))
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject("That target is currently blocked — the path is not yet clear."));
                    _selectedDef = null;
                    try { GameMenu.SwitchToMenu("town"); } catch { }
                    return;
                }

                int goldCost = SchemeSystem.ComputeGoldCost(_selectedDef, targetHero, targetSett);
                int infCost  = SchemeSystem.ComputeInfluenceCost(_selectedDef, targetHero, targetSett);

                if (SchemeSystem.PlayerRetaliationActive)
                {
                    goldCost /= 2;
                    infCost  /= 2;
                }

                if (!SchemeSystem.DebugFree)
                {
                    if ((Hero.MainHero?.Gold ?? 0) < goldCost)
                    {
                        MBInformationManager.AddQuickInformation(new TextObject("Not enough gold."));
                        _selectedDef = null;
                        try { GameMenu.SwitchToMenu("town"); } catch { }
                        return;
                    }
                    if ((Hero.MainHero?.Clan?.Influence ?? 0f) < infCost)
                    {
                        MBInformationManager.AddQuickInformation(new TextObject("Not enough influence."));
                        _selectedDef = null;
                        try { GameMenu.SwitchToMenu("town"); } catch { }
                        return;
                    }
                    try { Hero.MainHero.Gold -= goldCost; } catch { }
                    try { if (Hero.MainHero?.Clan != null) Hero.MainHero.Clan.Influence -= infCost; } catch { }
                }

                try { ShiftPlayerTrait(DefaultTraits.Honor,       -1); } catch { }
                try { ShiftPlayerTrait(DefaultTraits.Calculating,  -1); } catch { }
                if (_selectedDef.Type == SchemeType.Assassinate)
                    try { ShiftPlayerTrait(DefaultTraits.Mercy, -1); } catch { }

                var capturedDef  = _selectedDef;
                var capturedHero = targetHero;
                var capturedSett = targetSett;
                _selectedDef = null;

                try { GameMenu.SwitchToMenu("town"); } catch { }
                try { SchemeMinigame.Begin(capturedDef, capturedHero, capturedSett); } catch { }
            }
            catch { }
        }
```

---

## STEP 4 — Verify

1. **Build:** `SchemeOutcome` enum is in `SchemeMinigame.cs`, used by `SchemeSystem.ApplyPlayerSchemeOutcome` — same namespace, no issue.
2. **`ApplySuccess` access:** `ApplyPlayerSchemeOutcome` is inside `SchemeSystem`, so it can call the private `ApplySuccess` directly.
3. **`DefaultSkills.Roguery`:** Used in `SchemeMinigame.ShowRound`. Requires `using TaleWorlds.CampaignSystem.CharacterDevelopment;` — already in the using list.
4. **NPC schemes:** `QueueScheme`, `DailyTick`, `ExecuteScheme`, `ApplySuccess`, `ApplyFailure` — all unchanged.
5. **Save/Load:** No changes needed — minigame is stateless between sessions.

---

## Mechanic Summary for Players

> Draw complication cards to build a running total. Hit your scheme's risk sum and stand — the operation succeeds. Push too far past 21 and you bust: the scheme backfires with consequences specific to what you attempted. Stop before the risk sum and you only lose your investment. Roguery 100+ lets you discard one bad card per run. Roguery 200+ also lets you peek at what the next card will be before deciding.
