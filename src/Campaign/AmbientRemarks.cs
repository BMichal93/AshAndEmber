// =============================================================================
// ASH AND EMBER — AmbientRemarks.cs
// Two non-intrusive ambient systems:
//
//   Campfire Vignettes — a single-line observation fires on daily tick when
//   the player is outside a settlement (~12% chance, 5-day cooldown). Pools:
//   general travel, mage-specific, aged, cold-touched (high whisper tier),
//   plus situational pools (wounded column, war, winter, riding alone) that
//   can pre-empt the flat pools when the world state actually warrants it.
//
//   Companion Remarks — a companion makes an unprompted world-aware comment
//   on settlement entry (~25% chance, 3-day cooldown). The remark is drawn
//   from a trait-specific pool (Valor/Mercy/Calculating/Honor/Generosity/
//   cynical/default) with world-state branches AND five relation tiers:
//
//     Very negative  (≤ −50)  — hostile, pointed, implies distrust
//     Negative       (−49 to −10) — clipped, professional, cool
//     Neutral        (−9 to +9)   — matter-of-fact, observational
//     Positive       (+10 to +49) — warm, collegial, "we" framing
//     Very positive  (≥ +50)  — personal, familiar, protective
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

        // Campfire vignettes fire less often than they used to — this is ambient
        // colour, not a slot machine. Roughly one line every ~13 days on average
        // (5-day cooldown, then a 12% roll per day until one lands).
        private const int CampfireCooldownDays   = 5;
        private const int CampfireChancePercent  = 12;

        private static int _campfireCooldown  = 0;
        private static int _companionCooldown = 0;

        private enum RelationTier { VeryNegative, Negative, Neutral, Positive, VeryPositive }

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
            if (_rng.Next(100) >= 25)   return;
            try { FireCompanionRemark(s); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Relation tier ─────────────────────────────────────────────────────

        private static RelationTier GetRelationTier(Hero companion)
        {
            int rel = 0;
            try { rel = (int)companion.GetRelationWithPlayer(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (rel <= -50) return RelationTier.VeryNegative;
            if (rel <= -10) return RelationTier.Negative;
            if (rel <=   9) return RelationTier.Neutral;
            if (rel <=  49) return RelationTier.Positive;
            return RelationTier.VeryPositive;
        }

        // ── Campfire vignette ─────────────────────────────────────────────────

        private static void TryFireCampfireVignette()
        {
            try
            {
                if (_campfireCooldown > 0) return;
                if (Hero.MainHero?.CurrentSettlement != null) return;
                if (_rng.Next(100) >= CampfireChancePercent) return;

                _campfireCooldown = CampfireCooldownDays;

                bool isMage      = MageKnowledge.IsMage;
                bool isOld       = isMage && Hero.MainHero != null && (int)Hero.MainHero.Age >= 55;
                int  whisperTier = isMage ? MageKnowledge.WhisperTier : 0;

                // Situational lines (wounds, war, winter, riding alone) take priority
                // over the flat pools when the world actually warrants them — but
                // only sometimes, so the vignettes stay varied rather than becoming
                // a status readout.
                string line = GetSituationalVignette()
                           ?? (isMage && _rng.Next(3) != 0 ? GetMageVignette(isOld, whisperTier) : GetGeneralVignette());

                ShowQuick(line);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Situational campfire lines ───────────────────────────────────────

        private static string GetSituationalVignette()
        {
            try
            {
                int wounded = 0;
                try { wounded = MobileParty.MainParty?.MemberRoster?.GetTroopRoster().Sum(e => e.WoundedNumber) ?? 0; }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (wounded >= 5 && _rng.Next(2) == 0) return Pick(_woundedVignettes);

                bool atWar = false;
                try
                {
                    var faction = Hero.MainHero?.MapFaction;
                    atWar = faction != null && Kingdom.All.Any(k => !k.IsEliminated && k != faction && faction.IsAtWarWith(k));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (atWar && _rng.Next(2) == 0) return Pick(_warVignettes);

                bool winter = false;
                try { winter = CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Winter; }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (winter && _rng.Next(2) == 0) return Pick(_winterVignettes);

                int partySize = MobileParty.MainParty?.MemberRoster?.TotalManCount ?? 2;
                if (partySize <= 1 && _rng.Next(2) == 0) return Pick(_soloVignettes);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return null;
        }

        private static readonly string[] _woundedVignettes =
        {
            "The wounded groan in their sleep tonight. The healers do what they can, which isn't enough, some nights.",
            "Someone is rationing bandages by firelight. There aren't enough for the whole column.",
            "A soldier bites down on leather while the surgeon works. Nobody sings tonight.",
            "The wagons carrying the wounded creak louder than they should. Every rut in the road is a small cruelty.",
            "The healer hasn't slept in two days. Neither, really, have you.",
        };

        private static readonly string[] _warVignettes =
        {
            "Word travels faster than the column moves. Every village you pass already knows there's a war on.",
            "The men check their gear twice before sleeping. Nobody sleeps deeply when there's a war somewhere close.",
            "A rider passes going the other way, faster than any messenger should be. Nobody stops him to ask why.",
            "Someone in camp is sharpening a blade that doesn't need it. War does that to people.",
            "The scouts report back later than usual tonight. That alone says something.",
        };

        private static readonly string[] _winterVignettes =
        {
            "Frost climbs the tent ropes overnight. The horses stand closer together than they used to.",
            "The cold gets into the boots first, then the bones. Nobody complains about it anymore. It doesn't help.",
            "Snow falls without wind tonight — straight down, patient, like it has somewhere to be.",
            "A soldier's breath hangs in the air long after he's stopped talking.",
            "The fire eats twice the wood it did in autumn and gives back half the warmth.",
        };

        private static readonly string[] _soloVignettes =
        {
            "No one to talk to tonight but the horse, and it isn't much of a conversationalist.",
            "The road feels longer walked alone. You've noticed that before. You'll notice it again.",
            "You count your own footsteps for a while, out of habit more than need.",
            "There's a particular kind of quiet that comes with traveling alone. You've made peace with it, mostly.",
        };

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
                "A crow sits on the supply cart for the whole meal and won't be shooed. Someone eventually names it and stops trying.",
                "The quartermaster counts the coin twice and gets a different number both times. He decides not to mention it.",
                "Someone swears the tree line moved since yesterday's camp. Nobody else saw it. He keeps watching it anyway.",
                "A soldier writes a letter he won't send. You've seen him do it three times this month.",
                "The cook burns the stew and serves it anyway. Nobody complains. Complaining would mean admitting they're hungry enough to care.",
                "One of the mules refuses to cross a perfectly ordinary bridge. It takes four men and a lot of patience.",
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
                    "A soldier asks if you're feeling well. You say yes. He doesn't look convinced, and you're not sure you are either.",
                    "The frost near your bedroll melts in a ring no wider than your shoulders. You've stopped being surprised by it.",
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
                    "A recruit half your age asks for advice. You give it. It sounds like something someone once told you, decades gone.",
                    "You count the years by scars now, not by seasons. There are more scars than there used to be.",
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
                    "A moth circles your hand instead of the lantern. You let it. You understand the impulse.",
                    "The embers hold their shape longer than embers should. You give them a moment before you scatter them.",
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

                RelationTier rel = GetRelationTier(c);

                bool magePlayer  = MageKnowledge.IsMage;
                bool agingPlayer = magePlayer && Hero.MainHero != null && (int)Hero.MainHero.Age >= 55;

                bool ashenActive = false;
                try { ashenActive = Campaign.Current != null &&
                                    Settlement.All.Any(st => st.IsTown && st.MapFaction?.StringId == "ashen_kingdom"); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                bool nearWar = false;
                try
                {
                    if (s?.MapFaction is Kingdom pk)
                        nearWar = Kingdom.All.Any(k => !k.IsEliminated && k != pk && pk.IsAtWarWith(k));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (valor       >= 1) return PickValor(s, ashenActive, nearWar, agingPlayer, rel);
                if (mercy       >= 1) return PickMercy(s, ashenActive, rel);
                if (calculating >= 1) return PickCalculating(s, ashenActive, nearWar, rel);
                if (honor       >= 1) return PickHonor(s, nearWar, rel);
                if (generosity  >= 1) return PickGenerosity(s, rel);
                if (honor       <= -1) return PickCynical(s, ashenActive, rel);
                return PickDefault(agingPlayer, rel);
            }
            catch { return ""; }
        }

        // ── Valor ─────────────────────────────────────────────────────────────

        private static string PickValor(Settlement s, bool ashen, bool war, bool aging, RelationTier rel)
        {
            if (ashen)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("The grey ones are out there. Try not to walk us into them.",
                                    "Something worth killing on the road ahead. Hopefully you've noticed.");
                    case RelationTier.Negative:
                        return Pick("Grey things nearby. We should be careful.",
                                    "The cold ones have a presence on the road east. Worth knowing.");
                    case RelationTier.Neutral:
                        return Pick("The grey ones are out there. Good. Something worth killing is better than nothing.",
                                    "I've been watching the road east. The cold things leave a trace — we could follow it.",
                                    "The soldiers here look scared. They should look ready. There is a difference.");
                    case RelationTier.Positive:
                        return Pick("Grey ones nearby. I'll keep an eye on the flanks while you do what you need to do.",
                                    "I can feel the cold things on the road. Tell me when you're ready and I'll walk point.");
                    default: // VeryPositive
                        return Pick("Grey ones close. Stay near me today. I mean it.",
                                    "I've been watching since the last junction. The cold is there. I've got you.");
                }
            }
            if (war)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("War on the roads. At least someone around here is fighting.",
                                    "Armies moving nearby. Suppose that's someone else's problem to manage.");
                    case RelationTier.Negative:
                        return Pick("War on the roads. Obstacle or opportunity. Your call.",
                                    "The walls here have held. Someone knows how to build them.");
                    case RelationTier.Neutral:
                        return Pick("War on the roads — either an obstacle or an opportunity. I'm still deciding which.",
                                    "The walls here have held. I want to know who built them and whether they still know how.",
                                    "Armies moving nearby. I can hear it in the way the locals talk about the roads.");
                    case RelationTier.Positive:
                        return Pick("War out there. I'm watching which direction it's coming from. You focus on what's ahead.",
                                    "The garrison here is thin. I've been counting. We should be aware of that.");
                    default: // VeryPositive
                        return Pick("War out there. I'll walk point when we leave. You shouldn't be the first thing they see.",
                                    "I've been watching the roads. We're fine right now. I'll tell you the moment that changes.");
                }
            }
            if (aging)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("The fire's eating you. Hope you've got a plan for what happens when it runs out.",
                                    "You fight like you have something to prove. To yourself, mostly.");
                    case RelationTier.Negative:
                        return Pick("The fire costs you. You already know that.",
                                    "Still swinging. Noted.");
                    case RelationTier.Neutral:
                        return Pick("The fire is eating you and you're still swinging. I respect that more than I expected to.",
                                    "You fight like someone with something to prove to themselves. Not a bad thing.");
                    case RelationTier.Positive:
                        return Pick("The fire's eating you and you keep going. I'll keep going with you.",
                                    "I've watched you fight with the weight of that fire on you. Not many could.");
                    default: // VeryPositive
                        return Pick("The fire takes more from you than you let on. I see it. I'm not going anywhere.",
                                    "You carry that cost quietly. I notice. For what it's worth.");
                }
            }
            // Default valor lines
            switch (rel)
            {
                case RelationTier.VeryNegative:
                    return Pick("These roads are too quiet. Though I suppose you hadn't noticed.",
                                "The sentries here are bored. Much like the rest of us, frankly.",
                                "A place without enemies is just a place. I wouldn't know what to do with it either.");
                case RelationTier.Negative:
                    return Pick("Roads are quiet. That ends badly.",
                                "The sentries here are bored. Bored sentries miss things.",
                                "No enemies in sight. Doesn't mean there aren't any.");
                case RelationTier.Neutral:
                    return Pick("These roads are quieter than I'd like. Quiet usually ends badly.",
                                "A place without enemies is just a place. I'm never sure what to do with it.",
                                "The sentries here are bored. Bored sentries miss things.",
                                "I've been watching the patrols. The pattern is off. Either lazy or deliberate.");
                case RelationTier.Positive:
                    return Pick("Roads feel off today. I'm watching the treeline. You're fine.",
                                "Too quiet out here. I've been tracking the patrol rotations — there's a gap. Good to know.",
                                "The sentries here are bored. I'll keep an eye open while we're inside.");
                default: // VeryPositive
                    return Pick("Too quiet. I've been watching since the last junction. We're good for now.",
                                "Roads feel wrong. Stay close today, yeah?",
                                "I've counted the exits. We're fine. Just telling you so you don't have to think about it.");
            }
        }

        // ── Mercy ─────────────────────────────────────────────────────────────

        private static string PickMercy(Settlement s, bool ashen, RelationTier rel)
        {
            bool village = s?.IsVillage == true;
            if (ashen)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("The cold takes villages first. Lords notice last. I'm sure that's fine with you.",
                                    "Nobody sent these border people reinforcements. Nobody ever does.");
                    case RelationTier.Negative:
                        return Pick("The cold takes villages first. Lords notice last.",
                                    "Refugees from the grey border don't look like fighters. They look like people who had no choice.");
                    case RelationTier.Neutral:
                        return Pick("The cold takes villages first. Lords notice last. That pattern never changes.",
                                    "I've been thinking about the people near the grey border. Nobody sent them reinforcements.",
                                    "Refugees from the grey don't look like fighters. They look like people who ran out of choices.");
                    case RelationTier.Positive:
                        return Pick("The border villages are taking the worst of it. Worth remembering when we decide what to do next.",
                                    "People near the grey have nothing left. You've seen it. I know you have.");
                    default: // VeryPositive
                        return Pick("I know you see what the grey is doing to these people. I'm glad you care about it.",
                                    "The cold takes the vulnerable first. I know that bothers you. It bothers me too.");
                }
            }
            if (village)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("These people are still standing. No thanks to anyone from above.",
                                    "A village that holds together when lords don't bother. Interesting, given the company.");
                    case RelationTier.Negative:
                        return Pick("There's a dignity here. They keep going when lords decide not to.",
                                    "These people are still counting what they lost.");
                    case RelationTier.Neutral:
                        return Pick("There's a kind of dignity in places like this. They keep going when lords decide not to.",
                                    "These people are still counting what they lost. We shouldn't forget we passed through.",
                                    "A village that's still standing held through something. Worth remembering.");
                    case RelationTier.Positive:
                        return Pick("These people have held through more than we know. We should remember that when we leave.",
                                    "I'd like to do something for them before we go, if we can. Worth thinking about.");
                    default: // VeryPositive
                        return Pick("I know you see what this place is. I'm glad we stopped.",
                                    "These people kept going when they could have stopped. Reminds me of someone.");
                }
            }
            // Town/default mercy
            switch (rel)
            {
                case RelationTier.VeryNegative:
                    return Pick("The lower quarter here is thin. You can see it in the bread. Not that it matters.",
                                "Someone burned those fields outside town. Someone with a title, probably.");
                case RelationTier.Negative:
                    return Pick("The lower quarter here is thin. You can see it in the bread.",
                                "Someone ordered those fields burned at some point. The soil still shows it.");
                case RelationTier.Neutral:
                    return Pick("The lower quarter here is thin. You can see it in the bread.",
                                "You can tell which part of a city eats well and which doesn't by where the market stalls run out.",
                                "Someone ordered the fields outside this town burned at some point. The soil still shows it.",
                                "The children here look healthy enough. The adults look tired. That's a thing to notice.");
                case RelationTier.Positive:
                    return Pick("The lower quarter here is struggling. Not sure if there's anything we can do, but I wanted to say it.",
                                "You can see which part of this city eats well. The other part is watching us pass.");
                default: // VeryPositive
                    return Pick("I know you see what the lower quarter here looks like. I know you're thinking about it.",
                                "The children look alright. The parents don't. You noticed, didn't you. You always do.");
            }
        }

        // ── Calculating ───────────────────────────────────────────────────────

        private static string PickCalculating(Settlement s, bool ashen, bool war, RelationTier rel)
        {
            if (ashen)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("The grey advance has a pattern. I mapped it. I'll let you know if you ever ask.",
                                    "The cold ones aren't raiding randomly. They're clearing routes. Someone should be paying attention.");
                    case RelationTier.Negative:
                        return Pick("The grey advance has a pattern. Consistent arc each season. Someone is directing it.",
                                    "Cold kingdom expansion follows supply lines. For what that's worth.");
                    case RelationTier.Neutral:
                        return Pick("The grey ones don't raid randomly. They're clearing routes. Someone is directing that.",
                                    "I've been mapping the Ashen advance against the kingdom borders. The correlation is not coincidental.",
                                    "The cold kingdom's expansion has been consistent — roughly the same arc each season. That takes planning.");
                    case RelationTier.Positive:
                        return Pick("I've been mapping the grey advance. Thought you'd want to know — the pattern suggests they're moving toward the river roads.",
                                    "The cold ones aren't random. I've been tracking it. I'll brief you when you have a moment.");
                    default: // VeryPositive
                        return Pick("I've mapped the grey advance. Our position relative to their current arc is good — I've already thought through the contingencies.",
                                    "The cold pattern makes sense if you know what to look for. I'll walk you through it when we're clear of this place.");
                }
            }
            if (war)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("Two kingdoms bleeding each other. The third one wins. Obviously.",
                                    "The garrison here is understaffed. You probably didn't count.");
                    case RelationTier.Negative:
                        return Pick("Two factions bleeding each other. The third that stays out inherits what remains.",
                                    "The garrison here is understaffed for the roads it guards.");
                    case RelationTier.Neutral:
                        return Pick("The garrison here is understaffed for the number of roads it guards. Someone doesn't know, or doesn't care.",
                                    "Two factions bleeding each other. The third that stays out will inherit what remains.",
                                    "Supply lines for the war run through this region. That makes this settlement worth more than it looks.");
                    case RelationTier.Positive:
                        return Pick("I've been counting the garrison. Understaffed. We should keep that in mind.",
                                    "Supply lines run through here. Thought you'd want to know — it changes what this place is worth.");
                    default: // VeryPositive
                        return Pick("The garrison count is off. I've already marked the gaps. We're fine — just wanted you to know I'm watching it.",
                                    "Supply lines run through here. I've been thinking about how that affects our position. Tell me when you want to talk it through.");
                }
            }
            // Default calculating
            switch (rel)
            {
                case RelationTier.VeryNegative:
                    return Pick("I've been watching the supply lines. Something doesn't add up. You're welcome.",
                                "The lord here isn't spending what this settlement earns. Must be nice to notice things like that.");
                case RelationTier.Negative:
                    return Pick("The supply lines here don't match the troop numbers. Worth noting.",
                                "The lord here is not spending what this settlement earns. Interesting.");
                case RelationTier.Neutral:
                    return Pick("I've been watching the supply lines. Something doesn't match the troop numbers in the reports.",
                                "The lord who holds this settlement is not spending what it earns. Interesting.",
                                "Three different faction flags have flown over this gate in ten years. The locals have learned to be flexible.",
                                "The market prices here are off. Not wrong enough to matter, but wrong enough to notice.");
                case RelationTier.Positive:
                    return Pick("Supply routes here are off. I've been mapping it — thought you'd want to know before we commit to anything.",
                                "Market prices here are wrong by about eight percent. Means something is moving that shouldn't be. I'll keep looking.");
                default: // VeryPositive
                    return Pick("I've been running numbers on this settlement since we arrived. I'll have something useful for you by tonight.",
                                "Supply lines are off. Market prices are off. Something is moving through here quietly. I'm on it — I'll tell you what I find.");
            }
        }

        // ── Honor ─────────────────────────────────────────────────────────────

        private static string PickHonor(Settlement s, bool war, RelationTier rel)
        {
            if (war)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("There are oaths being broken in this war. I'm sure that doesn't keep anyone awake.",
                                    "The dead here had names. Someone waiting for them doesn't know yet. Probably not your concern.");
                    case RelationTier.Negative:
                        return Pick("There are oaths being broken in this war that nobody will remember when it ends.",
                                    "A lord who burns fields to deny the enemy is right about tactics and wrong about everything else.");
                    case RelationTier.Neutral:
                        return Pick("There are oaths being broken in this war that nobody will remember when it ends. Somebody should.",
                                    "The dead here had names. Someone is waiting for them who doesn't know yet.",
                                    "A lord who burns fields to deny the enemy is right about tactics and wrong about everything else.");
                    case RelationTier.Positive:
                        return Pick("There are oaths going unfulfilled in this war. It matters. I think you know that too.",
                                    "The dead here had names. Worth remembering, for when we decide how this ends.");
                    default: // VeryPositive
                        return Pick("There are oaths broken in this war that should trouble us both. I believe they do.",
                                    "The dead here had names. You carry that kind of thing. I've seen it. It's one of the things I trust about you.");
                }
            }
            switch (rel)
            {
                case RelationTier.VeryNegative:
                    return Pick("The garrison here kept their oath and stayed. Makes one think.",
                                "These people are watching us pass and wondering if we're going to be another promise that leaves.");
                case RelationTier.Negative:
                    return Pick("The garrison here swore to someone and stayed. That deserves acknowledgment.",
                                "There are oaths here that have gone unfulfilled too long.");
                case RelationTier.Neutral:
                    return Pick("We're being watched by people who don't know if we mean harm. That matters. We should remember it.",
                                "There are oaths here that have gone unfulfilled too long. I can feel it in how people speak about the past.",
                                "The garrison here swore to someone and stayed. That deserves something.",
                                "A promise made in a place like this carries weight. The walls remember.");
                case RelationTier.Positive:
                    return Pick("These people are watching us carefully. They've been failed before. I'd like us to leave a better impression.",
                                "There are old oaths here, unfulfilled. Worth being mindful of that while we're inside.");
                default: // VeryPositive
                    return Pick("These people will remember us. I want to make sure it's for the right reasons. I think you do too.",
                                "There are oaths unfulfilled in this place. I know you feel the weight of that kind of thing. So do I.");
            }
        }

        // ── Generosity ────────────────────────────────────────────────────────

        private static string PickGenerosity(Settlement s, RelationTier rel)
        {
            bool village = s?.IsVillage == true;
            if (village)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("We could leave something here. We have more than these people. Not that I'd expect you to.",
                                    "A village this size feeds three hundred mouths. Somebody could help. Theoretically.");
                    case RelationTier.Negative:
                        return Pick("We could leave something here. We have more than they do.",
                                    "The children here look healthy. The adults are stretched thin.");
                    case RelationTier.Neutral:
                        return Pick("We could leave something here. We have more than these people do.",
                                    "A village this size feeds three hundred mouths and asks nothing of anyone. Worth helping.",
                                    "The children here look healthy enough, but the adults look tired. Something we could address.");
                    case RelationTier.Positive:
                        return Pick("If you're thinking what I'm thinking, I'm in. These people could use it.",
                                    "I've been looking at what we have in stores. We could leave something and not feel it. Thought you should know.");
                    default: // VeryPositive
                        return Pick("You always find something to give, don't you. I'm with you on this one.",
                                    "I was going to say we should leave something here, but you're probably already thinking it.");
                }
            }
            switch (rel)
            {
                case RelationTier.VeryNegative:
                    return Pick("There's wealth in this city that could reach twenty villages. That it doesn't is a choice. Not yours, of course.",
                                "After a war the coin goes back to the lords. Villages stay hollow. Every time.");
                case RelationTier.Negative:
                    return Pick("There's wealth in this city that could reach twenty villages. That it doesn't is a choice.",
                                "After a war the coin goes back to the lords. The villages stay hollow.");
                case RelationTier.Neutral:
                    return Pick("There's wealth in this city that could reach twenty villages. That it doesn't is a choice.",
                                "After a war the coin goes back to the lords. The villages stay hollow. Every time.",
                                "A merchant told me he gives to the poor quarter on festival days. Once a year. He said it proudly.",
                                "The guild here pays its workers under the rate. Not by much. Just enough to notice if you're looking.");
                case RelationTier.Positive:
                    return Pick("There's wealth here that could do a lot of good if it moved differently. Worth thinking about what we can do.",
                                "The lower district here is struggling. I'd like to do something about it if we get the chance. You in?");
                default: // VeryPositive
                    return Pick("I know you see the same thing I see here. What do you want to do about it? Because I'll back you.",
                                "Every time we come through a place like this I think about how much a little would mean to them. And then I watch you do something about it.");
            }
        }

        // ── Cynical ───────────────────────────────────────────────────────────

        private static string PickCynical(Settlement s, bool ashen, RelationTier rel)
        {
            if (ashen)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("The grey spread while three lords argued over who owned the road. You'd fit right in with them.",
                                    "The cold takes what it wants. Nobody stopped it. Nobody will.");
                    case RelationTier.Negative:
                        return Pick("The grey spread while three lords argued about who owned the road.",
                                    "People act surprised when the cold takes a settlement. They weren't watching.");
                    case RelationTier.Neutral:
                        return Pick("The grey ones don't pretend to care about the smallfolk. At least they're honest about it.",
                                    "People act surprised when the cold takes a settlement. They weren't watching. Nobody was.",
                                    "The grey spread while three lords argued about who owned the road it spread along.");
                    case RelationTier.Positive:
                        return Pick("The grey don't bother pretending. I'll give them that. Unlike most of what we deal with.",
                                    "The cold spread while lords debated ownership. At least we're honest about what we're doing here.");
                    default: // VeryPositive
                        return Pick("The grey are honest, in their way. Which is more than most. You're one of the exceptions. Just so you know.",
                                    "The cold took what it wanted while lords argued. We're trying to be different. Some days I think we are.");
                }
            }
            switch (rel)
            {
                case RelationTier.VeryNegative:
                    return Pick("These people trust us because they can't afford not to. I hope you're aware of that.",
                                "Everyone in this room wants something they're not saying. Including us. Including you.",
                                "Don't look at me for backup if this goes sideways. I'm watching myself first.");
                case RelationTier.Negative:
                    return Pick("These people trust us because they can't afford not to.",
                                "Every lord claims they protect the people. Every lord means they own the people.",
                                "The innkeeper smiled at us. He'll smile the same way at the next company through.");
                case RelationTier.Neutral:
                    return Pick("These people trust us because they can't afford not to. Useful.",
                                "Every lord claims they protect the people. Every lord means they own the people.",
                                "The innkeeper smiled at us. He'll smile the same way at the next company through. It's not personal.",
                                "Everyone in this room wants something they're not saying. Including us.");
                case RelationTier.Positive:
                    return Pick("These people trust us because they can't afford not to. Let's at least earn it.",
                                "Everyone here wants something they're not saying. Including us. You probably already know what I want.");
                default: // VeryPositive
                    return Pick("These people trust us because they can't afford not to. I used to use that. With you I'm trying not to.",
                                "Everyone in here is playing an angle. Except maybe you. That's rarer than it sounds.");
            }
        }

        // ── Default ───────────────────────────────────────────────────────────

        private static string PickDefault(bool aging, RelationTier rel)
        {
            if (aging)
            {
                switch (rel)
                {
                    case RelationTier.VeryNegative:
                        return Pick("You look different than when we started. Not sure what to do with that.",
                                    "I've ridden with people who aged like you do. It didn't end well for most of them.");
                    case RelationTier.Negative:
                        return Pick("You look different than when we started. I don't mean tired.",
                                    "I've been counting the grey in your hair. Tactfully I decided not to say anything — and then did.");
                    case RelationTier.Neutral:
                        return Pick("You look different than when we started. I don't mean tired.",
                                    "I've been counting the grey in your hair. Tactfully I decided not to mention it — and then did.",
                                    "I've ridden with a lot of people. Not many that age like you do.");
                    case RelationTier.Positive:
                        return Pick("You look older than when we started. I'm not saying that to be unkind. I'm saying it because I'm paying attention.",
                                    "I've been watching the toll it takes on you. You don't complain. I notice things like that.");
                    default: // VeryPositive
                        return Pick("You look different. I see it. I'm still here.",
                                    "The years are moving faster for you than they should. I'm not going to pretend I don't see it. And I'm not going anywhere.");
                }
            }
            switch (rel)
            {
                case RelationTier.VeryNegative:
                    return Pick("Strange mood in this place. You probably didn't notice.",
                                "The road ahead looks clear. For now.",
                                "These people are tired. The kind of tired that doesn't go away. I'm sure it doesn't concern you.");
                case RelationTier.Negative:
                    return Pick("Strange mood in this place today.",
                                "The road ahead looks clear. That's not always a good sign.",
                                "These people are tired. The kind that doesn't go away after one night.");
                case RelationTier.Neutral:
                    return Pick("Strange mood in this place today. The locals know something we don't.",
                                "I've been through places like this before. There's something under the surface.",
                                "The road ahead looks clear. That's not always a good sign.",
                                "These people are tired. The kind of tired that doesn't go away after one night's sleep.",
                                "Something about this place feels like it's waiting. Can't say for what.",
                                "I don't know what happened here, but the dogs know.");
                case RelationTier.Positive:
                    return Pick("Strange mood in this place. Worth keeping an eye on. I'll let you know if anything changes.",
                                "Something about this place feels off. Just telling you so you're not caught flat-footed.",
                                "The road ahead looks clear. I've been checking. We should be alright.");
                default: // VeryPositive
                    return Pick("Strange feeling in this place. Wanted to mention it. You usually know what to do with things like that.",
                                "Something about today feels like it's building to something. I'll be watching your back.",
                                "The road ahead looks clear. I checked it myself. We're good.");
            }
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private static string Pick(params string[] options)
            => options[_rng.Next(options.Length)];

        private static void ShowQuick(string text)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch { try { InformationManager.DisplayMessage(new InformationMessage(text, _dim)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
        }
    }
}
