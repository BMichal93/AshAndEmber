// =============================================================================
// COLOURS OF CALRADIA — AffectSpells.cs
// Mount & Blade II: Bannerlord Mod  v2.0
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
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace ColoursOfCalradia
{
    public static partial class SpellEffects
    {
        // =================================================================
        // AFFECT SPELLS — campaign map only, each tied to a specific situation.
        // Daytime restriction is enforced by the global light-level check in
        // MagicInputHandler — no additional guard needed here.
        // Combos: UL prefix + colour (UL = W then A)
        //
        // Spam limiters (no cooldowns — all mechanical):
        //   Red    −10% current HP per cast; blocked at ≤5 HP
        //   Orange food cost doubles each cast within 24h (1→2→4→8…)
        //   Yellow self-morale drain escalates +5 each cast within 24h (5→10→15…)
        //   Green  −5% current HP per cast; blocked at ≤5 HP
        //   Blue   −3 renown per cast (buying influence damages reputation)
        //   Purple aging scales per session: 7→14→21→… days
        // =================================================================

        // ── Orange escalation tracking ──────────────────────────────────────
        private static int _orangeCastCount   = 0;
        private static int _orangeFirstCastDay = -1; // campaign day of first cast; -1 = never

        // ── Yellow escalation tracking ──────────────────────────────────────
        private static int _yellowCastCount   = 0;
        private static int _yellowFirstCastDay = -1;

        // ── Purple session tracking (resets on load — intentional) ──────────
        private static int _purpleCastCount = 0;

        // ── Red — Ember Drive ─────────────────────────────────────────────
        // During raid or hideout assault: +100×power gold per cast.
        // Cost: −10% current HP. Blocked at ≤5 HP.
        private static void SpellAffectRed()
        {
            var ev = MapEvent.PlayerMapEvent;
            if (ev == null || (!ev.IsRaid && !ev.IsHideoutBattle))
            {
                Msg("Ember Drive — only usable while raiding a village or clearing a hideout.", ColorSchool.Red);
                return;
            }
            if (Hero.MainHero == null) return;
            if (Hero.MainHero.HitPoints <= 5)
            {
                Msg("Ember Drive — you are too weakened to push further.", ColorSchool.Red);
                return;
            }

            float power = SpellPower(ColorSchool.Red);
            int gold = (int)(100f * power);
            try { Hero.MainHero.ChangeHeroGold(gold); } catch { return; }
            int hpCost = Math.Max(1, (int)(Hero.MainHero.HitPoints * 0.10f));
            Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - hpCost);
            string context = ev.IsRaid ? "village" : "hideout";
            Msg($"Ember Drive — seized +{gold} gold from the {context}. HP −{hpCost}.", ColorSchool.Red);
        }

        // ── Orange — Shared Feast ─────────────────────────────────────────
        // Consume food → +8×power morale. Food cost doubles each consecutive cast
        // within a calendar day (1→2→4→8…). Counter resets at the next campaign midnight.
        private static void SpellAffectOrange()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            try
            {
                int today = (int)CampaignTime.Now.ToDays;
                if (_orangeFirstCastDay >= 0 && today > _orangeFirstCastDay)
                { _orangeCastCount = 0; _orangeFirstCastDay = -1; }
            }
            catch { }

            int foodRequired = (int)Math.Pow(2, _orangeCastCount); // 1, 2, 4, 8…

            int totalFood = 0;
            foreach (ItemRosterElement el in party.ItemRoster)
                if (el.EquipmentElement.Item?.IsFood == true) totalFood += el.Amount;

            if (totalFood < foodRequired)
            {
                Msg($"Shared Feast — not enough food ({foodRequired} needed, {totalFood} available).", ColorSchool.Orange);
                return;
            }

            // Collect how much to take from each food stack, then apply
            int remaining = foodRequired;
            var toConsume = new List<(EquipmentElement item, int amount)>();
            foreach (ItemRosterElement el in party.ItemRoster)
            {
                if (remaining <= 0) break;
                if (el.EquipmentElement.Item?.IsFood != true || el.Amount <= 0) continue;
                int take = Math.Min(el.Amount, remaining);
                toConsume.Add((el.EquipmentElement, take));
                remaining -= take;
            }
            foreach (var (item, amount) in toConsume)
                party.ItemRoster.AddToCounts(item, -amount);

            float power = SpellPower(ColorSchool.Orange);
            float gain = 8f * power;
            try { party.RecentEventsMorale += gain; } catch { }

            if (_orangeFirstCastDay < 0) try { _orangeFirstCastDay = (int)CampaignTime.Now.ToDays; } catch { }
            _orangeCastCount++;

            int nextCost = (int)Math.Pow(2, _orangeCastCount);
            Msg($"Shared Feast — {foodRequired} food consumed. Party morale +{gain:F0}. (Next cast: {nextCost} food)", ColorSchool.Orange);
        }

        // ── Yellow — Dread Whisper ────────────────────────────────────────
        // Self-morale drain escalates +5 per cast within 24h (5→10→15…).
        // Yellow limitation adds a further −8 on top. No cap — self-limiting by design.
        private static void SpellAffectYellow()
        {
            var party = MobileParty.MainParty;
            if (party == null || Hero.MainHero == null) return;

            try
            {
                int today = (int)CampaignTime.Now.ToDays;
                if (_yellowFirstCastDay >= 0 && today > _yellowFirstCastDay)
                { _yellowCastCount = 0; _yellowFirstCastDay = -1; }
            }
            catch { }

            float power = SpellPower(ColorSchool.Yellow);
            IFaction playerFaction = Hero.MainHero.MapFaction;
            Vec2 playerPos = party.GetPosition2D;

            MobileParty closest = null;
            float minDist = float.MaxValue;
            foreach (MobileParty p in MobileParty.All)
            {
                if (p == party || !p.IsActive || p.IsMainParty) continue;
                if (p.MapFaction == null) continue;
                if (playerFaction != null && p.MapFaction == playerFaction) continue;
                if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                float d = (p.GetPosition2D - playerPos).Length;
                if (d < minDist) { minDist = d; closest = p; }
            }

            float selfLoss = 5f * (_yellowCastCount + 1); // 5, 10, 15…
            try { party.RecentEventsMorale -= selfLoss; } catch { }

            if (_yellowFirstCastDay < 0) try { _yellowFirstCastDay = (int)CampaignTime.Now.ToDays; } catch { }
            _yellowCastCount++;

            if (closest == null)
            {
                Msg($"Dread Whisper — no enemy at war found. Your morale still bleeds (−{selfLoss:F0}; Yellow limitation adds −8 more).", ColorSchool.Yellow);
                return;
            }

            float enemyLoss = 15f * power;
            try { closest.RecentEventsMorale -= enemyLoss; } catch { }
            int nextSelfLoss = (int)(5f * (_yellowCastCount + 1));
            Msg($"Dread Whisper — morale −{selfLoss:F0} (+−8 Yellow limitation); {closest.Name} −{enemyLoss:F0} ({minDist:F1} km). Next self-cost: −{nextSelfLoss}.", ColorSchool.Yellow);
        }

        // ── Green — Verdant Hour ─────────────────────────────────────────
        // Produce 1–4 grain (scales with Control attribute).
        // Cost: −5% current HP per cast. Forcing nature drains life from the caster.
        private static void SpellAffectGreen()
        {
            var party = MobileParty.MainParty;
            if (party == null || Hero.MainHero == null) return;

            if (Hero.MainHero.HitPoints <= 5)
            {
                Msg("Verdant Hour — you are too depleted to draw from the earth.", ColorSchool.Green);
                return;
            }

            float power = SpellPower(ColorSchool.Green);
            int amount = 1 + _rng.Next(Math.Max(1, (int)(power * 2f) + 1));

            try
            {
                ItemObject grain = Game.Current.ObjectManager.GetObject<ItemObject>("grain");
                if (grain == null) { Msg("Verdant Hour — the green stirs, but nothing takes form.", ColorSchool.Green); return; }
                party.ItemRoster.AddToCounts(new EquipmentElement(grain), amount);
                int hpCost = Math.Max(1, (int)(Hero.MainHero.HitPoints * 0.05f));
                Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - hpCost);
                Msg($"Verdant Hour — {amount} {(amount == 1 ? "unit of grain ripens" : "units of grain ripen")} at your touch. HP −{hpCost}.", ColorSchool.Green);
            }
            catch { Msg("Verdant Hour — the green stirs, but nothing takes form.", ColorSchool.Green); }
        }

        // ── Blue — Scholar's Investment ───────────────────────────────────
        // Spend 500 gold → +15×power influence.
        // Each cast also costs −3 renown: buying influence taints your reputation.
        // Requires membership in a kingdom — influence has no meaning outside one.
        private static void SpellAffectBlue()
        {
            if (Hero.MainHero == null) return;
            if (Hero.MainHero.Clan?.Kingdom == null)
            {
                Msg("Scholar's Investment — you must belong to a kingdom for influence to mean anything.", ColorSchool.Blue);
                return;
            }
            const int Cost = 500;
            if (Hero.MainHero.Gold < Cost)
            {
                Msg($"Scholar's Investment — you need at least {Cost} gold to convert.", ColorSchool.Blue);
                return;
            }

            float power = SpellPower(ColorSchool.Blue);
            int influence = (int)(15f * power);
            Hero.MainHero.ChangeHeroGold(-Cost);
            try { GainKingdomInfluenceAction.ApplyForDefault(Hero.MainHero, influence); } catch { }
            try { Hero.MainHero.Clan?.AddRenown(-3f); } catch { }
            Msg($"Scholar's Investment — {Cost} gold spent. Influence: +{influence}. Renown: −3.", ColorSchool.Blue);
        }

        // =================================================================
        // INVOKE SPELLS (LU prefix) — campaign map only, advanced forms.
        // Combos: LU prefix + colour (LU = A then W)
        //
        // Spam limiters (no cooldowns — all mechanical):
        //   Red    −20% current HP per cast; blocked at ≤5 HP
        //   Orange gold cost 100→200→400 (capped), resets at campaign midnight
        //   Yellow −2 own clan renown per cast (self-limiting)
        //   Green  −5% current HP per cast; blocked at ≤5 HP
        //   Blue   −300 gold per cast; kingdom required
        //   Purple flat 14-day aging per cast
        // =================================================================

        // ── Orange Invoke tracking ──────────────────────────────────────────
        private static int _orangeCallCount    = 0;
        private static int _orangeCallFirstDay = -1;

        // ── Red — Bloodprice ─────────────────────────────────────────────
        // Sacrifice 20% HP → party morale +20×power. No situation lock.
        private static void SpellInvokeRed()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;
            if (Hero.MainHero.HitPoints <= 5)
            {
                Msg("Bloodprice — you are too weakened to bleed for your troops.", ColorSchool.Red);
                return;
            }

            float power = SpellPower(ColorSchool.Red);
            int hpCost = Math.Max(1, (int)(Hero.MainHero.HitPoints * 0.20f));
            Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - hpCost);
            float moraleGain = 20f * power;
            try { MobileParty.MainParty.RecentEventsMorale += moraleGain; } catch { }
            Msg($"Bloodprice — HP −{hpCost}. Party morale +{moraleGain:F0}.", ColorSchool.Red);
        }

        // ── Orange — Muster Call ─────────────────────────────────────────
        // Instantly recruit 2–4 tier-1 troops from the nearest friendly settlement.
        // Gold cost: 100→200→400 (capped at 400), resets at campaign midnight.
        private static void SpellInvokeOrange()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            try
            {
                int today = (int)CampaignTime.Now.ToDays;
                if (_orangeCallFirstDay >= 0 && today > _orangeCallFirstDay)
                { _orangeCallCount = 0; _orangeCallFirstDay = -1; }
            }
            catch { }

            const int MaxGold = 400;
            int goldCost = Math.Min(MaxGold, 100 * (int)Math.Pow(2, _orangeCallCount));

            if (Hero.MainHero.Gold < goldCost)
            {
                Msg($"Muster Call — not enough gold ({goldCost} needed).", ColorSchool.Orange);
                return;
            }

            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
            Settlement nearest = null;
            float minDist = float.MaxValue;
            try
            {
                foreach (Settlement s in Settlement.All)
                {
                    if (!s.IsVillage && !s.IsTown && !s.IsCastle) continue;
                    if (s.MapFaction != Hero.MainHero.MapFaction) continue;
                    float d = (new Vec2(s.GatePosition.X, s.GatePosition.Y) - playerPos).Length;
                    if (d < minDist) { minDist = d; nearest = s; }
                }
            }
            catch { }

            if (nearest == null)
            {
                Msg("Muster Call — no friendly settlement found to draw from.", ColorSchool.Orange);
                return;
            }

            CharacterObject recruit = null;
            try
            {
                string culture = Hero.MainHero.Culture?.StringId ?? nearest.Culture?.StringId ?? "";
                foreach (CharacterObject c in CharacterObject.All)
                {
                    if (!c.IsHero && c.Tier == 1 && c.Culture?.StringId == culture)
                    { recruit = c; break; }
                }
            }
            catch { }

            if (recruit == null)
            {
                Msg("Muster Call — no recruits available.", ColorSchool.Orange);
                return;
            }

            float power = SpellPower(ColorSchool.Orange);
            int count = 2 + _rng.Next(Math.Max(1, (int)(power * 2f)));
            Hero.MainHero.ChangeHeroGold(-goldCost);
            try { MobileParty.MainParty.MemberRoster.AddToCounts(recruit, count); } catch { return; }

            if (_orangeCallFirstDay < 0) try { _orangeCallFirstDay = (int)CampaignTime.Now.ToDays; } catch { }
            _orangeCallCount++;

            int nextCost = Math.Min(MaxGold, 100 * (int)Math.Pow(2, _orangeCallCount));
            Msg($"Muster Call — {count} recruits from {nearest.Name}. −{goldCost} gold. (Next: {nextCost}g)", ColorSchool.Orange);
        }

        // ── Yellow — Whispered Ruin ──────────────────────────────────────
        // Drain 8 renown from the nearest enemy lord's clan. Costs 2 own renown.
        // Requires active war.
        private static void SpellInvokeYellow()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;
            if (Hero.MainHero.Clan == null)
            {
                Msg("Whispered Ruin — you have no clan standing to spend.", ColorSchool.Yellow);
                return;
            }

            IFaction playerFaction = Hero.MainHero.MapFaction;
            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;

            Hero targetLord = null;
            float minDist = float.MaxValue;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes)
                {
                    if (!h.IsLord || h.Clan == null || !h.IsAlive) continue;
                    if (h.MapFaction == null) continue;
                    if (playerFaction != null && h.MapFaction == playerFaction) continue;
                    if (playerFaction != null && !playerFaction.IsAtWarWith(h.MapFaction)) continue;
                    if (h.PartyBelongedTo == null) continue;
                    float d = (h.PartyBelongedTo.GetPosition2D - playerPos).Length;
                    if (d < minDist) { minDist = d; targetLord = h; }
                }
            }
            catch { }

            if (targetLord == null)
            {
                Msg("Whispered Ruin — no enemy lord at war to target.", ColorSchool.Yellow);
                return;
            }

            try { Hero.MainHero.Clan.AddRenown(-2f); } catch { }
            try { targetLord.Clan.AddRenown(-8f); } catch { }
            Msg($"Whispered Ruin — own renown −2; {targetLord.Name}'s clan renown −8 ({minDist:F1} km away).", ColorSchool.Yellow);
        }

        // ── Green — Tend the Fallen ──────────────────────────────────────
        // Heal 3+power*2 wounded troops in own party roster. Cost: −5% HP.
        private static void SpellInvokeGreen()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;
            if (Hero.MainHero.HitPoints <= 5)
            {
                Msg("Tend the Fallen — you are too depleted to channel life into others.", ColorSchool.Green);
                return;
            }

            float power = SpellPower(ColorSchool.Green);
            int healBudget = 3 + (int)(power * 2f);

            int totalHealed = 0;
            try
            {
                foreach (TroopRosterElement e in MobileParty.MainParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (totalHealed >= healBudget) break;
                    if (e.WoundedNumber <= 0) continue;
                    int heal = Math.Min(e.WoundedNumber, healBudget - totalHealed);
                    MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, -heal);
                    totalHealed += heal;
                }
            }
            catch { }

            if (totalHealed == 0)
            {
                Msg("Tend the Fallen — no wounded troops to mend.", ColorSchool.Green);
                return;
            }

            int hpCost = Math.Max(1, (int)(Hero.MainHero.HitPoints * 0.05f));
            Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - hpCost);
            Msg($"Tend the Fallen — {totalHealed} {(totalHealed == 1 ? "soldier recovers" : "soldiers recover")} from wounds. HP −{hpCost}.", ColorSchool.Green);
        }

        // ── Blue — Counter-Scheme ────────────────────────────────────────
        // Spend 300 gold to drain 8 influence from the nearest enemy lord's clan.
        // Kingdom required. Targets any non-player faction (no war required).
        private static void SpellInvokeBlue()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;
            if (Hero.MainHero.Clan?.Kingdom == null)
            {
                Msg("Counter-Scheme — you must belong to a kingdom for your networks to reach.", ColorSchool.Blue);
                return;
            }

            const int Cost = 300;
            if (Hero.MainHero.Gold < Cost)
            {
                Msg($"Counter-Scheme — you need at least {Cost} gold to operate.", ColorSchool.Blue);
                return;
            }

            IFaction playerFaction = Hero.MainHero.MapFaction;
            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;

            Hero targetLord = null;
            float minDist = float.MaxValue;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes)
                {
                    if (!h.IsLord || h.Clan == null || !h.IsAlive) continue;
                    if (h.MapFaction == null || h.MapFaction == playerFaction) continue;
                    if (h.PartyBelongedTo == null) continue;
                    float d = (h.PartyBelongedTo.GetPosition2D - playerPos).Length;
                    if (d < minDist) { minDist = d; targetLord = h; }
                }
            }
            catch { }

            if (targetLord == null)
            {
                Msg("Counter-Scheme — no enemy lord found to undermine.", ColorSchool.Blue);
                return;
            }

            Hero.MainHero.ChangeHeroGold(-Cost);
            try { ChangeClanInfluenceAction.Apply(targetLord.Clan, -8f); } catch { }
            Msg($"Counter-Scheme — {Cost} gold spent. {targetLord.Name}'s clan influence −8.", ColorSchool.Blue);
        }

        // ── Purple — Wither's Touch ──────────────────────────────────────
        // Curse the nearest enemy lord: −15 party morale, −8 clan renown.
        // Cost: 14 days aging (flat). Targets any hostile lord regardless of war state.
        private static void SpellInvokePurple()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;

            Hero targetLord = null;
            float minDist = float.MaxValue;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes)
                {
                    if (!h.IsLord || !h.IsAlive) continue;
                    if (h.MapFaction == null || h.MapFaction == playerFaction) continue;
                    if (h.PartyBelongedTo == null) continue;
                    float d = (h.PartyBelongedTo.GetPosition2D - playerPos).Length;
                    if (d < minDist) { minDist = d; targetLord = h; }
                }
            }
            catch { }

            if (targetLord == null)
            {
                Msg("Wither's Touch — no enemy lord found.", ColorSchool.Purple);
                return;
            }

            const int DaysToAge = 14;
            try { Hero.MainHero.SetBirthDay(Hero.MainHero.BirthDay - CampaignTime.Days(DaysToAge)); } catch { }
            try { targetLord.PartyBelongedTo?.RecentEventsMorale -= 15f; } catch { }
            try { targetLord.Clan?.AddRenown(-8f); } catch { }
            int age = (int)Hero.MainHero.Age;
            Msg($"Wither's Touch — {DaysToAge} days paid. {targetLord.Name}: morale −15, renown −8 ({minDist:F1} km). Age: {age}.", ColorSchool.Purple);
        }

        // ── Purple — Grey Veil ────────────────────────────────────────────
        // Aging cost scales per session: 7→14→21→… days (resets on load).
        // Scatter radius 2 map units.
        private static void SpellAffectPurple()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
            IFaction playerFaction = Hero.MainHero.MapFaction;
            int scattered = 0;
            foreach (MobileParty p in MobileParty.All.ToList())
            {
                if (p == MobileParty.MainParty || !p.IsActive) continue;
                if (p.MapFaction == null) continue;
                if (playerFaction != null && p.MapFaction == playerFaction) continue;
                if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                if ((p.GetPosition2D - playerPos).Length > 2f) continue;

                Vec2 away = p.GetPosition2D - playerPos;
                if (away.Length < 0.01f) away = new Vec2(1f, 0f); else away = away.Normalized();
                Vec2 target = p.GetPosition2D + away * 3f;
                try { p.SetMoveGoToPoint(new CampaignVec2(target, true), MobileParty.NavigationType.Default); scattered++; } catch { }
            }

            int daysToAge = 7 * (_purpleCastCount + 1); // 7, 14, 21…
            _purpleCastCount++;

            try { Hero.MainHero.SetBirthDay(Hero.MainHero.BirthDay - CampaignTime.Days(daysToAge)); } catch { }

            int age = (int)(Hero.MainHero.Age);
            string effect = scattered > 0
                ? $"{scattered} nearby {(scattered == 1 ? "party loses" : "parties lose")} your trail."
                : "No enemies were close enough to scatter.";
            Msg($"Grey Veil — {effect} {daysToAge} days paid. | Age: {age}", ColorSchool.Purple);
        }
    }
}
