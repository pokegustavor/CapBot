using PulsarModLoader;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace CapBot
{
    [HarmonyPatch(typeof(PLPlayer), "UpdateAIPriorities")]
    class Patch
    {
        static void Postfix(PLPlayer __instance)
        {
            if (__instance.GetPawn() == null || !__instance.IsBot || __instance.GetClassID() != 0) return;
            if(__instance.StartingShip != null && __instance.StartingShip.ShipTypeID == EShipType.E_POLYTECH_SHIP && __instance.RaceID != 2) 
            {
                __instance.RaceID = 2;
            }
            if (__instance.StartingShip != null && __instance.StartingShip.InWarp && PLServer.Instance.AllPlayersLoaded()) //Skip warp
                PLInGameUI.Instance.WarpSkipButtonClicked();
            if (__instance.MyBot.AI_TargetPos != __instance.StartingShip.CaptainsChairPivot.position && __instance.StartingShip.CaptainsChairPlayerID == __instance.GetPlayerID())// leave chair
            {
                __instance.StartingShip.AttemptToSitInCaptainsChair(-1);
            }
            if (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.TOPSEC)//Inside the colony 
            {
                AtColony(__instance);
                return;
            }
            if (__instance.StartingShip != null && __instance.StartingShip.MyStats.GetShipComponent<PLCaptainsChair>(ESlotType.E_COMP_CAPTAINS_CHAIR, false) != null) //Sit in chair
            {
                __instance.MyBot.AI_TargetPos = __instance.StartingShip.CaptainsChairPivot.position;
                __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                if ((__instance.StartingShip.CaptainsChairPivot.position - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                {
                    __instance.MyBot.EnablePathing = true;
                }
                else
                {
                    if (__instance.StartingShip.CaptainsChairPlayerID != __instance.GetPlayerID())
                    {
                        __instance.StartingShip.AttemptToSitInCaptainsChair(__instance.GetPlayerID());
                    }
                }

            }



            
        }

        static float LastDestiny = Time.time;
        static void AtColony(PLPlayer CapBot) 
        {
            PLBot AI = CapBot.MyBot;
            PLPawn pawn = CapBot.GetPawn();
            PLTeleportationLocationInstance planet = null;
            foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
            {
                if(teleport.name == "PLGamePlanet") 
                {
                    planet = teleport;
                    break;
                }
            }
            if (planet == null || pawn == null) return;
            AI.AI_TargetTLI = planet;
            List<Vector3> possibleTargets;
            PLContainmentSystem colonyDoor = Object.FindObjectOfType(typeof(PLContainmentSystem)) as PLContainmentSystem;
            if (!PLServer.AnyPlayerHasItemOfName("Facility Keycard")) //Step 1: Find facility key
            {
                if(Time.time - LastDestiny > 10f) 
                {
                    possibleTargets = new List<Vector3>()
                    {
                        new Vector3(1025,-516,476),
                        new Vector3(1054,-516,483),
                        new Vector3(1042,-515,447),
                        new Vector3(1014,-515,444),
                        new Vector3(972,-517,461),
                        new Vector3(1062,-512,508),
                        new Vector3(1019,-515,523),
                        new Vector3(1001,-515,443),
                        new Vector3(988,-517,494),
                    };
                    AI.AI_TargetPos = possibleTargets[Random.Range(0, possibleTargets.Count - 1)];
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    LastDestiny = Time.time;
                }
            }
            else if(!PLServer.AnyPlayerHasItemOfName("Lower Facilities Keycard")) //Step 2: Find lower facility key
            {
                if (Time.time - LastDestiny > 25f)
                {
                    possibleTargets = new List<Vector3>()
                    {
                        new Vector3(941,-497,468),
                        new Vector3(946,-517,503),
                        new Vector3(964,-517,528),
                        new Vector3(984,-517,505),
                        new Vector3(975,-517,484),
                        new Vector3(946,-511,506),
                        new Vector3(921,-514,519),
                        new Vector3(943,-498,499),
                        new Vector3(962,-499,500),
                        new Vector3(961,-499,521),
                        new Vector3(920,-503,503),
                        new Vector3(954,-481,515),
                        new Vector3(952,-499,495),
                        new Vector3(966,-511,526),
                        new Vector3(963,-505,562),
                    };
                    AI.AI_TargetPos = possibleTargets[Random.Range(0, possibleTargets.Count - 1)];
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    LastDestiny = Time.time;
                }
            }
            else if(colonyDoor != null && !colonyDoor.GetHasBeenCompleted()) //Step 3: Fix errors at locked door
            {
                AI.AI_TargetPos = new Vector3(954,-534,511);
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                if((pawn.transform.position - AI.AI_TargetPos).sqrMagnitude < 4 && !colonyDoor.HasStarted) 
                {
                    colonyDoor.SetHasStarted();
                }
                if(Time.time - LastDestiny > 30 && colonyDoor.HasStarted) 
                {
                    PulsarModLoader.Utilities.Messaging.ChatMessage(PhotonTargets.All, "Need fixing:", CapBot.GetPlayerID());
                    foreach(ContainmentSystemParameter parameter in colonyDoor.Parameters) 
                    {
                        if (!parameter.GoalAndTargetMatch()) 
                        {
                            PulsarModLoader.Utilities.Messaging.ChatMessage(PhotonTargets.All, parameter.GetStringCategoryName() + ": " + parameter.GetStringDisplayName() + ": " + parameter.GetString_GoalValue(), CapBot.GetPlayerID());
                        }
                    }
                    LastDestiny = Time.time;
                }
            }
            else if (!colonyDoor.ContainmentDoor.GetIsOpen()) //Step 4: Open the door
            {
                colonyDoor.OpenContainmentDoorNow();
                LastDestiny = Time.time;
            }
            else if(PLLCChair.Instance != null && !PLLCChair.Instance.Triggered && PLLCChair.Instance.GetNumErrors(true) > 0) //Step 5: Fix screen erros at final door
            {
                AI.Tick_HelpWithChairSyncMiniGame(true);
                LastDestiny = Time.time;
            }
            else if(PLLCChair.Instance != null && PLLCChair.Instance.GetNumErrors(true) <= 0 && PLLCChair.Instance.PlayerIDInChair != CapBot.GetPlayerID() && !PLLCChair.Instance.Triggered_LevelThree) //Step 6: Sit in the chair
            {
                AI.AI_TargetPos = PLLCChair.Instance.gameObject.transform.position;
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                if ((pawn.transform.position - AI.AI_TargetPos).sqrMagnitude < 8 && Time.time - LastDestiny > 60)
                {
                    PLLCChair.Instance.SetPlayerIDInChair(CapBot.GetPlayerID());
                    PLLCChair.Instance.Triggered = true;
                    PLLCChair.Instance.Triggered_LevelTwo = true;
                }
            }
            else if(PLLCChair.Instance != null && PLLCChair.Instance.Triggered_LevelTwo && PLLCChair.Instance.PlayerIDInChair == CapBot.GetPlayerID() && !PLLCChair.Instance.Triggered_LevelThree) //Step 7: Do the minigame
            {
                if(Time.time - LastDestiny > 30 && PLLCChairUI.Instance != null) 
                {
                    if(Random.Range(0,9) != 0) 
                    {
                        PLLCChair.Instance.photonView.RPC("SetUICurrentLayer", PhotonTargets.All, new object[]
                        {
                        PLLCChairUI.Instance.CurrentLayer+1
                        });
                    }
                    else 
                    {
                        PLLCChair.Instance.photonView.RPC("SetUICurrentLayer", PhotonTargets.All, new object[]
                            {
                        PLLCChairUI.Instance.CurrentLayer > 0 ? PLLCChairUI.Instance.CurrentLayer-1 : 0
                            });
                    }
                    LastDestiny = Time.time;
                }
            }
            else if(PLLCChair.Instance != null && PLLCChair.Instance.Triggered_LevelThree && PLLCChair.Instance.PlayerIDInChair == CapBot.GetPlayerID()) //Step 8: You keep control over the infected for yourselfs
            {
                PLLCChair.Instance.StartKeepItEnding();
                PLLCChair.Instance.SetPlayerIDInChair(-1);
            }
            foreach (PLPickupObject item in Object.FindObjectsOfType(typeof(PLPickupObject)))
            {
                if ((item.transform.position - pawn.transform.position).sqrMagnitude < 16 && item.GetItemName(true).Contains("Keycard") && !item.PickedUp)
                {
                    AI.AI_TargetPos = item.transform.position;
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    PLMusic.PostEvent("play_sx_player_item_pickup", pawn.gameObject);
                    pawn.photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                    CapBot.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                    {
                            item.PickupID
                    });
                }
            }
        }
    }

    [HarmonyPatch(typeof(PLController), "Update")]
    class SitInChair
    {
        static void Postfix(PLController __instance)
        {
            if (__instance.MyPawn == null || __instance.MyPawn.MyPlayer == null || !__instance.MyPawn.MyPlayer.IsBot || __instance.MyPawn.MyPlayer.GetClassID() != 0 || __instance.MyPawn.MyPlayer.StartingShip == null) return;
            if (__instance.MyPawn.MyPlayer.StartingShip.CaptainsChairPlayerID == __instance.MyPawn.MyPlayer.GetPlayerID())
            {
                PLCaptainsChair shipComponent = __instance.MyPawn.MyPlayer.StartingShip.MyStats.GetShipComponent<PLCaptainsChair>(ESlotType.E_COMP_CAPTAINS_CHAIR, false);
                if (shipComponent != null && shipComponent.MyInstance != null && shipComponent.MyInstance.MalePawnPivot != null)
                {
                    __instance.MyPawn.MyPlayer.GetPawn().transform.position = shipComponent.MyInstance.MalePawnPivot.position;
                    shipComponent.MyInstance.Rot.transform.rotation = __instance.MyPawn.MyPlayer.GetPawn().HorizontalMouseLook.transform.rotation;
                }
            }
            else if(PLLCChair.Instance != null && PLLCChair.Instance.PlayerIDInChair == __instance.MyPawn.GetPlayer().GetPlayerID()) 
            {
                __instance.MyPawn.transform.position = PLLCChair.Instance.transform.TransformPoint(PLLCChair.Instance.Offset_RootAnimPos);
                __instance.MyPawn.VerticalMouseLook.RotationY = 35f;
                __instance.MyPawn.HorizontalMouseLook.RotationX = PLLCChair.Instance.transform.rotation.eulerAngles.y;
            }
        }
    }

    [HarmonyPatch(typeof(PLUIClassSelectionMenu), "Update")]
    class SpawnBot
    {
        public static float delay = 0f;
        static void Postfix()
        {
            if (PLEncounterManager.Instance.PlayerShip != null && PLServer.Instance.GetCachedFriendlyPlayerOfClass(0, PLEncounterManager.Instance.PlayerShip) == null && delay > 3f)
            {
                PLServer.Instance.ServerAddCrewBotPlayer(0);
                PLServer.Instance.GameHasStarted = true;
                PLServer.Instance.CrewPurchaseLimitsEnabled = false;
            }
            else if (PLEncounterManager.Instance.PlayerShip != null && PLServer.Instance.GetCachedFriendlyPlayerOfClass(0, PLEncounterManager.Instance.PlayerShip) == null) delay += Time.deltaTime;
        }
    }

    [HarmonyPatch(typeof(PLGlobal), "EnterNewGame")]
    class OnJoin
    {
        static void Postfix()
        {
            SpawnBot.delay = 0f;
        }
    }
}
