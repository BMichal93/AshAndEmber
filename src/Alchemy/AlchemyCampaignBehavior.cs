// =============================================================================
// ASH AND EMBER — Alchemy/AlchemyCampaignBehavior.cs
//
// The campaign side of Alchemy. Establishes Alchemical Labs (every Aserai town +
// a few Imperial ones), registers their town menus, runs the brewing ritual
// (a Medicine test that always yields a vial — clean on success, tainted on
// failure), persists the player's satchel, and ticks NPC lords/companions so the
// world's alchemists brew and use elixirs off screen too.
//
// Brewing rule (requirement): pick an elixir, make a Medicine test, and ADD the
// elixir regardless of the result. A failed test taints it, so it backfires when
// drunk — for the player and for NPCs alike. Capacity = Intelligence.
//
// Lab selection and announcement copy the proven Sanctuary pattern (pick-once,
// announce-after-sync, fully null-guarded). Persistence uses parallel ALCH_* keys
// so pre-Alchemy saves load cleanly (no keys → empty satchel, labs re-picked).
// =============================================================================

using System;
using System.Collections.Generic;
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
    public class AlchemyCampaignBehavior : CampaignBehaviorBase
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const int   ImperialLabCount = 3;
        private const int   BrewGoldCost     = 200;   // ingredients
        private const float BrewMedicineXp   = 25f;   // practice rewards study
        private const string AseraiCultureId = "aserai";

        private static readonly HashSet<string> EmpireKingdomIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "empire", "empire_w", "empire_s", "empire_n" };

        private static readonly List<string> _labIds = new List<string>();
        private static bool _announced = false;
        private static bool _needsAnnounceAfterSync = false;

        private static readonly Random _rng = new Random();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try
            {
                var ids = _labIds.ToList();
                store.SyncData("ALCH_LabIds", ref ids);
                if (ids != null) { _labIds.Clear(); foreach (var id in ids) _labIds.Add(id); }
            }
            catch { }
            try { store.SyncData("ALCH_Announced", ref _announced); } catch { }
            try
            {
                var types = AlchemyInventory._types.ToList();
                store.SyncData("ALCH_InvTypes", ref types);
                if (types != null)
                { AlchemyInventory._types.Clear(); AlchemyInventory._types.AddRange(types); }
            }
            catch { }
            try
            {
                var tainted = AlchemyInventory._tainted.ToList();
                store.SyncData("ALCH_InvTainted", ref tainted);
                if (tainted != null)
                { AlchemyInventory._tainted.Clear(); AlchemyInventory._tainted.AddRange(tainted); }
            }
            catch { }
            // Repair any length mismatch from a partially-written save.
            try
            {
                int n = Math.Min(AlchemyInventory._types.Count, AlchemyInventory._tainted.Count);
                while (AlchemyInventory._types.Count > n) AlchemyInventory._types.RemoveAt(AlchemyInventory._types.Count - 1);
                while (AlchemyInventory._tainted.Count > n) AlchemyInventory._tainted.RemoveAt(AlchemyInventory._tainted.Count - 1);
            }
            catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureLabs();
            RegisterMenus(starter);
        }

        // ── New-game lifecycle (called from CampaignBehavior.OnNewGameCreated) ──
        public static void ResetForNewGame()
        {
            _labIds.Clear();
            _announced = false;
            _needsAnnounceAfterSync = false;
            AlchemyInventory.ResetForNewGame();
        }

        public static void EstablishForNewCampaign()
        {
            ResetForNewGame();
            EnsureLabs();
            Announce();
        }

        // ── Lab selection ──────────────────────────────────────────────────────
        private static void EnsureLabs()
        {
            if (_labIds.Count > 0) return;
            try
            {
                var towns = Settlement.All.Where(s => s.IsTown).ToList();

                // Every Aserai-cultured town.
                var aserai = towns.Where(IsAseraiTown).ToList();

                // Southern Imperial towns are the preferred trading partners of the Aserai.
                // Fill up to ImperialLabCount from them first, then other imperial towns.
                var southImperial = towns
                    .Where(s => !aserai.Contains(s) && s.OwnerClan?.Kingdom?.StringId == "empire_s")
                    .OrderBy(_ => _rng.Next()).ToList();
                var otherImperial = towns
                    .Where(s => !aserai.Contains(s) && !southImperial.Contains(s)
                             && s.OwnerClan?.Kingdom != null
                             && EmpireKingdomIds.Contains(s.OwnerClan.Kingdom.StringId))
                    .OrderBy(_ => _rng.Next()).ToList();
                var imperial = southImperial.Concat(otherImperial).Take(ImperialLabCount).ToList();

                var picks = aserai.Concat(imperial).ToList();

                // Fallback so the network is never empty on an odd/modded map.
                if (picks.Count == 0)
                    picks = towns.OrderBy(_ => _rng.Next()).Take(ImperialLabCount + 1).ToList();

                _labIds.Clear();
                foreach (var s in picks) _labIds.Add(s.StringId);
                if (_labIds.Count > 0 && !_announced) _needsAnnounceAfterSync = true;
            }
            catch { }
        }

        private static bool IsAseraiTown(Settlement s)
        {
            try { return s.Culture?.StringId == AseraiCultureId; }
            catch { return false; }
        }

        private static void Announce()
        {
            _needsAnnounceAfterSync = false;
            if (_announced || _labIds.Count == 0) return;
            _announced = true;
            try
            {
                var names = _labIds
                    .Select(id => Settlement.All.FirstOrDefault(s => s.StringId == id)?.Name?.ToString())
                    .Where(n => !string.IsNullOrEmpty(n)).ToList();
                if (names.Count == 0) return;
                string joined = names.Count == 1 ? names[0]
                    : string.Join(", ", names.Take(names.Count - 1)) + ", and " + names.Last();
                string line = $"Alchemical Labs ply their trade in {joined}. "
                            + "Any traveller may brew elixirs there — if their hand is steady enough.";
                MBInformationManager.AddQuickInformation(new TextObject(line));
                try { InformationManager.DisplayMessage(new InformationMessage(line, new Color(0.55f, 0.80f, 0.50f))); }
                catch { }
            }
            catch { }
        }

        public static bool HasLab(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            return _labIds.Contains(s.StringId);
        }

        // ── Menus ────────────────────────────────────────────────────────────
        private static void RegisterMenus(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "alchemy_lab_enter", "Visit the Alchemical Lab",
                    args =>
                    {
                        try
                        {
                            if (!HasLab(Settlement.CurrentSettlement)) return false;
                            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("alchemy_lab_menu"); } catch { } },
                    false, -1, false);
            }
            catch { }

            try
            {
                starter.AddGameMenu("alchemy_lab_menu", "{ALCH_HEADER}", args =>
                {
                    try
                    {
                        int held = AlchemyInventory.Count, cap = AlchemyInventory.Capacity();
                        int med  = SafeMedicine(Hero.MainHero);
                        int pct  = (int)(AlchemyMath.BrewSuccessChance(med) * 100f);
                        MBTextManager.SetTextVariable("ALCH_HEADER",
                            "The Alchemical Lab. Glass and copper, the air sharp with salts.\n"
                            + $"Satchel: {held}/{cap} vials.   Brewing skill (Medicine {med}): ~{pct}% to brew it clean.");
                    }
                    catch { }
                });
            }
            catch { }

            try
            {
                starter.AddGameMenuOption("alchemy_lab_menu", "alchemy_brew", "Brew an elixir",
                    args =>
                    {
                        try
                        {
                            args.optionLeaveType = GameMenuOption.LeaveType.Default;
                            bool full = !AlchemyInventory.HasSpace();
                            int minCost = TalentSystem.Has(TalentId.DeeperSatchel) ? 150 : BrewGoldCost;
                            bool poor = (Hero.MainHero?.Gold ?? 0) < minCost;
                            args.IsEnabled = !full && !poor;
                            if (full)      args.Tooltip = new TextObject("Your satchel is full.");
                            else if (poor) args.Tooltip = new TextObject($"You need {minCost} denars for ingredients.");
                        }
                        catch { }
                        return true;
                    },
                    args => ShowBrewMenu());
            }
            catch { }

            try
            {
                starter.AddGameMenuOption("alchemy_lab_menu", "alchemy_satchel", "Open your satchel",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { } return true; },
                    args => AlchemyInputHandler.ShowSatchel(inMission: false));
            }
            catch { }

            try
            {
                starter.AddGameMenuOption("alchemy_lab_menu", "alchemy_study_rite", "Study the Art",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            MageKnowledge.ShowRiteTalentMenu("The Alchemical Lab",
                                new[] { TalentId.AshenAlchemist });
                        }
                        catch { }
                    });
            }
            catch { }

            try
            {
                starter.AddGameMenuOption("alchemy_lab_menu", "alchemy_leave", "Leave the lab",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Brewing ──────────────────────────────────────────────────────────
        private static void ShowBrewMenu()
        {
            try
            {
                var elements = AlchemyCatalog.All.Select(d => new InquiryElement(
                    d.Type, $"{d.Name}  [{d.Context}]", null, true,
                    $"{d.Effect}  {d.Flavour}")).ToList();

                int displayCost = TalentSystem.Has(TalentId.DeeperSatchel) ? 150 : BrewGoldCost;
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "Brew an Elixir",
                    $"Choose your formula. Ingredients cost {displayCost} denars. The brew is added to your "
                        + "satchel whatever the outcome — but a clumsy hand may spoil it, and a spoiled brew "
                        + "turns on whoever drinks it.",
                    elements, true, 1, 1, "Brew", "Cancel",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) { ReturnToLab(); return; }
                        BrewElixir((ElixirType)chosen[0].Identifier);
                    },
                    _ => ReturnToLab(), "", false), false, true);
            }
            catch { ReturnToLab(); }
        }

        private static void BrewElixir(ElixirType type)
        {
            var hero = Hero.MainHero;
            if (hero == null) { ReturnToLab(); return; }

            if (!AlchemyInventory.HasSpace())
            {
                ShowResult("Your satchel has no room for another vial.");
                return;
            }

            // DeeperSatchel: ingredient costs drop to a flat 150 denars.
            int actualCost = TalentSystem.Has(TalentId.DeeperSatchel) ? 150 : BrewGoldCost;
            if (hero.Gold < actualCost)
            {
                ShowResult($"You cannot afford the ingredients ({actualCost} denars).");
                return;
            }
            try { hero.Gold -= actualCost; } catch { }

            int med = SafeMedicine(hero);
            // SteadierHand: boost brew success chance by 15%.
            bool clean;
            if (TalentSystem.Has(TalentId.SteadierHand))
            {
                float boostedChance = Math.Min(AlchemyMath.BrewChanceCeil,
                    AlchemyMath.BrewSuccessChance(med) + 0.15f);
                clean = _rng.NextDouble() < boostedChance;
            }
            else
            {
                clean = AlchemyMath.IsBrewSuccess(med, _rng.NextDouble());
            }
            AlchemyInventory.Add(type, tainted: !clean);
            try { hero.HeroDeveloper?.AddSkillXp(DefaultSkills.Medicine, BrewMedicineXp); } catch { }

            // SteadierHand: 20% chance to yield a second clean vial on success.
            bool doubleBrew = false;
            if (clean && TalentSystem.Has(TalentId.SteadierHand) && _rng.NextDouble() < 0.20 && AlchemyInventory.HasSpace())
            {
                doubleBrew = true;
                AlchemyInventory.Add(type, tainted: false);
            }

            // The brew is sealed — but whether you can tell good from bad is a
            // separate test against your Intelligence.
            int wit = SafeIntelligence(hero);
            BrewAppraisal read = AlchemyMath.ReadBrew(wit, _rng.NextDouble());
            // SteadierHand: the hand that seals it may doubt, but it will not lie.
            if (TalentSystem.Has(TalentId.SteadierHand) && read == BrewAppraisal.Misleading)
                read = BrewAppraisal.Unknown;

            string name = AlchemyCatalog.Name(type);
            string result = BrewResultLine(name, clean, read);
            if (doubleBrew)
                result += " The formula yields twice — a second vial settles in your satchel.";
            ShowResult(result);
        }

        // Builds the after-brew message from the true quality and how well it was
        // read. A misleading read deliberately reports the opposite of the truth.
        private static string BrewResultLine(string name, bool clean, BrewAppraisal read)
        {
            string cleanLine  = $"The {name} runs clean and bright. You are certain it is sound.";
            string taintLine  = $"The {name} clouds and spits as it sets — you are certain it has spoiled. You bottle it anyway.";
            switch (read)
            {
                case BrewAppraisal.Correct:
                    return clean ? cleanLine : taintLine;
                case BrewAppraisal.Misleading:
                    // Confident, and wrong.
                    return clean ? taintLine : cleanLine;
                case BrewAppraisal.Unknown:
                default:
                    return $"The {name} settles into the glass. You stopper it, but you cannot say whether it set true — only drinking it will tell.";
            }
        }

        private static void ShowResult(string msg)
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Alchemical Lab", msg, true, false, "Step back", "", () => ReturnToLab(), null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject(msg));
                ReturnToLab();
            }
        }

        private static void ReturnToLab()
        {
            try { GameMenu.SwitchToMenu("alchemy_lab_menu"); } catch { }
        }

        // ── NPC daily tick ─────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (_needsAnnounceAfterSync) Announce();

            try
            {
                foreach (var hero in Hero.AllAliveHeroes
                    .Where(h => (h.IsLord || h.IsWanderer) && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero && h.PartyBelongedTo?.IsActive == true)
                    .OrderBy(_ => _rng.Next()).Take(12))
                {
                    bool aserai    = IsAseraiHero(hero);
                    bool preferred = aserai || IsSouthImperialHero(hero);
                    int  med       = SafeMedicine(hero);
                    if (!preferred && med < 25) continue;
                    if (_rng.NextDouble() >= AlchemyMath.NpcDailyBrewChance(med, preferred)) continue;

                    NpcBrewAndUse(hero, med, aserai);
                }
            }
            catch { }
        }

        private static void NpcBrewAndUse(Hero hero, int med, bool aserai)
        {
            var party = hero.PartyBelongedTo;
            ElixirType type = ChooseCampaignElixir(hero, party, aserai);
            bool clean = AlchemyMath.IsBrewSuccess(med, _rng.NextDouble());

            string name = AlchemyCatalog.Name(type);
            string where = hero.CurrentSettlement?.Name?.ToString();
            string place = string.IsNullOrEmpty(where) ? "in the field" : $"in {where}";

            if (clean)
            {
                AlchemyEffects.ApplyCampaignEffect(hero, party, type);
                Notify($"{hero.Name} brews a {name} {place}.", false);
            }
            else
            {
                AlchemyEffects.ApplyCampaignBackfire(hero, party,
                    AlchemyMath.PickBackfire(_rng.NextDouble()), announce: false);
                Notify($"{hero.Name}'s brew goes wrong {place} — a {name} ill-made.", true);
            }
        }

        private static ElixirType ChooseCampaignElixir(Hero hero, MobileParty party, bool aserai)
        {
            try
            {
                if (hero.HitPoints < hero.MaxHitPoints * 0.5f) return ElixirType.HealingDraught;
            }
            catch { }

            bool hasWounded = false;
            try
            {
                hasWounded = party?.MemberRoster != null
                    && party.MemberRoster.GetTroopRoster().Any(e => !e.Character.IsHero && e.WoundedNumber > 0);
            }
            catch { }
            if (hasWounded && _rng.Next(2) == 0)
                return _rng.Next(2) == 0 ? ElixirType.MarrowmendTincture : ElixirType.FieldSurgeonPhiltre;

            // Aserai near a settlement may tend the land or steady a town; otherwise
            // rally the column.
            if (aserai && _rng.Next(3) == 0)
                return _rng.Next(2) == 0 ? ElixirType.HearthsmokeCenser : ElixirType.KindlingCenser;
            return ElixirType.OathWine;
        }

        // NPC notice — posted to the message log like the Sanctuary's NPC miracles.
        private static void Notify(string line, bool bad)
        {
            try
            {
                Color c = bad ? new Color(0.80f, 0.45f, 0.30f) : new Color(0.55f, 0.78f, 0.48f);
                InformationManager.DisplayMessage(new InformationMessage(line, c));
            }
            catch { }
        }

        // ── helpers ──────────────────────────────────────────────────────────
        private static int SafeMedicine(Hero hero)
        {
            try { return hero?.GetSkillValue(DefaultSkills.Medicine) ?? 0; }
            catch { return 0; }
        }

        private static int SafeIntelligence(Hero hero)
        {
            try { return hero?.GetAttributeValue(DefaultCharacterAttributes.Intelligence) ?? 0; }
            catch { return 0; }
        }

        private static bool IsAseraiHero(Hero hero)
        {
            try { return hero.Clan?.Culture?.StringId == AseraiCultureId; }
            catch { return false; }
        }

        private static bool IsSouthImperialHero(Hero hero)
        {
            try { return hero.Clan?.Kingdom?.StringId == "empire_s"; }
            catch { return false; }
        }
    }
}
