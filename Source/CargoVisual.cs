using HarmonyLib;
using Verse;

namespace Monolyn.Mecha
{
    // Warframes carry cargo inside their hull; the animal render tree must not load a "Pack" saddle.
    [HarmonyPatch(typeof(PawnRenderNode_AnimalPack), nameof(PawnRenderNode_AnimalPack.GraphicFor))]
    static class InternalCargoGraphic
    {
        static bool Prefix(Pawn pawn, ref Graphic __result)
        {
            if (!(pawn is Pawn_Immortal)) return true;
            __result = null;
            return false;
        }
    }
}
