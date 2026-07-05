// =============================================================================
// ASH AND EMBER — Miracles/MiracleTalents.cs
//
// The deeper devotions — a learnable talent list for the Grace-faithful, the
// mirror of the mage's Codex. These do NOT cast miracles; they REFINE the
// miracles you already carry. Five are bound to the five virtues that grant
// your prayers (you may study a virtue's devotion only once that virtue stands
// at +1 or higher); one, Abundant Grace, deepens the well itself.
//
// Studied with focus points on the campaign map (Left Shift + L opens the
// Litany of Devotions). Each costs one point more than the last. Persisted in
// the save alongside the Grace counter.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public enum MiracleTalentId
    {
        MercyDeepMending  = 0,  // Mercy prayers mend far more
        ValorUnbroken     = 1,  // Valour prayers give more morale and longer speed
        HonorIronOath     = 2,  // Honour prayers bind harder (ward, oath)
        GenerosityFullHand= 3,  // Generosity prayers give more
        CalculatingClarity= 4,  // Calculating prayers strike/see further
        AbundantGrace     = 5,  // the well holds more Grace
    }

    public static class MiracleTalents
    {
        private static readonly MiracleTalentId[] _all =
        {
            MiracleTalentId.MercyDeepMending, MiracleTalentId.ValorUnbroken,
            MiracleTalentId.HonorIronOath, MiracleTalentId.GenerosityFullHand,
            MiracleTalentId.CalculatingClarity, MiracleTalentId.AbundantGrace,
        };

        private static readonly HashSet<MiracleTalentId> _owned = new HashSet<MiracleTalentId>();

        // A learned virtue-devotion lifts that virtue's prayers by this much.
        private const float TraitTalentPower = 1.40f;
        // Abundant Grace deepens the well by this many points.
        public  const int   AbundantGraceBonus = 5;

        // ── State ───────────────────────────────────────────────────────────────
        public static bool Has(MiracleTalentId id) => _owned.Contains(id);
        public static int  OwnedCount => _owned.Count;

        // The virtue a devotion is bound to (null for the general Abundant Grace).
        private static GraceTrait? TraitOf(MiracleTalentId id)
        {
            switch (id)
            {
                case MiracleTalentId.MercyDeepMending:   return GraceTrait.Mercy;
                case MiracleTalentId.ValorUnbroken:      return GraceTrait.Valor;
                case MiracleTalentId.HonorIronOath:      return GraceTrait.Honor;
                case MiracleTalentId.GenerosityFullHand: return GraceTrait.Generosity;
                case MiracleTalentId.CalculatingClarity: return GraceTrait.Calculating;
                default:                                 return null;
            }
        }

        private static MiracleTalentId? TalentFor(GraceTrait t)
        {
            switch (t)
            {
                case GraceTrait.Mercy:      return MiracleTalentId.MercyDeepMending;
                case GraceTrait.Valor:      return MiracleTalentId.ValorUnbroken;
                case GraceTrait.Honor:      return MiracleTalentId.HonorIronOath;
                case GraceTrait.Generosity: return MiracleTalentId.GenerosityFullHand;
                case GraceTrait.Calculating:return MiracleTalentId.CalculatingClarity;
                default:                    return null;
            }
        }

        // ── The multipliers the effects read ─────────────────────────────────────
        // A virtue's prayers loose at full strength once its devotion is learned.
        public static float TraitPower(GraceTrait t)
        {
            var id = TalentFor(t);
            return id.HasValue && Has(id.Value) ? TraitTalentPower : 1.0f;
        }

        public static int GraceCapBonus => Has(MiracleTalentId.AbundantGrace) ? AbundantGraceBonus : 0;

        // ── Cost / learning ──────────────────────────────────────────────────────
        // Shared gentle curve (1,1,2,2,2,3,…), as every talent tree uses.
        public static int NextCost() => TalentCostCurve.Cost(OwnedCount);

        private static bool VirtueMet(MiracleTalentId id)
        {
            var t = TraitOf(id);
            if (!t.HasValue) return true; // Abundant Grace has no virtue gate
            try
            {
                int lvl = Hero.MainHero?.GetTraitLevel(MiracleEffects.TraitObjectOf(t.Value)) ?? 0;
                return MiracleMath.MeetsTraitGate(lvl);
            }
            catch { return false; }
        }

        public static bool TryLearn(MiracleTalentId id, out string message)
        {
            message = "";
            if (Has(id)) { message = $"You have already taken {Name(id)}."; return false; }
            if (!VirtueMet(id))
            {
                message = $"{Name(id)} is closed to you — you are not yet {TraitName(id)} enough (that virtue must stand at +1).";
                return false;
            }
            int cost = NextCost();
            var hero = Hero.MainHero;
            int have = 0;
            try { have = hero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (have < cost)
            {
                message = $"{Name(id)} asks {cost} focus point{(cost != 1 ? "s" : "")}; you have {have}.";
                return false;
            }
            try { hero.HeroDeveloper.UnspentFocusPoints -= cost; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _owned.Add(id);
            message = $"You take {Name(id)} into your devotions. ({cost} focus point{(cost != 1 ? "s" : "")})";
            return true;
        }

        // ── The Litany of Devotions (learning menu) ──────────────────────────────
        public static void ShowCodex()
        {
            int have = 0;
            try { have = Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            int cost = NextCost();

            var elements = new List<InquiryElement>();
            foreach (var id in _all)
            {
                bool known = Has(id);
                bool met   = VirtueMet(id);
                string tail = known ? "[taken]" : met ? $"{cost} fp" : $"needs {TraitName(id)} +1";
                elements.Add(new InquiryElement((int)id, $"{Name(id)}  —  {tail}", null, !known && met, Effect(id)));
            }

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"The Litany of Devotions   [Focus: {have}]",
                    "The light rewards the studied as well as the virtuous. Each devotion refines the " +
                    $"prayers a virtue already grants you; the next costs {cost} focus point{(cost != 1 ? "s" : "")}. " +
                    "Choose what to deepen.",
                    elements, true, 1, 1, "Study", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var id = (MiracleTalentId)(int)chosen[0].Identifier;
                        if (TryLearn(id, out string msg))
                            InformationManager.DisplayMessage(new InformationMessage(msg, Gold));
                        else
                            InformationManager.DisplayMessage(new InformationMessage(msg, Dim));
                    },
                    null, "", false), false, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Labels ────────────────────────────────────────────────────────────────
        public static string Name(MiracleTalentId id)
        {
            switch (id)
            {
                case MiracleTalentId.MercyDeepMending:   return "Deeper Mending";
                case MiracleTalentId.ValorUnbroken:      return "The Unbroken";
                case MiracleTalentId.HonorIronOath:      return "The Iron Oath";
                case MiracleTalentId.GenerosityFullHand: return "The Fuller Hand";
                case MiracleTalentId.CalculatingClarity: return "Clearer Sight";
                default:                                 return "Abundant Grace";
            }
        }

        private static string TraitName(MiracleTalentId id)
        {
            var t = TraitOf(id);
            if (!t.HasValue) return "";
            switch (t.Value)
            {
                case GraceTrait.Mercy:      return "Merciful";
                case GraceTrait.Valor:      return "Valorous";
                case GraceTrait.Honor:      return "Honourable";
                case GraceTrait.Generosity: return "Generous";
                default:                    return "Calculating";
            }
        }

        public static string Effect(MiracleTalentId id)
        {
            switch (id)
            {
                case MiracleTalentId.MercyDeepMending:   return "Deeper Mending (Mercy) — Radiant Mending and The Mending Road close far more of every wound.";
                case MiracleTalentId.ValorUnbroken:      return "The Unbroken (Valour) — Light of Valour and The Long March give greater heart and swifter feet.";
                case MiracleTalentId.HonorIronOath:      return "The Iron Oath (Honour) — the Aegis returns more damage as healing, and the Sworn Word binds harder.";
                case MiracleTalentId.GenerosityFullHand: return "The Fuller Hand (Generosity) — Shared Light and The Open Hand give more freely.";
                case MiracleTalentId.CalculatingClarity: return "Clearer Sight (Calculating) — the Pyre falls heavier and Far-Sight reaches further.";
                default:                                 return $"Abundant Grace — the well holds {AbundantGraceBonus} more Grace against the day you need it.";
            }
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        public static void ResetForNewGame() => _owned.Clear();

        public static void Save(IDataStore store)
        {
            try
            {
                var owned = _owned.Select(t => (int)t).ToList();
                store.SyncData("MIRACLE_Talents", ref owned);
                if (owned != null)
                {
                    _owned.Clear();
                    foreach (int i in owned) _owned.Add((MiracleTalentId)i);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static readonly Color Gold = new Color(1.0f, 0.9f, 0.5f);
        private static readonly Color Dim  = new Color(0.7f, 0.68f, 0.6f);
    }
}
