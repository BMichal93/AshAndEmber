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
                case MagicElement.Wind:   return "Scattering Gale";
                case MagicElement.Earth:  return "Deeproot Blight";
                case MagicElement.Water:  return "Tidewash";
                case MagicElement.Spirit: return "Farsight";
                default:                  return "Emberfall";
            }
        }

        public static string Lore(MagicElement el)
        {
            switch (el)
            {
                case MagicElement.Wind:   return "Call the high wind down on a hostile host until their order comes apart and their nerve with it.";
                case MagicElement.Earth:  return "Reach through the soil to a hostile village and let the root-rot take its hearth.";
                case MagicElement.Water:  return "Draw the quiet of still water through your own column — wounds close, hearts steady.";
                case MagicElement.Spirit: return "Send the fire further than sight, reading the currents of power and the knives set behind your back.";
                default:                  return "Pull the fire high and let it fall upon a hostile settlement — garrison, granary, and wall alike.";
            }
        }

        // ── Dispatch ──────────────────────────────────────────────────────────────
        public static void Execute(MagicElement el, float mult)
        {
            switch (el)
            {
                case MagicElement.Wind:   CastScatteringGale(mult); break;
                case MagicElement.Earth:  CastDeeprootBlight(mult);  break;
                case MagicElement.Water:  CastTidewash(mult);        break;
                case MagicElement.Spirit: CastFarsight(mult);        break;
                default:                  CastEmberfall(mult);       break;
            }
            try { MageKnowledge.RewardCastSkill(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
