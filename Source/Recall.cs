using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Monolyn.Mecha
{
    public static class MechaRecall
    {
        public static bool Applies(Pawn pawn) => pawn is Pawn_Immortal || pawn?.GetComp<CompStalker>() != null || pawn?.GetComp<CompGuardian>() != null;

        // Validate the whole chassis BEFORE GenSpawn rejects an out-of-bounds rect.
        // SpawnSetup is too late. No random draws, and the order is identical on peers.
        public static bool Clear(Pawn pawn, Map map, IntVec3 cell, Rot4 rotation)
        {
            if (map == null) return false;
            var rect = GenAdj.OccupiedRect(cell, rotation, pawn.def.size);
            if (!rect.InBounds(map)) return false;
            foreach (var c in rect)
            {
                if (!c.Standable(map) || c.GetEdifice(map) != null) return false;
                foreach (var t in c.GetThingList(map)) if (t is Pawn && t != pawn) return false;
            }
            return true;
        }

        public static IntVec3 Landing(Pawn pawn, Map map, IntVec3 center, bool everyRotation, bool wholeMap = false)
        {
            bool Valid(IntVec3 c)
            {
                if (!everyRotation) return Clear(pawn, map, c, pawn.Rotation);
                for (int r = 0; r < 4; r++) if (!Clear(pawn, map, c, new Rot4(r))) return false;
                return true;
            }
            foreach (var offset in GenRadial.RadialPattern)
            {
                if (offset.LengthHorizontal > (wholeMap ? 30 : 8)) break;
                var cell = center + offset;
                if (Valid(cell)) return cell;
            }
            if (wholeMap)
                foreach (var cell in map.AllCells) if (Valid(cell)) return cell;
            return IntVec3.Invalid;
        }

        public static float Fraction(Pawn pawn, Pawn owner) => pawn.Spawned && pawn.Map == owner.Map ? .3f : .5f;
        public static float Energy(Pawn pawn) => pawn is Pawn_Immortal w ? w.Light.currentLight / w.Light.maxLightP : pawn.needs?.energy?.CurLevelPercentage ?? 0;
        public static AcceptanceReport CanRecall(Pawn pawn, Pawn owner)
        {
            if (!Applies(pawn) || pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer
                || owner == null || owner.Dead || !owner.Spawned || owner.Faction != pawn.Faction
                || owner.mechanitor == null || pawn.GetOverseer() != owner)
                return "MM_RecallNoOwner".Translate();
            // Only map pawns, caravan members and orphaned world pawns are eligible.
            // Do not steal a pawn out of a transporter, cryptosleep casket or flyer.
            if ((!pawn.Spawned && pawn.GetCaravan() == null && (!pawn.IsWorldPawn() || pawn.ParentHolder != null))
                || (pawn is Pawn_Immortal w && (w.Pilot != null || w.MovementLocked)))
                return "MM_RecallUnavailable".Translate();
            if (Energy(pawn) + .000001f < Fraction(pawn, owner)) return "MM_RecallEnergy".Translate();
            return true;
        }

        // Integers deliberately keep this command on MP's global queue. Serializing
        // two Pawn/Map arguments on different maps causes a map-mismatch exception.
        public static void Execute(int pawnId, int ownerId)
        {
            var pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            Pawn pawn = pawns.FirstOrDefault(p => p.thingIDNumber == pawnId);
            Pawn owner = pawns.FirstOrDefault(p => p.thingIDNumber == ownerId);
            if (!CanRecall(pawn, owner)) return;
            Map map = owner.Map;
            IntVec3 cell = Landing(pawn, map, owner.Position, false);
            if (!cell.IsValid) { Messages.Message("MM_RecallNoCell".Translate(), owner, MessageTypeDefOf.RejectInput, false); return; }
            float cost = Fraction(pawn, owner);
            var caravan = pawn.GetCaravan();
            var warframe = pawn as Pawn_Immortal;
            pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
            pawn.jobs?.StopAll(); pawn.pather?.StopDead();
            warframe?.CancelWeapon();
            if (warframe != null) warframe.Combat.SetEnabled(warframe.Combat.Enabled);
            var stalker = pawn.GetComp<CompStalker>();
            if (stalker != null) stalker.SetAutoCombat(stalker.autoCombat);
            var guardian = pawn.GetComp<CompGuardian>();
            if (guardian != null)
            { guardian.StopChannels(); guardian.SetAutoCombat(guardian.autoCombat); guardian.SetAutoRestore(guardian.autoRestore); }
            warframe?.Receiver?.UnregisterFromLightDistributorsNoSound();
            if (pawn.Spawned && pawn.Map == map)
            {
                pawn.Position = cell;
                pawn.Notify_Teleported();
            }
            else
            {
                Map source = pawn.Map;
                bool drafted = pawn.Drafted;
                bool fireAtWill = pawn.drafter?.FireAtWill ?? true;
                if (pawn.Spawned) pawn.DeSpawn();
                // A bare PassToWorld treats the pawn as abandoned and changes its
                // faction. Move directly; the optional MP adapter converts clocks.
                SpawnTransferred(pawn, source, map, cell);
                pawn.Notify_Teleported();
                if (pawn.drafter != null)
                { pawn.drafter.FireAtWill = fireAtWill; if (drafted) pawn.drafter.Drafted = true; }
                if (caravan != null && caravan.PawnsListForReading.Count == 0 && !caravan.Destroyed) caravan.Destroy();
            }
            if (!pawn.Spawned || pawn.Map != map) return;
            if (warframe != null) { warframe.Light.UseLight(warframe.Light.maxLightP * cost); warframe.ReconnectLight(); }
            else pawn.needs.energy.CurLevel -= pawn.needs.energy.MaxLevel * cost;
        }

        public static void SpawnTransferred(Pawn pawn, Map source, Map destination, IntVec3 cell)
            => GenSpawn.Spawn(pawn, cell, destination, pawn.Rotation);

        sealed class Ui
        {
            internal readonly Command_Action recall, menu;
            internal Ui(Pawn pawn)
            {
                recall = new Command_Action { icon = ContentFinder<Texture2D>.Get("UI/Transfer"),
                    action = () => { var owner = pawn.GetOverseer(); if (owner != null) Execute(pawn.thingIDNumber, owner.thingIDNumber); } };
                menu = new Command_Action { icon = ContentFinder<Texture2D>.Get("UI/Transfer"), action = () => OpenMenu(pawn) };
            }
        }
        static readonly ConditionalWeakTable<Pawn, Ui> cache = new ConditionalWeakTable<Pawn, Ui>();
        public static Command_Action Command(Pawn pawn)
        {
            var c = cache.GetValue(pawn, p => new Ui(p)).recall;
            c.defaultLabel = "MM_Recall".Translate(); c.defaultDesc = "MM_RecallDesc".Translate();
            c.Disabled = false; c.disabledReason = null;
            var report = CanRecall(pawn, pawn.GetOverseer());
            if (!report) c.Disable(report.Reason);
            return c;
        }
        public static Command_Action Menu(Pawn owner)
        {
            var c = cache.GetValue(owner, p => new Ui(p)).menu;
            c.defaultLabel = "MM_RecallMenu".Translate(); c.defaultDesc = "MM_RecallDesc".Translate();
            return c;
        }
        static void OpenMenu(Pawn owner)
        {
            var options = new List<FloatMenuOption>();
            // Relations also retain orphaned pawns from the old caravan entry bug.
            foreach (var pawn in owner.relations.DirectRelations.Where(r => r.def == PawnRelationDefOf.Overseer)
                .Select(r => r.otherPawn).Where(Applies).OrderBy(p => p.thingIDNumber))
            {
                var report = CanRecall(pawn, owner);
                string label = pawn.LabelShortCap + " (" + (Fraction(pawn, owner) * 100).ToString("F0") + "%)";
                options.Add(new FloatMenuOption(report ? label : label + ": " + report.Reason,
                    report ? (Action)(() => Execute(pawn.thingIDNumber, owner.thingIDNumber)) : null));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("MM_RecallNone".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    static class MechaRecallGizmos
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;
            if (__instance.Faction != Faction.OfPlayer || __instance.Dead) yield break;
            if (MechaRecall.Applies(__instance)) yield return MechaRecall.Command(__instance);
            else if (__instance.Spawned && __instance.mechanitor != null) yield return MechaRecall.Menu(__instance);
        }
    }

    [HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter),
        new[] { typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool) })]
    static class MechaCaravanLanding
    {
        static void Prefix(Map map, ref Func<Pawn, IntVec3> spawnCellGetter)
        {
            var original = spawnCellGetter;
            spawnCellGetter = pawn =>
            {
                var proposed = original(pawn);
                if (!MechaRecall.Applies(pawn)) return proposed;
                var safe = MechaRecall.Landing(pawn, map, proposed, true, true);
                // Do not let vanilla silently clear the last holder of an unspawned
                // pawn when a completely obstructed destination has no safe landing.
                if (!safe.IsValid) throw new InvalidOperationException("Monolyn Mecha: no caravan landing for " + pawn.ThingID);
                return safe;
            };
        }
    }
}
