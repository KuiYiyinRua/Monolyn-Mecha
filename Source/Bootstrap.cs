using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Monolyn.Mecha
{
    public sealed class MechaMod : Mod
    {
        public MechaMod(ModContentPack pack) : base(pack)
        {
            new Harmony("local.monolyn.mecha").PatchAll();
            Log.Message("[Monolyn Mecha] 0.9.2 loaded; Immortal, Stalker, Colossus, Guardian, Reaver and Disruptor with native Monolyn light and shielding.");
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetChildHolders))]
    static class PilotTraversal
    {
        static void Postfix(Pawn __instance, List<IThingHolder> outChildren)
        { if (__instance is Pawn_Immortal m) outChildren.Add(m.pilotHolder); }
    }
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    static class OnlyLightNeed
    {
        static bool Prefix(Pawn ___pawn, ref bool __result)
        { if (!(___pawn is Pawn_Immortal)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    static class RequireDriver
    {
        static bool Prefix(Pawn ___pawn) => !(___pawn is Pawn_Immortal m) || m.MayWalk;
    }
    // A chassis uses its defined hull health, not the organic pawn's 150-damage lethal threshold.
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDead))]
    static class HullDeath
    {
        static bool Prefix(Pawn ___pawn, ref bool __result)
        { if (!(___pawn is Pawn_Immortal m)) return true; __result = m.Dead || m.Hull <= 0f; return false; }
    }
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDowned))]
    static class HullDowning
    {
        static bool Prefix(Pawn ___pawn, ref bool __result)
        { if (!(___pawn is Pawn_Immortal)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    static class MechaCargoCapacity
    {
        static bool Prefix(Pawn p, StringBuilder explanation, ref float __result)
        {
            if (!(p is Pawn_Immortal m)) return true;
            __result = m.CargoMax;
            explanation?.AppendLine("  - " + p.LabelShortCap + ": " + m.CargoMax.ToString("F0") + " kg");
            return false;
        }
    }

    public static class MechaCaravans
    {
        public const int TicksPerTile = 1714; // round(60,000 / 35), vanilla integer travel clock.
        // A mixed convoy travels at the slowest warframe's fixed rate.
        public static int TravelTicks(IEnumerable<Pawn> pawns)
            => Units(pawns).Select(m => UnityEngine.Mathf.RoundToInt(60000f / m.WorldTilesPerDay)).DefaultIfEmpty(TicksPerTile).Max();
        public static List<Pawn_Immortal> Units(IEnumerable<Pawn> pawns)
            => pawns.OfType<Pawn_Immortal>().Where(m => !m.Dead).ToList();
        public static bool HasMecha(Caravan caravan) => caravan != null && caravan.PawnsListForReading.Any(p => p is Pawn_Immortal m && !m.Dead);
    }
    [HarmonyPatch(typeof(CaravanTicksPerMoveUtility), nameof(CaravanTicksPerMoveUtility.GetTicksPerMove),
        new Type[] { typeof(List<Pawn>), typeof(float), typeof(float), typeof(bool), typeof(StringBuilder) })]
    static class MechaWorldSpeed
    {
        static bool Prefix(List<Pawn> pawns, StringBuilder explanation, ref int __result)
        {
            if (!pawns.Any(p => p is Pawn_Immortal m && !m.Dead)) return true;
            __result = MechaCaravans.TravelTicks(pawns);
            explanation?.Append("MM_WorldSpeed".Translate(MechaCaravans.Units(pawns).Min(m => m.WorldTilesPerDay).ToString("0.##")));
            return false;
        }
    }
    [HarmonyPatch(typeof(Caravan_PathFollower), nameof(Caravan_PathFollower.CostToMove),
        new Type[] { typeof(Caravan), typeof(PlanetTile), typeof(PlanetTile), typeof(int?) })]
    static class FixedWorldTileCost
    {
        static bool Prefix(Caravan caravan, PlanetTile start, PlanetTile end, ref int __result)
        {
            if (!MechaCaravans.HasMecha(caravan)) return true;
            __result = start == end ? 0 : MechaCaravans.TravelTicks(caravan.PawnsListForReading);
            return false;
        }
    }
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.NightResting), MethodType.Getter)]
    static class MechanicalNightTravel
    {
        static void Postfix(Caravan __instance, ref bool __result)
        { if (MechaCaravans.HasMecha(__instance)) __result = false; }
    }
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.CantMove), MethodType.Getter)]
    static class EmptyWorldFuel
    {
        static void Postfix(Caravan __instance, ref bool __result)
        {
            if (__instance.PawnsListForReading.OfType<Pawn_Immortal>().Any(m => !m.Dead && (!m.Powered || m.MovementLocked))) __result = true;
        }
    }
    [HarmonyPatch(typeof(Caravan_PathFollower), nameof(Caravan_PathFollower.PatherTickInterval))]
    static class CaravanLightConsumption
    {
        static void Prefix(Caravan_PathFollower __instance, Caravan ___caravan, int delta)
        {
            if (!__instance.Moving || __instance.Paused || ___caravan.CantMove) return;
            foreach (var m in ___caravan.PawnsListForReading.OfType<Pawn_Immortal>())
                if (!m.Dead) m.Light.UseLight(m.WorldLightRate * delta / 60000f);
        }
    }

    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    static class CaravanLightDisplay
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (var gizmo in __result) yield return gizmo;
            if (__instance.Faction == Faction.OfPlayer)
                foreach (var m in MechaCaravans.Units(__instance.PawnsListForReading))
                {
                    yield return new Gizmo_ImmortalStatus(m);
                    if (m is Pawn_Reaver reaver)
                        foreach (var gizmo in reaver.MagazineGizmos()) yield return gizmo;
                }
        }
    }

    // Native receiver IDs use System.Random; this unit has a stable ID and remains safe across save/load.
    public sealed class CompMechaLightReceiver : ASEL.CompLightReceiver
    {
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (Code == null) Code = "MM" + parent.thingIDNumber;
            base.PostSpawnSetup(respawningAfterLoad);
        }
    }
    public sealed class CompProperties_MechaLightReceiver : ASEL.CompProperties_LightReceiver
    {
        public CompProperties_MechaLightReceiver() { compClass = typeof(CompMechaLightReceiver); }
    }
    public sealed class StatPart_ImmortalMoveSpeed : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        { if (req.HasThing && req.Thing is Pawn_Immortal m) val = m.GroundSpeed; }
        public override string ExplanationPart(StatRequest req)
            => req.HasThing && req.Thing is Pawn_Immortal m ? (m is Pawn_Disruptor ? "MM_DisruptorSpeed" : m is Pawn_Colossus ? "MM_ColossusSpeed" : m is Pawn_Reaver ? "MM_ReaverSpeed" : "MM_MapSpeed").Translate() : null;
    }
}
