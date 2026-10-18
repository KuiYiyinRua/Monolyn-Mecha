using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Monolyn.Mecha.Multiplayer
{
    // This optional assembly owns only Mecha's commands. MP-Race-Compatibility-Plus
    // retains all native ASEL registration, serialization and visual Rand patches.
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
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
            Log.Message("[Monolyn Mecha MP] ready commands=" + registered + " pilotHolder=1 missing=0"
                + " meow=" + ModsConfig.IsActive("local.mp.meowonlineshop.sellslingshot")
                + " version=" + typeof(Bootstrap).Assembly.GetName().Version);
        }

        static void SyncPilotHolder(SyncWorker sync, ref PilotHolder holder)
        {
            Pawn_Immortal owner = sync.isWriting ? holder?.Owner : null;
            sync.Bind(ref owner);
            if (!sync.isWriting) holder = owner?.pilotHolder;
        }
    }
}
