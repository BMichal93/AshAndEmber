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
// Backward compatibility: this is purely additive. Saves made before a quest was
// ever active contain none of these objects, so they load unchanged; saves WITH a
// live quest could not be written at all before, so there is nothing to conflict
// with. Never renumber an existing id once shipped — it would orphan saved objects.
// =============================================================================

using TaleWorlds.SaveSystem;

namespace AshAndEmber
{
    public sealed class AshAndEmberSaveDefiner : SaveableTypeDefiner
    {
        // Unique mod-wide base id. Chosen to be well clear of the base game and of
        // other mods' ranges. Do not change once released.
        public AshAndEmberSaveDefiner() : base(9_271_400) { }

        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(DragonQuestLog),      1);
            AddClassDefinition(typeof(AshenQuestLog),       2);
            AddClassDefinition(typeof(BurningLabQALog),     3);
            AddClassDefinition(typeof(BurningLabQBLog),     4);
            AddClassDefinition(typeof(BurningLabQCLog),     5);
            AddClassDefinition(typeof(KeybindReferenceLog), 6);
        }
    }
}
