using System;
using System.Collections.Generic;
using System.Linq;
using ASEL;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    public sealed class Pawn_Reaver : Pawn_Immortal
    {
        public const int MagazineCapacity = 8, ManufactureTicks = 300, AttackTicks = 60;
        public const float MissileLight = 150f, MissileRange = 45f;
        public override int HullMax => 280;
        public override float CargoMax => 2000f;
        public override float GroundSpeed => 4f;
        public override float LightMax => 10000f;
        public override float DamageLimit => float.MaxValue;
        public override float WorldTilesPerDay => 4f;
        public override string StatusTipKey => "MM_ReaverStatusTip";
        public int missiles, fabricationTicks, missilesBuilt;
        public bool autoFabricate = true;
        public override float WeaponRange => MissileRange;
        public override float MinimumWeaponRange => 4.5f;

        // Pawn tweening follows the anchor cell, while a 2x3 footprint has a half-cell center offset.
        public override Vector3 DrawPos
        {
            get
            {
                var rect = this.OccupiedRect();
                return base.DrawPos + new Vector3((rect.minX + rect.maxX + 1) * .5f - (Position.x + .5f), 0,
                    (rect.minZ + rect.maxZ + 1) * .5f - (Position.z + .5f));
            }
        }

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

        protected override void Tick()
        {
            base.Tick();
            if (Spawned) AdvanceFabrication(1);
        }

        // The magazine belongs to this pawn, so relocation never creates fresh ammunition.
        // Pay once, atomically at completion; low light and disabling the factory pause progress.
        public void AdvanceFabrication(int delta)
        {
            if (Dead || !autoFabricate || delta <= 0 || missiles >= MagazineCapacity || Light.currentLight < MissileLight) return;
            while (delta > 0 && missiles < MagazineCapacity && Light.currentLight >= MissileLight)
            {
                int step = Math.Min(delta, ManufactureTicks - fabricationTicks);
                fabricationTicks += step;
                delta -= step;
                if (fabricationTicks < ManufactureTicks) break;
                Light.UseLight(MissileLight);
                fabricationTicks = 0;
                missiles++;
                missilesBuilt++;
            }
        }

        public override bool CanShootFrom(IntVec3 cell, Thing target)
            => target != null && !target.Destroyed && target.Spawned && target.Map == Map && target != this
            && cell.InHorDistOf(target.Position, MissileRange) && !cell.InHorDistOf(target.Position, MinimumWeaponRange)
            && !target.Position.Fogged(Map) && GenSight.LineOfSight(cell, target.Position, Map, skipFirstCell: true)
            && (!(target is Pawn p) || (!p.Dead && !p.Downed));

        protected override void TickWeapon()
        {
            if (attackTarget != null && !CanShoot(attackTarget)) attackTarget = null;
            if (pather.Moving || weaponClock > 0 || missiles == 0) return;
            if (attackTarget == null && autoFire && this.IsHashIntervalTick(30))
                attackTarget = Map.attackTargetsCache.GetPotentialTargetsFor(this).Select(t => t.Thing)
                    .Where(t => t.HostileTo(this) && CanShoot(t)).OrderBy(t => t.Position.DistanceToSquared(Position))
                    .ThenBy(t => t.thingIDNumber).FirstOrDefault();
            if (attackTarget == null) return;
            Rotation = Rot4.FromAngleFlat((MechaGeometry.Center(attackTarget) - MechaGeometry.Center(this)).AngleFlat());
            var missile = (Projectile)ThingMaker.MakeThing(ThingDef.Named("MM_ReaverMissile"));
            GenSpawn.Spawn(missile, Position, Map);
            missile.Launch(this, MechaGeometry.Center(this) + MissileMuzzleOffset(), attackTarget, attackTarget,
                ProjectileHitFlags.IntendedTarget | ProjectileHitFlags.NonTargetWorld);
            missiles--;
            shotsFired++;
            weaponClock = AttackTicks;
        }

        public Vector3 MissileMuzzleOffset()
        {
            // Keep the emitter on the artwork when its transparent margins are compensated.
            if (Rotation.IsHorizontal) return ReaverSpriteGeometry.Point(Rotation, 1238, 731);
            return Rotation == Rot4.South ? ReaverSpriteGeometry.Point(Rotation, 512, 1160)
                : ReaverSpriteGeometry.Point(Rotation, 512, 167);
        }

        public string FactoryStatus => (missiles >= MagazineCapacity ? "MM_ReaverFull"
            : !autoFabricate ? "MM_ReaverPaused" : Light.currentLight < MissileLight ? "MM_ReaverLowLight" : "MM_ReaverBuilding").Translate();
        public IEnumerable<Gizmo> MagazineGizmos()
        {
            yield return new Gizmo_ReaverMagazine(this);
            yield return new Command_Toggle
            {
                defaultLabel = "MM_ReaverFabricate".Translate(), defaultDesc = "MM_ReaverFabricateDesc".Translate(),
                icon = def.uiIcon, isActive = () => autoFabricate, toggleAction = ToggleFabrication
            };
        }
        public void ToggleFabrication() { if (!Dead && Faction == Faction.OfPlayer) autoFabricate = !autoFabricate; }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction == Faction.OfPlayer && !Dead && Spawned)
                foreach (var gizmo in MagazineGizmos()) yield return gizmo;
        }
        public override string GetInspectString()
            => base.GetInspectString() + "\n" + "MM_ReaverAmmo".Translate(missiles, MagazineCapacity)
                + " · " + FactoryStatus + " (" + (fabricationTicks / 60f).ToString("F1") + " / 5 s)";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref missiles, "mmReaverMissiles");
            Scribe_Values.Look(ref fabricationTicks, "mmReaverFabrication");
            Scribe_Values.Look(ref missilesBuilt, "mmReaverBuilt");
            Scribe_Values.Look(ref autoFabricate, "mmReaverAutoFabricate", true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                missiles = Mathf.Clamp(missiles, 0, MagazineCapacity);
                fabricationTicks = missiles == MagazineCapacity ? 0 : Mathf.Clamp(fabricationTicks, 0, ManufactureTicks - 1);
            }
        }
    }

    public sealed class Projectile_ReaverMissile : TrailProjectileNewB
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (blockedByShield) { Destroy(); return; }
            Map map = Map;
            IntVec3 cell = Position;
            var target = intendedTarget.Thing;
            Destroy();
            if (map == null || !cell.InBounds(map)) return;
            def.GetModExtension<ASELModExtension>()?.impactEffecter?
                .Spawn(new TargetInfo(cell, map), new TargetInfo(cell, map)).Cleanup();
            GenExplosion.DoExplosion(cell, map, 4f, def.projectile.damageDef, launcher,
                damAmount: 16, armorPenetration: .75f, projectile: def, intendedTarget: target,
                chanceToStartFire: 0f, damageFalloff: false, applyDamageToExplosionCellsNeighbors: false);
        }
    }

    // Map pawns manufacture in Tick; world pawns manufacture once per caravan clock interval.
    [HarmonyPatch(typeof(Caravan_PathFollower), nameof(Caravan_PathFollower.PatherTickInterval))]
    static class ReaverWorldFabrication
    {
        static void Postfix(Caravan ___caravan, int delta)
        {
            foreach (var reaver in ___caravan.PawnsListForReading.OfType<Pawn_Reaver>())
                reaver.AdvanceFabrication(delta);
        }
    }

    public sealed class Gizmo_ReaverMagazine : Gizmo
    {
        readonly Pawn_Reaver reaver;
        public Gizmo_ReaverMagazine(Pawn_Reaver reaver) { this.reaver = reaver; Order = -99f; }
        public override float GetWidth(float maxWidth) => 170f;
        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            var rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Widgets.DrawWindowBackground(rect);
            var inner = rect.ContractedBy(6f);
            var font = Text.Font; var anchor = Text.Anchor;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 18f), "MM_ReaverAmmo".Translate(reaver.missiles, Pawn_Reaver.MagazineCapacity));
            var bar = new Rect(inner.x, inner.y + 22f, inner.width, 23f);
            Widgets.FillableBar(bar, reaver.fabricationTicks / (float)Pawn_Reaver.ManufactureTicks, BaseContent.GreyTex, BaseContent.BlackTex, true);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, (reaver.fabricationTicks / 60f).ToString("F1") + " / 5 s");
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(inner.x, inner.y + 46f, inner.width, 18f), reaver.FactoryStatus);
            TooltipHandler.TipRegion(rect, "MM_ReaverFabricateDesc".Translate());
            Text.Font = font; Text.Anchor = anchor;
            return new GizmoResult(GizmoState.Clear);
        }
    }

    // Measured opaque silhouettes, rather than the PNG canvas, fill the actual six-cell footprint.
    public static class ReaverSpriteGeometry
    {
        public static Vector2 DrawSize(Rot4 facing) => facing.IsHorizontal ? new Vector2(4608f / 1449f, 2048f / 810f)
            : facing == Rot4.North ? new Vector2(2048f / 882f, 4608f / 1289f)
            : new Vector2(2048f / 837f, 4608f / 1198f);

        public static Vector3 Point(Rot4 facing, float px, float py)
        {
            var size = DrawSize(facing);
            float width = facing.IsHorizontal ? 1536f : 1024f, height = facing.IsHorizontal ? 1024f : 1536f;
            float centerX = facing.IsHorizontal ? 763.5f : facing == Rot4.North ? 512f : 512.5f;
            float centerY = facing.IsHorizontal ? 519f : facing == Rot4.North ? 748.5f : 723f;
            var result = new Vector3((px - centerX) / width * size.x, 0, (centerY - py) / height * size.y);
            if (facing == Rot4.West) result.x = -result.x;
            return result;
        }

        public static Vector3 Offset(Rot4 facing) => Point(facing, facing.IsHorizontal ? 768 : 512, facing.IsHorizontal ? 512 : 768);
    }

    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    static class ReaverDirectionalScale
    {
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (!(node is PawnRenderNode_AnimalPart_Body) || !(parms.pawn is Pawn_Reaver)) return;
            var size = ReaverSpriteGeometry.DrawSize(parms.facing);
            __result.x *= size.x / 2f; __result.z *= size.y / 3f;
        }
    }
    [HarmonyPatch(typeof(PawnRenderNodeWorker_AnimalBody), nameof(PawnRenderNodeWorker_AnimalBody.OffsetFor))]
    static class ReaverBodyOffset
    {
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            if (node is PawnRenderNode_AnimalPart_Body && parms.pawn is Pawn_Reaver)
                __result += ReaverSpriteGeometry.Offset(parms.facing);
        }
    }
}

