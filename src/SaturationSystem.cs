// =============================================================================
// COLOURS OF CALRADIA â€” SaturationSystem.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0
// =============================================================================
// The mage absorbs light to split it into colour. Absorbing too much is dangerous.
//
// Player Saturation:
//   Max = hero.Level + 10 (cap 30). Starts at 0.
//   Each cast gains 0â€“3 saturation randomly.
//   Resets to 0 when darkness falls (night time or dark location).
//   Oversaturation (â‰Ą max): brief interruption, random trait shift, max â’1 permanently.
//   When max reaches 0: player chooses to lose all colours or become a Blight.
//
// Blights and the Prism are fully immune to all oversaturation effects.
// NPC battle interruption (5% per cast) is handled in ColourLordAI.
// Weekly world event (2% NPC oversaturation) is handled in CampaignBehavior.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ColoursOfCalradia
{
    public static class SaturationSystem
    {
        private static int  _playerSaturation         = 0;
        private static int  _playerMaxSaturation       = 10;
        private static bool _playerIsBlight            = false;
        private static bool _playerIsPrism             = false;
        private static bool _saturationResetThisNight  = false;
        private static bool _maxDepletionPromptPending  = false;

        // Brief battle interruption timer
        private static readonly Dictionary<int, float> _knockdownTimers = new Dictionary<int, float>();
        private static readonly ActionIndexCache _knockdownAction = ActionIndexCache.Create("act_knock_down");

        private static readonly Random _rng = new Random();

        public static bool IsPlayerBlight => _playerIsBlight;
        public static bool IsPlayerPrism  => _playerIsPrism;
        public static int  PlayerSaturation    => _playerSaturation;
        public static int  PlayerMaxSaturation => _playerMaxSaturation;

        // â”€â”€ Called on new game start â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static void ResetForNewGame()
        {
            _playerSaturation         = 0;
            _playerMaxSaturation      = Math.Min(30, Math.Max(1, (Hero.MainHero?.Level ?? 1) + 10));
            _playerIsBlight           = false;
            _playerIsPrism            = false;
            _saturationResetThisNight = false;
            _maxDepletionPromptPending = false;
            _knockdownTimers.Clear();
        }

        // â”€â”€ Called after each successful player cast â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static void GainSaturation()
        {
            if (!ColourKnowledge.HasAnySchool) return;
            if (_playerIsBlight || _playerIsPrism) return;
            if (_playerMaxSaturation <= 0) return;

            int gain = _rng.Next(0, 4); // 0, 1, 2, or 3
            _playerSaturation = Math.Min(_playerSaturation + gain, _playerMaxSaturation);

            string gainStr = gain > 0 ? $" (+{gain})" : "";
            InformationManager.DisplayMessage(new InformationMessage(
                $"Saturation: {_playerSaturation}/{_playerMaxSaturation}{gainStr}",
                new Color(0.6f, 0.4f, 0.9f)));

            if (_playerSaturation >= _playerMaxSaturation)
                TriggerOversaturation();
        }

        // â”€â”€ Called on hourly tick â€” resets saturation when darkness falls â”€â”€â”€â”€â”€
        public static void CheckNightReset()
        {
            if (SpellEffects.GetCampaignLightLevel() == SpellEffects.LightLevel.Dark)
            {
                if (!_saturationResetThisNight && _playerSaturation > 0)
                {
                    _playerSaturation         = 0;
                    _saturationResetThisNight = true;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "As darkness settles, your absorbed light disperses. Saturation resets to 0.",
                        new Color(0.5f, 0.3f, 0.7f)));
                }
            }
            else
            {
                _saturationResetThisNight = false;
            }
        }

        // â”€â”€ Called when player levels up â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static void RecalcMax()
        {
            if (_playerIsBlight || _playerIsPrism) return;
            if (_playerMaxSaturation >= 30) return;
            _playerMaxSaturation = Math.Min(30, _playerMaxSaturation + 1);
            InformationManager.DisplayMessage(new InformationMessage(
                $"Your capacity to hold light grows: Saturation max is now {_playerMaxSaturation}.",
                new Color(0.6f, 0.4f, 0.9f)));
        }

        // â”€â”€ Mission-tick: clears the temporary battle interruption after 3 s â”€â”€â”€
        public static void TickKnockdown(float dt)
        {
            if (_knockdownTimers.Count == 0 || Mission.Current == null) return;

            foreach (int agentIndex in _knockdownTimers.Keys.ToList())
            {
                _knockdownTimers[agentIndex] -= dt;
                if (_knockdownTimers[agentIndex] > 0f) continue;

                _knockdownTimers.Remove(agentIndex);
                try
                {
                    Agent agent = Mission.Current.Agents.FirstOrDefault(a => a.Index == agentIndex);
                    if (agent?.IsActive() == true)
                        agent.SetMaximumSpeedLimit(10f, false);
                }
                catch { }
            }
        }

        public static void ApplyKnockdown(Agent agent, float duration = 3f)
        {
            if (agent == null || !agent.IsActive() || Mission.Current == null) return;

            try { agent.SetActionChannel(0, _knockdownAction, true, 0UL); } catch { }
            try { agent.SetMaximumSpeedLimit(0f, false); } catch { }

            if (_knockdownTimers.TryGetValue(agent.Index, out float existing))
                _knockdownTimers[agent.Index] = Math.Max(existing, duration);
            else
                _knockdownTimers[agent.Index] = duration;
        }

        public static void ClearKnockdowns()
        {
            _knockdownTimers.Clear();
        }

        // â”€â”€ Deferred prompt flush (daily tick) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static void FlushMaxDepletionPrompt()
        {
            if (!_maxDepletionPromptPending) return;
            _maxDepletionPromptPending = false;
            ShowMaxDepletionPrompt();
        }

        // â”€â”€ Oversaturation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private static void TriggerOversaturation()
        {
            _playerMaxSaturation = Math.Max(0, _playerMaxSaturation - 1);
            _playerSaturation    = 0;

            // Briefly interrupt the player in battle only â€” Agent.Main is null on campaign map
            try
            {
                if (Agent.Main?.IsActive() == true)
                {
                    ApplyKnockdown(Agent.Main, 3f);
                }
            }
            catch { }

            // Random trait shift
            string traitMsg = "";
            try
            {
                Hero hero = Hero.MainHero;
                if (hero != null)
                {
                    var traits = new[]
                    {
                        DefaultTraits.Mercy, DefaultTraits.Valor, DefaultTraits.Honor,
                        DefaultTraits.Generosity, DefaultTraits.Calculating
                    };
                    var   trait   = traits[_rng.Next(traits.Length)];
                    int   current = hero.GetTraitLevel(trait);
                    int   shift   = _rng.Next(2) == 0 ? 1 : -1;
                    int   next    = Math.Max(-2, Math.Min(2, current + shift));
                    if (next != current) { hero.SetTraitLevel(trait, next); traitMsg = $" {trait.StringId} ({next:+0;-0})."; }
                }
            }
            catch { }

            InformationManager.DisplayMessage(new InformationMessage(
                $"OVERSATURATED â€” Light tears through you.{traitMsg} Max saturation now {_playerMaxSaturation}.",
                new Color(0.9f, 0.5f, 1.0f)));

            if (_playerMaxSaturation <= 0)
                _maxDepletionPromptPending = true;
        }

        private static void ShowMaxDepletionPrompt()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Light Consumes You",
                "Your capacity to hold magic is exhausted by Oversaturation. Two paths remain.\n\n" +
                "Surrender your colours â€” the magic fades entirely. Others will inherit them in time.\n\n" +
                "Embrace the Blight â€” keep your colours, but Calradia will turn against you permanently. " +
                "You become immune to further Oversaturation.",
                new List<InquiryElement>
                {
                    new InquiryElement("lose",  "Surrender your colours", null, true,
                        "All colour schools are lost. Others will inherit them in time."),
                    new InquiryElement("blight", "Embrace the Blight",    null, true,
                        "Keep your colours. -100 relations with every lord. Immune to Oversaturation. Crime rating hits the maximum everywhere."),
                },
                false, 1, 1,
                "Accept your fate.",
                "",
                chosen =>
                {
                    string choice = chosen?.Count > 0 ? chosen[0].Identifier?.ToString() : "lose";
                    if (choice == "blight") BecomePlayerBlight();
                    else                    LoseAllColours();
                },
                null,
                "", false
            ), false, true);
        }

        private static void LoseAllColours()
        {
            _playerMaxSaturation = 0;
            ColourKnowledge.ClearAllSchools();
            InformationManager.DisplayMessage(new InformationMessage(
                "Your colours are gone. The magic you carried returns to the world â€” others will inherit it in time.",
                new Color(0.7f, 0.7f, 0.7f)));
        }

        private static void BecomePlayerBlight()
        {
            _playerIsBlight      = true;
            _playerMaxSaturation = 0;
            try
            {
                Hero player = Hero.MainHero;
                if (player != null)
                    foreach (Hero h in Hero.AllAliveHeroes.ToList())
                    {
                        if (h == player || !h.IsLord) continue;
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, h, -100, false); } catch { }
                    }
            }
            catch { }
            try
            {
                var crimeFactions = new List<IFaction>();
                if (Campaign.Current?.Kingdoms != null)
                    crimeFactions.AddRange(Campaign.Current.Kingdoms.Cast<IFaction>());

                IFaction clanFaction = Hero.MainHero?.Clan?.Kingdom as IFaction ?? Hero.MainHero?.Clan as IFaction;
                if (clanFaction != null)
                    crimeFactions.Add(clanFaction);

                foreach (IFaction faction in crimeFactions.Distinct())
                    try { ChangeCrimeRatingAction.Apply(faction, 100f, false); } catch { }
            }
            catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                "You embrace the Blight. Your colours remain, but Calradia knows what you are. All relations collapse, and your crime rating is driven to the maximum everywhere.",
                new Color(0.5f, 0.0f, 0.8f)));
        }

        // â”€â”€ Prism immunity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static void SetPlayerPrism(bool isPrism)
        {
            _playerIsPrism = isPrism;
            if (isPrism)
                InformationManager.DisplayMessage(new InformationMessage(
                    "You are the Prism. Madness and Oversaturation will not touch you.",
                    new Color(0.9f, 0.7f, 1.0f)));
        }

        // â”€â”€ Save / Load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static void Save(IDataStore store)
        {
            store.SyncData("COC_Saturation",            ref _playerSaturation);
            store.SyncData("COC_SatMax",              ref _playerMaxSaturation);
            store.SyncData("COC_IsPlayerBlight",      ref _playerIsBlight);
            store.SyncData("COC_IsPlayerPrism",       ref _playerIsPrism);
            store.SyncData("COC_SatResetThisNight",   ref _saturationResetThisNight);
            store.SyncData("COC_MaxDepletionPending",  ref _maxDepletionPromptPending);
        }
    }
}
