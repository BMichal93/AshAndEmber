// =============================================================================
// ASH AND EMBER — NorthmenStonesCampaignBehavior.Menu.cs
//
// "Donate materials for the standing stones" — the Varcheg town submenu.
// Each option hands over the player's entire carried stock of one material
// (or one bound Kindled troop), capped to what's still needed. Once the
// stones are raised, a second, permanently-visible line on the plain "town"
// menu describes them — it has no choices and changes nothing, matching
// "exists but isn't interactive."
// =============================================================================

using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public partial class NorthmenStonesCampaignBehavior
    {
        private static bool AtVarcheg()
        {
            try { return Settlement.CurrentSettlement != null && Settlement.CurrentSettlement == VarchegSettlement(); }
            catch { return false; }
        }

        private static ItemObject GetItem(string id)
        {
            try { return MBObjectManager.Instance?.GetObject<ItemObject>(id); }
            catch { return null; }
        }

        private static void RegisterMenus(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "nstones_donate_enter", "{NSTONES_DONATE_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (_phase != PhaseActive) return false;
                            if (!AtVarcheg()) return false;
                            int pct = (int)(NorthmenStonesMath.BlendedProgress(
                                _iron, _hardwood, _tools, _silver, _denars, KindledTotal()) * 100f);
                            MBTextManager.SetTextVariable("NSTONES_DONATE_ENTER_TEXT",
                                $"Donate materials for the standing stones  [{pct}%]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("nstones_donate_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddGameMenu("nstones_donate_menu", "{NSTONES_DONATE_HEADER}", args =>
                {
                    try
                    {
                        string ownerNote = IsVarchegNorthmenOwned()
                            ? "Varcheg still stands with the Northmen. What is given here stays given."
                            : "Varcheg has fallen out of Northmen hands. What is stored here is bleeding away, " +
                              "ten in every hundred lost with every week it stays lost.";
                        MBTextManager.SetTextVariable("NSTONES_DONATE_HEADER",
                            "Masons and seers work the stones by turns, waiting on what the working still needs.\n\n" +
                            $"Iron: {_iron:N0} / {NorthmenStonesMath.IronTarget:N0}\n" +
                            $"Hardwood: {_hardwood:N0} / {NorthmenStonesMath.HardwoodTarget:N0}\n" +
                            $"Tools: {_tools:N0} / {NorthmenStonesMath.ToolsTarget:N0}\n" +
                            $"Silver: {_silver:N0} / {NorthmenStonesMath.SilverTarget:N0}\n" +
                            $"Denars: {_denars:N0} / {NorthmenStonesMath.DenarsTarget:N0}\n" +
                            $"Bound Kindled given: {KindledTotal()} / {NorthmenStonesMath.KindledTotalTarget}\n\n" +
                            ownerNote);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            RegisterMaterialOption(starter, "iron",     "Iron",     () => _iron,     v => _iron = v,     NorthmenStonesMath.IronTarget);
            RegisterMaterialOption(starter, "hardwood", "Hardwood", () => _hardwood, v => _hardwood = v, NorthmenStonesMath.HardwoodTarget);
            RegisterMaterialOption(starter, "tools",    "Tools",    () => _tools,    v => _tools = v,    NorthmenStonesMath.ToolsTarget);
            RegisterMaterialOption(starter, "silver",   "Silver",   () => _silver,   v => _silver = v,   NorthmenStonesMath.SilverTarget);
            RegisterDenarsOption(starter);
            RegisterKindledOptions(starter);

            try
            {
                starter.AddGameMenuOption("nstones_donate_menu", "nstones_donate_leave", "Step away",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    true, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // The stones themselves, once raised — always visible at Varcheg,
            // no consequence beyond a line of flavor text.
            try
            {
                starter.AddGameMenuOption("town", "nstones_circle_flavor", "The Bonefire Circle stands silent here.",
                    args =>
                    {
                        try
                        {
                            if (!_stoneBuilt) return false;
                            if (!AtVarcheg()) return false;
                            args.IsEnabled = true;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                "The standing stones give off no heat you can feel, and no cold either. Sometimes, " +
                                "at the edge of hearing, something in them seems to be still burning.",
                                new Color(0.85f, 0.45f, 0.15f)));
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void RegisterMaterialOption(
            CampaignGameStarter starter, string itemId, string label,
            System.Func<int> get, System.Action<int> set, int target)
        {
            string optId = $"nstones_donate_{itemId}";
            string varId = $"NSTONES_DONATE_{itemId.ToUpperInvariant()}_TEXT";
            try
            {
                starter.AddGameMenuOption("nstones_donate_menu", optId, "{" + varId + "}",
                    args =>
                    {
                        try
                        {
                            if (_phase != PhaseActive) return false;
                            var item = GetItem(itemId);
                            int held = item != null ? (MobileParty.MainParty?.ItemRoster?.GetItemNumber(item) ?? 0) : 0;
                            MBTextManager.SetTextVariable(varId, $"Donate {label} you carry ({held})");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = item != null && held > 0 && get() < target;
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try
                        {
                            var item = GetItem(itemId);
                            var party = MobileParty.MainParty;
                            if (item == null || party?.ItemRoster == null) return;
                            int held = party.ItemRoster.GetItemNumber(item);
                            int remaining = target - get();
                            int give = System.Math.Min(held, remaining);
                            if (give <= 0) return;
                            party.ItemRoster.AddToCounts(item, -give);
                            set(get() + give);
                            NotifyDonation($"{give} {label} given to the working.");
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void RegisterDenarsOption(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("nstones_donate_menu", "nstones_donate_denars", "{NSTONES_DONATE_DENARS_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (_phase != PhaseActive) return false;
                            int gold = Hero.MainHero?.Gold ?? 0;
                            MBTextManager.SetTextVariable("NSTONES_DONATE_DENARS_TEXT", $"Donate denars for the masons ({gold:N0})");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = gold > 0 && _denars < NorthmenStonesMath.DenarsTarget;
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try
                        {
                            Hero h = Hero.MainHero;
                            if (h == null) return;
                            int remaining = NorthmenStonesMath.DenarsTarget - _denars;
                            int give = System.Math.Min(h.Gold, remaining);
                            if (give <= 0) return;
                            h.ChangeHeroGold(-give);
                            _denars += give;
                            NotifyDonation($"{give:N0} denars given to the masons.");
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void RegisterKindledOptions(CampaignGameStarter starter)
        {
            foreach (var def in SacredSiteCatalog.All)
            {
                var kind = def.Kind;
                string troopId = def.TroopId;
                string name = def.Name;
                string optId = $"nstones_donate_kindled_{kind}";

                try
                {
                    starter.AddGameMenuOption("nstones_donate_menu", optId, "{" + optId.ToUpperInvariant() + "_TEXT}",
                        args =>
                        {
                            try
                            {
                                if (_phase != PhaseActive) return false;
                                int held = HeldKindledCount(troopId);
                                int given = KindledGivenForKind(kind);
                                MBTextManager.SetTextVariable(optId.ToUpperInvariant() + "_TEXT",
                                    $"Give a bound {name} to the working ({given}/{NorthmenStonesMath.KindledTargetPerKind}, {held} bound and with you)");
                                try { args.optionLeaveType = GameMenuOption.LeaveType.Continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                args.IsEnabled = held > 0 && given < NorthmenStonesMath.KindledTargetPerKind;
                                return true;
                            }
                            catch { return false; }
                        },
                        args => DonateKindled(troopId, kind, name));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static int HeldKindledCount(string troopId)
        {
            try
            {
                var character = MBObjectManager.Instance?.GetObject<CharacterObject>(troopId);
                var roster = MobileParty.MainParty?.MemberRoster?.GetTroopRoster();
                if (character == null || roster == null) return 0;
                return roster.Where(e => e.Character == character).Sum(e => e.Number);
            }
            catch { return 0; }
        }

        private static int KindledGivenForKind(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Stone: return _kindledStone;
                case ElementalKind.Frost: return _kindledFrost;
                case ElementalKind.Sand:  return _kindledSand;
                case ElementalKind.Flame: return _kindledFlame;
                case ElementalKind.Tide:  return _kindledTide;
                case ElementalKind.Gale:  return _kindledGale;
                default: return 0;
            }
        }

        private static void AddKindledGiven(ElementalKind kind, int amount)
        {
            switch (kind)
            {
                case ElementalKind.Stone: _kindledStone += amount; break;
                case ElementalKind.Frost: _kindledFrost += amount; break;
                case ElementalKind.Sand:  _kindledSand  += amount; break;
                case ElementalKind.Flame: _kindledFlame += amount; break;
                case ElementalKind.Tide:  _kindledTide  += amount; break;
                case ElementalKind.Gale:  _kindledGale  += amount; break;
            }
        }

        private static void DonateKindled(string troopId, ElementalKind kind, string name)
        {
            try
            {
                var character = MBObjectManager.Instance?.GetObject<CharacterObject>(troopId);
                var party = MobileParty.MainParty;
                if (character == null || party?.MemberRoster == null) return;
                if (HeldKindledCount(troopId) <= 0) return;
                if (KindledGivenForKind(kind) >= NorthmenStonesMath.KindledTargetPerKind) return;

                party.MemberRoster.AddToCounts(character, -1);
                AddKindledGiven(kind, 1);
                NotifyDonation($"The bound {name} is led to Varcheg's ring of stones and does not come back.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void NotifyDonation(string text)
        {
            try
            {
                int pct = (int)(NorthmenStonesMath.BlendedProgress(
                    _iron, _hardwood, _tools, _silver, _denars, KindledTotal()) * 100f);
                NorthmenStonesQuestLog.UpdateProgress(pct);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{text} The working stands at {pct}%.", new Color(0.85f, 0.45f, 0.15f)));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
