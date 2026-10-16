using Verse;
using Verse.AI;
using RimWorld;
namespace Monolyn.Mecha
{
    public sealed class JobGiver_ImmortalIdle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var job = JobMaker.MakeJob(JobDefOf.Wait);
            job.expiryInterval = 250;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
