using System;
using System.Collections.Generic;
using System.Linq;
using ASEL;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Monolyn.Mecha
{
    public class Pawn_Immortal : Pawn
    {
        public const float CargoCapacity = 500f, LightCapacity = 6000f, MapMoveSpeed = 3.3f;
        public const float IdlePerSecond = 1f, MovingPerSecond = 3f, WorldLightPerDay = 1200f;
        public const float ShotLight = 30f, MaximumHit = 50f;
        public const int HullMaximum = 250, ShieldMaximum = 500;
        public ThingOwner<Pawn> pilots;
        public readonly PilotHolder pilotHolder;
        public Thing attackTarget;
        public int burstRemaining, shotClock, weaponClock, teleportCooldown;
        public bool autoFire = true;
        public int shotsFired;
        MechaCombatAI combat;
        public MechaCombatAI Combat => combat ?? (combat = new MechaCombatAI(this));
        public virtual float WeaponRange => 26f;
        public virtual float MinimumWeaponRange => 2.5f;
        // Presentation-only state: never serialized or read by simulation/action executors.
        MechaUiState uiState;
        internal MechaUiState UiState => uiState ?? (uiState = new MechaUiState(this));
        internal MechaActions.CachedGizmos actionGizmos;
        public virtual int HullMax => HullMaximum;
        public virtual float CargoMax => CargoCapacity;
        public virtual float GroundSpeed => MapMoveSpeed;
        public virtual float LightMax => LightCapacity;
        public virtual float DamageLimit => MaximumHit;
        public virtual float IdleLightRate => IdlePerSecond;
        public virtual float MovingLightRate => MovingPerSecond;
        public virtual float WorldTilesPerDay => 35f;
        public virtual float WorldLightRate => WorldLightPerDay;
        public virtual string StatusTipKey => "MM_StatusTip";
        public CompLightStorage Light => this.GetComp<CompLightStorage>();
        public CompLightReceiver Receiver => this.GetComp<CompLightReceiver>();
        public CompProjectileInterceptor Shield => this.GetComp<CompProjectileInterceptor>();
        public Pawn Pilot => pilots.Count > 0 ? pilots[0] : null;
        public bool Powered => Light != null && Light.currentLight > 0.001f;
        public bool Driven => Pilot != null && !Pilot.Dead && !Pilot.Downed;
        public bool FormingCaravan => this.GetLord()?.LordJob is LordJob_FormAndSendCaravan;
        public virtual bool MovementLocked => false;
        public bool MayWalk => !MovementLocked && !Dead && Powered && (Driven || FormingCaravan || this.GetCaravan() != null);
        public float Hull => health == null ? HullMax : health.hediffSet.GetPartHealth(RaceProps.body.corePart);

        public Pawn_Immortal()
        {
            pilotHolder = new PilotHolder(this);
            pilots = new ThingOwner<Pawn>(pilotHolder, false, LookMode.Deep);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            Light.maxLightP = LightMax;
            if (!respawningAfterLoad && string.IsNullOrEmpty(Receiver.Code)) Receiver.Code = "MM" + thingIDNumber;
            // On load the distributor restores the saved graph in PostMapInit.
            // A moving pawn may be between its 60-tick reconnect checks: pruning
            // only on the joining peer would change that graph ahead of the host.
            if (!respawningAfterLoad) ReconnectLight();
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PreApplyDamage(ref dinfo, out absorbed);
            // Clamp after native damage multipliers; the single hull part cannot spill to organs.
            if (!absorbed && dinfo.Amount > DamageLimit) dinfo.SetAmount(DamageLimit);
        }

        protected override void Tick()
        {
            if (Spawned && !MayWalk && pather.Moving) pather.StopDead();
            base.Tick();
            // Thing.DoTick traverses PilotTraversal's child holder in RimWorld 1.6.
            // Ticking it here as well advances every passenger twice per game tick.
            if (!Spawned || Dead) return;
            if (teleportCooldown > 0) teleportCooldown--;
            if (weaponClock > 0) weaponClock--;
            if (shotClock > 0) shotClock--;
            if (this.IsHashIntervalTick(60)) ReconnectLight();
            if (Driven || pather.Moving)
                Light.UseLight((pather.Moving ? MovingLightRate : IdleLightRate) / 60f);
            if (!Powered || !Driven)
            {
                CancelWeapon();
                if (!MayWalk && pather.Moving) { pather.StopDead(); jobs.EndCurrentJob(JobCondition.InterruptForced); }
                return;
            }
            TickWeapon();
        }

        protected virtual void TickWeapon()
        {
            if (pather.Moving) return;
            if (attackTarget != null && !CanShoot(attackTarget)) { attackTarget = null; burstRemaining = 0; }
            if (burstRemaining > 0 && shotClock == 0)
            {
                if (attackTarget != null && Light.currentLight >= ShotLight) FireMissile(attackTarget);
                else burstRemaining = 0;
                return;
            }
            if (weaponClock > 0 || burstRemaining > 0) return;
            if (attackTarget == null && autoFire && this.IsHashIntervalTick(30))
                attackTarget = Map.attackTargetsCache.GetPotentialTargetsFor(this)
                    .Select(t => t.Thing).Where(t => t.HostileTo(this) && CanShoot(t))
                    .OrderBy(t => t.Position.DistanceToSquared(Position)).ThenBy(t => t.thingIDNumber).FirstOrDefault();
            if (attackTarget != null && Light.currentLight >= ShotLight * 2)
            {
                burstRemaining = 2;
                FireMissile(attackTarget);
            }
        }

        public virtual bool CanShoot(Thing target) => CanShootFrom(Position, target);
        public virtual bool CanShootFrom(IntVec3 cell, Thing target)
            => target != null && !target.Destroyed && target.Spawned && target.Map == Map && target != this
            && cell.InHorDistOf(target.Position, WeaponRange) && !cell.InHorDistOf(target.Position, MinimumWeaponRange)
            && GenSight.LineOfSight(cell, target.Position, Map, skipFirstCell: true)
            && (!(target is Pawn p) || !p.Dead);

        void FireMissile(Thing target)
        {
            Light.UseLight(ShotLight);
            Rotation = Rot4.FromAngleFlat((MechaGeometry.Center(target) - MechaGeometry.Center(this)).AngleFlat());
            var missile = (Projectile)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("MM_ImmortalMissile"));
            GenSpawn.Spawn(missile, Position, Map);
            Vector3 direction = (MechaGeometry.Center(target) - MechaGeometry.Center(this)).Yto0().normalized;
            Vector3 side = new Vector3(direction.z, 0f, -direction.x) * (burstRemaining == 2 ? -.42f : .42f);
            missile.Launch(this, MechaGeometry.Center(this) + side, target, target, ProjectileHitFlags.IntendedTarget | ProjectileHitFlags.NonTargetWorld);
            shotsFired++;
            burstRemaining--;
            shotClock = 12;
            if (burstRemaining == 0) weaponClock = 60;
        }

        public void ReconnectLight()
        {
            if (!Spawned || Receiver == null) return;
            foreach (var old in Receiver.cachedLightDistributors.ToArray())
                if (old == null || !old.parent.Spawned || old.parent.Map != Map
                    || !Position.InHorDistOf(old.parent.Position, old.Props.distributeRadius))
                    old?.UnregisterReceiver(Receiver);
            var nearest = LightNetwork.Covering(Map, Position);
            if (nearest != null && !Receiver.cachedLightDistributors.Contains(nearest)) nearest.RegisterReceiverNoSound(Receiver);
        }

        public virtual bool CanOccupy(IntVec3 cell, bool rejectPawns = false)
            => Spawned && cell.InBounds(Map) && cell.Standable(Map) && cell.GetEdifice(Map) == null
            && (!rejectPawns || !cell.GetThingList(Map).OfType<Pawn>().Any(p => p != this));

        public virtual void CancelWeapon() { attackTarget = null; burstRemaining = 0; }

        public bool Board(Pawn pawn)
        {
            if (Pilot != null || pawn == null || !pawn.Spawned || pawn.Map != Map || pawn.Dead || pawn.Downed
                || pawn.Faction != Faction || !pawn.RaceProps.Humanlike || !this.OccupiedRect().ExpandedBy(1).Contains(pawn.Position)) return false;
            pawn.DeSpawn();
            if (!pilots.TryAdd(pawn)) { GenSpawn.Spawn(pawn, Position, Map); return false; }
            jobs.StopAll();
            return true;
        }

        public bool Eject()
        {
            var pilot = Pilot;
            if (pilot == null) return true;
            var caravan = this.GetCaravan();
            if (caravan != null)
            {
                pilots.Remove(pilot);
                caravan.AddPawn(pilot, true);
            }
            else if (Spawned)
            {
                if (!pilots.TryDrop(pilot, Position, Map, ThingPlaceMode.Near, out _)) return false;
            }
            else return false;
            CancelWeapon();
            if (Spawned) { jobs.StopAll(); pather.StopDead(); }
            return true;
        }

        public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            Eject();
            base.Kill(dinfo, exactCulprit);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            Eject();
            base.Destroy(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref pilots, "mmPilots", pilotHolder);
            Scribe_References.Look(ref attackTarget, "mmTarget");
            Scribe_Values.Look(ref burstRemaining, "mmBurst");
            Scribe_Values.Look(ref shotClock, "mmShotClock");
            Scribe_Values.Look(ref weaponClock, "mmWeaponClock");
            Scribe_Values.Look(ref teleportCooldown, "mmTeleportCooldown");
            Scribe_Values.Look(ref autoFire, "mmAutoFire", true);
            Scribe_Values.Look(ref shotsFired, "mmShotsFired");
            Combat.ExposeData();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction != Faction.OfPlayer || Dead || !Spawned) yield break;
            yield return UiState.StatusGizmo;
            foreach (var gizmo in MechaActions.Gizmos(this)) yield return gizmo;
        }

        public override string GetInspectString()
        {
            var state = UiState;
            return state.TryGetInspectString(out var text) ? text : state.InspectString(base.GetInspectString());
        }
    }

    // Same saved ThingOwner/child-holder pattern as NivarianAircraft's real hangar and cargo.
    public sealed class PilotHolder : IThingHolder
    {
        readonly Pawn_Immortal mecha;
        public Pawn_Immortal Owner => mecha;
        public PilotHolder(Pawn_Immortal mecha) { this.mecha = mecha; }
        public IThingHolder ParentHolder => mecha;
        public ThingOwner GetDirectlyHeldThings() => mecha.pilots;
        public void GetChildHolders(List<IThingHolder> children)
            => ThingOwnerUtility.AppendThingHoldersFromThings(children, mecha.pilots);
    }

    public static class LightNetwork
    {
        public static CompLightDistributor Covering(Map map, IntVec3 cell)
        {
            var distributors = ASELComponent.Distributors;
            if (distributors == null) return null;
            CompLightDistributor nearest = null;
            int bestDistance = int.MaxValue, bestId = int.MaxValue;
            foreach (var d in distributors)
            {
                if (d?.parent == null || !d.parent.Spawned || d.parent.Map != map
                    || d.parent.Faction != Faction.OfPlayer || !d.ConnectedWithTower
                    || !cell.InHorDistOf(d.parent.Position, d.Props.distributeRadius)) continue;
                int distance = d.parent.Position.DistanceToSquared(cell), id = d.parent.thingIDNumber;
                if (nearest == null || distance < bestDistance || (distance == bestDistance && id < bestId))
                { nearest = d; bestDistance = distance; bestId = id; }
            }
            return nearest;
        }
    }
}
