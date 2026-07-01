# Lifespan migration — implement this now

This is a ready-to-implement spec, not a discussion. Follow it exactly, in
order. When you finish (build + tests pass, changes match the checklist),
do the cleanup step at the very end — do not leave this file or its
`CLAUDE.md` import in place afterward.

## What's changing

Today, `AgingSystem` pays every spellcasting cost by pushing `Hero.BirthDay`
backward (`AgeHero`) — the caster gets literally older. Death/Ashen
transformation triggers at a hardcoded `Age >= 100`.

The new model: casting no longer touches `Age` at all. Instead every hero
(player, mage lords, companions) carries a **MaxLifespan** — a moving
ceiling that starts at 100 and *shrinks* with every cast. `Hero.Age` still
climbs at the normal campaign pace for everyone, mage or not. The
Ashen/death check becomes `Age >= MaxLifespan` instead of `Age >= 100`.
"Rejuvenation" effects (Reap, Ashen Ruins rewards, blood tempering, etc.)
now restore MaxLifespan instead of literally de-aging the body.

This is a strict reinterpretation of the existing death-trigger system, not
new plumbing: `AgeHero`/`RejuvenateHero` keep their exact signatures and
~30 existing call sites across the codebase are untouched — only what
happens *inside* those two functions changes.

## Files to change

### 1. `src/AI/ColourLordRegistry.cs` — NPC/companion storage

Add a new dictionary next to `_campaignCooldowns` (around line 38):

```csharp
// Per-hero max lifespan (years). Missing entry = DefaultMaxLifespan.
// Shrinks with spellcasting cost, grows with rejuvenation rites.
private static readonly Dictionary<string, float> _maxLifespan = new Dictionary<string, float>();
private const float DefaultMaxLifespan = 100f;
private const float MaxLifespanCeiling = 150f; // even the fire's mercy has a limit
```

Add accessors near `IsCompanionMage` (around line 171):

```csharp
public static float GetMaxLifespan(Hero hero) =>
    hero != null && _maxLifespan.TryGetValue(hero.StringId, out var v) ? v : DefaultMaxLifespan;

// deltaYears negative = spellcasting cost, positive = rejuvenation.
public static void AdjustMaxLifespan(Hero hero, float deltaYears)
{
    if (hero == null) return;
    _maxLifespan[hero.StringId] = Math.Min(MaxLifespanCeiling, GetMaxLifespan(hero) + deltaYears);
}
```

In `ResetForNewGame()` (~line 174), add:
```csharp
_maxLifespan.Clear();
```

In `OnLordDied()` (~line 403), add:
```csharp
_maxLifespan.Remove(hero.StringId);
```

In `CheckAgeLimit()` (~line 302-325), change the query condition from:
```csharp
&& h.Age >= 100f)
```
to:
```csharp
&& h.Age >= GetMaxLifespan(h))
```

In `Save()` (~line 414-465), add parallel-list persistence, following the
exact pattern already used for `_campaignCooldowns` in the same method:
```csharp
var lifespanKeys = _maxLifespan.Keys.ToList();
var lifespanVals = _maxLifespan.Values.ToList();
```
```csharp
store.SyncData("LDM_LifespanKeys", ref lifespanKeys);
store.SyncData("LDM_LifespanVals", ref lifespanVals);
```
```csharp
_maxLifespan.Clear();
if (lifespanKeys != null && lifespanVals != null)
    for (int i = 0; i < Math.Min(lifespanKeys.Count, lifespanVals.Count); i++)
        _maxLifespan[lifespanKeys[i]] = lifespanVals[i];
```
(Place these three blocks alongside the equivalent `cdKeys`/`cdVals`
declare / SyncData / rebuild blocks already in that method — same
structure, new key names.)

### 2. `src/AgingSystem.cs` — player storage + core rewrite

Update the file header comment (lines 1-6) — it currently says "On
reaching age 100, the mage dies." Change to reflect that the threshold
now moves per-hero instead of being fixed.

Add a player field near `_pendingAshenDecision` (~line 22):
```csharp
private static float _maxLifespanYears = 100f;
private const float MaxLifespanCeiling = 150f;
```

