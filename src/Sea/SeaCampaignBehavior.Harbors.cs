// =============================================================================
// ASH AND EMBER — Sea/SeaCampaignBehavior.Harbors.cs
// Harbor menus and destination pickers.
// Partial of SeaCampaignBehavior (shared static state lives in SeaCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SeaCampaignBehavior
    {
        // ── Menu registration ──────────────────────────────────────────────────
        private static void RegisterHarborMenus(CampaignGameStarter starter)
        {
            // Entry in town menu
            try
            {
                starter.AddGameMenuOption("town", "sea_harbor_enter", "{SEA_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (_ports.Count < 2 || !IsPort(Settlement.CurrentSettlement)) return false;
                            MBTextManager.SetTextVariable("SEA_ENTER_TEXT", "Visit the harbor");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("sea_harbor"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Harbor menu header
            try
            {
                starter.AddGameMenu("sea_harbor", "{SEA_HARBOR_HEADER}", args =>
                {
                    try
                    {
                        string ventureNote = "";
                        if (_ventures.Count > 0)
                            ventureNote = "  " + string.Join("  ", _ventures.Select(v =>
                                $"[Venture to {v.DestName}: {v.DaysLeft} day(s) out, {v.Invested} denars{(v.Blessed ? ", blessed" : "")}]"));
                        string windNote = _emberwindCalled
                            ? "  [The Emberwind is called — the next crossing will be swift and stormless]"
                            : (_stillWatersCalled ? "  [The waters are stilled — the next crossing will run swift and calm]" : "");
                        string portName = Settlement.CurrentSettlement?.Name?.ToString() ?? "";
                        string baseDesc = _harborDesc.TryGetValue(portName, out string pd) ? pd :
                            "The harbor. Gulls argue over fish guts, ropes creak against the tide, and captains weigh your purse from across the quay.";
                        MBTextManager.SetTextVariable("SEA_HARBOR_HEADER", baseDesc + windNote + ventureNote);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Charter passage ─────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_charter", "{SEA_CHARTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            MBTextManager.SetTextVariable("SEA_CHARTER_TEXT",
                                "Charter passage to another port" +
                                (_emberwindCalled ? " (Emberwind called)" :
                                 _stillWatersCalled ? " (Waters stilled)" : ""));
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args => ShowDestinationPicker(forTrade: false));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Fund a trade venture ────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_venture", "{SEA_VENTURE_TEXT}",
                    args =>
                    {
                        try
                        {
                            string full = _ventures.Count >= SeaMath.MaxActiveVentures
                                ? $"  [All your factors are at sea — {_ventures.Count} venture(s) out]" : "";
                            if (full.Length > 0) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("SEA_VENTURE_TEXT", "Fund a trade venture" + full);
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args => ShowDestinationPicker(forTrade: true));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Call the Emberwind (mages) ──────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_emberwind", "{SEA_WIND_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!MageKnowledge.IsMage) return false;
                            if (_emberwindCalled) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("SEA_WIND_TEXT",
                                _emberwindCalled
                                    ? "Call the Emberwind  [already called — it waits in the rigging]"
                                    : $"Call the Emberwind ({SeaMath.EmberwindAgingDays} days aging) — halve the next crossing and ward it against storms");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch { return false; }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            if (_emberwindCalled) return;
                            AgingSystem.AgeHero(Hero.MainHero, SeaMath.EmberwindAgingDays);
                            _emberwindCalled = true;
                            MBInformationManager.AddQuickInformation(new TextObject(
                                "You breathe a thread of the Inner Fire into the sky. The pennants snap taut toward open water."));
                            try { GameMenu.SwitchToMenu("sea_harbor"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Still the Waters (mages) ──────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_still_waters", "{SEA_STILL_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!MageKnowledge.IsMage) return false;
                            bool canAfford = Hero.MainHero.HitPoints > SeaMath.StillWatersHpCost + 10;
                            if (_stillWatersCalled || !canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("SEA_STILL_TEXT",
                                _stillWatersCalled
                                    ? "Still the Waters  [already stilled — the deep waits]"
                                    : $"Still the Waters ({SeaMath.StillWatersHpCost} HP) — halve the next crossing and ward it against storms");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch { return false; }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            if (_stillWatersCalled) return;
                            if (Hero.MainHero.HitPoints <= SeaMath.StillWatersHpCost + 10)
                            {
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    "You do not have enough left to give. The sea does not take the dying."));
                                return;
                            }
                            Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - SeaMath.StillWatersHpCost);
                            _stillWatersCalled = true;
                            MBInformationManager.AddQuickInformation(new TextObject(
                                "You reach down into the harbour bed and call to what moves beneath. " +
                                "Something vast and patient stirs. The water outside the breakwater goes flat."));
                            try { GameMenu.SwitchToMenu("sea_harbor"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Raid the coast ──────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_raid", "{SEA_RAID_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (Hero.MainHero?.MapFaction == null) return false;
                            bool anyEnemyPort = false;
                            var here = Settlement.CurrentSettlement;
                            foreach (var p in _ports)
                            {
                                if (p == here || p.MapFaction == null) continue;
                                if (Hero.MainHero.MapFaction.IsAtWarWith(p.MapFaction))
                                { anyEnemyPort = true; break; }
                            }
                            if (!anyEnemyPort) return false;
                            MBTextManager.SetTextVariable("SEA_RAID_TEXT", "Raid an enemy coast");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            return true;
                        }
                        catch { return false; }
                    },
                    args => ShowRaidDestinationPicker());
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Leave ───────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_leave", "Back to the town",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    true, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Voyage wait menu ────────────────────────────────────────────
            try
            {
                starter.AddWaitGameMenu("sea_voyage", "{SEA_VOYAGE_TEXT}",
                    new OnInitDelegate(VoyageOnInit),
                    new OnConditionDelegate(VoyageOnCondition),
                    new OnConsequenceDelegate(VoyageOnConsequence),
                    new OnTickDelegate(VoyageOnTick),
                    GameMenu.MenuAndOptionType.WaitMenuShowOnlyProgressOption,
                    GameMenu.MenuOverlayType.None, 0f, GameMenu.MenuFlags.None, null);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Destination picking (shared by passage and ventures) ──────────────
        private static void ShowDestinationPicker(bool forTrade)
        {
            try
            {
                var here = Settlement.CurrentSettlement;
                if (!IsPort(here)) return;

                var options = new List<InquiryElement>();
                foreach (var p in _ports)
                {
                    if (p == here) continue;
                    float dist = PortDistance(here, p);
                    string hover;
                    if (forTrade)
                    {
                        int days = SeaMath.VentureDays(dist);
                        int risk = (int)(SeaMath.VentureLossChance(dist, false) * 100f);
                        hover = $"A factor sails, trades, and returns in about {days} days. Roughly {risk}% of cargoes on this route never come home.";
                    }
                    else
                    {
                        int fare  = SeaMath.Fare(dist, PartySize());
                        int hours = (int)SeaMath.TravelHours(dist, _emberwindCalled);
                        int risk  = (int)(SeaMath.AshenAdjusted(SeaMath.PirateChance(dist), IsAshenPort(p)) * 100f);
                        hover = $"Fare {fare} denars. About {hours} hours at sea. Corsair risk roughly {risk}%."
                              + (IsAshenPort(p) ? " The grey waters off this cold coast take far more ships than they give back." : "");
                    }
                    string faction = "";
                    try { faction = p.MapFaction?.Name?.ToString() ?? ""; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    string ashenMark = IsAshenPort(p) ? "  ❄" : "";
                    options.Add(new InquiryElement(p, $"{p.Name} ({faction}){ashenMark}", null, true, hover));
                }
                if (options.Count == 0) return;

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    forTrade ? "Fund a Trade Venture" : "Charter Passage",
                    forTrade
                        ? "Choose the route. Your factor buys here, sells there, and sails back with whatever the sea allows."
                        : "Choose your destination. The captain wants the fare up front — drowned men pay nothing.",
                    options, true, 1, 1, "Choose", "Never mind",
                    chosen =>
                    {
                        var dest = chosen?[0]?.Identifier as Settlement;
                        if (dest == null) return;
                        if (forTrade) StartVenture(dest);
                        else StartVoyage(dest);
                    },
                    null, "", false), false, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ShowRaidDestinationPicker()
        {
            try
            {
                var here = Settlement.CurrentSettlement;
                if (!IsPort(here) || Hero.MainHero?.MapFaction == null) return;

                var options = new List<InquiryElement>();
                foreach (var p in _ports)
                {
                    if (p == here || p.MapFaction == null) continue;
                    if (!Hero.MainHero.MapFaction.IsAtWarWith(p.MapFaction)) continue;
                    float dist  = PortDistance(here, p);
                    int   fare  = SeaMath.Fare(dist, PartySize());
                    int   hours = (int)SeaMath.TravelHours(dist, _emberwindCalled);
                    int   risk  = (int)(SeaMath.PirateChance(dist) * 100f);
                    bool  blkd  = HostileBlockade(p, Hero.MainHero.MapFaction).HasValue;
                    string blkNote = blkd ? "  [BLOCKADED — expect a fight at the harbor mouth]" : "";
                    string hover = $"Fare {fare} denars. About {hours} hours. Corsair risk {risk}%.{blkNote} " +
                                   "You will land at the gate — take what you can hold.";
                    options.Add(new InquiryElement(p,
                        $"{p.Name} ({p.MapFaction?.Name}){(blkd ? "  ⚓" : "")}",
                        null, true, hover));
                }
                if (options.Count == 0) return;

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "Raid an Enemy Coast",
                    "Choose your target. You will land at their harbor gate — from there it is steel and nerve. " +
                    "Blockaded ports will contest your approach.",
                    options, true, 1, 1, "Sail for it", "Never mind",
                    chosen =>
                    {
                        var dest = chosen?[0]?.Identifier as Settlement;
                        if (dest != null) StartVoyage(dest);
                    },
                    null, "", false), false, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static int PartySize()
        {
            try { return MobileParty.MainParty.MemberRoster.TotalManCount; } catch { return 1; }
        }

    }
}
