// =============================================================================
// ASH AND EMBER — AmbientRemarks.cs
// Two non-intrusive ambient systems:
//
//   Campfire Vignettes — a single-line observation fires on daily tick when
//   the player is outside a settlement (~20% chance, 3-day cooldown). Three
//   pools: general travel, mage-specific, and cold-touched (high whisper tier).
//
//   Companion Remarks — a companion makes an unprompted world-aware comment
//   when the player enters a settlement (~25% chance, 3-day cooldown). The
//   remark is drawn from a trait-specific pool (Valor/Mercy/Calculating/
//   Honor/Generosity/cynical/default) and weighted toward active world state
//   (Ashen presence, nearby war, player aging).
//
// Both systems are purely cosmetic — no mechanics, no save keys needed.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal static class AmbientRemarks
    {
        private static readonly Random _rng = new Random();
        private static readonly Color  _dim = new Color(0.65f, 0.60f, 0.52f);

        private static int _campfireCooldown   = 0;
        private static int _companionCooldown  = 0;

        // ── Public API ────────────────────────────────────────────────────────

        internal static void ResetForNewGame()
        {
            _campfireCooldown  = 0;
            _companionCooldown = 0;
        }

        internal static void DailyTick()
        {
            if (_campfireCooldown  > 0) _campfireCooldown--;
            if (_companionCooldown > 0) _companionCooldown--;
            TryFireCampfireVignette();
        }

        // Called from SettlementEncounters.OnPartyEnteredSettlement.
        internal static void CheckCompanionRemark(Settlement s)
        {
            if (_companionCooldown > 0) return;
            if (_rng.Next(100) >= 25)  return;
            try { FireCompanionRemark(s); } catch { }
        }

        // ── Campfire vignette ─────────────────────────────────────────────────

        private static void TryFireCampfireVignette()
        {
            try
            {
                if (_campfireCooldown > 0) return;
                if (Hero.MainHero?.CurrentSettlement != null) return;
                if (_rng.Next(100) >= 20) return;

                _campfireCooldown = 3;

                bool isMage      = MageKnowledge.IsMage;
                bool isOld       = isMage && Hero.MainHero != null && (int)Hero.MainHero.Age >= 55;
                int  whisperTier = isMage ? MageKnowledge.WhisperTier : 0;

                string line;
                if (isMage && _rng.Next(3) != 0)
                    line = GetMageVignette(isOld, whisperTier);
                else
                    line = GetGeneralVignette();

                ShowQuick(line);
            }
            catch { }
        }

        private static string GetGeneralVignette()
        {
            string[] pool = {
                "A soldier at the fire is carving something from a piece of wood. He won't say what it is.",
                "The fire goes low and no one moves to add wood. For a moment the dark presses in close.",
                "Two of your men are laughing about something that happened three campaigns ago. You were there. You don't remember it that way.",
                "An owl calls from the treeline. Your sentry answers by mistake and then says nothing about it.",
                "The road is quiet tonight. Too quiet is a soldier's saying. You've stopped saying it because it's always true.",
                "Someone in the column started humming an old tune. By the time you noticed, half the camp was singing it quietly.",
                "A dog followed the column for three hours before turning back. The men named it before it left.",
                "The fire settles and the sparks go up. For a moment they look like a map. Then they go dark.",
                "Your horse is not asleep. It is watching something in the dark that you cannot see.",
                "One of your sergeants is sitting apart, cleaning armour that doesn't need cleaning. You leave him to it.",
                "The stars are particularly clear tonight. An old campaigner says that always means something. He has been saying that for thirty years.",
                "The wind changes direction three times in an hour. The men don't like it. They won't say why.",
                "Someone lost a boot somewhere on today's march. It has not been found. Nobody is admitting to losing it.",
                "A fire in a distant village is visible from camp. You watch it for a while. It does nothing unusual.",
                "Two of your soldiers are arguing quietly about a card game that ended days ago. The argument has become philosophical.",
            };
            return pool[_rng.Next(pool.Length)];
        }

        private static string GetMageVignette(bool isOld, int whisperTier)
        {
            if (whisperTier >= 3)
            {
                string[] cold = {
                    "The campfire leans toward you when you sit close. It has been doing that more often.",
                    "Someone in your column is watching your hands. You catch them at it twice. They don't look away.",
                    "You wake in the night and the fire is out, but your hands are warm. You do not mention this to anyone.",
                    "The ash from the campfire settles into a shape. You scatter it before anyone else sees.",
                    "The cold feels closer tonight. Not the air — something behind the air. It has been like this for a while now.",
                    "Your shadow on the tent wall doesn't quite match your movements. You watch it for a while. It catches up.",
                };
                return cold[_rng.Next(cold.Length)];
            }
            if (isOld)
            {
                string[] aged = {
                    "Your reflection in a still puddle stops you. You look older than you felt this morning.",
                    "A young soldier asks how long you have been campaigning. You give a number. He goes quiet.",
                    "The fire is warm. You notice things like that now — small warmths. They matter more than they used to.",
                    "You ache in the morning. Not from injury. Just time. The fire helps.",
                    "One of your men was born after you first carried the fire. You are trying not to think about that.",
                    "The fire you lit tonight took no effort. That ease is not reassurance. You know what it means.",
                };
                return aged[_rng.Next(aged.Length)];
            }
            {
                string[] mage = {
                    "The fire burns a little too evenly tonight. No wind would explain it.",
                    "You catch yourself staring into the campfire for longer than you meant to.",
                    "The flame on your candle goes out. You light it again without thinking. Then you think about how you lit it.",
                    "There is something satisfying about a good fire that has nothing to do with warmth. You know what it is. You don't say it.",
                    "The fire in the hearth at the last inn bent toward you when you passed. The innkeeper did not notice.",
                    "You press your palms together in the dark and feel them warmer than the night allows. You do not find this strange anymore.",
                };
                return mage[_rng.Next(mage.Length)];
            }
        }

        // ── Companion remark ──────────────────────────────────────────────────

        private static void FireCompanionRemark(Settlement s)
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            var companions = Hero.AllAliveHeroes
                .Where(h => h.IsWanderer && h.IsAlive && !h.IsDead
                         && h.PartyBelongedTo == MobileParty.MainParty)
                .ToList();

            if (companions.Count == 0) return;

            var companion = companions[_rng.Next(companions.Count)];
            if (companion == null) return;

            string remark = BuildRemark(companion, s);
            if (string.IsNullOrEmpty(remark)) return;

            _companionCooldown = 3;
            ShowQuick($"{companion.Name}: \"{remark}\"");
        }

        private static string BuildRemark(Hero c, Settlement s)
        {
            try
            {
                int honor       = c.GetTraitLevel(DefaultTraits.Honor);
                int mercy       = c.GetTraitLevel(DefaultTraits.Mercy);
                int valor       = c.GetTraitLevel(DefaultTraits.Valor);
                int calculating = c.GetTraitLevel(DefaultTraits.Calculating);
                int generosity  = c.GetTraitLevel(DefaultTraits.Generosity);

                bool magePlayer  = MageKnowledge.IsMage;
                bool agingPlayer = magePlayer && Hero.MainHero != null && (int)Hero.MainHero.Age >= 55;

                bool ashenActive = false;
                try { ashenActive = Campaign.Current != null &&
                                    Settlement.All.Any(st => st.IsTown && st.MapFaction?.StringId == "ashen_kingdom"); }
                catch { }

                bool nearWar = false;
                try
                {
                    if (s?.MapFaction is Kingdom pk)
                        nearWar = Kingdom.All.Any(k => !k.IsEliminated && k != pk && pk.IsAtWarWith(k));
                }
                catch { }

                // Dominant trait determines pool; ties broken by order below
                if (valor      >= 1) return PickValor(s, ashenActive, nearWar, agingPlayer);
                if (mercy      >= 1) return PickMercy(s, ashenActive);
                if (calculating >= 1) return PickCalculating(s, ashenActive, nearWar);
                if (honor      >= 1) return PickHonor(s, nearWar);
                if (generosity >= 1) return PickGenerosity(s);
                if (honor      <= -1) return PickCynical(s, ashenActive);
                return PickDefault(agingPlayer);
            }
            catch { return ""; }
        }

        private static string PickValor(Settlement s, bool ashen, bool war, bool aging)
        {
            if (ashen)
            {
                string[] p = {
                    "The grey ones are out there. Good. Something worth killing is better than nothing at all.",
                    "I've been watching the road east. The cold things leave a trace — we could follow it.",
                    "The soldiers here look scared. They should look ready. There is a difference.",
                };
                return p[_rng.Next(p.Length)];
            }
            if (war)
            {
                string[] p = {
                    "War on the roads is either an obstacle or an opportunity. I'm still deciding which.",
                    "The walls here have held. I want to know who built them and whether they still know how.",
                    "Armies moving nearby. I can hear it in the way the locals talk about the roads.",
                };
                return p[_rng.Next(p.Length)];
            }
            if (aging)
            {
                string[] p = {
                    "The fire is eating you and you're still swinging. I respect that more than I expected to.",
                    "You fight like someone with something to prove to themselves. Not a bad thing.",
                };
                return p[_rng.Next(p.Length)];
            }
            {
                string[] p = {
                    "These roads are quieter than I'd like. Quiet usually ends badly.",
                    "A place without enemies is just a place. I'm never sure what to do with it.",
                    "The sentries here are bored. Bored sentries miss things.",
                    "I've been watching the patrols. The pattern is off. Either lazy or deliberate.",
                };
                return p[_rng.Next(p.Length)];
            }
        }

        private static string PickMercy(Settlement s, bool ashen)
        {
            bool village = s?.IsVillage == true;
            if (ashen)
            {
                string[] p = {
                    "The cold takes villages first. Lords notice last. That pattern never changes.",
                    "I've been thinking about the people who live near the grey border. Nobody sent them reinforcements.",
                    "Refugees from the grey don't look like fighters. They look like people who ran out of choices.",
                };
                return p[_rng.Next(p.Length)];
            }
            if (village)
            {
                string[] p = {
                    "There's a kind of dignity in places like this. They keep going when lords decide not to.",
                    "These people are still counting what they lost. We shouldn't forget we passed through.",
                    "A village that's still standing is one that held through something. Worth remembering.",
                };
                return p[_rng.Next(p.Length)];
            }
            {
                string[] p = {
                    "The lower quarter here is thin. You can see it in the bread.",
                    "You can tell which part of a city eats well and which doesn't by where the market stalls run out.",
                    "Someone ordered the fields outside this town burned at some point. The soil still shows it.",
                    "The children here look healthy enough. The adults look tired. That's a thing to notice.",
                };
                return p[_rng.Next(p.Length)];
            }
        }

        private static string PickCalculating(Settlement s, bool ashen, bool war)
        {
            if (ashen)
            {
                string[] p = {
                    "The grey ones don't raid randomly. They're clearing routes. Someone is directing that.",
                    "I've been mapping the Ashen advance against the kingdom borders. The correlation is not coincidental.",
                    "The cold kingdom's expansion has been consistent — roughly the same arc each season. That takes planning.",
                };
                return p[_rng.Next(p.Length)];
            }
            if (war)
            {
                string[] p = {
                    "The garrison here is understaffed for the number of roads it guards. Someone doesn't know, or doesn't care.",
                    "Two factions bleeding each other. The third that stays out will inherit what remains.",
                    "Supply lines for the war run through this region. That makes this settlement worth more than it looks.",
                };
                return p[_rng.Next(p.Length)];
            }
            {
                string[] p = {
                    "I've been watching the supply routes. Something doesn't match the troop numbers in the reports.",
                    "The lord who holds this settlement is not spending what it earns. Interesting.",
                    "Three different faction flags have flown over this gate in ten years. The locals have learned to be flexible.",
                    "The market prices here are off. Not wrong enough to matter, but wrong enough to notice.",
                };
                return p[_rng.Next(p.Length)];
            }
        }

        private static string PickHonor(Settlement s, bool war)
        {
            if (war)
            {
                string[] p = {
                    "There are oaths being broken in this war that nobody will remember when it ends. Somebody should.",
                    "The dead here had names. Someone is waiting for them who doesn't know yet.",
                    "A lord who burns fields to deny the enemy is right about tactics and wrong about everything else.",
                };
                return p[_rng.Next(p.Length)];
            }
            {
                string[] p = {
                    "We're being watched by people who don't know if we mean harm. That matters. We should remember it.",
                    "There are oaths here that have gone unfulfilled too long. I can feel it in how people speak about the past.",
                    "The garrison here swore to someone and stayed. That deserves something.",
                    "A promise made in a place like this carries weight. The walls remember.",
                };
                return p[_rng.Next(p.Length)];
            }
        }

        private static string PickGenerosity(Settlement s)
        {
            bool village = s?.IsVillage == true;
            if (village)
            {
                string[] p = {
                    "We could leave something here. We have more than these people do.",
                    "A village this size feeds three hundred mouths and asks nothing of anyone. Worth helping.",
                    "The children here look healthy enough, but the adults look tired. Something we could address.",
                };
                return p[_rng.Next(p.Length)];
            }
            {
                string[] p = {
                    "There's wealth in this city that could reach twenty villages. That it doesn't is a choice.",
                    "After a war the coin goes back to the lords. The villages stay hollow. Every time.",
                    "A merchant told me he gives to the poor quarter on festival days. Once a year. He said it proudly.",
                    "The guild here pays its workers under the rate. Not by much. Just enough to notice if you're looking.",
                };
                return p[_rng.Next(p.Length)];
            }
        }

        private static string PickCynical(Settlement s, bool ashen)
        {
            if (ashen)
            {
                string[] p = {
                    "The grey ones don't pretend to care about the smallfolk. At least they're honest about it.",
                    "People act surprised when the cold takes a settlement. They weren't watching. Nobody was.",
                    "The grey spread while three lords argued about who owned the road it spread along.",
                };
                return p[_rng.Next(p.Length)];
            }
            {
                string[] p = {
                    "These people trust us because they can't afford not to. Useful.",
                    "Every lord claims they protect the people. Every lord means they own the people.",
                    "The innkeeper smiled at us. He'll smile the same way at the next company through. It's not personal.",
                    "Everyone in this room wants something they're not saying. Including us.",
                };
                return p[_rng.Next(p.Length)];
            }
        }

        private static string PickDefault(bool aging)
        {
            if (aging)
            {
                string[] p = {
                    "You look different than when we started. I don't mean tired.",
                    "I've been counting the grey in your hair. Tactfully I decided not to mention it — and then did.",
                    "I've ridden with a lot of people. Not many that age like you do.",
                };
                return p[_rng.Next(p.Length)];
            }
            {
                string[] p = {
                    "Strange mood in this place today. The locals know something we don't.",
                    "I've been through places like this before. There's something under the surface.",
                    "The road ahead looks clear. That's not always a good sign.",
                    "These people are tired. The kind of tired that doesn't go away after one night's sleep.",
                    "Something about this place feels like it's waiting. Can't say for what.",
                    "I don't know what happened here, but the dogs know.",
                };
                return p[_rng.Next(p.Length)];
            }
        }

        // ── Shared helper ─────────────────────────────────────────────────────

        private static void ShowQuick(string text)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch { try { InformationManager.DisplayMessage(new InformationMessage(text, _dim)); } catch { } }
        }
    }
}
