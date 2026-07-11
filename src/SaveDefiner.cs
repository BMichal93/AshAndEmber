// =============================================================================
// ASH AND EMBER — SaveDefiner.cs
// Registers the mod's custom saveable types with TaleWorlds' save system.
//
// Custom QuestBase subclasses are added to Campaign.Current.QuestManager when a
// quest starts. When the campaign is saved, the QuestManager serializes every
// quest it holds — and a quest whose concrete type has no registered class
// definition cannot be serialized, which makes the whole save FAIL / corrupt.
// (The failure only ever shows ON SAVE, never at build or while playing — which
//  is exactly how this presented: a quest triggered, then the save broke.)
//
// This definer is auto-discovered by the save driver via reflection across loaded
// module assemblies, so it needs no explicit registration in MainSubModule.
//
// ── ADDING A NEW QUESTLINE ────────────────────────────────────────────────────
// Every concrete QuestBase subclass MUST get a row in ClassDefinitions below, with
// a fresh id. Two guards exist so a forgotten row cannot ship as a broken save:
//   • SelfCheck() (called from MainSubModule.OnGameStart) reflects over the module
//     assembly and writes any unregistered quest type to errors.log at boot.
//   • PureLogicTests.SaveDefiner_EveryQuestBaseSubclass_IsRegistered scans the
//     source and fails the build's test run.
// TaleWorlds' TypeDefinition.CollectFields walks the whole inheritance chain, so an
// abstract intermediate base (EmberConclaveMissionLogBase) needs no row of its own —
// only instantiable types do.
//
// Backward compatibility: this is purely additive. Saves made before a quest was
// ever active contain none of these objects, so they load unchanged; saves WITH a
// live quest could not be written at all before, so there is nothing to conflict
// with. Never renumber an existing id once shipped — it would orphan saved objects.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace AshAndEmber
{
    public sealed class AshAndEmberSaveDefiner : SaveableTypeDefiner
    {
        // Unique mod-wide base id. Chosen to be well clear of the base game and of
        // other mods' ranges. Do not change once released.
        public AshAndEmberSaveDefiner() : base(9_271_400) { }

        // Single source of truth: DefineClassTypes registers these, SelfCheck audits
        // against these. Keeping one table means the two can never drift apart.
        // Ids are permanent — append, never renumber or reuse.
        internal static readonly IReadOnlyList<KeyValuePair<Type, int>> ClassDefinitions =
            new List<KeyValuePair<Type, int>>
            {
                new KeyValuePair<Type, int>(typeof(DragonQuestLog),      1),
                new KeyValuePair<Type, int>(typeof(AshenQuestLog),       2),
                new KeyValuePair<Type, int>(typeof(BurningLabQALog),     3),
                new KeyValuePair<Type, int>(typeof(BurningLabQBLog),     4),
                new KeyValuePair<Type, int>(typeof(BurningLabQCLog),     5),
                new KeyValuePair<Type, int>(typeof(KeybindReferenceLog), 6),
                new KeyValuePair<Type, int>(typeof(EternalColdQuestLog), 7),

                // Ember Conclave quest logs. Each is a live QuestBase added to the
                // QuestManager when the Conclave fires; without these definitions the
                // save could not be written once the quest started.
                new KeyValuePair<Type, int>(typeof(EmberConclaveMainLog),      8),
                new KeyValuePair<Type, int>(typeof(EmberConclaveEliminateLog), 9),
                new KeyValuePair<Type, int>(typeof(EmberConclaveVisitLog),     10),
                new KeyValuePair<Type, int>(typeof(EmberConclaveRuinLog),      11),
                new KeyValuePair<Type, int>(typeof(EmberConclaveProtectLog),   12),

                // The Great Awakening — Duneborn's bid to drag something from
                // beyond the Sands into Calradia.
                new KeyValuePair<Type, int>(typeof(GreatAwakeningQuestLog),    13),

                // The Bonefire Circle — the Northmen seers' standing stones at Varcheg.
                new KeyValuePair<Type, int>(typeof(NorthmenStonesQuestLog),    14),
            };

        protected override void DefineClassTypes()
        {
            foreach (var def in ClassDefinitions)
            {
                try { AddClassDefinition(def.Key, def.Value); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // Boot-time audit. A quest type that reaches the QuestManager without a class
        // definition does not fail at build or while playing — it fails the moment the
        // player hits Save, and only then. This turns that silent trap into a log line
        // at startup, before any quest has had a chance to trigger.
        internal static void SelfCheck()
        {
            try
            {
                Type[] types;
                try { types = Assembly.GetExecutingAssembly().GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t != null).ToArray(); }

                var registered = new HashSet<Type>(ClassDefinitions.Select(d => d.Key));

                foreach (Type t in types)
                {
                    if (t == null || t.IsAbstract || !typeof(QuestBase).IsAssignableFrom(t)) continue;
                    if (registered.Contains(t)) continue;

                    ModLog.Error(new InvalidOperationException(
                        $"Quest type '{t.FullName}' has no AddClassDefinition row in AshAndEmberSaveDefiner. " +
                        "The campaign CANNOT be saved once this quest starts. Add it to " +
                        "AshAndEmberSaveDefiner.ClassDefinitions with a fresh, unused id."));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
