// =============================================================================
// ASH AND EMBER — Visual/AshenVisuals.cs
// Witchy-ashen look for everything touched by the cold fire:
//   - Ashen Spawn troops (ashen_thrall / ashen_invoker) and the bandits of
//     Ashen Spawn parties
//   - Soldiers fighting under the Ashen kingdom's banner
//   - Ashen heroes (player and lords) — face change is persisted separately
//     via MageKnowledge.ApplyAshenAppearance, which delegates here
// Look: grey skin, cold pale-blue eyes, ash-grey hair, and (for troops) a
// ragged hood/cloak armour element swapped in at mission spawn.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class AshenVisuals
    {
        private const string AshenKingdomId = "ashen_kingdom";

        // Dedicated Ashen Spawn troop tree (ModuleData/troops.xml)
        private static readonly HashSet<string> _ashenTroopIds = new HashSet<string>
        {
            "ashen_thrall", "ashen_invoker",
        };

        // Cold ash-grey / frost-blue cloth tint for agents we spawn ourselves
        public const uint ClothAshGrey  = 0xFF46505A;
        public const uint ClothColdBlue = 0xFF2E4A66;

        // Cached armour-element lookups (rebuilt per game session)
        private static ItemObject _capeItem;
        private static bool       _capeSearched;
        private static ItemObject _hoodItem;
        private static bool       _hoodSearched;

        private static readonly string[] _capeHints =
            { "hood", "cloak", "shawl", "scarf", "cape" };
        private static readonly string[] _hoodHints =
            { "hood", "cowl", "wrapped", "kerchief" };

        public static void Reset()
        {
            _capeItem = null; _capeSearched = false;
            _hoodItem = null; _hoodSearched = false;
        }

        // ── Body-property key transforms (pure, covered by PureLogicTests) ───
        // All colour positions are empirical; the exact bit layout varies by
        // game version, so callers wrap application in try/catch.

        // Hair: clear saturation, near-zero hue → ash-grey hair.
        public static ulong AshenHairKey(ulong keyPart4) =>
            (keyPart4 & ~0x00FFFF0000000000UL) | 0x0000010000000000UL;

        // Eyes: high saturation with a blue hue → cold pale-blue iris.
        public static ulong AshenEyeKey(ulong keyPart5) =>
            (keyPart5 & ~0x00FFFF0000000000UL) | 0x00E0AA0000000000UL;

        // Skin: clearing the colour bytes approximates a pale grey/ashen tone.
        public static ulong AshenSkinKey(ulong keyPart7) =>
            keyPart7 & ~0x000000FFFFFF0000UL;

        public static BodyProperties MakeAshenBodyProperties(BodyProperties bp)
        {
            var sp = bp.StaticProperties;
            var newStatic = new StaticBodyProperties(
                sp.KeyPart1, sp.KeyPart2, sp.KeyPart3, AshenHairKey(sp.KeyPart4),
                AshenEyeKey(sp.KeyPart5), sp.KeyPart6, AshenSkinKey(sp.KeyPart7), sp.KeyPart8);
            return new BodyProperties(bp.DynamicProperties, newStatic);
        }

        // ── Detection ─────────────────────────────────────────────────────────

        public static bool ShouldLookAshen(Agent agent)
        {
            if (agent == null || agent.IsMount) return false;
            var character = agent.Character as CharacterObject;
            if (character != null && _ashenTroopIds.Contains(character.StringId)) return true;
            if (Campaign.Current == null) return false;

            if (agent.IsHero)
            {
                var hero = character?.HeroObject;
                if (hero == null) return false;
                return hero == Hero.MainHero
                    ? MageKnowledge.IsAshen
                    : ColourLordRegistry.IsAshenLord(hero);
            }

            // Regular troops: ashen when their origin party is an Ashen Spawn
            // band or fights for the Ashen kingdom (garrisons included).
            try
            {
                var party = agent.Origin?.BattleCombatant as PartyBase;
                if (party == null) return false;
                var mobile = party.MobileParty;
                if (mobile != null && FireWorshippersSystem.IsAshenSpawn(mobile)) return true;
                if (party.MapFaction?.StringId == AshenKingdomId) return true;
            }
            catch { }
            return false;
        }

        // ── Application ──────────────────────────────────────────────────────

        // Called from MagicMissionBehavior.OnAgentBuild for every agent.
        public static void TryApply(Agent agent)
        {
            if (!ShouldLookAshen(agent)) return;
            // Heroes keep their own gear — only the face turns ashen.
            ForceApply(agent, includeArmour: !agent.IsHero);
        }

        // Skips detection — for agents the mod spawns itself on the Ashen side
        // (e.g. The Rising), where fallback troop types carry no Ashen marker.
        public static void ForceApply(Agent agent, bool includeArmour = true)
        {
            if (agent == null || agent.IsMount) return;
            try
            {
                // Agent.UpdateBodyProperties is resolved by reflection so a
                // renamed/removed method degrades to "no face change" instead
                // of breaking the mod on other game versions.
                typeof(Agent).GetMethod("UpdateBodyProperties", new[] { typeof(BodyProperties) })
                    ?.Invoke(agent, new object[] { MakeAshenBodyProperties(agent.BodyPropertiesValue) });
            }
            catch { }
            if (includeArmour)
                TryApplyAshenArmour(agent);
        }

        // Swaps a ragged hood/cloak onto the agent: every ashen troop gets a
        // cape element; the dedicated Ashen Spawn tree also trades its helmet
        // for a hood. No-op when no suitable native item exists.
        private static void TryApplyAshenArmour(Agent agent)
        {
            try
            {
                var refresh = typeof(Agent).GetMethod("UpdateSpawnEquipmentAndRefreshVisuals",
                    new[] { typeof(Equipment) });
                if (refresh == null) return;
                Equipment src = agent.SpawnEquipment;
                if (src == null) return;

                var eq = new Equipment();
                for (int i = 0; i < (int)EquipmentIndex.NumEquipmentSetSlots; i++)
                    eq[(EquipmentIndex)i] = src[(EquipmentIndex)i];

                bool changed = false;
                var cape = FindWitchyItem(ItemObject.ItemTypeEnum.Cape,
                                          _capeHints, ref _capeItem, ref _capeSearched);
                if (cape != null)
                {
                    eq[EquipmentIndex.Cape] = new EquipmentElement(cape);
                    changed = true;
                }

                var character = agent.Character as CharacterObject;
                if (character != null && _ashenTroopIds.Contains(character.StringId))
                {
                    var hood = FindWitchyItem(ItemObject.ItemTypeEnum.HeadArmor,
                                              _hoodHints, ref _hoodItem, ref _hoodSearched);
                    if (hood != null)
                    {
                        eq[EquipmentIndex.Head] = new EquipmentElement(hood);
                        changed = true;
                    }
                }

                if (changed)
                    refresh.Invoke(agent, new object[] { eq });
            }
            catch { }
        }

        // Cheapest native item of the given type whose id matches a hint —
        // deterministic, and immune to item-id changes between game versions.
        private static ItemObject FindWitchyItem(ItemObject.ItemTypeEnum type,
            string[] hints, ref ItemObject cache, ref bool searched)
        {
            if (searched) return cache;
            searched = true;
            try
            {
                var items = MBObjectManager.Instance?.GetObjectTypeList<ItemObject>();
                if (items == null) return null;
                cache = items
                    .Where(it => it != null && it.ItemType == type && it.StringId != null
                              && hints.Any(h => it.StringId.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(it => it.Value)
                    .ThenBy(it => it.StringId)
                    .FirstOrDefault();
            }
            catch { cache = null; }
            return cache;
        }
    }
}
