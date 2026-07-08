// =============================================================================
// ASH AND EMBER — GreatAwakeningCampaignBehavior.Resolution.cs
//
// Once the sacrifice is complete, a 50/50 roll (independent of which path the
// player was on) decides which ending fires:
//   A (controlled)   — The Great Other serves Duneborn; Duneborn declares war
//                       on everyone and will not make peace while it lives.
//   B (uncontrolled) — The Great Other answers to no one and roams free.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class GreatAwakeningCampaignBehavior
    {
        // Persisted so a mid-resolution save (the same weekly tick that rolls the
        // ending and summons the party) can't accidentally roll or summon twice.
        private static bool _resolutionHandled = false;

        private static void SyncResolutionData(IDataStore store)
        {
            try { store.SyncData("GRAWK_ResolutionHandled", ref _resolutionHandled); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ResetResolutionState() { _resolutionHandled = false; }

        private void ResolutionWeeklyTick()
        {
            if (_phase != PhaseActive) return;
            if (_prisonersSacrificed < GreatAwakeningMath.PrisonerTarget) return;
            if (_resolutionHandled) return;
            _resolutionHandled = true;

            bool controlled = GreatAwakeningMath.ResolutionIsControlled(_rng);
            _phase = controlled ? PhaseResolvedControlled : PhaseResolvedUncontrolled;

            var altar = AltarSettlement();
            try { GreatOtherParty.Summon(altar, controlled); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (controlled)
            {
                try { DeclarePermanentWar(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "The count is paid. The Dark Altar drinks the last of it — and The Great Other steps out " +
                        "of the space behind the stone, into Duneborn's service. Every kingdom in Calradia is now " +
                        "Duneborn's enemy, and Duneborn means to keep it that way until the thing dies."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { GreatAwakeningQuestLog.CompleteSummoningControlled(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            else
            {
                try
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "The count is paid — but whatever answered the Dark Altar answers to no one. The Great " +
                        "Other has come, and it belongs to nothing and no one, least of all Duneborn."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { GreatAwakeningQuestLog.CompleteSummoningUncontrolled(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // Duneborn at war with the entire world — the diplomacy-model side of
        // "will not stop until The Great Other dies" lives in
        // AshenDiplomacyModel.IsDunebornPermanentWar (blocks peace while the
        // controlled Great Other is alive); this just opens every war at once.
        private static void DeclarePermanentWar()
        {
            Kingdom duneborn = DunebornKingdom();
            if (duneborn == null) return;
            foreach (var other in Kingdom.All)
            {
                if (other == null || other == duneborn || other.IsEliminated) continue;
                if (duneborn.IsAtWarWith(other)) continue;
                try { DeclareWarAction.ApplyByDefault(duneborn, other); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }
    }
}