Add dispatcher helpers (place above `AgeHero`):
```csharp
public static float GetMaxLifespan(Hero hero) =>
    hero == Hero.MainHero ? _maxLifespanYears : ColourLordRegistry.GetMaxLifespan(hero);

private static void AdjustMaxLifespan(Hero hero, float deltaYears)
{
    if (hero == Hero.MainHero)
        _maxLifespanYears = Math.Min(MaxLifespanCeiling, _maxLifespanYears + deltaYears);
    else
        ColourLordRegistry.AdjustMaxLifespan(hero, deltaYears);
}
```

Replace the body of `AgeHero` (lines ~37-64). Keep every guard clause
exactly as-is (Ashen immunity, Aelisar covenant, null/days checks) — only
replace the `SetBirthDay` line and drop the milestone calls (they move to
`DailyAgeCheck`, see below, since `Age` no longer moves when this fires):

```csharp
public static void AgeHero(Hero hero, int days)
{
    if (hero == null || days <= 0) return;
    // Ashen do not age — the cold preserves what remains
    if (hero == Hero.MainHero && MageKnowledge.IsAshen) return;
    if (hero != Hero.MainHero && ColourLordRegistry.IsAshenLord(hero)) return;
    // Aelisar's covenant — the Vessel bears no aging cost from fire
    if (hero == Hero.MainHero && DragonQuestSystem.IsEmperorMerged) return;
    try
    {
        if (hero == Hero.MainHero) _ledgerDaysSpent += days;
        AdjustMaxLifespan(hero, -(days / 84f));

        if (hero == Hero.MainHero)
            InformationManager.DisplayMessage(new InformationMessage(
                $"The fire steals from what's ahead — {days} day{(days > 1 ? "s" : "")} shorter. Age: {(int)hero.Age}.",
                new Color(0.7f, 0.5f, 0.3f)));

        CheckAgeLimit(hero);
    }
    catch { }
}
```

Replace the body of `RejuvenateHero` (lines ~122-150) entirely — the old
"never below age 20" clamp and the float-drift snap-back existed only
because the old model touched `BirthDay` directly; neither is needed now:

```csharp
public static void RejuvenateHero(Hero hero, int days)
{
    if (hero == null || days <= 0) return;
    try
    {
        AdjustMaxLifespan(hero, days / 84f);

        if (hero == Hero.MainHero)
        {
            _ledgerDaysReclaimed += days;
            InformationManager.DisplayMessage(new InformationMessage(
                $"The fire gives back — {days} day{(days > 1 ? "s" : "")} of years reclaimed.",
                new Color(0.9f, 0.6f, 0.3f)));
        }
    }
    catch { }
}
```

In `CheckAgeLimit(Hero hero)` (~line 154), change:
```csharp
if (hero.Age < 100f) return;
```
to:
```csharp
if (hero.Age < GetMaxLifespan(hero)) return;
```
Nothing else in this method changes — the Ashen-immunity guards, the
player prompt flow, the 5% NPC-Ashen-instead-of-death roll, and the
`KillCharacterAction.ApplyByOldAge` call all stay exactly as they are.

In `DailyAgeCheck()` (~line 192-203), add the milestone check for the
player (moved out of `AgeHero` — it must now run on the daily clock
because `Age` advances only with real campaign time, not with casting):
```csharp
public static void DailyAgeCheck()
{
    try
    {
        foreach (Hero h in Hero.AllAliveHeroes.Where(h => h.IsAlive && ColourLordRegistry.IsColourLord(h)).ToList())
            CheckAgeLimit(h);
        if (Hero.MainHero != null && MageKnowledge.IsMage)
        {
            CheckAgeLimit(Hero.MainHero);
            try { CheckAgingMilestone(Hero.MainHero); } catch { }
        }
    }
    catch { }
}
```
(Do not add a `FlushPendingMilestone()` call here — `CampaignBehavior.Ticks.cs`
already calls it immediately after `DailyAgeCheck()` each day; adding a
second call here would be redundant.)

In `BuildLedgerText()` (~line 305-347), update the non-Ashen branch to
read the moving cap instead of the hardcoded `100f`:
```csharp
float cap = GetMaxLifespan(h);
int daysLeft  = Math.Max(0, (int)((cap - (float)h.Age) * 84f));
int yearsLeft = daysLeft / 84;
lines.Append($"  Age: {age}   |   Life expectancy remaining: ~{yearsLeft} year{(yearsLeft != 1 ? "s" : "")} ({daysLeft} days)\n");
```

In `Save()` (~line 351-366), add:
```csharp
store.SyncData("AG_MaxLifespan", ref _maxLifespanYears);
```

