using PulsarModLoader;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
namespace CapBot
{
    [HarmonyPatch(typeof(PLPlayer), "UpdateAIPriorities")]
    class Patch
    {
        static float LastAction = 0;
        static float LastMapUpdate = Time.time;
        static float LastBlindJump = 0;
        static float WeaponsTest = Time.time;
        static void Postfix(PLPlayer __instance)
        {
            if ((__instance.cachedAIData == null || __instance.cachedAIData.Priorities.Count == 0) && SpawnBot.capisbot && __instance.TeamID == 0 && __instance.IsBot) //Give default AI priorities
            {
                if (__instance.cachedAIData == null) PulsarModLoader.Utilities.Messaging.Notification("Null value!");
                if (__instance.cachedAIData != null) PulsarModLoader.Utilities.Messaging.Notification("Priorities value: " + __instance.cachedAIData.Priorities.Count);
                PulsarModLoader.Utilities.Messaging.Notification("Name: " + __instance.cachedAIData.Priorities.Count);
                if (__instance.cachedAIData == null) __instance.cachedAIData = new AIDataIndividual();
                PLGlobal.Instance.SetupClassDefaultData(ref __instance.cachedAIData, __instance.GetClassID(), false);
            }
            if (__instance.GetPawn() == null || !__instance.IsBot || __instance.GetClassID() != 0 || __instance.TeamID != 0 || !PhotonNetwork.isMasterClient || __instance.StartingShip == null) return;
            if (__instance.StartingShip != null && __instance.StartingShip.ShipTypeID == EShipType.E_POLYTECH_SHIP && __instance.RaceID != 2) //set race to robot in paladin
            {
                __instance.RaceID = 2;
            }
            if (__instance.StartingShip != null && __instance.StartingShip.InWarp && PLServer.Instance.AllPlayersLoaded() && (__instance.StartingShip.MyShieldGenerator == null || __instance.StartingShip.MyStats.ShieldsCurrent / __instance.StartingShip.MyStats.ShieldsMax > 0.99)) //Skip warp
                PLInGameUI.Instance.WarpSkipButtonClicked();
            if (__instance.MyBot.AI_TargetPos != __instance.StartingShip.CaptainsChairPivot.position && __instance.StartingShip.CaptainsChairPlayerID == __instance.GetPlayerID())//leave chair
            {
                __instance.StartingShip.AttemptToSitInCaptainsChair(-1);
            }
            if (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.TOPSEC)//Inside the colony 
            {
                AtColony(__instance);
                return;
            }
            if (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.LCWBATTLE)//In the warp guardian battle 
            {
                WarpGuardianBattle(__instance);
                return;
            }
            PLSectorInfo sector = PLServer.GetCurrentSector();
            if (sector.VisualIndication == ESectorVisualIndication.GENERAL_STORE || sector.VisualIndication == ESectorVisualIndication.EXOTIC1 || sector.VisualIndication == ESectorVisualIndication.EXOTIC2 || sector.VisualIndication == ESectorVisualIndication.EXOTIC3 || sector.VisualIndication == ESectorVisualIndication.EXOTIC4
                        || sector.VisualIndication == ESectorVisualIndication.EXOTIC5 || sector.VisualIndication == ESectorVisualIndication.EXOTIC6 || sector.VisualIndication == ESectorVisualIndication.EXOTIC7 || sector.VisualIndication == ESectorVisualIndication.AOG_HUB || sector.VisualIndication == ESectorVisualIndication.GENTLEMEN_START || sector.VisualIndication == ESectorVisualIndication.CORNELIA_HUB
                        || sector.VisualIndication == ESectorVisualIndication.COLONIAL_HUB || sector.VisualIndication == ESectorVisualIndication.WD_START || sector.VisualIndication == ESectorVisualIndication.SPACE_SCRAPYARD || sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_01 || sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_02 || sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_03 || sector.VisualIndication == ESectorVisualIndication.SPACE_CAVE_2)
            {
                //In a sector with a store
                if (PLEncounterManager.Instance.PlayerShip.NumberOfFuelCapsules <= 15)
                {
                    int numoffuels = PLServer.Instance.CurrentCrewCredits / (int)(PLServer.Instance.GetFuelBasePrice() * ShopRepMultiplier()) / 2;
                    numoffuels = Mathf.Min(numoffuels, 200 - PLEncounterManager.Instance.PlayerShip.NumberOfFuelCapsules);
                    for (int i = 0; i < numoffuels; i++)
                    {
                        PLServer.Instance.photonView.RPC("CaptainBuy_Fuel", PhotonTargets.All, new object[]
                        {
                         PLEncounterManager.Instance.PlayerShip.ShipID,
                         (int)(PLServer.Instance.GetFuelBasePrice()* ShopRepMultiplier())
                        });
                    }
                }
                if (PLEncounterManager.Instance.PlayerShip.ReactorCoolantLevelPercent < 0.9f)
                {
                    int numofcoolant = PLServer.Instance.CurrentCrewCredits / (int)(PLServer.Instance.GetCoolantBasePrice() * ShopRepMultiplier());
                    numofcoolant = Mathf.Min(numofcoolant, (int)((1 - PLEncounterManager.Instance.PlayerShip.ReactorCoolantLevelPercent) * 8));
                    for (int i = 0; i < numofcoolant; i++)
                    {
                        PLServer.Instance.photonView.RPC("CaptainBuy_Coolant", PhotonTargets.All, new object[]
                        {
                         PLEncounterManager.Instance.PlayerShip.ShipID,
                         PLServer.Instance.GetCoolantBasePrice() * ShopRepMultiplier()
                        });
                    }

                }
                foreach (PLShipComponent component in PLEncounterManager.Instance.PlayerShip.MyStats.AllComponents)
                {
                    if (component is PLTrackerMissile)
                    {
                        PLTrackerMissile missile = component as PLTrackerMissile;
                        if (missile.SubTypeData < missile.AmmoCapacity && missile.IsEquipped)
                        {
                            if ((missile.AmmoCapacity - missile.SubTypeData) * missile.MissileRefillPrice * ShopRepMultiplier() < PLServer.Instance.CurrentCrewCredits)
                            {
                                PLServer.Instance.photonView.RPC("CaptainBuy_MissileRefill", PhotonTargets.All, new object[]
                                {
                                PLEncounterManager.Instance.PlayerShip.ShipID,
                                missile.NetID,
                                missile.AmmoCapacity - missile.SubTypeData,
                                (int)((missile.AmmoCapacity - missile.SubTypeData) * missile.MissileRefillPrice * ShopRepMultiplier())
                                });
                            }

                        }
                    }
                }
            }
            PLServer.Instance.SetCustomCaptainOrderText(0, "Use the WarpGate!", false);
            PLServer.Instance.SetCustomCaptainOrderText(1, "Engage Repair Protocols!", false);
            PLServer.Instance.SetCustomCaptainOrderText(2, "Align the ship!", false);
            PLServer.Instance.SetCustomCaptainOrderText(3, "Collect Missions!", false);
            if (PLServer.GetCurrentSector() != null && (PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.COLONIAL_HUB || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.WD_START || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.AOG_HUB || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.CORNELIA_HUB || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.CYPHER_LAB || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_01))
            {
                List<PLDialogueActorInstance> allNPC = new List<PLDialogueActorInstance>();
                foreach (PLDialogueActorInstance pLDialogueActorInstance in Object.FindObjectsOfType<PLDialogueActorInstance>()) //Finds all NPCs that have mission (with exception of Explorer's appeal)
                {
                    if (pLDialogueActorInstance.AllAvailableChoices().Count <= 0 && pLDialogueActorInstance.CurrentLine == null && pLDialogueActorInstance.ActorTypeData != null)
                    {
                        if (pLDialogueActorInstance.ActorTypeData.OpeningLines.Count > 0)
                        {
                            foreach (LineData lineData in pLDialogueActorInstance.ActorTypeData.OpeningLines)
                            {
                                if (lineData != null && lineData.PassesRequirements(pLDialogueActorInstance, null, null))
                                {
                                    pLDialogueActorInstance.CurrentLine = lineData;
                                    break;
                                }
                            }
                        }
                    }
                    if ((pLDialogueActorInstance.HasMissionStartAvailable && pLDialogueActorInstance.DisplayName == "Eldon Gatra") || (pLDialogueActorInstance.DisplayName == "Commander Darine Hatham" && (!PLServer.Instance.HasActiveMissionWithID(48632))) || pLDialogueActorInstance.DisplayName.ToLower().Contains("baris") 
                        || pLDialogueActorInstance.DisplayName.ToLower().Contains("zeng") || (pLDialogueActorInstance.HasMissionStartAvailable && pLDialogueActorInstance.DisplayName.ToLower().Contains("zesho")) || pLDialogueActorInstance.DisplayName.ToLower().Contains("eikeni")) continue;
                    if (!pLDialogueActorInstance.ShipDialogue && (pLDialogueActorInstance.HasMissionStartAvailable && pLDialogueActorInstance.AllAvailableChoices().Count > 0) || pLDialogueActorInstance.HasMissionEndAvailable)
                    {
                        allNPC.Add(pLDialogueActorInstance);
                    }
                }
                if (allNPC.Count > 0) //if there is at least 1 mission to gather or deliver
                {
                    if (PLServer.Instance.CaptainsOrdersID != 11)
                    {
                        PLServer.Instance.CaptainSetOrderID(11);
                    }
                    float NearestNPCDistance = (allNPC[0].gameObject.transform.position - __instance.GetPawn().transform.position).magnitude;
                    __instance.MyBot.AI_TargetPos = allNPC[0].gameObject.transform.position;
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    PLDialogueActorInstance targetNPC = allNPC[0];
                    foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                    {
                        if (teleport.name == "PLGamePlanet" || teleport.name == "PL_GamePlanet" || teleport.name == "PLGame")
                        {
                            __instance.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    foreach (PLDialogueActorInstance pLDialogueActorInstance in allNPC)
                    {
                        if ((pLDialogueActorInstance.gameObject.transform.position - __instance.GetPawn().transform.position).magnitude < NearestNPCDistance)
                        {
                            NearestNPCDistance = (pLDialogueActorInstance.gameObject.transform.position - __instance.GetPawn().transform.position).magnitude;
                            __instance.MyBot.AI_TargetPos = pLDialogueActorInstance.gameObject.transform.position;
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            targetNPC = pLDialogueActorInstance;
                        }
                    }
                    if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 8)
                    {
                        __instance.MyBot.EnablePathing = true;
                    }
                    else if (targetNPC.HasMissionStartAvailable)
                    {
                        LineData currentDiologue = targetNPC.AllAvailableChoices()[0];
                        if (targetNPC.DisplayName.ToLower().Contains("yiria"))
                        {
                            if (targetNPC.AllAvailableChoices()[0].ChildLines.Count > 1)
                            {
                                while ((currentDiologue.TextOptions.Count <= 0 || currentDiologue.TextOptions[0].ToLower() != "accept") && currentDiologue.ChildLines.Count > 0)
                                {
                                    currentDiologue = currentDiologue.ChildLines[0];
                                }
                            }
                            else
                            {
                                currentDiologue = targetNPC.AllAvailableChoices()[1];
                                while ((currentDiologue.TextOptions.Count <= 0 || currentDiologue.TextOptions[0].ToLower() != "accept") && currentDiologue.ChildLines.Count > 0)
                                {
                                    currentDiologue = currentDiologue.ChildLines[0];
                                }
                            }
                            targetNPC.SelectChoice(currentDiologue, true, true);
                        }
                        else if(targetNPC.DisplayName.ToLower().Contains("bomy"))
                        {
                            targetNPC.SelectChoice(targetNPC.AllAvailableChoices()[0], true, true);
                        }
                        else if(targetNPC.DisplayName.ToLower().Contains("oskal"))
                        {
                            while (currentDiologue.ChildLines.Count > 0)
                            {
                                currentDiologue = currentDiologue.ChildLines[0];
                            }
                            targetNPC.SelectChoice(currentDiologue, true, true);
                        }
                        else
                        {
                            while ((currentDiologue.TextOptions.Count <= 0 || currentDiologue.TextOptions[0].ToLower() != "accept") && currentDiologue.ChildLines.Count > 0)
                            {
                                currentDiologue = currentDiologue.ChildLines[0];
                            }
                            if (currentDiologue.TextOptions[0].ToLower() == "accept")
                            {
                                targetNPC.SelectChoice(currentDiologue, true, true);
                            }
                        }
                        
                        
                    }
                    else if (targetNPC.HasMissionEndAvailable)
                    {
                        if (targetNPC.AllAvailableChoices().Count > 0)
                        {
                            targetNPC.SelectChoice(targetNPC.AllAvailableChoices()[0], true, true);
                        }
                        try
                        {
                            targetNPC.BeginDialogue();
                        }
                        catch { }
                    }
                    return;
                }
            }
            if (__instance.StartingShip.CurrentRace != null) __instance.StartingShip.AutoTarget = false;
            else __instance.StartingShip.AutoTarget = true;
            if (__instance.StartingShip.MyFlightAI.cachedRepairDepotList.Count > 0 && __instance.StartingShip.MyStats.HullCurrent / __instance.StartingShip.MyStats.HullMax < 0.99f)
            {
                if (PLServer.Instance.CaptainsOrdersID != 9) PLServer.Instance.CaptainSetOrderID(9);
                __instance.StartingShip.AlertLevel = 0;
                PLRepairDepot repair = __instance.StartingShip.MyFlightAI.cachedRepairDepotList[0];
                if (repair.TargetShip == __instance.StartingShip && !__instance.StartingShip.ShieldIsActive && Time.time - LastAction > 1f)
                {
                    int ammount = 0;
                    int price = 0;
                    PLRepairDepot.GetAutoPurchaseInfo(__instance.StartingShip, out ammount, out price, 2);
                    PLServer.Instance.ServerRepairHull(__instance.StartingShip.ShipID, ammount, price);
                    repair.photonView.RPC("OnRepairTargetShip", PhotonTargets.All, new object[]
                    {
                        __instance.StartingShip.ShipID
                    });
                    LastAction = Time.time;
                }
            }
            else if (__instance.StartingShip.MyFlightAI.cachedWarpStationList.Count > 0 && __instance.StartingShip.MyFlightAI.cachedWarpStationList[0].IsAligned)
            {
                PLServer.Instance.CaptainSetOrderID(8);
                __instance.StartingShip.AlertLevel = 0;
            }
            else if ((__instance.StartingShip.TargetShip != null && __instance.StartingShip.TargetShip != __instance.StartingShip) || __instance.StartingShip.TargetSpaceTarget != null)
            {
                PLServer.Instance.CaptainSetOrderID(4);
                __instance.StartingShip.AlertLevel = 2;
            }
            else
            {
                PLServer.Instance.CaptainSetOrderID(1);
                __instance.StartingShip.AlertLevel = 0;
            }
            if (Time.time - __instance.StartingShip.LastTookDamageTime() < 10f && __instance.StartingShip.AlertLevel == 0)
            {
                __instance.StartingShip.AlertLevel = 1;
            }
            if (__instance.StartingShip.CurrentHailTargetSelection != null)
            {
                if (__instance.StartingShip.CurrentHailTargetSelection is PLHailTarget_StartPickupMission)
                {
                    PLHailTarget_StartPickupMission mission = __instance.StartingShip.CurrentHailTargetSelection as PLHailTarget_StartPickupMission;
                    if (mission.PickupMissionID != -1 && !PLServer.Instance.HasActiveMissionWithID(mission.PickupMissionID))
                    {
                        PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                        {
                        mission.PickupMissionID,
                        true
                        });
                        __instance.StartingShip.TargetHailTargetID = -1;
                    }
                }
                if (__instance.StartingShip.CurrentHailTargetSelection is PLHailTarget_Ship && Time.time - LastAction > 3f)
                {
                    PLHailTarget_Ship ship = __instance.StartingShip.CurrentHailTargetSelection as PLHailTarget_Ship;
                    if (ship.Hostile())
                    {
                        __instance.StartingShip.OnHailChoiceSelected(0, true, false);
                    }
                    else if (PLServer.GetCurrentSector().MissionSpecificID == 20572 && PLServer.Instance.HasActiveMissionWithID(20572) && !PLServer.Instance.GetMissionWithID(20572).Ended && PLEncounterManager.Instance.PlayerShip != null && PLEncounterManager.Instance.PlayerShip.NumberOfFuelCapsules > 1)
                    {
                        __instance.StartingShip.OnHailChoiceSelected(0, true, false);
                    }
                    LastAction = Time.time;
                }
            }
            if (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.WD_MISSIONCHAIN_WEAPONS_DEMO && !PLServer.Instance.HasCompletedMissionWithID(59682)) //In the W.D. Weapons testing mission 
            {
                __instance.MyBot.AI_TargetPos = new Vector3(165, -124, -64);
                __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                PLBurrowArena arena = Object.FindObjectOfType<PLBurrowArena>();
                foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                {
                    if (teleport.name == "PLGamePlanet")
                    {
                        __instance.MyBot.AI_TargetTLI = teleport;
                        break;
                    }
                }
                if (arena.ArenaIsActive) WeaponsTest = Time.time;
                __instance.MyBot.EnablePathing = true;
                if (!arena.ArenaIsActive && Time.time - WeaponsTest > 90)
                {
                    arena.StartArena_NoCredits(0);
                    PLServer.Instance.GetMissionWithID(59682).Objectives[1].AmountCompleted = 1;
                    WeaponsTest = Time.time;
                }
                if (__instance.GetPawn().SpawnedInArena)
                {
                    __instance.MyBot.AI_TargetPos = new Vector3(126, -139, -27);
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    __instance.ActiveMainPriority = new AIPriority(AIPriorityType.E_MAIN, 2, 1);
                    __instance.MyBot.TickFindInvaderAction(null);

                }
                return;
            }
            if (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.DESERT_HUB && !PLServer.Instance.IsFragmentCollected(1))//In the burrow
            {
                if (PLServer.Instance.CurrentCrewCredits >= 100000)
                {
                    __instance.MyBot.AI_TargetPos = new Vector3(212, 64, -38);
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                    {
                        if (teleport.name == "PLGame")
                        {
                            __instance.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                    {
                        __instance.MyBot.EnablePathing = true;
                    }
                    else
                    {
                        PLServer.Instance.photonView.RPC("AttemptForceEndMissionOfTypeID", PhotonTargets.All, new object[]
                        {
                        100786
                        });
                        PLServer.Instance.CollectFragment(1);
                        PLServer.Instance.CurrentCrewCredits -= 100000;
                    }
                }
                else if (PLServer.Instance.CurrentCrewCredits >= 50000 && PLServer.Instance.CurrentCrewLevel >= 5)
                {
                    __instance.MyBot.AI_TargetPos = new Vector3(62, 18, -56);
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    PLBurrowArena arena = Object.FindObjectOfType(typeof(PLBurrowArena)) as PLBurrowArena;
                    if (arena != null)
                    {
                        if (__instance.GetPawn().SpawnedInArena)
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(103, 4, -115);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            __instance.MyBot.EnablePathing = true;
                        }
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "PLGame")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4 && !__instance.GetPawn().SpawnedInArena)
                        {
                            __instance.MyBot.EnablePathing = true;
                        }
                        else if (!arena.ArenaIsActive)
                        {
                            arena.StartArena(0);
                            __instance.GetPawn().transform.position = new Vector3(103, 4, -115);
                        }
                        else if (arena.ArenaIsActive && __instance.GetPawn().SpawnedInArena)
                        {
                            __instance.ActiveMainPriority = new AIPriority(AIPriorityType.E_MAIN, 2, 1);
                            __instance.MyBot.TickFindInvaderAction(null);
                        }
                    }
                }
                LastAction = Time.time;
                return;
            }
            if (PLServer.GetCurrentSector() != null && (PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR_2 || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR_3))
            {
                PLRace race = (Object.FindObjectOfType(typeof(PLRaceStartScreen)) as PLRaceStartScreen).MyRace;
                PLPickupComponent prize = Object.FindObjectOfType(typeof(PLPickupComponent)) as PLPickupComponent;
                if (PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR && race != null)
                {
                    if (!race.ReadyToStart && (PLServer.Instance.RacesWonBitfield & 1) == 0)
                    {
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "GarageBSO")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        if (!PLServer.Instance.HasActiveMissionWithID(43499) && !PLServer.Instance.HasActiveMissionWithID(43072) && PLServer.Instance.CurrentCrewCredits >= 1000)
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(174, 4, -332);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                if (PLServer.Instance.CurrentCrewCredits >= 5000 && !PLServer.Instance.HasActiveMissionWithID(43499))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                                    {
                                    43499,
                                    false
                                    });
                                }
                                else if (!PLServer.Instance.HasActiveMissionWithID(43072) && PLServer.Instance.CurrentCrewCredits >= 1000)
                                {
                                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                                    {
                                    43072,
                                    false
                                    });
                                }
                            }
                        }
                        else
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(158, 4, -341);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;

                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                race.SetAsReadyToStart();
                            }
                        }
                        LastAction = Time.time;
                        return;
                    }
                    else if (race.RaceEnded && (PLServer.Instance.RacesWonBitfield & 1) != 0 && ((prize != null && !prize.PickedUp) || (PLServer.Instance.HasActiveMissionWithID(43499) && !PLServer.Instance.GetMissionWithID(43499).Ended) || (PLServer.Instance.HasActiveMissionWithID(43072) && !PLServer.Instance.GetMissionWithID(43072).Ended)))
                    {
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "GarageBSO")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        if ((PLServer.Instance.HasActiveMissionWithID(43499) && !PLServer.Instance.GetMissionWithID(43499).Ended) || (PLServer.Instance.HasActiveMissionWithID(43072) && !PLServer.Instance.GetMissionWithID(43072).Ended))
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(174, 4, -332);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                if (PLServer.Instance.HasActiveMissionWithID(43499))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptForceEndMissionOfTypeID", PhotonTargets.All, new object[]
                                    {
                                    43499
                                    });
                                    PLServer.Instance.CurrentCrewCredits += 15000;
                                }
                                else if (PLServer.Instance.HasActiveMissionWithID(43072))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptForceEndMissionOfTypeID", PhotonTargets.All, new object[]
                                    {
                                    43072
                                    });
                                    PLServer.Instance.CurrentCrewCredits += 3000;
                                }
                            }
                        }
                        else if (prize != null && !prize.PickedUp)
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(162, 6, -335);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                __instance.AttemptToPickupComponentAtID(prize.PickupID);
                            }
                        }
                        LastAction = Time.time;
                        return;
                    }
                }
                else if (PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR_2 && race != null)
                {
                    if (!race.ReadyToStart && (PLServer.Instance.RacesWonBitfield & 2) == 0)
                    {
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "GarageBSO")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        if (!PLServer.Instance.HasActiveMissionWithID(43932) && !PLServer.Instance.HasActiveMissionWithID(43938) && PLServer.Instance.CurrentCrewCredits >= 1000)
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(123, -15, -345);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                if (PLServer.Instance.CurrentCrewCredits >= 5000 && !PLServer.Instance.HasActiveMissionWithID(43938))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                                    {
                                    43938,
                                    false
                                    });
                                }
                                else if (!PLServer.Instance.HasActiveMissionWithID(43932) && PLServer.Instance.CurrentCrewCredits >= 1000)
                                {
                                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                                    {
                                    43932,
                                    false
                                    });
                                }
                            }
                        }
                        else
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(132, -15, -278);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;

                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                race.SetAsReadyToStart();
                            }
                        }
                        LastAction = Time.time;
                        return;
                    }
                    else if (race.RaceEnded && (PLServer.Instance.RacesWonBitfield & 2) != 0 && ((prize != null && !prize.PickedUp) || (PLServer.Instance.HasActiveMissionWithID(43932) && !PLServer.Instance.GetMissionWithID(43932).Ended) || (PLServer.Instance.HasActiveMissionWithID(43938) && !PLServer.Instance.GetMissionWithID(43938).Ended)))
                    {
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "GarageBSO")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        if ((PLServer.Instance.HasActiveMissionWithID(43938) && !PLServer.Instance.GetMissionWithID(43938).Ended) || (PLServer.Instance.HasActiveMissionWithID(43932) && !PLServer.Instance.GetMissionWithID(43932).Ended))
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(123, -15, -345);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                if (PLServer.Instance.HasActiveMissionWithID(43932))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptForceEndMissionOfTypeID", PhotonTargets.All, new object[]
                                    {
                                    43932
                                    });
                                    PLServer.Instance.CurrentCrewCredits += 3000;
                                }
                                else if (PLServer.Instance.HasActiveMissionWithID(43938))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptForceEndMissionOfTypeID", PhotonTargets.All, new object[]
                                    {
                                    43938
                                    });
                                    PLServer.Instance.CurrentCrewCredits += 15000;
                                }
                            }
                        }
                        else if (prize != null && !prize.PickedUp)
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(129, -14, -270);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                __instance.AttemptToPickupComponentAtID(prize.PickupID);
                            }
                        }
                        LastAction = Time.time;
                        return;
                    }
                }
                else if (PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR_3 && race != null && (PLServer.Instance.RacesWonBitfield & 1) != 0 && (PLServer.Instance.RacesWonBitfield & 2) != 0)
                {
                    if (!race.ReadyToStart && (PLServer.Instance.RacesWonBitfield & 4) == 0)
                    {
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "GarageBSO")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        if (!PLServer.Instance.HasActiveMissionWithID(44085) && !PLServer.Instance.HasActiveMissionWithID(44088) && PLServer.Instance.CurrentCrewCredits >= 1000)
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(115, -7, -233);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                if (PLServer.Instance.CurrentCrewCredits >= 5000 && !PLServer.Instance.HasActiveMissionWithID(44088))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                                    {
                                    44088,
                                    false
                                    });
                                }
                                else if (!PLServer.Instance.HasActiveMissionWithID(44085) && PLServer.Instance.CurrentCrewCredits >= 1000)
                                {
                                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                                    {
                                    44085,
                                    false
                                    });
                                }
                            }
                        }
                        else
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(106, -7, -234);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;

                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                race.SetAsReadyToStart();
                            }
                        }
                        LastAction = Time.time;
                        return;
                    }
                    else if (race.RaceEnded && (PLServer.Instance.RacesWonBitfield & 4) != 0 && ((prize != null && !prize.PickedUp) || (PLServer.Instance.HasActiveMissionWithID(44085) && !PLServer.Instance.GetMissionWithID(44085).Ended) || (PLServer.Instance.HasActiveMissionWithID(44088) && !PLServer.Instance.GetMissionWithID(44088).Ended)))
                    {
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "GarageBSO")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        if ((PLServer.Instance.HasActiveMissionWithID(44085) && !PLServer.Instance.GetMissionWithID(44085).Ended) || (PLServer.Instance.HasActiveMissionWithID(44088) && !PLServer.Instance.GetMissionWithID(44088).Ended))
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(115, -7, -233);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                if (PLServer.Instance.HasActiveMissionWithID(44085))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptForceEndMissionOfTypeID", PhotonTargets.All, new object[]
                                    {
                                    44085
                                    });
                                    PLServer.Instance.CurrentCrewCredits += 15000;
                                }
                                else if (PLServer.Instance.HasActiveMissionWithID(44088))
                                {
                                    PLServer.Instance.photonView.RPC("AttemptForceEndMissionOfTypeID", PhotonTargets.All, new object[]
                                    {
                                    44088
                                    });
                                    PLServer.Instance.CurrentCrewCredits += 30000;
                                }
                            }
                        }
                        else if (prize != null && !prize.PickedUp)
                        {
                            __instance.MyBot.AI_TargetPos = new Vector3(110, -6, -226);
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                            {
                                __instance.MyBot.EnablePathing = true;
                            }
                            else
                            {
                                __instance.AttemptToPickupComponentAtID(prize.PickupID);
                            }
                        }
                        LastAction = Time.time;
                        return;
                    }
                }
            }
            if (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.GREY_HUNTSMAN_HQ && PLServer.Instance.HasActiveMissionWithID(104869) && !PLServer.Instance.GetMissionWithID(104869).Ended && !PLServer.Instance.IsFragmentCollected(7))//Get fragment from grey hunstman
            {
                __instance.MyBot.AI_TargetPos = new Vector3(217, 111, -108);
                __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                {
                    if (teleport.name == "PLGamePlanet")
                    {
                        __instance.MyBot.AI_TargetTLI = teleport;
                        break;
                    }
                }
                if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                {
                    __instance.MyBot.EnablePathing = true;
                }
                else
                {
                    PLServer.Instance.photonView.RPC("AttemptForceEndMissionOfTypeID", PhotonTargets.All, new object[]
                    {
                        104869
                    });
                    PLServer.Instance.CollectFragment(7);
                }
                LastAction = Time.time;
                return;
            }
            if (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.HIGHROLLERS_STATION && !PLServer.Instance.IsFragmentCollected(3))//In the highroller
            {
                PLHighRollersShipInfo highRoller = Object.FindObjectOfType<PLHighRollersShipInfo>();
                if (__instance.ActiveMainPriority == null || __instance.ActiveMainPriority.TypeData != 65)
                {
                    __instance.ActiveMainPriority = new AIPriority(AIPriorityType.E_MAIN, 65, 1);
                }
                if (__instance.CurrentlyInLiarsDiceGame != null && highRoller.SmallGames.Contains(__instance.CurrentlyInLiarsDiceGame) && highRoller.CrewChips >= 3)
                {
                    __instance.CurrentlyInLiarsDiceGame = null;
                }
                if (!PLServer.Instance.GetMissionWithID(103216).Ended)
                {
                    if (PLServer.Instance.CurrentCrewCredits < 10000) return;
                    __instance.MyBot.AI_TargetPos = new Vector3(64, -102, -34);
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                    {
                        if (teleport.name == "PLGamePlanet")
                        {
                            __instance.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                    {
                        __instance.MyBot.EnablePathing = true;
                    }
                    else
                    {
                        PLServer.Instance.GetMissionWithID(103216).Objectives[0].AmountCompleted = 1;
                    }
                }
                else if (highRoller != null && highRoller.CrewChips < 3)
                {
                    List<PLLiarsDiceGame> possibleGames = new List<PLLiarsDiceGame>();
                    PLLiarsDiceGame neareastGame;
                    foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                    {
                        if (teleport.name == "PLGamePlanet")
                        {
                            __instance.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    foreach (PLLiarsDiceGame game in highRoller.SmallGames) //Finds all small games that have a slot
                    {
                        if (game.LocalPlayerCanJoinRightNow())
                        {
                            possibleGames.Add(game);
                        }
                    }
                    if (possibleGames.Count == 0) return;
                    neareastGame = possibleGames[0];
                    float nearestGameDist = (neareastGame.transform.position - __instance.GetPawn().transform.position).magnitude;
                    foreach (PLLiarsDiceGame game in possibleGames)
                    {
                        if ((game.transform.position - __instance.GetPawn().transform.position).magnitude < nearestGameDist)
                        {
                            nearestGameDist = (game.transform.position - __instance.GetPawn().transform.position).magnitude;
                            neareastGame = game;
                        }
                    }
                    __instance.MyBot.AI_TargetPos = neareastGame.transform.position;
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 10)
                    {
                        __instance.MyBot.EnablePathing = true;
                    }
                    else
                    {
                        __instance.CurrentlyInLiarsDiceGame = neareastGame;
                    }
                }
                else if (highRoller.BigGame.LocalPlayerCanJoinRightNow())
                {
                    __instance.MyBot.AI_TargetPos = highRoller.BigGame.transform.position;
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                    {
                        if (teleport.name == "PLGamePlanet")
                        {
                            __instance.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 10)
                    {
                        __instance.MyBot.EnablePathing = true;
                    }
                    else
                    {
                        __instance.CurrentlyInLiarsDiceGame = highRoller.BigGame;
                    }
                }
                return;
            }
            __instance.CurrentlyInLiarsDiceGame = null;
            if (__instance.StartingShip.TargetShip != null && __instance.StartingShip.TargetShip != __instance.StartingShip && (__instance.StartingShip.TargetShip as PLShipInfo) != null && __instance.StartingShip.TargetShip.TeamID > 0 && !__instance.StartingShip.TargetShip.IsQuantumShieldActive)
            {
                PLShipInfo targetEnemy = __instance.StartingShip.TargetShip as PLShipInfo;
                int screensCaptured = 0;
                int num2 = 0;
                bool CaptainScreenCaptured = false;
                foreach (PLUIScreen pluiscreen in targetEnemy.MyScreenBase.AllScreens)
                {
                    if (pluiscreen != null && !pluiscreen.IsClonedScreen)
                    {
                        if (pluiscreen.PlayerControlAlpha >= 0.9f)
                        {
                            screensCaptured++;
                            if ((pluiscreen as PLCaptainScreen) != null)
                            {
                                CaptainScreenCaptured = true;
                            }
                        }
                        num2++;
                    }
                }
                if (screensCaptured >= num2 / 2 && CaptainScreenCaptured)
                {
                    foreach (PLUIScreen pluiscreen in targetEnemy.MyScreenBase.AllScreens)
                    {
                        if ((pluiscreen as PLCaptainScreen) != null)
                        {
                            __instance.MyBot.AI_TargetPos = pluiscreen.transform.position;
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            break;
                        }
                    }
                    __instance.MyBot.AI_TargetTLI = targetEnemy.MyTLI;
                    if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                    {
                        __instance.MyBot.EnablePathing = true;
                    }
                    else
                    {
                        PLServer.Instance.photonView.RPC("ClaimShip", PhotonTargets.MasterClient, new object[]
                        {
                            targetEnemy.ShipID
                        });
                    }
                    return;
                }
            }
            if (__instance.StartingShip == null && __instance.MyCurrentTLI.MyShipInfo != null)
            {
                PLShipInfo targetEnemy = __instance.MyCurrentTLI.MyShipInfo;
                int screensCaptured = 0;
                int num2 = 0;
                bool CaptainScreenCaptured = false;
                foreach (PLUIScreen pluiscreen in targetEnemy.MyScreenBase.AllScreens)
                {
                    if (pluiscreen != null && !pluiscreen.IsClonedScreen)
                    {
                        if (pluiscreen.PlayerControlAlpha >= 0.9f)
                        {
                            screensCaptured++;
                            if ((pluiscreen as PLCaptainScreen) != null)
                            {
                                CaptainScreenCaptured = true;
                            }
                        }
                        num2++;
                    }
                }
                if (screensCaptured >= num2 / 2 && CaptainScreenCaptured)
                {
                    foreach (PLUIScreen pluiscreen in targetEnemy.MyScreenBase.AllScreens)
                    {
                        if ((pluiscreen as PLCaptainScreen) != null)
                        {
                            __instance.MyBot.AI_TargetPos = pluiscreen.transform.position;
                            __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                            break;
                        }
                    }
                    __instance.MyBot.AI_TargetTLI = targetEnemy.MyTLI;
                    if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                    {
                        __instance.MyBot.EnablePathing = true;
                    }
                    else
                    {
                        PLServer.Instance.photonView.RPC("ClaimShip", PhotonTargets.MasterClient, new object[]
                        {
                            targetEnemy.ShipID
                        });
                    }
                    return;
                }
            }
            if ((PLServer.Instance.m_ShipCourseGoals.Count == 0 || Time.time - LastMapUpdate > 15) && (!IsRandomDestiny || (PLServer.Instance.m_ShipCourseGoals.Count > 0 && (PLServer.Instance.m_ShipCourseGoals[0] == PLServer.GetCurrentSector().ID || (PLGlobal.Instance.Galaxy.AllSectorInfos[PLServer.Instance.m_ShipCourseGoals[0]].Position - PLServer.GetCurrentSector().Position).magnitude > __instance.StartingShip.MyStats.WarpRange))))
            {
                //Updates the map destines
                if (PLServer.Instance.m_ShipCourseGoals.Count == 0) IsRandomDestiny = false;
                PLServer.Instance.photonView.RPC("ClearCourseGoals", PhotonTargets.All, new object[0]);
                SetNextDestiny();
                if (PLServer.Instance.m_ShipCourseGoals.Count > 0 && PLServer.Instance.m_ShipCourseGoals[0] == PLServer.GetCurrentSector().ID)
                {
                    PLServer.Instance.photonView.RPC("RemoveCourseGoal", PhotonTargets.All, new object[]
                    {
                    PLServer.Instance.m_ShipCourseGoals[0]
                    });
                }
            }
            if (__instance.StartingShip != null && __instance.StartingShip.MyStats.GetShipComponent<PLCaptainsChair>(ESlotType.E_COMP_CAPTAINS_CHAIR, false) != null && Time.time - LastAction > 20f) //Sit in chair
            {
                __instance.MyBot.AI_TargetPos = __instance.StartingShip.CaptainsChairPivot.position;
                __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                __instance.MyBot.AI_TargetTLI = __instance.StartingShip.MyTLI;
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
            if ((__instance.StartingShip.HostileShips.Count > 1 || (__instance.StartingShip.TargetShip != null && __instance.StartingShip.TargetShip.GetCombatLevel() > __instance.StartingShip.GetCombatLevel())) && __instance.StartingShip.MyStats.HullCurrent / __instance.StartingShip.MyStats.HullMax < 0.2f && !__instance.StartingShip.InWarp && Time.time - LastBlindJump > 60)
            {
                //Blind jump in emergency
                __instance.MyBot.AI_TargetPos = (__instance.StartingShip.Spawners[4] as GameObject).transform.position;
                __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                __instance.MyBot.AI_TargetTLI = __instance.StartingShip.MyTLI;
                if ((__instance.MyBot.AI_TargetPos - __instance.GetPawn().transform.position).sqrMagnitude > 4)
                {
                    __instance.MyBot.EnablePathing = true;
                }
                else
                {
                    __instance.StartingShip.BlindJumpUnlocked = true;
                    PLServer.Instance.photonView.RpcSecure("AttemptBlindJump", PhotonTargets.MasterClient, true, new object[]
                    {
                        __instance.StartingShip.ShipID,
                        __instance.GetPlayerID()
                    });
                    LastBlindJump = Time.time;
                }
                LastAction = Time.time;
            }
        }
        static float ShopRepMultiplier()
        {
            float num = 1f;
            if (PLServer.GetCurrentSector() != null)
            {
                int faction = PLServer.GetCurrentSector().MySPI.Faction;
                if (faction != -1)
                {
                    bool flag = true;
                    if (faction == 1 && PLServer.GetCurrentSector().VisualIndication != ESectorVisualIndication.AOG_HUB)
                    {
                        flag = false;
                    }
                    if (flag)
                    {
                        num -= 0.05f * (float)PLServer.Instance.RepLevels[faction];
                    }
                }
            }
            return Mathf.Clamp(num, 0.5f, 2f);
        }

        static float LastDestiny = Time.time;
        static bool IsRandomDestiny = false;
        static float LastWarpGateUse = Time.time;
        static void AtColony(PLPlayer CapBot)
        {
            PLBot AI = CapBot.MyBot;
            PLPawn pawn = CapBot.GetPawn();
            PLTeleportationLocationInstance planet = null;
            foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
            {
                if (teleport.name == "PLGamePlanet")
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
                if (Time.time - LastDestiny > 10f)
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
            else if (!PLServer.AnyPlayerHasItemOfName("Lower Facilities Keycard")) //Step 2: Find lower facility key
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
            else if (colonyDoor != null && !colonyDoor.GetHasBeenCompleted()) //Step 3: Fix errors at locked door
            {
                AI.AI_TargetPos = new Vector3(954, -534, 511);
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                if ((pawn.transform.position - AI.AI_TargetPos).sqrMagnitude < 4 && !colonyDoor.HasStarted)
                {
                    colonyDoor.SetHasStarted();
                }
                if (Time.time - LastDestiny > 30 && colonyDoor.HasStarted)
                {
                    PulsarModLoader.Utilities.Messaging.ChatMessage(PhotonTargets.All, "Need fixing:", CapBot.GetPlayerID());
                    foreach (ContainmentSystemParameter parameter in colonyDoor.Parameters)
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
            else if (PLLCChair.Instance != null && !PLLCChair.Instance.Triggered && PLLCChair.Instance.GetNumErrors(true) > 0) //Step 5: Fix screen erros at final door
            {
                AI.Tick_HelpWithChairSyncMiniGame(true);
                LastDestiny = Time.time;
            }
            else if (PLLCChair.Instance != null && PLLCChair.Instance.GetNumErrors(true) <= 0 && PLLCChair.Instance.PlayerIDInChair != CapBot.GetPlayerID() && !PLLCChair.Instance.Triggered_LevelThree) //Step 6: Sit in the chair
            {
                AI.AI_TargetPos = PLLCChair.Instance.gameObject.transform.position;
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                if ((pawn.transform.position - AI.AI_TargetPos).sqrMagnitude < 8)
                {
                    PLLCChair.Instance.photonView.RPC("Trigger", PhotonTargets.All, new object[0]);
                    if (Time.time - LastDestiny > 60)
                    {
                        PLLCChair.Instance.photonView.RPC("SetPlayerIDInChair", PhotonTargets.All, new object[]
                        {
                            CapBot.GetPlayerID()
                        });
                        PLLCChair.Instance.photonView.RPC("Trigger_LevelTwo", PhotonTargets.All, new object[0]);
                    }
                }
            }
            else if (PLLCChair.Instance != null && PLLCChair.Instance.Triggered_LevelTwo && PLLCChair.Instance.PlayerIDInChair == CapBot.GetPlayerID() && !PLLCChair.Instance.Triggered_LevelThree) //Step 7: Do the minigame
            {
                if (Time.time - LastDestiny > 30 && PLLCChairUI.Instance != null)
                {
                    if (Random.Range(0, 9) != 0)
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
            else if (PLLCChair.Instance != null && PLLCChair.Instance.Triggered_LevelThree && PLLCChair.Instance.PlayerIDInChair == CapBot.GetPlayerID()) //Step 8: You keep control over the infected for yourselfs
            {
                PLLCChair.Instance.photonView.RPC("StartKeepItEnding", PhotonTargets.All, new object[0]);
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
        static void WarpGuardianBattle(PLPlayer CapBot)
        {
            if (PLWarpGuardian.Instance == null) return;
            if (PLWarpGuardian.Instance.GetCurrentPhase() == 1)
            {
                if (!PLWarpGuardian.Instance.BottomArmor.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.BottomArmor;
                }
                else if (!PLWarpGuardian.Instance.Core.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.Core;
                }
                else if (!PLWarpGuardian.Instance.HeadBeamWeapon.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.HeadBeamWeapon;
                }
                else if (!PLWarpGuardian.Instance.SideEnergyProjWeapon.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.SideEnergyProjWeapon;
                }
            }
            else
            {
                if (!PLWarpGuardian.Instance.BoardingSystem.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.BoardingSystem;
                }
                else if (!PLWarpGuardian.Instance.SideCannonModule.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.SideCannonModule;
                }
                else if (!PLWarpGuardian.Instance.BottomArmor.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.BottomArmor;
                }
                else if (!PLWarpGuardian.Instance.Core.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.Core;
                }
                else if (!PLWarpGuardian.Instance.HeadBeamWeapon.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.HeadBeamWeapon;
                }
                else if (!PLWarpGuardian.Instance.SideEnergyProjWeapon.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.SideEnergyProjWeapon;
                }
                else if (!PLWarpGuardian.Instance.BoostModule.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.BoostModule;
                }
                else if (!PLWarpGuardian.Instance.ModuleRepairModule.Destroyed)
                {
                    PLEncounterManager.Instance.PlayerShip.TargetSpaceTarget = PLWarpGuardian.Instance.ModuleRepairModule;
                }
                CapBot.ActiveMainPriority = new AIPriority(AIPriorityType.E_MAIN, 2, 1);
                //CapBot.MyBot.TickFindInvaderAction(null);
            }

        }
        static void SetNextDestiny()
        {
            if (PLEncounterManager.Instance.PlayerShip == null) return;
            List<PLSectorInfo> destines = new List<PLSectorInfo>();
            PLSectorInfo GWG = PLGlobal.Instance.Galaxy.GetSectorOfVisualIndication(ESectorVisualIndication.GWG);
            float nearestWarpGatedist = 500;
            PLSectorInfo nearestWarpGate = null;
            PLSectorInfo nearestWarpGatetoDest = null;
            PLSectorInfo nearestDestiny = null;
            if (PLEncounterManager.Instance.PlayerShip.GetCombatLevel() > 80 && PLServer.Instance.GetNumFragmentsCollected() >= 4 && PLServer.Instance.CurrentCrewLevel >= 10 && PLEncounterManager.Instance.PlayerShip.ShipTypeID != EShipType.E_POLYTECH_SHIP)
            {
                destines.Add(GWG);
            }
            else if (PLEncounterManager.Instance.PlayerShip.ShipTypeID == EShipType.E_POLYTECH_SHIP && PLServer.Instance.PTCountdownArmed && (PLServer.Instance.PTCountdownTime <= 600 || PLEncounterManager.Instance.PlayerShip.GetCombatLevel() >= 80))
            {
                destines.Add(PLGlobal.Instance.Galaxy.GetSectorOfVisualIndication(ESectorVisualIndication.PT_WARP_GATE));
            }
            else
            {
                foreach (PLMissionBase mission in PLServer.Instance.AllMissions) //Add mission sectors not visited and not completed
                {
                    if (!mission.Ended && !mission.Abandoned)
                    {
                        foreach (PLSectorInfo plsectorInfo in PLGlobal.Instance.Galaxy.AllSectorInfos.Values)
                        {
                            if (plsectorInfo.MissionSpecificID == mission.MissionTypeID && plsectorInfo != GWG && !plsectorInfo.Visited && PLStarmap.ShouldShowSector(plsectorInfo))
                            {
                                destines.Add(plsectorInfo);
                                break;
                            }
                        }
                    }
                }
                if (destines.Count == 0) //Add more destines if you are not going to any mission
                {
                    foreach (PLSectorInfo plsectorInfo in PLGlobal.Instance.Galaxy.AllSectorInfos.Values)
                    {
                        if (PLEncounterManager.Instance.PlayerShip.MyStats.ThrustOutputMax >= 0.35 && !PLServer.Instance.IsFragmentCollected(10) && PLServer.Instance.CurrentCrewLevel >= 4) //Add races to possible destinations
                        {
                            if ((PLServer.Instance.RacesWonBitfield & 1) == 0 && plsectorInfo.VisualIndication == ESectorVisualIndication.RACING_SECTOR)
                            {
                                destines.Add(plsectorInfo);
                            }
                            if ((PLServer.Instance.RacesWonBitfield & 2) == 0 && plsectorInfo.VisualIndication == ESectorVisualIndication.RACING_SECTOR_2)
                            {
                                destines.Add(plsectorInfo);
                            }
                            if ((PLServer.Instance.RacesWonBitfield & 1) != 0 && (PLServer.Instance.RacesWonBitfield & 2) != 0 && (PLServer.Instance.RacesWonBitfield & 4) == 0 && plsectorInfo.VisualIndication == ESectorVisualIndication.RACING_SECTOR_3)
                            {
                                destines.Add(plsectorInfo);
                            }
                        }
                        if (!PLServer.Instance.IsFragmentCollected(1) && (PLServer.Instance.CurrentCrewCredits >= 100000 || (PLServer.Instance.CurrentCrewCredits >= 50000 && PLServer.Instance.CurrentCrewLevel >= 5)) && plsectorInfo.VisualIndication == ESectorVisualIndication.DESERT_HUB)
                        //Add burrow to possible destinations
                        {
                            destines.Add(plsectorInfo);
                        }
                        if (PLServer.Instance.HasActiveMissionWithID(104869) && !PLServer.Instance.GetMissionWithID(104869).Ended && plsectorInfo.VisualIndication == ESectorVisualIndication.GREY_HUNTSMAN_HQ) //Add bounty hunter agency to collect fragment
                        {
                            destines.Add(plsectorInfo);
                        }
                        if (PLServer.Instance.HasActiveMissionWithID(102403) && !PLServer.Instance.IsFragmentCollected(3) && plsectorInfo.VisualIndication == ESectorVisualIndication.HIGHROLLERS_STATION && PLServer.Instance.CurrentCrewCredits >= 10000) //Add high rollers
                        {
                            destines.Add(plsectorInfo);
                        }
                        if (PLServer.Instance.CurrentCrewCredits >= 100000 && plsectorInfo.VisualIndication == ESectorVisualIndication.SPACE_SCRAPYARD && !plsectorInfo.Visited) //Add not visited scrapyards
                        {
                            destines.Add(plsectorInfo);
                        }
                    }
                }
            }
            destines.RemoveAll((PLSectorInfo sector) => sector == PLServer.GetCurrentSector());
            foreach (PLSectorInfo sector in destines) //finds nearest destiny
            {
                if ((sector.Position - PLServer.GetCurrentSector().Position).magnitude < nearestWarpGatedist)
                {
                    nearestWarpGatedist = (sector.Position - PLServer.GetCurrentSector().Position).magnitude;
                    nearestDestiny = sector;
                }
            }
            nearestWarpGatedist = 500;
            if (PLEncounterManager.Instance.PlayerShip.MyStats.HullCurrent / PLEncounterManager.Instance.PlayerShip.MyStats.HullMax < 0.6 || PLEncounterManager.Instance.PlayerShip.NumberOfFuelCapsules <= 10 || PLEncounterManager.Instance.PlayerShip.ReactorCoolantLevelPercent <= 0.25)
            {
                foreach (PLSectorInfo sector in PLGlobal.Instance.Galaxy.AllSectorInfos.Values) //finds nearest repair depot
                {
                    if ((sector.Position - PLServer.GetCurrentSector().Position).magnitude < nearestWarpGatedist && (sector.VisualIndication == ESectorVisualIndication.GENERAL_STORE || sector.VisualIndication == ESectorVisualIndication.EXOTIC1 || sector.VisualIndication == ESectorVisualIndication.EXOTIC2 || sector.VisualIndication == ESectorVisualIndication.EXOTIC3 || sector.VisualIndication == ESectorVisualIndication.EXOTIC4
                        || sector.VisualIndication == ESectorVisualIndication.EXOTIC5 || sector.VisualIndication == ESectorVisualIndication.EXOTIC6 || sector.VisualIndication == ESectorVisualIndication.EXOTIC7 || sector.VisualIndication == ESectorVisualIndication.AOG_HUB || sector.VisualIndication == ESectorVisualIndication.GENTLEMEN_START || sector.VisualIndication == ESectorVisualIndication.CORNELIA_HUB
                        || sector.VisualIndication == ESectorVisualIndication.COLONIAL_HUB || sector.VisualIndication == ESectorVisualIndication.WD_START || sector.VisualIndication == ESectorVisualIndication.SPACE_SCRAPYARD || (sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_01 && PLServer.Instance.CrewFactionID == 3) || (sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_02 && PLServer.Instance.CrewFactionID == 3) || (sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_03 && PLServer.Instance.CrewFactionID == 3)))
                    {
                        nearestWarpGatedist = (sector.Position - PLServer.GetCurrentSector().Position).magnitude;
                        nearestDestiny = sector;
                    }
                }

            }
            if (nearestDestiny != null) IsRandomDestiny = false;
            if (nearestDestiny == null && PLServer.Instance.CurrentCrewLevel >= 4 && !IsRandomDestiny)
            {
                List<PLSectorInfo> random = new List<PLSectorInfo>();
                PLSectorInfo nearestPlanet = null;
                foreach (PLSectorInfo plsectorInfo in PLGlobal.Instance.Galaxy.AllSectorInfos.Values) //finds near random sectors
                {
                    if ((plsectorInfo.Position - PLServer.GetCurrentSector().Position).magnitude <= PLEncounterManager.Instance.PlayerShip.MyStats.WarpRange && !plsectorInfo.Visited && plsectorInfo.MySPI.Faction != 4 && plsectorInfo != PLServer.GetCurrentSector() && PLStarmap.ShouldShowSector(plsectorInfo))
                    {
                        random.Add(plsectorInfo);
                        PLPersistantEncounterInstance plpersistantEncounterInstance = PLEncounterManager.Instance.CreatePersistantEncounterInstanceOfID(plsectorInfo.ID, false);
                        if (plpersistantEncounterInstance != null && plpersistantEncounterInstance is PLPersistantPlanetEncounterInstance && plsectorInfo != PLGlobal.Instance.Galaxy.GetSectorOfVisualIndication(ESectorVisualIndication.TOPSEC))
                        {
                            if (nearestPlanet == null)
                            {
                                nearestPlanet = plsectorInfo;
                            }
                            else if ((plsectorInfo.Position - PLServer.GetCurrentSector().Position).magnitude < (nearestPlanet.Position - PLServer.GetCurrentSector().Position).magnitude)
                            {
                                nearestPlanet = plsectorInfo;
                            }
                        }
                    }
                }
                if (random.Count == 0) return;
                nearestDestiny = random[Random.Range(0, random.Count - 1)];
                if (nearestPlanet != null)
                {
                    nearestDestiny = nearestPlanet;
                }
                IsRandomDestiny = true;
            }
            else if (nearestDestiny == null) return;
            nearestWarpGatedist = 500;
            foreach (PLSectorInfo plsectorInfo in PLGlobal.Instance.Galaxy.AllSectorInfos.Values) //finds nearest warpgate
            {
                if (plsectorInfo.IsPartOfLongRangeWarpNetwork)
                {
                    if ((plsectorInfo.Position - PLServer.GetCurrentSector().Position).magnitude < nearestWarpGatedist)
                    {
                        nearestWarpGatedist = (plsectorInfo.Position - PLServer.GetCurrentSector().Position).magnitude;
                        nearestWarpGate = plsectorInfo;
                    }
                }
            }
            nearestWarpGatedist = 500;
            foreach (PLSectorInfo plsectorInfo in PLGlobal.Instance.Galaxy.AllSectorInfos.Values) //finds nearest warpgate to destiny
            {
                if (plsectorInfo.IsPartOfLongRangeWarpNetwork)
                {
                    if ((plsectorInfo.Position - nearestDestiny.Position).magnitude < nearestWarpGatedist)
                    {
                        nearestWarpGatedist = (plsectorInfo.Position - nearestDestiny.Position).magnitude;
                        nearestWarpGatetoDest = plsectorInfo;
                    }
                }
            }
            if (PLGlobal.Instance.Galaxy.GetPathToSector(PLServer.GetCurrentSector(), nearestWarpGate).Count + 1 + PLGlobal.Instance.Galaxy.GetPathToSector(nearestDestiny, nearestWarpGatetoDest).Count < PLGlobal.Instance.Galaxy.GetPathToSector(PLServer.GetCurrentSector(), nearestDestiny).Count)
            {
                PLWarpStation warpGate = null;
                if (PLEncounterManager.Instance.PlayerShip.MyFlightAI.cachedWarpStationList.Count > 0)
                {
                    warpGate = PLEncounterManager.Instance.PlayerShip.MyFlightAI.cachedWarpStationList[0];
                }
                if (nearestWarpGate != PLServer.GetCurrentSector() && (nearestWarpGate.ID != PLEncounterManager.Instance.PlayerShip.WarpTargetID || !PLEncounterManager.Instance.PlayerShip.InWarp))
                {
                    PLServer.Instance.photonView.RPC("AddCourseGoal", PhotonTargets.All, new object[]
                    {
                    nearestWarpGate.ID
                    });
                }
                else if (warpGate != null && warpGate.GetPriceForSectorID(nearestWarpGatetoDest.ID) <= PLServer.Instance.CurrentCrewCredits && !warpGate.IsAligned && Time.time - LastWarpGateUse > 30 && PLServer.GetCurrentSector() != nearestWarpGatetoDest && !PLEncounterManager.Instance.PlayerShip.InWarp)
                {
                    warpGate.photonView.RPC("SetTargetedSectorID", PhotonTargets.All, new object[]
                    {
                        nearestWarpGatetoDest.ID,
                        true
                    });
                    LastWarpGateUse = Time.time;
                }
                if (warpGate != null && warpGate.IsAligned) LastWarpGateUse = Time.time;
                if (nearestDestiny != PLServer.GetCurrentSector() && (nearestDestiny.ID != PLEncounterManager.Instance.PlayerShip.WarpTargetID || !PLEncounterManager.Instance.PlayerShip.InWarp))
                {
                    PLServer.Instance.photonView.RPC("AddCourseGoal", PhotonTargets.All, new object[]
                    {
                    nearestDestiny.ID
                    });
                }
            }
            else if (nearestDestiny != PLServer.GetCurrentSector() && (nearestDestiny.ID != PLEncounterManager.Instance.PlayerShip.WarpTargetID || !PLEncounterManager.Instance.PlayerShip.InWarp))
            {
                PLServer.Instance.photonView.RPC("AddCourseGoal", PhotonTargets.All, new object[]
                {
                    nearestDestiny.ID
                });
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
            else if (PLLCChair.Instance != null && PLLCChair.Instance.PlayerIDInChair == __instance.MyPawn.GetPlayer().GetPlayerID())
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
        public static bool capisbot = false;
        public static float delay = 0f;
        static void Postfix()
        {
            if (PLEncounterManager.Instance.PlayerShip != null && PLServer.Instance.GetCachedFriendlyPlayerOfClass(0, PLEncounterManager.Instance.PlayerShip) == null && delay > 3f && PhotonNetwork.isMasterClient)
            {
                PLServer.Instance.ServerAddCrewBotPlayer(0);
                PLServer.Instance.GameHasStarted = true;
                PLServer.Instance.CrewPurchaseLimitsEnabled = false;
                PLGlobal.Instance.LoadedAIData = PLGlobal.Instance.GenerateDefaultPriorities();
                capisbot = true;
            }
            else if (PLEncounterManager.Instance.PlayerShip != null && PLServer.Instance.GetCachedFriendlyPlayerOfClass(0, PLEncounterManager.Instance.PlayerShip) == null && PhotonNetwork.isMasterClient) delay += Time.deltaTime;
            else delay = 0;
        }
    }
    [HarmonyPatch(typeof(PLPlayer), "GetAIData")]
    class CapbotReciveAI
    {
        /*
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> Instructions)
        {
            List<CodeInstruction> instructionsList = Instructions.ToList();
            instructionsList[10].opcode = OpCodes.Ldc_I4_M1;
            return instructionsList.AsEnumerable();
        }
        */
        static void Postfix(PLPlayer __instance, ref AIDataIndividual __result)
        {
            if (__instance.cachedAIData == null && SpawnBot.capisbot && __instance.TeamID == 0 && __instance.IsBot)
            {
                __instance.cachedAIData = new AIDataIndividual();
                PLGlobal.Instance.SetupClassDefaultData(ref __instance.cachedAIData, __instance.GetClassID(), false);
            }
            __result = __instance.cachedAIData;
        }
    }

    [HarmonyPatch(typeof(PLGlobal), "EnterNewGame")]
    class OnJoin
    {
        static void Postfix()
        {
            SpawnBot.delay = 0f;
            SpawnBot.capisbot = false;
        }
    }

}
