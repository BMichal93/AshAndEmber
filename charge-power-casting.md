# Charge-power casting — implement this now

This is a ready-to-implement spec, not a discussion. Follow it exactly, in
order. When you finish (build + tests pass, changes match the checklist),
do the cleanup step at the very end — do not leave this file or its
`CLAUDE.md` import in place afterward.

## Scope

This applies to the **unified elemental magic battle cast** — `MagicElement`
(Fire/Wind/Earth/Water/Spirit), drawn with Left Alt / LB and released with
Attack/Block. That is the *only* live continuous-charge combat mechanic in
the codebase today (`ElementMagicInput.Tick` is the sole handler ticked
during `OnMissionTick` — the older WADS-tap Blast/Missile/Barrier/Burst path
in `MagicInputHandler.cs` is no longer reachable in battle, and the direct
hold-Ctrl Nature charge gesture in `NatureInputHandler.cs` is likewise not
ticked in missions). Do **not** touch Miracles (discrete six-mark sequence)
or Crystals (fixed 2 s charge, no variable power) — out of scope.

## What's changing

Today (`ElementMagicMath.cs` / `ElementMagicInput.cs`):
- You must draw for **at least 3 s** before Attack/Block will do anything —
  casting below that is blocked outright ("The element is not yet
  gathered…").
- Drawing longer (up to 7 s) makes the cast **cheaper** in aging days
  (`DrawDiscountPerSec`, tripled for the Nature attunement).
- There is no cap on how long you can hold a charge, and no dispersal —
  you can stand there charged indefinitely.

New model:
- **No minimum draw.** Attack/Block fires immediately at 0 s of charge —
  weakly, but it always lands.
- **Draw length scales power, not cost.** The longer you charge (up to a
  10 s cap), the more powerful the cast. Aging cost is flat regardless of
  draw time — charging longer never makes a cast cheaper, only stronger.
- **Charge disperses at 10 s.** Hold past the cap without releasing and the
  gathered charge is lost unspent; you start drawing from zero again.

## Files to change

### 1. `src/Magic/ElementMagicMath.cs` — pure math (testable)

Update the file header comment (lines ~7-15) to describe the new model
instead of the old "draw 3 s minimum, longer draw = cheaper" one.

Replace the `── Draw / charge ──` and `── Aging cost (days) ──` blocks
(lines ~29-49) with:

```csharp
// ── Draw / charge ───────────────────────────────────────────────────────
// No minimum: casting is instant, but weak. Charge builds toward full
// power for up to MaxDrawSeconds, then disperses if not spent.
public const float MaxDrawSeconds  = 10f;   // charge cap and dispersal timer

// An instant (0 s) cast still lands, at this fraction of full power.
public const float MinPowerFraction       = 0.35f;
// The Nature attunement rewards patience more richly than other casters:
// a higher power floor on instant casts, and a small overcharge ceiling
// on a full draw — in place of the old cost discount.
public const float NatureMinPowerFraction = 0.45f;
public const float NatureMaxPowerFraction = 1.15f;

// ── Aging cost (days) — flat, independent of how long you charged ───────
public const int AttackBaseDays = 4;
public const int WallBaseDays   = 6;
public const int MinCastDays    = 1;     // a cast is never free

public static int CastAgingDays(CastForm form)
{
    int baseDays = form == CastForm.Wall ? WallBaseDays : AttackBaseDays;
    return Math.Max(MinCastDays, baseDays);
}

// 0 s draw → the min fraction; MaxDrawSeconds draw → full power (or the
// Nature overcharge ceiling). hasNature is the same flag that used to
// triple the draw-time cost discount — it now widens the power curve
// instead, since cost no longer moves with draw time.
public static float PowerFraction(float drawSeconds, bool hasNature = false)
{
    float clamped = Math.Max(0f, Math.Min(drawSeconds, MaxDrawSeconds));
    float minFrac = hasNature ? NatureMinPowerFraction : MinPowerFraction;
    float maxFrac = hasNature ? NatureMaxPowerFraction : 1f;
    return minFrac + (maxFrac - minFrac) * (clamped / MaxDrawSeconds);
}
```

Remove `MinDrawSeconds`, `FullDrawSeconds`, `DrawDiscountPerSec`, and
`NatureDrawDiscountPerSec` entirely — nothing outside this file and
`ElementMagicInput.cs` references them (verify with a repo-wide search
before deleting; if something else does reference them, update that call
site to the new API instead of leaving a dangling reference).

Leave the `── Blood ──` section (`BloodDaysPerTier` / `BloodRejuvenationDays`)
untouched — it is the lord-execution rejuvenation formula and is unrelated
to draw timing.

### 2. `src/Magic/ElementMagicInput.cs` — input/timing

Update the file header comment (lines ~6-19): replace "DRAW for at least
~3 s… the longer you draw (up to ~7 s) the LESS it ages you" with wording
that matches the new model (draw is optional, up to 10 s, scales power not
cost, disperses if held too long).

Remove the `_readyAnnounced` field and every place that sets or reads it
(`ResetInputState`, the refocus branch, the draw block, the interrupt
branch, `TryCast`, the defocus branch) — there is no "not ready yet" state
anymore, so nothing needs to track whether a readiness message already
fired.

Replace the draw block inside `Tick()` (the `if (reason == null) { ... }
else { ... }` around lines ~84-100):

```csharp
string reason = ChannelBlockReason();
if (reason == null)
{
    _drawTime += dt;
    if (_drawTime >= ElementMagicMath.MaxDrawSeconds)
    {
        _drawTime = 0f;
        Msg($"The {MageElementKnowledge.LoadedName()} slips away, unspent.");
    }
}
else
{
    _drawTime = 0f;
    _reminder -= dt;
    if (_reminder <= 0f) { Msg(reason); _reminder = ReminderInterval; }
}
```

Replace `TryCast` (lines ~144-166):

```csharp
private static void TryCast(CastForm form)
{
    var caster = Agent.Main;
    if (caster == null || !caster.IsActive()) return;

    var el      = MageElementKnowledge.Loaded;
    float power = ElementMagicMath.PowerFraction(_drawTime, MageElementKnowledge.HasNature);
    try
    {
        if (form == CastForm.Attack) ElementSpellEffects.CastAttack(el, caster, power);
        else                         ElementSpellEffects.CastWall(el, caster, power);
    }
    catch { }

    int days = ElementMagicMath.CastAgingDays(form);
    ApplyCastCost(days);
    _drawTime = 0f;        // the charge is spent — draw again
}
```

### 3. `src/Magic/ElementSpellEffects.cs` — thread power into magnitude

Add a `float power = 1f` parameter to `CastAttack` and `CastWall` (default
keeps any other/future caller unaffected):

```csharp
public static void CastAttack(MagicElement el, Agent caster, float power = 1f)
{
    if (caster == null || !caster.IsActive()) return;
    switch (el)
    {
        case MagicElement.Fire:   FireCone(caster, power);  break;
        case MagicElement.Wind:   NatureEffects.ExecuteNpc(NaturePower.Gale,     caster, caster.Team, power); break;
        case MagicElement.Earth:  NatureEffects.ExecuteNpc(NaturePower.Entangle, caster, caster.Team, power); break;
        case MagicElement.Water:  NatureEffects.ExecuteNpc(NaturePower.Torrent,  caster, caster.Team, power); break;
        case MagicElement.Spirit: SpiritPanic(caster, power); break;
    }
    CastFlash(el, caster);
    try { SpellEffects.RecordMagicCast(caster.Position); } catch { }
}

public static void CastWall(MagicElement el, Agent caster, float power = 1f)
{
    if (caster == null || !caster.IsActive()) return;
    switch (el)
    {
        case MagicElement.Fire:   FireWall(caster, power);  break;
        case MagicElement.Wind:   NatureEffects.ExecuteNpc(NaturePower.Windwall,  caster, caster.Team, power); break;
        case MagicElement.Earth:  NatureEffects.ExecuteNpc(NaturePower.Thornwall, caster, caster.Team, power); break;
        case MagicElement.Water:  NatureEffects.ExecuteNpc(NaturePower.Mistwall,  caster, caster.Team, power); break;
        case MagicElement.Spirit: SpiritWall(caster, power); break;
    }
    CastFlash(el, caster);
    try { SpellEffects.RecordMagicCast(caster.Position); } catch { }
}
```

Add a `float power` parameter to `FireCone` and `FireWall`; multiply their
damage constant by it:
```csharp
private static void FireCone(Agent caster, float power)
{
    // ... unchanged setup ...
    try { SpellEffects.DamageAgent(a, FireConeDamage * power, ColorSchool.Red, caster); } catch { }
    // ... unchanged rest ...
}

private static void FireWall(Agent caster, float power)
{
    // ... unchanged setup ...
    try { SpellEffects.DamageAgent(a, FireWallDamage * power, ColorSchool.Red, caster); } catch { }
    // ... unchanged rest ...
}
```

Add a `float power` parameter to `SpiritPanic` and `SpiritWall`:
```csharp
private static void SpiritPanic(Agent caster, float power)
{
    // ... unchanged setup ...
    // scale the fear duration, not the targeting radius — a weak cast
    // should feel short-lived, not like it missed people it should have hit
    try { NatureEffects.ApplySpeedToken(a, SpiritFearSlow, SpiritFearSec * power); } catch { }
    // ... unchanged rest ...
}

private static void SpiritWall(Agent caster, float power)
{
    // ... unchanged setup ...
    try { MobileParty.MainParty.RecentEventsMorale += SpiritMorale * power; } catch { }
    // ...
    try { SpellEffects.HealAgent(a, SafeLimit(a) * SpiritHealFrac * power); } catch { }
    // ... unchanged rest ...
}
```

Update the two call sites inside `FireCone`/`FireWall`/`SpiritPanic`/
`SpiritWall` bodies accordingly — everything else in those methods
(targeting radius, cone angle, visuals, glow) is unchanged.

### 4. `src/Nature/NatureEffects.cs` — magnitude passthrough for Wind/Earth/Water

`ElementSpellEffects` routes Wind/Earth/Water through
`NatureEffects.ExecuteNpc`, which is also called elsewhere (`NatureSeerAI.cs`)
with no magnitude concept — so add an optional parameter that defaults to
1f, preserving every existing caller unchanged:

```csharp
public static void ExecuteNpc(NaturePower power, Agent caster, Team casterTeam, float magnitude = 1f)
{
    if (power == NaturePower.None || caster == null || !caster.IsActive()) return;
    try { ExecuteBattleCore(power, caster, casterTeam, magnitude); } catch { }
}
```

Thread `magnitude` (default `1f`) through `ExecuteBattleCore` into
`BattleGale`, `BattleEntangle`, `BattleTorrent`, and `BattleBarrier` (the
four powers Wind/Earth/Water attacks and walls use). In each of those four
methods, multiply **only the `ApplyDamage(...)` damage argument** by
`magnitude` — leave radius, knockback distance, and slow/debuff duration
untouched, so a weak cast still reaches and affects the same targets, it
just hits softer. `BattleGale`'s change, as a worked example:

```csharp
private static void BattleGale(Agent caster, Vec3 pos, Team team, float magnitude = 1f)
{
    // ... unchanged setup ...
    ForEachEnemyInRadius(pos, NatureMath.GaleRadius, team, enemy =>
    {
        ApplyDamage(enemy, caster, NatureMath.GaleDamage * magnitude, DamageTypes.Invalid);
        // ... unchanged knockback / slow-token calls ...
    });
}
```

Apply the same pattern to `BattleEntangle`, `BattleTorrent`, and
`BattleBarrier`. Do **not** touch `BattleThunderClap` or the `Stormwall`
case in `ExecuteBattleCore` — those are unrelated to the element-magic
system (leave their calls in `ExecuteBattleCore`'s switch without a
`magnitude` argument, matching their existing signatures).

`ExecuteBattle` (the player Living Ember path used by `NatureInputHandler`)
and every other existing caller of `ExecuteNpc`/`ExecuteBattleCore` must
keep compiling and behaving identically — they simply don't pass
`magnitude`, so it defaults to `1f`.

### 5. Talent readjustment — Blood and Nature

The Nature-attunement hook (`MageElementKnowledge.HasNature`) is already
handled above: it used to triple the draw-time *cost discount*
(`NatureDrawDiscountPerSec`); it now widens the *power curve* instead
(`NatureMinPowerFraction` / `NatureMaxPowerFraction` in step 1). That
conversion — cost-discount becomes power-bonus — is the pattern to apply
everywhere else too.

This checkout has no talent literally named "Blood" — search your local
tree for the Blood and Nature talents and apply the same conversion to
each: if a talent currently makes a longer draw cheaper, or grants a flat
aging-cost discount tied to draw/charge time, change it to instead grant a
better power floor (higher `MinPowerFraction`-equivalent, so its instant
casts hit harder) and/or a higher power ceiling (an overcharge above 1.0
on a full draw), never a bigger cost discount — cost is flat by design
now, and no talent should reintroduce cost-scaling-by-draw-time. Update
each talent's `MechanicDesc`/`Lore` text to describe the new effect.

### 6. `tests/PureLogicTests.cs` — update the stale draw-time tests

`ElementMagicMath.CastAgingDays` drops the `drawSeconds`/`hasNature`
parameters, so these three existing tests (~lines 874-904) will fail to
compile as written. Replace:

```csharp
[TestMethod]
public void ElementMagicMath_CastAgingDays_MinDraw_IsBaseCost() { ... }

[TestMethod]
public void ElementMagicMath_CastAgingDays_FullDraw_IsCheaper() { ... }

[TestMethod]
public void ElementMagicMath_CastAgingDays_Nature_FloorsCheap() { ... }
```

with:

```csharp
[TestMethod]
public void ElementMagicMath_CastAgingDays_IsFlat_RegardlessOfDraw()
{
    // Cost no longer depends on draw time — charging changes power, not price.
    Assert.AreEqual(4, ElementMagicMath.CastAgingDays(CastForm.Attack));
    Assert.AreEqual(6, ElementMagicMath.CastAgingDays(CastForm.Wall));
}

[TestMethod]
public void ElementMagicMath_PowerFraction_ZeroDraw_IsMinFraction()
{
    Assert.AreEqual(ElementMagicMath.MinPowerFraction, ElementMagicMath.PowerFraction(0f), 0.001f);
    Assert.AreEqual(ElementMagicMath.NatureMinPowerFraction, ElementMagicMath.PowerFraction(0f, true), 0.001f);
}

[TestMethod]
public void ElementMagicMath_PowerFraction_MaxDraw_IsFullPower()
{
    Assert.AreEqual(1f, ElementMagicMath.PowerFraction(ElementMagicMath.MaxDrawSeconds), 0.001f);
    Assert.AreEqual(1f, ElementMagicMath.PowerFraction(999f), 0.001f); // clamps past the cap
    Assert.AreEqual(ElementMagicMath.NatureMaxPowerFraction,
        ElementMagicMath.PowerFraction(ElementMagicMath.MaxDrawSeconds, true), 0.001f);
}

[TestMethod]
public void ElementMagicMath_PowerFraction_ScalesLinearlyBetween()
{
    float half = ElementMagicMath.PowerFraction(ElementMagicMath.MaxDrawSeconds / 2f);
    float expected = ElementMagicMath.MinPowerFraction + (1f - ElementMagicMath.MinPowerFraction) * 0.5f;
    Assert.AreEqual(expected, half, 0.001f);
}
```

Leave `ElementMagicMath_BloodRejuvenation_ScalesByTier` untouched.

## Verify

```bash
dotnet build src/TheWitheringArt.csproj
dotnet test tests/AshAndEmber.Tests.csproj
```

If you have a Bannerlord install available, smoke-test manually:
- In battle, tap Attack the instant you start drawing (0 s charge) —
  confirm the cast fires immediately and visibly hits softer than before.
- Hold the draw for the full 10 s and release — confirm it hits at full
  (or, with Nature attunement, overcharged) power.
- Hold the draw past 10 s without releasing — confirm the charge disperses
  with a message and you have to start drawing again.
- Confirm the aging-cost message/ledger shows the same flat cost (4 days
  attack / 6 days wall, adjusted by any talent flat discounts) regardless
  of how long you charged.
- Cast Wind/Earth/Water at both 0 s and full draw and confirm the damage
  difference is visible (Gale knockback should reach the same distance
  either way — only the damage number should move).

## Version bump

Per `behaviour.md`, bump all four places. Check `src/TheWitheringArt.csproj`
for the current version first (the lifespan-migration task, if it landed
first, will have already moved it to `0.35.0.0`) and bump one minor version
past whatever you find there:
1. `src/TheWitheringArt.csproj` — `Version`, `AssemblyVersion`, `FileVersion`
2. `SubModule.xml` — `<Version value="vX.Y.0.0"/>`
3. `dist/AshAndEmber/SubModule.xml` — same
4. `CHANGELOG.md` — promote `## Unreleased` (or add below the most recent
   entry if lifespan-migration already promoted it) with an entry in the
   existing lore voice. Draft (edit freely — this is a suggestion, not a
   mandate):

```markdown
### The fire answers as soon as you reach for it — but patience still pays
- **Casting the unified elemental magic (Fire/Wind/Earth/Water/Spirit) no
  longer requires a minimum draw.** Reach for Attack or Block the instant
  you begin and the working fires immediately — weakly, but it lands.
  Hold the draw longer, up to ten seconds, and the same working grows
  steadily more powerful.
- **The cost is the same either way.** Charging no longer changes what a
  cast costs in years — only how hard it hits. Patience is now a wager on
  power, not a discount.
- **Held too long, the gathered fire slips away.** A charge kept past ten
  seconds without release disperses unspent — draw again from nothing.
- **The Nature attunement rewards patience more richly than before** — its
  instant casts hit harder than an ordinary caster's, and a full draw can
  overcharge past what the working could otherwise reach.
```

## Cleanup (do this last, after everything above is done and verified)

Delete this file (`charge-power-casting.md`) and remove the
`@charge-power-casting.md` import line — and the section header above it —
from `CLAUDE.md`.
