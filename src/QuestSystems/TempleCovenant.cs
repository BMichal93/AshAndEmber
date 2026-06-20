// =============================================================================
// ASH AND EMBER — QuestSystems/TempleCovenant.cs
// The Temple's relationship with a player who is NOT one of its members.
//
// Once The Temple has risen (CampaignMapEvents.TryFireTheTemple), it watches
// the player:
//   • A clean-handed player (whisper tier ≤ 1, clan tier ≥ 2) may be offered
//     the COVENANT: a standing pact against the Ashen. While sworn, battle
//     casts cost 1 fewer day of life (the Temple's rites steady the fire),
//     and every few weeks the Temple calls for aid in a strike on the Ashen —
//     answering builds renown and standing, refusing erodes it.
//   • A whisper-heavy mage (tier 3, 75+ whispers) is declared ANATHEMA: the
//     covenant is revoked if sworn, relations with the High Templar collapse,
//     and zealot ambushes periodically bleed the player's column until the
//     whispers fade back below tier 2.
//
// Ticked from CampaignMapEvents.DailyTick; persisted via CampaignBehavior.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static class TempleCovenant
    {
        private const string TempleKingdomId = "vlandia";
        private const string AshenKingdomId  = "ashen_kingdom";

        // Covenant state machine.
        private const int StateNone     = 0; // Temple exists, no pact yet — offer possible
        private const int StateSworn    = 1; // covenant active
        private const int StateClosed   = 2; // declined, broken, or Temple destroyed — no re-offer
        private const int StateAnathema = 3; // hunted while whisper tier stays high

        private static int _state           = StateNone;
        private static int _nextCallDay     = -1;   // next joint-strike call (campaign day)
        private static int _huntCooldownDay = -1;   // next zealot ambush while anathema

        private static readonly Random _rng = new Random();

        public static bool CovenantActive => _state == StateSworn;

        // One-line status for the grimoire ledger.
        public static string LedgerLine()
        {
            switch (_state)
            {
                case StateSworn:    return "  The Temple: covenant sworn — battle workings cost one day less.\n";
                case StateAnathema: return "  The Temple: you are anathema. Their zealots hunt your column.\n";
                default:            return "";
            }
        }

        private static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        private static Kingdom FindTemple()
        {
            try { return Kingdom.All.FirstOrDefault(k => k.StringId == TempleKingdomId && !k.IsEliminated); }
            catch { return null; }
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        public static void DailyTick()
        {
            try
            {
                Kingdom temple = FindTemple();
                if (temple == null)
                {
                    if (_state == StateSworn)
                    {
                        _state = StateClosed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The Temple has fallen. The covenant dies with it.",
                            new Color(0.6f, 0.5f, 0.4f)));
                    }
                    return;
                }

                // Temple members need no covenant — their standing is the kingdom itself.
                if (Hero.MainHero?.Clan?.Kingdom == temple) return;

                // The Ashen are already at war with the Temple; nothing personal remains.
                if (MageKnowledge.IsAshen)
                {
                    if (_state == StateSworn) _state = StateClosed;
                    return;
                }

                int tier = MageKnowledge.IsMage ? MageKnowledge.WhisperTier : 0;

                // ── Anathema: the cold is too loud in you ──────────────────────
                if (tier >= 3)
                {
                    if (_state != StateAnathema)
                        DeclareAnathema(temple, wasSworn: _state == StateSworn);
                    TickZealotHunt();
                    return;
                }

                // ── Redemption: the whispers have faded ────────────────────────
                if (_state == StateAnathema)
                {
                    if (tier <= 1)
                    {
                        _state = StateClosed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Word reaches you: the Temple's hunters have been recalled. Whatever they saw in you, "
                            + "they no longer see. The covenant, however, will not be offered again.",
                            new Color(0.7f, 0.65f, 0.5f)));
                    }
                    return;
                }

                // ── Covenant offer ─────────────────────────────────────────────
                if (_state == StateNone
                    && (Hero.MainHero?.Clan?.Tier ?? 0) >= 2
                    && tier <= 1
                    && _rng.Next(100) < 8
                    && MageKnowledge._deferredInquiry == null)
                {
                    MageKnowledge._deferredInquiry = () => ShowCovenantOffer(temple);
                    return;
                }

                // ── Joint strike calls ─────────────────────────────────────────
                if (_state == StateSworn && _nextCallDay >= 0 && Today() >= _nextCallDay
                    && MageKnowledge._deferredInquiry == null)
                {
                    _nextCallDay = Today() + 21 + _rng.Next(15);
                    MageKnowledge._deferredInquiry = () => ShowStrikeCall(temple);
                }
            }
            catch { }
        }

        // ── Covenant offer ────────────────────────────────────────────────────
        private static void ShowCovenantOffer(Kingdom temple)
        {
            string leaderName = temple?.Leader?.Name?.ToString() ?? "the High Templar";
            InformationManager.ShowInquiry(new InquiryData(
                "The Temple's Covenant",
                $"An envoy of The Temple finds your camp — grey-robed, travel-worn, unarmed. "
                + $"They carry a letter sealed by {leaderName}.\n\n"
                + "\"We have watched you. Your fire burns clean, and the cold has not found purchase in it. "
                + "The Temple offers covenant: stand with us against the Ashen when we call, and our rites "
                + "will steady your fire — every working in battle will cost you one day less of your life.\"\n\n"
                + "The envoy waits. The covenant binds both ways: the Temple will call for aid, "
                + "and an answer will be expected.",
                true, true,
                "Swear the covenant",
                "Decline — your road is your own",
                () =>
                {
                    _state = StateSworn;
                    _nextCallDay = Today() + 15 + _rng.Next(10);
                    try
                    {
                        var leader = FindTemple()?.Leader;
                        if (leader != null && leader.IsAlive && leader != Hero.MainHero)
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leader, 15, false);
                    }
                    catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The covenant is sworn. The Temple's rites settle over your fire like a steady hand — "
                        + "battle workings now cost one day less.",
                        new Color(0.85f, 0.75f, 0.45f)));
                },
                () =>
                {
                    _state = StateClosed;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The envoy bows and departs. The offer will not come again.",
                        new Color(0.6f, 0.6f, 0.6f)));
                }
            ), true, true);
        }

        // ── Joint strike call ─────────────────────────────────────────────────
        private static void ShowStrikeCall(Kingdom temple)
        {
            string leaderName = temple?.Leader?.Name?.ToString() ?? "the High Templar";
            int goldOffer = 800;

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Temple Calls",
                $"A rider in grey reaches you with word from {leaderName}: the Temple strikes at the Ashen "
                + "within the week, and the covenant asks your answer.\n\n"
                + "\"Ride with us, send what you can spare, or stand aside — but know that the cold counts "
                + "those who stand aside.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("ride", "Ride with the strike", null, true,
                        "Your veterans join the templar column. Ashen warbands are bloodied, your renown grows, and the Temple remembers."),
                    new InquiryElement("coin", $"Send coin ({goldOffer} denars)", null, true,
                        "Fund the strike without leaving your road. A smaller mark of faith, but a mark."),
                    new InquiryElement("decline", "Stand aside this time", null, true,
                        "The covenant holds — barely. The Temple notes your absence."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    try
                    {
                        var leader = FindTemple()?.Leader;
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "ride":
                            {
                                int struck = StrikeAshenParties(2, 10, 18);
                                try { if (Hero.MainHero?.Clan != null) Hero.MainHero.Clan.Renown += 50f; } catch { }
                                try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale += 10f; } catch { }
                                if (leader != null && leader.IsAlive && leader != Hero.MainHero)
                                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leader, 10, false); } catch { }
                                InformationManager.DisplayMessage(new InformationMessage(
                                    struck > 0
                                        ? $"You ride with the templars. {struck} Ashen warband{(struck != 1 ? "s were" : " was")} caught on the road and bloodied. "
                                          + "+50 renown. The Temple remembers."
                                        : "You ride with the templars, but the grey columns melted away before the strike could land. "
                                          + "The intent is remembered all the same. +50 renown.",
                                    new Color(0.85f, 0.75f, 0.45f)));
                                break;
                            }
                            case "coin":
                            {
                                if ((Hero.MainHero?.Gold ?? 0) >= goldOffer)
                                {
                                    try { Hero.MainHero.Gold -= goldOffer; } catch { }
                                    try { if (Hero.MainHero?.Clan != null) Hero.MainHero.Clan.Renown += 15f; } catch { }
                                    if (leader != null && leader.IsAlive && leader != Hero.MainHero)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leader, 5, false); } catch { }
                                    InformationManager.DisplayMessage(new InformationMessage(
                                        "Your coin rides where you do not. The strike goes ahead. +15 renown.",
                                        new Color(0.75f, 0.7f, 0.5f)));
                                }
                                else
                                {
                                    InformationManager.DisplayMessage(new InformationMessage(
                                        "You have no coin to spare. The rider leaves with empty hands — the Temple notes it.",
                                        new Color(0.6f, 0.55f, 0.5f)));
                                    if (leader != null && leader.IsAlive && leader != Hero.MainHero)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leader, -5, false); } catch { }
                                }
                                break;
                            }
                            default:
                            {
                                if (leader != null && leader.IsAlive && leader != Hero.MainHero)
                                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leader, -5, false); } catch { }
                                InformationManager.DisplayMessage(new InformationMessage(
                                    "You stand aside. The strike goes on without you. The covenant holds — for now.",
                                    new Color(0.6f, 0.6f, 0.6f)));
                                break;
                            }
                        }
                    }
                    catch { }
                },
                null, "", false), false, true);
        }

        // Wounds soldiers in up to `maxParties` Ashen field parties. Returns parties struck.
        private static int StrikeAshenParties(int maxParties, int minWounds, int maxWounds)
        {
            int struck = 0;
            try
            {
                var targets = MobileParty.All
                    .Where(p => p.IsActive && !p.IsMainParty
                             && p.MapFaction?.StringId == AshenKingdomId
                             && p.MemberRoster != null && p.MemberRoster.TotalRegulars > 0)
                    .OrderBy(_ => _rng.Next())
                    .Take(maxParties)
                    .ToList();

                foreach (var party in targets)
                {
                    int toWound = minWounds + _rng.Next(maxWounds - minWounds + 1);
                    int w = 0;
                    foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        int healthy = e.Number - e.WoundedNumber;
                        int n = Math.Min(healthy, toWound - w);
                        if (n <= 0) continue;
                        try { party.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                        if (w >= toWound) break;
                    }
                    try { party.RecentEventsMorale -= 20f; } catch { }
                    if (w > 0) struck++;
                }
            }
            catch { }
            return struck;
        }

        // ── Anathema ──────────────────────────────────────────────────────────
        private static void DeclareAnathema(Kingdom temple, bool wasSworn)
        {
            _state           = StateAnathema;
            _huntCooldownDay = Today() + 3; // first ambush a few days after the declaration
            try
            {
                var leader = temple?.Leader;
                if (leader != null && leader.IsAlive && leader != Hero.MainHero)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        Hero.MainHero, leader, wasSworn ? -40 : -30, false);
            }
            catch { }

            string body = wasSworn
                ? "A templar courier delivers a single torn page: your name, struck through in grey ink.\n\n"
                  + "\"The covenant is revoked. The cold speaks through your fire now — we have heard it. "
                  + "Until it falls silent, you are anathema to The Temple, and our hunters will treat "
                  + "your column accordingly.\""
                : "A templar courier delivers a single page bearing your name, struck through in grey ink.\n\n"
                  + "\"The Temple has listened to your fire, and what answers is not the flame. "
                  + "You are declared anathema. Our hunters will be watching your roads.\"";

            if (MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Anathema", body, true, false,
                            "Let them watch.", "", () => { }, null));
                    }
                    catch { }
                };
            }
        }

        // Periodic zealot ambush while anathema: a handful of the player's
        // soldiers are wounded. Stops on its own once the whispers fade.
        private static void TickZealotHunt()
        {
            if (Today() < _huntCooldownDay) return;
            _huntCooldownDay = Today() + 12 + _rng.Next(8);
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int toWound = 3 + _rng.Next(6), w = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    int n = Math.Min(healthy, toWound - w);
                    if (n <= 0) continue;
                    try { roster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                    if (w >= toWound) break;
                }
                if (w > 0)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Templar zealots fall on your stragglers at a ford and vanish before the line can turn. "
                        + $"{w} soldier{(w != 1 ? "s are" : " is")} wounded. The anathema stands while the cold clings to you.",
                        new Color(0.75f, 0.55f, 0.35f)));
            }
            catch { }
        }

        // ── Persistence ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            store.SyncData("TPLC_State",    ref _state);
            store.SyncData("TPLC_NextCall", ref _nextCallDay);
            store.SyncData("TPLC_HuntCd",   ref _huntCooldownDay);
        }

        public static void ResetForNewGame()
        {
            _state           = StateNone;
            _nextCallDay     = -1;
            _huntCooldownDay = -1;
        }
    }
}
