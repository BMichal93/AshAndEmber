// =============================================================================
// ASH AND EMBER — Magic/ElementMapSpells.cs
//
// Each element the player carries grants ONE campaign-map working, cast through
// the memory-rite (ElementSpellMinigame) exactly as the old fire map spells were.
// The recall score scales the working's power (powerMult).
//
//   Fire   — Emberfall   : fire rains on the nearest hostile settlement.
//   Wind   — Scattering Gale : nearby hostile parties are thrown into disorder.
//   Earth  — Deeproot Blight : the nearest hostile village's hearth withers.
//   Water  — Tidewash    : your own party is mended and heartened.
//   Spirit — Farsight    : the currents of power bend toward you (influence/gold)
//                          and any scheme set against you is revealed.
//
// Effects deliberately mirror the proven fire map-spell logic so the economy and
// balance carry over unchanged; only the flavour and target shift per element.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    internal static class ElementMapSpells
    {
        private static readonly Random _rng = new Random();

        public static string Name(MagicElement el)
        {
            switch (el)
            {
                case MagicElement.Wind:      return "Scattering Gale";
                case MagicElement.Earth:     return "Deeproot Blight";
                case MagicElement.Water:     return "Tidewash";
                case MagicElement.Spirit:    return "Farsight";
                case MagicElement.Lightning: return "Storm's Reckoning";
                case MagicElement.Magma:     return "Scorched Earth";
                case MagicElement.Fog:       return "The Hidden Road";
                case MagicElement.Sandstorm: return "Shifting Dunes";
                case MagicElement.Mire:      return "The Sinking Road";
                case MagicElement.Ice:       return "The Long Stillness";
                default:                     return "Emberfall";
            }
        }

        public static string Lore(MagicElement el)
        {
            switch (el)
            {
                case MagicElement.Wind:      return "Call the high wind down on a hostile host until their order comes apart and their nerve with it.";
                case MagicElement.Earth:     return "Reach through the soil to a hostile village and let the root-rot take its hearth.";
                case MagicElement.Water:     return "Draw the quiet of still water through your own column — wounds close, hearts steady.";
                case MagicElement.Spirit:    return "Send the fire further than sight, reading the currents of power and the knives set behind your back.";
                case MagicElement.Lightning: return "Call the sky down on the nearest hostile host — no cone, no warning, only the strike.";
                case MagicElement.Magma:     return "Reach into a hostile host's wagons and let the fire take their stores before their swords.";
                case MagicElement.Fog:       return "Wrap your own column in a bank of standing fog and slip away from the nearest threat unseen.";
                case MagicElement.Sandstorm: return "Turn a hostile host back on itself — the storm swallows the ground they already crossed.";
                case MagicElement.Mire:      return "Let the road itself give way beneath a hostile host's wagons — the ground and the grain both go.";
                case MagicElement.Ice:       return "Freeze not the flesh but the will — a hostile host's nerve and standing wither, and not a blade is drawn.";
                default:                     return "Pull the fire high and let it fall upon a hostile settlement — garrison, granary, and wall alike.";
            }
        }

        // ── Dispatch ──────────────────────────────────────────────────────────────
        public static void Execute(MagicElement el, float mult)
        {
            switch (el)
            {
                case MagicElement.Wind:      CastScatteringGale(mult); break;
                case MagicElement.Earth:     CastDeeprootBlight(mult);  break;
                case MagicElement.Water:     CastTidewash(mult);        break;
                case MagicElement.Spirit:    CastFarsight(mult);        break;
                case MagicElement.Lightning: CastStormsReckoning(mult); break;
                case MagicElement.Magma:     CastScorchedEarth(mult);   break;
                case MagicElement.Fog:       CastHiddenRoad(mult);      break;
                case MagicElement.Sandstorm: CastShiftingDunes(mult);   break;
                case MagicElement.Mire:      CastSinkingRoad(mult);     break;
                case MagicElement.Ice:       CastLongStillness(mult);   break;
                default:                     CastEmberfall(mult);       break;
            }
            try { MageKnowledge.RewardCastSkill(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Shared: nearest hostile party within reach ──────────────────────────
        private static MobileParty NearestHostileParty(Vec2 from, IFaction playerFaction, float maxDist)
        {
            return MobileParty.All
                .Where(p => p != null && p.IsActive && p != MobileParty.MainParty
                         && p.MapFaction != null && playerFaction != null
                         && FactionManager.IsAtWarAgainstFaction(p.MapFaction, playerFaction)
                         && (p.GetPosition2D - from).Length < maxDist)
                .OrderBy(p => (p.GetPosition2D - from).Length)
                .FirstOrDefault();
        }

        // ── Lightning: Storm's Reckoning — kill troops + morale ─────────────────
        private static void CastStormsReckoning(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                var target = NearestHostileParty(MobileParty.MainParty.GetPosition2D, MobileParty.MainParty.MapFaction, 60f);
                if (target == null) { Msg("Storm's Reckoning — no hostile host within reach."); return; }
                int toKill = (int)((6 + _rng.Next(10)) * mult);
                int killed = 0;
                var troops = target.MemberRoster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                for (int i = 0; i < toKill && troops.Count > 0; i++)
                {
                    int idx = _rng.Next(troops.Count);
                    try { target.MemberRoster.AddToCounts(troops[idx].Character, -1); killed++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                try { target.RecentEventsMorale -= 25f * mult; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                Msg(killed > 0
                    ? $"Storm's Reckoning — the sky strikes {target.Name}. {killed} soldier{(killed != 1 ? "s" : "")} fall where they stood."
                    : $"Storm's Reckoning — the sky strikes {target.Name}, but finds little to take.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Magma: Scorched Earth — burn food + kill a few troops ───────────────
        private static void CastScorchedEarth(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                var target = NearestHostileParty(MobileParty.MainParty.GetPosition2D, MobileParty.MainParty.MapFaction, 60f);
                if (target == null) { Msg("Scorched Earth — no hostile host within reach."); return; }
                int foodBurned = NatureEffects.RemoveFoodFromRoster(target, (int)(30 * mult));
                int toKill = (int)((2 + _rng.Next(4)) * mult);
                int killed = 0;
                var troops = target.MemberRoster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                for (int i = 0; i < toKill && troops.Count > 0; i++)
                {
                    int idx = _rng.Next(troops.Count);
                    try { target.MemberRoster.AddToCounts(troops[idx].Character, -1); killed++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                Msg($"Scorched Earth — {target.Name}'s wagons catch fire. {foodBurned} food lost" +
                    (killed > 0 ? $", {killed} soldier{(killed != 1 ? "s" : "")} caught in the blaze." : "."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Fog: The Hidden Road — slip the player's own column away, unseen ────
        private static void CastHiddenRoad(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                var party = MobileParty.MainParty;
                Vec2 pos = party.GetPosition2D;
                var threat = MobileParty.All
                    .Where(p => p != null && p.IsActive && p != party
                             && p.MapFaction != null && party.MapFaction != null
                             && FactionManager.IsAtWarAgainstFaction(p.MapFaction, party.MapFaction))
                    .OrderBy(p => (p.GetPosition2D - pos).Length)
                    .FirstOrDefault();
                party.RecentEventsMorale += 10f;
                if (threat == null)
                {
                    Msg("The Hidden Road — the fog rolls out, but no threat presses close enough to matter. (+10 morale)");
                    return;
                }
                Vec2 away = pos - threat.GetPosition2D;
                float len = away.Length;
                if (len < 0.5f) away = new Vec2(1f, 0f); else away *= 1f / len;
                float push = Math.Min(8f, 4f * mult);
                party.Position = new CampaignVec2(pos + away * push, true);
                Msg($"The Hidden Road — a bank of fog swallows the column. {threat.Name} loses your trail. (+10 morale)");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Sandstorm: Shifting Dunes — push a hostile host off its road ────────
        private static void CastShiftingDunes(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                var target = NearestHostileParty(MobileParty.MainParty.GetPosition2D, MobileParty.MainParty.MapFaction, 55f);
                if (target == null) { Msg("Shifting Dunes — no hostile host within reach."); return; }
                Vec2 tPos = target.GetPosition2D;
                Vec2 back = tPos - MobileParty.MainParty.GetPosition2D;
                float len = back.Length;
                if (len < 0.5f) back = new Vec2(1f, 0f); else back *= 1f / len;
                float push = Math.Min(10f, 5f * mult);
                target.Position = new CampaignVec2(tPos + back * push, true);
                try { target.RecentEventsMorale -= 20f * mult; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                Msg($"Shifting Dunes — the ground itself turns beneath {target.Name}. Days of the march undone.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Mire: The Sinking Road — burn food and swallow their ground ─────────
        private static void CastSinkingRoad(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                var target = NearestHostileParty(MobileParty.MainParty.GetPosition2D, MobileParty.MainParty.MapFaction, 55f);
                if (target == null) { Msg("The Sinking Road — no hostile host within reach."); return; }
                int foodLost = NatureEffects.RemoveFoodFromRoster(target, (int)(20 * mult));
                Vec2 tPos = target.GetPosition2D;
                Vec2 back = tPos - MobileParty.MainParty.GetPosition2D;
                float len = back.Length;
                if (len < 0.5f) back = new Vec2(1f, 0f); else back *= 1f / len;
                float push = Math.Min(6f, 3f * mult);
                target.Position = new CampaignVec2(tPos + back * push, true);
                Msg($"The Sinking Road — the road beneath {target.Name} gives way. {foodLost} food lost to the mud, days undone.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Ice: The Long Stillness — pure morale/standing, no blade drawn ──────
        private static void CastLongStillness(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                var target = NearestHostileParty(MobileParty.MainParty.GetPosition2D, MobileParty.MainParty.MapFaction, 60f);
                if (target == null) { Msg("The Long Stillness — no hostile host within reach."); return; }
                try { target.RecentEventsMorale -= 45f * mult; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                var tClan = target.LeaderHero?.Clan;
                int influenceLost = 0;
                if (tClan != null)
                {
                    influenceLost = (int)(15f * mult);
                    tClan.Influence = Math.Max(0f, tClan.Influence - influenceLost);
                }
                Msg($"The Long Stillness — {target.Name} freezes where it stands. Not a blade drawn, and their nerve is gone." +
                    (influenceLost > 0 ? $" -{influenceLost} influence." : ""));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Fire: Emberfall (mirror of the old Ashstorm) ───────────────────────────
        private static void CastEmberfall(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos     = MobileParty.MainParty.GetPosition2D;
                var  playerFaction = MobileParty.MainParty.MapFaction;

                var target = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle) && s.Town != null && s.MapFaction != null
                             && FactionManager.IsAtWarAgainstFaction(s.MapFaction, playerFaction)
                             && (s.GetPosition2D - playerPos).Length < 50f)
                    .OrderBy(s => (s.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Emberfall — no enemy settlement within reach."); return; }

                int toKill = (int)((10 + _rng.Next(20)) * mult);
                int killed = 0;
                var garrison = target.Town?.GarrisonParty?.MemberRoster;
                if (garrison != null)
                {
                    var troops = garrison.GetTroopRoster()
                        .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                    for (int i = 0; i < toKill && troops.Count > 0; i++)
                    {
                        int idx = _rng.Next(troops.Count);
                        try { garrison.AddToCounts(troops[idx].Character, -1); killed++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
                try { target.Town.FoodStocks -= (int)(150f * mult); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { target.Town.Prosperity = Math.Max(100f, target.Town.Prosperity - (int)(250f * mult)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { target.Town.Security   = Math.Max(0f,   target.Town.Security   - (int)(25f  * mult)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                string killLine = killed > 0 ? $" {killed} garrison soldier{(killed != 1 ? "s" : "")} consumed." : "";
                Msg($"Emberfall — fire rains over {target.Name}.{killLine} Food burns. The walls remember it.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Wind: Scattering Gale (disorder nearby hostile parties) ────────────────
        private static void CastScatteringGale(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                int morale = (int)(40f * mult);
                int scattered = 0;
                foreach (MobileParty p in MobileParty.All.ToList())
                {
                    if (!p.IsActive || p == MobileParty.MainParty) continue;
                    try { if (!FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)) continue; } catch { continue; }
                    if ((p.GetPosition2D - playerPos).Length > 65f) continue;
                    try { p.RecentEventsMorale -= morale; scattered++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                Msg(scattered > 0
                    ? $"Scattering Gale — the high wind comes down. {scattered} enemy {(scattered == 1 ? "host is" : "hosts are")} thrown into disorder. -{morale} morale."
                    : "Scattering Gale — the wind rises, but finds no enemy host nearby.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Earth: Deeproot Blight (mirror of the old Plague/Wither) ───────────────
        private static void CastDeeprootBlight(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var playerFaction = MobileParty.MainParty.MapFaction;
                var target = Settlement.All
                    .Where(s => s.IsVillage && s.Village != null && s.MapFaction != null
                             && s.MapFaction != playerFaction
                             && FactionManager.IsAtWarAgainstFaction(s.MapFaction, playerFaction))
                    .OrderBy(s => (s.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Deeproot Blight — no enemy villages found."); return; }
                float before    = target.Village.Hearth;
                float reduction = 0.20f * mult;
                target.Village.Hearth = Math.Max(10f, before * (1f - reduction));
                Msg($"Deeproot Blight — the root-rot reaches {target.Name}. Hearth reduced by {(int)(reduction * 100f)}%.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Water: Tidewash (mirror of the old Inspire/Kindle) ─────────────────────
        private static void CastTidewash(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                int morale = (int)(40f * mult);
                MobileParty.MainParty.RecentEventsMorale += morale;
                int healPerTroop = Math.Max(1, (int)(8f * mult));
                var roster = MobileParty.MainParty.MemberRoster;
                var wounded = roster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.WoundedNumber > 0).ToList();
                int roused = 0;
                foreach (var entry in wounded)
                {
                    int heal = Math.Min(entry.WoundedNumber, healPerTroop);
                    try { roster.AddToCounts(entry.Character, 0, false, -heal); roused += heal; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                Msg(roused > 0
                    ? $"Tidewash — still water runs through your column. +{morale} morale, {roused} soldier{(roused != 1 ? "s" : "")} rise from their wounds."
                    : $"Tidewash — still water runs through your column. +{morale} morale.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Spirit: Farsight (mirror of the old Clairvoyance) ──────────────────────
        private static void CastFarsight(float mult)
        {
            try
            {
                if (Hero.MainHero?.Clan?.Kingdom != null)
                {
                    int influence = (int)(25f * mult);
                    Hero.MainHero.Clan.Influence += influence;
                    Msg($"Farsight — the threads of power revealed. +{influence} influence.");
                }
                else
                {
                    int gold = (int)(700f * mult);
                    Hero.MainHero.ChangeHeroGold(gold);
                    Msg($"Farsight — no throne to bend, but the fire finds other currents. +{gold} gold.");
                }
            }
            catch { Msg("Farsight — insight granted."); }

            // Reveal any pending NPC scheme against the player and offer to sever it.
            try
            {
                if (SchemeSystem.TryGetSchemeAgainstPlayer(out Hero schemer, out SchemeType schemeType))
                {
                    string schemerName = schemer?.Name?.ToString() ?? "someone";
                    string typeName    = schemeType.ToString();
                    MageKnowledge._deferredInquiry = () =>
                    {
                        try
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                "The Fire Sees Further",
                                $"The threads do not lie. {schemerName} has set a {typeName} scheme against you — it has not yet resolved.\n\nPay 2000 gold now to sever it before it reaches you?",
                                true, true,
                                "Pay 2000 gold — cancel the scheme",
                                "Let it play out",
                                () =>
                                {
                                    if (Hero.MainHero != null && Hero.MainHero.Gold >= 2000)
                                    {
                                        Hero.MainHero.ChangeHeroGold(-2000);
                                        SchemeSystem.CancelSchemesFromInstigator(schemer);
                                        Msg("The scheme collapses before it begins. 2000 gold spent.");
                                    }
                                    else
                                    {
                                        Msg("Not enough gold — the scheme remains in motion.");
                                    }
                                },
                                () => { Msg("You know it is coming. That is something."); }));
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    };
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void Msg(string text)
            => InformationManager.DisplayMessage(new InformationMessage(text, new Color(0.85f, 0.7f, 0.45f)));
    }
}
