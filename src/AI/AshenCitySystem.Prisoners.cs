// =============================================================================
// ASH AND EMBER — AshenCitySystem.Prisoners.cs
// Ashen prisoner fate and the player capture prompt.
// Partial of AshenCitySystem (shared static state lives in AshenCitySystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public partial class AshenCitySystem
    {
        // ── Prisoner fate ─────────────────────────────────────────────────────
        // Each cycle: 60% chance the Ashen leave a prisoner alone (normal captivity).
        // The remaining 40% triggers an ultimatum — join the cold or be executed.
        //   Player: queues a deferred choice dialog (join Ashen vs permanent death).
        //   NPC lords: auto-resolved — 50% yield and become Ashen, 50% refuse and die.
        // At most 1 heavy action (kill/release/convert) per call to avoid cascading
        // KillCharacterAction calls on a single daily tick.
        public static void ExecuteAshenPrisoners()
        {
            if (_ashenClanIds.Count == 0) return;
            try
            {
                foreach (Hero hero in Hero.AllAliveHeroes.ToList())
                {
                    try
                    {
                        bool isPlayer = hero == Hero.MainHero;
                        if (!hero.IsPrisoner || hero.IsChild) continue;
                        if (!isPlayer && !hero.IsLord) continue;

                        var captorParty = hero.PartyBelongedToAsPrisoner;
                        if (captorParty == null) continue;

                        bool captorIsAshen =
                            (captorParty.LeaderHero != null &&
                             ColourLordRegistry.IsAshenLord(captorParty.LeaderHero)) ||
                            captorParty.MapFaction?.StringId == AshenKingdomId;
                        if (!captorIsAshen) continue;

                        // 60% chance: the Ashen leave this prisoner to languish — for now.
                        if (_rng.NextDouble() >= 0.40) continue;

                        if (isPlayer)
                        {
                            if (MageKnowledge._deferredInquiry == null)
                            {
                                Hero exec = captorParty.LeaderHero;
                                MageKnowledge._deferredInquiry = () => ShowAshenCapturePrompt(exec);
                            }
                            continue;
                        }

                        // NPC lord: the Ashen force the ultimatum — join or die.
                        // 50% yield to the cold, 50% refuse and are executed.
                        Hero executor = captorParty.LeaderHero
                                     ?? Hero.AllAliveHeroes.FirstOrDefault(h =>
                                            h.IsAlive && !h.IsDisabled && !h.IsPrisoner &&
                                            _ashenClanIds.Contains(h.Clan?.StringId));

                        if (_rng.NextDouble() < 0.50)
                        {
                            try { ColourLordRegistry.SetAshen(hero, true); } catch { }
                            try { EndCaptivityAction.ApplyByReleasedAfterBattle(hero); } catch { }
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} has taken the cold. They walk free — and Ashen.",
                                new Color(0.38f, 0.50f, 0.75f)));
                        }
                        else
                        {
                            if (executor == null) continue;
                            try { KillCharacterAction.ApplyByExecution(hero, executor); } catch { }
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} refused the cold. The Ashen gave them the ending they chose.",
                                new Color(0.55f, 0.30f, 0.30f)));
                        }

                        return; // one action per tick — process remaining on next call
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Player capture prompt ─────────────────────────────────────────────
        private static void ShowAshenCapturePrompt(Hero captor)
        {
            string captorName = captor?.Name.ToString() ?? "the Ashen lord";

            InformationManager.ShowInquiry(new InquiryData(
                "The Grey Lords",

                $"{captorName} crouches to your level. The battlefield has gone quiet — your men are dead or fled. " +
                "They study you the way fire studies wood.\n\n" +
                "When they speak, their voice carries the cold of something very old.\n\n" +
                "\"You burned well,\" they say. \"That kind of fire does not simply go out. " +
                "It changes. We have seen it many times.\"\n\n" +
                "They extend a hand. The skin is grey-white, faintly luminous. Like ash that has forgotten it was ever warm.\n\n" +
                "\"Take the cold. Or do not. Both are a kind of ending.\"",

                true, true,
                "Take the cold. Let it have me.",
                "I have lived long enough.",

                () =>
                {
                    // Join the Ashen
                    try
                    {
                        MageKnowledge.SetAshen(true);
                        try { MageKnowledge.ApplyAshenAppearance(Hero.MainHero); } catch { }
                        try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                        try { EndCaptivityAction.ApplyByReleasedAfterBattle(Hero.MainHero); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Something ancient settles in you. The warmth you have always carried shifts — " +
                            "not gone, but changed. Cold fire. Grey flame. You are still here. " +
                            "But something that was purely yours is no longer.",
                            new Color(0.35f, 0.35f, 0.75f)));
                    }
                    catch { }
                },

                () =>
                {
                    // Accept execution
                    try
                    {
                        if (captor != null)
                            try { KillCharacterAction.ApplyByExecution(Hero.MainHero, captor); } catch { }
                        else
                            try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, true); } catch { }
                    }
                    catch { }
                }
            ), true, true);
        }
    }
}
