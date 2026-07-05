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
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;
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

        // All custom mod troops that already carry a meaningful name — do not overwrite with "Ashen Warrior"
        private static readonly HashSet<string> _namedModTroopIds = new HashSet<string>
        {
            "ashen_thrall", "ashen_invoker", "ashen_priest", "flame_priest",
            "fire_devotee", "fire_zealot", "ember_caller", "ember_shaman",
            "circle_acolyte", "circle_druid", "circle_shaman",
        };

        // Reflected backing field used to rename agents in-battle
        private static FieldInfo _agentNameField;
        private static bool      _agentNameResolved;

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
            _agentBpMethod = null; _agentBpResolved = false;
            _heroBpSetter = null; _heroSpSetter = null; _heroBpField = null; _heroSpField = null; _heroBpResolved = false;
        }

        // ── Body-property key transforms (pure, covered by PureLogicTests) ───
        // All colour positions are empirical; the exact bit layout varies by
        // game version, so callers wrap application in try/catch.

        // Hair: light grey #D3D3D3 — two colour bytes encoded at bits 40–55.
        private const ulong _ashenHairColour = 0x00D3D30000000000UL;
        public static ulong AshenHairKey(ulong keyPart4) =>
            (keyPart4 & ~0x00FFFF0000000000UL) | _ashenHairColour;

        // Eyes: high saturation with a blue hue → cold pale-blue iris.
        public static ulong AshenEyeKey(ulong keyPart5) =>
            (keyPart5 & ~0x00FFFF0000000000UL) | 0x00E0AA0000000000UL;

        // Skin: light grey #D3D3D3 (RGB 211,211,211) encoded in the three colour bytes.
        private const ulong _ashenSkinColour = 0x000000D3D3D30000UL;
        public static ulong AshenSkinKey(ulong keyPart7) =>
            (keyPart7 & ~0x000000FFFFFF0000UL) | _ashenSkinColour;

        // An adult age floor for ashen agents. Some bandit/troop templates resolve
        // to a near-zero ("child") age at spawn; clamping here stops the ashen look
        // from ever producing a child-sized body.
        private const float AshenAdultAgeFloor = 30f;

        public static BodyProperties MakeAshenBodyProperties(BodyProperties bp)
        {
            var sp = bp.StaticProperties;
            var newStatic = new StaticBodyProperties(
                sp.KeyPart1, sp.KeyPart2, sp.KeyPart3, AshenHairKey(sp.KeyPart4),
                AshenEyeKey(sp.KeyPart5), sp.KeyPart6, AshenSkinKey(sp.KeyPart7), sp.KeyPart8);

            var dp = bp.DynamicProperties;
            float adultAge = Math.Max(dp.Age, AshenAdultAgeFloor);
            var newDynamic = new DynamicBodyProperties(adultAge, dp.Weight, dp.Build);

            return new BodyProperties(newDynamic, newStatic);
        }

        // ── Agent body-property write ─────────────────────────────────────────
        // UpdateBodyProperties may have been renamed or its parameter type changed
        // to BodyPropertiesMin in newer Bannerlord builds.  We search by name
        // (ignoring parameter type) so we degrade to "no face change" rather than
        // silently crashing.
        private static MethodInfo   _agentBpMethod;
        private static bool         _agentBpResolved;

        private static void TryUpdateAgentBodyProperties(Agent agent, BodyProperties bp)
        {
            if (!_agentBpResolved)
            {
                _agentBpResolved = true;
                // Prefer the exact BodyProperties overload; fall back to any single-param
                // overload named UpdateBodyProperties.
                _agentBpMethod =
                    typeof(Agent).GetMethod("UpdateBodyProperties",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(BodyProperties) }, null)
                    ?? typeof(Agent)
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == "UpdateBodyProperties"
                                             && m.GetParameters().Length == 1);
            }
            if (_agentBpMethod == null) return;

            var paramType = _agentBpMethod.GetParameters()[0].ParameterType;
            object arg;
            if (paramType == typeof(BodyProperties))
            {
                arg = bp;
            }
            else
            {
                // Newer builds may use BodyPropertiesMin — try to construct it from
                // BodyProperties, or fall back to static-only construction.
                try { arg = Activator.CreateInstance(paramType, bp); }
                catch
                {
                    try { arg = Activator.CreateInstance(paramType, bp.StaticProperties); }
                    catch { return; }
                }
            }
            _agentBpMethod.Invoke(agent, new[] { arg });
        }

        // ── Hero body-property write ──────────────────────────────────────────
        // Hero.BodyProperties has no public setter. We try four strategies in
        // order so the mod degrades gracefully across Bannerlord builds:
        //  1. Non-public BodyProperties property setter (some builds)
        //  2. Public StaticBodyProperties property setter (current build) — the
        //     ashen face lives entirely in the static keys, so this covers it.
        //  3. Backing field "_bodyProperties" (BodyProperties)
        //  4. Backing field "_staticBodyProperties" / auto-property backing field
        private static MethodInfo   _heroBpSetter;
        private static MethodInfo   _heroSpSetter;
        private static FieldInfo    _heroBpField;
        private static FieldInfo    _heroSpField;
        private static bool         _heroBpResolved;

        public static void SetHeroBodyProperties(Hero hero, BodyProperties bp)
        {
            if (!_heroBpResolved)
            {
                _heroBpResolved = true;
                var t = typeof(Hero);
                _heroBpSetter = t.GetProperty("BodyProperties",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?.GetSetMethod(nonPublic: true);
                _heroSpSetter = t.GetProperty("StaticBodyProperties",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?.GetSetMethod(nonPublic: true);
                if (_heroBpSetter == null && _heroSpSetter == null)
                {
                    _heroBpField = t.GetField("_bodyProperties",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (_heroBpField?.FieldType != typeof(BodyProperties))
                        _heroBpField = null;
                    if (_heroBpField == null)
                        _heroSpField = t.GetField("_staticBodyProperties",
                            BindingFlags.NonPublic | BindingFlags.Instance)
                            ?? t.GetField("<StaticBodyProperties>k__BackingField",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }
            if (_heroBpSetter != null)
                _heroBpSetter.Invoke(hero, new object[] { bp });
            else if (_heroSpSetter != null)
                _heroSpSetter.Invoke(hero, new object[] { bp.StaticProperties });
            else if (_heroBpField != null)
                _heroBpField.SetValue(hero, bp);
            else if (_heroSpField != null)
                _heroSpField.SetValue(hero, bp.StaticProperties);
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                TryUpdateAgentBodyProperties(agent, MakeAshenBodyProperties(agent.BodyPropertiesValue));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Cold ash-grey / frost-blue clothing tint. Unlike the face-key transform
            // above — whose exact colour-bit layout drifts between game builds and may
            // not register — SetClothingColor is a stable API, so this is the reliable
            // visual cue that the agent belongs to the cold.
            try
            {
                agent.SetRandomizeColors(false);
                agent.SetClothingColor1(ClothAshGrey);
                agent.SetClothingColor2(ClothColdBlue);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── In-battle unit rename ─────────────────────────────────────────────

        // Sets the battle nameplate of regular (non-hero, non-named-custom) troops
        // in Ashen armies to "Ashen Warrior". Called from OnAgentBuild.
        public static void TryRenameToAshenWarrior(Agent agent)
        {
            if (agent == null || agent.IsHero || agent.IsMount) return;

            var character = agent.Character as CharacterObject;
            if (character != null && _namedModTroopIds.Contains(character.StringId)) return;

            if (!ShouldRenameAshenAgent(agent)) return;

            if (!_agentNameResolved)
            {
                _agentNameResolved = true;
                var field = typeof(Agent).GetField("_name",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(TextObject))
                    _agentNameField = field;
            }
            if (_agentNameField == null) return;
            try { _agentNameField.SetValue(agent, new TextObject("Ashen Warrior")); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static bool ShouldRenameAshenAgent(Agent agent)
        {
            try
            {
                var party = agent.Origin?.BattleCombatant as PartyBase;
                if (party == null) return false;

                var mobile = party.MobileParty;
                if (mobile != null && MageKnowledge.IsAshen && mobile == MobileParty.MainParty)
                    return true;

                if (party.MapFaction?.StringId == AshenKingdomId) return true;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return false;
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
