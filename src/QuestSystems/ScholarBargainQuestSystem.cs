// =============================================================================
// ASH AND EMBER — QuestSystems/ScholarBargainQuestSystem.cs
// The Scholar's Bargain — a settlement encounter questline. A wandering
// scholar named Ambrose Voss offers the player an edge over his rivals in
// exchange for protection, then escalates from raising a single dead man
// to raising an army of the cold-fire dead. Refuse him, or turn on him, and
// the bargain does not disappear — it simply finds another patron.
//
// Gate:    clan tier ≥ 2, and the town/castle entered or left belongs to the
//          player's own clan.
// Trigger: entering or leaving that town/castle (folded into
//          SettlementEncounters' town enter/leave pools).
//
// ┌─────────────────────────────────────────────────────────────────────┐
// │ Stage 0 (Idle)      initial approach — A / B / C / D                │
// │ Stage 1 (PendingA2) 7d  → breakthrough offer — 1 / 2 / 3             │
// │ Stage 2 (PendingA3) 3d  → first revenant, offer more — 1 / 2 / 3     │
// │ Stage 3 (PendingA4) 7d  → 50/50: Ashen recruiting boon, or overrun   │
// │ Stage 4 (Ended)     terminal — no further triggers                  │
// │ (parallel) B2 countdown, 7-30d → consequence for another lord        │
// └─────────────────────────────────────────────────────────────────────┘
//
// Wiring:
//   SettlementEncounters.Dispatch → EO_ScholarApproach (town enter/leave pool)
//   CampaignBehavior.Ticks     → OnDailyTick     → ScholarBargainQuestSystem.DailyTick()
//   CampaignBehavior.Lifecycle → SyncData        → ScholarBargainQuestSystem.Save()
//   CampaignBehavior.Events    → OnNewGameCreated → ScholarBargainQuestSystem.ResetForNewGame()
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class ScholarBargainQuestSystem
    {
        internal const int StageIdle      = 0;
        internal const int StagePendingA2 = 1;
        internal const int StagePendingA3 = 2;
        internal const int StagePendingA4 = 3;
        internal const int StageEnded     = 4;

        private static int    _stage        = StageIdle;
        private static int    _countdown    = 0;    // days until the current stage's follow-up fires
        private static string _settlementId = null; // settlement the scholar settled in

        private static int _b2Countdown = 0; // days until the "other lord" consequence fires (0 = inactive)

        private static readonly Random _rng = new Random();

        private static readonly Color GoodColor  = new Color(0.55f, 0.80f, 0.45f);
        private static readonly Color DimColor   = new Color(0.65f, 0.60f, 0.52f);
        private static readonly Color BadColor   = new Color(0.75f, 0.35f, 0.28f);

        public static void ResetForNewGame()
        {
            _stage        = StageIdle;
            _countdown    = 0;
            _settlementId = null;
            _b2Countdown  = 0;
        }

        public static void Save(IDataStore store)
        {
            store.SyncData("SBQ_Stage",       ref _stage);
            store.SyncData("SBQ_Countdown",   ref _countdown);
            store.SyncData("SBQ_Settlement",  ref _settlementId);
            store.SyncData("SBQ_B2Countdown", ref _b2Countdown);
        }

        // Gate for SettlementEncounters.Dispatch: clan tier ≥ 2, quest untouched,
        // and the settlement entered/left belongs to the player's own clan.
        internal static bool CanTriggerAt(Settlement s)
        {
            if (_stage != StageIdle || s == null) return false;
            if (!s.IsTown && !s.IsCastle) return false;
            if (s.OwnerClan == null || s.OwnerClan != Hero.MainHero?.Clan) return false;
            return (Hero.MainHero?.Clan?.Tier ?? 0) >= 2;
        }

        public static void DailyTick()
        {
            if (_countdown > 0)
            {
                _countdown--;
                if (_countdown == 0)
                {
                    switch (_stage)
                    {
                        case StagePendingA2: FireBreakthrough();  break;
                        case StagePendingA3: FireFirstRevenant(); break;
                        case StagePendingA4: FireAshenOutcome();  break;
                    }
                }
            }

            if (_b2Countdown > 0)
            {
                _b2Countdown--;
                if (_b2Countdown == 0)
                    FireOtherLordConsequence();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static void Msg(string text, Color c)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch { try { InformationManager.DisplayMessage(new InformationMessage(text, c)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
        }

        private static void ShiftTrait(TraitObject trait, int delta)
        {
            try
            {
                Hero h = Hero.MainHero;
                if (h == null) return;
                int v = h.GetTraitLevel(trait);
                h.SetTraitLevel(trait, Math.Min(2, Math.Max(-2, v + delta)));
                string sign = delta >= 0 ? "+" : "";
                Msg($"({trait.Name} {sign}{delta})", delta >= 0 ? GoodColor : DimColor);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static bool ChangeGold(int amount)
        {
            if (amount < 0 && (Hero.MainHero?.Gold ?? 0) < -amount)
            {
                Msg($"Not enough gold. (Need {-amount}, have {Hero.MainHero?.Gold ?? 0})", BadColor);
                return false;
            }
            try { Hero.MainHero?.ChangeHeroGold(amount); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return true;
        }

        private static void ChangeCrime(float amount)
        {
            try
            {
                var kingdom = Hero.MainHero?.MapFaction as Kingdom;
                if (kingdom != null)
                    ChangeCrimeRatingAction.Apply(kingdom, amount, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static int GetSkill(SkillObject skill)
        {
            try { return Hero.MainHero?.GetSkillValue(skill) ?? 0; }
            catch { return 0; }
        }

        private static float SkillChance(SkillObject skill, float baseChance, float perPoint = 0.003f, float cap = 0.90f)
        {
            int level = GetSkill(skill);
            return Math.Min(cap, baseChance + level * perPoint);
        }

        private static bool SkillRoll(SkillObject skill, float baseChance, float perPoint = 0.003f, float cap = 0.90f)
            => _rng.NextDouble() < SkillChance(skill, baseChance, perPoint, cap);

        private static string OddsLabel(float chance)
        {
            if (chance >= 0.85f) return "very likely";
            if (chance >= 0.68f) return "likely";
            if (chance >= 0.48f) return "even odds";
            if (chance >= 0.32f) return "unlikely";
            return "a long shot";
        }

        private static string SkillHint(SkillObject skill, float baseChance, string outcomeLabel)
        {
            int level = GetSkill(skill);
            float pct  = SkillChance(skill, baseChance) * 100f;
            return $"[{skill.Name} {level}] {outcomeLabel} — {OddsLabel(SkillChance(skill, baseChance))} ({(int)pct}%).";
        }

        private static void StartB2Countdown()
        {
            _b2Countdown = 7 + _rng.Next(24); // 7-30 days
        }
    }
}
