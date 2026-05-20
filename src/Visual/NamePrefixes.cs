// =============================================================================
// COLOURS OF CALRADIA — NamePrefixes.cs
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
    public static partial class SpellEffects
    {
        // ── In-battle colour name prefixes ────────────────────────────────────
        // Hero names are prefixed with [ROYGBP] letters during battle so players
        // can see at a glance which schools a lord wields. Restored on mission end.
        private static readonly Dictionary<string, string> _originalHeroNames = new Dictionary<string, string>();

        public static void ApplyColourNamePrefixes()
        {
            _originalHeroNames.Clear();
            if (Mission.Current == null) return;
            foreach (Agent a in Mission.Current.Agents)
            {
                if (!a.IsActive() || !a.IsHero) continue;
                Hero hero = (a.Character as CharacterObject)?.HeroObject;
                if (hero == null) continue;
                IEnumerable<ColorSchool> schools = ColourLordRegistry.GetColors(hero);
                if (hero == Hero.MainHero && !schools.Any())
                    schools = ColourKnowledge.AllSchools;
                if (!schools.Any()) continue;
                string prefix   = BuildSchoolPrefix(schools);
                string original = hero.Name.ToString();
                _originalHeroNames[hero.StringId] = original;
                try { hero.SetName(new TextObject(prefix + original), hero.FirstName); } catch { }
            }
        }

        public static void RestoreColourNamePrefixes()
        {
            foreach (var kvp in _originalHeroNames)
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == kvp.Key);
                if (hero == null) continue;
                try { hero.SetName(new TextObject(kvp.Value), hero.FirstName); } catch { }
            }
            _originalHeroNames.Clear();
        }

        internal static string BuildSchoolPrefix(IEnumerable<ColorSchool> schools)
        {
            var sb = new System.Text.StringBuilder("[");
            foreach (ColorSchool s in schools)
            {
                switch (s)
                {
                    case ColorSchool.Red:    sb.Append('R'); break;
                    case ColorSchool.Orange: sb.Append('O'); break;
                    case ColorSchool.Yellow: sb.Append('Y'); break;
                    case ColorSchool.Green:  sb.Append('G'); break;
                    case ColorSchool.Blue:   sb.Append('B'); break;
                    case ColorSchool.Purple: sb.Append('P'); break;
                }
            }
            sb.Append("] ");
            return sb.ToString();
        }
    }
}
