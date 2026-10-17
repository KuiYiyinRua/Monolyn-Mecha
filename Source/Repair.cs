using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Monolyn.Mecha
{
    // Warframes use light storage, so vanilla's mandatory energy need excludes them.
    [HarmonyPatch(typeof(WorkGiver_RepairMech), nameof(WorkGiver_RepairMech.HasJobOnThing))]
    static class WarframeRepairEligibility
    {
        static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
        {
            if (!(t is Pawn_Immortal mech)) return;
            var repair = mech.TryGetComp<CompMechRepairable>();
            __result = ModsConfig.BiotechActive && !mech.Dead && mech.Spawned
                && mech.Faction == pawn.Faction && repair != null
                && !mech.InAggroMentalState && !mech.HostileTo(pawn)
                && !mech.IsBurning() && !mech.IsAttacking() && !mech.IsForbidden(pawn)
                && pawn.CanReserve(mech, 1, -1, null, forced)
                && MechRepairUtility.CanRepair(mech) && (forced || repair.autoRepair);
        }
    }

    [HarmonyPatch(typeof(WorkGiver_RepairMech), nameof(WorkGiver_RepairMech.JobOnThing))]
    static class WarframeRepairJob
    {
        static void Postfix(Thing t, ref Job __result)
        {
            if (t is Pawn_Immortal)
                __result = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("MM_RepairWarframe"), t);
        }
    }

    // Keep vanilla reservation, repair speed, crafting XP and health repair semantics.
    // Light upkeep remains handled by the chassis; it has no mech battery to drain.
    public sealed class JobDriver_RepairWarframe : JobDriver_RepairMech
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnForbidden(TargetIndex.A);
            this.FailOn(() => !(Mech is Pawn_Immortal) || Mech.Dead
                || Mech.Faction != pawn.Faction || Mech.InAggroMentalState
                || Mech.IsBurning() || Mech.IsAttacking());
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var repair = Toils_General.WaitWith(TargetIndex.A, int.MaxValue,
                useProgressBar: false, maintainPosture: true, maintainSleep: true);
            repair.WithEffect(EffecterDefOf.MechRepairing, TargetIndex.A);
            repair.PlaySustainerOrSound(SoundDefOf.RepairMech_Touch);
            repair.AddPreInitAction(() => ticksToNextRepair = TicksPerHeal);
            repair.handlingFacing = true;
            repair.tickIntervalAction = delta =>
            {
                ticksToNextRepair -= delta;
                if (ticksToNextRepair <= 0)
                {
                    MechRepairUtility.RepairTick(Mech);
                    ticksToNextRepair = TicksPerHeal;
                }
                pawn.rotationTracker.FaceTarget(Mech);
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.05f * delta);
            };
            repair.AddFinishAction(() =>
            {
                if (Mech.jobs?.curJob != null)
                    Mech.jobs.EndCurrentJob(JobCondition.InterruptForced);
            });
            repair.AddEndCondition(() => MechRepairUtility.CanRepair(Mech)
                ? JobCondition.Ongoing : JobCondition.Succeeded);
            repair.activeSkill = () => SkillDefOf.Crafting;
            yield return repair;
        }
    }
}
