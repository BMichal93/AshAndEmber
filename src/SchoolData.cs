// =============================================================================
// COLOURS OF CALRADIA — SchoolData.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0
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
    // =========================================================================
    // 2. COLOUR SCHOOLS
    // =========================================================================
    public enum ColorSchool { Red, Orange, Yellow, Green, Blue, Purple }

    public static class ColorSchoolData
    {
        public struct SchoolInfo
        {
            public string Name;
            public string FlavorText;
            public string PersonalityEffect;
            public string LimitationA;
            public string LimitationB;
            public string AttributePenalty;
        }

        public static readonly Dictionary<ColorSchool, SchoolInfo> Info =
            new Dictionary<ColorSchool, SchoolInfo>
        {
            [ColorSchool.Red] = new SchoolInfo
            {
                Name             = "Red",
                FlavorText       = "Blood Price — Violent, fiery magic of war and ruin. Red mages channel rage into devastating waves and burning bursts. " +
                                   "The art demands payment in pain: each working scorches the caster, and every cast drives your soldiers into a frenzy.",
                PersonalityEffect= "Repeated casting makes you less Calculating — more instinctive, more impulsive.",
                LimitationA      = "Furious: Each Red spell automatically issues a Charge order to your formations.",
                LimitationB      = "Blood Price: Each Red spell deals 2 damage to the caster — magic always takes its due.",
                AttributePenalty = "-1 Cunning"
            },
            [ColorSchool.Orange] = new SchoolInfo
            {
                Name             = "Orange",
                FlavorText       = "Generous Hunger — Joyful, generous magic of warmth and plenty. Orange mages inspire and conjure allies from nothing. " +
                                   "The warmth only flows when the heart is light — misery chokes the golden current before it can form.",
                PersonalityEffect= "Repeated casting increases your Generosity — open-handed and free with what you have.",
                LimitationA      = "Joyful Cast: You cannot cast Orange magic if your party morale is below 51 — the warmth will not flow through misery.",
                LimitationB      = "",
                AttributePenalty = "-1 Intelligence"
            },
            [ColorSchool.Yellow] = new SchoolInfo
            {
                Name             = "Yellow",
                FlavorText       = "The Fearful Eye — Visceral, stomach-turning magic of dread and revulsion. Yellow mages poison courage and stir the deep animal panic beneath every soldier's composure. " +
                                   "Animals sense the wrongness in the Yellow mage and will not carry them — every mount feels it and throws them.",
                PersonalityEffect= "Repeated casting erodes your Mercy — disgust curdles into indifference; pity becomes revulsion.",
                LimitationA      = "Animal Fear: You cannot cast Yellow magic from horseback — animals sense the wrongness and refuse to carry you while you channel it.",
                LimitationB      = "",
                AttributePenalty = "-1 Social"
            },
            [ColorSchool.Green] = new SchoolInfo
            {
                Name             = "Green",
                FlavorText       = "Gentle Burden — Kind, mending magic of life and restoration. Green mages sustain their companions through battle. " +
                                   "The green flows from open sky and living earth; stone walls and city streets choke it. Step beyond the walls before calling on it.",
                PersonalityEffect= "Repeated casting increases your Mercy — slow to strike, quick to spare.",
                LimitationA      = "Nature's Calling: You cannot cast Green campaign magic inside settlements — not in cities, castles, or villages. The colour requires open sky and living earth.",
                LimitationB      = "",
                AttributePenalty = "-1 Control"
            },
            [ColorSchool.Blue] = new SchoolInfo
            {
                Name             = "Blue",
                FlavorText       = "The Scholar's Craft — Cold, precise magic of clarity and form. Blue mages freeze formations and conjure spectral barriers. " +
                                   "The art demands empty hands: a mind armed with a blade cannot hold the delicate geometry of the colour. Lay down your weapon, and the blue may answer.",
                PersonalityEffect= "Repeated casting increases your Calculating trait — measured, deliberate, distant.",
                LimitationA      = "Scholar's Craft: Cannot cast Blue magic in battle while wielding a weapon — the colour requires empty hands and a focused mind.",
                LimitationB      = "",
                AttributePenalty = "-1 Vigor"
            },
            [ColorSchool.Purple] = new SchoolInfo
            {
                Name             = "Purple",
                FlavorText       = "The Waning Art — Melancholic, fading magic of grief and hollow quietude. Purple mages touch the deep sadness beneath living things, drawing on resignation and loss. " +
                                   "The grey does not take violently — it takes steadily. Each working costs a piece of the future: fertility dims and the body quietly ages.",
                PersonalityEffect= "Repeated casting hollows out all personality — grief erodes every trait toward a silent zero.",
                LimitationA      = "The Slow Unravelling: Each Purple cast reduces the caster's fertility by 1% and ages them by 1 day — both are permanent. The future, sacrificed piece by piece.",
                LimitationB      = "",
                AttributePenalty = "-1 Endurance"
            }
        };

        // ARGB hex glow colors per school
        public static uint GetGlowColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return 0xFFFF2200u;
                case ColorSchool.Orange: return 0xFFFF8800u;
                case ColorSchool.Yellow: return 0xFFFFFF00u;
                case ColorSchool.Green:  return 0xFF00CC44u;
                case ColorSchool.Blue:   return 0xFF2244FFu;
                case ColorSchool.Purple: return 0xFF8800CCu;
                default:                 return 0xFFFFFFFFu;
            }
        }

        // Attribute that scales spell power for each school.
        // No school scales from its own penalty attribute; this is a strict bijection.
        public static CharacterAttribute GetScaleAttribute(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return DefaultCharacterAttributes.Vigor;        // raw destructive force
                case ColorSchool.Orange: return DefaultCharacterAttributes.Social;       // inspiring presence
                case ColorSchool.Yellow: return DefaultCharacterAttributes.Cunning;      // manipulation of fear
                case ColorSchool.Green:  return DefaultCharacterAttributes.Endurance;    // healing draws on inner resilience
                case ColorSchool.Blue:   return DefaultCharacterAttributes.Intelligence; // scholarly command of stillness
                case ColorSchool.Purple: return DefaultCharacterAttributes.Control;      // grief's precise, careful touch
                default:                 return DefaultCharacterAttributes.Vigor;
            }
        }

        // Attribute that receives -1 penalty when school is chosen
        public static CharacterAttribute GetPenaltyAttribute(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return DefaultCharacterAttributes.Cunning;
                case ColorSchool.Orange: return DefaultCharacterAttributes.Intelligence;
                case ColorSchool.Yellow: return DefaultCharacterAttributes.Social;
                case ColorSchool.Green:  return DefaultCharacterAttributes.Control;
                case ColorSchool.Blue:   return DefaultCharacterAttributes.Vigor;
                case ColorSchool.Purple: return DefaultCharacterAttributes.Endurance;
                default:                 return DefaultCharacterAttributes.Vigor;
            }
        }

        // Trait affected by casting and direction (+1 = increase, -1 = decrease)
        public static (TraitObject trait, int direction) GetTraitEffect(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return (DefaultTraits.Calculating, -1);
                case ColorSchool.Orange: return (DefaultTraits.Generosity,  +1);
                case ColorSchool.Yellow: return (DefaultTraits.Mercy,       -1);
                case ColorSchool.Green:  return (DefaultTraits.Mercy,       +1);
                case ColorSchool.Blue:   return (DefaultTraits.Calculating, +1);
                case ColorSchool.Purple: return (DefaultTraits.Valor,       -1);
                default:                 return (DefaultTraits.Valor,        0);
            }
        }

        // Informational color for messages about each school
        public static Color GetMessageColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return new Color(1.0f, 0.13f, 0.0f);
                case ColorSchool.Orange: return new Color(1.0f, 0.53f, 0.0f);
                case ColorSchool.Yellow: return new Color(1.0f, 1.0f,  0.0f);
                case ColorSchool.Green:  return new Color(0.0f, 0.8f,  0.27f);
                case ColorSchool.Blue:   return new Color(0.13f, 0.27f, 1.0f);
                case ColorSchool.Purple: return new Color(0.53f, 0.0f, 0.8f);
                default:                 return Color.White;
            }
        }
    }
}
