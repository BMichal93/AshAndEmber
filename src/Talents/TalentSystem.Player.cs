// =============================================================================
// ASH AND EMBER — TalentSystem.Player.cs
// Fade state, daily cast budget, talent purchase, kinship limits.
// Partial of TalentSystem (shared static state lives in TalentSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class TalentSystem
    {
        // ── Fade spell state ───────────────────────────────────────────────────
        private static int _fadeDaysRemaining = 0;

        // Conceal/reveal the party. MobileParty.IsVisible is the live API for map
        // stealth (the older IgnoreByOtherParties property no longer exists); hidden
        // parties are not detected or engaged by enemy AI. `conceal == true` hides.
        private static bool TrySetPartyConcealed(MobileParty party, bool conceal)
        {
            try { if (party == null) return false; party.IsVisible = !conceal; return true; }
            catch { return false; }
        }

        /// <summary>Call from the daily tick to count down and clear the Fade effect.</summary>
        public static void DailyFadeTick()
        {
            if (_fadeDaysRemaining <= 0) return;
            _fadeDaysRemaining--;
            if (_fadeDaysRemaining <= 0)
            {
                try { if (MobileParty.MainParty != null) TrySetPartyConcealed(MobileParty.MainParty, false); } catch { }
                Msg("Fade — the ash settles. Your party is visible once more.");
            }
        }

        // ── Daily map cast counter ────────────────────────────────────────────
        private static int _dailyMapCastCount = 0;
        public static int DailyCastCount => _dailyMapCastCount;
        // Escalation softened from ×7 (1 → 7 → 14 → 21): the 2nd map cast cost
        // more than most battle spells, which made map magic read as a trap.
        public static int GetDailyCastCost() => _dailyMapCastCount == 0 ? 1 : _dailyMapCastCount * 4;
        public static void ResetDailyCastCount()
        {
            if (_dailyMapCastCount > 0 && MageKnowledge.IsMage)
                InformationManager.DisplayMessage(new InformationMessage(
                    "Midnight — the toll of your workings resets.", new Color(0.5f, 0.5f, 0.7f)));
            _dailyMapCastCount = 0;
        }

        // ── Player talent tracking ─────────────────────────────────────────────
        private static readonly HashSet<TalentId> _purchased = new HashSet<TalentId>();

        // The six purchasable fire paths. Cost escalates: 1 fp for the first owned,
        // 2 fp for the second, 3 fp for the third, and so on.
        private static readonly TalentId[] _firePathIds =
        {
            TalentId.DarkMage, TalentId.Seer, TalentId.WardKeeper,
            TalentId.Heartfire, TalentId.Pyrelord, TalentId.Ashbinder,
        };

        // Per-discipline escalating pools — independent of fire paths and each other.
        private static readonly TalentId[] _graceTalentIds =
        {
            TalentId.Gracebound, TalentId.KeepingFlame, TalentId.UnbrokenWard, TalentId.EmberCovenant,
        };

        private static readonly TalentId[] _alchemyTalentIds =
        {
            TalentId.AshenAlchemist, TalentId.SteadierHand, TalentId.DeeperSatchel, TalentId.VolatileHarvest,
        };

        private static readonly TalentId[] _natureTalentIds =
        {
            TalentId.Wildsworn, TalentId.NatureLivingRoot, TalentId.NatureStillDraw,
            TalentId.NatureOpenGrip, TalentId.NatureDeepEarth, TalentId.NatureDawnCall,
        };

        public static int GetNextPathCost()
        {
            int owned = _firePathIds.Count(id => _purchased.Contains(id));
            return owned + 1;
        }

        public static int GetNextDisciplineCost(TalentId[] pool)
        {
            int owned = pool.Count(id => _purchased.Contains(id));
            return owned + 1;
        }

        private static bool TryGetDisciplinePool(TalentId id, out TalentId[] pool)
        {
            if (Array.IndexOf(_graceTalentIds,   id) >= 0) { pool = _graceTalentIds;   return true; }
            if (Array.IndexOf(_alchemyTalentIds, id) >= 0) { pool = _alchemyTalentIds; return true; }
            if (Array.IndexOf(_natureTalentIds,  id) >= 0) { pool = _natureTalentIds;  return true; }
            pool = null;
            return false;
        }

        // A talent is owned if it was purchased directly (legacy single-talent
        // saves) OR if the class that bundles it has been purchased.
        public static bool Has(TalentId id)
        {
            if (_purchased.Contains(id)) return true;
            if (_memberToClass.TryGetValue(id, out var owningClass) && _purchased.Contains(owningClass))
                return true;
            return false;
        }

        public static IEnumerable<TalentId> AllPurchased => _purchased;

        // Effective ability count: a class counts for all the talents it bundles,
        // so systems that scale by "how much magic you know" (e.g. Ashen Ruins
        // proficiency) read the same whether a player bought singles or classes.
        public static int PurchasedCount
        {
            get
            {
                int n = 0;
                foreach (var id in _purchased)
                    n += ClassMembers.TryGetValue(id, out var members) ? members.Length : 1;
                return n;
            }
        }

        public static void ResetForNewGame()
        {
            _purchased.Clear();
            _purchased.Add(TalentId.Gift);
            _dailyMapCastCount = 0;
        }

        public static void UnlockAll()
        {
            foreach (TalentId id in Enum.GetValues(typeof(TalentId)))
                _purchased.Add(id);
            InformationManager.DisplayMessage(new InformationMessage(
                "All talents unlocked.",
                new Color(1f, 0.8f, 0.2f)));
        }

        public static int PurchaseCost() => 1;

        // Test seam: add a talent or class to the owned set with no focus-point
        // cost, hero traits, or UI. Mirrors what a purchase does to _purchased,
        // without touching the TaleWorlds runtime, so the bundle logic stays
        // coverable by PureLogicTests.
        public static void GrantClassForTest(TalentId id) => _purchased.Add(id);

        // Grant a talent for free (no focus-point cost). Returns false if already owned.
        public static bool GrantFree(TalentId id, Hero hero)
        {
            if (_purchased.Contains(id)) return false;
            _purchased.Add(id);
            ApplyMemberHooks(id, hero);
            var def = GetDef(id);
            if (def != null)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Talent learned: {def.Name}.", new Color(0.90f, 0.60f, 0.20f)));
            return true;
        }

        public static bool TryPurchase(TalentId id, Hero hero)
        {
            if (_purchased.Contains(id)) return false;
            if (hero == null) return false;

            var defCheck = All.FirstOrDefault(d => d.Id == id);
            if (defCheck?.IsInfo == true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "This cannot be learned.", Color.FromUint(0xFFAAAAAA)));
                return false;
            }
            if (defCheck?.IsConsumable == true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "This cannot be learned — it must be found.", Color.FromUint(0xFFAAAAAA)));
                return false;
            }

            int cost;
            if (defCheck?.Category == TalentCategory.Class && defCheck.FocusCost == 0)
                cost = GetNextPathCost();
            else if (TryGetDisciplinePool(id, out var disciplinePool))
                cost = GetNextDisciplineCost(disciplinePool);
            else
                cost = defCheck?.FocusCost > 0 ? defCheck.FocusCost : PurchaseCost();

            bool spent = false;
            try
            {
                if (hero.HeroDeveloper.UnspentFocusPoints >= cost)
                {
                    hero.HeroDeveloper.UnspentFocusPoints -= cost;
                    spent = true;
                }
            }
            catch { }

            if (!spent)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Not enough focus points. Cost: {cost} point{(cost != 1 ? "s" : "")}.",
                    new Color(0.8f, 0.5f, 0.2f)));
                return false;
            }

            _purchased.Add(id);

            ApplyMemberHooks(id, hero);

            var def = GetDef(id);
            InformationManager.DisplayMessage(new InformationMessage(
                $"You have learned {def.Name}. {def.MechanicDesc}",
                new Color(0.7f, 0.9f, 0.7f)));
            return true;
        }

        // Run the per-talent purchase side-effects for a newly learned talent or
        // class. Buying a class fires the hooks of every member it bundles, so
        // Reaper darkens you (Reap) and Heartfire warms mage relations (Kinship)
        // exactly as the single talents once did.
        private static void ApplyMemberHooks(TalentId purchased, Hero hero)
        {
            IEnumerable<TalentId> ids = ClassMembers.TryGetValue(purchased, out var members)
                ? members
                : new[] { purchased };
            foreach (var t in ids)
            {
                if (t == TalentId.Camaraderie) ApplyCamaraderie(hero);
                if (t == TalentId.Reap)        ApplyReapTraits(hero);
            }
        }

        private static void ApplyReapTraits(Hero hero)
        {
            try
            {
                int mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
                if (mercy > -3) hero.SetTraitLevel(DefaultTraits.Mercy, mercy - 1);
                int honor = hero.GetTraitLevel(DefaultTraits.Honor);
                if (honor > -3) hero.SetTraitLevel(DefaultTraits.Honor, honor - 1);
                if (hero.MapFaction is Kingdom k)
                    try { ChangeCrimeRatingAction.Apply(k, 30f, true); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    "The fire darkens with hunger. Those who witness what you do will remember.",
                    new Color(0.8f, 0.4f, 0.2f)));
            }
            catch { }
        }

        private static void ApplyCamaraderie(Hero player)
        {
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (h == player || !ColourLordRegistry.IsColourLord(h)) continue;
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, h, 10, false); } catch { }
                }
            }
            catch { }
        }

        public static void EnforceCaramaraderieLimits(Hero player, Hero mage)
        {
            if (!Has(TalentId.Camaraderie)) return;
            try
            {
                int rel = CharacterRelationManager.GetHeroRelation(player, mage);
                if (rel < 0) CharacterRelationManager.SetHeroRelation(player, mage, 0);
            }
            catch { }
        }

        // Enforce Kinship floor for all living mage lords — call from daily tick.
        public static void EnforceKinship()
        {
            if (!Has(TalentId.Camaraderie) || Hero.MainHero == null) return;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes)
                {
                    if (h == Hero.MainHero || !h.IsAlive || !ColourLordRegistry.IsColourLord(h)) continue;
                    EnforceCaramaraderieLimits(Hero.MainHero, h);
                }
            }
            catch { }
        }

        public static TalentDef GetDef(TalentId id) =>
            All.FirstOrDefault(d => d.Id == id) ?? All[0];

    }
}
