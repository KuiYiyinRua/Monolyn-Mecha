using HarmonyLib;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    // Render the actual core direction, then depth-sort separate orbiting armor modules around it.
    // No combat state, random calls or serialized phase belongs in the renderer.
    [StaticConstructorOnStartup]
    public static class DisruptorVisual
    {
        static readonly Material[] cores = {
            MaterialPool.MatFrom("Things/Mecha/Disruptor/Disruptor_north", ShaderDatabase.Cutout),
            MaterialPool.MatFrom("Things/Mecha/Disruptor/Disruptor_east", ShaderDatabase.Cutout),
            MaterialPool.MatFrom("Things/Mecha/Disruptor/Disruptor_south", ShaderDatabase.Cutout),
            MaterialPool.MatFrom("Things/Mecha/Disruptor/Disruptor_east", ShaderDatabase.Cutout)};
        static readonly Material inner = MaterialPool.MatFrom("Things/Mecha/Disruptor/PetalInner", ShaderDatabase.Cutout);
        static readonly Material outer = MaterialPool.MatFrom("Things/Mecha/Disruptor/PetalOuter", ShaderDatabase.Cutout);

        public static void Draw(Pawn_Disruptor unit, Vector3 center)
        {
            center.y = AltitudeLayer.Pawn.AltitudeFor();
            var size = DisruptorSpriteGeometry.DrawSize(unit.Rotation);
            DrawPlane(cores[unit.Rotation.AsInt], center + DisruptorSpriteGeometry.Offset(unit.Rotation), size, 0, unit.Rotation == Rot4.West);
            if (!unit.PetalsVisible) return;
            for (int i = 0; i < 3; i++)
            {
                // The armor orbits continuously in world space, independently of path-facing changes.
                float angle = (unit.rotationPhase + i * 120) * Mathf.Deg2Rad;
                float depth = Mathf.Cos(angle), lateral = Mathf.Sin(angle);
                var at = center + new Vector3(lateral * .64f, depth > 0 ? -.02f : .02f, depth * .48f + .09f);
                // The near/inside and far/outside artwork alternate as a solid petal turns around the core.
                // Foreshortening retains an edge-width floor so the thick armor never becomes a zero-width card.
                float perspective = .90f - depth * .10f;
                // First enlarge both axes 1.3x; the user's follow-up widens only the current petal another 1.3x.
                var scale = new Vector2(.85f * 1.30f * (.38f + .62f * Mathf.Abs(depth)), 1.2f) * (perspective * 1.30f);
                DrawPlane(depth < 0 ? inner : outer, at, scale, lateral * -24, lateral < 0);
            }
        }

        static void DrawPlane(Material material, Vector3 position, Vector2 size, float angle, bool flip)
        {
            var matrix = Matrix4x4.TRS(position, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(size.x, 1, size.y));
            Graphics.DrawMesh(flip ? MeshPool.plane10Flip : MeshPool.plane10, matrix, material, 0);
        }
    }
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.CanDrawNow))]
    static class DisruptorLayeredBody
    {
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref bool __result)
        {
            if (node is PawnRenderNode_AnimalPart_Body && parms.pawn is Pawn_Disruptor unit && unit.Spawned
                && !unit.Dead && !parms.flags.FlagSet(PawnRenderFlags.Portrait)) __result = false;
        }
    }
}