In `ResetForNewGame()` (~line 368-377), add:
```csharp
_maxLifespanYears = 100f;
```

### 3. `src/Campaign/CampaignBehavior.ReapAging.cs` — NPC battle-cast cost

Replace `AgeHeroDeferred` (~line 256-260):
```csharp
private static void AgeHeroDeferred(Hero hero, int days)
{
    if (hero == null || days <= 0) return;
    try { ColourLordRegistry.AdjustMaxLifespan(hero, -(days / 84f)); } catch { }
}
```
Update the comment immediately above it (~line 252-255) — it currently
explains this is deferred to avoid a `KillCharacterAction` crash mid
`OnMapEventEnded`. That's still true and still the reason for the
deferral (this method still never calls `CheckAgeLimit`/`KillCharacterAction`
directly — `DailyAgeCheck` on the next tick still owns that), just reword
away from "shifts a hero's birth day" since it no longer does that.

### 4. Nothing else needs to change

- `ComputeBattleAgingCost` (pure days-cost math) is untouched — the cost
  number's meaning doesn't change, only what it's subtracted from.
- All ~30 existing call sites of `AgingSystem.AgeHero` / `RejuvenateHero`
  across `Sea/`, `AshenRuins/`, `QuestSystems/`, `Talents/`,
  `Mage/MageKnowledge.Events.cs`, `AI/RivalShadowSystem.cs`, etc. are
  correct as-is — they call the same functions with the same day counts;
  the new internal behavior applies automatically.
- `tests/PureLogicTests.cs` needs no changes — none of the
  `AgingSystem_ComputeBattleAgingCost_*` tests touch the code paths above.

## Verify

```bash
dotnet build src/TheWitheringArt.csproj
dotnet test tests/AshAndEmber.Tests.csproj
```
All existing tests must still pass unmodified. If the test project fails
to *compile*, treat that as a blocking failure per `behaviour.md` — a
green `dotnet build` of the mod alone is not sufficient.

If you have a Bannerlord install available, smoke-test manually:
- Cast a battle spell; confirm the new "steals from what's ahead" message,
  and confirm `Age` in the message did **not** jump — only the grimoire's
  "Life expectancy remaining" line should shrink.
- Open the grimoire (Alt+X) and confirm the ledger reads correctly.
- Trigger a rejuvenation source (e.g. a Reap raid win) and confirm the
  "years reclaimed" message appears and remaining life expectancy grows.
- Load a save created before this migration — no crash expected. Heroes
  with no `LDM_LifespanKeys`/`AG_MaxLifespan` entry default to 100 via
  `GetMaxLifespan`'s fallback, so old saves behave identically to today
  until new casts start shrinking the ceiling.

## Version bump

Per `behaviour.md`, bump all four places, `0.34.0.0` → `0.35.0.0`:
1. `src/TheWitheringArt.csproj` — `Version`, `AssemblyVersion`, `FileVersion`
2. `SubModule.xml` — `<Version value="v0.35.0.0"/>`
3. `dist/AshAndEmber/SubModule.xml` — same
4. `CHANGELOG.md` — promote `## Unreleased` to `## v0.35.0` with an entry
   in the existing lore voice. Draft (edit freely — this is a suggestion,
   not a mandate):

```markdown
## v0.35.0

### The fire no longer makes you old — it makes your time shorter
- **Spellcasting cost is reframed from forward-aging to shrinking life
  expectancy.** Every cast still costs exactly what it always did — the
  same geometric scaling, the same Tempered discount — but instead of
  pushing your birthday backward, the cost now shortens the horizon
  ahead of you. You age at the same pace as everyone else; the fire
  steals from what's coming, not what's already been lived.
- **Rites that once made you younger now buy back that horizon instead.**
  Reap yields, Ashen Ruins rewards, and blood tempering restore life
  expectancy rather than reversing your birth date. The Ledger of Years
  in your grimoire (Alt+X) tracks the same numbers as before, aimed at
  a different account.
- **Mage lords and companions pay the same way** — no new mechanic, same
  registry, same battle-cast pipeline, just a shrinking ceiling instead
  of a rising age.
```

## Cleanup (do this last, after everything above is done and verified)

Delete this file (`lifespan-migration.md`) and remove the
`@lifespan-migration.md` import line — and the section header above it —
from `CLAUDE.md`.
