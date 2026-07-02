// =============================================================================
// ASH AND EMBER — TalentSystem.NpcSpells.cs
// NPC campaign-map spell execution and save/load.
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
        // ── NPC campaign map spell execution ─────────────────────────────────
        public static void ExecuteNpcMapSpell(Hero caster, TalentId id)
        {
            if (caster == null) return;
            string blurb = null;
            try
            {
                switch (id)
                {
                    case TalentId.BreakWills:  NpcBreakWills(caster);  blurb = "casts Unsettle — dread spreads through an enemy party."; break;
                    case TalentId.Inspire:     NpcInspire(caster);     blurb = "kindles their warband — morale rises."; break;
                    case TalentId.Plague:      NpcPlague(caster);      blurb = "works a Wither — a village's hearth fades."; break;
                    case TalentId.Extinguish:  NpcExtinguish(caster);  blurb = "casts Extinguish — fires snuffed in a distant party."; break;
                    case TalentId.Clairvoyance:NpcClairvoyance(caster);blurb = "reads the threads — power flows to them."; break;
                    default: break;
                }
            }
            catch { }

            if (blurb != null)
            {
                bool isAshen = ColourLordRegistry.IsAshenLord(caster);
                Color c = isAshen ? new Color(0.38f, 0.50f, 0.75f) : new Color(0.65f, 0.45f, 0.8f);
                InformationManager.DisplayMessage(new InformationMessage($"{caster.Name} — {blurb}", c));
            }

            if (ColourLordRegistry.IsAshenLord(caster)) ApplyBlightDrain(caster);
            else ColourLordRegistry.SpendLordLifeExpectancy(caster, 1);
        }

        private static void ApplyBlightDrain(Hero caster)
        {
            try
            {
                var party = caster.PartyBelongedTo;
                if (_rng.Next(2) == 0 && party != null)
                {
                    var troops = party.MemberRoster.GetTroopRoster()
                        .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                    if (troops.Count > 0)
                    {
                        var entry = troops[_rng.Next(troops.Count)];
                        try { party.MemberRoster.AddToCounts(entry.Character, 0, false, 1); } catch { }
                        return;
                    }
                }
                Vec2 pos = party?.GetPosition2D ?? Vec2.Zero;
                var village = Settlement.All
                    .Where(s => s.IsVillage && s.Village != null)
                    .OrderBy(s => (s.GetPosition2D - pos).Length)
                    .FirstOrDefault();
                if (village != null)
                    village.Village.Hearth = Math.Max(10f, village.Village.Hearth * 0.97f);
            }
            catch { }
        }

        private static void NpcBreakWills(Hero caster)
        {
            Vec2 pos = caster.PartyBelongedTo?.GetPosition2D ?? Vec2.Zero;
            var target = MobileParty.All
                .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, caster.PartyBelongedTo?.MapFaction)
                         && (p.GetPosition2D - pos).Length < 50f)
                .OrderBy(p => (p.GetPosition2D - pos).Length).FirstOrDefault();
            if (target == null) return;
            target.RecentEventsMorale -= 35f;
            var tClan = target.LeaderHero?.Clan;
            if (tClan != null) tClan.Influence = Math.Max(0f, tClan.Influence - 10f);

            // Mage-to-mage interference: if Unsettle hits a fellow mage lord, threads cross — aging cost and log
            var targetHero = target.LeaderHero;
            if (targetHero != null && ColourLordRegistry.IsColourLord(targetHero))
            {
                try { ColourLordRegistry.SpendLordLifeExpectancy(targetHero, 1); } catch { }
                string casterName = caster.Name?.ToString() ?? "A mage";
                string targetName = targetHero.Name?.ToString() ?? "another mage";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{casterName}'s Unsettle crossed {targetName}'s fire — threads tangled. Both pay.",
                    new Color(0.55f, 0.40f, 0.70f)));
            }
        }

        private static void NpcInspire(Hero caster)
        {
            var party = caster.PartyBelongedTo;
            if (party == null) return;
            party.RecentEventsMorale += 20f;
        }

        private static void NpcPlague(Hero caster)
        {
            var villages = Settlement.All
                .Where(s => s.IsVillage && s.Village != null && s.MapFaction != caster.MapFaction).ToList();
            if (villages.Count == 0) return;
            var v = villages[_rng.Next(villages.Count)];
            v.Village.Hearth = Math.Max(10f, v.Village.Hearth * 0.80f);
        }

        private static void NpcExtinguish(Hero caster)
        {
            Vec2 pos = caster.PartyBelongedTo?.GetPosition2D ?? Vec2.Zero;
            var target = MobileParty.All
                .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, caster.PartyBelongedTo?.MapFaction)
                         && p.MemberRoster.TotalRegulars > 2
                         && (p.GetPosition2D - pos).Length < 60f)
                .OrderBy(p => (p.GetPosition2D - pos).Length).FirstOrDefault();
            if (target == null) return;
            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
            if (troops.Count == 0) return;
            try { target.MemberRoster.AddToCounts(troops[_rng.Next(troops.Count)].Character, 0, false, 1); } catch { }

            // Mage-to-mage interference: if Extinguish hits a fellow mage lord, threads cross
            var targetHero = target.LeaderHero;
            if (targetHero != null && ColourLordRegistry.IsColourLord(targetHero))
            {
                try { ColourLordRegistry.SpendLordLifeExpectancy(targetHero, 1); } catch { }
                string casterName = caster.Name?.ToString() ?? "A mage";
                string targetName = targetHero.Name?.ToString() ?? "another mage";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{casterName}'s Extinguish grazed {targetName}'s fire — the cold spreads where it was not aimed.",
                    new Color(0.40f, 0.55f, 0.75f)));
            }
        }

        private static void NpcClairvoyance(Hero caster)
        {
            try
            {
                if (caster.Clan != null)
                {
                    caster.Clan.Renown    += 10f;
                    caster.Clan.Influence += 15f;
                }
                if (caster.PartyBelongedTo != null)
                    caster.PartyBelongedTo.RecentEventsMorale += 10f;
            }
            catch { }
        }

        private static void Msg(string text) =>
            MBInformationManager.AddQuickInformation(new TextObject(text));

        // ── Save / Load ────────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var list = _purchased.Select(t => (int)t).ToList();
            store.SyncData("LDM_Talents", ref list);
            _purchased.Clear();
            if (list != null)
                foreach (int v in list) _purchased.Add((TalentId)v);

            // Persist the daily map-cast counter — it is static, so without this a
            // load mid-day (or of a different campaign) inherits the previous
            // session's counter and miscalculates the escalating cast cost.
            store.SyncData("LDM_DailyCastCount", ref _dailyMapCastCount);

            // Fade is intentionally not persisted — on load the effect resets.
            // Explicitly clear IgnoreByOtherParties so a save/load while faded
            // does not leave the party permanently invisible.
            _fadeDaysRemaining = 0;
            try { if (MobileParty.MainParty != null) TrySetPartyConcealed(MobileParty.MainParty, false); } catch { }
        }
    }
}
