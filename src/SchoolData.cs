// =============================================================================
// COLOURS OF CALRADIA — SchoolData.cs
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
                LimitationB      = "Blood Price: Each Red spell opens a wound on the caster — magic always takes its due.",
                AttributePenalty = "-1 Control"
            },
            [ColorSchool.Orange] = new SchoolInfo
            {
                Name             = "Orange",
                FlavorText       = "Generous Hunger — Joyful, generous magic of warmth and plenty. Orange mages inspire and conjure allies from nothing. " +
                                   "Their indulgent nature, however, devours resources and sends the caster lurching with each casting.",
                PersonalityEffect= "Repeated casting increases your Generosity — open-handed and free with what you have.",
                LimitationA      = "Overindulgent: Your party consumes food faster and army upkeep is higher.",
                LimitationB      = "Generous Flood: Each Orange spell briefly seizes your body — you stagger in random directions for a moment, lurching unpredictably across the field.",
                AttributePenalty = "-1 Intellect"
            },
            [ColorSchool.Yellow] = new SchoolInfo
            {
                Name             = "Yellow",
                FlavorText       = "The Fearful Eye — Visceral, stomach-turning magic of dread and revulsion. Yellow mages poison courage and stir the deep animal panic beneath every soldier's composure. " +
                                   "The cost is insidious: those who spread fear begin to feel it — their judgment frays and their nerve hollows from within.",
                PersonalityEffect= "Repeated casting erodes your Mercy — disgust curdles into indifference; pity becomes revulsion.",
                LimitationA      = "Paranoia: Each Yellow spell costs party morale — the fear bleeds inward as well as outward.",
                LimitationB      = "Blurred Judgment: Yellow magic clouds the caster's mind — each cast increases your criminal rating as you begin to see threats everywhere.",
                AttributePenalty = "-1 Social"
            },
            [ColorSchool.Green] = new SchoolInfo
            {
                Name             = "Green",
                FlavorText       = "Gentle Burden — Kind, mending magic of life and restoration. Green mages sustain their companions through battle. " +
                                   "Their pacifist heart cannot act while holding a blade, and the weight of nearby violence seeps back into their body.",
                PersonalityEffect= "Repeated casting increases your Mercy — slow to strike, quick to spare.",
                LimitationA      = "Pacifist: You cannot use Green magic while wielding a weapon.",
                LimitationB      = "Gentle Burden: Each killing blow you land costs you — Green magic does not forgive the taking of life.",
                AttributePenalty = "-1 Endurance"
            },
            [ColorSchool.Blue] = new SchoolInfo
            {
                Name             = "Blue",
                FlavorText       = "Scholar's Weight — Cold, distanced magic of order and stillness. Blue mages freeze formations and conjure spectral shields. " +
                                   "But knowledge is heavy — each casting strains the body, adding invisible weight to armour and limb.",
                PersonalityEffect= "Repeated casting increases your Calculating trait — measured, deliberate, distant.",
                LimitationA      = "Scholar's Weight: Each Blue spell makes your equipment feel heavier — movement slows with every cast and does not recover until the battle ends. Six stacks will slow you to a crawl.",
                LimitationB      = "Grounded: Casting Blue magic from horseback has a chance to throw you from the saddle — the Scholar's Weight unbalances you mid-cast.",
                AttributePenalty = "-1 Vigor"
            },
            [ColorSchool.Purple] = new SchoolInfo
            {
                Name             = "Purple",
                FlavorText       = "The Waning Art — Melancholic, fading magic of grief and hollow quietude. Purple mages touch the deep sadness beneath living things, drawing on resignation and loss. " +
                                   "The grey does not take violently — it takes slowly, steadily. Each working bleeds away a little of the mage's time, presence, and will to be.",
                PersonalityEffect= "Repeated casting drains your Valor — grief and resignation make it harder to believe anything is worth the fight.",
                LimitationA      = "Waning Cost: Each Purple spell ages the caster by ~2 days — the grey draws time inward, quietly.",
                LimitationB      = "The Slow Unravelling: Each Purple cast quietly reduces the caster's fertility — something within grows dimmer with every working. It never reaches zero, but it never comes back.",
                AttributePenalty = "-1 Cunning"
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
                case ColorSchool.Green:  return DefaultCharacterAttributes.Control;      // precise restorative touch
                case ColorSchool.Blue:   return DefaultCharacterAttributes.Intelligence; // scholarly command of stillness
                case ColorSchool.Purple: return DefaultCharacterAttributes.Endurance;    // enduring the weight of grief and time
                default:                 return DefaultCharacterAttributes.Vigor;
            }
        }

        // Attribute that receives -1 penalty when school is chosen
        public static CharacterAttribute GetPenaltyAttribute(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return DefaultCharacterAttributes.Control;
                case ColorSchool.Orange: return DefaultCharacterAttributes.Intelligence;
                case ColorSchool.Yellow: return DefaultCharacterAttributes.Social;
                case ColorSchool.Green:  return DefaultCharacterAttributes.Endurance;
                case ColorSchool.Blue:   return DefaultCharacterAttributes.Vigor;
                case ColorSchool.Purple: return DefaultCharacterAttributes.Cunning;
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
