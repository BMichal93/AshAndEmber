// =============================================================================
// ASH AND EMBER — BattleEvents.Events.cs
// The seven battle events (Cinder Rain, Ember Tithe, Rising, Dread, …).
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
        // All non-Ashen agents take PeriodicDamage HP of damage.
        private static void FireCinderRain()
        {
            if (Mission.Current == null) return;
            var victims = new List<Agent>();
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (IsAshenAgent(agent)) continue;
                SpellEffects.DamageAgent(agent, PeriodicDamage);
                victims.Add(agent);
            }
            // Impact: big fire strike + explosion burst at up to 8 victim positions
            for (int i = 0; i < Math.Min(8, victims.Count); i++)
            {
                var a = victims[_rng.Next(victims.Count)];
                Vec3 pos = a.Position + new Vec3((float)(_rng.NextDouble() - 0.5) * 2f,
                                                 (float)(_rng.NextDouble() - 0.5) * 2f, 0f);
                try { SpellEffects.SpawnBigFireParticle(pos, CinderRainInterval * 0.55f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.SpawnExplosionParticle(pos, CinderRainInterval * 0.35f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Sky layer: stacked fire columns at multiple heights simulate fire streaks descending
            Vec3 centre = GetFieldCentre();
            SpawnFireRainLayer(centre, 32f, 14);
            // Atmospheric: burning-sky fog + wide ground fire field + aerial glow
            ApplyFog(new Vec3(0.90f, 0.28f, 0.04f), 0.004f);
            SpawnGroundFireField(centre, 35f, 6, ColorSchool.Red, CinderRainInterval * 0.80f);
            SpawnAerialGlow(centre, 30f, 14f, 3, ColorSchool.Orange, CinderRainInterval * 0.80f);
        }

        // ── Event: Ember Tithe ────────────────────────────────────────────────
        // All Ashen agents take PeriodicDamage HP of damage but gain +10 morale
        // (the ritual price they embrace fuels their resolve).
        private static void FireEmberTithe()
        {
            if (Mission.Current == null) return;
            var victims = new List<Agent>();
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (!IsAshenAgent(agent)) continue;
                SpellEffects.DamageAgent(agent, PeriodicDamage);
                // The Ashen embrace the tithe — pain fuels their resolve
                try { agent.SetMorale(Math.Min(100f, agent.GetMorale() + 10f)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                victims.Add(agent);
            }
            // Inner-fire glow on each burning Ashen agent: bodies lit with amber resolve
            foreach (var a in victims.Take(6))
                try { SpellEffects.BeginAgentGlow(a, ColorSchool.Yellow, EmberTitheInterval * 0.65f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // Atmospheric: ritual circle lights + amber pulse above the Ashen position
            if (_ashenTeam != null)
            {
                Vec3 ashenCentre = GetTeamCentroid(_ashenTeam);
                try { SpellEffects.SpawnCircleLights(ashenCentre, ColorSchool.Yellow, 10f, EmberTitheInterval * 0.80f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                SpawnAerialGlow(ashenCentre, 20f, 10f, 3, ColorSchool.Yellow, EmberTitheInterval * 0.80f);
                SpawnGroundFireField(ashenCentre, 12f, 4, ColorSchool.Orange, EmberTitheInterval * 0.80f);
            }
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
        // Sets scene time-of-day to midnight, drains morale from non-Ashen
        // agents and boosts Ashen agents. Fires once.
        // NOTE: Scene.TimeOfDay setter API varies by Bannerlord version;
        //       wrapped in try/catch so a missing API fails silently.
        private static void FireLastLight()
        {
            // Last Light always overrides the sky — it's the defining one-shot event.
            _skySet = true;
            try { Mission.Current?.Scene.TimeOfDay = 23f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
            // Atmospheric: fire-lit midnight fog, wide ground fire, burning-sky aerial glow
            ApplyFog(new Vec3(0.80f, 0.22f, 0.05f), 0.005f); // fire-lit night
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
            // Impact bursts erupt across the field as lines break and chaos spreads
            Vec3 centre = GetFieldCentre();
            for (int i = 0; i < 4; i++)
            {
                Vec3 pos = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 25f,
                                             (float)(_rng.NextDouble() - 0.5) * 25f, 0f);
                try { SpellEffects.SpawnImpactBurst(pos, ColorSchool.Red, FrenzyInterval * 0.55f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Scattered fires mark the lines breaking into chaos
            for (int i = 0; i < 5; i++)
            {
                Vec3 pos = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 25f,
                                             (float)(_rng.NextDouble() - 0.5) * 25f, 0f);
                try { SpellEffects.SpawnTempFireParticle(pos, FrenzyInterval * 0.9f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            // Atmospheric: red chaos fog, wide ground fire, aerial crimson glow
            ApplyFog(new Vec3(0.85f, 0.15f, 0.05f), 0.003f); // blood-red fog
            SpawnGroundFireField(centre, 38f, 7, ColorSchool.Red, FrenzyInterval * 0.80f);
            SpawnAerialGlow(centre, 32f, 12f, 4, ColorSchool.Orange, FrenzyInterval * 0.80f);
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
