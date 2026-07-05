// =============================================================================
// COLOURS OF CALRADIA — ActiveEffects.cs
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

namespace AshAndEmber
{
    // =========================================================================
    // 5. ACTIVE EFFECT MANAGER  — timed buffs & debuffs (unchanged from v1)
    // =========================================================================
    public class ActiveEffect
    {
        public string   Name;
        public float    Duration;
        public float    Elapsed;
        public bool     IsMissionEffect;
        public Action<float> OnTick;
        public Action   OnExpire;
        public bool IsExpired => Elapsed >= Duration;
    }

    public static class ActiveEffectManager
    {
        private static readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        private const int MaxEffects = 50;

        public static void Add(ActiveEffect e)
        {
            if (_effects.Count >= MaxEffects) return;
            _effects.Add(e);
        }

        public static bool Has(string name) =>
            _effects.Any(e => e.Name == name && !e.IsExpired);

        public static void Remove(string name) =>
            _effects.RemoveAll(e => e.Name == name);

        public static void MissionTick(float dt) => Tick(dt, true);
        public static void MapTick(float dt)     => Tick(dt, false);

        private static void Tick(float dt, bool inMission)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var e = _effects[i];
                if (e.IsMissionEffect != inMission) continue;
                e.Elapsed += dt;
                try { e.OnTick?.Invoke(dt); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (!e.IsExpired) continue;
                try { e.OnExpire?.Invoke(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _effects.RemoveAt(i);
            }
        }

        public static void ClearMissionEffects() =>
            _effects.RemoveAll(e => e.IsMissionEffect);
    }
}
