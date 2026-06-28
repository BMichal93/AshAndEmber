// =============================================================================
// ASH AND EMBER — Nature/NatureBacklash.cs
//
// When a caster forces a draw from an exhausted land (LivingEnergy returns a
// soured outcome), the living world bites back. Rather than one flat recoil, it
// answers in many forms — root, wither, blight, fever, ash-flare — so the
// consequence of stripping a place bare feels varied and alive.
//
// The SAME palette serves the player and NPC mages; only the player is told what
// happened (announce). Battle forms act on the agent on the field; map forms act
// on the party and its hero.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class NatureBacklash
    {
        private static readonly Random _rng = new Random();
        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        // ── Battle ────────────────────────────────────────────────────────────
        // The land recoils on a caster standing on it. Picks one form at random.
        public static void ApplyBattle(Agent caster, bool announce)
        {
            if (caster == null || !caster.IsActive()) return;
            try
            {
                switch (_rng.Next(5))
                {
                    case 0: BattleRecoil(caster, announce);     break;
                    case 1: BattleBriars(caster, announce);     break;
                    case 2: BattleHollowing(caster, announce);  break;
                    case 3: BattleAshFlare(caster, announce);   break;
                    default: BattleWither(caster, announce);    break;
                }
            }
            catch { }
        }

        // Raw recoil — the drawn force turns inward.
        private static void BattleRecoil(Agent caster, bool announce)
        {
            Hurt(caster, LivingEnergyMath.SourSelfDamage);
            try { SpawnGrey(caster.Position, 1.0f); } catch { }
            if (announce) Msg("The drained land recoils — the charge sours in your hands and the cold of it bites back.");
        }

        // Dead briars erupt from the spent soil and seize the caster.
        private static void BattleBriars(Agent caster, bool announce)
        {
            Hurt(caster, LivingEnergyMath.SourSelfDamage * 0.5f);
            try { NatureEffects.ApplySpeedToken(caster, 0f, 1.4f); } catch { }   // rooted
            try { SpellEffects.SpawnNatureBurst(caster.Position, NatureElement.Earth, 1.2f); } catch { }
            if (announce) Msg("Grey, dead briars burst from the spent ground and seize your legs — the land will not let go of what it is owed.");
        }

        // The hollow land drinks the caster's strength — sluggish and slow.
        private static void BattleHollowing(Agent caster, bool announce)
        {
            try { NatureEffects.ApplySpeedToken(caster, 0.45f, 6f); } catch { }
            try { SpawnGrey(caster.Position, 0.8f); } catch { }
            if (announce) Msg("The hollow land drinks from you in turn — your limbs go leaden and the world slows to a crawl.");
        }

        // A gout of grey ash detonates from the failed draw.
        private static void BattleAshFlare(Agent caster, bool announce)
        {
            Hurt(caster, LivingEnergyMath.SourSelfDamage * 0.8f);
            try { NatureEffects.ApplySpeedToken(caster, 0f, 0.6f); } catch { }   // brief stagger
            try { SpellEffects.SpawnTempLightRgb(caster.Position + new Vec3(0f, 0f, 1f), new Vec3(0.4f, 0.45f, 0.35f), 8f, 0.5f); } catch { }
            try { SpellEffects.SpawnNatureBurst(caster.Position, NatureElement.Storm, 1.4f); } catch { }
            if (announce) Msg("The draw detonates in a gout of grey ash — the dead light claws across your skin.");
        }

        // The withering — a slower, deeper bleed of vitality.
        private static void BattleWither(Agent caster, bool announce)
        {
            Hurt(caster, LivingEnergyMath.SourSelfDamage * 0.65f);
            try { NatureEffects.ApplySpeedToken(caster, 0.7f, 4f); } catch { }
            try { SpawnGrey(caster.Position, 0.9f); } catch { }
            if (announce) Msg("Something withers in you to pay the debt — a grey ache settles into the marrow.");
        }

        // ── Campaign map ───────────────────────────────────────────────────────
        // The land bites a party that draws from exhausted country. Picks one form.
        public static void ApplyMap(MobileParty party, Hero hero, bool announce)
        {
            if (party == null) return;
            try
            {
                switch (_rng.Next(4))
                {
                    case 0: MapRecoil(hero, announce);      break;
                    case 1: MapBlight(party, announce);     break;
                    case 2: MapDespair(party, announce);    break;
                    default: MapFever(party, announce);     break;
                }
            }
            catch { }
        }

        // The roots take their tithe from the caster's own body.
        private static void MapRecoil(Hero hero, bool announce)
        {
            int loss = 0;
            try
            {
                if (hero != null && hero.HitPoints > 12)
                {
                    loss = 10 + _rng.Next(8);
                    hero.HitPoints = Math.Max(5, hero.HitPoints - loss);
                }
            }
            catch { }
            if (announce) Msg($"The roots take their tithe from the nearest living vessel — you. [-{loss} health]");
        }

        // The provisions spoil — green rot creeps through the stores.
        private static void MapBlight(MobileParty party, bool announce)
        {
            int spoiled = RemoveFood(party, 8 + _rng.Next(8));
            if (announce)
                Msg(spoiled > 0
                    ? $"A green rot creeps through your stores overnight — provisions blacken and must be thrown out. [-{spoiled} food]"
                    : "A green rot creeps through your stores, but there was little left to spoil.");
        }

        // A grey weight settles on the column — the land's despair is contagious.
        private static void MapDespair(MobileParty party, bool announce)
        {
            float drop = 10f + _rng.Next(8);
            try { party.RecentEventsMorale -= drop; } catch { }
            if (announce) Msg($"A grey weight settles over the column — the dying land's despair is contagious. [-{(int)drop} morale]");
        }

        // A creeping sickness fells the weakest of the party.
        private static void MapFever(MobileParty party, bool announce)
        {
            int felled = WoundWeakest(party, 1 + _rng.Next(2));
            if (announce)
                Msg(felled > 0
                    ? $"A creeping marsh-fever rises from the spent ground — {felled} of your people take to the wagons, shivering. [-{felled} wounded]"
                    : "A creeping fever rises from the spent ground, but finds no one to take.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private static void Hurt(Agent a, float dmg)
        {
            try { if (a != null && a.IsActive()) a.Health = Math.Max(1f, a.Health - dmg); } catch { }
        }

        private static void SpawnGrey(Vec3 pos, float scale)
        {
            try { SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1f), new Vec3(0.35f, 0.4f, 0.3f), 6f * scale, 0.6f); } catch { }
        }

        private static readonly string[] _foodIds =
            { "grain", "meat", "fish", "vegetables", "cheese", "bread", "dried_meat", "oil", "beer", "wine" };

        private static int RemoveFood(MobileParty party, int amount)
        {
            int removed = 0;
            try
            {
                int remaining = amount;
                foreach (string id in _foodIds)
                {
                    if (remaining <= 0) break;
                    var item = MBObjectManager.Instance?.GetObject<ItemObject>(id);
                    if (item == null) continue;
                    int have = party.ItemRoster.GetItemNumber(item);
                    if (have <= 0) continue;
                    int take = Math.Min(have, remaining);
                    party.ItemRoster.AddToCounts(item, -take);
                    removed += take; remaining -= take;
                }
            }
            catch { }
            return removed;
        }

        private static int WoundWeakest(MobileParty party, int count)
        {
            int wounded = 0;
            try
            {
                var roster = party.MemberRoster;
                int remaining = count;
                foreach (var e in roster.GetTroopRoster()
                    .Where(x => x.Character != null && !x.Character.IsHero && x.Number > x.WoundedNumber)
                    .OrderBy(x => x.Character.Tier).ToList())
                {
                    if (remaining <= 0) break;
                    int w = Math.Min(remaining, e.Number - e.WoundedNumber);
                    try { roster.AddToCounts(e.Character, 0, false, w, 0); } catch { continue; }
                    wounded += w; remaining -= w;
                }
            }
            catch { }
            return wounded;
        }

        private static void Msg(string text)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(text, NatureColor)); }
            catch { }
        }
    }
}
