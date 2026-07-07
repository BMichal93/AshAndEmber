// =============================================================================
// ASH AND EMBER — AshenRuins/AshenRuinMenus.cs
// Session launch: village name resolution, menu registration, guard spawning.
// Partial-class wiring lives in CampaignBehavior.Ticks.cs / Lifecycle.cs.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static class AshenRuinMenus
    {
        private static readonly List<Settlement> _ruinVillages = new List<Settlement>();
        private static readonly Dictionary<string, RuinDef> _bySettlementId = new Dictionary<string, RuinDef>();

        // ── Session launch ─────────────────────────────────────────────────────
        public static void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { ResolveVillages(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RegisterMenus(starter); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpawnInitialGuards(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ResolveVillages()
        {
            _ruinVillages.Clear();
            _bySettlementId.Clear();
            if (Campaign.Current == null) return;

            foreach (var def in AshenRuinDefs.All)
            {
                bool matched = false;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.IsVillage) continue;
                    string name = null;
                    try { name = s.Name?.ToString()?.Trim(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!string.Equals(name, def.VillageName, StringComparison.OrdinalIgnoreCase)) continue;
                    _ruinVillages.Add(s);
                    _bySettlementId[s.StringId] = def;
                    matched = true;
                    break;
                }
                // A RuinDef whose VillageName doesn't match any live village never
                // spawns a menu option — surface that instead of failing silently,
                // so a mistyped or non-village name is caught on first launch.
                if (!matched)
                    AshAndEmber.ModLog.Error(new System.Exception(
                        $"AshenRuins: '{def.RuinName}' expects a village named '{def.VillageName}' but none was found on this map — it will never appear."));
            }
        }

        private static void SpawnInitialGuards()
        {
            foreach (var def in AshenRuinDefs.All)
                try { AshenRuinSystem.SpawnGuardsForRuin(def); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void WeeklySpawnGuards()
        {
            try { AshenRuinSystem.WeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Menu registration ──────────────────────────────────────────────────
        private static void RegisterMenus(CampaignGameStarter starter)
        {
            // Entry option in village menu
            try
            {
                starter.AddGameMenuOption("village", "ar_ruin_enter", "{AR_RUIN_LABEL}",
                    args =>
                    {
                        try
                        {
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !s.IsVillage) return false;
                            if (!_bySettlementId.TryGetValue(s.StringId, out var def)) return false;
                            if (!MageKnowledge.IsMage) return false;

                            bool onCd   = AshenRuinSystem.IsOnCooldown(def.VillageName);
                            bool cleared = AshenRuinSystem.IsCleared(def.VillageName);
                            bool contested = AshenRuinSystem.IsContested(def.VillageName);

                            string note = "";
                            if (onCd && cleared) note = " [exhausted for this season]";
                            else if (onCd)       note = $" [disturbed — {AshenRuinSystem.CooldownDays(def.VillageName)} day(s)]";
                            else if (contested)  note = " [contested by a lord]";

                            MBTextManager.SetTextVariable("AR_RUIN_LABEL", $"Explore {def.RuinName}{note}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = !onCd;
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try
                        {
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !_bySettlementId.TryGetValue(s.StringId, out var def)) return;
                            GameMenu.SwitchToMenu("ar_ruin_enter");
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Entry confirmation menu
            try
            {
                starter.AddGameMenu("ar_ruin_enter", "{AR_RUIN_HDR}", args =>
                {
                    try
                    {
                        var s = Settlement.CurrentSettlement;
                        if (s == null || !_bySettlementId.TryGetValue(s.StringId, out var def))
                        {
                            MBTextManager.SetTextVariable("AR_RUIN_HDR", "The ruins are silent.");
                            return;
                        }
                        bool isSolo = (MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0) <= 1;
                        string tierStr = def.Tier switch
                        {
                            RuinTier.Easy      => "low danger",
                            RuinTier.Standard  => "moderate danger",
                            RuinTier.Brutal    => "high danger",
                            _                  => "extreme danger",
                        };
                        string soloNote = isSolo ? "  [You are alone — some rooms will be harder, others easier.]" : "";
                        MBTextManager.SetTextVariable("AR_RUIN_HDR",
                            $"{def.RuinName}  [{tierStr}]  [{def.Challenges.Length} room(s)]{soloNote}\n\n{def.EntryLore}");
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Option: Slip past guards (Tier 3+) vs Enter directly (Tier 1-2)
            try
            {
                starter.AddGameMenuOption("ar_ruin_enter", "ar_ruin_go", "{AR_RUIN_GO}",
                    args =>
                    {
                        try
                        {
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !_bySettlementId.TryGetValue(s.StringId, out var def)) return false;
                            bool isSolo = (MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0) <= 1;
                            bool needsSlip = def.Tier >= RuinTier.Brutal && isSolo;
                            string label = needsSlip ? "Slip past the wardens and enter" : "Enter the ruin";
                            MBTextManager.SetTextVariable("AR_RUIN_GO", label);
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try
                        {
                            GameMenu.SwitchToMenu("village");
                            var s = Settlement.CurrentSettlement ?? FindCurrentRuinVillage();
                            if (s == null || !_bySettlementId.TryGetValue(s?.StringId ?? "", out var def)) return;
                            bool isSolo = (MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0) <= 1;

                            if (def.Tier >= RuinTier.Brutal && isSolo)
                                ShowSlipPastDialog(def, isSolo);
                            else
                                AshenRuinSystem.BeginExploration(def, isSolo);
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Option: Leave
            try
            {
                starter.AddGameMenuOption("ar_ruin_enter", "ar_ruin_leave", "Walk away",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("village"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    true, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Solo Tier-3/4 slip-past gate ──────────────────────────────────────
        private static void ShowSlipPastDialog(RuinDef def, bool isSolo)
        {
            int roguery = 0;
            try { roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            int proficiency = TalentSystem.PurchasedCount;
            bool autoPass = roguery > 150 || proficiency >= 10;

            InformationManager.ShowInquiry(new InquiryData(
                "Ashen Wardens",
                $"A patrol guards the approach to {def.RuinName}. Alone, you might slip past — or spend 2 aging days to create a distraction and guarantee it. Or wait for nightfall (costs 1 day).",
                true, true, "Attempt to slip past", "Wait for nightfall (1 day, guaranteed)",
                () =>
                {
                    if (autoPass || new Random().Next(100) < Math.Min(80, roguery / 3 + proficiency * 4))
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Slipped Past",
                            "The wardens move in a pattern you can read. You are through before they turn.",
                            true, false, "Enter", "",
                            () => AshenRuinSystem.BeginExploration(def, isSolo), null), true);
                    }
                    else
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Detected",
                            "A warden glances your way. You freeze. Then, in a moment of expensive inspiration, you burn 2 days of fire into a distraction that pulls them away.",
                            true, true, "Pay (2 days aging) and enter", "Abort",
                            () => { AgingSystem.AgeHero(Hero.MainHero, 2); AshenRuinSystem.BeginExploration(def, isSolo); },
                            () => { }), true);
                    }
                },
                () =>
                {
                    AgingSystem.AgeHero(Hero.MainHero, 1);
                    AshenRuinSystem.BeginExploration(def, isSolo);
                }), true);
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        public static bool IsRuinVillage(Settlement s) =>
            s != null && _bySettlementId.ContainsKey(s.StringId);

        public static RuinDef GetDef(Settlement s) =>
            s != null && _bySettlementId.TryGetValue(s.StringId, out var d) ? d : null;

        // Fallback: find ruin village by current party position when CurrentSettlement is null
        private static Settlement FindCurrentRuinVillage()
        {
            try
            {
                Vec2 pos = MobileParty.MainParty?.GetPosition2D ?? Vec2.Zero;
                return _ruinVillages
                    .OrderBy(s => (s.GetPosition2D - pos).LengthSquared)
                    .FirstOrDefault();
            }
            catch { return null; }
        }
    }
}
