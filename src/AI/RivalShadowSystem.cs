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
        private static int    _lastLetterScheme  = 0;
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
            _lastLetterScheme  = 0;
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

                // Try to find a player-owned settlement for settlement-targeted schemes
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

                // 5 scheme types: 0–1 settlement harm, 2 assassination attempt, 3 stolen shipment, 4 dead informant
                int schemeType = _rng.Next(5);
                if (target?.Town == null && schemeType <= 1) schemeType = 3; // no settlement → fallback

                switch (schemeType)
                {
                    case 0:
                        target.Town.Loyalty = Math.Max(0f, target.Town.Loyalty - 10f);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{shadowName}'s cold schemes reach {target.Name} — loyalty erodes. {progress}",
                            new Color(0.38f, 0.50f, 0.75f)));
                        break;

                    case 1:
                        target.Town.Security = Math.Max(0f, target.Town.Security - 15f);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{shadowName}'s shadow spreads through {target.Name} — fear takes root. {progress}",
                            new Color(0.38f, 0.50f, 0.75f)));
                        break;

                    case 2:
                        // Failed assassination — dramatic enough for a deferred inquiry
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{shadowName}'s blade found your outriders instead of you. One dead. A message left in the wound. {progress}",
                            new Color(0.38f, 0.50f, 0.75f)));
                        try { AgingSystem.AgeHero(Hero.MainHero, 1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        string sName = shadowName;
                        MageKnowledge._deferredInquiry = () =>
                        {
                            try
                            {
                                InformationManager.ShowInquiry(new InquiryData(
                                    "The Knife That Missed",
                                    $"Your outriders found him in a ditch three leagues back — one of your own men, a knife in his chest. Not his knife.\n\n" +
                                    $"{sName}'s work. The blade was meant for you. It arrived early.\n\n" +
                                    $"They are not far behind.",
                                    true, false, "I expected worse.", null,
                                    () => { }, null));
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        };
                        break;

                    case 3:
                        // Stolen shipment — takes a share of the player's gold
                        int goldLost = Math.Min(500, (Hero.MainHero?.Gold ?? 0) / 5);
                        if (goldLost > 50)
                        {
                            try { Hero.MainHero?.ChangeHeroGold(-goldLost); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"A supply cart never arrived. {shadowName}'s hand was on every thief you found. (-{goldLost} gold) {progress}",
                                new Color(0.38f, 0.50f, 0.75f)));
                        }
                        else
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{shadowName} reaches into your supply lines. Nothing of value — this time. {progress}",
                                new Color(0.38f, 0.50f, 0.75f)));
                        }
                        break;

                    case 4:
                        // Dead informant — a contact goes silent
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"One of your contacts stopped writing. When your man found them, the door was open and the hearth cold. {shadowName} reads your correspondence. {progress}",
                            new Color(0.38f, 0.50f, 0.75f)));
                        break;
                }

                if (_schemeCount > _lastLetterScheme && _schemeCount <= 4)
                {
                    _lastLetterScheme = _schemeCount;
                    string capturedName  = shadowName;
                    int    capturedCount = _schemeCount;
                    MageKnowledge._deferredInquiry = () => ShowShadowLetter(capturedName, capturedCount);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ShowShadowLetter(string shadowName, int letterNum)
        {
            try
            {
                string title, body;
                switch (letterNum)
                {
                    case 1:
                        title = "An Unsigned Letter";
                        body  = "Parchment left on your camp table overnight. You find it before your men do. The handwriting is deliberate — someone who chose every stroke carefully.\n\n" +
                                "\"You carry a fire not given to you. The one who gave it is gone. We are watching to see which you choose.\"\n\n" +
                                "Nothing else. No name.";
                        break;
                    case 2:
                        title = "A Second Letter";
                        body  = "Left inside your saddlebag. The parchment smells of winter — not weather, but something older and colder than weather.\n\n" +
                                "\"You have felt our hand twice. We have been considerate. There are terms — when you are ready to listen.\"\n\n" +
                                "\"You are not ready yet.\"";
                        break;
                    case 3:
                        title = "The Third Letter";
                        body  = $"Pinned to the door of your room in a town you only decided to visit that morning. {shadowName} followed you inside walls.\n\n" +
                                "\"We know who stands beside you. We know which faces you trust and which you merely use. We know which of them know what you carry, and which of them only suspect.\"\n\n" +
                                "\"The next move will not be aimed at wood and stone.\"";
                        break;
                    default:
                        title = "The Last Letter";
                        body  = $"You find the words written in charcoal on the inside of your tent. No one entered. No one saw anything.\n\n" +
                                $"\"We are done writing. Four times we shaped your world and left our mark where you would find it. {shadowName} rides now — not to scheme, not to watch.\"\n\n" +
                                "\"The Shadow comes to meet the fire. Prepare yourself. Or don't. It makes less difference than you think.\"";
                        break;
                }

                InformationManager.ShowInquiry(new InquiryData(
                    title, body, true, false, "Set it aside.", null, () => { }, null), true, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Duel event ────────────────────────────────────────────────────────
        private static void ShowDuelEvent()
        {
            Hero shadow = null;
            try { shadow = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _shadowLordId && h.IsAlive); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (shadow == null)
            {
                _shadowLordId = null;
                _shadowDefeated = true;
                _duelPending = false;
                return;
            }

            string shadowName = shadow.Name?.ToString() ?? "the Shadow";
            int lSkill = 0, aSkill = 0;
            try { lSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Leadership) ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { aSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics)  ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        try { ClanRenown.Lose(Hero.MainHero?.Clan, 30f); }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try { ClanRenown.Gain(Hero.MainHero?.Clan, 200f); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
            try { AgingSystem.AgeHero(Hero.MainHero, 5); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            store.SyncData("RS_LastLetter",    ref _lastLetterScheme);
        }
    }
}
