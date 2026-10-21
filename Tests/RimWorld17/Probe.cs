using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MonolynMechaVersionTests;

[StaticConstructorOnStartup]
static class Entry
{
    static int frames;
    static Entry()
    {
        if (GenCommandLine.CommandLineArgPassed("mm17-worldbaseline"))
            new Harmony("local.monolyn.mecha.versionprobe.world").Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"),
                prefix: new HarmonyMethod(typeof(Entry), nameof(Push)), finalizer: new HarmonyMethod(typeof(Entry), nameof(Pop)));
        if (!GenCommandLine.CommandLineArgPassed("mm17-startup")) return;
        new Harmony("local.monolyn.mecha.versionprobe").Patch(AccessTools.Method(typeof(Root_Entry), "Update"),
            postfix: new HarmonyMethod(typeof(Entry), nameof(Update)));
        Log.Message("[MM17] STARTUP game=" + VersionControl.CurrentVersionStringWithRev);
    }
    static void Push() => Rand.PushState(261021);
    static void Pop() => Rand.PopState();
    static void Update()
    {
        if (LongEventHandler.AnyEventNowOrWaiting || Current.ProgramState != ProgramState.Entry || ++frames < 120) return;
        string root = GenFilePaths.SaveDataFolderPath;
        File.WriteAllLines(Path.Combine(root, "loaded-mods.txt"), LoadedModManager.RunningModsListForReading
            .Select(m => m.PackageId + "|" + m.RootDir));
        File.WriteAllText(Path.Combine(root, "complete"), "STARTUP_COMPLETE");
        Log.Message("[MM17] STARTUP_COMPLETE");
        Application.Quit();
    }
}

public sealed class WorldBaseline : GameComponent
{
    readonly bool active = GenCommandLine.CommandLineArgPassed("mm17-worldbaseline");
    int start = -1;
    bool complete;
    public WorldBaseline(Game game) { }
    public override void GameComponentUpdate()
    {
        if (!active || complete || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
        if (start < 0) start = Find.TickManager.TicksGame;
        Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        if (Find.TickManager.TicksGame - start < 1000) return;
        complete = true;
        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "complete"), "WORLD_BASELINE_1000_TICKS");
        Log.Message("[MM17] WORLD_BASELINE_1000_TICKS");
        Application.Quit();
    }
}
