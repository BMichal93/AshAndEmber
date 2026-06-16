// =============================================================================
// ASH AND EMBER — SettlementEncounters.Ambient.cs
// Ambient comments from NPCs who notice the player's accelerated aging.
// Fires independently of the encounter pool — a brief quick-info line, never
// a popup — so it doesn't compete with or clobber real encounter dialogs.
// Partial of SettlementEncounters (shared state lives in SettlementEncounters.cs).
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem.Settlements;

namespace AshAndEmber
{
    public static partial class SettlementEncounters
    {
        // Called from TryFireEnter and TryFireLeave before pool selection.
        // 20% chance per settlement transition, 50-day cooldown, age ≥ 50 only.
        internal static void CheckAgingAmbient(Settlement s)
        {
            try
            {
                if (!MageKnowledge.IsMage || MageKnowledge.IsAshen) return;
                if (Hero.MainHero == null) return;
                int age = (int)Hero.MainHero.Age;
                if (age < 50) return;
                if (_agingCommentCooldown > 0) return;
                if (_rng.Next(100) >= 20) return;

                _agingCommentCooldown = 50;
                ShowAgingComment(age);
            }
            catch { }
        }

        private static readonly string[][] _agingCommentsByBracket =
        {
            // age 50–64
            new[] {
                "A child asks their father why the lord looks so old. You hear the question from ten feet away.",
                "A guardsman straightens his post when you pass. Mages who live this long make men careful.",
                "Someone in the crowd whispers. You don't turn to see who.",
                "A trader's wife touches her children's shoulders and watches you walk by.",
            },
            // age 65–79
            new[] {
                "A soldier watches you pass with the look of a man calculating something.",
                "You catch your reflection in a merchant's mirror. You look older than you did last winter.",
                "An elder at the gate bows lower than your rank requires. He's seen you before, you think — though you don't recall him.",
                "A young priest marks a ward sign as you enter. He doesn't hide it.",
            },
            // age 80+
            new[] {
                "A child points at you from a doorway. Their mother pulls them back and whispers. You don't need to hear it.",
                "A guardsman holds the gate open before you reach it. He doesn't meet your eyes. He knows what you are.",
                "The innkeeper gives you the corner table without being asked. There are songs about mages who lived this long, he says. Not kind ones.",
                "A beggar reaches for your purse and stops. He stares at your face and walks away without a word.",
            },
        };

        private static void ShowAgingComment(int age)
        {
            string[] pool = age >= 80
                ? _agingCommentsByBracket[2]
                : age >= 65
                    ? _agingCommentsByBracket[1]
                    : _agingCommentsByBracket[0];

            string line = pool[_rng.Next(pool.Length)];
            try { MBInformationManager.AddQuickInformation(new TextObject(line)); }
            catch { InformationManager.DisplayMessage(new InformationMessage(line, DimColor)); }
        }
    }
}
