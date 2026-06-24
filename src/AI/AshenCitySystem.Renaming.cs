// =============================================================================
// ASH AND EMBER — AshenCitySystem.Renaming.cs
// Renames Ashen settlements (towns, castles, villages) on every session launch.
// Partial of AshenCitySystem (shared static state lives in AshenCitySystem.cs).
//
// Settlement names revert to vanilla on each game load (they come from the
// game's data XML, not the save). This runs on the first daily tick after
// clans are established so both new games and save reloads are covered.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class AshenCitySystem
    {
        // Vanilla name → Ashen display name for towns.
        // Keys are matched with OrdinalIgnoreCase IndexOf so partial matches
        // (e.g. "Tyal" finds "Tyal Castle" if one existed) are safe.
        private static readonly Dictionary<string, string> _townRenames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Tyal",       "The Heart of Winter"      },
            { "Sibir",      "The Pale Throne"           },
            { "Baltakhand", "The Smouldering Bastion"   },
            { "Amprela",    "The Ashen Crown"           },
            { "Varnovapol", "The Sundered Hold"         },
            { "Ostican",    "The Grey Harbour"          },
            { "Argoron",    "The Cinder Shore"          },
            { "Omor",       "The Dying Ember"           },
        };

        // Vanilla name → Ashen display name for castles.
        private static readonly Dictionary<string, string> _castleRenames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Urikskala",  "Ashen Spire"              },
            { "Kaysar",     "The Winter Keep"           },
            { "Dinar",      "The Cold Watch"            },
            { "Vladiv",     "The Frozen Rampart"        },
            { "Tepes",      "The Iron Pyre"             },
            { "Epinosa",    "Scorchstone Keep"          },
            { "Takor",      "The Char Bastion"          },
            { "Khimli",     "The Frostfang"             },
            { "Lochana",    "The Ashen Gate"            },
            { "Syratos",    "The Cinder Tower"          },
            { "Atrion",     "The Grey Spire"            },
            { "Ov Castle",  "The Obsidian Keep"         },
            { "Mazhadan",   "The Hollow Fortress"       },
            { "Mecalovea",  "The Smouldering Tower"     },
            { "Rhesos",     "The Bone Rampart"          },
        };

        // Names drawn in order for Ashen villages. Sorted by StringId (deterministic)
        // so the same village always gets the same name across reloads.
        // If the pool is exhausted, "The Wastes N" is used as fallback.
        private static readonly string[] _villageNames =
        {
            "Ash Hollows",      "Cinder Fen",       "Scorched Dale",    "Wasted Mire",
            "The Grey Downs",   "Ember Crossing",   "Soot Haven",       "The Black Fens",
            "Cinder Reach",     "Ashen Furrow",     "The Pale Dale",    "Scorched Crossing",
            "The Wasted Heath", "Ember Hollow",     "Ash Flats",        "The Blighted Fen",
            "Grey Hearth",      "The Char Downs",   "Soot Crossing",    "Wasted Hollow",
            "The Cinder Moor",  "Pale Furrow",      "Scorched Hollow",  "Ash Mire",
            "The Grey Flats",   "Ember Downs",      "Soot Fen",         "The Black Moors",
            "Cinder Dale",      "Wasted Crossing",  "The Pale Fens",    "Scorched Furrow",
            "Ash Downs",        "The Cinder Heath", "Ember Mire",       "Grey Hollow",
            "The Wasted Dale",  "Soot Moors",       "Pale Hollow",      "Black Flats",
        };

        // Reflection handle for MBObjectBase._name — cached once, used per-call.
        // Works for Kingdom and CultureObject (they read the base field).
        private static readonly FieldInfo _nameField =
            typeof(MBObjectBase).GetField("_name",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Settlement SHADOWS MBObjectBase._name with its own _name field, and
        // Settlement.Name reads the shadowing field. Writing the base field has no
        // effect — that was the bug that left towns named Tyal, Sibir, … in game.
        private static readonly FieldInfo _settlementNameField =
            typeof(Settlement).GetField("_name",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // ── Entry point ───────────────────────────────────────────────────────
        // Called from DailyTick() on the first tick after clans are ready.
        public static void RenameAshenSettlements()
        {
            RenameByTable(_townRenames,   isTown:   true);
            RenameByTable(_castleRenames, isTown:   false);
            RenameAshenVillages();
        }

        // ── Towns / castles ───────────────────────────────────────────────────
        private static void RenameByTable(Dictionary<string, string> table, bool isTown)
        {
            foreach (var kvp in table)
            {
                try
                {
                    // Match by vanilla name OR by Ashen name (handles reloads where
                    // the name is already changed if the engine does persist names).
                    var s = Settlement.All.FirstOrDefault(x =>
                        (isTown ? x.IsTown : x.IsCastle) &&
                        (x.Name.ToString().IndexOf(kvp.Key,   StringComparison.OrdinalIgnoreCase) >= 0 ||
                         x.Name.ToString().Equals(kvp.Value,  StringComparison.OrdinalIgnoreCase)));
                    if (s != null)
                        SetSettlementName(s, kvp.Value);
                }
                catch { }
            }
        }

        // ── Villages ──────────────────────────────────────────────────────────
        private static void RenameAshenVillages()
        {
            // Villages whose bound town/castle is in the Ashen settlement map.
            // Sorted by StringId for deterministic assignment across reloads.
            var villages = Settlement.All
                .Where(s => s.IsVillage
                         && s.Village?.Bound != null
                         && _settlementClanMap.ContainsKey(s.Village.Bound.StringId))
                .OrderBy(s => s.StringId)
                .ToList();

            for (int i = 0; i < villages.Count; i++)
            {
                try
                {
                    string name = i < _villageNames.Length
                        ? _villageNames[i]
                        : $"The Wastes {i - _villageNames.Length + 1}";
                    SetSettlementName(villages[i], name);
                }
                catch { }
            }
        }

        // ── Name setter ───────────────────────────────────────────────────────
        private static void SetSettlementName(Settlement settlement, string name)
        {
            if (settlement == null) return;
            // Use the Settlement-declared _name field (see field comment above).
            (_settlementNameField ?? _nameField)?.SetValue(settlement, new TextObject(name));
        }

        // ── Holy Temple kingdom rename ─────────────────────────────────────────
        // Vlandia IS The Holy Temple. Kingdoms revert to their XML names on every
        // session load, so this runs on the first daily tick each session.
        public static void RenameHolyTempleKingdom()
        {
            try
            {
                var vlandia = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == "vlandia" && !k.IsEliminated);
                if (vlandia == null) return;

                // MBObjectBase._name backs the Name property for all game objects.
                _nameField?.SetValue(vlandia, new TextObject("The Holy Temple"));

                // Kingdom-specific informal name and ruler title fields.
                // Try both the explicit-field and auto-property backing-field conventions.
                SetKingdomField(vlandia,
                    new[] { "_informalName", "<InformalName>k__BackingField" },
                    new TextObject("Temple"));
                SetKingdomField(vlandia,
                    new[] { "_rulerTitle", "<RulerTitle>k__BackingField" },
                    new TextObject("High Templar"));

                // The Vlandian culture IS the Templar order — rename the culture so a
                // character's background reads "Templar" rather than "Vlandian"
                // everywhere it is shown (character sheet, encyclopedia, troop culture).
                RenameTempleCulture();
            }
            catch { }
        }

        // ── Culture-only rename (no Campaign required) ──────────────────────────
        // The Vlandian culture IS the Templar order. This is split out from the
        // kingdom rename above because the culture must read "Templar" on the
        // character-creation screen, which runs before any campaign daily tick
        // (so Kingdom.All is not yet usable). Idempotent and guarded.
        public static void RenameTempleCulture()
        {
            try
            {
                var vlandiaCulture = MBObjectManager.Instance?.GetObject<CultureObject>("vlandia");
                if (vlandiaCulture != null)
                    _nameField?.SetValue(vlandiaCulture, new TextObject("Templar"));
            }
            catch { }
        }

        // ── Character-creation culture card text override ───────────────────────
        // The culture-selection card does NOT read CultureObject.Name. It builds its
        // title from the game text "str_culture_rich_name" and its blurb from
        // "str_culture_description", both keyed by the culture's StringId ("vlandia").
        // So renaming the culture object is not enough — those two game-text
        // variations must be rewritten as well. Game texts are session data (never
        // serialised), so this is safe and reverts cleanly if the mod is removed.
        // Returns true once the game-text manager is available (so callers can stop
        // retrying). Idempotent: skips work once the variation already matches.
        public static bool ApplyTempleCultureTexts()
        {
            try
            {
                RenameTempleCulture();

                var mgrField = typeof(GameTexts).GetField("_gameTextManager",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var mgr = mgrField?.GetValue(null) as GameTextManager;
                if (mgr == null) return false;

                SetCultureVariation(mgr, "str_culture_rich_name", "vlandia", "The Holy Temple");
                SetCultureVariation(mgr, "str_culture_description", "vlandia",
                    "Once they were lords of the western marches, bound to no altar and no god but conquest. " +
                    "When the grey march first came down from the north, the Empire turned east and left them " +
                    "to face it alone. They held. In the silence that followed, they made a covenant with the " +
                    "fire inside them — not as weapon, but as vow. The Templars are what that vow became. " +
                    "They bind throne to altar. They count the cost. They do not flinch at what the Light requires of them.");
                return true;
            }
            catch { return false; }
        }

        private static void SetCultureVariation(GameTextManager mgr, string textId, string variation, string value)
        {
            try
            {
                var existing = GameTexts.FindText(textId, variation);
                if (existing != null && existing.ToString() == value) return;  // already applied
                GameText gt = mgr.GetGameText(textId);
                if (gt == null) return;
                gt.SetVariationWithId(variation, new TextObject(value), null);
            }
            catch { }
        }

        private static void SetKingdomField(Kingdom kingdom, string[] candidates, TextObject value)
        {
            foreach (var fieldName in candidates)
            {
                try
                {
                    var f = typeof(Kingdom).GetField(fieldName,
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) { f.SetValue(kingdom, value); return; }
                }
                catch { }
            }
        }

        // ── Vlandian troop rename ─────────────────────────────────────────────
        // Renames all vanilla Vlandian troops so they read "Templar X" rather
        // than "Vlandian X". BasicCharacterObject has its own _name field that
        // shadows MBObjectBase._name; we prefer that one and fall back to the
        // base field. Idempotent: already-renamed names are left unchanged.
        // Called once per session alongside the kingdom rename.
        //
        // Specific overrides give a handful of units more evocative Templar names;
        // everything else gets a simple "Vlandian" → "Templar" substitution.
        private static readonly FieldInfo _characterNameField =
            typeof(TaleWorlds.Core.BasicCharacterObject).GetField(
                "_name", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly System.Collections.Generic.Dictionary<string, string> _troopNameOverrides =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Vlandian Recruit",       "Templar Initiate"   },
            { "Vlandian Sharpshooter",  "Templar Marksman"   },
            { "Vlandian Banner Knight", "Templar Champion"   },
        };

        public static void RenameVlandianTroops()
        {
            try
            {
                var nameField = _characterNameField ?? _nameField;
                if (nameField == null) return;

                foreach (var ch in MBObjectManager.Instance
                             ?.GetObjectTypeList<CharacterObject>()
                             ?? Enumerable.Empty<CharacterObject>())
                {
                    try
                    {
                        if (ch == null) continue;
                        if (!(ch.StringId?.StartsWith("vlandian_", StringComparison.OrdinalIgnoreCase) ?? false))
                            continue;

                        string current = ch.Name?.ToString() ?? "";
                        if (!current.StartsWith("Vlandian ", StringComparison.OrdinalIgnoreCase)) continue;

                        string newName;
                        if (!_troopNameOverrides.TryGetValue(current, out newName))
                            newName = "Templar " + current.Substring("Vlandian ".Length);

                        nameField.SetValue(ch, new TextObject(newName));
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
