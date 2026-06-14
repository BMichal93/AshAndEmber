// =============================================================================
// ASH AND EMBER — Tavern/TavernCampaignBehavior.cs
//
// "Drinking with Locals" — a push-your-luck tavern game.
//
// UI flow:
//   Tavernkeeper dialogue → "ldm_tavern_menu" (order screen)
//     → choose drink tier (20 / 50 / 100 gold per round)
//       → Athletics test + outcome → "ldm_tavern_result" (what happened)
//         → "Another round" (back to order) or "Call it a night" (exit)
//         → [if passed out] → "ldm_tavern_sober_up" wait menu (4-8 hours)
//
// State is entirely transient — no save/load needed.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class TavernCampaignBehavior : CampaignBehaviorBase
    {
        // ── Transient session state ───────────────────────────────────────────
        private static int    _roundsDrunk       = 0;
        private static int    _totalSpent        = 0;
        private static int    _lastDrinkCost     = 50; // remembered between rounds
        private static float  _soberHoursTotal   = 0f;
        private static float  _soberHoursElapsed = 0f;
        private static bool   _soberDone         = false;

        private static readonly Random _rng = new Random();

        private static readonly Color GoodColor = new Color(0.56f, 0.93f, 0.56f);
        private static readonly Color BadColor  = new Color(0.93f, 0.40f, 0.40f);
        private static readonly Color DimColor  = new Color(0.78f, 0.78f, 0.78f);

        // ── CampaignBehaviorBase ──────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore store) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            ResetSessionState();
            try { RegisterDialogue(starter);  } catch { }
            try { RegisterMenus(starter);     } catch { }
        }

        private static void ResetSessionState()
        {
            _roundsDrunk       = 0;
            _totalSpent        = 0;
            _soberHoursTotal   = 0f;
            _soberHoursElapsed = 0f;
            _soberDone         = false;
        }
    }
}
