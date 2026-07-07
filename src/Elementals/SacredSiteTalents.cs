// =============================================================================
// ASH AND EMBER — Elementals/SacredSiteTalents.cs
//
// The old ways' learnable craft — a small talent list for the sacred sites,
// studied with focus points, mirroring the Litany of Devotions (Grace) and
// the lapidary's craft (Crystals):
//
//   Deeper Binding — the working takes more readily (+20% binding success).
//   Sparing Rite   — a failed binding returns its Iron Ore and Charcoal.
//   Kindred Ease   — halves the daily upkeep a bound Kindled costs you.
//
// Persisted in the save by SacredSitesCampaignBehavior. Bought at a Standing
// Stones site ("Study the Old Ways"), each costing one focus point more than
// the last.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public enum SacredSiteTalentId
    {
        DeeperBinding = 0,  // +20% binding success chance
        SparingRite   = 1,  // materials refunded on a failed binding
        KindredEase   = 2,  // halves daily Kindled upkeep
    }

    public static class SacredSiteTalents
    {
        private static readonly SacredSiteTalentId[] _all =
            { SacredSiteTalentId.DeeperBinding, SacredSiteTalentId.SparingRite, SacredSiteTalentId.KindredEase };

        private static readonly HashSet<SacredSiteTalentId> _owned = new HashSet<SacredSiteTalentId>();

        // ── The hooks the sacred-site system reads ───────────────────────────────
        public static bool  Has(SacredSiteTalentId id) => _owned.Contains(id);
        public static float BindingOddsBonus => Has(SacredSiteTalentId.DeeperBinding) ? 0.20f : 0f;
        public static bool  RefundsOnFailure => Has(SacredSiteTalentId.SparingRite);
        public static float UpkeepMult       => Has(SacredSiteTalentId.KindredEase) ? 0.5f : 1f;

        public static int OwnedCount => _owned.Count;

        // Shared gentle curve (1,1,2,2,2,3,…), as every talent tree uses.
        public static int NextCost() => TalentCostCurve.Cost(OwnedCount);

        public static bool TryLearn(SacredSiteTalentId id, out string message)
        {
            message = "";
            if (Has(id)) { message = $"You have already learned {Name(id)}."; return false; }
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
            message = $"You learn {Name(id)}. ({cost} focus point{(cost != 1 ? "s" : "")})";
            return true;
        }

        // ── The Standing Stones' study menu ──────────────────────────────────────
        public static void ShowCodex()
        {
            int have = 0;
            try { have = Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            int cost = NextCost();

            var elements = new List<InquiryElement>();
            foreach (var id in _all)
            {
                bool known = Has(id);
                elements.Add(new InquiryElement((int)id,
                    $"{Name(id)}  —  {(known ? "[known]" : $"{cost} fp")}", null, !known, Effect(id)));
            }

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"Study the Old Ways   [Focus: {have}]",
                    $"The stones do not teach quickly, but they do not forget a listener either. The next lesson " +
                    $"costs {cost} focus point{(cost != 1 ? "s" : "")}. Choose what to learn.",
                    elements, true, 1, 1, "Study", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var id = (SacredSiteTalentId)(int)chosen[0].Identifier;
                        if (TryLearn(id, out string msg))
                            InformationManager.DisplayMessage(new InformationMessage(msg, Glow));
                        else
                            InformationManager.DisplayMessage(new InformationMessage(msg, Dim));
                    },
                    null, "", false), false, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static string Name(SacredSiteTalentId id)
        {
            switch (id)
            {
                case SacredSiteTalentId.DeeperBinding: return "Deeper Binding";
                case SacredSiteTalentId.SparingRite:   return "Sparing Rite";
                default:                                return "Kindred Ease";
            }
        }

        public static string Effect(SacredSiteTalentId id)
        {
            switch (id)
            {
                case SacredSiteTalentId.DeeperBinding: return "Deeper Binding — you have learned to listen properly; a sacred-site binding takes more readily (+20% success chance).";
                case SacredSiteTalentId.SparingRite:   return "Sparing Rite — a failed binding is not wasted; the Iron Ore and Charcoal are returned to you.";
                default:                                return "Kindred Ease — the old debt sits lighter on you; every Kindled you have bound costs half its usual upkeep.";
            }
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        public static void ResetForNewGame() => _owned.Clear();

        public static void Save(IDataStore store)
        {
            try
            {
                var owned = _owned.Select(t => (int)t).ToList();
                store.SyncData("SACRED_Talents", ref owned);
                if (owned != null)
                {
                    _owned.Clear();
                    foreach (int i in owned) _owned.Add((SacredSiteTalentId)i);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static readonly Color Glow = new Color(0.55f, 0.85f, 0.6f);
        private static readonly Color Dim  = new Color(0.7f, 0.68f, 0.6f);
    }
}
