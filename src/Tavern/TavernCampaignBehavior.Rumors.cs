// =============================================================================
// ASH AND EMBER — TavernCampaignBehavior.Rumors.cs
// "Listen for rumours" and "Spend an evening" — two non-drinking tavern
// activities that surface world-state information and give a morale bump.
// Partial of TavernCampaignBehavior (shared static state lives in TavernCampaignBehavior.cs).
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
    public partial class TavernCampaignBehavior
    {
        // ── Listen for rumours ────────────────────────────────────────────────
        internal static void TryListenForRumors()
        {
            const int cost = 30;
            if ((Hero.MainHero?.Gold ?? 0) < cost)
            {
                Msg("You don't have the coin to loosen anyone's tongue tonight.", BadColor);
                return;
            }
            try { Hero.MainHero?.ChangeHeroGold(-cost); } catch { }

            var rumors = BuildRumors();
            string body = string.Join("\n\n", rumors);

            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "What the Common Room Knows",
                    body,
                    true, false,
                    "Much obliged.",
                    "",
                    () => { try { GameMenu.SwitchToMenu("ldm_tavern_menu"); } catch { } },
                    null
                ));
            }
            catch
            {
                try { GameMenu.SwitchToMenu("ldm_tavern_menu"); } catch { }
            }
        }

        private static List<string> BuildRumors()
        {
            var buckets = new List<string>();
            try { AddWarRumor(buckets); }        catch { }
            try { AddDeclineRumor(buckets); }    catch { }
            try { AddAshenRumor(buckets); }      catch { }
            try { AddProsperityRumor(buckets); } catch { }
            try { AddCaptiveRumor(buckets); }    catch { }
            try { AddSeasonRumor(buckets); }     catch { }

            // Shuffle what world-state produced
            for (int i = buckets.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                string tmp = buckets[i]; buckets[i] = buckets[j]; buckets[j] = tmp;
            }

            // Pad to 3 with generic banter
            var banterShuffled = _genericBanter.OrderBy(_ => _rng.Next()).ToList();
            int bi = 0;
            while (buckets.Count < 3 && bi < banterShuffled.Count)
                buckets.Add(banterShuffled[bi++]);

            return buckets.Take(3).ToList();
        }

        private static void AddWarRumor(List<string> buckets)
        {
            if (Campaign.Current == null) return;
            var kingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && k.StringId != "ashen_kingdom")
                .ToList();
            for (int i = 0; i < kingdoms.Count; i++)
            {
                for (int j = i + 1; j < kingdoms.Count; j++)
                {
                    if (!kingdoms[i].IsAtWarWith(kingdoms[j])) continue;
                    string a = kingdoms[i].Name?.ToString() ?? "one kingdom";
                    string b = kingdoms[j].Name?.ToString() ?? "another";
                    string[] templates = {
                        $"{a} and {b} have been trading blows for weeks. Merchants are going the long way around.",
                        $"The war between {a} and {b} is starving villages on both sides of the line.",
                        $"{a}'s levies went east a fortnight ago. Nobody's betting on when they come back.",
                        $"Caravans from {b} stopped coming through when {a} started raiding the passes.",
                        $"You can tell how the war between {a} and {b} is going by how much the grain costs here.",
                    };
                    buckets.Add(templates[_rng.Next(templates.Length)]);
                    return;
                }
            }
        }

        private static void AddDeclineRumor(List<string> buckets)
        {
            if (Campaign.Current == null) return;
            var thin = Kingdom.All
                .Where(k => !k.IsEliminated && k.StringId != "ashen_kingdom")
                .OrderBy(k => k.Settlements.Count(s => s.IsTown))
                .FirstOrDefault();
            if (thin == null) return;
            int n = thin.Settlements.Count(s => s.IsTown);
            if (n > 5) return;
            string name   = thin.Name?.ToString()        ?? "a kingdom";
            string leader = thin.Leader?.Name?.ToString() ?? "their lord";
            string[] templates = {
                $"{name}'s lords are selling their horses. A bad sign when a kingdom starts selling horses.",
                $"The banner of {name} flies from fewer towers each season. Men don't say it out loud.",
                $"Word from the road: {name} is down to {n} holding{(n != 1 ? "s" : "")}. {leader} hasn't been seen at court.",
                $"{name} is thinning. The kind of thinning that doesn't reverse on its own.",
                $"Heard {leader} of {name} can barely pay their garrison. Nobody knows for how much longer.",
            };
            buckets.Add(templates[_rng.Next(templates.Length)]);
        }

        private static void AddAshenRumor(List<string> buckets)
        {
            if (Campaign.Current == null) return;
            int ashenTowns = 0;
            try { ashenTowns = Settlement.All.Count(s => s.IsTown && s.MapFaction?.StringId == "ashen_kingdom"); }
            catch { }
            if (ashenTowns == 0) return;
            string anchor = Settlement.All
                .Where(s => s.IsTown && s.MapFaction?.StringId == "ashen_kingdom" && s.Town != null)
                .OrderByDescending(s => s.Town.Prosperity)
                .Select(s => s.Name?.ToString())
                .FirstOrDefault() ?? "the grey reaches";
            string[] templates = {
                $"The grey ones have settled in {anchor}. The traders won't go near it anymore.",
                $"Someone found tracks near {anchor} at dawn. No boots — just a groove in the frost. The soldiers say not to ask.",
                $"A merchant came through with grey dust on his cart and no explanation. He didn't stay long.",
                $"The cold things hold {ashenTowns} settlement{(ashenTowns != 1 ? "s" : "")} now. Nobody agrees on what to do about it.",
                $"Travellers from {anchor} say the fires there burn wrong — the colour is off. Not orange. Grey.",
            };
            buckets.Add(templates[_rng.Next(templates.Length)]);
        }

        private static void AddProsperityRumor(List<string> buckets)
        {
            var s = Settlement.CurrentSettlement;
            if (s == null || !s.IsTown || s.Town == null) return;
            float pros  = s.Town.Prosperity;
            string name = s.Name?.ToString() ?? "this place";
            if (pros >= 5000)
            {
                string[] rich = {
                    $"{name} has money right now. The kind that makes people do things they later regret.",
                    $"Three merchant houses opened offices in {name} this season. The smart money follows.",
                    $"Silk is moving through {name} again. Whatever was blocking the eastern routes is apparently settled.",
                    $"The guilds here are hiring. When the guilds hire, the city is doing well — or doing something.",
                };
                buckets.Add(rich[_rng.Next(rich.Length)]);
            }
            else if (pros < 2000)
            {
                string[] poor = {
                    $"The market in {name} is thin. Three stalls closed last month.",
                    $"Food prices here have gone up twice since summer. Nobody's happy about it.",
                    $"{name} is struggling. The merchants are always the first to know — and the first to leave.",
                    $"Half the inns in {name} are running short-staffed. Work dried up faster than the coin did.",
                };
                buckets.Add(poor[_rng.Next(poor.Length)]);
            }
            else
            {
                string[] mid = {
                    $"{name} is holding steady. Steady isn't good, but it isn't bad — just the world not ending today.",
                    $"The trade through {name} is moving. That's more than most places can say right now.",
                    $"The grain merchant here said business is 'acceptable'. From a grain merchant, that means comfortable.",
                };
                buckets.Add(mid[_rng.Next(mid.Length)]);
            }
        }

        private static void AddCaptiveRumor(List<string> buckets)
        {
            if (Campaign.Current == null) return;
            var captive = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h.IsPrisoner
                         && h != Hero.MainHero && h.Clan?.Kingdom != null)
                .OrderBy(_ => _rng.Next())
                .FirstOrDefault();
            if (captive == null) return;
            string name = captive.Name?.ToString() ?? "a lord";
            string[] templates = {
                $"{name} has been in someone's dungeon for a while now. The ransom request came back unsigned.",
                $"Heard {name} got taken on the road. Their clan is quiet about it — which means something.",
                $"{name} is a prisoner somewhere. Their household isn't saying where. That's a bad sign.",
                $"Word is {name} was captured last month. Their lands are already being watched by neighbours.",
            };
            buckets.Add(templates[_rng.Next(templates.Length)]);
        }

        private static void AddSeasonRumor(List<string> buckets)
        {
            string rumor;
            try
            {
                var season = CampaignTime.Now.GetSeasonOfYear;
                switch (season)
                {
                    case CampaignTime.Seasons.Winter:
                    {
                        string[] w = {
                            "Snow on the northern road. The passes will close if it keeps up.",
                            "The cold came early this year. Old men say it means something. It always means something.",
                            "Wood prices tripled since the frost set in. The poor quarters will be burning furniture by midwinter.",
                        };
                        rumor = w[_rng.Next(w.Length)];
                        break;
                    }
                    case CampaignTime.Seasons.Spring:
                    {
                        string[] sp = {
                            "The first caravans are moving again. Always busy, first thaw. Always dangerous.",
                            "The roads are mud to the ankle right now. Armies hate it. Bandits love it.",
                            "Spring means conscription letters in three kingdoms. The young men are looking at maps.",
                        };
                        rumor = sp[_rng.Next(sp.Length)];
                        break;
                    }
                    case CampaignTime.Seasons.Summer:
                    {
                        string[] su = {
                            "Dust on every road south of the mountains. Men are drinking more water than they have.",
                            "The eastern passes are clear. Good for trade — also good for armies.",
                            "Long summer. Good harvest if the lords don't burn half of it fighting each other.",
                        };
                        rumor = su[_rng.Next(su.Length)];
                        break;
                    }
                    default: // Autumn
                    {
                        string[] au = {
                            "The harvest came in thin this year. Not ruined — thin. Enough to argue over.",
                            "Lords are stockpiling grain before winter. The merchants know exactly what that means.",
                            "Leaves are turning and the road north gets colder by the day. Last caravans of the season are moving now.",
                        };
                        rumor = au[_rng.Next(au.Length)];
                        break;
                    }
                }
            }
            catch { return; }
            buckets.Add(rumor);
        }

        private static readonly string[] _genericBanter = {
            "A mercenary captain came through last week. Paid in full, no trouble. Left before anyone could ask questions.",
            "There's a lord's son somewhere in this city who owes three gamblers money they don't know how to collect.",
            "Someone lost a sword on the road south. A good sword. Nobody's claiming it.",
            "The road guards were doubled last week. Nobody official will say why.",
            "Two priests got into a debate here last night about which flame burns truer. Nobody won.",
            "There's a woman in the lower quarter who reads fire. Not for show — she reads it. The soldiers go to her before campaigns.",
            "A messenger came through riding hard. Changed horse twice. Didn't say where he was going.",
            "The innkeeper two streets over found a coin in his well that he can't identify. Older than the city, he says.",
            "A lord's hunting party came through three days ago. They came back with fewer horses than they left with. They're not talking about it.",
            "Someone in a red cloak has been asking about the eastern road for three days. Nobody knows them.",
            "The ferry downstream hasn't run in two weeks. The ferryman says the water is wrong. Won't say how.",
            "A whole caravan showed up without its captain. The goods were there. The man wasn't. The guards don't remember him leaving.",
        };

        // ── Spend an evening ──────────────────────────────────────────────────
        internal static int EveningCost()
        {
            int size = 1;
            try { size = Math.Max(1, MobileParty.MainParty?.MemberRoster?.TotalManCount ?? 1); } catch { }
            return Math.Max(50, size * 10);
        }

        internal static void SpendEvening()
        {
            int cost = EveningCost();
            if ((Hero.MainHero?.Gold ?? 0) < cost)
            {
                Msg("You don't have the coin to treat the whole party tonight.", BadColor);
                return;
            }
            try { Hero.MainHero?.ChangeHeroGold(-cost); } catch { }

            var pool = _eveningBanter.OrderBy(_ => _rng.Next()).ToList();
            _innStayLine1 = pool.Count > 0 ? pool[0] : "The fire in the hearth holds the dark at bay.";
            _innStayLine2 = pool.Count > 1 ? pool[1] : "The common room is warm.";
            _innStayHoursTotal   = 8f;
            _innStayHoursElapsed = 0f;
            _innStayDone         = false;

            try { GameMenu.SwitchToMenu("ldm_inn_stay_menu"); } catch { }
        }

        // ── The old green (rare nature weeds) ─────────────────────────────────
        internal static int WeedCost() => 150;

        // Smoke the rare weeds: it leaves you tired (−10% of your health) but for a
        // day the living world counts you as one of its own — each nature draw has a
        // 30% chance to cost the land nothing. Then you drowse a few hours.
        internal static void SmokeNatureWeeds()
        {
            int cost = WeedCost();
            if ((Hero.MainHero?.Gold ?? 0) < cost)
            {
                Msg("You haven't the coin for a pouch of the old green.", BadColor);
                return;
            }
            try { Hero.MainHero?.ChangeHeroGold(-cost); } catch { }

            // The toll on the body: ten percent of your full health, never lethal.
            int hpLoss = 0;
            try
            {
                var h = Hero.MainHero;
                if (h != null)
                {
                    hpLoss = Math.Max(1, (int)(h.MaxHitPoints * 0.10f));
                    h.HitPoints = Math.Max(1, h.HitPoints - hpLoss);
                }
            }
            catch { }

            // The gift: a day's communion with the living world.
            try { NatureKnowledge.GrantWeedBlessing(24.0); } catch { }

            // …and the drowse. A few tired hours pass (reuses the wait menu).
            _weedRest          = true;
            _soberHoursTotal   = 5f + _rng.Next(3);   // 5-7 hours
            _soberHoursElapsed = 0f;
            _soberDone         = false;

            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Old Green",
                    "The pouch holds dried leaves the colour of deep moss, threaded with pale root and a flower " +
                    "you have no name for. The keeper sells it without meeting your eye.\n\n" +
                    "You smoke it slow by the fire. The smoke is bitter, then sweet, then nothing. The room recedes. " +
                    "Your limbs grow heavy and far away — but underneath the heaviness something opens, and for the " +
                    "first time you feel the land breathing under the floorboards, under the town, going down and " +
                    "down. You are tired. You are also, briefly, part of it all.\n\n" +
                    $"[−{hpLoss} health · for one day, 30% of your nature draws cost the land nothing]",
                    true, false, "Let it take you.", "",
                    () => { try { GameMenu.SwitchToMenu("ldm_tavern_sober_up"); } catch { } },
                    null));
            }
            catch
            {
                try { GameMenu.SwitchToMenu("ldm_tavern_sober_up"); } catch { }
            }
        }

        private static readonly string[] _eveningBanter = {
            "A soldier at the next table is teaching a younger one to play cards. The younger one is better and pretending not to be.",
            "The innkeeper's wife is singing in the back room. No one mentions it. Everyone is listening.",
            "Two merchants are arguing about a road they both say belongs to their town. Neither will back down. Neither will leave.",
            "A man in the corner has been nursing the same drink since you arrived. He is watching the door.",
            "Three soldiers on leave are making promises they won't remember in the morning. The barmaid has heard all of them before.",
            "An old man is telling the table beside you about a battle that happened before most of them were born. He was there. You can tell.",
            "Someone is playing a stringed instrument badly in the street outside. Inside, no one says anything about it.",
            "Two women at the bar are speaking in low voices and laughing. Whatever they are laughing about, you are not supposed to hear it.",
            "A dog has positioned itself under the largest table and is managing the situation with professional calm.",
            "A priest and a merchant are sharing a bottle and getting along in the way people only do after the third cup.",
            "The fire in the hearth keeps guttering. The innkeeper blames the chimney. Everyone else blames something they don't name.",
            "A young messenger is trying to stay awake until his horse is ready. He is losing. The table is keeping him upright.",
            "A bearded man at the bar is crying very quietly. No one has asked him why. Probably they know.",
            "The fire snaps and a coal rolls out. Three people reach for the tongs at the same time. The fourth person laughs.",
            "A child has fallen asleep under a bench at the back of the room. None of the adults seem to be responsible for her.",
        };
    }
}
