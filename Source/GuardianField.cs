using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    // Native refueling supplies hauling, reservations, the target slider and MP's
    // built-in field watching. The base properties' stats assume a building.
    public sealed class CompProperties_GuardianSeeds : CompProperties_Refuelable
    {
        public CompProperties_GuardianSeeds() { compClass = typeof(CompGuardianSeeds); }
        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req) { yield break; }
    }
    public sealed class CompGuardianSeeds : CompRefuelable
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            // Match the native per-draw lifetime: Gizmo_Slider caches Target on
            // initialization. Reusing it would overwrite a remote target change.
            foreach (var gizmo in base.CompGetGizmosExtra())
                yield return gizmo is Gizmo_SetFuelLevel ? new Gizmo_GuardianSeeds(this) : gizmo;
        }
    }
    public sealed class Gizmo_GuardianSeeds : Gizmo_SetFuelLevel
    {
        public Gizmo_GuardianSeeds(CompRefuelable comp) : base(comp) { }
        protected override int Increments => 30;
    }

    public sealed class CompProperties_GuardianField : CompProperties
    { public CompProperties_GuardianField() { compClass = typeof(CompGuardianField); } }

    public sealed class CompGuardianField : ThingComp
    {
        public const float Radius = 8f;
        public const int Duration = 1800, SeedCost = 5;
        public int remainingTicks;
        HashSet<int> attempted = new HashSet<int>();
        Command_Action command;
        LoadedLanguage language;
        public Pawn Pawn => (Pawn)parent;
        public CompRefuelable Seeds => parent.GetComp<CompRefuelable>();
        public bool Active => remainingTicks > 0 && parent.Spawned && !Pawn.Dead && !Pawn.Downed;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        { parent.Map.GetComponent<GuardianFieldMap>().Register(this); }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        { map.GetComponent<GuardianFieldMap>().Unregister(this); }
        public override void CompTick()
        {
            if (remainingTicks <= 0) return;
            remainingTicks--;
            if (Pawn.Dead || Pawn.Downed) remainingTicks = 0;
            if (remainingTicks == 0) attempted.Clear();
        }
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref remainingTicks, "mmGuardianFieldTicks");
            // Sorted serialization prevents a process-specific hash iteration order
            // from changing snapshots. Every distinct field gets its own roll.
            List<int> ids = Scribe.mode == LoadSaveMode.Saving ? new List<int>(attempted) : null;
            ids?.Sort();
            Scribe_Collections.Look(ref ids, "mmGuardianFieldProjectiles", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) attempted = new HashSet<int>(ids ?? new List<int>());
        }
        public void Activate()
        {
            if (!parent.Spawned || parent.Faction != Faction.OfPlayer || Pawn.Dead || Pawn.Downed
                || !Pawn.IsColonyMechPlayerControlled || remainingTicks > 0 || Seeds == null || Seeds.Fuel < SeedCost) return;
            Seeds.ConsumeFuel(SeedCost);
            attempted.Clear();
            remainingTicks = Duration;
        }
        internal bool TryRoll(Projectile projectile)
            => attempted.Add(projectile.thingIDNumber) && Rand.Chance(.5f);
        internal bool AlreadyTried(Projectile projectile) => attempted.Contains(projectile.thingIDNumber);
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!parent.Spawned || parent.Faction != Faction.OfPlayer || Pawn.Dead) yield break;
            if (command == null || language != LanguageDatabase.activeLanguage)
            {
                language = LanguageDatabase.activeLanguage;
                command = new Command_Action { defaultLabel = "MM_GuardianField".Translate(),
                    defaultDesc = "MM_GuardianFieldDesc".Translate(), icon = parent.def.uiIcon, action = Activate,
                    onHover = () => GenDraw.DrawRadiusRing(parent.Position, Radius) };
            }
            command.Disabled = false; command.disabledReason = null;
            if (remainingTicks > 0) command.Disable("MM_GuardianFieldActive".Translate((remainingTicks / 60f).ToString("F1")));
            else if (Pawn.Downed || !Pawn.IsColonyMechPlayerControlled) command.Disable("MM_AutonomyUnavailable".Translate());
            else if (Seeds == null || Seeds.Fuel < SeedCost) command.Disable("MM_GuardianFieldNeedsSeeds".Translate());
            yield return command;
        }
        public override string CompInspectStringExtra()
            => remainingTicks > 0 ? "MM_GuardianFieldActive".Translate((remainingTicks / 60f).ToString("F1")).ToString() : null;
        public override void PostDraw()
        {
            if (!Active) return;
            var pos = parent.DrawPos;
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            var matrix = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(Radius * 2, 1, Radius * 2));
            Graphics.DrawMesh(MeshPool.plane10, matrix, GuardianFieldVisual.Material, 0);
        }
    }

    [StaticConstructorOnStartup]
    static class GuardianFieldVisual
    {
        internal static readonly Material Material = MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow,
            new Color(.55f, .85f, 1f, .24f));
    }

    public sealed class GuardianFieldMap : MapComponent
    {
        readonly List<CompGuardianField> fields = new List<CompGuardianField>();
        readonly List<Crossing> crossings = new List<Crossing>();
        struct Crossing { internal CompGuardianField Field; internal float Fraction; }
        public GuardianFieldMap(Map map) : base(map) { }
        internal void Register(CompGuardianField field) { if (!fields.Contains(field)) fields.Add(field); }
        internal void Unregister(CompGuardianField field) { fields.Remove(field); }

        internal bool Intercept(Projectile projectile, Vector3 from, Vector3 to)
        {
            if (projectile.Launcher == null || from == to) return false;
            crossings.Clear();
            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (!field.Active || field.AlreadyTried(projectile) || !projectile.Launcher.HostileTo(field.parent)) continue;
                if (TryCrossing(from, to, MechaGeometry.Center(field.parent), CompGuardianField.Radius, out float fraction))
                    crossings.Add(new Crossing { Field = field, Fraction = fraction });
            }
            crossings.Sort((a, b) => { int c = a.Fraction.CompareTo(b.Fraction);
                return c != 0 ? c : a.Field.parent.thingIDNumber.CompareTo(b.Field.parent.thingIDNumber); });
            for (int i = 0; i < crossings.Count; i++)
            {
                var hit = crossings[i];
                if (!hit.Field.TryRoll(projectile)) continue;
                GuardianReflection.Reflect(projectile, hit.Field.Pawn, Vector3.Lerp(from, to, hit.Fraction));
                return true;
            }
            return false;
        }

        // Sweep the segment, rather than testing one cell: fast shots cannot skip a
        // field, and overlapping fields are processed in travel order then pawn ID.
        public static bool TryCrossing(Vector3 from, Vector3 to, Vector3 center, float radius, out float fraction)
        {
            var d = (to - from).Yto0(); var offset = (from - center).Yto0();
            float a = d.sqrMagnitude, b = 2f * Vector3.Dot(offset, d), c = offset.sqrMagnitude - radius * radius;
            fraction = 0f;
            if (a < .0000001f) return false;
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return false;
            float root = Mathf.Sqrt(discriminant);
            float first = (-b - root) / (2f * a), second = (-b + root) / (2f * a);
            fraction = first >= 0f ? first : second;
            return fraction >= 0f && fraction <= 1f;
        }
    }

    // Register on the main thread; Projectile's own static initializer creates
    // materials and must not be forced by the Mod constructor's PatchAll.
    [StaticConstructorOnStartup]
    static class GuardianReflection
    {
        static readonly AccessTools.FieldRef<Projectile, Vector3> Origin = AccessTools.FieldRefAccess<Projectile, Vector3>("origin");
        static readonly AccessTools.FieldRef<Projectile, Vector3> Destination = AccessTools.FieldRefAccess<Projectile, Vector3>("destination");
        static readonly AccessTools.FieldRef<Projectile, Thing> Launcher = AccessTools.FieldRefAccess<Projectile, Thing>("launcher");
        static readonly AccessTools.FieldRef<Projectile, int> Ticks = AccessTools.FieldRefAccess<Projectile, int>("ticksToImpact");
        static readonly AccessTools.FieldRef<Projectile, int> Lifetime = AccessTools.FieldRefAccess<Projectile, int>("lifetime");
        static readonly AccessTools.FieldRef<Projectile, ThingDef> Cover = AccessTools.FieldRefAccess<Projectile, ThingDef>("targetCoverDef");
        static GuardianReflection()
        {
            var target = AccessTools.DeclaredMethod(typeof(Projectile), "CheckForFreeInterceptBetween");
            if (target == null) throw new MissingMethodException(typeof(Projectile).FullName, "CheckForFreeInterceptBetween");
            new Harmony("local.monolyn.mecha.guardianfield").Patch(target,
                prefix: new HarmonyMethod(typeof(GuardianReflection), nameof(Prefix)));
            Log.Message("[Monolyn Mecha] Guardian field projectile target resolved=1 missing=0");
        }
        static bool Prefix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
        {
            if (!__instance.Spawned || !__instance.Map.GetComponent<GuardianFieldMap>().Intercept(__instance, lastExactPos, newExactPos)) return true;
            __result = true; // End this movement tick without invoking Impact/destroy.
            return false;
        }
        internal static void Reflect(Projectile projectile, Pawn guardian, Vector3 contact)
        {
            Vector3 source = Origin(projectile);
            Thing shooter = projectile.Launcher;
            Origin(projectile) = contact.Yto0();
            Destination(projectile) = source.Yto0();
            Launcher(projectile) = guardian;
            projectile.usedTarget = shooter != null && !shooter.Destroyed && shooter.Spawned && shooter.Map == projectile.Map
                ? new LocalTargetInfo(shooter) : new LocalTargetInfo(source.ToIntVec3());
            projectile.intendedTarget = projectile.usedTarget;
            projectile.HitFlags = ProjectileHitFlags.All;
            Cover(projectile) = null;
            Ticks(projectile) = Lifetime(projectile) = Math.Max(1, Mathf.CeilToInt((source.Yto0() - contact.Yto0()).magnitude / projectile.def.projectile.SpeedTilesPerTick));
            projectile.Position = contact.ToIntVec3();
        }
    }
}
