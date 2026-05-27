// =============================================================================
// LIFE & DEATH MAGIC — MagicData.cs (SchoolData.cs)
// Mount & Blade II: Bannerlord Mod  v2.0.0
// Reflavoured: colour magic → life and death magic, manipulation of life energies.
// =============================================================================

using System.Collections.Generic;
using TaleWorlds.Library;

namespace ColoursOfCalradia
{
    // Visual colour identifiers used by glow / light systems.
    // These now represent effect colours, not mage schools.
    public enum ColorSchool
    {
        Red    = 0,  // Damage effect
        Orange = 1,  // Damage + Morale combined
        Yellow = 2,  // Morale drain effect
        Green  = 3,  // Morale + Push combined
        Blue   = 4,  // Push effect
        Purple = 5,  // Push + Damage combined
        White  = 6,  // Reverse / heal
    }

    public static class ColorSchoolData
    {
        // ARGB hex glow colours
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
                case ColorSchool.White:  return 0xFFFFFFFFu;
                default:                 return 0xFFFFFFFFu;
            }
        }

        // Lighter reversed-effect glow colours
        public static uint GetReversedGlowColor(ColorSchool base_school)
        {
            switch (base_school)
            {
                case ColorSchool.Red:    return 0xFFFFAABBu; // pink (heal)
                case ColorSchool.Orange: return 0xFFFFCC99u; // light orange
                case ColorSchool.Yellow: return 0xFFFFFFAAu; // light yellow
                case ColorSchool.Green:  return 0xFF99FFCCu; // light green
                case ColorSchool.Blue:   return 0xFF88AAFFu; // light blue (pull)
                case ColorSchool.Purple: return 0xFFCC99FFu; // light purple
                case ColorSchool.White:  return 0xFFFFFFFFu;
                default:                 return 0xFFDDDDDDu;
            }
        }

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
                case ColorSchool.White:  return new Color(0.9f, 0.9f, 0.9f);
                default:                 return Color.White;
            }
        }

        // Compute the display colour for a spell's effect mix
        public static ColorSchool ComputeEffectColor(int damageCount, int pushCount, int moraleCount, bool reversed)
        {
            bool hasDmg    = damageCount > 0;
            bool hasPush   = pushCount   > 0;
            bool hasMorale = moraleCount > 0;

            ColorSchool primary;
            if (hasDmg && hasMorale && !hasPush)       primary = ColorSchool.Orange;
            else if (hasPush && hasDmg && !hasMorale)  primary = ColorSchool.Purple;
            else if (hasPush && hasMorale && !hasDmg)  primary = ColorSchool.Green;
            else if (hasDmg)                           primary = ColorSchool.Red;
            else if (hasPush)                          primary = ColorSchool.Blue;
            else if (hasMorale)                        primary = ColorSchool.Yellow;
            else                                       primary = ColorSchool.White;

            return reversed ? primary : primary; // reversed visual handled at glow time
        }

        // Magic flavour name used in messages
        public static string GetEffectName(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return "Vitality";
                case ColorSchool.Orange: return "Sapping";
                case ColorSchool.Yellow: return "Will-Breaking";
                case ColorSchool.Green:  return "Restoration";
                case ColorSchool.Blue:   return "Force";
                case ColorSchool.Purple: return "Oblivion";
                case ColorSchool.White:  return "Reversal";
                default:                 return "Arcane";
            }
        }
    }
}
