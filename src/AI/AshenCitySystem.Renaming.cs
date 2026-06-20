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
        private static readonly FieldInfo _nameField =
            typeof(MBObjectBase).GetField("_name",
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
            _nameField?.SetValue(settlement, new TextObject(name));
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
    }
}
