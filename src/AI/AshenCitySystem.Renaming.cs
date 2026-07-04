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
        // NOTE: Kingdom and BasicCultureObject SHADOW the base Name with their own
        // auto-properties, so writing this base field has NO effect on them — use
        // the dedicated handles below for kingdoms and cultures.
        private static readonly FieldInfo _nameField =
            typeof(MBObjectBase).GetField("_name",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Kingdom declares its own `public TextObject Name { get; }` auto-property
        // whose backing field shadows MBObjectBase._name. Kingdom.Name reads the
        // backing field, so this is the one that must be written.
        private static readonly FieldInfo _kingdomNameField =
            typeof(Kingdom).GetField("<Name>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // Same shadowing on BasicCultureObject — CultureObject.Name reads the
        // BasicCultureObject auto-property, never MBObjectBase._name.
        private static readonly FieldInfo _cultureNameField =
            typeof(TaleWorlds.Core.BasicCultureObject).GetField("<Name>k__BackingField",
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

                // Kingdom.Name reads Kingdom's own backing field, not MBObjectBase._name.
                (_kingdomNameField ?? _nameField)?.SetValue(vlandia, new TextObject("The Holy Temple"));

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
                    (_cultureNameField ?? _nameField)?.SetValue(vlandiaCulture, new TextObject("Templar"));
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
                RelabelCulturalFeats("vlandia", _templeFeats, ref _templeFeatsRelabeled);
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

        // ── Tribes of the East kingdom rename ──────────────────────────────────
        // The Khuzait Khanate IS the Tribes of the East. Kingdom names revert to
        // their XML values on every session load, so this runs on the first daily
        // tick each session alongside the Holy Temple rename.
        public static void RenameTribesKingdom()
        {
            try
            {
                var khuzait = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == "khuzait" && !k.IsEliminated);
                if (khuzait == null) return;

                (_kingdomNameField ?? _nameField)?.SetValue(khuzait, new TextObject("Tribes of the East"));

                SetKingdomField(khuzait,
                    new[] { "_informalName", "<InformalName>k__BackingField" },
                    new TextObject("Tribes"));
                SetKingdomField(khuzait,
                    new[] { "_rulerTitle", "<RulerTitle>k__BackingField" },
                    new TextObject("God-King"));

                RenameTribalCulture();
            }
            catch { }
        }

        // Renames the khuzait culture object to "Tribal" so character backgrounds
        // and the encyclopedia read correctly. Called from RenameTribesKingdom and
        // from ApplyTribalCultureTexts (which runs before the campaign exists).
        public static void RenameTribalCulture()
        {
            try
            {
                var khuzaitCulture = MBObjectManager.Instance?.GetObject<CultureObject>("khuzait");
                if (khuzaitCulture != null)
                    (_cultureNameField ?? _nameField)?.SetValue(khuzaitCulture, new TextObject("Tribal"));
            }
            catch { }
        }

        // ── Character-creation culture card text override for Tribes ───────────
        // Works identically to ApplyTempleCultureTexts but for the khuzait culture.
        public static bool ApplyTribalCultureTexts()
        {
            try
            {
                RenameTribalCulture();

                var mgrField = typeof(GameTexts).GetField("_gameTextManager",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var mgr = mgrField?.GetValue(null) as GameTextManager;
                if (mgr == null) return false;

                SetCultureVariation(mgr, "str_culture_rich_name", "khuzait", "Tribes of the East");
                SetCultureVariation(mgr, "str_culture_description", "khuzait",
                    "They came from the eastern steppe — a hundred warring clans who forgot how to stop fighting " +
                    "until the God-King put his hand on the sky and turned three chieftains to ash. " +
                    "The rest knelt. Now the Tribes ride as one, not because they love their king, " +
                    "but because they love war, and he alone has shown them how to win it. " +
                    "He wields fire the way other men wield iron. He does not negotiate. " +
                    "He takes wives from every city his horsemen put to tribute. " +
                    "He is watching the Empire bleed itself empty, and he is patient. " +
                    "The Tribes do not seek peace. They seek the next horizon.");
                RelabelCulturalFeats("khuzait", _tribalFeats, ref _tribalFeatsRelabeled);
                return true;
            }
            catch { return false; }
        }

        // ── Cultural feats (character-creation card) ───────────────────────────
        // The culture card's feats panel reads each FeatObject.Description directly
        // (via Culture.GetCulturalFeats), so the only reliable way to show OUR feats
        // there is to relabel the culture's own feats — not to rewrite the view-model,
        // which rebuilds itself from these objects. Vlandia and Khuzait each have
        // exactly two positive feats and one negative, matching our sets, so we map
        // by sign. Effect amounts are left intact; only the displayed text changes.
        // Each entry is { positive, positive, negative } in display order.
        private static readonly string[] _templeFeats =
        {
            "Dawn's Grace — Should your Grace run dry, each dawn the Light restores a measure of it. (+1 Grace at dawn, if empty)",
            "Oath of the Vigil — Your sworn discipline steadies those who follow. (+4 party morale per day)",
            "The Order's Price — Dark gifts demand twice their cost, and the living ember answers your hand a breath slower. (Dark Gift ×2 cost; Nature channelling +1s)",
        };
        private static readonly string[] _tribalFeats =
        {
            "War Fever — The Tribes ride to war as if born to it; your clan's parties never lose heart. (party morale floor +15)",
            "Spoils of the Raid — A village put to the torch yields more than the usual plunder. (+50–150 gold per raid)",
            "No Quarter — The God-King's word burns through any treaty; your wars do not end in peace.",
        };
        // Duneborn: the caravan bonus is REPLACED (its effect zeroed via
        // zeroPositiveIndex 0) by the Blood Tithe altar discount, which lives in
        // DunebornCulture.AltarCost. The desert feat and the wage penalty keep
        // their vanilla effects under new names.
        private static readonly string[] _dunebornFeats =
        {
            "Blood Tithe — the thing beneath the dunes takes a fifth less of every offering. (Dark Altar sacrifices −20%)",
            "Children of the Sand — the deep desert neither slows nor wearies your kin. (No speed penalty on desert)",
            "Hungry Knives — hired blades smell the old bargain on you, and charge for it. (Daily troop wages +5%)",
        };
        private static bool _templeFeatsRelabeled;
        private static bool _tribalFeatsRelabeled;
        private static bool _dunebornFeatsRelabeled;

        // Relabels a culture's feat descriptions: positives in order, then the negative.
        // `zeroPositiveIndex` additionally REMOVES the effect of the n-th positive
        // feat (bonus set to 0) — used when a relabel replaces a vanilla bonus with
        // a mod mechanic rather than merely renaming it.
        private static void RelabelCulturalFeats(string cultureId, string[] feats, ref bool done,
            int zeroPositiveIndex = -1)
        {
            if (done) return;
            try
            {
                var culture = MBObjectManager.Instance?.GetObject<CultureObject>(cultureId);
                if (culture == null) return;

                var field = typeof(CultureObject).GetField("_cultureFeats",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(field?.GetValue(culture) is System.Collections.IEnumerable list)) return;

                // Split our texts into the positive set and the single negative.
                var positives = feats.Take(feats.Length - 1).ToArray();
                string negative = feats[feats.Length - 1];

                int posIdx = 0;
                bool any = false;
                foreach (var f in list)
                {
                    if (f == null) continue;
                    var ft = f.GetType();
                    bool isPositive = (bool)(ft.GetProperty("IsPositive")?.GetValue(f) ?? true);
                    float bonus     = (float)(ft.GetProperty("EffectBonus")?.GetValue(f) ?? 0f);
                    if (isPositive && posIdx == zeroPositiveIndex) bonus = 0f;
                    object incType  = ft.GetProperty("IncrementType")?.GetValue(f);
                    string name     = (ft.GetProperty("Name")?.GetValue(f) as TextObject)?.ToString() ?? "";
                    string desc     = isPositive
                        ? (posIdx < positives.Length ? positives[posIdx++] : positives[positives.Length - 1])
                        : negative;
                    // FeatObject.Initialize(name, description, effectBonus, isPositive, incrementType)
                    ft.GetMethod("Initialize")?.Invoke(f, new[] { name, desc, (object)bonus, isPositive, incType });
                    any = true;
                }
                if (any) done = true;
            }
            catch { }
        }

        // ── Sturgia → the Northmen (culture rename) ───────────────────────────
        // Renames the Sturgian culture object so the character sheet, encyclopedia,
        // troop culture and creation card read "Northmen" — the same treatment as
        // Vlandia → Templar and Khuzait → Tribes. Sturgia stays mechanically vanilla;
        // only the label and the culture blurb change (no feat relabelling).
        public static void RenameNorthmenCulture()
        {
            try
            {
                var sturgiaCulture = MBObjectManager.Instance?.GetObject<CultureObject>("sturgia");
                if (sturgiaCulture != null)
                    (_cultureNameField ?? _nameField)?.SetValue(sturgiaCulture, new TextObject("Northmen"));
            }
            catch { }
        }

        // ── Character-creation culture card text override for the Northmen ─────
        // Works identically to ApplyTempleCultureTexts / ApplyTribalCultureTexts.
        public static bool ApplyNorthmenCultureTexts()
        {
            try
            {
                RenameNorthmenCulture();

                var mgrField = typeof(GameTexts).GetField("_gameTextManager",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var mgr = mgrField?.GetValue(null) as GameTextManager;
                if (mgr == null) return false;

                SetCultureVariation(mgr, "str_culture_rich_name", "sturgia", "Northmen");
                SetCultureVariation(mgr, "str_culture_description", "sturgia",
                    "The Northmen hold the cold edge of the world, where the forest gives way to ice and the " +
                    "winter nights run longest. They are a hard folk — raiders and shipwrights, sworn to oath, " +
                    "blood-feud, and the long memory of their kings. But what truly shapes them is the war that " +
                    "never ends. Out of the deeper north press the Ashen — the dead-cold lords who neither age " +
                    "nor tire — and it falls to the Northmen to stand in the gap. Every hall keeps its watch-fires " +
                    "burning; every child learns the axe before the plough. They do not expect to break the cold. " +
                    "They expect to hold the line — one more winter, and the next.");
                return true;
            }
            catch { return false; }
        }

        // ── Aserai → the Duneborn (culture rename) ──────────────────────────────
        // Renames the Aserai culture object so the character sheet, encyclopedia,
        // troop culture and creation card read "Duneborn" — the same treatment as
        // Sturgia → Northmen. Aserai stays mechanically vanilla; only the label and
        // the culture blurb change (no feat relabelling, no kingdom rename).
        public static void RenameDunebornCulture()
        {
            try
            {
                var aseraiCulture = MBObjectManager.Instance?.GetObject<CultureObject>("aserai");
                if (aseraiCulture != null)
                    (_cultureNameField ?? _nameField)?.SetValue(aseraiCulture, new TextObject("Duneborn"));
            }
            catch { }
        }

        // ── Character-creation culture card text override for the Duneborn ─────
        // Works identically to ApplyTempleCultureTexts / ApplyNorthmenCultureTexts.
        public static bool ApplyDunebornCultureTexts()
        {
            try
            {
                RenameDunebornCulture();

                var mgrField = typeof(GameTexts).GetField("_gameTextManager",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var mgr = mgrField?.GetValue(null) as GameTextManager;
                if (mgr == null) return false;

                SetCultureVariation(mgr, "str_culture_rich_name", "aserai", "Duneborn");
                SetCultureVariation(mgr, "str_culture_description", "aserai",
                    "The desert does not forgive, and the Duneborn stopped asking it to. Once they kept the same " +
                    "covenant with the inner fire as every tribe beneath the sun — a warmth earned, a debt honoured. " +
                    "Then came the long drought: three generations of cracked wells and a sun that gave nothing back " +
                    "for what it took, and the fire-covenant went dry along with everything else. In the black-glass " +
                    "caverns beneath the dunes, where no torch had ever burned, the first Duneborn found something " +
                    "older than fire and far hungrier — a power that asked no devotion, only blood, and did not care " +
                    "what was done with what it gave. They do not call it a god. They call it patient. Every great " +
                    "house keeps its bargain quiet and its knives quieter, for the desert has always kept its own " +
                    "secrets better than any temple ever kept its.");
                RelabelCulturalFeats("aserai", _dunebornFeats, ref _dunebornFeatsRelabeled,
                    zeroPositiveIndex: 0);   // Blood Tithe replaces the caravan bonus outright
                return true;
            }
            catch { return false; }
        }

        // ── Khuzait troop rename ───────────────────────────────────────────────
        // Renames all vanilla Khuzait troops from "Khuzait X" to "Tribal X", with
        // specific overrides for key units. Idempotent: already-renamed names are
        // left unchanged. Called once per session alongside the kingdom rename.
        private static readonly Dictionary<string, string> _tribalTroopOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Khuzait Nomad",        "Tribesman"             },
            { "Khuzait Khan's Guard", "God-King's Vanguard"   },
            { "Khuzait Raider",       "Tribal Ravager"        },
        };

        public static void RenameKhuzaitTroops()
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
                        if (!(ch.StringId?.StartsWith("khuzait_", StringComparison.OrdinalIgnoreCase) ?? false))
                            continue;

                        string current = ch.Name?.ToString() ?? "";
                        if (!current.StartsWith("Khuzait ", StringComparison.OrdinalIgnoreCase)) continue;

                        string newName;
                        if (!_tribalTroopOverrides.TryGetValue(current, out newName))
                            newName = "Tribal " + current.Substring("Khuzait ".Length);

                        nameField.SetValue(ch, new TextObject(newName));
                    }
                    catch { }
                }
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
                "_basicName", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(TaleWorlds.Core.BasicCharacterObject).GetField(
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

        // ── Sturgia → Northmen troop rename ────────────────────────────────────
        // Renames vanilla Sturgian troops from "Sturgian X" to "Northman X", the
        // same treatment as Vlandia → Templar and Khuzait → Tribal. A few iconic
        // units get more evocative overrides; everything else is a plain prefix
        // swap. Idempotent (already-renamed names are skipped by the "Sturgian "
        // guard). Called once per session alongside the other troop renames.
        private static readonly Dictionary<string, string> _northmenTroopOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Sturgian Recruit",             "Northman Bondsman" },
            { "Sturgian Druzhinnik Champion", "Northman Chosen"   },
        };

        public static void RenameSturgianTroops()
            => RenameCultureTroops("sturgian_", "Sturgian ", "Northman ", _northmenTroopOverrides);

        // ── Aserai → Duneborn troop rename ─────────────────────────────────────
        // Renames vanilla Aserai troops from "Aserai X" to "Duneborn X". "Duneborn"
        // reads cleanly as an attributive, so most units need no override.
        private static readonly Dictionary<string, string> _dunebornTroopOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Aserai Recruit",       "Duneborn Servant"    },
            { "Aserai Vanguard Faris", "Duneborn Bloodsworn" },
        };

        public static void RenameAseraiTroops()
            => RenameCultureTroops("aserai_", "Aserai ", "Duneborn ", _dunebornTroopOverrides);

        // Shared troop-rename loop used by the Sturgia/Aserai renames above.
        // (RenameVlandianTroops / RenameKhuzaitTroops predate this helper and keep
        // their own inline copies.) Matches CharacterObjects by StringId prefix and
        // rewrites the display name via overrides, falling back to a prefix swap.
        private static void RenameCultureTroops(string idPrefix, string namePrefix,
            string newPrefix, Dictionary<string, string> overrides)
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
                        if (!(ch.StringId?.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase) ?? false))
                            continue;

                        string current = ch.Name?.ToString() ?? "";
                        if (!current.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) continue;

                        string newName;
                        if (overrides == null || !overrides.TryGetValue(current, out newName))
                            newName = newPrefix + current.Substring(namePrefix.Length);

                        nameField.SetValue(ch, new TextObject(newName));
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
