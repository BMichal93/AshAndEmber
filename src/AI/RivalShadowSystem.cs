// =============================================================================
// ASH AND EMBER — AI/RivalShadowSystem.cs
// One Ashen lord is designated as the player's Shadow at campaign start.
// The Shadow schemes against player settlements every 14 days and eventually
// rides out alone to end the distance between you.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class RivalShadowSystem
    {
        private static string _shadowLordId      = null;
        private static int    _schemeCount       = 0;
        private static int    _schemeTimer       = 14;
        private static bool   _shadowDefeated    = false;
        private static bool   _duelPending       = false;
        private static bool   _shadowHealPending = false;
        private static readonly Random _rng = new Random();

        public static bool   HasShadow          => _shadowLordId != null && !_shadowDefeated;
        public static string ShadowLordId       => _shadowLordId;
        public static bool   ShadowDefeated     => _shadowDefeated;

        // ColourLordAI consumes this once on the Shadow's next battle cast.
        public static bool ConsumeShadowHealPending()
        {
            if (!_shadowHealPending) return false;
            _shadowHealPending = false;
            return true;
        }

        public static void ResetForNewGame()
        {
            _shadowLordId      = null;
            _schemeCount       = 0;
            _schemeTimer       = 14;
            _shadowDefeated    = false;
            _duelPending       = false;
            _shadowHealPending = false;
        }

        // ── Designation ───────────────────────────────────────────────────────
        // Called from DailyTick once Ashen lords exist in the world.
        // The cold ignores nobodies: no Shadow is assigned until the player's
        // clan reaches tier 3 — by then they are worth watching.
        public static void TryDesignateShadow()
        {
            if (_shadowLordId != null || _shadowDefeated) return;
            if (!MageKnowledge.IsMage) return;
            if (Hero.MainHero?.Clan == null || Hero.MainHero.Clan.Tier < 3) return;

            try
            {
                var ashenLords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && ColourLordRegistry.IsAshenLord(h))
                    .ToList();
                if (ashenLords.Count == 0) return;

                Hero shadow = ashenLords[_rng.Next(ashenLords.Count)];
                _shadowLordId = shadow.StringId;
                _schemeTimer  = 14 + _rng.Next(7);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"You sense a cold fire fixed on you — {shadow.Name} marks you as their quarry.",
                    new Color(0.38f, 0.50f, 0.75f)));
                MageKnowledge._deferredInquiry = () => ShowDesignationEvent(shadow.Name?.ToString() ?? "an Ashen lord");
            }
            catch { }
        }

        private static void ShowDesignationEvent(string shadowName)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "A Cold Attention",
                $"Your name has begun to travel. Banners know it; courts repeat it.\n\n" +
                $"Something else has heard it too. In the north, where the fire went out, " +
                $"{shadowName} has turned their face toward you. The dark forces of the Ashen " +
                $"have noticed you — and one of them has made you their personal concern.\n\n" +
                $"Expect their hand in your affairs.",
                true, false, "I am ready", "",
                null, null), true);
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        public static void DailyTick()
        {
            if (_shadowLordId == null || _shadowDefeated) return;
            if (!MageKnowledge.IsMage) return;
            if (_duelPending) return;

            // Check if Shadow died by other means
            try
            {
                bool alive = Hero.AllAliveHeroes.Any(h => h.StringId == _shadowLordId && h.IsAlive);
                if (!alive) { _shadowLordId = null; return; }
            }
            catch { return; }

            _schemeTimer--;
            if (_schemeTimer > 0) return;
            _schemeTimer = 14 + _rng.Next(7);
            _schemeCount++;

            ExecuteShadowScheme();

            if (_schemeCount >= 5)
            {
                // Queue unconditionally: DailyTick pauses while _duelPending, so a
                // busy dialog day here used to lose the duel forever.
                _duelPending = true;
                MageKnowledge._deferredInquiry = ShowDuelEvent;
            }
        }

        // ── Scheme ────────────────────────────────────────────────────────────
        private static void ExecuteShadowScheme()
        {
            try
            {
                Hero shadow = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _shadowLordId);
                if (shadow == null) return;

                string shadowName = shadow.Name?.ToString() ?? "the Shadow";

                // Try to harm a player-owned settlement
                Settlement target = null;
                if (Hero.MainHero?.Clan != null)
                {
                    var owned = Settlement.All
                        .Where(s => (s.IsTown || s.IsCastle) && s.Town != null
                                 && s.OwnerClan == Hero.MainHero.Clan)
                        .ToList();
                    if (owned.Count > 0)
                        target = owned[_rng.Next(owned.Count)];
                }

                string progress = $"({_schemeCount}/5 schemes)";
                if (target?.Town != null)
                {
                    if (_rng.Next(2) == 0)
                    {
                        target.Town.Loyalty = Math.Max(0f, target.Town.Loyalty - 10f);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{shadowName}'s cold schemes reach {target.Name} — loyalty erodes. {progress}",
                            new Color(0.38f, 0.50f, 0.75f)));
                    }
                    else
                    {
                        target.Town.Security = Math.Max(0f, target.Town.Security - 15f);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{shadowName}'s shadow spreads through {target.Name} — fear takes root. {progress}",
                            new Color(0.38f, 0.50f, 0.75f)));
                    }
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{shadowName} moves against you from the dark. {progress}",
                        new Color(0.38f, 0.50f, 0.75f)));
                }
            }
            catch { }
        }

        // ── Duel event ────────────────────────────────────────────────────────
        private static void ShowDuelEvent()
        {
            Hero shadow = null;
            try { shadow = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _shadowLordId && h.IsAlive); }
            catch { }

            if (shadow == null)
            {
                _shadowLordId = null;
                _shadowDefeated = true;
                _duelPending = false;
                return;
            }

            string shadowName = shadow.Name?.ToString() ?? "the Shadow";
            int lSkill = 0, aSkill = 0;
            try { lSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Leadership) ?? 0; } catch { }
            try { aSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics)  ?? 0; } catch { }
            int lPct = Math.Min(85, (int)(lSkill * 0.4f));
            int aPct = Math.Min(85, (int)(aSkill * 0.4f));

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Shadow Approaches",
                $"{shadowName} rides alone to meet you — no host, no herald. " +
                $"The cold that clings to them is familiar now. " +
                $"Five times you have felt their hand in your affairs. They have come to end the distance between you.\n\n" +
                $"They carry a fire that does not warm. You carry one that does not go out.",
                new List<InquiryElement>
                {
                    new InquiryElement("lead", "Face them — through will and command.", null, true,
                        $"Leadership test. Skill: {lSkill}. Success chance: {lPct}%."),
                    new InquiryElement("body", "Face them — through force and endurance.", null, true,
                        $"Athletics test. Skill: {aSkill}. Success chance: {aPct}%."),
                    new InquiryElement("flee", "Not today. Withdraw.", null, true,
                        "The shadow continues. −30 renown."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    string choice = chosen?[0]?.Identifier as string ?? "flee";
                    float lChance = Math.Min(0.85f, lSkill * 0.004f);
                    float aChance = Math.Min(0.85f, aSkill * 0.004f);

                    if (choice == "lead")
                    {
                        if (_rng.NextDouble() < lChance) OnShadowDefeated(shadowName);
                        else OnDuelLoss(shadowName, "Your will bent under the cold.");
                    }
                    else if (choice == "body")
                    {
                        if (_rng.NextDouble() < aChance) OnShadowDefeated(shadowName);
                        else OnDuelLoss(shadowName, "Your body gave out before theirs.");
                    }
                    else
                    {
                        _duelPending = false;
                        _schemeTimer = 14 + _rng.Next(7);
                        try
                        {
                            if (Hero.MainHero?.Clan != null)
                                Hero.MainHero.Clan.Renown = Math.Max(0f, Hero.MainHero.Clan.Renown - 30f);
                        }
                        catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"You ride away. {shadowName} watches from the road. −30 renown.",
                            new Color(0.5f, 0.4f, 0.5f)));
                    }
                },
                null, "", false
            ), false, true);
        }

        private static void OnShadowDefeated(string shadowName)
        {
            _shadowDefeated = true;
            _duelPending    = false;
            _shadowLordId   = null;

            try
            {
                if (Hero.MainHero?.HeroDeveloper != null)
                    Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 5;
            }
            catch { }

            try
            {
                if (Hero.MainHero?.Clan != null)
                    Hero.MainHero.Clan.Renown += 200f;
            }
            catch { }

            // Nearest Ashen lord converts to regular mage
            try
            {
                if (MobileParty.MainParty != null)
                {
                    Vec2 pos = MobileParty.MainParty.GetPosition2D;
                    Hero nearest = Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h.IsAlive && ColourLordRegistry.IsAshenLord(h)
                                 && h.PartyBelongedTo != null)
                        .OrderBy(h => (h.PartyBelongedTo.GetPosition2D - pos).Length)
                        .FirstOrDefault();
                    if (nearest != null)
                    {
                        ColourLordRegistry.SetAshen(nearest, false);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{nearest.Name} — the cold breaks in them. Something warmer stirs.",
                            new Color(0.9f, 0.6f, 0.3f)));
                    }
                }
            }
            catch { }

            InformationManager.DisplayMessage(new InformationMessage(
                $"{shadowName} falls back — spent, not destroyed. The cold recedes. +5 focus, +200 renown.",
                new Color(0.9f, 0.7f, 0.3f)));
        }

        private static void OnDuelLoss(string shadowName, string reason)
        {
            _duelPending       = false;
            _shadowHealPending = true;
            _schemeCount       = 0;
            _schemeTimer       = 21 + _rng.Next(7);
            try { AgingSystem.AgeHero(Hero.MainHero, 5); } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{reason} {shadowName} departs — satisfied, for now. −5 days.",
                new Color(0.38f, 0.50f, 0.75f)));
        }

        // ── Hero died ─────────────────────────────────────────────────────────
        public static void OnHeroKilled(Hero hero)
        {
            if (hero?.StringId == _shadowLordId)
            {
                _shadowLordId   = null;
                _shadowDefeated = true;
                _duelPending    = false;
            }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            store.SyncData("RS_ShadowId",      ref _shadowLordId);
            store.SyncData("RS_SchemeCount",   ref _schemeCount);
            store.SyncData("RS_SchemeTimer",   ref _schemeTimer);
            store.SyncData("RS_Defeated",      ref _shadowDefeated);
            store.SyncData("RS_DuelPending",   ref _duelPending);
            store.SyncData("RS_HealPending",   ref _shadowHealPending);
        }
    }
}
