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
    // Warm, fiery palette: Red/Orange/Yellow/Amber/Ember/Crimson/White.
    // Green, Blue, Purple are retained as enum values but render as warm colours.
    // Blight is an ash-cold variant used only by blight mages.
    public enum ColorSchool
    {
        Red    = 0,  // Flame  — damage
        Orange = 1,  // Scorch — damage + morale
        Yellow = 2,  // Smoulder — morale drain
        Green  = 3,  // Amber  — morale + push combined
        Blue   = 4,  // Ember  — push / surge
        Purple = 5,  // Crimson — push + damage
        White  = 6,  // Pale Flame — reversal / heal
        Blight = 7,  // Ash-cold — blight mages only
    }

    public static class ColorSchoolData
    {
        // ARGB hex glow colours — all warm (fire, ember, ash)
        public static uint GetGlowColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return 0xFFFF2200u; // bright fire-red
                case ColorSchool.Orange: return 0xFFFF7700u; // deep orange
                case ColorSchool.Yellow: return 0xFFFFCC00u; // amber-gold
                case ColorSchool.Green:  return 0xFFFF9900u; // warm amber
                case ColorSchool.Blue:   return 0xFFFF6600u; // hot ember-orange
                case ColorSchool.Purple: return 0xFFDD1100u; // deep crimson
                case ColorSchool.White:  return 0xFFFFEECCu; // pale warm flame
                case ColorSchool.Blight: return 0xFF4A5566u; // ash grey-blue
                default:                 return 0xFFFFEECCu;
            }
        }

        // Reversed-effect glow: softer, tending toward gold/cream (life returning)
        // Blight reversed is darker ash
        public static uint GetReversedGlowColor(ColorSchool base_school)
        {
            switch (base_school)
            {
                case ColorSchool.Red:    return 0xFFFFCCBBu; // warm cream (healing)
                case ColorSchool.Orange: return 0xFFFFDDAA u; // pale gold
                case ColorSchool.Yellow: return 0xFFFFEE99u; // bright warm yellow (kindle)
                case ColorSchool.Green:  return 0xFFFFCCAA u; // pale amber
                case ColorSchool.Blue:   return 0xFFFFDD88u; // gold-draw
                case ColorSchool.Purple: return 0xFFCC8844u; // bronze
                case ColorSchool.White:  return 0xFFFFFFEEu;
                case ColorSchool.Blight: return 0xFF2A3340u; // deep cold ash
                default:                 return 0xFFFFEEDDu;
            }
        }

        public static Color GetMessageColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return new Color(1.0f,  0.13f, 0.0f);
                case ColorSchool.Orange: return new Color(1.0f,  0.47f, 0.0f);
                case ColorSchool.Yellow: return new Color(1.0f,  0.80f, 0.0f);
                case ColorSchool.Green:  return new Color(1.0f,  0.60f, 0.0f);
                case ColorSchool.Blue:   return new Color(1.0f,  0.40f, 0.0f);
                case ColorSchool.Purple: return new Color(0.87f, 0.07f, 0.0f);
                case ColorSchool.White:  return new Color(1.0f,  0.93f, 0.8f);
                case ColorSchool.Blight: return new Color(0.42f, 0.48f, 0.58f);
                default:                 return Color.White;
            }
        }

        // Compute display colour from effect mix
        public static ColorSchool ComputeEffectColor(int damageCount, int pushCount, int moraleCount, bool reversed)
        {
            bool hasDmg    = damageCount > 0;
            bool hasPush   = pushCount   > 0;
            bool hasMorale = moraleCount > 0;

            if (hasDmg && hasMorale && !hasPush)       return ColorSchool.Orange;
            if (hasPush && hasDmg   && !hasMorale)     return ColorSchool.Purple;
            if (hasPush && hasMorale && !hasDmg)       return ColorSchool.Green;
            if (hasDmg)                                return ColorSchool.Red;
            if (hasPush)                               return ColorSchool.Blue;
            if (hasMorale)                             return ColorSchool.Yellow;
            return ColorSchool.White;
        }

        // Flavour name used in display strings
        public static string GetEffectName(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return "Flame";
                case ColorSchool.Orange: return "Scorch";
                case ColorSchool.Yellow: return "Smoulder";
                case ColorSchool.Green:  return "Ember Surge";
                case ColorSchool.Blue:   return "Surge";
                case ColorSchool.Purple: return "Cinder";
                case ColorSchool.White:  return "Kindle";
                case ColorSchool.Blight: return "Ash";
                default:                 return "Fire";
            }
        }
    }
}
