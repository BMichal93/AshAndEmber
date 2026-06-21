// =============================================================================
// ASH AND EMBER — AshenAltarsCampaignBehavior.Menus.cs
//
// Dark Altar menu system. Replaces the old Cold-charging Ashen Altar menus.
//
// Structure:
//   Town menu → "Visit the Dark Altar"
//     → altar_main  (summary of owned gifts + active state)
//       → altar_buy_gift  (buy a new gift — submenu)
//       → altar_renounce  (choose a gift to remove)
//       → altar_leave
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class AshenAltarsCampaignBehavior
    {
        private static void RegisterAltarMenus(CampaignGameStarter starter)
        {
            RegisterTownEntry(starter);
            RegisterWastelandRite(starter);
            RegisterMainMenu(starter);
            RegisterBuyMenu(starter);
            RegisterRenounceMenu(starter);
        }

        // ── Town entry ─────────────────────────────────────────────────────────
        private static void RegisterTownEntry(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "dark_altar_enter", "{DARK_ALTAR_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!HasDarkAltar(Settlement.CurrentSettlement)) return false;
                            string status = DarkGiftSystem.GiftsDisabled
                                ? "  [Gifts inactive — virtue has softened you]"
                                : DarkGiftSystem.HasAnyGift
                                    ? $"  [{DarkGiftSystem.TotalOwned} dark gift(s)]"
                                    : "";
                            MBTextManager.SetTextVariable("DARK_ALTAR_ENTER_TEXT",
                                "Visit the Dark Altar" + status);
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("dark_altar_main"); } catch { } },
                    false, -1, false);
            }
            catch { }
        }

        // ── Wasteland Rite (questline — unchanged) ─────────────────────────────
        private static void RegisterWastelandRite(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "wasteland_rite", "{WASTELAND_RITE_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!AshenQuestSystem.IsWastelandUnlocked) return false;
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !s.IsTown) return false;
                            if (s.MapFaction?.StringId != AshenKingdomId) return false;
                            bool done = AshenQuestSystem.IsWastelandCity(s.StringId);
                            MBTextManager.SetTextVariable("WASTELAND_RITE_TEXT",
                                done ? "Wasteland Rite  [already consecrated]" : "Perform the Wasteland Rite");
                            args.IsEnabled = !done;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { AshenQuestSystem.ShowWastelandRiteDialog(Settlement.CurrentSettlement); } catch { } },
                    false, -1, false);
            }
            catch { }
        }

        // ── Main altar menu ────────────────────────────────────────────────────
        private static void RegisterMainMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenu("dark_altar_main", "{DARK_ALTAR_HEADER}", args =>
                {
                    try
                    {
                        string giftList = "";
                        if (DarkGiftSystem.HasAnyGift)
                        {
                            giftList = "\n\nYour dark gifts:";
                            foreach (DarkGiftId g in DarkGiftSystem.AllGifts)
                            {
                                if (g == DarkGiftId.DarkSpirit)
                                {
                                    int cnt = DarkGiftSystem.DarkSpiritCount;
                                    if (cnt > 0)
                                        giftList += $"\n  • {DarkGiftSystem.GetGiftName(g)} ×{cnt}";
                                }
                                else if (DarkGiftSystem.HasGift(g))
                                    giftList += $"\n  • {DarkGiftSystem.GetGiftName(g)}";
                            }
                        }
                        else
                            giftList = "\n\nYou carry no dark gifts yet.";

                        string activeNote = DarkGiftSystem.GiftsDisabled
                            ? "\n\n[Gifts are dormant. You have grown too virtuous — become Merciless or Devious again to reawaken them.]"
                            : "";

                        MBTextManager.SetTextVariable("DARK_ALTAR_HEADER",
                            "The Dark Altar. The stone is older than the city around it, and colder than the stone should be. "
                          + "It does not ask your name. It already knows what you are willing to give."
                          + giftList + activeNote);
                    }
                    catch { }
                });
            }
            catch { }

            // Buy gift option
            try
            {
                starter.AddGameMenuOption("dark_altar_main", "dark_altar_buy", "{DARK_ALTAR_BUY_TEXT}",
                    args =>
                    {
                        try
                        {
                            bool qualifies = DarkGiftSystem.PlayerQualifies();
                            int  owned     = DarkGiftSystem.TotalOwned;
                            int  pCost     = DarkGiftCosts.GetNextPrisonerCost(owned);
                            int  lCost     = DarkGiftCosts.GetNextLordCost(owned);
                            string costStr = lCost > 0
                                ? $"{pCost} prisoners + {lCost} lord(s)"
                                : $"{pCost} prisoners";
                            string lockNote = !qualifies ? "  [Requires Merciless or Devious]" : "";
                            MBTextManager.SetTextVariable("DARK_ALTAR_BUY_TEXT",
                                $"Offer blood for a Dark Gift  (costs {costStr}){lockNote}");
                            args.IsEnabled = qualifies;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("dark_altar_buy"); } catch { } });
            }
            catch { }

            // Renounce gift option
            try
            {
                starter.AddGameMenuOption("dark_altar_main", "dark_altar_renounce", "Renounce a Dark Gift",
                    args =>
                    {
                        try
                        {
                            args.IsEnabled = DarkGiftSystem.HasAnyGift;
                            if (!DarkGiftSystem.HasAnyGift)
                            {
                                MBTextManager.SetTextVariable("DARK_ALTAR_RENOUNCE_INFO",
                                    "  [You carry no dark gifts]");
                            }
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("dark_altar_renounce"); } catch { } });
            }
            catch { }

            // Leave
            try
            {
                starter.AddGameMenuOption("dark_altar_main", "dark_altar_leave", "Leave the Altar",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Buy gift submenu ───────────────────────────────────────────────────
        private static void RegisterBuyMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenu("dark_altar_buy", "Choose the gift you would take from the darkness. Each carries its own hunger.",
                    args => { });
            }
            catch { }

            foreach (var gift in DarkGiftSystem.AllGifts)
            {
                var capturedGift = gift; // closure capture
                string optionId  = $"dark_gift_buy_{(int)gift}";
                try
                {
                    starter.AddGameMenuOption("dark_altar_buy", optionId, $"{{DARK_GIFT_BUY_{(int)gift}}}",
                        args =>
                        {
                            try
                            {
                                bool canBuy = DarkGiftSystem.CanBuyGift(capturedGift);
                                int  owned  = DarkGiftSystem.TotalOwned;
                                int  pCost  = DarkGiftCosts.GetNextPrisonerCost(owned);
                                int  lCost  = DarkGiftCosts.GetNextLordCost(owned);
                                string costStr = lCost > 0
                                    ? $"{pCost}p + {lCost}L"
                                    : $"{pCost}p";

                                string ownedNote = "";
                                if (capturedGift == DarkGiftId.DarkSpirit)
                                {
                                    int cnt = DarkGiftSystem.DarkSpiritCount;
                                    if (cnt > 0) ownedNote = $" [owned ×{cnt}/3]";
                                    if (cnt >= 3) { args.IsEnabled = false; ownedNote = " [max 3]"; }
                                }
                                else if (DarkGiftSystem.HasGift(capturedGift))
                                {
                                    args.IsEnabled = false;
                                    ownedNote = " [already owned]";
                                }

                                MBTextManager.SetTextVariable($"DARK_GIFT_BUY_{(int)capturedGift}",
                                    $"{DarkGiftSystem.GetGiftName(capturedGift)}{ownedNote}  ({costStr})  — {DarkGiftSystem.GetGiftMechanic(capturedGift)}");
                                try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            }
                            catch { }
                            return true;
                        },
                        args => DoBuyGift(capturedGift));
                }
                catch { }
            }

            // Back
            try
            {
                starter.AddGameMenuOption("dark_altar_buy", "dark_altar_buy_back", "Step away",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("dark_altar_main"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Renounce submenu ───────────────────────────────────────────────────
        private static void RegisterRenounceMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenu("dark_altar_renounce",
                    "The altar does not refuse. It simply takes back what it gave, and leaves you lighter for it — though not cleaner.",
                    args => { });
            }
            catch { }

            foreach (var gift in DarkGiftSystem.AllGifts)
            {
                var capturedGift = gift;
                string optionId  = $"dark_gift_renounce_{(int)gift}";
                try
                {
                    starter.AddGameMenuOption("dark_altar_renounce", optionId, $"{{DARK_GIFT_RENOUNCE_{(int)gift}}}",
                        args =>
                        {
                            try
                            {
                                bool has = DarkGiftSystem.HasGift(capturedGift);
                                if (!has) return false; // hide options for gifts not owned

                                string stackNote = capturedGift == DarkGiftId.DarkSpirit
                                    ? $" ×{DarkGiftSystem.DarkSpiritCount}"
                                    : "";
                                MBTextManager.SetTextVariable($"DARK_GIFT_RENOUNCE_{(int)capturedGift}",
                                    $"Renounce: {DarkGiftSystem.GetGiftName(capturedGift)}{stackNote}");
                                try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            }
                            catch { return false; }
                            return true;
                        },
                        args => DoRenounceGift(capturedGift));
                }
                catch { }
            }

            // Back
            try
            {
                starter.AddGameMenuOption("dark_altar_renounce", "dark_altar_renounce_back", "Leave",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("dark_altar_main"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Buy action ─────────────────────────────────────────────────────────
        private static void DoBuyGift(DarkGiftId gift)
        {
            _lastAltarUseDay = CurrentCampaignDay();

            if (!DarkGiftSystem.TryPurchaseGift(gift, out string error))
            {
                ShowDialog("The altar is unmoved.",
                    $"The stone takes nothing. {error}",
                    () => { try { GameMenu.SwitchToMenu("dark_altar_buy"); } catch { } });
                return;
            }

            string lore = DarkGiftSystem.GetGiftLore(gift);
            string mech = DarkGiftSystem.GetGiftMechanic(gift);
            string blockNote = "";
            if (MiracleInventory.Grace == 0)
                blockNote = "";
            else
                blockNote = "\n\nThe warmth in you gutters and dies. You cannot hold both.";

            ShowDialog($"The Altar Accepts — {DarkGiftSystem.GetGiftName(gift)}",
                $"{lore}\n\n{mech}{blockNote}",
                () => { try { GameMenu.SwitchToMenu("dark_altar_main"); } catch { } });
        }

        // ── Renounce action ────────────────────────────────────────────────────
        private static void DoRenounceGift(DarkGiftId gift)
        {
            string name = DarkGiftSystem.GetGiftName(gift);
            DarkGiftSystem.RenounceGift(gift);

            ShowDialog("The Altar Reclaims",
                $"You press your hands to the stone and name what you are giving back. "
              + $"The {name} withdraws from you like a tide pulling sand. "
              + "You are smaller for it. Perhaps that is the point.",
                () => { try { GameMenu.SwitchToMenu("dark_altar_main"); } catch { } });
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static void ShowDialog(string title, string body, Action onClose)
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    title, body, true, false, "It is done.", "",
                    onClose, null));
            }
            catch
            {
                string brief = body.Length > 80 ? body.Substring(0, 80) + "…" : body;
                MBInformationManager.AddQuickInformation(new TextObject(brief));
                try { onClose?.Invoke(); } catch { }
            }
        }
    }
}
