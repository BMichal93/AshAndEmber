// =============================================================================
// COLOURS OF CALRADIA — SaturationSystem.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0
// =============================================================================
// The mage absorbs light to split it into colour. Absorbing too much is dangerous.
//
// Player Saturation:
//   Max = hero.Level + 10 (cap 30). Starts at 0.
//   Each cast gains 0–3 saturation randomly.
//   Resets to 0 when darkness falls (night time or dark location).
//   Oversaturation (≥ max): knocked down 3 s, random trait shift, max −1 permanently.
//   When max reaches 0: player chooses to lose all colours or become a Blight.
//
// Blights and the Prism are fully immune to all oversaturation effects.
// NPC battle knockdown (5% per cast) is handled in ColourLordAI.
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

        // Knockdown timer for battle oversaturation
        private static float _knockdownTimer = 0f;

        private static readonly Random _rng = new Random();

        public static bool IsPlayerBlight => _playerIsBlight;
        public static bool IsPlayerPrism  => _playerIsPrism;
        public static int  PlayerSaturation    => _playerSaturation;
        public static int  PlayerMaxSaturation => _playerMaxSaturation;

        // ── Called on new game start ──────────────────────────────────────────
        public static void ResetForNewGame()
        {
            _playerSaturation         = 0;
            _playerMaxSaturation      = Math.Min(30, Math.Max(1, (Hero.MainHero?.Level ?? 1) + 10));
            _playerIsBlight           = false;
            _playerIsPrism            = false;
            _saturationResetThisNight = false;
            _maxDepletionPromptPending = false;
            _knockdownTimer           = 0f;
        }

        // ── Called after each successful player cast ──────────────────────────
        public static void GainSaturation()
        {
            if (!ColourKnowledge.HasAnySchool) return;
            if (_playerIsBlight || _playerIsPrism) return;
            if (_playerMaxSaturation <= 0) return;

            int gain = _rng.Next(0, 4); // 0, 1, 2, or 3
            if (gain == 0) return;

            _playerSaturation = Math.Min(_playerSaturation + gain, _playerMaxSaturation);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Saturation: {_playerSaturation}/{_playerMaxSaturation} (+{gain})",
                new Color(0.6f, 0.4f, 0.9f)));

            if (_playerSaturation >= _playerMaxSaturation)
                TriggerOversaturation();
        }

        // ── Called on hourly tick — resets saturation when darkness falls ─────
        public static void CheckNightReset()
        {
            if (SpellEffects.GetLightLevel() == SpellEffects.LightLevel.Dark)
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

        // ── Called when player levels up ──────────────────────────────────────
        public static void RecalcMax()
        {
            if (_playerIsBlight || _playerIsPrism) return;
            Hero hero = Hero.MainHero;
            if (hero == null) return;
            int newMax = Math.Min(30, hero.Level + 10);
            if (newMax > _playerMaxSaturation)
            {
                _playerMaxSaturation = newMax;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Your capacity to hold light grows: Saturation max is now {_playerMaxSaturation}.",
                    new Color(0.6f, 0.4f, 0.9f)));
            }
        }

        // ── Mission-tick: clears knockdown freeze after 3 s ───────────────────
        public static void TickKnockdown(float dt)
        {
            if (_knockdownTimer <= 0f) return;
            _knockdownTimer -= dt;
            if (_knockdownTimer <= 0f)
                try { if (Agent.Main?.IsActive() == true) Agent.Main.SetMaximumSpeedLimit(10f, false); } catch { }
        }

        // ── Deferred prompt flush (daily tick) ────────────────────────────────
        public static void FlushMaxDepletionPrompt()
        {
            if (!_maxDepletionPromptPending) return;
            _maxDepletionPromptPending = false;
            ShowMaxDepletionPrompt();
        }

        // ── Oversaturation ────────────────────────────────────────────────────
        private static void TriggerOversaturation()
        {
            _playerMaxSaturation = Math.Max(0, _playerMaxSaturation - 1);
            _playerSaturation    = 0;

            // Freeze player briefly (battle only — Agent.Main is null on campaign map)
            try
            {
                if (Agent.Main?.IsActive() == true)
                {
                    Agent.Main.SetMaximumSpeedLimit(0f, false);
                    _knockdownTimer = 3f;
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
                $"OVERSATURATED — Light tears through you. Knocked down.{traitMsg} Max saturation now {_playerMaxSaturation}.",
                new Color(0.9f, 0.5f, 1.0f)));

            if (_playerMaxSaturation <= 0)
                _maxDepletionPromptPending = true;
        }

        private static void ShowMaxDepletionPrompt()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Light Consumes You",
                "Your capacity to hold magic is exhausted by Oversaturation. Two paths remain.\n\n" +
                "Surrender your colours — the magic fades entirely. Others will inherit them in time.\n\n" +
                "Embrace the Blight — keep your colours, but Calradia will turn against you permanently. " +
                "You become immune to further Oversaturation.",
                new List<InquiryElement>
                {
                    new InquiryElement("lose",  "Surrender your colours", null, true,
                        "All colour schools are lost. Others will inherit them in time."),
                    new InquiryElement("blight", "Embrace the Blight",    null, true,
                        "Keep your colours. −100 relations with every lord. Immune to Oversaturation."),
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
                "Your colours are gone. The magic you carried returns to the world — others will inherit it in time.",
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
            InformationManager.DisplayMessage(new InformationMessage(
                "You embrace the Blight. Your colours remain — but Calradia knows what you are. All relations collapse.",
                new Color(0.5f, 0.0f, 0.8f)));
        }

        // ── Prism immunity ────────────────────────────────────────────────────
        public static void SetPlayerPrism(bool isPrism)
        {
            _playerIsPrism = isPrism;
            if (isPrism)
                InformationManager.DisplayMessage(new InformationMessage(
                    "You are the Prism. Madness and Oversaturation will not touch you.",
                    new Color(0.9f, 0.7f, 1.0f)));
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            store.SyncData("COC_Saturation",          ref _playerSaturation);
            store.SyncData("COC_SatMax",              ref _playerMaxSaturation);
            store.SyncData("COC_IsPlayerBlight",      ref _playerIsBlight);
            store.SyncData("COC_IsPlayerPrism",       ref _playerIsPrism);
            store.SyncData("COC_SatResetThisNight",   ref _saturationResetThisNight);
        }
    }
}
