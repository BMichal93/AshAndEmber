// =============================================================================
// ASH AND EMBER — BattleEvents.Events.cs
// The battle events (Cinder Rain, Rising, Dread, Last Light, Ashen Ground,
// Frenzy, Kindling, Storm, Tremor, Deluge, Madness).
// Partial of BattleEvents (shared state lives in BattleEvents.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class BattleEvents
    {
        // ── Event: Cinder Rain ────────────────────────────────────────────────
        // Cinders fall from a burning sky in scattered patches. Each patch is a
        // lingering, IMPARTIAL fire (CasterTeam null) that burns whoever stands in
        // it — no longer a blanket strike on every fighter at once, but a field
        // sown with hazards to fight around.
        private static void FireCinderRain()
        {
            if (Mission.Current == null) return;
            Vec3 centre = GetFieldCentre();
            if (centre == Vec3.Zero) return;

            int patches = CinderPatchMin + _rng.Next(CinderPatchMax - CinderPatchMin + 1);
            for (int i = 0; i < patches; i++)
            {
                double angle = _rng.NextDouble() * Math.PI * 2;
                float  dist  = (float)(_rng.NextDouble() * CinderSpread);
                Vec3   pos   = centre + new Vec3((float)Math.Cos(angle) * dist,
                                                 (float)Math.Sin(angle) * dist, 0f);
                SnapToGround(ref pos);
                // A lingering burning patch that damages anyone in it (team null).
                try { SpellEffects.SpawnFirePatch(pos, CinderPatchDamage, null); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // A cinder streaks down out of the sky onto the patch.
                try { SpellEffects.SpawnBigFireParticle(pos + new Vec3(0f, 0f, 10f), CinderRainInterval * 0.35f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.SpawnExplosionParticle(pos, CinderRainInterval * 0.30f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Sky layer: stacked fire columns read as cinders descending.
            SpawnFireRainLayer(centre, 32f, 14);
            // Atmospheric: burning-sky fog + aerial glow (no blanket ground fire —
            // the damaging patches above already carry the flame).
            ApplyFog(new Vec3(0.90f, 0.28f, 0.04f), 0.004f);
            SpawnAerialGlow(centre, 30f, 14f, 3, ColorSchool.Orange, CinderRainInterval * 0.80f);
        }

        // ── Event: The Rising ─────────────────────────────────────────────────
        // Spawns RisingSpawnCount tier-1 units on the Ashen side.
        private static void FireTheRising()
        {
            if (Mission.Current == null || _ashenTeam == null) return;
            Vec3 anchor = GetTeamCentroid(_ashenTeam);
            int spawned = SpawnRisingUnits(RisingSpawnCount);
            if (spawned <= 0) return; // troop type missing or no valid anchor — say nothing
            // Ground eruption: explosion burst at the spawn point, as if torn from below
            try { SpellEffects.SpawnExplosionEffect(anchor, ColorSchool.Purple, 8f, TheRisingInterval * 0.60f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.SpawnBurstExplosion(anchor, ColorSchool.Ashen, 12f, TheRisingInterval * 0.65f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // Fire ring surrounding the breach
            for (int i = 0; i < 4; i++)
            {
                double angle = Math.PI * 2.0 / 4 * i;
                Vec3 pos = anchor + new Vec3((float)Math.Cos(angle) * 3f,
                                             (float)Math.Sin(angle) * 3f, 0f);
                try { SpellEffects.SpawnTempFireParticle(pos, TheRisingInterval * 0.7f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Atmospheric: dim ghostly lights above the tear
            SpawnGroundFireField(anchor, 12f, 5, ColorSchool.Purple, TheRisingInterval * 0.75f);
            SpawnAerialGlow(anchor, 16f, 10f, 3, ColorSchool.Ashen, TheRisingInterval * 0.75f);
            MBInformationManager.AddQuickInformation(new TextObject(
                $"The Rising — {spawned} more pour from the grey."));
        }

        // ── Event: Dread ──────────────────────────────────────────────────────
        // All non-Ashen agents lose DreadMoralePenalty morale. Fires once.
        private static void FireDread()
        {
            if (Mission.Current == null) return;
            int count = 0;
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (IsAshenAgent(agent)) continue;
                try { agent.SetMorale(Math.Max(0f, agent.GetMorale() - DreadMoralePenalty)); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Haunted grey aura on every affected fighter — visible fear made manifest
                try { SpellEffects.BeginAgentGlow(agent, ColorSchool.Ashen, 30f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                count++;
            }
            // Impact bursts radiate outward across the field like a shockwave of terror
            Vec3 centre = GetFieldCentre();
            for (int i = 0; i < 4; i++)
            {
                Vec3 pos = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 22f,
                                             (float)(_rng.NextDouble() - 0.5) * 22f, 0f);
                try { SpellEffects.SpawnImpactBurst(pos, ColorSchool.Ashen, 30f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Atmospheric: deep-dusk sky, cold dark fog, wide field of grey flames
            TintSky(22f); // deep dusk / near-night
            ApplyFog(new Vec3(0.18f, 0.18f, 0.26f), 0.006f); // cold dark fog
            SpawnGroundFireField(centre, 40f, 10, ColorSchool.Ashen, 30f);
            SpawnAerialGlow(centre, 35f, 16f, 5, ColorSchool.Ashen, 30f);
            if (count > 0)
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Dread — something cold passes through {count} fighters. Courage breaks."));
        }

        // ── Event: Last Light ─────────────────────────────────────────────────
        // The sun dies: non-Ashen morale drains, the Ashen are lifted and lit like
        // beacons. Fires once.
        // NOTE: the Scene.TimeOfDay setter is unreliable mid-mission (the skybox is
        //       baked, so it changed nothing) — the darkness is driven by dense
        //       dark fog and the only remaining light is the fires we scatter.
        private static void FireLastLight()
        {
            // Last Light owns the sky — bar periodic events from re-tinting it.
            _skySet = true;

            int blinded = 0;
            int empowered = 0;
            if (Mission.Current != null)
            {
                foreach (var agent in Mission.Current.Agents.ToList())
                {
                    if (!agent.IsActive() || agent.IsMount) continue;
                    try
                    {
                        if (IsAshenAgent(agent))
                        {
                            agent.SetMorale(Math.Min(100f, agent.GetMorale() + 15f));
                            empowered++;
                        }
                        else
                        {
                            agent.SetMorale(Math.Max(0f, agent.GetMorale() - 20f));
                            blinded++;
                        }
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }

            // Ashen agents glow with fire-light in the sudden darkness — beacons in the black
            if (Mission.Current != null)
            {
                foreach (var agent in Mission.Current.Agents.ToList())
                {
                    if (!agent.IsActive() || agent.IsMount || !IsAshenAgent(agent)) continue;
                    try { SpellEffects.BeginAgentGlow(agent, ColorSchool.Orange, 60f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            // Large fires scattered across the field — the only light sources left standing
            Vec3 centre = GetFieldCentre();
            for (int i = 0; i < 6; i++)
            {
                Vec3 pos = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 30f,
                                             (float)(_rng.NextDouble() - 0.5) * 30f, 0f);
                try { SpellEffects.SpawnBigFireParticle(pos, 60f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Atmospheric: deep, dense near-black fog swallows the field (this is
            // what actually darkens it), lit only by the scattered fires and the
            // Ashen beacons; a low red aerial glow keeps the sky from going flat.
            ApplyFog(new Vec3(0.05f, 0.05f, 0.09f), 0.020f, 1.6f); // drowning dark
            SpawnGroundFireField(centre, 40f, 10, ColorSchool.Orange, 60f);
            SpawnAerialGlow(centre, 40f, 18f, 6, ColorSchool.Red, 60f);
            MBInformationManager.AddQuickInformation(new TextObject(
                $"Last Light — the sun dies. Darkness swallows the field." +
                (blinded > 0   ? $" {blinded} fighters lose their footing in the dark." : "") +
                (empowered > 0 ? $" The Ashen rise." : "")));
        }

        // ── Event: Ashen Ground ───────────────────────────────────────────────
        // All mounted agents (both sides) are dismounted via SpellEffects.ForceDismount.
        private static void FireAshenGround()
        {
            if (Mission.Current == null) return;
            int count = 0;
            var dismounted = new List<Vec3>();
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || !agent.HasMount) continue;
                dismounted.Add(agent.Position);
                SpellEffects.ForceDismount(agent);
                count++;
            }
            // Ground eruption at each dismount position — the earth itself tears the mount down
            Vec3 centre = GetFieldCentre();
            foreach (var pos in dismounted.Take(4))
            {
                try { SpellEffects.SpawnExplosionEffect(pos, ColorSchool.Ashen, 5f, AshenGroundInterval * 0.70f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.SpawnExplosionParticle(pos, AshenGroundInterval * 0.50f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Central shockwave as the ashen ground cracks open across the whole field
            try { SpellEffects.SpawnBurstExplosion(centre, ColorSchool.Ashen, 25f, AshenGroundInterval * 0.65f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // Atmospheric: ash fog + grey ground effect across the field
            ApplyFog(new Vec3(0.48f, 0.47f, 0.50f), 0.005f); // grey ash fog
            SpawnGroundFireField(centre, 30f, 5, ColorSchool.Ashen, AshenGroundInterval * 0.80f);
            if (count > 0)
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Ashen Ground — {count} mount{(count != 1 ? "s" : "")} fall. No one rides today."));
        }

        // ── Event: Frenzy ─────────────────────────────────────────────────────
        // Issues a charge order to every non-empty formation on both sides.
        private static void FireFrenzy()
        {
            if (Mission.Current == null) return;
            foreach (var team in Mission.Current.Teams.ToList())
            {
                if (team == null) continue;
                try
                {
                    foreach (var formation in team.FormationsIncludingEmpty.ToList())
                    {
                        if (formation == null || formation.CountOfUnits == 0) continue;
                        try { formation.SetMovementOrder(MovementOrder.MovementOrderCharge); }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Red bloodlust glow on every agent — discipline shattered, only killing remains
            if (Mission.Current != null)
            {
                foreach (var agent in Mission.Current.Agents.ToList())
                {
                    if (!agent.IsActive() || agent.IsMount) continue;
                    try { SpellEffects.BeginAgentGlow(agent, ColorSchool.Red, FrenzyInterval * 0.55f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            // Chaos flashes across the field as lines break — pure red light, no
            // fire textures (Frenzy is bloodlust, not a blaze).
            Vec3 centre = GetFieldCentre();
            for (int i = 0; i < 6; i++)
            {
                Vec3 pos = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 25f,
                                             (float)(_rng.NextDouble() - 0.5) * 25f, 0f);
                try { SpellEffects.SpawnTempLight(pos + new Vec3(0f, 0f, 1f), ColorSchool.Red, 8f, FrenzyInterval * 0.55f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Atmospheric: red chaos fog + crimson aerial glow (no ground fire).
            ApplyFog(new Vec3(0.85f, 0.15f, 0.05f), 0.003f); // blood-red haze
            SpawnAerialGlow(centre, 32f, 12f, 4, ColorSchool.Red, FrenzyInterval * 0.80f);
            MBInformationManager.AddQuickInformation(new TextObject(
                "Frenzy — no one can hold the line. All charge."));
        }

        // ── Spawn helper ──────────────────────────────────────────────────────
        // Spawns `count` Ashen Spawn agents (ashen_thrall, falling back to
        // vanilla bandit troops) near the centroid of the Ashen team. Returns
        // the number actually spawned so the caller can stay silent when
        // nothing appeared.
        // ── Event: The Kindling ───────────────────────────────────────────────
        // Raw magic in the field wakes into a few elemental beings that join a
        // side (the player's enemies where there is one), shaped by the ground.
        private static void FireKindling()
        {
            if (Mission.Current == null) return;
            try
            {
                // Pick a side to reinforce — an enemy of the player if the battle
                // has one, otherwise any team with bodies on it.
                Team target = null;
                try
                {
                    Team pt = Mission.Current.PlayerTeam;
                    foreach (var t in Mission.Current.Teams.ToList())
                    {
                        if (t == null) continue;
                        bool hasBodies = Mission.Current.Agents.Any(a => a.IsActive() && !a.IsMount && a.Team == t);
                        if (!hasBodies) continue;
                        if (pt != null && t.IsEnemyOf(pt)) { target = t; break; }
                        if (pt == null && target == null) target = t;
                    }
                    if (target == null && pt != null)
                        target = Mission.Current.Teams.FirstOrDefault(t => t != null && t != pt);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (target == null) return;

                bool snowy = false; try { snowy = SpellEffects.SceneIsSnowy(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                string sceneName = "";
                try { sceneName = (Mission.Current.SceneName ?? "").ToLowerInvariant(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                ElementalKind kind = ElementUltimateMath.ElementalKindForScene(snowy, sceneName);

                Vec3 anchor = GetTeamCentroid(target);
                if (anchor.x == 0f && anchor.y == 0f) return;

                for (int i = 0; i < ElementalMath.KindlingBodies; i++)
                {
                    Vec3 pos = anchor + new Vec3((float)(_rng.NextDouble() - 0.5) * 6f,
                                                 (float)(_rng.NextDouble() - 0.5) * 6f, 0f);
                    try { ElementalFactory.SpawnElemental(kind, target, pos, charge: true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                try { MBInformationManager.AddQuickInformation(new TextObject(
                    "The Kindling — the ground itself wakes and takes a side.")); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event: Storm ──────────────────────────────────────────────────────
        // A gale sweeps the field. The engine missile modifiers are driven near
        // zero so arrows, bolts and thrown weapons flop out of the air; a gust
        // shoves a handful of fighters down-wind each fire.
        private static void FireStorm()
        {
            var m = Mission.Current;
            if (m == null) return;

            // Re-assert the missile choke each gust (cheap, idempotent) in case
            // another system touched the modifiers.
            ArmStormMissiles();

            Vec3 centre = GetFieldCentre();
            // Driven dust sweeping across the field along the wind line.
            for (int i = 0; i < 10; i++)
            {
                Vec3 p = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 60f,
                                           (float)(_rng.NextDouble() - 0.5) * 60f, 0f);
                try { SpellEffects.SpawnTempSmokeWisp(p + new Vec3(0f, 0f, 1.2f), 1.4f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // The gust shoves a few fighters down-wind and staggers them.
            var caught = new List<Agent>();
            foreach (var a in m.Agents.ToList())
            {
                if (!a.IsActive() || a.IsMount || !a.IsHuman) continue;
                caught.Add(a);
            }
            for (int i = 0; i < Math.Min(StormGustVictims, caught.Count); i++)
            {
                var a = caught[_rng.Next(caught.Count)];
                try { NatureEffects.KnockbackAgent(a, a.Position + _stormDir * StormGustPush); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { NatureEffects.ApplySpeedToken(a, 0.7f, StormInterval * 0.6f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.SpawnTempSmokeWisp(a.Position + new Vec3(0f, 0f, 1.0f), 0.8f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // Atmospheric: pale grey storm haze.
            ApplyFog(new Vec3(0.55f, 0.58f, 0.65f), 0.004f);
        }

        // Drive the engine's missile speed/range modifiers near zero so no ranged
        // weapon carries. A fresh mission resets these, so no teardown is needed.
        private static void ArmStormMissiles()
        {
            var m = Mission.Current;
            if (m == null) return;
            try { m.SetBowMissileSpeedModifier(StormMissileSpeed);      } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { m.SetCrossbowMissileSpeedModifier(StormMissileSpeed); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { m.SetThrowingMissileSpeedModifier(StormMissileSpeed); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { m.SetMissileRangeModifier(StormMissileRange);         } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event: Tremor ─────────────────────────────────────────────────────
        // The ground heaves. Several quakes erupt across the field, blunting and
        // staggering everyone in each radius (both sides), and churn the broken
        // earth to bogging mud that slows all who cross it.
        private static void FireTremor()
        {
            var m = Mission.Current;
            if (m == null) return;
            Vec3 centre = GetFieldCentre();
            if (centre == Vec3.Zero) return;

            int quakes = TremorQuakeMin + _rng.Next(TremorQuakeMax - TremorQuakeMin + 1);
            for (int q = 0; q < quakes; q++)
            {
                double angle = _rng.NextDouble() * Math.PI * 2;
                float  dist  = (float)(_rng.NextDouble() * TremorSpread);
                Vec3   pos   = centre + new Vec3((float)Math.Cos(angle) * dist,
                                                 (float)Math.Sin(angle) * dist, 0f);
                SnapToGround(ref pos);

                // Eruption + lingering mud (impartial slow).
                try { SpellEffects.SpawnBurstExplosion(pos, ColorSchool.Nature, 6f, TremorInterval * 0.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.SpawnNatureBurst(pos, NatureElement.Earth, 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.SpawnMudPatch(pos); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                float r2 = TremorRadius * TremorRadius;
                foreach (var a in m.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    if ((a.Position - pos).LengthSquared > r2) continue;
                    try { SpellEffects.DamageAgent(a, TremorDamage); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    // Thrown off their feet — held a beat as the ground bucks.
                    try { NatureEffects.ApplySpeedToken(a, 0f, 1.2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            // Atmospheric: settling dust haze.
            ApplyFog(new Vec3(0.42f, 0.36f, 0.26f), 0.004f);
            MBInformationManager.AddQuickInformation(new TextObject(
                "Tremor — the earth heaves and will not be still."));
        }

        // ── Event: Deluge ─────────────────────────────────────────────────────
        // A drowning rain. Every fire on the field is quenched (patches, fire
        // walls, burning men), and the sodden ground drags at everyone — all wade
        // at reduced speed. A cold counter to Cinder Rain and any fire-caster.
        private static void FireDeluge()
        {
            var m = Mission.Current;
            if (m == null) return;
            Vec3 centre = GetFieldCentre();
            if (centre == Vec3.Zero) return;

            // One broad sweep douses field-fire patches and fire-wall wards.
            try { SpellEffects.QuenchFireAt(centre, DelugeQuenchRadius); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            foreach (var a in m.Agents.ToList())
            {
                if (!a.IsActive() || a.IsMount || !a.IsHuman) continue;
                // Douse a burning man and drag him down to a wading pace.
                try { ElementSpellEffects.QuenchIgnition(a); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { NatureEffects.ApplySpeedToken(a, DelugeSlowMult, DelugeInterval * 0.8f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // Rain made visible: water bursts scattered across the field.
            for (int i = 0; i < 10; i++)
            {
                Vec3 p = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 60f,
                                           (float)(_rng.NextDouble() - 0.5) * 60f, 0f);
                try { SpellEffects.SpawnNatureBurst(p + new Vec3(0f, 0f, 0.3f), NatureElement.Water, 1.2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Atmospheric: cold blue-grey downpour haze.
            ApplyFog(new Vec3(0.30f, 0.42f, 0.60f), 0.005f);
        }

        // ── Event: Madness ────────────────────────────────────────────────────
        // Reason breaks. One common (non-hero) fighter on each side turns on its
        // own and joins an enemy team. Heroes and lords keep their wits — only the
        // rank and file are taken.
        private static void FireMadness()
        {
            var m = Mission.Current;
            if (m == null) return;

            var teams = new List<Team>();
            try { foreach (var t in m.Teams) if (t != null) teams.Add(t); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (teams.Count < 2) return;

            // Choose a victim per team FIRST (before any reassignment), so a unit
            // just turned cannot be picked again and sent straight back.
            var swaps = new List<(Agent victim, Team dest)>();
            foreach (var team in teams)
            {
                var pool = new List<Agent>();
                foreach (var a in m.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || !a.IsHuman || a.IsHero) continue;
                    if (a.Team != team) continue;
                    pool.Add(a);
                }
                if (pool.Count == 0) continue;

                var enemies = new List<Team>();
                foreach (var t in teams)
                {
                    if (t == team) continue;
                    bool foe = false;
                    try { foe = t.IsEnemyOf(team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    if (foe) enemies.Add(t);
                }
                if (enemies.Count == 0) continue;

                swaps.Add((pool[_rng.Next(pool.Count)], enemies[_rng.Next(enemies.Count)]));
            }

            int turned = 0;
            foreach (var (victim, dest) in swaps)
            {
                try
                {
                    // A spirit-touched flash as the mind gives way, then the turn.
                    try { SpellEffects.SpawnBurstExplosion(victim.Position, ColorSchool.Purple, 3f, 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { SpellEffects.BeginAgentGlow(victim, ColorSchool.Purple, 5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    victim.SetTeam(dest, false);
                    turned++;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            if (turned > 0)
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Madness — {turned} turn on their own and take up the enemy's cause."));
        }

        private static int SpawnRisingUnits(int count)
        {
            int spawned = 0;
            try
            {
                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("ashen_thrall")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                if (troop == null) return 0;

                Vec3 anchor = GetTeamCentroid(_ashenTeam);
                if (anchor.x == 0f && anchor.y == 0f) return 0; // no valid anchor

                Vec2 dir = Vec2.Forward;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        Vec3 pos = anchor + new Vec3(
                            (float)(_rng.NextDouble() - 0.5) * 6f,
                            (float)(_rng.NextDouble() - 0.5) * 6f,
                            0f
                        );
                        // Snap to ground surface
                        float gz = pos.z;
                        try
                        {
                            Mission.Current.Scene.GetHeightAtPoint(
                                pos.AsVec2,
                                BodyFlags.CommonCollisionExcludeFlagsForAgent,
                                ref gz);
                            pos.z = gz;
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                        // An AgentBuildData with no explicit equipment/body properties
                        // spawns with default BodyProperties (age 0) — which renders as
                        // the infant/"baby" model. Generate proper adult body properties
                        // from the troop so the spawn always looks like a grown warrior.
                        int seed = _rng.Next();
                        Equipment equipment = troop.FirstBattleEquipment ?? troop.Equipment;
                        BodyProperties body = troop.GetBodyProperties(equipment, seed);

                        var origin    = new BasicBattleAgentOrigin(troop);
                        var agentData = new AgentBuildData(origin)
                            .Team(_ashenTeam)
                            .Controller(AgentControllerType.AI)
                            .Equipment(equipment)
                            .BodyProperties(body)
                            .Age((int)body.Age)
                            .ClothingColor1(AshenVisuals.ClothAshGrey)
                            .ClothingColor2(AshenVisuals.ClothColdBlue)
                            .InitialPosition(in pos)
                            .InitialDirection(in dir);

                        var agent = Mission.Current.SpawnAgent(agentData, false);
                        // Fallback bandit troops carry no Ashen marker, so the
                        // OnAgentBuild hook won't catch them — force the look.
                        try { AshenVisuals.ForceApply(agent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        spawned++;
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return spawned;
        }

    }
}
