// =============================================================================
// LIFE & DEATH MAGIC — Visual/AshenSceneTone.cs
// Applies a cold-grey atmospheric tint to missions involving the Ashen.
// Triggered when the player is Ashen OR when enemy Ashen lords are present.
// Uses reflection to call Scene.SetFog — silently skipped if the API changes.
// =============================================================================

using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class AshenSceneTone
    {
        private static bool  _checked     = false;
        private static float _warmupTimer = 0f;
        private const  float WarmupDuration = 3f;

        private static MethodInfo _setFogMethod;
        private static bool       _fogResolved = false;

        private static MethodInfo _setSunMethod;
        private static bool       _sunResolved = false;

        public static void MissionTick(float dt)
        {
            if (_checked) return;
            if (!SpellEffects.IsBattleMission()) return;

            _warmupTimer += dt;
            if (_warmupTimer < WarmupDuration) return;
            _checked = true;

            bool playerAshen = MageKnowledge.IsAshen;
            bool ashenEnemies = false;
            try
            {
                if (Agent.Main != null && Mission.Current != null)
                    ashenEnemies = Mission.Current.Agents.Any(a =>
                        a != null && a.IsActive() && !a.IsMount && a.IsHero &&
                        a.Team != null && Agent.Main.Team != null && a.Team != Agent.Main.Team &&
                        (a.Character as CharacterObject)?.HeroObject is Hero h &&
                        ColourLordRegistry.IsAshenLord(h));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (!playerAshen && !ashenEnemies) return;

            ApplyColdFog();
            TryApplyAshenSky();

            string toneMsg = ashenEnemies
                ? "A shadow lord's cold fire reaches across the field. The sky drains."
                : "The cold fire spreads. The air carries ash.";
            InformationManager.DisplayMessage(new InformationMessage(
                toneMsg, new Color(0.35f, 0.4f, 0.6f)));
        }

        private static void ApplyColdFog()
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return;

                if (!_fogResolved)
                {
                    _fogResolved = true;
                    _setFogMethod = scene.GetType().GetMethod("SetFog",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (_setFogMethod == null) return;
                var parms = _setFogMethod.GetParameters();
                if (parms.Length == 3)
                    _setFogMethod.Invoke(scene, new object[]
                        { 0.004f, new Vec3(0.48f, 0.52f, 0.68f), 1.2f });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void TryApplyAshenSky()
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return;

                if (!_sunResolved)
                {
                    _setSunMethod = scene.GetType().GetMethod("SetSun",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _sunResolved = true;
                }

                if (_setSunMethod == null) return;
                var parms = _setSunMethod.GetParameters();
                if (parms.Length == 4)
                    _setSunMethod.Invoke(scene, new object[]
                        { new Vec3(0.3f, 0.7f, 0.4f), new Vec3(0.5f, 0.55f, 0.7f), 1.0f, 0.4f });
                else if (parms.Length == 3)
                    _setSunMethod.Invoke(scene, new object[]
                        { new Vec3(0.3f, 0.7f, 0.4f), new Vec3(0.5f, 0.55f, 0.7f), 1.0f });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void Reset()
        {
            _checked     = false;
            _warmupTimer = 0f;
            _sunResolved = false;
            _setSunMethod = null;
        }
    }
}
