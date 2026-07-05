// =============================================================================
// ASH AND EMBER — Crystals/CrystallinesCampaignBehavior.Menus.cs
//
// Game menu implementation for the Crystalline Chamber.
//
// Menu tree:
//   "town" → "Visit the Crystalline Chamber"
//     → "crystal_main"  (description + which crystal to form)
//       → "crystal_form_<0..5>"  (per-crystal option — checks materials, rolls)
//       → "crystal_rites"         (Crystalseeker class rite — if not owned)
//       → "crystal_leave"
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class CrystallinesCampaignBehavior
    {
        private static void RegisterCrystalMenus(CampaignGameStarter starter)
        {
            RegisterTownEntry(starter);
            RegisterMainMenu(starter);
            RegisterFormOptions(starter);
            RegisterRiteOption(starter);
        }

        // ── Town entry ─────────────────────────────────────────────────────────

        private static void RegisterTownEntry(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "crystalline_chamber_enter", "{CRYSTAL_CHAMBER_TEXT}",
                    args =>
                    {
                        try
                        {
                            // The Chamber serves anyone — crystals need no magical path,
                            // only Silver Ore, the right trade good, and a steady hand.
                            if (!HasCrystallineChamber(Settlement.CurrentSettlement)) return false;
                            MBTextManager.SetTextVariable("CRYSTAL_CHAMBER_TEXT", "Visit the Crystalline Chamber");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("crystal_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Main chamber menu ──────────────────────────────────────────────────

        private static void RegisterMainMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenu("crystal_main", "{CRYSTAL_MAIN_TEXT}", args =>
                {
                    try
                    {
                        int med = 0, eng = 0;
                        try
                        {
                            var hero = Hero.MainHero;
                            if (hero != null)
                            {
                                med = hero.GetSkillValue(DefaultSkills.Medicine);
                                eng = hero.GetSkillValue(DefaultSkills.Engineering);
                            }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                        float odds = TalentSystem.Has(TalentId.PatientGrowth)
                            ? CrystalMath.FormationOddsWithPatience(med, eng)
                            : CrystalMath.FormationOdds(med, eng);

                        string riteNote = TalentSystem.Has(TalentId.Crystalseeker)
                            ? "\n\n[Crystalseeker rites active — Patient Growth, Expanded Pouch, Solar Flare.]"
                            : "";

                        MBTextManager.SetTextVariable("CRYSTAL_MAIN_TEXT",
                            "The Crystalline Chamber. Stone shelves line the walls, each carved to cradle a growing formation. "
                          + "Water seeps from above; crystals the size of a fist rest half-finished in their mineral beds. "
                          + "The process takes focused heat and the right materials.\n\n"
                          + $"Formation chance: {(int)(odds * 100)} % (Medicine {med}, Engineering {eng})."
                          + riteNote);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Study the lattice — the lapidary's learnable craft (focus points).
            try
            {
                starter.AddGameMenuOption("crystal_main", "crystal_study", "{CRYSTAL_STUDY_TEXT}",
                    args =>
                    {
                        try
                        {
                            int have = 0;
                            try { have = Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            MBTextManager.SetTextVariable("CRYSTAL_STUDY_TEXT",
                                $"Study the lattice — the lapidary's craft  [Focus: {have}]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            if (MageKnowledge._deferredInquiry == null)
                                MageKnowledge._deferredInquiry = CrystalTalents.ShowCodex;
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        try { GameMenu.SwitchToMenu("crystal_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Leave.
            try
            {
                starter.AddGameMenuOption("crystal_main", "crystal_leave", "Leave the Chamber",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    true, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Per-crystal formation options ─────────────────────────────────────

        private static void RegisterFormOptions(CampaignGameStarter starter)
        {
            foreach (var def in CrystalCatalog.All)
            {
                var captured = def;
                string optId  = $"crystal_form_{(int)def.Type}";
                string textId = $"CRYSTAL_FORM_{(int)def.Type}";

                try
                {
                    starter.AddGameMenuOption("crystal_main", optId, $"{{{textId}}}",
                        args =>
                        {
                            try
                            {
                                string missing = null;
                                bool   canForm = HasFormationMaterials(captured, out missing);
                                string matNote = canForm
                                    ? $"  [Silver Ore ×1 + {MaterialName(captured.TradeGoodId)} ×1]"
                                    : $"  [{missing ?? "missing materials"}]";
                                MBTextManager.SetTextVariable(textId,
                                    $"Form a {captured.Name}{matNote}  — {captured.EffectDesc}");
                                args.IsEnabled = canForm;
                                try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            return true;
                        },
                        args => { try { DoFormCrystal(captured); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Rite option (Crystalseeker class purchase) ─────────────────────────

        private static void RegisterRiteOption(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("crystal_main", "crystal_rites", "{CRYSTAL_RITES_TEXT}",
                    args =>
                    {
                        try
                        {
                            bool owned = TalentSystem.Has(TalentId.Crystalseeker);
                            int  cost  = TalentSystem.GetNextDisciplineCost(
                                new[] { TalentId.Crystalseeker, TalentId.PatientGrowth,
                                        TalentId.ExpandedPouch, TalentId.SolarFlare });
                            string note = owned
                                ? "  [Crystalseeker rites already known]"
                                : $"  [{cost} focus point{(cost != 1 ? "s" : "")}]";
                            MBTextManager.SetTextVariable("CRYSTAL_RITES_TEXT",
                                $"Study the crystal rites (Crystalseeker){note}");
                            args.IsEnabled = !owned;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args => { try { DoLearnRites(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Formation action ───────────────────────────────────────────────────

        private static void DoFormCrystal(CrystalDef def)
        {
            if (!HasFormationMaterials(def, out string missing))
            {
                ShowDialog("The Chamber cannot proceed.",
                    $"You are missing materials: {missing}",
                    () => { try { GameMenu.SwitchToMenu("crystal_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                return;
            }

            ConsumeFormationMaterials(def);
            bool success = RollFormation(out bool doubleGrow);

            if (!success)
            {
                // ExpandedPouch refunds silver ore on failure.
                bool refunded = TalentSystem.Has(TalentId.ExpandedPouch);
                if (refunded) RefundSilverOre();

                string failNote = refunded
                    ? "\n\n[Silver Ore returned — Expanded Pouch.]"
                    : "\n\n[All materials consumed.]";

                ShowDialog("Formation Failed",
                    "The crystal did not take. The water carried too much sediment, or the heat was wrong. "
                  + "The lattice simply would not cohere."
                  + failNote,
                    () => { try { GameMenu.SwitchToMenu("crystal_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                return;
            }

            GrantCrystal(def);
            string extraNote = "";
            if (doubleGrow)
            {
                GrantCrystal(def);
                extraNote = "\n\n[Patient Growth — a second crystal formed alongside the first.]";
            }

            ShowDialog($"{def.Name} formed",
                $"The lattice closed correctly. Light gathered into the stone's interior, "
              + $"and the crystal held. You add a {def.Name} to your inventory.\n\n"
              + def.EffectDesc
              + extraNote,
                () => { try { GameMenu.SwitchToMenu("crystal_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
        }

        // ── Rite learning ──────────────────────────────────────────────────────

        private static void DoLearnRites()
        {
            if (!TalentSystem.TryPurchase(TalentId.Crystalseeker, Hero.MainHero))
            {
                ShowDialog("The Rites Remain Closed",
                    "You do not have enough focus points, or you have already learned these rites.",
                    () => { try { GameMenu.SwitchToMenu("crystal_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                return;
            }

            ShowDialog("Crystalseeker — Rites Learned",
                "You commit to the discipline. The language of the crystal lattice — its mineral logic, "
              + "its silent patience — opens to you.\n\n"
              + "Patient Growth: +20 % formation success, 15 % chance to form a second crystal.\n"
              + "Expanded Pouch: Silver Ore returned on failure.\n"
              + "Solar Flare: crystals active at dusk and dawn; +25 % effect radius.",
                () => { try { GameMenu.SwitchToMenu("crystal_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string MaterialName(string goodId)
        {
            try
            {
                var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<ItemObject>(goodId);
                return item?.Name?.ToString() ?? goodId;
            }
            catch { return goodId; }
        }

        private static void ShowDialog(string title, string body, Action onClose)
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    title, body, true, false, "Understood.", "",
                    onClose, null));
            }
            catch
            {
                string brief = body.Length > 100 ? body.Substring(0, 100) + "…" : body;
                MBInformationManager.AddQuickInformation(new TextObject(brief));
                try { onClose?.Invoke(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }
    }
}
