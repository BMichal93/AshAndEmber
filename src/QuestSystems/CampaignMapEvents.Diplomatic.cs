// =============================================================================
// ASH AND EMBER — CampaignMapEvents.Diplomatic.cs
// Player-event/diplomatic helpers and court events 24–28.
// Partial of CampaignMapEvents (shared state lives in CampaignMapEvents.cs).
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
    public static partial class CampaignMapEvents
    {
        // ── Player-event choice helpers ───────────────────────────────────────

        // Returns true when the player's clan is in the given kingdom at tier 4+.
        private static bool PlayerIsQualifiedForEvent(Kingdom kingdom)
        {
            var player = Hero.MainHero;
            if (player?.Clan == null) return false;
            return player.Clan.Kingdom == kingdom && player.Clan.Tier >= 4;
        }

        // Applies a relation delta between the player and all living adult members
        // of a clan (used by Stolen Heirloom, Tyranny, and Seeds of Betrayal choices).
        private static void PlayerRelationWithClan(Clan clan, int delta)
        {
            if (clan == null || Hero.MainHero == null) return;
            foreach (var h in clan.Heroes)
            {
                if (h == null || !h.IsAlive || h.IsChild || h == Hero.MainHero) continue;
                try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, h, delta, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Diplomatic incident helpers ───────────────────────────────────────
        // Maps the relation score between two kings to a probability that an
        // incident escalates to open war rather than cooling diplomatically.
        //   rel < −50 → 85%  (already bitter enemies)
        //   rel < −20 → 65%  (hostile)
        //   rel <  10 → 45%  (cold/neutral)
        //   rel <  40 → 25%  (cordial)
        //          else 10%  (genuine allies)
        private static double WarChanceFromRelation(int rel)
        {
            if (rel < -50) return 0.85;
            if (rel < -20) return 0.65;
            if (rel <  10) return 0.45;
            if (rel <  40) return 0.25;
            return 0.10;
        }

        // Fills ka/kb with two non-Ashen kingdoms that are currently at peace
        // with each other and both have a living leader. Returns false if no
        // such pair exists. The player's own kingdom is never picked — scripted
        // incident-wars must not drag the player's faction into a war it had no
        // part in, matching how Game of Thrones and Broken Will spare the player.
        private static bool TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)
        {
            ka = kb = null;
            var playerKingdom = Hero.MainHero?.Clan?.Kingdom;
            var pool = Kingdom.All
                .Where(k => !k.IsEliminated
                         && k.StringId != AshenKingdomId
                         && k.StringId != "vlandia"   // Holy Temple fights only the Ashen
                         && k != playerKingdom         // never force the player's faction into these wars
                         && k.Leader != null && k.Leader.IsAlive && !k.Leader.IsChild)
                .ToList();
            if (pool.Count < 2) return false;

            var pairs = new List<(Kingdom, Kingdom)>();
            for (int i = 0; i < pool.Count; i++)
                for (int j = i + 1; j < pool.Count; j++)
                    if (!pool[i].IsAtWarWith(pool[j]))
                        pairs.Add((pool[i], pool[j]));

            if (pairs.Count == 0) return false;
            var pick = pairs[_rng.Next(pairs.Count)];
            ka = pick.Item1; kb = pick.Item2;
            return true;
        }

        // ── Event 24: A Slight at Court ───────────────────────────────────────
        // An ambassador was publicly turned away from the rival king's hall.
        // If the kings already distrust each other the insult draws steel;
        // otherwise it is swallowed with gritted teeth and lasting bitterness.
        private static void TryFireASlightAtCourt()
        {
            if (_rng.NextDouble() >= ChanceASlightAtCourt) return;
            if (!TryClaimWarSlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Slight at Court — {lordB} of {nameB} turned away {nameA}'s envoy in the great hall " +
                        $"and had words said in front of witnesses that could not be unsaid. " +
                        $"{lordA} answered the insult with a sealed declaration. " +
                        $"{nameA} and {nameB} are now at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -15, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Slight at Court — {lordB} of {nameB} turned away {nameA}'s envoy, " +
                        $"but {lordA} chose restraint over retaliation. " +
                        $"The humiliation is remembered. The border is quieter than it should be."));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 25: Border Torches ──────────────────────────────────────────
        // Villages near the shared border burned in the night. Neither crown
        // claims the act; each accuses the other. Whether war follows depends
        // on how much the kings already suspect each other.
        private static void TryFireBorderTorches()
        {
            if (_rng.NextDouble() >= ChanceBorderTorches) return;
            if (!TryClaimWarSlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Border Torches — villages on the border between {nameA} and {nameB} " +
                        $"burned in the night. Both sides blame the other. " +
                        $"The smoke was still rising when the first cavalry crossed the line. " +
                        $"{nameA} and {nameB} are at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -10, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Border Torches — villages between {nameA} and {nameB} burned. " +
                        $"Accusations flew on both sides. {lordA} and {lordB} pulled back from the edge, " +
                        $"though neither believes the other's denials. The border is tense. The ashes are still warm."));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 26: A Debt in Blood ─────────────────────────────────────────
        // A lord's envoy was found dead in the rival kingdom's territory,
        // his seal broken and his escort missing. The accusation of murder
        // poisons what little trust remained between the two crowns.
        private static void TryFireADebtInBlood()
        {
            if (_rng.NextDouble() >= ChanceADebtInBlood) return;
            if (!TryClaimWarSlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Debt in Blood — a {nameA} envoy was found dead in {nameB} territory, " +
                        $"his seal broken and his escort nowhere to be found. " +
                        $"{lordA} did not wait for an inquiry. " +
                        $"{nameA} and {nameB} are at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -20, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Debt in Blood — a {nameA} envoy was found dead in {nameB} territory. " +
                        $"{lordB} opened an inquiry and sent condolences. {lordA} accepted both, barely. " +
                        $"The truth may never surface. The suspicion will not leave."));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 27: The Broken Betrothal ───────────────────────────────────
        // A political marriage arranged between two noble houses was dissolved —
        // one side backed out, or the bride fled, or a scandal made it impossible.
        // The offended house demands satisfaction. Kings who already mistrust each
        // other interpret the slight as a deliberate act of war; those on good
        // terms manage a quiet settlement and an awkward silence.
        private static void TryFireBrokenBetrothal()
        {
            if (_rng.NextDouble() >= ChanceBrokenBetrothal) return;
            if (!TryClaimWarSlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Broken Betrothal — the marriage between {nameA} and {nameB} was called off " +
                        $"before the ink was dry on the compact. The insult was too great and the timing too suspicious. " +
                        $"{lordA} returned the gifts and sent soldiers instead. " +
                        $"{nameA} and {nameB} are at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -15, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Broken Betrothal — the marriage between {nameA} and {nameB} fell apart before it began. " +
                        $"Gifts were quietly returned. No one mentioned it at court. " +
                        $"{lordA} and {lordB} exchanged letters that said nothing and meant everything. " +
                        $"The alliance is over. War was avoided, this time."));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 28: The Treasonous Scroll ───────────────────────────────────
        // Letters were intercepted — or claimed to have been — proving that
        // agents of one kingdom have been bribing officials in the other.
        // Whether the plot is real or fabricated matters less than the accusation:
        // kings who already suspect each other rarely need much convincing.
        private static void TryFireTreasonousScroll()
        {
            if (_rng.NextDouble() >= ChanceTreasonousScroll) return;
            if (!TryClaimWarSlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Treasonous Scroll — letters surfaced in {nameA} proving, or appearing to prove, " +
                        $"that {nameB} agents have been buying lords and poisoning counsel inside the court. " +
                        $"{lordA} read the letters, had the messengers arrested, and called his banners. " +
                        $"{nameA} and {nameB} are at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -20, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Treasonous Scroll — letters surfaced in {nameA} alleging {nameB} spies " +
                        $"inside the court. {lordB} denied everything and offered to open his archives. " +
                        $"{lordA} accepted the offer with a smile that did not reach his eyes. " +
                        $"Both crowns know the investigation will find nothing. Both crowns remember."));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 23: Embers of Hope ──────────────────────────────────────────
        // Fires once the Ashen kingdom holds at least EmbersOfHopeMinTowns towns.
        // The weight of a common darkness is enough to still old hatreds —
        // up to 3 random wars between non-Ashen kingdoms are ended as rivals
        // recognise that a greater threat walks among them.
        private static void TryFireEmbersOfHope()
        {
            if (_rng.NextDouble() >= ChanceEmbersOfHope) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                // Condition: Ashen must hold at least EmbersOfHopeMinTowns towns.
                var ashen = Kingdom.All.FirstOrDefault(k => k.StringId == AshenKingdomId && !k.IsEliminated);
                if (ashen == null) return;

                int ashenTowns = Settlement.All.Count(s => s.IsTown && s.Town != null && s.MapFaction == ashen);
                if (ashenTowns < EmbersOfHopeMinTowns) return;

                // Collect every active war between two non-Ashen kingdoms.
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();

                var warPairs = new List<(Kingdom a, Kingdom b)>();
                for (int i = 0; i < kingdoms.Count; i++)
                    for (int j = i + 1; j < kingdoms.Count; j++)
                        if (kingdoms[i].IsAtWarWith(kingdoms[j]))
                            warPairs.Add((kingdoms[i], kingdoms[j]));

                if (warPairs.Count == 0) return;

                // Fisher-Yates shuffle, then take up to EmbersOfHopePeaceCount pairs.
                for (int i = warPairs.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    var tmp = warPairs[i]; warPairs[i] = warPairs[j]; warPairs[j] = tmp;
                }

                var peacedNames = new List<string>();
                foreach (var (a, b) in warPairs.Take(EmbersOfHopePeaceCount))
                {
                    try
                    {
                        MakePeaceAction.Apply(a, b);
                        peacedNames.Add($"{a.Name} and {b.Name}");
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                if (peacedNames.Count == 0) return;

                string conflicts = string.Join("; ", peacedNames);
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Embers of Hope — the Ashen hold {ashenTowns} cities now. " +
                    $"Beneath that shadow old quarrels feel small and foolish. " +
                    $"Banners are lowered and bitter words withdrawn: " +
                    $"{peacedNames.Count} war{(peacedNames.Count != 1 ? "s" : "")} end{(peacedNames.Count == 1 ? "s" : "")}: {conflicts}."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

    }
}
