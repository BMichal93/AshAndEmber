// =============================================================================
// ASH AND EMBER — SchemeSystem.Helpers.cs
// Cooldown notices, lookups, bandit spawning.
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
        // ── Helpers ───────────────────────────────────────────────────────────

        // Fires when a player-set per-target cooldown expires.
        // Key format: "{schemeTypeInt}:{targetStringId}"
        private static void NotifyCooldownExpired(string key)
        {
            var parts = key.Split(new[] { ':' }, 2);
            if (parts.Length < 2) return;
            if (!int.TryParse(parts[0], out int typeInt)) return;
            var  type       = (SchemeType)typeInt;
            bool hardBlock  = type == SchemeType.Assassinate;
            var  def        = GetDefinition(type);
            string scheme   = def?.Name ?? "The scheme";

            // Resolve target name
            string targetId = parts[1];
            string target   = "the target";
            try
            {
                Hero h = FindHero(targetId);
                if (h != null) target = h.Name?.ToString() ?? target;
                else
                {
                    Settlement s = FindSettlement(targetId);
                    if (s != null) target = s.Name?.ToString() ?? target;
                }
            }
            catch { }

            string msg = hardBlock
                ? $"Contacts reset — the path to {target} is open again. Assassination may be attempted."
                : $"Network cooled — {scheme} against {target} may be repeated at normal cost.";

            MBInformationManager.AddQuickInformation(new TextObject(msg));
        }

        internal static SchemeDefinition GetDefinition(SchemeType type)
            => Definitions.FirstOrDefault(d => d.Type == type);

        private static Hero FindHero(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id)
                       ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h.StringId == id); }
            catch { return null; }
        }

        private static Settlement FindSettlement(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return Settlement.All.FirstOrDefault(s => s.StringId == id); }
            catch { return null; }
        }

        // Notify: player schemes → popup notification; NPC schemes → console log.
        // Schemes targeting the player are always shown as popups regardless of instigator.
        private static void Notify(PendingScheme s, string text, Color color)
        {
            bool targetIsPlayer = s.TargetHeroId == Hero.MainHero?.StringId;
            if (s.IsPlayer || targetIsPlayer)
                MBInformationManager.AddQuickInformation(new TextObject(text));
            else
                InformationManager.DisplayMessage(new InformationMessage(text, color));
        }

        // Spawns bandit parties throughout the target kingdom, each tied to the
        // nearest hideout — critical to avoid the null-hideout crash. Mirrors the
        // SpawnLooterParty pattern in CampaignMapEvents.cs exactly.
        // Returns the number of parties actually created.
        internal static int SpawnBanditsInKingdom(Kingdom kingdom, int partyCount)
        {
            if (kingdom == null || kingdom.IsEliminated) return 0;

            Clan banditClan = Clan.BanditFactions.FirstOrDefault(c => c != null && !c.IsEliminated);
            if (banditClan == null) return 0;
            var pt = banditClan.DefaultPartyTemplate;
            if (pt == null) return 0;

            CharacterObject troop =
                MBObjectManager.Instance.GetObject<CharacterObject>("looter")
             ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit");
            if (troop == null) return 0;

            // Gather settlement positions in the target kingdom as spawn anchors.
            var anchors = Settlement.All
                .Where(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == kingdom)
                .Select(s => s.GetPosition2D)
                .ToList();
            if (anchors.Count == 0) return 0;

            int spawned = 0;
            for (int i = 0; i < partyCount; i++)
            {
                try
                {
                    Vec2 anchor = anchors[_rng.Next(anchors.Count)];

                    // 3-level hideout fallback — never pass null to CreateBanditParty.
                    Hideout hideout = null;
                    try
                    {
                        Settlement hs = banditClan.Settlements.FirstOrDefault(s => s?.Hideout != null);
                        if (hs == null)
                            hs = Settlement.All
                                .Where(s => s?.Hideout != null)
                                .OrderBy(s => (s.GetPosition2D.x - anchor.x) * (s.GetPosition2D.x - anchor.x)
                                            + (s.GetPosition2D.y - anchor.y) * (s.GetPosition2D.y - anchor.y))
                                .FirstOrDefault();
                        if (hs == null) hs = Settlement.All.FirstOrDefault(s => s?.Hideout != null);
                        hideout = hs?.Hideout;
                    }
                    catch { }
                    if (hideout == null) continue;

                    const float scatter = 5f;
                    Vec2 sp = anchor + new Vec2(
                        (float)(_rng.NextDouble() - 0.5) * scatter * 2f,
                        (float)(_rng.NextDouble() - 0.5) * scatter * 2f);
                    var cv = new CampaignVec2(sp, true);

                    int troops = 20 + _rng.Next(16); // 20–35 per party
                    string pid = "scatter_wolves_" + _rng.Next(999999).ToString("D6");

                    MobileParty party = BanditPartyComponent.CreateBanditParty(pid, banditClan, hideout, false, pt, cv);
                    if (party == null) continue;
                    party.MemberRoster.AddToCounts(troop, troops);
                    spawned++;
                }
                catch { }
            }
            return spawned;
        }

    }
}
