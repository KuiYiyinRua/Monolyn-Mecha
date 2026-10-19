using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
}
