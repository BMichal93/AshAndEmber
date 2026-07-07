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
        // ── NPC campaign map spell execution (unified element magic) ─────────────
        // A mage lord casts ONE map working per element he has learned, mirroring the
        // player's five element map spells (ElementMapSpells) but centred on his own
        // party and faction. No old fire-path workings — the same magic the player
        // wields. The Ashen wear the cold mask (naming) over the same effects.
        public static void ExecuteNpcElementMapSpell(Hero caster, MagicElement el)
        {
            if (caster == null) return;
            bool isAshen = ColourLordRegistry.IsAshenLord(caster);
            string blurb = null;
            try
            {
                switch (el)
                {
                    case MagicElement.Fire:      NpcEmberfall(caster);       blurb = isAshen ? "looses a Coldfall — the freeze guts an enemy host."          : "calls Emberfall — fire guts an enemy host."; break;
                    case MagicElement.Wind:      NpcScatteringGale(caster);  blurb = isAshen ? "raises a Stormfront — an enemy host is thrown into disorder." : "raises a Scattering Gale — an enemy host is thrown into disorder."; break;
                    case MagicElement.Earth:     NpcDeeprootBlight(caster);  blurb = isAshen ? "spreads the Ashrot — a village's hearth withers."            : "works a Deeproot Blight — a village's hearth withers."; break;
                    case MagicElement.Water:     NpcTidewash(caster);        blurb = isAshen ? "draws the Snowmelt — their column is mended and steadied."    : "draws a Tidewash — their column is mended and steadied."; break;
                    case MagicElement.Spirit:    NpcFarsight(caster);        blurb = isAshen ? "casts the Void's Sight — power flows to them."                : "casts Farsight — power flows to them."; break;
                    case MagicElement.Lightning: NpcStormsReckoning(caster); blurb = isAshen ? "unleashes the Deathbolt — an enemy host is struck from the sky." : "calls Storm's Reckoning — an enemy host is struck from the sky."; break;
                    case MagicElement.Magma:     NpcScorchedEarth(caster);   blurb = isAshen ? "spreads the Ashfall — an enemy host's wagons burn."          : "works Scorched Earth — an enemy host's wagons burn."; break;
                    case MagicElement.Fog:       NpcHiddenRoad(caster);      blurb = isAshen ? "raises the White Shroud — their column slips from sight."    : "raises the Hidden Road — their column slips from sight."; break;
                    case MagicElement.Sandstorm: NpcShiftingDunes(caster);   blurb = isAshen ? "raises the Bone Storm — an enemy host loses its ground."     : "raises Shifting Dunes — an enemy host loses its ground."; break;
                    case MagicElement.Mire:      NpcSinkingRoad(caster);     blurb = isAshen ? "opens the Grey Sinking — an enemy host's road gives way."     : "opens the Sinking Road — an enemy host's road gives way."; break;
                    case MagicElement.Ice:       NpcLongStillness(caster);   blurb = isAshen ? "casts the Endless Winter — an enemy host's nerve is gone."    : "casts the Long Stillness — an enemy host's nerve is gone."; break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (blurb != null)
            {
                Color c = isAshen ? new Color(0.38f, 0.50f, 0.75f) : new Color(0.65f, 0.45f, 0.8f);
                InformationManager.DisplayMessage(new InformationMessage($"{caster.Name} — {blurb}", c));
            }

            if (isAshen) ApplyBlightDrain(caster);
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
                        try { party.MemberRoster.AddToCounts(entry.Character, 0, false, 1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Wind — Scattering Gale: an enemy host near the caster is thrown into disorder.
        private static void NpcScatteringGale(Hero caster)
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

            // Mage-to-mage interference: crossing a fellow mage lord's fire costs both.
            var targetHero = target.LeaderHero;
            if (targetHero != null && ColourLordRegistry.IsColourLord(targetHero))
            {
                try { ColourLordRegistry.SpendLordLifeExpectancy(targetHero, 1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                string casterName = caster.Name?.ToString() ?? "A mage";
                string targetName = targetHero.Name?.ToString() ?? "another mage";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{casterName}'s gale crossed {targetName}'s fire — threads tangled. Both pay.",
                    new Color(0.55f, 0.40f, 0.70f)));
            }
        }

        // Water — Tidewash: the caster's own column is heartened and its wounded mended.
        private static void NpcTidewash(Hero caster)
        {
            var party = caster.PartyBelongedTo;
            if (party == null) return;
            party.RecentEventsMorale += 20f;
            try
            {
                var roster = party.MemberRoster;
                var wounded = roster.GetTroopRoster().Where(e => !e.Character.IsHero && e.WoundedNumber > 0).ToList();
                foreach (var entry in wounded)
                {
                    int heal = Math.Min(entry.WoundedNumber, 6);
                    try { roster.AddToCounts(entry.Character, 0, false, -heal); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Earth — Deeproot Blight: an enemy village's hearth withers.
        private static void NpcDeeprootBlight(Hero caster)
        {
            var villages = Settlement.All
                .Where(s => s.IsVillage && s.Village != null && s.MapFaction != caster.MapFaction).ToList();
            if (villages.Count == 0) return;
            var v = villages[_rng.Next(villages.Count)];
            v.Village.Hearth = Math.Max(10f, v.Village.Hearth * 0.80f);
        }

        // Fire — Emberfall: fire falls on an enemy host near the caster, guttering its ranks.
        private static void NpcEmberfall(Hero caster)
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
            try { target.MemberRoster.AddToCounts(troops[_rng.Next(troops.Count)].Character, 0, false, 1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Mage-to-mage interference: crossing a fellow mage lord's fire costs both.
            var targetHero = target.LeaderHero;
            if (targetHero != null && ColourLordRegistry.IsColourLord(targetHero))
            {
                try { ColourLordRegistry.SpendLordLifeExpectancy(targetHero, 1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                string casterName = caster.Name?.ToString() ?? "A mage";
                string targetName = targetHero.Name?.ToString() ?? "another mage";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{casterName}'s Emberfall grazed {targetName}'s fire — the flame spread where it was not aimed.",
                    new Color(0.75f, 0.45f, 0.30f)));
            }
        }

        // ── Fusion map spells (v0.37) — mirror ElementMapSpells, caster-centred ──
        private static MobileParty NearestHostileToCaster(Hero caster, float maxDist)
        {
            var party = caster.PartyBelongedTo;
            if (party == null) return null;
            Vec2 pos = party.GetPosition2D;
            return MobileParty.All
                .Where(p => p != null && p.IsActive && p != party
                         && p.MapFaction != null && caster.MapFaction != null
                         && FactionManager.IsAtWarAgainstFaction(p.MapFaction, caster.MapFaction)
                         && (p.GetPosition2D - pos).Length < maxDist)
                .OrderBy(p => (p.GetPosition2D - pos).Length)
                .FirstOrDefault();
        }

        // Lightning — Storm's Reckoning: the nearest hostile host is struck from the sky.
        private static void NpcStormsReckoning(Hero caster)
        {
            var target = NearestHostileToCaster(caster, 60f);
            if (target == null) return;
            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
            if (troops.Count == 0) return;
            try { target.MemberRoster.AddToCounts(troops[_rng.Next(troops.Count)].Character, 0, false, 1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { target.RecentEventsMorale -= 25f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Magma — Scorched Earth: the nearest hostile host's wagons and stores burn.
        private static void NpcScorchedEarth(Hero caster)
        {
            var target = NearestHostileToCaster(caster, 60f);
            if (target == null) return;
            try { NatureEffects.RemoveFoodFromRoster(target, 25); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
            if (troops.Count > 0)
                try { target.MemberRoster.AddToCounts(troops[_rng.Next(troops.Count)].Character, 0, false, 1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Fog — The Hidden Road: the caster's OWN column slips away from its nearest threat.
        private static void NpcHiddenRoad(Hero caster)
        {
            var party = caster.PartyBelongedTo;
            if (party == null) return;
            party.RecentEventsMorale += 10f;
            var threat = NearestHostileToCaster(caster, 200f);
            if (threat == null) return;
            try
            {
                Vec2 pos = party.GetPosition2D;
                Vec2 away = pos - threat.GetPosition2D;
                float len = away.Length;
                if (len < 0.5f) away = new Vec2(1f, 0f); else away *= 1f / len;
                party.Position = new CampaignVec2(pos + away * 4f, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Sandstorm — Shifting Dunes: the nearest hostile host is turned back on itself.
        private static void NpcShiftingDunes(Hero caster)
        {
            var target = NearestHostileToCaster(caster, 55f);
            if (target == null) return;
            try
            {
                var party = caster.PartyBelongedTo;
                Vec2 tPos = target.GetPosition2D;
                Vec2 back = tPos - (party?.GetPosition2D ?? tPos);
                float len = back.Length;
                if (len < 0.5f) back = new Vec2(1f, 0f); else back *= 1f / len;
                target.Position = new CampaignVec2(tPos + back * 5f, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { target.RecentEventsMorale -= 20f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Mire — The Sinking Road: the nearest hostile host loses stores AND ground.
        private static void NpcSinkingRoad(Hero caster)
        {
            var target = NearestHostileToCaster(caster, 55f);
            if (target == null) return;
            try { NatureEffects.RemoveFoodFromRoster(target, 18); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try
            {
                var party = caster.PartyBelongedTo;
                Vec2 tPos = target.GetPosition2D;
                Vec2 back = tPos - (party?.GetPosition2D ?? tPos);
                float len = back.Length;
                if (len < 0.5f) back = new Vec2(1f, 0f); else back *= 1f / len;
                target.Position = new CampaignVec2(tPos + back * 3f, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Ice — The Long Stillness: pure morale and standing, no blade drawn.
        private static void NpcLongStillness(Hero caster)
        {
            var target = NearestHostileToCaster(caster, 60f);
            if (target == null) return;
            try { target.RecentEventsMorale -= 40f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            var tClan = target.LeaderHero?.Clan;
            if (tClan != null)
                try { tClan.Influence = Math.Max(0f, tClan.Influence - 12f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Spirit — Farsight: the caster reads the currents of power; renown and influence flow to him.
        private static void NpcFarsight(Hero caster)
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            try { if (MobileParty.MainParty != null) TrySetPartyConcealed(MobileParty.MainParty, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
