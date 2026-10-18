using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Monolyn.Mecha
{
    public sealed class CompProperties_WarframeOverseer : CompProperties_OverseerSubject
    { public CompProperties_WarframeOverseer() { compClass = typeof(CompWarframeOverseer); } }

    // Control, bandwidth and groups use the real Overseer relation. An unbound
    // warframe remains a parked piloted vehicle, so it must not become feral.
    public sealed class CompWarframeOverseer : CompOverseerSubject
    {
        Command_Action bind, disconnect;
        LoadedLanguage language;
        public override void CompTick() { }
        public override string CompInspectStringExtra()
        {
            var owner = Parent.GetOverseer();
            return owner == null ? null : "MM_AutonomyStatus".Translate(owner.LabelShortCap,
                ((Pawn_Immortal)Parent).Autonomous ? "MM_AutonomyRunning".Translate() : "MM_AutonomyUnavailable".Translate()).ToString();
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!parent.Spawned || parent.Faction != Faction.OfPlayer || Parent.Dead) yield break;
            var mecha = (Pawn_Immortal)Parent;
            if (bind == null || language != LanguageDatabase.activeLanguage)
            {
                language = LanguageDatabase.activeLanguage;
                bind = new Command_Action { defaultLabel = "MM_Autonomy".Translate(),
                    defaultDesc = "MM_AutonomyDesc".Translate(mecha.GetStatValue(StatDefOf.BandwidthCost).ToString("F0")),
                    icon = parent.def.uiIcon, action = () => OpenMechanitors(mecha) };
                disconnect = new Command_Action { defaultLabel = "MM_AutonomyDisconnect".Translate(),
                    defaultDesc = "MM_AutonomyDisconnectDesc".Translate(), icon = TexCommand.ForbidOff,
                    action = () => MechaAutonomy.Unbind(mecha.Map, mecha) };
            }
            bind.Disabled = false; bind.disabledReason = null;
            if (mecha.Pilot != null) bind.Disable("MM_AutonomyEjectFirst".Translate());
            yield return bind;
            if (Parent.GetOverseer() != null) yield return disconnect;
        }
        static void OpenMechanitors(Pawn_Immortal mecha)
        {
            var options = new List<FloatMenuOption>();
            foreach (var mechanitor in mecha.Map.mapPawns.FreeColonistsSpawned.Where(MechanitorUtility.IsMechanitor).OrderBy(p => p.thingIDNumber))
            {
                Pawn selected = mechanitor;
                var report = MechaAutonomy.CanBind(mecha, selected);
                string label = selected.LabelShortCap + " (" + (selected.mechanitor.TotalBandwidth - selected.mechanitor.UsedBandwidth)
                    + "/" + selected.mechanitor.TotalBandwidth + ")";
                options.Add(report ? new FloatMenuOption(label, () => MechaAutonomy.Bind(mecha.Map, mecha, selected))
                    : new FloatMenuOption(label + " — " + report.Reason, null));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("MM_AutonomyNoMechanitor".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    public static class MechaAutonomy
    {
        public static bool Available(Pawn_Immortal mecha)
        {
            if (mecha.Pilot != null || mecha.Dead || mecha.Faction != Faction.OfPlayer) return false;
            Pawn owner = mecha.GetOverseer();
            return owner != null && !owner.Dead && owner.Faction == mecha.Faction && owner.mechanitor != null
                && owner.mechanitor.ControlledPawns.Contains(mecha) && owner.mechanitor.CanControlMechs
                && owner.MapHeld == mecha.MapHeld && !mecha.InMentalState;
        }
        public static bool Working(Pawn_Immortal mecha)
        {
            if (!Available(mecha)) return false;
            var mode = mecha.GetMechWorkMode();
            return mecha.Drafted || mode == MechWorkModeDefOf.Work || mode == MechWorkModeDefOf.Escort;
        }
        public static AcceptanceReport CanBind(Pawn_Immortal mecha, Pawn mechanitor)
        {
            if (mecha == null || !mecha.Spawned || mecha.Dead || mecha.Faction != Faction.OfPlayer
                || mechanitor == null || !mechanitor.Spawned || mechanitor.Map != mecha.Map || mechanitor.Faction != mecha.Faction
                || mechanitor.Dead || !MechanitorUtility.IsMechanitor(mechanitor)) return "MM_AutonomyUnavailable".Translate();
            if (mecha.Pilot != null) return "MM_AutonomyEjectFirst".Translate();
            if (!mechanitor.mechanitor.CanControlMechs) return mechanitor.mechanitor.CanControlMechs;
            if (mecha.GetOverseer() == mechanitor) return "MM_AutonomyAlreadyBound".Translate();
            if (mechanitor.mechanitor.TotalBandwidth - mechanitor.mechanitor.UsedBandwidth < mecha.GetStatValue(StatDefOf.BandwidthCost))
                return "MM_AutonomyBandwidth".Translate();
            return true;
        }
        public static void Bind(Map map, Pawn_Immortal mecha, Pawn mechanitor)
        {
            if (mecha == null || mecha.Map != map || !CanBind(mecha, mechanitor)) return;
            Release(mecha);
            mecha.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mechanitor);
            mechanitor.mechanitor.Notify_BandwidthChanged();
        }
        public static void Unbind(Map map, Pawn_Immortal mecha)
        {
            if (mecha == null || !mecha.Spawned || mecha.Map != map || mecha.Faction != Faction.OfPlayer) return;
            Release(mecha);
        }
        internal static void Release(Pawn_Immortal mecha)
        {
            Pawn owner = mecha.GetOverseer();
            if (owner == null) return;
            if (mecha.Drafted) mecha.drafter.Drafted = false;
            mecha.CancelWeapon();
            mecha.Combat.ResetMovement();
            mecha.jobs?.StopAll(); mecha.pather?.StopDead();
            mecha.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, owner);
            owner.mechanitor?.Notify_BandwidthChanged();
        }
        public static bool CanCommand(Pawn_Immortal mecha, IntVec3 cell)
        {
            if (mecha.Pilot != null) return mecha.Driven;
            Pawn owner = mecha.GetOverseer();
            return mecha.Autonomous && mecha.Drafted && owner.mechanitor.CanCommandTo(cell);
        }
    }

    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.CanControlMech))]
    static class WarframeNativeBinding
    {
        static bool Prefix(Pawn pawn, Pawn mech, ref AcceptanceReport __result)
        {
            if (!(mech is Pawn_Immortal warframe)) return true;
            __result = MechaAutonomy.CanBind(warframe, pawn);
            return false;
        }
    }
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.IsColonyMechRequiringMechanitor))]
    static class ParkedWarframeControlAlert
    {
        static void Postfix(Pawn mech, ref bool __result)
        {
            if (mech is Pawn_Immortal warframe && (warframe.Pilot != null || warframe.GetOverseer() == null)) __result = false;
        }
    }
    // Native group changes already synchronize in Multiplayer. Keep the existing
    // warframe think tree, which uses light and has no vanilla mech energy need.
    public sealed class JobGiver_WarframeEscort : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn is Pawn_Immortal mecha) || !mecha.Autonomous || mecha.Drafted || !mecha.Powered
                || mecha.GetMechWorkMode() != MechWorkModeDefOf.Escort) return null;
            Pawn owner = pawn.GetOverseer();
            if (owner == null || !owner.Spawned || owner.Map != pawn.Map || pawn.Position.InHorDistOf(owner.Position, 5f)) return null;
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(owner.Position, pawn.Map, 4,
                c => mecha.CanOccupy(c) && mecha.CanReach(c, PathEndMode.OnCell, Danger.Some));
            if (!cell.IsValid || cell == mecha.Position) return null;
            var job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            job.expiryInterval = 120; job.checkOverrideOnExpire = true;
            job.locomotionUrgency = LocomotionUrgency.Jog;
            return job;
        }
    }
}
