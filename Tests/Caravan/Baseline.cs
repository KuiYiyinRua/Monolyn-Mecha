using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CaravanTests
{
    public static class Selection
    {
        public static List<Pawn> Rows(TransferableOneWayWidget widget, string title = null)
        {
            var rows = new List<Pawn>();
            foreach (var section in (IEnumerable)AccessTools.Field(typeof(TransferableOneWayWidget), "sections").GetValue(widget))
            {
                var type = section.GetType();
                if (title != null && (string)AccessTools.Field(type, "title").GetValue(section) != title) continue;
                foreach (var tr in (IEnumerable<TransferableOneWay>)AccessTools.Field(type, "transferables").GetValue(section))
                    if (tr.AnyThing is Pawn p) rows.Add(p);
            }
            return rows;
        }
        public static TransferableOneWayWidget Widget(List<TransferableOneWay> transferables)
        {
            var widget = new TransferableOneWayWidget(null, null, null, null);
            CaravanUIUtility.AddPawnsSections(widget, transferables);
            return widget;
        }
    }
    public sealed class Baseline : GameComponent
    {
        bool done;
        public Baseline(Game game) { }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("mm-baseline") || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            done = true;
            string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
            try
            {
                Map map = Find.CurrentMap;
                var units = new List<Pawn_Immortal>();
                foreach (var name in new[] { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" })
                {
                    var p = (Pawn_Immortal)PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(name), Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                    GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(map.Center, map, 16), map); units.Add(p);
                }
                foreach (bool piloted in new[] { false, true })
                {
                    foreach (var p in units)
                        if (piloted)
                        {
                            var pilot = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                            p.pilots.TryAdd(pilot);
                        }
                    var sendable = Dialog_FormCaravan.AllSendablePawns(map, false);
                    var transferables = sendable.Select(p => { var t = new TransferableOneWay(); t.things.Add(p); return t; }).ToList();
                    var rows = Selection.Rows(Selection.Widget(transferables));
                    if (!units.All(sendable.Contains) || units.Any(rows.Contains)) throw new Exception("Expected sendable but invisible warframes");
                    Log.Message("CARAVAN BASELINE piloted=" + piloted + " sendable=4 visible=0 cargo=" + string.Join(",", units.Select(p => MassUtility.Capacity(p))));
                }
                File.WriteAllText(Path.Combine(root, "baseline.complete"), "REPRODUCED: four unbound and four piloted warframes are sendable but absent from every visible section.");
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(root, "host.failed"), e.ToString()); Log.Error("CARAVAN FAILED " + e); }
            Application.Quit();
        }
    }
}
