// =============================================================================
// LIFE & DEATH MAGIC — BlightSystem.cs
// Blight system removed in v2.0. Stub retained so the project compiles
// against any lingering call-sites during the transition build.
// =============================================================================

using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public static class BlightSystem
    {
        public static bool IsBlight(Hero hero) => false;
        public static ColorSchool GetBlightSchool(Hero hero) => ColorSchool.Red;
        public static void RecordPlayerBlightKill(ColorSchool school) { }
        public static IEnumerable<ColorSchool> ConsumePlayerBlightKills()
            => System.Array.Empty<ColorSchool>();
        public static void OnBlightKilled(Hero hero) { }
        public static void OnNpcKilledBlight(Hero killer, ColorSchool school) { }
        public static void InitializeBlights() { }
        public static void ResetForNewGame() { }
        public static void SpawnBlightFromOversaturation(ColorSchool school) { }
        public static void CheckRespawnTimers() { }
        public static void Save(object store) { }
    }
}
