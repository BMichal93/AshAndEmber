// =============================================================================
// ASH AND EMBER — SchemeSystem.Persistence.cs
// Save/load.
// Partial of SchemeSystem (shared state lives in SchemeSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    internal static partial class SchemeSystem
    {
        // ── Save / Load ───────────────────────────────────────────────────────
        internal static void Save(IDataStore store)
        {
            var types  = _pending.Select(p => (int)p.Type).ToList();
            var insts  = _pending.Select(p => p.InstigatorId).ToList();
            var tHero  = _pending.Select(p => p.TargetHeroId).ToList();
            var tSett  = _pending.Select(p => p.TargetSettlementId).ToList();
            var days   = _pending.Select(p => p.DaysRemaining).ToList();
            var isPlyr = _pending.Select(p => p.IsPlayer ? 1 : 0).ToList();

            store.SyncData("SCH_Types",   ref types);
            store.SyncData("SCH_Insts",   ref insts);
            store.SyncData("SCH_THero",   ref tHero);
            store.SyncData("SCH_TSett",   ref tSett);
            store.SyncData("SCH_Days",    ref days);
            store.SyncData("SCH_IsPlayer",ref isPlyr);

            var cdKeys = _npcCooldowns.Keys.ToList();
            var cdVals = _npcCooldowns.Values.ToList();
            store.SyncData("SCH_CdKeys",  ref cdKeys);
            store.SyncData("SCH_CdVals",  ref cdVals);

            // Restore from loaded data
            if (types != null)
            {
                _pending.Clear();
                for (int i = 0; i < types.Count; i++)
                    _pending.Add(new PendingScheme
                    {
                        Type               = (SchemeType)(types[i]),
                        InstigatorId       = insts?[i]  ?? "",
                        TargetHeroId       = tHero?[i]  ?? "",
                        TargetSettlementId = tSett?[i]  ?? "",
                        DaysRemaining      = days?[i]   ?? 1,
                        IsPlayer           = (isPlyr?[i] ?? 0) != 0
                    });
            }
            if (cdKeys != null && cdVals != null && cdKeys.Count == cdVals.Count)
            {
                _npcCooldowns.Clear();
                for (int i = 0; i < cdKeys.Count; i++)
                    _npcCooldowns[cdKeys[i]] = cdVals[i];
            }

            var tcdKeys = _targetCooldowns.Keys.ToList();
            var tcdVals = _targetCooldowns.Values.ToList();
            store.SyncData("SCH_TcdKeys", ref tcdKeys);
            store.SyncData("SCH_TcdVals", ref tcdVals);
            if (tcdKeys != null && tcdVals != null && tcdKeys.Count == tcdVals.Count)
            {
                _targetCooldowns.Clear();
                for (int i = 0; i < tcdKeys.Count; i++)
                    _targetCooldowns[tcdKeys[i]] = tcdVals[i];
            }

            var pckList = _playerCooldownKeys.ToList();
            store.SyncData("SCH_PckList", ref pckList);
            if (pckList != null)
            {
                _playerCooldownKeys.Clear();
                foreach (var k in pckList) _playerCooldownKeys.Add(k);
            }

            store.SyncData("SCH_RetDays",    ref _retaliationDays);
            store.SyncData("SCH_GlobCd",     ref _playerGlobalCooldown);

            store.SyncData("SCH_PendOpType", ref _pendingOpType);
            store.SyncData("SCH_PendOpHero", ref _pendingOpHeroId);
            store.SyncData("SCH_PendOpSett", ref _pendingOpSettId);
            store.SyncData("SCH_PendOpSkip", ref _pendingOpSkip);
        }
    }
}
