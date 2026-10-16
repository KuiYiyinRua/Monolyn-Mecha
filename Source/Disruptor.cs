using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    public sealed class Pawn_Disruptor : Pawn_Immortal
    {
        public const int OrbTicks = 300, RecoveryTicks = 300;
        public const float OrbLight = 180f, OrbRange = 30f;
        public override int HullMax => 200;
        public override float CargoMax => 600f;
        public override float GroundSpeed => 6f;
        public override float LightMax => 8000f;
        public override float DamageLimit => float.MaxValue;
        public override float WorldTilesPerDay => 30f;
        public override string StatusTipKey => "MM_DisruptorStatusTip";
        public Thing_DisruptorOrb activeOrb;
        // Visual-only phase: deliberately not serialized. An empty cockpit has no outer petals.
        public float rotationPhase;
        public bool PetalsVisible => Driven;
        public override bool MovementLocked => activeOrb != null && !activeOrb.Destroyed;
        public override float WeaponRange => OrbRange;
        public override float MinimumWeaponRange => 5.5f;

        protected override void Tick()
        {
            base.Tick();
            if (Spawned && !Dead && Driven && Powered) rotationPhase = Mathf.Repeat(rotationPhase + .75f, 360f);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        { base.DrawAt(drawLoc, flip); if (Spawned && !Dead) DisruptorVisual.Draw(this, drawLoc); }

        public override Vector3 DrawPos
        {
            get
            {
                // Spawned positions already include the footprint center inside the tweener.
                // Adding it here would snap by a cell whenever an even-sized pawn turns.
                return base.DrawPos + (Spawned ? Vector3.zero : FootprintCenterOffset);
            }
        }
        internal Vector3 FootprintCenterOffset => (MechaGeometry.Center(this) - Position.ToVector3Shifted()).Yto0();
        public override bool CanOccupy(IntVec3 cell, bool rejectPawns = false)
            => Spawned && ReaverFootprint.Clear(Map, cell, Rotation, this, rejectPawns);

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            if (!respawningAfterLoad && !ReaverFootprint.Clear(map, Position, Rotation, this, true))
                foreach (var offset in GenRadial.RadialPattern)
                {
                    if (offset.LengthHorizontal > 30) break;
                    if (ReaverFootprint.Clear(map, Position + offset, Rotation, this, true)) { Position += offset; break; }
                }
            base.SpawnSetup(map, respawningAfterLoad);
        }

        public override bool CanShootFrom(IntVec3 cell, Thing target)
            => Thing_DisruptorOrb.Enemy(target, this) && !target.Position.Fogged(Map)
            && cell.InHorDistOf(target.Position, OrbRange) && !cell.InHorDistOf(target.Position, MinimumWeaponRange)
            && GenSight.LineOfSight(cell, target.Position, Map, skipFirstCell: true);

        protected override void TickWeapon()
        {
            if (MovementLocked || weaponClock > 0 || pather.Moving) return;
            if (!CanShoot(attackTarget)) attackTarget = null;
            if (attackTarget == null && autoFire && this.IsHashIntervalTick(30))
                attackTarget = Map.attackTargetsCache.GetPotentialTargetsFor(this).Select(t => t.Thing)
                    .Where(CanShoot).OrderBy(t => t.Position.DistanceToSquared(Position))
                    .ThenBy(t => t.thingIDNumber).FirstOrDefault();
            if (attackTarget == null || Light.currentLight < OrbLight) return;
            Rotation = Rot4.FromAngleFlat((MechaGeometry.Center(attackTarget) - MechaGeometry.Center(this)).AngleFlat());
            var orb = (Thing_DisruptorOrb)ThingMaker.MakeThing(ThingDef.Named("MM_DisruptorOrb"));
            orb.caster = this; orb.target = attackTarget; orb.location = MechaGeometry.Center(this).Yto0() + new Vector3(0, 0, .15f);
            activeOrb = orb;
            pather.StopDead(); jobs.StopAll();
            Light.UseLight(OrbLight); shotsFired++;
            GenSpawn.Spawn(orb, orb.location.ToIntVec3(), Map);
        }

        // Stop/auto-fire changes only affect subsequent shots; an already paid orb keeps its lifetime.
        public void OrbEnded(Thing_DisruptorOrb orb)
        {
            if (activeOrb != orb) return;
            activeOrb = null; weaponClock = RecoveryTicks;
        }
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // Forced map removal/death dissipates the field; it never detonates on another map.
            if (activeOrb != null && !activeOrb.Destroyed) activeOrb.Destroy();
            base.DeSpawn(mode);
        }
        public override void ExposeData()
        { base.ExposeData(); Scribe_References.Look(ref activeOrb, "mmDisruptorOrb"); }
        public string OrbStatus => MovementLocked ? "MM_DisruptorActive".Translate(((OrbTicks - activeOrb.age) / 60f).ToString("F1"))
            : weaponClock > 0 ? "MM_Cooldown".Translate((weaponClock / 60f).ToString("F1"))
            : "MM_DisruptorReady".Translate();
        public override string GetInspectString() => base.GetInspectString() + "\n" + OrbStatus;
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            if (Spawned && !Dead && Faction == Faction.OfPlayer) yield return new Gizmo_DisruptorOrb(this);
        }
    }

    // A real saved map entity, independently ticking after pilot ejection or loss of power.
    public sealed class Thing_DisruptorOrb : Thing
    {
        public Pawn_Disruptor caster;
        public Thing target;
        public Vector3 location;
        public int age;
        public const float Speed = 8f;
        public override Vector3 DrawPos => new Vector3(location.x, AltitudeLayer.MoteOverhead.AltitudeFor(), location.z);
        public static bool Enemy(Thing thing, Pawn_Disruptor owner)
            => owner != null && owner.Spawned && thing != null && thing != owner && !thing.Destroyed && thing.Spawned
            && thing.Map == owner.Map && thing.def.destroyable && (thing is Pawn || thing is Building)
            && thing.HostileTo(owner) && (!(thing is Pawn pawn) || (!pawn.Dead && !pawn.Downed));

        protected override void Tick()
        {
            base.Tick();
            if (caster == null || caster.Dead || caster.Destroyed || !caster.Spawned || caster.Map != Map)
            { Destroy(); return; }
            age++;
            if (!Enemy(target, caster))
            {
                target = null;
                if (age % 15 == 1) target = Map.attackTargetsCache.GetPotentialTargetsFor(caster).Select(t => t.Thing)
                    .Where(t => Enemy(t, caster) && t.Position.InHorDistOf(Position, 30f)
                        && GenSight.LineOfSight(Position, t.Position, Map, skipFirstCell: true))
                    .OrderBy(t => t.Position.DistanceToSquared(Position)).ThenBy(t => t.thingIDNumber).FirstOrDefault();
            }
            if (target != null)
            {
                // World-space cells per second: pawn movement costs and terrain slowdowns do not apply.
                var next = Vector3.MoveTowards(location, MechaGeometry.Center(target).Yto0(), Speed / 60f);
                var cell = next.ToIntVec3();
                if (cell.InBounds(Map) && (cell == Position || cell.CanBeSeenOverFast(Map) || target.OccupiedRect().Contains(cell)))
                { location = next; if (cell != Position) Position = cell; }
            }
            if (age % 3 == 0) FleckMaker.Static(DrawPos, Map, DefDatabase<FleckDef>.GetNamed("Fleck_DisruptorTrail"), .65f);
            if (age % 15 == 0) Pulse();
            if (age >= Pawn_Disruptor.OrbTicks) Detonate();
        }

        void Pulse()
        {
            var touched = new HashSet<Thing>();
            foreach (var cell in CellRect.CenteredOn(Position, 1).Cells)
                if (cell.InBounds(Map) && GenSight.LineOfSight(Position, cell, Map, skipFirstCell: true))
                    foreach (var thing in MechaGeometry.CellThings(cell, Map))
                        if (Enemy(thing, caster) && touched.Add(thing))
                            thing.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("MM_DisruptorField"), 2f, .45f,
                                (MechaGeometry.Center(thing) - location).AngleFlat(), caster));
        }

        void Detonate()
        {
            var map = Map; var cell = Position; var owner = caster;
            // Native explosion handles propagation and building multipliers. Restrict both phases to enemies.
            var ignored = map.listerThings.AllThings.Where(t => !Enemy(t, owner)).ToList();
            var cells = GenRadial.RadialCellsAround(cell, 5f, true).Where(c => c.InBounds(map)).ToList();
            FleckMaker.Static(DrawPos, map, DefDatabase<FleckDef>.GetNamed("Fleck_MNSparkPulse"), 3.3f);
            FleckMaker.Static(DrawPos, map, DefDatabase<FleckDef>.GetNamed("Fleck_MNSparkFlashPulse"), 1.4f);
            GenExplosion.DoExplosion(cell, map, 5f, DefDatabase<DamageDef>.GetNamed("MM_DisruptorBlast"), owner,
                damAmount: 20, armorPenetration: .35f, ignoredThings: ignored, intendedTarget: target,
                chanceToStartFire: 0, damageFalloff: false, doVisualEffects: false, overrideCells: cells);
            Destroy();
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        { caster?.OrbEnded(this); base.Destroy(mode); }
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(DrawPos, flip);
            var size = 2.2f + .15f * Mathf.Sin(age * .15f);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(DrawPos + new Vector3(0,.01f,0),
                Quaternion.AngleAxis(age * 4f, Vector3.up), new Vector3(size, 1, size)), OrbMaterials.Ring, 0);
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref caster, "mmCaster"); Scribe_References.Look(ref target, "mmHomingTarget");
            Scribe_Values.Look(ref location, "mmOrbLocation"); Scribe_Values.Look(ref age, "mmOrbAge");
        }
    }
    [StaticConstructorOnStartup]
    static class OrbMaterials
    { public static readonly Material Ring = MaterialPool.MatFrom("Things/Mote/MNPulseBase", ShaderDatabase.MoteGlow); }

    public sealed class Gizmo_DisruptorOrb : Gizmo
    {
        readonly Pawn_Disruptor unit;
        public Gizmo_DisruptorOrb(Pawn_Disruptor unit) { this.unit = unit; Order = -99; }
        public override float GetWidth(float maxWidth) => 178;
        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            var rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75); Widgets.DrawWindowBackground(rect);
            var font = Text.Font; var anchor = Text.Anchor; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(rect.x+7, rect.y+6, rect.width-14, 20), "MM_DisruptorOrbLabel".Translate());
            var bar = new Rect(rect.x+7, rect.y+28, rect.width-14, 28);
            float fill = unit.MovementLocked ? 1f - unit.activeOrb.age/(float)Pawn_Disruptor.OrbTicks
                : 1f - unit.weaponClock/(float)Pawn_Disruptor.RecoveryTicks;
            Widgets.FillableBar(bar, Mathf.Clamp01(fill), BaseContent.GreyTex, BaseContent.BlackTex, true);
            Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(bar, unit.OrbStatus);
            TooltipHandler.TipRegion(rect, "MM_DisruptorAttackDesc".Translate()); Text.Font = font; Text.Anchor = anchor;
            return new GizmoResult(GizmoState.Clear);
        }
    }

    [HarmonyPatch(typeof(PawnTweener), "TweenedPosRoot")]
    static class DisruptorTweenCenter
    {
        static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            if (___pawn is Pawn_Disruptor unit && unit.Spawned) __result += unit.FootprintCenterOffset;
        }
    }

    // The native single-cell avoidance offset changes sides when pawns overlap.
    // Keep the chassis centered on its footprint; pathing and physical collision stay native.
    [HarmonyPatch(typeof(PawnCollisionTweenerUtility), nameof(PawnCollisionTweenerUtility.PawnCollisionPosOffsetFor))]
    static class DisruptorCollisionVisual
    {
        static bool Prefix(Pawn pawn, ref Vector3 __result)
        {
            if (!(pawn is Pawn_Disruptor)) return true;
            __result = Vector3.zero;
            return false;
        }
    }

    // Alpha-calibrated constants are set after the three approved source PNGs are measured.
    public static class DisruptorSpriteGeometry
    {
        public static Vector2 DrawSize(Rot4 facing) => Vector2.one * (facing.IsHorizontal ? 1.80f : 1.79f);
        public static Vector3 Offset(Rot4 facing)
        {
            float x = facing.IsHorizontal ? -.1478f : facing == Rot4.North ? .0014f : 0f;
            if (facing == Rot4.West) x = -x;
            return new Vector3(x, 0, facing.IsHorizontal ? .0725f : facing == Rot4.North ? .0957f : .0945f);
        }
    }
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    static class DisruptorDirectionalScale
    {
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (!(node is PawnRenderNode_AnimalPart_Body) || !(parms.pawn is Pawn_Disruptor)) return;
            var size = DisruptorSpriteGeometry.DrawSize(parms.facing); __result.x *= size.x / 2; __result.z *= size.y / 2;
        }
    }
    [HarmonyPatch(typeof(PawnRenderNodeWorker_AnimalBody), nameof(PawnRenderNodeWorker_AnimalBody.OffsetFor))]
    static class DisruptorBodyOffset
    {
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        { if (node is PawnRenderNode_AnimalPart_Body && parms.pawn is Pawn_Disruptor) __result += DisruptorSpriteGeometry.Offset(parms.facing); }
    }
}
