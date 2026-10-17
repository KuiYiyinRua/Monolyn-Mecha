using System;
using System.IO;
using System.Linq;
using ASEL;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using UnityEngine;
using Verse;

namespace LightLineTests
{
    public sealed class Probe : GameComponent
    {
        readonly bool active = GenCommandLine.CommandLineArgPassed("mm-light-lines-test");
        string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        bool baseline => Root.Contains("Before-");
        int frames;
        bool done;
        public Probe(Game game) { }
        void Check(bool ok, string text)
        {
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), (ok ? "PASS " : "FAIL ") + text + "\n");
            if (!ok) throw new Exception(text);
        }
        public override void GameComponentUpdate()
        {
            if (!active || done || Current.ProgramState != ProgramState.Playing) return;
            try
            {
                foreach (var w in Find.WindowStack.Windows.Where(w => w.forcePause).ToArray()) w.Close();
                if (frames++ == 0) Setup();
                if (frames == 15) ScreenCapture.CaptureScreenshot(Path.Combine(Root, "selected-light-network.png"));
                if (frames > 30 && File.Exists(Path.Combine(Root, "selected-light-network.png")))
                {
                    done = true; File.WriteAllText(Path.Combine(Root, "complete"), "PASS light materials and isolation"); Application.Quit();
                }
            }
            catch (Exception e) { done = true; File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Application.Quit(); }
        }
        void Setup()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            Check(UnityData.IsInMainThread, "probe runs on main thread");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var file = File.OpenRead(typeof(Pawn_Immortal).Assembly.Location))
                Check(BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(), "exact loaded candidate");
            foreach (string name in new[] { "LineMatWhite", "ArrowMat", "DotMat", "LineMatRed", "ArrowMatRed" })
            {
                var mat = (Material)AccessTools.Field(typeof(CompLightDistributor), name).GetValue(null);
                Check(baseline ? mat == null : mat != null && mat.mainTexture != null && mat.shader != null && mat.shader.isSupported,
                    name + (baseline ? " reproduces null material" : " has valid texture and supported shader"));
            }
            var map = Find.CurrentMap; map.fogGrid.ClearAllFog();
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray()) p.DeSpawn();
            foreach (var c in new CellRect(40, 40, 35, 35))
            {
                foreach (var t in c.GetThingList(map).ToArray()) { if (t.def.destroyable) t.Destroy(); else t.DeSpawn(); }
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            }
            var tower = (ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("TowerOfLight")); tower.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(tower, new IntVec3(50, 0, 55), map);
            var player = (Pawn_Immortal)PawnGenerator.GeneratePawn(PawnKindDef.Named("MM_Immortal"), Faction.OfPlayer);
            GenSpawn.Spawn(player, new IntVec3(59, 0, 55), map); player.Light.currentLight = 0;
            var enemy = (Pawn_Immortal)PawnGenerator.GeneratePawn(PawnKindDef.Named("MM_Immortal"), Faction.OfMechanoids);
            GenSpawn.Spawn(enemy, new IntVec3(60, 0, 60), map); enemy.Light.currentLight = 0;
            var distributor = tower.GetComp<CompLightDistributor>();
            distributor.RegisterReceiverNoSound(player.Receiver);
            Check(distributor.registeredReceivers.Contains(player), "player can register with light grid");
            ASELComponent.currentLight = 10000;
            var transfer = AccessTools.Method(typeof(CompLightDistributor), "TransferLight");
            transfer.Invoke(distributor, new object[] { 50f, player.Receiver });
            Check(player.Light.currentLight > 0 && ASELComponent.currentLight == 9950, "player receives actual light transfer");
            distributor.RegisterReceiver(enemy.Receiver); distributor.RegisterReceiverNoSound(enemy.Receiver);
            enemy.Receiver.AddDistributor(distributor);
            transfer.Invoke(distributor, new object[] { 50f, enemy.Receiver });
            Check(!distributor.registeredReceivers.Contains(enemy) && enemy.Receiver.cachedLightDistributors.Count == 0
                && enemy.Light.currentLight == 0 && ASELComponent.currentLight == 9950, "enemy registration and transfer remain blocked");
            foreach (var name in new[] { "RegisterReceiver", "RegisterReceiverNoSound", "TransferLight" })
                Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(CompLightDistributor), name)).Prefixes.Count(p => p.owner == "local.monolyn.mecha") == 1,
                    name + " has exactly one isolation prefix");
            enemy.DeSpawn();
            Find.Selector.ClearSelection(); Find.Selector.Select(tower);
            Find.CameraDriver.SetRootPosAndSize(new Vector3(54.5f, 0, 55.5f), 16f);
        }
    }
}
