using System;
using System.Collections.Generic;
using System.Linq;
using ASEL;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Monolyn.Mecha
{
    // Hide only the Colossus shield's world rendering. Keep the native component,
    // saved pool, projectile interception, recharge, EMP timers and status gizmo.
    [HarmonyPatch(typeof(CompProjectileInterceptor), nameof(CompProjectileInterceptor.PostDraw))]
    static class ColossusShieldVisual
    {
        static bool Prefix(CompProjectileInterceptor __instance) => !(__instance.parent is Pawn_Colossus);
    }

    [HarmonyPatch(typeof(CompProjectileInterceptor), nameof(CompProjectileInterceptor.PostDrawExtraSelectionOverlays))]
    static class ColossusShieldSelectionVisual
    {
        static bool Prefix(CompProjectileInterceptor __instance) => !(__instance.parent is Pawn_Colossus);
    }

    // Reuses the same real-pawn cockpit, cargo, light network and relocation executors as Immortal.
    public sealed class Pawn_Colossus : Pawn_Immortal
    {
        public const float BeamRange = 38f, SweepHalfWidth = 3f, SweepLight = 120f;
        public const int AimTicks = 36, SweepTicks = 54, RecoveryTicks = 66;
        public override int HullMax => 300;
        public override float CargoMax => 800f;
        public override float GroundSpeed => 8f;
        public override float LightMax => 10000f;
        public override float DamageLimit => float.MaxValue;
        public override float IdleLightRate => 2f;
        public override float MovingLightRate => 4f;
        public override string StatusTipKey => "MM_ColossusStatusTip";
        public int aimRemaining, sweepRemaining, sweepsFired;
        public Vector3 sweepCenter, sweepSide;
        public List<int> leftHits = new List<int>(), rightHits = new List<int>();
        readonly MoteDualAttached[] beams = new MoteDualAttached[2];
        readonly Effecter[] ends = new Effecter[2];
        Sustainer sound;
        public bool Sweeping => sweepRemaining > 0;
        public override float WeaponRange => BeamRange;
        public override float MinimumWeaponRange => 4f;

        public override bool CanOccupy(IntVec3 cell, bool rejectPawns = false)
            => Spawned && ColossusFootprint.Clear(Map, cell, this, rejectPawns);

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            // Keep native generation and the same entity; the chassis can phase through terrain.
            if (!respawningAfterLoad && !ColossusFootprint.Clear(map, Position, this, true))
                foreach (var offset in GenRadial.RadialPattern)
                {
                    var cell = Position + offset;
                    if (offset.LengthHorizontal > 30) break;
                    if (ColossusFootprint.Clear(map, cell, this, true)) { Position = cell; break; }
                }
            base.SpawnSetup(map, respawningAfterLoad);
        }

        public override bool CanShootFrom(IntVec3 cell, Thing target)
            => target != null && !target.Destroyed && target.Spawned && target.Map == Map && target != this
            && cell.InHorDistOf(target.Position, BeamRange) && !cell.InHorDistOf(target.Position, MinimumWeaponRange)
            && !target.Position.Fogged(Map) && GenSight.LineOfSight(cell, target.Position, Map, skipFirstCell: true)
            && (!(target is Pawn p) || (!p.Dead && !p.Downed));

        protected override void TickWeapon()
        {
            if (Sweeping)
            {
                UpdateSweep();
                if (--sweepRemaining == 0) { EndEffects(); weaponClock = RecoveryTicks; }
                return;
            }
            if (attackTarget != null && !CanShoot(attackTarget)) { attackTarget = null; aimRemaining = 0; }
            if (weaponClock > 0) return;
            if (attackTarget == null && autoFire && this.IsHashIntervalTick(30))
                attackTarget = Map.attackTargetsCache.GetPotentialTargetsFor(this).Select(t => t.Thing)
                    .Where(t => t.HostileTo(this) && CanShoot(t)).OrderBy(t => t.Position.DistanceToSquared(Position))
                    .ThenBy(t => t.thingIDNumber).FirstOrDefault();
            if (attackTarget == null || Light.currentLight < SweepLight) { aimRemaining = 0; return; }
            Rotation = Rot4.FromAngleFlat((MechaGeometry.Center(attackTarget) - MechaGeometry.Center(this)).AngleFlat());
            if (aimRemaining == 0) { aimRemaining = AimTicks; return; }
            if (--aimRemaining != 0) return;
            sweepCenter = attackTarget.Position.ToVector3Shifted();
            var forward = (sweepCenter - Position.ToVector3Shifted()).Yto0().normalized;
            sweepSide = new Vector3(forward.z, 0, -forward.x);
            Light.UseLight(SweepLight);
            leftHits.Clear(); rightHits.Clear(); sweepRemaining = SweepTicks; sweepsFired++; shotsFired += 2;
            UpdateSweep(); sweepRemaining--;
        }

        public Vector3 DesiredEnd(int beam, float progress)
            => sweepCenter + sweepSide * Mathf.Lerp(beam == 0 ? -SweepHalfWidth : SweepHalfWidth,
                beam == 0 ? SweepHalfWidth : -SweepHalfWidth, progress);

        public Vector3 EmitterOffset(int beam)
        {
            // Reviewed gun-tip pixels on the 962x1635 sprites; projected offsets, not rotated again.
            if (Rotation == Rot4.East || Rotation == Rot4.West)
            {
                var offset = beam == 0 ? SpriteAnchor(241, 33) : SpriteAnchor(390, 167);
                if (Rotation == Rot4.West) offset.x = -offset.x;
                return offset;
            }
            if (Rotation == Rot4.North) return beam == 0 ? SpriteAnchor(216, 96) : SpriteAnchor(742, 93);
            return beam == 0 ? SpriteAnchor(222, 105) : SpriteAnchor(740, 103);
        }

        static Vector3 SpriteAnchor(float x, float y) => new Vector3((x / 962f - .5f) * 3.6f, 0, 1.1f + (.5f - y / 1635f) * 5.6f);

        public Vector3 ClippedEnd(Vector3 desired)
        {
            var target = desired.ToIntVec3();
            // Same ground line-of-sight rule used by native Monolyn sustained beams.
            var hit = GenSight.LastPointOnLineOfSight(Position, target,
                c => c.InBounds(Map) && (this.OccupiedRect().Contains(c) || c.CanBeSeenOverFast(Map)), true);
            return hit.IsValid && hit != target ? hit.ToVector3Shifted() : desired;
        }

        void UpdateSweep()
        {
            float progress = (SweepTicks - sweepRemaining) / (float)(SweepTicks - 1);
            for (int i = 0; i < 2; i++)
            {
                Vector3 end = ClippedEnd(DesiredEnd(i, progress));
                var cell = end.ToIntVec3();
                if (!cell.InBounds(Map)) continue;
                if (beams[i] == null || beams[i].Destroyed)
                    beams[i] = MoteMaker.MakeInteractionOverlay(DefDatabase<ThingDef>.GetNamed("Mote_LargeBeam"), this, new TargetInfo(cell, Map));
                var muzzle = EmitterOffset(i);
                // Native BeamLarge has a dark additive pad at its source end (~20/512 UV).
                // Extend the invisible segment so its visible core starts on the drawn gun tip.
                const float sourcePad = 20f / 512f;
                var sourceOffset = muzzle - (end - (DrawPos + muzzle)).Yto0() * (sourcePad / (1f - sourcePad));
                beams[i].UpdateTargets(this, new TargetInfo(cell, Map), sourceOffset, end - cell.ToVector3Shifted());
                beams[i].Maintain();
                if (ends[i] == null) ends[i] = DefDatabase<EffecterDef>.GetNamed("SupressionBeam_End").Spawn(cell, Map);
                ends[i].offset = end - cell.ToVector3Shifted();
                ends[i].EffectTick(new TargetInfo(cell, Map), TargetInfo.Invalid);
                if (sweepRemaining % 5 == 0) FleckMaker.Static(end, Map, DefDatabase<FleckDef>.GetNamed("Fleck_BeamBurn"));
                var hits = i == 0 ? leftHits : rightHits;
                // One 12-damage contact per beam and entity per sweep, including multi-cell buildings.
                foreach (var thing in MechaGeometry.CellThings(cell, Map))
                {
                    if (thing == this || thing.Destroyed || !(thing is Pawn || thing is Building) || !thing.def.destroyable
                        || hits.Contains(thing.thingIDNumber) || (thing is Pawn pawn && pawn.Dead)) continue;
                    hits.Add(thing.thingIDNumber);
                    thing.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("MM_ColossusBeam"), 12f, .6f,
                        (end - MechaGeometry.Center(this)).AngleFlat(), this));
                }
            }
            if (sound == null || sound.Ended) sound = DefDatabase<SoundDef>.GetNamed("Supression_Shooting").TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
            sound?.Maintain();
        }

        void EndEffects()
        {
            for (int i = 0; i < 2; i++)
            {
                if (beams[i] != null && !beams[i].Destroyed) beams[i].Destroy();
                beams[i] = null; ends[i]?.Cleanup(); ends[i] = null;
            }
            sound?.End(); sound = null;
        }

        public override void CancelWeapon()
        {
            base.CancelWeapon(); aimRemaining = 0;
            if (Sweeping) weaponClock = Math.Max(weaponClock, RecoveryTicks);
            sweepRemaining = 0; EndEffects();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish) { EndEffects(); base.DeSpawn(mode); }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref aimRemaining, "mmColossusAim");
            Scribe_Values.Look(ref sweepRemaining, "mmColossusSweep");
            Scribe_Values.Look(ref sweepsFired, "mmColossusSweeps");
            Scribe_Values.Look(ref sweepCenter, "mmColossusCenter");
            Scribe_Values.Look(ref sweepSide, "mmColossusSide");
            Scribe_Collections.Look(ref leftHits, "mmColossusLeftHits", LookMode.Value);
            Scribe_Collections.Look(ref rightHits, "mmColossusRightHits", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { leftHits = leftHits ?? new List<int>(); rightHits = rightHits ?? new List<int>(); }
        }
    }
}
