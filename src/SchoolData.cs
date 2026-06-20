// =============================================================================
// LIFE & DEATH MAGIC — SchoolData.cs
// =============================================================================

using System.Collections.Generic;
using TaleWorlds.Library;

namespace AshAndEmber
{
    // Visual colour identifiers used by glow / light systems.
    // Red = Damage, White = Restore, Orange = both.
    // Ashen is the cold-fire variant used only by Ashen mages.
    // Yellow/Blue/Green/Purple are used by NPC area effects.
    public enum ColorSchool
    {
        Red    = 0,  // Damage
        Orange = 1,  // Damage + Restore combined
        Yellow = 2,  // NPC morale cloud
        Green  = 3,  // NPC heal zone
        Blue   = 4,  // NPC barrier
        Purple = 5,  // Lord caster glow
        White  = 6,  // Restore / Ward
        Ashen  = 7,  // Ash-cold — Ashen mages only
        Nature = 8,  // The Living Ember — nature seers
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
                case ColorSchool.White:  return 0xFFFFDD44u; // golden-yellow restore
                case ColorSchool.Ashen:  return 0xFF5588CCu; // cold blue
                case ColorSchool.Nature: return 0xFF44CC44u; // living green
                default:                 return 0xFFFFEECCu;
            }
        }

        // Soft glow for restore/healing — warm cream tones
        public static uint GetReversedGlowColor(ColorSchool base_school)
        {
            switch (base_school)
            {
                case ColorSchool.Red:    return 0xFFFFCCBBu;
                case ColorSchool.Orange: return 0xFFFFDDAAu;
                case ColorSchool.Yellow: return 0xFFFFEE99u;
                case ColorSchool.Green:  return 0xFFFFCCAAu;
                case ColorSchool.Blue:   return 0xFFFFDD88u;
                case ColorSchool.Purple: return 0xFFCC8844u;
                case ColorSchool.White:  return 0xFFFFEE88u; // pale yellow
                case ColorSchool.Ashen:  return 0xFF334466u;
                case ColorSchool.Nature: return 0xFF99EE99u; // pale leaf
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
                case ColorSchool.White:  return new Color(1.0f,  0.87f, 0.2f); // golden-yellow
                case ColorSchool.Ashen:  return new Color(0.38f, 0.50f, 0.75f);
                case ColorSchool.Nature: return new Color(0.35f, 0.75f, 0.35f);
                default:                 return Color.White;
            }
        }

        // Compute display colour from the two effect types
        public static ColorSchool ComputeEffectColor(int damageCount, int restoreCount)
        {
            if (damageCount > 0 && restoreCount > 0) return ColorSchool.Orange;
            if (damageCount > 0)  return ColorSchool.Red;
            if (restoreCount > 0) return ColorSchool.White;
            return ColorSchool.Red;
        }

        public static string GetEffectName(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return "Flame";
                case ColorSchool.Orange: return "Kindle";
                case ColorSchool.Yellow: return "Smoulder";
                case ColorSchool.Green:  return "Ember";
                case ColorSchool.Blue:   return "Surge";
                case ColorSchool.Purple: return "Cinder";
                case ColorSchool.White:  return "Restore";
                case ColorSchool.Ashen:  return "Ash";
                case ColorSchool.Nature: return "Living Ember";
                default:                 return "Fire";
            }
        }
    }
}
