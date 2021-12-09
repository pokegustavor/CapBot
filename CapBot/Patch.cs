using PulsarModLoader;
using HarmonyLib;

namespace CapBot
{
    [HarmonyPatch(typeof(PLPlayer), "UpdateAIPriorities")]
    class Patch
    {
        static void Postfix(PLPlayer __instance) 
        {
            if (__instance.GetPawn() == null || !__instance.IsBot || __instance.GetClassID() != 0) return;
            if(__instance.StartingShip != null && __instance.StartingShip.MyStats.GetShipComponent<PLCaptainsChair>(ESlotType.E_COMP_CAPTAINS_CHAIR,false) != null) 
            {
                if ((__instance.StartingShip.CaptainsChairPivot.position - __instance.GetPawn().transform.position).sqrMagnitude > 16)
                {
                    __instance.MyBot.AI_TargetPos = __instance.StartingShip.CaptainsChairPivot.position;
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    __instance.MyBot.EnablePathing = true;
                }
                else if(__instance.StartingShip.CaptainsChairPlayerID != __instance.GetPlayerID())
                {
                    __instance.StartingShip.AttemptToSitInCaptainsChair(__instance.GetPlayerID());
                }
            }
        }
    }
}
