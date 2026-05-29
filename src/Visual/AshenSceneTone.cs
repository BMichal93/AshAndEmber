// =============================================================================
// ASH AND EMBER — Visual/AshenSceneTone.cs
// Applies a grey overcast visual tone to any mission scene that takes place
// inside an Ashen settlement (towns, villages, sieges, interiors).
//
// Effect: sets the scene time-of-day to a grey early-morning value and
// attempts to push a desaturated fog layer via AtmosphereData reflection.
// AtmosphereData's field layout varies across Bannerlord versions so the
// reflection calls are wrapped in try/catch and fail silently; the
// TimeOfDay change alone is enough to produce a noticeably dimmed sky.
//
// Wiring (MagicSystem.cs — MagicMissionBehavior):
//   OnMissionTick → AshenSceneTone.MissionTick(dt)
//   OnEndMission  → AshenSceneTone.Reset()
// =============================================================================

using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class AshenSceneTone
    {
        // Grey early-morning light — visually overcast without being unplayably dark.
        private const float AshenTimeOfDay = 7.5f;

        // Fog settings pushed via AtmosphereData if that API is available.
        private const float FogDensity        = 0.02f;
        private const float SunBrightness     = 0.45f;
        private const float AmbientBrightness = 0.55f;
        private static readonly Vec3 FogColor = new Vec3(0.55f, 0.55f, 0.57f);

        // Wait this many seconds after mission start before touching the scene,
        // giving the engine time to finish initialising terrain and atmosphere.
        private const float InitDelay = 1.5f;

        private static bool  _applied = false;
        private static float _timer   = 0f;

        // ── Public API ────────────────────────────────────────────────────────

        /// Called every frame from MagicMissionBehavior.OnMissionTick(dt).
        public static void MissionTick(float dt)
        {
            if (_applied) return;
            _timer += dt;
            if (_timer < InitDelay) return;
            _applied = true;
            Apply();
        }

        /// Called from MagicMissionBehavior.OnEndMission().
        public static void Reset()
        {
            _applied = false;
            _timer   = 0f;
        }

        // ── Core ──────────────────────────────────────────────────────────────

        private static void Apply()
        {
            try
            {
                if (Mission.Current == null) return;

                var settlement = ResolveSettlement();
                if (settlement == null) return;
                if (!AshenCitySystem.IsAshenSettlement(settlement)) return;

                var scene = Mission.Current.Scene;
                if (scene == null) return;

                try { scene.TimeOfDay = AshenTimeOfDay; } catch { }
                TryPushGreyAtmosphere(scene);
            }
            catch { }
        }

        // Returns the settlement this mission is taking place in, or null for
        // field battles and any mission unrelated to a settlement.
        private static Settlement ResolveSettlement()
        {
            // Town / village walkabout
            try { var s = Hero.MainHero?.CurrentSettlement;          if (s != null) return s; } catch { }
            // Siege battle (main party is the besieger)
            try { var s = MobileParty.MainParty?.BesiegedSettlement; if (s != null) return s; } catch { }
            // Map-event-driven battle at a settlement (defender side, garrison sorties, etc.)
            try { var s = Mission.Current?.MapEvent?.MapEventSettlement; if (s != null) return s; } catch { }
            return null;
        }

        // Attempt to push grey fog via AtmosphereData. AtmosphereData is a
        // struct in TaleWorlds.Engine whose field names differ across game
        // versions; reflection lets us set whatever fields exist without a
        // compile dependency on a specific struct layout.
        private static void TryPushGreyAtmosphere(object scene)
        {
            try
            {
                var sceneType = scene.GetType();
                var method    = sceneType.GetMethod("SetAtmosphereWithParams",
                                    BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return;

                var atmoType = method.GetParameters()[0].ParameterType;
                var atmoData = Activator.CreateInstance(atmoType);

                TrySetMember(atmoData, atmoType, "FogDensity",       (object)FogDensity);
                TrySetMember(atmoData, atmoType, "FogColor",          (object)FogColor);
                TrySetMember(atmoData, atmoType, "SunBrightness",     (object)SunBrightness);
                TrySetMember(atmoData, atmoType, "AmbientBrightness", (object)AmbientBrightness);

                method.Invoke(scene, new[] { atmoData });
            }
            catch { }
        }

        private static void TrySetMember(object obj, Type t, string name, object value)
        {
            try
            {
                const BindingFlags flags =
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var field = t.GetField(name, flags);
                if (field != null) { field.SetValue(obj, value); return; }
                t.GetProperty(name, flags)?.SetValue(obj, value);
            }
            catch { }
        }
    }
}
