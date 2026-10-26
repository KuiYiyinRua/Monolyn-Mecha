using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Monolyn.Mecha
{
    // Classification belongs to the caravan UI. Changing Pawn.IsAnimal or
    // IsColonyMech globally would also change combat, repair and mechanitor control.
    static class WarframeCaravanSelection
    {
        public static bool PackAnimal(Pawn pawn) => pawn.IsAnimal || pawn is Pawn_Immortal;
        public static bool ControlledMech(Pawn pawn) => !(pawn is Pawn_Immortal) && pawn.IsColonyMech;

        public static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                string helper = instruction.Calls(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsAnimal)))
                    ? nameof(PackAnimal)
                    : instruction.Calls(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonyMech)))
                        ? nameof(ControlledMech) : null;
                if (helper != null)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(WarframeCaravanSelection), helper);
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1)
                throw new InvalidOperationException("Monolyn Mecha: unexpected caravan predicate in " + original + ": " + replaced);
        }
    }

    [HarmonyPatch]
    static class WarframeCaravanSections
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var animal = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsAnimal));
            var mech = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonyMech));
            var targets = typeof(CaravanUIUtility).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(AccessTools.GetDeclaredMethods)
                .Where(m => m.Name.StartsWith("<AddPawnsSections>", StringComparison.Ordinal)
                    && PatchProcessor.GetOriginalInstructions(m).Any(i => i.Calls(animal) || i.Calls(mech)))
                .ToArray();
            if (targets.Length != 2)
                throw new MissingMethodException("Monolyn Mecha: caravan animal/mech section predicates were not found.");
            return targets;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            => WarframeCaravanSelection.Replace(instructions, __originalMethod);
    }

    // Pack warframes can accompany any colonist. Their mechanitor may stay home;
    // the ordinary Escort selection rule must not silently deselect them again.
    [HarmonyPatch]
    static class WarframeCaravanEscortSelection
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Dialog_FormCaravan), "Notify_TransferablesChanged");
            yield return AccessTools.Method(typeof(Dialog_FormCaravan), "ShouldShowWarningForMechWithoutMechanitor");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
            => WarframeCaravanSelection.Replace(instructions, __originalMethod);
    }

    // Native previews call the int-only CostToMove overload, which reapplies
    // terrain/road modifiers even though mechanical travel ignores them. Use
    // the same calculation as the actual path follower while pawns are known.
    static class MechaCaravanSpeedPreview
    {
        internal static bool Prefix(IEnumerable<Pawn> pawns, bool isShuttle, StringBuilder explanation, ref float result)
        {
            var selected = pawns.ToList();
            if (isShuttle || !selected.Any(p => p is Pawn_Immortal m && !m.Dead)) return true;
            result = 60000f / MechaCaravans.TravelTicks(selected, explanation);
            return false;
        }
        internal static IEnumerable<Pawn> Pawns(List<TransferableOneWay> transferables, bool remaining)
        {
            foreach (var transferable in transferables)
            {
                if (!transferable.HasAnyThing || !(transferable.AnyThing is Pawn)) continue;
                int start = remaining ? transferable.CountToTransfer : 0;
                int end = remaining ? transferable.things.Count : transferable.CountToTransfer;
                for (int i = start; i < end; i++) yield return (Pawn)transferable.things[i];
            }
        }
    }
    [HarmonyPatch(typeof(TilesPerDayCalculator), nameof(TilesPerDayCalculator.ApproxTilesPerDay),
        new Type[] { typeof(Caravan), typeof(StringBuilder) })]
    static class MechaCaravanCurrentSpeed
    {
        static bool Prefix(Caravan caravan, StringBuilder explanation, ref float __result)
            => MechaCaravanSpeedPreview.Prefix(caravan.PawnsListForReading, caravan.Shuttle != null, explanation, ref __result);
    }
    [HarmonyPatch(typeof(TilesPerDayCalculator), nameof(TilesPerDayCalculator.ApproxTilesPerDay),
        new Type[] { typeof(List<TransferableOneWay>), typeof(float), typeof(float), typeof(PlanetTile), typeof(PlanetTile), typeof(bool), typeof(StringBuilder) })]
    static class MechaCaravanFormationSpeed
    {
        static bool Prefix(List<TransferableOneWay> transferables, bool isShuttle, StringBuilder explanation, ref float __result)
            => MechaCaravanSpeedPreview.Prefix(MechaCaravanSpeedPreview.Pawns(transferables, false), isShuttle, explanation, ref __result);
    }
    [HarmonyPatch(typeof(TilesPerDayCalculator), nameof(TilesPerDayCalculator.ApproxTilesPerDayLeftAfterTransfer))]
    static class MechaCaravanRemainingSpeed
    {
        static bool Prefix(List<TransferableOneWay> transferables, bool isShuttle, StringBuilder explanation, ref float __result)
            => MechaCaravanSpeedPreview.Prefix(MechaCaravanSpeedPreview.Pawns(transferables, true), isShuttle, explanation, ref __result);
    }
    [HarmonyPatch(typeof(TilesPerDayCalculator), nameof(TilesPerDayCalculator.ApproxTilesPerDay),
        new Type[] { typeof(List<ThingCount>), typeof(float), typeof(float), typeof(PlanetTile), typeof(PlanetTile), typeof(bool), typeof(StringBuilder) })]
    static class MechaCaravanTradeSpeed
    {
        static bool Prefix(List<ThingCount> thingCounts, bool isShuttle, StringBuilder explanation, ref float __result)
            => MechaCaravanSpeedPreview.Prefix(thingCounts.Where(t => t.Count > 0).Select(t => t.Thing).OfType<Pawn>(), isShuttle, explanation, ref __result);
    }
}
