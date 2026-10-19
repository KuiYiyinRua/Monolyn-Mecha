using System.Collections.Generic;
using System.Linq;
using ASEL;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Monolyn.Mecha
{
    public static class MechaTowerTransit
    {
        public static bool HasController(Pawn pawn)
        {
            if (pawn is Pawn_Immortal warframe && warframe.Pilot != null)
                return !warframe.Pilot.Dead && !warframe.Pilot.Downed && !warframe.Pilot.InMentalState;
            var owner = pawn.GetOverseer();
            // The mechanitor may be on either side of the conduit. Requiring the
            // same map here would strand an otherwise bound chassis after arrival.
            return owner != null && !owner.Dead && !owner.Destroyed && owner.Faction == pawn.Faction
                && owner.mechanitor != null && owner.mechanitor.ControlledPawns.Contains(pawn);
        }

        public static AcceptanceReport CanEnter(Pawn pawn, Building_LightConduit portal)
        {
            if (!MechaRecall.Applies(pawn) || pawn.Dead || pawn.Destroyed || pawn.Downed || pawn.InMentalState
                || !pawn.Spawned || pawn.Faction != Faction.OfPlayer || portal == null || !portal.Spawned
                || pawn.Map != portal.Map || !portal.Useable) return "MM_TowerUnavailable".Translate();
            if (!HasController(pawn)) return "MM_TowerNeedsController".Translate();
            if (pawn is Pawn_Immortal warframe)
            {
                if (!warframe.Powered) return "MM_TowerNeedsPower".Translate();
                if (warframe.MovementLocked) return "MM_DisruptorLocked".Translate();
            }
            else if (pawn.needs?.energy == null || pawn.needs.energy.CurLevel <= 0f || pawn.IsSelfShutdown())
                return "MM_TowerNeedsPower".Translate();
            if (!portal.IsEnterable(out string reason)) return reason;
            return true;
        }

        public static bool Travelling(Pawn_Immortal pawn)
            => pawn.CurJobDef == JobDefOf.EnterPortal && pawn.CurJob.targetA.Thing is Building_LightConduit portal
                && CanEnter(pawn, portal).Accepted;

        internal static IEnumerable<Toil> Enter(JobDriver driver, Building_LightConduit portal)
        {
            driver.FailOnDespawnedOrNull(TargetIndex.A);
            driver.FailOn(() => !CanEnter(driver.pawn, portal).Accepted);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(90).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch)
                .WithProgressBarToilDelay(TargetIndex.A, interpolateBetweenActorAndTarget: true);
            yield return Toils_General.Do(() => Transfer(driver.pawn, portal));
        }

        internal static void Transfer(Pawn pawn, Building_LightConduit portal)
        {
            if (!CanEnter(pawn, portal).Accepted) return;
            var destination = portal.GetComp<CompLightConduit>().connectedConduit?.parent;
            var map = destination?.Map;
            // Even-sized footprints shift their anchor when turning. Reserve room
            // for every facing so idle rotation after arrival cannot clip the tower.
            var cell = destination == null ? IntVec3.Invalid : MechaRecall.Landing(pawn, map, destination.Position, true);
            // Validate before despawning. A blocked exit leaves the original pawn,
            // its pilot and all cargo safely on the source map for a later attempt.
            if (!cell.IsValid)
            {
                Messages.Message("MM_TowerNoCell".Translate(), portal, MessageTypeDefOf.RejectInput, false);
                return;
            }
            var source = pawn.Map;
            bool drafted = pawn.Drafted, fireAtWill = pawn.drafter?.FireAtWill ?? true;
            var warframe = pawn as Pawn_Immortal;
            pawn.pather.StopDead();
            pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
            warframe?.CancelWeapon();
            // Scan deadlines belong to the source map's clock. Keep the player's
            // toggles, but clear cached targets and timers before changing maps.
            if (warframe != null) warframe.Combat.SetEnabled(warframe.Combat.Enabled);
            var stalker = pawn.GetComp<CompStalker>();
            if (stalker != null) stalker.SetAutoCombat(stalker.autoCombat);
            var guardian = pawn.GetComp<CompGuardian>();
            if (guardian != null)
            { guardian.StopChannels(); guardian.SetAutoCombat(guardian.autoCombat); guardian.SetAutoRestore(guardian.autoRestore); }
            warframe?.Receiver?.UnregisterFromLightDistributorsNoSound();
            pawn.DeSpawnOrDeselect();
            // Reuse the MP-aware map transfer boundary, including nested pilots.
            MechaRecall.SpawnTransferred(pawn, source, map, cell);
            pawn.Notify_Teleported();
            portal.OnEntered(pawn);
            if (pawn.drafter != null)
            { pawn.drafter.FireAtWill = fireAtWill; if (drafted) pawn.drafter.Drafted = true; }
            pawn.mindState.priorityWork.ClearPrioritizedWorkAndJobQueue();
            warframe?.ReconnectLight();
            var effect = DefDatabase<EffecterDef>.GetNamed("BulletTransferB").Spawn();
            effect.Trigger(new TargetInfo(cell, map), new TargetInfo(cell, map));
            effect.Cleanup();
        }
    }

    [HarmonyPatch(typeof(Dialog_EnterPortal), "AddToTransferables")]
    static class MechaTowerCandidates
    {
        static bool Prefix(Thing t, MapPortal ___portal)
            => !(___portal is Building_LightConduit portal) || !(t is Pawn pawn) || !MechaRecall.Applies(pawn)
                || MechaTowerTransit.CanEnter(pawn, portal).Accepted;
    }

    [HarmonyPatch(typeof(Dialog_EnterPortal), "TryAccept")]
    static class MechaTowerAccept
    {
        [HarmonyPriority(Priority.Last)]
        static bool Prefix(MapPortal ___portal, List<TransferableOneWay> ___transferables, ref bool __result)
        {
            if (!(___portal is Building_LightConduit portal)) return true;
            foreach (var pawn in TransferableUtility.GetPawnsFromTransferables(___transferables).Where(MechaRecall.Applies))
            {
                var report = MechaTowerTransit.CanEnter(pawn, portal);
                if (report.Accepted) continue;
                Messages.Message(pawn.LabelShortCap + ": " + report.Reason, pawn, MessageTypeDefOf.RejectInput, false);
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(EnterPortalUtility), nameof(EnterPortalUtility.MakeLordsAsAppropriate))]
    static class MechaTowerLord
    {
        static void Postfix(List<Pawn> pawns, MapPortal portal)
        {
            if (!(portal is Building_LightConduit conduit)) return;
            var units = pawns.Where(p => MechaRecall.Applies(p) && MechaTowerTransit.CanEnter(p, conduit).Accepted).ToList();
            if (units.Count == 0) return;
            var lord = portal.Map.lordManager.lords.FirstOrDefault(l => l.LordJob is LordJob_LoadAndEnterPortal job && job.portal == portal)
                ?? LordMaker.MakeNewLord(Faction.OfPlayer, new LordJob_LoadAndEnterPortal(portal), portal.Map);
            foreach (var pawn in units)
            {
                if (lord.ownedPawns.Contains(pawn)) continue;
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                lord.AddPawn(pawn);
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_EnterPortal), "MakeNewToils")]
    static class MechaTowerJob
    {
        static bool Prefix(JobDriver_EnterPortal __instance, ref IEnumerable<Toil> __result)
        {
            if (!MechaRecall.Applies(__instance.pawn) || !(__instance.MapPortal is Building_LightConduit portal)) return true;
            __result = MechaTowerTransit.Enter(__instance, portal);
            return false;
        }
    }
}
