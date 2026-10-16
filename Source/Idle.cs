using Verse;
using Verse.AI;
using RimWorld;
namespace Monolyn.Mecha
{
    // In 1.6, DeepCopy(false) preserves a resolved subtree's children/save keys but
    // not its subtreeNode pointer. Copying Mechanoid -> LordDuty -> AssaultColony
    // therefore leaves Abilities_Aggressive disconnected. Repair only our copy;
    // retain the existing nodes and keys instead of sharing a vanilla think tree.
    public sealed class ThinkNode_MechaSubtree : ThinkNode_Subtree
    {
        protected override void ResolveSubnodes()
        {
            base.ResolveSubnodes();
            foreach (var node in subtreeNode.ThisAndChildrenRecursive)
                // ResolveSubnodes appends the active root. A duty template can have
                // several expanded copies, so its active root is the last child.
                if (node is ThinkNode_Subtree subtree && subtree.subtreeNode == null && subtree.subNodes.Count > 0)
                    subtree.subtreeNode = subtree.subNodes[subtree.subNodes.Count - 1];
        }
    }

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
