using System;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Monolyn.Mecha.Multiplayer
{
    // This optional assembly owns only Mecha's commands. MP-Race-Compatibility-Plus
    // retains all native ASEL registration, serialization and visual Rand patches.
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Action<Pawn, Map, Map> fixTransferTimestamps;
        static System.Reflection.FieldInfo worldPawnRemoveTick;
        static System.Reflection.FieldInfo timestampOffset;
        static readonly AccessTools.FieldRef<Ability, int> cooldownEnd = AccessTools.FieldRefAccess<Ability, int>("cooldownEndTick");
        static readonly AccessTools.FieldRef<Ability, bool> inCooldown = AccessTools.FieldRefAccess<Ability, bool>("inCooldown");
        static Bootstrap()
        {
            if (!MP.enabled) return;
            int registered = 0;
            foreach (string action in new[] { "OrderBoard", "Eject", "Move", "Attack", "Stop", "ToggleFire", "ToggleCombat", "Teleport", "OrderLoad", "Unload" })
            {
                var method = AccessTools.DeclaredMethod(typeof(MechaActions), action)
                    ?? throw new MissingMethodException(typeof(MechaActions).FullName, action);
                // Thing/Map serialization chooses the owning map queue. Queue-order
                // context preserves shift-click movement without syncing local targeting UI.
                MP.RegisterSyncMethod(method).SetContext(SyncContext.QueueOrder_Down).CancelIfAnyArgNull();
                registered++;
            }
            MP.RegisterSyncMethod(typeof(CompStalker), nameof(CompStalker.SetAutoCombat)); registered++;
            MP.RegisterSyncMethod(typeof(CompStalker), nameof(CompStalker.SetAutoBlink)); registered++;
            MP.RegisterSyncMethod(typeof(CompGuardian), nameof(CompGuardian.SetAutoCombat)); registered++;
            MP.RegisterSyncMethod(typeof(CompGuardian), nameof(CompGuardian.SetAutoRestore)); registered++;
            MP.RegisterSyncMethod(typeof(Pawn_Reaver), nameof(Pawn_Reaver.ToggleFabrication)); registered++;
            MP.RegisterSyncMethod(typeof(CompGuardianField), nameof(CompGuardianField.Activate)); registered++;
            MP.RegisterSyncMethod(typeof(MechaAutonomy), nameof(MechaAutonomy.Bind)).CancelIfAnyArgNull(); registered++;
            MP.RegisterSyncMethod(typeof(MechaAutonomy), nameof(MechaAutonomy.Unbind)).CancelIfAnyArgNull(); registered++;
            MP.RegisterSyncMethod(typeof(MechaSelfDestruct), nameof(MechaSelfDestruct.Execute)).CancelIfAnyArgNull(); registered++;
            MP.RegisterSyncWorker<PilotHolder>(SyncPilotHolder);
            MP.RegisterSyncMethod(typeof(MechaRecall), nameof(MechaRecall.Execute)); registered++;
            var timestampFixer = AccessTools.Method("Multiplayer.Client.Patches.TimestampFixer:FixPawn",
                new[] { typeof(Pawn), typeof(Map), typeof(Map) })
                ?? throw new MissingMethodException("Multiplayer TimestampFixer.FixPawn");
            fixTransferTimestamps = (Action<Pawn, Map, Map>)Delegate.CreateDelegate(typeof(Action<Pawn, Map, Map>), timestampFixer);
            worldPawnRemoveTick = AccessTools.Field("Multiplayer.Client.MultiplayerPawnComp:worldPawnRemoveTick")
                ?? throw new MissingFieldException("MultiplayerPawnComp.worldPawnRemoveTick");
            timestampOffset = AccessTools.Field("Multiplayer.Client.Patches.TimestampFixer:currentOffset")
                ?? throw new MissingFieldException("TimestampFixer.currentOffset");
            var recallHarmony = new Harmony("local.monolyn.mecha.recall.mp");
            recallHarmony.Patch(
                AccessTools.Method(typeof(MechaRecall), nameof(MechaRecall.SpawnTransferred)),
                prefix: new HarmonyMethod(typeof(Bootstrap), nameof(FixTransferTimestamps)));
            recallHarmony.Patch(AccessTools.Method(typeof(Ability), nameof(Ability.ExposeData)),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(FixBlinkTimestamp)));
            Log.Message("[Monolyn Mecha MP] ready commands=" + registered + " pilotHolder=1 missing=0"
                + " meow=" + ModsConfig.IsActive("local.mp.meowonlineshop.sellslingshot")
                + " version=" + typeof(Bootstrap).Assembly.GetName().Version);
        }

        static void FixTransferTimestamps(Pawn pawn, Map source, Map destination)
        {
            if (MP.IsInMultiplayer && source != null && source != destination)
            {
                fixTransferTimestamps(pawn, source, destination);
                // A pawn can be recalled again on the same (possibly paused) world
                // tick it left a caravan. This is a map-to-map transfer, not another
                // world-to-map arrival: consume the old native arrival marker.
                foreach (var comp in pawn.AllComps)
                    if (worldPawnRemoveTick.DeclaringType.IsInstanceOfType(comp))
                    { worldPawnRemoveTick.SetValue(comp, -1); break; }
            }
        }

        static void FixBlinkTimestamp(Ability __instance)
        {
            // MP 0.11.5 only shifts four native pawn timestamps. Blink's absolute
            // cooldown must follow the same conversion, including caravan travel.
            if (__instance.def == StalkerDefOf.MM_StalkerBlink && inCooldown(__instance)
                && timestampOffset.GetValue(null) is int offset)
                cooldownEnd(__instance) += offset;
        }

        static void SyncPilotHolder(SyncWorker sync, ref PilotHolder holder)
        {
            Pawn_Immortal owner = sync.isWriting ? holder?.Owner : null;
            sync.Bind(ref owner);
            if (!sync.isWriting) holder = owner?.pilotHolder;
        }
    }
}
