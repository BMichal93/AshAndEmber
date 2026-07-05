// =============================================================================
// ASH AND EMBER — Magic/MagicLearning.cs
//
// Learning the inner fire's deeper craft. Fire is innate; everything else is
// learned by spending FOCUS POINTS (exactly as the old fire paths were) — the
// cost escalates: 1 point for the first power you take, 2 for the second, and
// so on. A TEACHER who carries that power teaches it for one point less.
//
//   Elements      — Wind · Earth · Water · Spirit
//   Disciplines   — Steel (cast armed, double weight) · Blood (a lord's death
//                   gives back years) · Nature (the patient draw costs far less)
//
// The Codex of the Inner Fire (the learning menu) is opened on the campaign map.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    // Everything a mage can learn beyond the innate Fire.
    public enum MagePower { Wind, Earth, Water, Spirit, Steel, Blood, Nature }

    public static class MagicLearning
    {
        private static readonly MagePower[] _all =
            { MagePower.Wind, MagePower.Earth, MagePower.Water, MagePower.Spirit,
              MagePower.Steel, MagePower.Blood, MagePower.Nature };

        // ── Has / Learn bridge to MageElementKnowledge ──────────────────────────
        public static bool Has(MagePower p)
        {
            switch (p)
            {
                case MagePower.Wind:   return MageElementKnowledge.HasElement(MagicElement.Wind);
                case MagePower.Earth:  return MageElementKnowledge.HasElement(MagicElement.Earth);
                case MagePower.Water:  return MageElementKnowledge.HasElement(MagicElement.Water);
                case MagePower.Spirit: return MageElementKnowledge.HasElement(MagicElement.Spirit);
                case MagePower.Steel:  return MageElementKnowledge.HasSteel;
                case MagePower.Blood:  return MageElementKnowledge.HasBlood;
                default:               return MageElementKnowledge.HasNature;
            }
        }

        private static void Grant(MagePower p)
        {
            switch (p)
            {
                case MagePower.Wind:   MageElementKnowledge.LearnElement(MagicElement.Wind);   break;
                case MagePower.Earth:  MageElementKnowledge.LearnElement(MagicElement.Earth);  break;
                case MagePower.Water:  MageElementKnowledge.LearnElement(MagicElement.Water);  break;
                case MagePower.Spirit: MageElementKnowledge.LearnElement(MagicElement.Spirit); break;
                case MagePower.Steel:  MageElementKnowledge.LearnSteel();  break;
                case MagePower.Blood:  MageElementKnowledge.LearnBlood();  break;
                default:               MageElementKnowledge.LearnNature(); break;
            }
        }

        // Free grant for encounters and boons that bestow a power without the
        // focus price — picks one the player does not yet hold, at random.
        // Returns false (name null) when every power is already held.
        public static bool TryGrantRandomUnknown(Random rng, out string name)
        {
            name = null;
            var unknown = _all.Where(p => !Has(p)).ToList();
            if (unknown.Count == 0) return false;
            var pick = unknown[rng.Next(unknown.Count)];
            Grant(pick);
            name = Name(pick);
            return true;
        }

        // ── Cost ────────────────────────────────────────────────────────────────
        public static int LearnedCount => _all.Count(Has);

        // Shared gentle curve (1,1,2,2,2,3,…). A teacher shaves one point off.
        public static int NextCost(bool fromTeacher = false)
            => Math.Max(1, TalentCostCurve.Cost(LearnedCount) - (fromTeacher ? 1 : 0));

        // ── Learn ───────────────────────────────────────────────────────────────
        public static bool TryLearn(MagePower p, bool fromTeacher, out string message)
        {
            message = "";
            if (Has(p)) { message = $"You already wield {Name(p)}."; return false; }

            int cost = NextCost(fromTeacher);
            var hero = Hero.MainHero;
            int have = 0;
            try { have = hero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (have < cost)
            {
                message = $"Learning {Name(p)} costs {cost} focus point{(cost != 1 ? "s" : "")}; you have {have}.";
                return false;
            }
            try { hero.HeroDeveloper.UnspentFocusPoints -= cost; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            Grant(p);
            message = fromTeacher
                ? $"You learn {Name(p)} at the teacher's hand. ({cost} focus point{(cost != 1 ? "s" : "")})"
                : $"You teach yourself {Name(p)}, the hard way. ({cost} focus point{(cost != 1 ? "s" : "")})";
            return true;
        }

        // ── The Codex (learning menu) ───────────────────────────────────────────
        public static void ShowCodex()
        {
            int have = 0;
            try { have = Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            int cost = NextCost(false);

            var elements = new List<InquiryElement>();
            foreach (var p in _all)
            {
                bool known   = Has(p);
                string label = $"{Name(p)}  —  {(known ? "[known]" : $"{cost} fp")}";
                elements.Add(new InquiryElement((int)p, label, null, !known, $"{Effect(p)}"));
            }

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"The Codex of the Inner Fire   [Focus: {have}]",
                    $"Fire is yours from the first day. The rest is learned — the next costs {cost} focus point{(cost != 1 ? "s" : "")}, " +
                    "or one less from a teacher who carries it. Choose what to study.",
                    elements, true, 1, 1, "Study", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var p = (MagePower)(int)chosen[0].Identifier;
                        if (TryLearn(p, false, out string msg))
                            InformationManager.DisplayMessage(new InformationMessage(msg, Gold));
                        else
                            InformationManager.DisplayMessage(new InformationMessage(msg, Dim));
                    },
                    null, "", false), false, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Labels ──────────────────────────────────────────────────────────────
        public static string Name(MagePower p)
        {
            switch (p)
            {
                case MagePower.Wind:   return "Wind";
                case MagePower.Earth:  return "Earth";
                case MagePower.Water:  return "Water";
                case MagePower.Spirit: return "Spirit";
                case MagePower.Steel:  return "Steel";
                case MagePower.Blood:  return "Blood";
                default:               return "Nature";
            }
        }

        public static string Effect(MagePower p)
        {
            switch (p)
            {
                case MagePower.Wind:   return "Wield the Wind — a forward gust that hurls and slows all it drives before it, and a wall of wind that turns aside arrows and bogs down all who cross it.";
                case MagePower.Earth:  return "Wield the Earth — a forward line of erupting stone that roots those it catches, and a stone wall raised from the ground.";
                case MagePower.Water:  return "Wield Water — a slowing wave that drags at the foe, and a barrier of mist.";
                case MagePower.Spirit: return "Wield Spirit — strike fear into men and horses and shout a stray order into their ranks, and raise a wall that heartens your own and mends them a little.";
                case MagePower.Steel:  return "Steel — shape the fire with a weapon still in your hand, and bear twice the armour before the channel smothers.";
                case MagePower.Blood:  return "Blood — when you take a lord's head, the years the fire has burned from you are given back, the more for the greater the lord.";
                default:               return "Nature — draw slowly, in tune with the living land, and a working costs you far fewer years.";
            }
        }

        private static readonly Color Gold = new Color(0.95f, 0.8f, 0.3f);
        private static readonly Color Dim  = new Color(0.7f, 0.65f, 0.55f);
    }
}
