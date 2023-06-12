using PulsarModLoader;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using Steamworks;
using Unity.Jobs;

//Fix visit planet and complete missions warnings
//Fix Boarding command and action
//Fix route control
namespace CapBot
{
    [HarmonyPatch(typeof(PLPlayer), "UpdateAIPriorities")]
    class Patch
    {
        static float LastAction = 0;
        static float LastMapUpdate = Time.time;
        static float LastBlindJump = 0;
        static float WeaponsTest = Time.time;
        static float LastOrder = Time.time;
        static void Postfix(PLPlayer __instance)
        {
            if ((__instance.cachedAIData == null || __instance.cachedAIData.Priorities.Count == 0) && SpawnBot.capisbot && __instance.TeamID == 0 && __instance.IsBot) //Give default AI priorities
            {
                //if (__instance.cachedAIData == null) PulsarModLoader.Utilities.Messaging.Notification("Null value!");
                //if (__instance.cachedAIData != null) PulsarModLoader.Utilities.Messaging.Notification("Priorities value: " + __instance.cachedAIData.Priorities.Count);
                //PulsarModLoader.Utilities.Messaging.Notification("Name: " + __instance.cachedAIData.Priorities.Count);
                if (__instance.cachedAIData == null) __instance.cachedAIData = new AIDataIndividual();
                PLGlobal.Instance.SetupClassDefaultData(ref __instance.cachedAIData, __instance.GetClassID(), false);
            }
            if (__instance.GetPawn() == null || !__instance.IsBot || __instance.GetClassID() != 0 || __instance.TeamID != 0 || !PhotonNetwork.isMasterClient || __instance.StartingShip == null) return;
            int botcounter = 0; //Counts to check if crew is bot (for bots only games)
            foreach (PLPlayer player in PLServer.Instance.AllPlayers)
            {
                if (player.TeamID == 0 && player.IsBot)
                {
                    botcounter++;
                }
            }
            if (botcounter >= 5) SpawnBot.crewisbot = true; //Enables special stuff if everyone is bot on the crew
            else SpawnBot.crewisbot = false;
            if (__instance.StartingShip != null && __instance.StartingShip.ShipTypeID == EShipType.E_POLYTECH_SHIP && __instance.RaceID != 2) //set race to robot in paladin
            {
                __instance.RaceID = 2;
            }
            bool HasIntruders = false;
            if(__instance.GetPawn().MyController.AI_Item_Target == __instance.GetPawn().transform) __instance.GetPawn().MyController.PreAIPriorityTick();
            if (__instance.GetPhotonPlayer() == null && PLNetworkManager.Instance.LocalPlayer != null) __instance.PhotonPlayer = PLNetworkManager.Instance.LocalPlayer.GetPhotonPlayer();
            if (__instance.StartingShip != null && !__instance.StartingShip.InWarp) //Check for intruders and set hostile ships
            {
                foreach (PLPlayer player in PLServer.Instance.AllPlayers) // Find if there is intruders in the ship
                {
                    if (player.TeamID != 0 && player.MyCurrentTLI == __instance.StartingShip.MyTLI)
                    {
                        HasIntruders = true;
                        break;
                    }
                }
                foreach (PLShipInfoBase ship in PLEncounterManager.Instance.AllShips.Values) //Attack everyone that hates us, and warp disable beacons
                {
                    if (ship.HostileShips.Contains(__instance.StartingShip.ShipID) || (ship.ShipTypeID == EShipType.E_BEACON && (ship as PLBeaconInfo).BeaconType == EBeaconType.E_WARP_DISABLE))
                    {
                        __instance.StartingShip.AddHostileShip(ship);
                    }
                }
            }
            if (__instance.StartingShip != null && __instance.StartingShip.InWarp && PLServer.Instance.AllPlayersLoaded() && (__instance.StartingShip.MyShieldGenerator == null || __instance.StartingShip.MyStats.ShieldsCurrent / __instance.StartingShip.MyStats.ShieldsMax > 0.99)) //Skip warp when ready
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
            if (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.WASTEDWING)//In the wasted wing
            {
                WastedWing(__instance);
                return;
            }
            PLSectorInfo sector = PLServer.GetCurrentSector();
            if (sector.VisualIndication == ESectorVisualIndication.GENERAL_STORE || sector.VisualIndication == ESectorVisualIndication.EXOTIC1 || sector.VisualIndication == ESectorVisualIndication.EXOTIC2 || sector.VisualIndication == ESectorVisualIndication.EXOTIC3 || sector.VisualIndication == ESectorVisualIndication.EXOTIC4
                        || sector.VisualIndication == ESectorVisualIndication.EXOTIC5 || sector.VisualIndication == ESectorVisualIndication.EXOTIC6 || sector.VisualIndication == ESectorVisualIndication.EXOTIC7 || sector.VisualIndication == ESectorVisualIndication.AOG_HUB || sector.VisualIndication == ESectorVisualIndication.GENTLEMEN_START || sector.VisualIndication == ESectorVisualIndication.CORNELIA_HUB
                        || sector.VisualIndication == ESectorVisualIndication.COLONIAL_HUB || sector.VisualIndication == ESectorVisualIndication.WD_START || sector.VisualIndication == ESectorVisualIndication.SPACE_SCRAPYARD || sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_01 || sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_02 || sector.VisualIndication == ESectorVisualIndication.FLUFFY_FACTORY_03 || sector.VisualIndication == ESectorVisualIndication.SPACE_CAVE_2)
            {
                //In a sector with a store
                if (PLEncounterManager.Instance.PlayerShip.NumberOfFuelCapsules <= 15)//Buy fuel capsules if needed
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
                if (PLEncounterManager.Instance.PlayerShip.ReactorCoolantLevelPercent < 0.9f)//Buy coolant if needed
                {
                    int numofcoolant = PLServer.Instance.CurrentCrewCredits / (int)(PLServer.Instance.GetCoolantBasePrice() * ShopRepMultiplier());
                    numofcoolant = Mathf.Min(numofcoolant, (int)((1 - PLEncounterManager.Instance.PlayerShip.ReactorCoolantLevelPercent) * 8));
                    for (int i = 0; i < numofcoolant; i++)
                    {
                        PLServer.Instance.photonView.RPC("CaptainBuy_Coolant", PhotonTargets.All, new object[]
                        {
                         PLEncounterManager.Instance.PlayerShip.ShipID,
                         (int)(PLServer.Instance.GetCoolantBasePrice() * ShopRepMultiplier())
                        });
                    }

                }
                foreach (PLShipComponent component in PLEncounterManager.Instance.PlayerShip.MyStats.AllComponents)//Buy missile refill if needed
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
                    if (PLServer.Instance.CaptainsOrdersID != 11 && Time.time - LastOrder > 1f)
                    {
                        LastOrder = Time.time;
                        PLServer.Instance.CaptainSetOrderID(11);
                    }
                    float NearestNPCDistance = (allNPC[0].gameObject.transform.position - __instance.GetPawn().transform.position).magnitude;
                    __instance.MyBot.AI_TargetPos = allNPC[0].gameObject.transform.position;
                    __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                    PLDialogueActorInstance targetNPC = allNPC[0];
                    foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType<PLTeleportationLocationInstance>())
                    {
                        if (teleport.name == "PLGamePlanet" || teleport.name == "PL_GamePlanet" || teleport.name == "PLGame")
                        {
                            __instance.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    foreach (PLDialogueActorInstance pLDialogueActorInstance in allNPC)
                    {
                        if (pLDialogueActorInstance.DisplayName.ToLower().Contains("yiria") && pLDialogueActorInstance.HasMissionStartAvailable && (pLDialogueActorInstance.AllAvailableChoices().Count < 2 || (pLDialogueActorInstance.AllAvailableChoices()[0].ChildLines.Count <= 1 && pLDialogueActorInstance.AllAvailableChoices()[1].ChildLines.Count <= 1))) continue;
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
                        else if (targetNPC.DisplayName.ToLower().Contains("bomy"))
                        {
                            targetNPC.SelectChoice(targetNPC.AllAvailableChoices()[0], true, true);
                        }
                        else if (targetNPC.DisplayName.ToLower().Contains("oskal"))
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
            if (__instance.StartingShip.TargetShip != null && __instance.StartingShip.TargetShip != __instance.StartingShip && __instance.StartingShip.TargetShip is PLShipInfo && __instance.StartingShip.TargetShip.TeamID > 0 && (!__instance.StartingShip.TargetShip.IsQuantumShieldActive || __instance.MyCurrentTLI == __instance.StartingShip.TargetShip.MyTLI))
            {
                //Board enemy to remove claim
                PLShipInfo targetEnemy = __instance.StartingShip.TargetShip as PLShipInfo;
                int screensCaptured = 0;
                int num2 = 0;
                bool CaptainScreenCaptured = false;
                if (PLServer.Instance.CaptainsOrdersID != 6 && Time.time - LastOrder > 1f)
                {
                    LastOrder = Time.time;
                    PLServer.Instance.CaptainSetOrderID(6);
                }
                __instance.StartingShip.AlertLevel = 2;
                __instance.MyBot.AI_TargetTLI = targetEnemy.MyTLI;
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
            if (__instance.StartingShip == null && __instance.MyCurrentTLI.MyShipInfo != null) //Claim current ship if player ship was destroyed/captured
            {
                PLShipInfo targetEnemy = __instance.MyCurrentTLI.MyShipInfo;
                int screensCaptured = 0;
                int num2 = 0;
                bool CaptainScreenCaptured = false;
                foreach (PLUIScreen pluiscreen in targetEnemy.MyScreenBase.AllScreens)//Capture enough screens
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
                if (screensCaptured >= num2 / 2 && CaptainScreenCaptured)//Claim the ship
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
            if (__instance.StartingShip.CurrentRace != null) __instance.StartingShip.AutoTarget = false; //Disables ship autotarget when racing
            else __instance.StartingShip.AutoTarget = true;
            //Set captain orders and special actions
            if (__instance.StartingShip.MyFlightAI.cachedRepairDepotList.Count > 0 && __instance.StartingShip.MyStats.HullCurrent / __instance.StartingShip.MyStats.HullMax < 0.99f)//Repair procedures on repair station
            {
                if (PLServer.Instance.CaptainsOrdersID != 9 && Time.time - LastOrder > 1f)
                {
                    LastOrder = Time.time;
                    PLServer.Instance.CaptainSetOrderID(9);
                }
                __instance.StartingShip.AlertLevel = 0;
                PLRepairDepot repair = __instance.StartingShip.MyFlightAI.cachedRepairDepotList[0];
                if (repair.TargetShip == __instance.StartingShip && !__instance.StartingShip.ShieldIsActive && Time.time - LastAction > 1f)//Uses repair station if possible
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
            else if (__instance.StartingShip.MyFlightAI.cachedWarpStationList.Count > 0 && __instance.StartingShip.MyFlightAI.cachedWarpStationList[0].IsAligned)//Asks to use the warp gate
            {
                if (PLServer.Instance.CaptainsOrdersID != 8 && Time.time - LastOrder > 1f)
                {
                    LastOrder = Time.time;
                    PLServer.Instance.CaptainSetOrderID(8);
                }
                __instance.StartingShip.AlertLevel = 0;
            }
            else if (__instance.StartingShip != null && HasIntruders) //Repel any intruders
            {
                if (PLServer.Instance.CaptainsOrdersID != 6 && Time.time - LastOrder > 1f)
                {
                    LastOrder = Time.time;
                    PLServer.Instance.CaptainSetOrderID(6);
                }
                __instance.StartingShip.AlertLevel = 2;
            }
            else if ((__instance.StartingShip.TargetShip != null && __instance.StartingShip.TargetShip != __instance.StartingShip) || __instance.StartingShip.TargetSpaceTarget != null)//Kill enemies
            {
                if (PLServer.Instance.CaptainsOrdersID != 4 && Time.time - LastOrder > 1f)
                {
                    LastOrder = Time.time;
                    PLServer.Instance.CaptainSetOrderID(4);
                }
                __instance.StartingShip.AlertLevel = 2;
            }
            else if (PLServer.GetCurrentSector().MySPI.HasPlanet && __instance.StartingShip != null)//Explore planet
            {
                if (PLServer.Instance.CaptainsOrdersID != 12 && Time.time - LastOrder > 1f)
                {
                    LastOrder = Time.time;
                    PLServer.Instance.CaptainSetOrderID(12);
                }
                if (SpawnBot.crewisbot || (PLServer.Instance.GetCachedFriendlyPlayerOfClass(2) != null && PLServer.Instance.GetCachedFriendlyPlayerOfClass(2).IsBot))
                {
                    List<PLPawnBase> targets = new List<PLPawnBase>();
                    List<PLPickupObject> pickupTargets = new List<PLPickupObject>();
                    List<PLPickupComponent> componentsTargets = new List<PLPickupComponent>();
                    foreach (PLMissionBase mission in PLServer.Instance.AllMissions)
                    {
                        if (!mission.Ended && !mission.Abandoned)
                        {
                            foreach (PLMissionObjective objective in mission.Objectives)
                            {
                                if (!objective.IsCompleted)
                                {
                                    if (objective is PLMissionObjective_KillEnemyOfName)
                                    {
                                        foreach (PLPawnBase target in PLGameStatic.Instance.AllPawnBases)
                                        {
                                            if (target.GetPlayer() != null && target.GetPlayer().GetPlayerName(false) == (objective as PLMissionObjective_KillEnemyOfName).EnemyName)
                                            {
                                                targets.Add(target);
                                            }
                                        }
                                    }
                                    else if (objective is PLMissionObjective_KillEnemyOfType)
                                    {
                                        foreach (PLPawnBase target in PLGameStatic.Instance.AllPawnBases)
                                        {
                                            if (target.PawnType == (objective as PLMissionObjective_KillEnemyOfType).EnemyType)
                                            {
                                                targets.Add(target);
                                            }
                                        }
                                    }
                                    else if (objective is PLMissionObjective_ReachSectorOfType && (objective as PLMissionObjective_ReachSectorOfType).MustKillAllEnemies)
                                    {
                                        foreach (PLPawnBase target in PLGameStatic.Instance.AllPawnBases)
                                        {
                                            if (target.GetPlayer() != null && target.GetPlayer().name == "PreviewPlayer" || target.IsDead || target.GetIsFriendly()) continue;
                                            if (target.CurrentShip != null && target.CurrentShip != __instance.StartingShip && (((target is PLPawn) && (target as PLPawn).TeamID != 0) || target.GetPlayer() == null || target.GetPlayer().TeamID != 0))
                                            {
                                                __instance.StartingShip.AddHostileShip(target.CurrentShip);
                                            }
                                            else if ((((target is PLPawn) && (target as PLPawn).TeamID != 0) || target.GetPlayer() == null || target.GetPlayer().TeamID != 0))
                                            {
                                                targets.Add(target);
                                            }
                                        }
                                    }
                                    else if (objective is PLMissionObjective_PickupItem && (SpawnBot.crewisbot || (PLServer.Instance.GetCachedFriendlyPlayerOfClass(2) != null && PLServer.Instance.GetCachedFriendlyPlayerOfClass(2).IsBot && PLServer.Instance.GetCachedFriendlyPlayerOfClass(2).Talents[34] == 1)))
                                    {
                                        foreach (PLPickupObject inObj in PLGameStatic.Instance.m_AllPickupObjects)
                                        {
                                            if (inObj.ItemType == (objective as PLMissionObjective_PickupItem).ItemTypeToPickup && inObj.SubItemType == (objective as PLMissionObjective_PickupItem).SubItemType && !inObj.PickedUp)
                                            {
                                                pickupTargets.Add(inObj);
                                            }
                                        }
                                    }
                                    else if (objective is PLMissionObjective_PickupComponent && (SpawnBot.crewisbot || (PLServer.Instance.GetCachedFriendlyPlayerOfClass(2) != null && PLServer.Instance.GetCachedFriendlyPlayerOfClass(2).IsBot && PLServer.Instance.GetCachedFriendlyPlayerOfClass(2).Talents[34] == 1)))
                                    {
                                        foreach (PLPickupComponent component in Object.FindObjectsOfType(typeof(PLPickupComponent)))
                                        {
                                            if (!component.PickedUp && component.ItemType == (objective as PLMissionObjective_PickupComponent).CompType && component.SubItemType == (objective as PLMissionObjective_PickupComponent).SubType)
                                            {
                                                componentsTargets.Add(component);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (targets.Count > 0)
                    {
                        float distance = (targets[0].transform.position - __instance.GetPawn().transform.position).magnitude;
                        PLPawnBase target = targets[0];
                        foreach (PLPawnBase pawn in targets)
                        {
                            if ((pawn.transform.position - __instance.GetPawn().transform.position).magnitude < distance)
                            {
                                distance = (pawn.transform.position - __instance.GetPawn().transform.position).magnitude;
                                target = pawn;
                            }
                        }
                        __instance.MyBot.AI_TargetPos = target.transform.position;
                        __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                        __instance.MyBot.AI_TargetTLI = target.MyCurrentTLI;
                        __instance.MyBot.AI_TargetInterior = target.MyInterior;
                        __instance.MyBot.EnablePathing = true;
                        PLServer.Instance.photonView.RPC("ClearCourseGoals", PhotonTargets.All, new object[0]);
                        return;
                    }
                    else if (pickupTargets.Count > 0)
                    {
                        float distance = (pickupTargets[0].transform.position - __instance.GetPawn().transform.position).magnitude;
                        PLPickupObject target = pickupTargets[0];
                        foreach (PLPickupObject item in pickupTargets)
                        {
                            if ((item.transform.position - __instance.GetPawn().transform.position).magnitude < distance)
                            {
                                distance = (item.transform.position - __instance.GetPawn().transform.position).magnitude;
                                target = item;
                            }
                        }
                        __instance.MyBot.AI_TargetPos = target.transform.position;
                        __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "PLGamePlanet" || teleport.name == "PL_GamePlanet" || teleport.name == "PLGame")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        __instance.MyBot.AI_TargetInterior = target.MyInterior;
                        if (distance < 8f)
                        {
                            __instance.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                            {
                            target.PickupID
                            });
                            __instance.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                            PLMusic.PostEvent("play_sx_player_item_pickup", __instance.GetPawn().gameObject);
                        }
                        else __instance.MyBot.EnablePathing = true;
                        PLServer.Instance.photonView.RPC("ClearCourseGoals", PhotonTargets.All, new object[0]);
                        return;
                    }
                    else if (componentsTargets.Count > 0)
                    {
                        float distance = (componentsTargets[0].transform.position - __instance.GetPawn().transform.position).magnitude;
                        PLPickupComponent target = componentsTargets[0];
                        foreach (PLPickupComponent component in componentsTargets)
                        {
                            if ((component.transform.position - __instance.GetPawn().transform.position).magnitude < distance)
                            {
                                distance = (component.transform.position - __instance.GetPawn().transform.position).magnitude;
                                target = component;
                            }
                        }
                        __instance.MyBot.AI_TargetPos = target.transform.position;
                        __instance.MyBot.AI_TargetPos_Raw = __instance.MyBot.AI_TargetPos;
                        foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                        {
                            if (teleport.name == "PLGamePlanet" || teleport.name == "PL_GamePlanet" || teleport.name == "PLGame")
                            {
                                __instance.MyBot.AI_TargetTLI = teleport;
                                break;
                            }
                        }
                        __instance.MyBot.AI_TargetInterior = target.MyInterior;
                        if (distance < 8f)
                        {
                            __instance.photonView.RPC("AttemptToPickupComponentAtID", PhotonTargets.MasterClient, new object[]
                                    {
                                    target.PickupID
                                    });
                            __instance.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                            PLMusic.PostEvent("play_sx_player_item_pickup_large", __instance.GetPawn().gameObject);
                        }
                        else __instance.MyBot.EnablePathing = true;
                        PLServer.Instance.photonView.RPC("ClearCourseGoals", PhotonTargets.All, new object[0]);
                        return;
                    }
                }
            }
            else if (PLStarmap.Instance.CurrentShipPath.Count > 0 && (__instance.StartingShip.MyFlightAI.cachedWarpStationList.Count == 0 || (!__instance.StartingShip.MyFlightAI.cachedWarpStationList[0].IsAligned && __instance.StartingShip.MyFlightAI.cachedWarpStationList[0].TargetedWarpSectorID == -1)))//Align the ship
            {
                if (PLServer.Instance.CaptainsOrdersID != 10 && Time.time - LastOrder > 1f)
                {
                    LastOrder = Time.time;
                    PLServer.Instance.CaptainSetOrderID(10);
                }
                __instance.StartingShip.AlertLevel = 0;
            }
            else//Just be at atention
            {
                if (PLServer.Instance.CaptainsOrdersID != 1 && Time.time - LastOrder > 1f)
                {
                    LastOrder = Time.time;
                    PLServer.Instance.CaptainSetOrderID(1);
                }
                __instance.StartingShip.AlertLevel = 0;
            }
            if (Time.time - __instance.StartingShip.LastTookDamageTime() < 10f && __instance.StartingShip.AlertLevel == 0) //Yellow alert if took damage recently and doesn't have a target
            {
                __instance.StartingShip.AlertLevel = 1;
            }
            if (__instance.StartingShip.CurrentHailTargetSelection != null)//Handle ship comms
            {
                if (__instance.StartingShip.CurrentHailTargetSelection is PLHailTarget_StartPickupMission)//Accepts any missions from long range comms
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
                if (__instance.StartingShip.CurrentHailTargetSelection is PLHailTarget_Ship && Time.time - LastAction > 3f)//Does dialogue with ships
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
                Burrow(__instance);
                LastAction = Time.time;
                return;
            }
            if (PLServer.GetCurrentSector() != null && (PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR_2 || PLServer.GetCurrentSector().VisualIndication == ESectorVisualIndication.RACING_SECTOR_3))//In any of the races
            {
                AtRaces(__instance, ref LastAction);
                return;
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
            if ((PLServer.Instance.m_ShipCourseGoals.Count == 0 || Time.time - LastMapUpdate > 5) && (!IsRandomDestiny || (PLServer.Instance.m_ShipCourseGoals.Count > 0 && (PLServer.Instance.m_ShipCourseGoals[0] == PLServer.GetCurrentSector().ID || (PLGlobal.Instance.Galaxy.AllSectorInfos[PLServer.Instance.m_ShipCourseGoals[0]].Position - PLServer.GetCurrentSector().Position).magnitude > __instance.StartingShip.MyStats.WarpRange) && (PLServer.GetCurrentSector() != null && PLServer.GetCurrentSector().VisualIndication != ESectorVisualIndication.STOP_ASTEROID_ENCOUNTER))))
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
                LastMapUpdate = Time.time;
            }
            if (__instance.StartingShip != null && __instance.StartingShip.MyStats.GetShipComponent<PLCaptainsChair>(ESlotType.E_COMP_CAPTAINS_CHAIR, false) != null && Time.time - LastAction > 20f) //Sit in chair
            {
                //If there is nothing to do captain will sit in the chair
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
        static Vector3 targetPos = Vector3.zero;
        static List<Vector3> targets = new List<Vector3>();
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
                if(targets.Count == 0) 
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
                    while(possibleTargets.Count > 0) 
                    {
                        Vector3 pos = possibleTargets[Random.Range(0, possibleTargets.Count - 1)];
                        possibleTargets.Remove(pos);
                        targets.Add(pos);
                    }
                    AI.AI_TargetPos = targets[0];
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    targetPos = AI.AI_TargetPos;
                }
                if((targetPos - pawn.transform.position).magnitude <= 3f) 
                {
                    targets.Remove(targets[0]);
                    if (targets.Count > 0)
                    {
                        AI.AI_TargetPos = targets[0];
                        AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                        targetPos = AI.AI_TargetPos;
                    }
                }
                else 
                {
                    AI.AI_TargetPos = targetPos;
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
            }
            else if (!PLServer.AnyPlayerHasItemOfName("Lower Facilities Keycard")) //Step 2: Find lower facility key
            {
                if (targets.Count == 0)
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
                    while (possibleTargets.Count > 0)
                    {
                        Vector3 pos = possibleTargets[Random.Range(0, possibleTargets.Count - 1)];
                        possibleTargets.Remove(pos);
                        targets.Add(pos);
                    }
                    AI.AI_TargetPos = targets[0];
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    targetPos = AI.AI_TargetPos;
                }
                if ((targetPos - pawn.transform.position).magnitude <= 3f)
                {
                    targets.Remove(targets[0]);
                    if (targets.Count > 0)
                    {
                        AI.AI_TargetPos = targets[0];
                        AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                        targetPos = AI.AI_TargetPos;
                    }
                }
                else
                {
                    AI.AI_TargetPos = targetPos;
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
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
                    PulsarModLoader.Utilities.Messaging.Notification("Sending Message");
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
                    targets.Clear();
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
        static void WastedWing(PLPlayer CapBot)
        {
            PLBot AI = CapBot.MyBot;
            PLPawn pawn = CapBot.GetPawn();
            PLLockedSeamlessDoor EntranceDoor = null;
            PLQuarantineDoor FirstDoor = null;
            PLQuarantineDoor SlimeDoors = null;
            PLRobotWalkerLarge paladin = null;
            foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
            {
                if (teleport.name == "PLGamePlanet")
                {
                    CapBot.MyBot.AI_TargetTLI = teleport;
                    break;
                }
            }
            foreach (PLQuarantineDoor teleport in Object.FindObjectsOfType(typeof(PLQuarantineDoor)))
            {
                if (teleport.name == "QuarantineDoor1")
                {
                    FirstDoor = teleport;
                    break;
                }
            }
            foreach (PLQuarantineDoor teleport in Object.FindObjectsOfType(typeof(PLQuarantineDoor)))
            {
                if (teleport.name == "QuarantineDoor1 (5)")
                {
                    SlimeDoors = teleport;
                    break;
                }
            }
            foreach (PLLockedSeamlessDoor teleport in Object.FindObjectsOfType(typeof(PLLockedSeamlessDoor)))
            {
                if (teleport.name == "Automated_Doors3 (1)")
                {
                    EntranceDoor = teleport;
                    break;
                }
            }
            foreach (PLRobotWalkerLarge teleport in Object.FindObjectsOfType(typeof(PLRobotWalkerLarge)))
            {
                if (teleport.name.Contains("Clone"))
                {
                    paladin = teleport;
                    break;
                }
            }
            if (!PLServer.AnyPlayerHasItemOfName("Entrance Security Keycard")) //Step 1: Find keycard
            {
                PLRandomChildItem positions = null;
                foreach (PLRandomChildItem teleport in Object.FindObjectsOfType<PLRandomChildItem>(true))
                {
                    if (teleport.name == "KeycardRCI")
                    {
                        positions = teleport;
                        break;
                    }
                }
                if(targets.Count == 0 && positions != null) 
                {
                    List<GameObject> keycards = new List<GameObject>();
                    foreach (PLPickupObject item in positions.gameObject.GetComponentsInChildren<PLPickupObject>(true))
                    {
                        keycards.Add(item.gameObject);
                    }
                    while(keycards.Count > 0) 
                    {
                        GameObject obj = keycards[Random.Range(0, keycards.Count - 1)];
                        keycards.Remove(obj);
                        targets.Add(obj.transform.position);
                    }
                    AI.AI_TargetPos = targets[0];
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    targetPos = AI.AI_TargetPos;
                }
                if ((targetPos - pawn.transform.position).magnitude <= 3f)
                {
                    targets.Remove(targets[0]);
                    if (targets.Count > 0)
                    {
                        AI.AI_TargetPos = targets[0];
                        AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                        targetPos = AI.AI_TargetPos;
                    }
                }
                else
                {
                    AI.AI_TargetPos = targetPos;
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
                foreach (PLPickupObject inObj in PLGameStatic.Instance.m_AllPickupObjects)
                {
                    if ((pawn.transform.position - inObj.transform.position).magnitude < 8f && !inObj.PickedUp)
                    {
                        CapBot.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                            {
                            inObj.PickupID
                            });
                        CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                        PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                        targets.Clear();
                    }
                }
            }
            else if (FirstDoor != null && !FirstDoor.IsDoorOpen && pawn.transform.position.z > -140 && pawn.transform.position.y >= -102)//Step 2: Open the first containment door and entrance door
            {
                AI.AI_TargetPos = new Vector3(58, -103, -111);
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                if((pawn.transform.position - EntranceDoor.transform.position).magnitude < 8f && !EntranceDoor.IsOpen()) 
                {
                    EntranceDoor.OpenDoor();
                }
            }
            else if (SlimeDoors != null && !SlimeDoors.IsDoorOpen)//Step 3: Kill Experiment 72 
            {
                if (pawn.transform.position.y < -120)
                {
                    if (pawn.Health / pawn.MaxHealth > 0.25f) AI.AI_TargetPos = new Vector3(59, -141, -184);
                    else AI.AI_TargetPos = new Vector3(60, -151, -186);
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
                else if(pawn.transform.position.y < -105 && pawn.transform.position.z > -232) 
                {
                    AI.AI_TargetPos = new Vector3(52.2f, -121.5f, -198.2f);
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
                else if(pawn.transform.position.y < -105) 
                {
                    AI.AI_TargetPos = new Vector3(58f, -119f, -231f);
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
                else 
                {
                    AI.AI_TargetPos = new Vector3(47f, -111f, -248f);
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
            }
            else if (!PLServer.AnyPlayerHasItemOfName("Level 1 Admin Access Card"))//Step 4: Find keycard 1 
            {
                PLRandomChildItem positions = null;
                foreach (PLRandomChildItem teleport in Object.FindObjectsOfType(typeof(PLRandomChildItem)))
                {
                    if (teleport.name == "Keycard_ADMIN_Lvl_1_RCI")
                    {
                        positions = teleport;
                        break;
                    }
                }
                if (targets.Count == 0 && positions != null)
                {
                    List<GameObject> keycards = new List<GameObject>();
                    foreach (PLPickupObject item in positions.gameObject.GetComponentsInChildren<PLPickupObject>(true))
                    {
                        keycards.Add(item.gameObject);
                    }
                    while (keycards.Count > 0)
                    {
                        GameObject obj = keycards[Random.Range(0, keycards.Count - 1)];
                        keycards.Remove(obj);
                        targets.Add(obj.transform.position);
                    }
                    AI.AI_TargetPos = targets[0];
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    targetPos = AI.AI_TargetPos;
                }
                if ((targetPos - pawn.transform.position).magnitude <= 3f)
                {
                    targets.Remove(targets[0]);
                    if (targets.Count > 0)
                    {
                        AI.AI_TargetPos = targets[0];
                        AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                        targetPos = AI.AI_TargetPos;
                    }
                }
                else
                {
                    AI.AI_TargetPos = targetPos;
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
                foreach (PLPickupObject inObj in PLGameStatic.Instance.m_AllPickupObjects)
                {
                    if ((pawn.transform.position - inObj.transform.position).magnitude < 8f && !inObj.PickedUp)
                    {
                        CapBot.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                            {
                            inObj.PickupID
                            });
                        CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                        PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                        targets.Clear();
                    }
                }
            }
            else if (!PLServer.Instance.HasMissionWithID(55400))//Step 5: Get kill Stalker Mission 
            {

                AI.AI_TargetPos = new Vector3(-12, -151, -176);
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                if ((pawn.transform.position - AI.AI_TargetPos).magnitude < 6f)
                {
                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                        {
                        55400,
                        true
                        });
                }
            }
            else if (!PLServer.Instance.HasCompletedMissionWithID(55400))//Step 6: Kill Stalker 
            {
                AI.AI_TargetPos = new Vector3(23, -233, -88);
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
            }
            else if (!PLServer.AnyPlayerHasItemOfName("Level 2 Admin Access Card"))//Step 7: Find keycard 2
            {
                PLRandomChildItem positions = null;
                foreach (PLRandomChildItem teleport in Object.FindObjectsOfType(typeof(PLRandomChildItem)))
                {
                    if (teleport.name == "Keycard_ADMIN_Lvl_2_RCI")
                    {
                        positions = teleport;
                        break;
                    }
                }
                if (targets.Count == 0 && positions != null)
                {
                    List<GameObject> keycards = new List<GameObject>();
                    foreach (PLPickupObject item in positions.gameObject.GetComponentsInChildren<PLPickupObject>(true))
                    {
                        keycards.Add(item.gameObject);
                    }
                    while (keycards.Count > 0)
                    {
                        GameObject obj = keycards[Random.Range(0, keycards.Count - 1)];
                        keycards.Remove(obj);
                        targets.Add(obj.transform.position);
                    }
                    AI.AI_TargetPos = targets[0];
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    targetPos = AI.AI_TargetPos;
                }
                if ((targetPos - pawn.transform.position).magnitude <= 3f)
                {
                    targets.Remove(targets[0]);
                    if (targets.Count > 0)
                    {
                        AI.AI_TargetPos = targets[0];
                        AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                        targetPos = AI.AI_TargetPos;
                    }
                }
                else
                {
                    AI.AI_TargetPos = targetPos;
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
                foreach (PLPickupObject inObj in PLGameStatic.Instance.m_AllPickupObjects)
                {
                    if ((pawn.transform.position - inObj.transform.position).magnitude < 8f && !inObj.PickedUp)
                    {
                        CapBot.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                            {
                            inObj.PickupID
                            });
                        CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                        PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                        targets.Clear();
                    }
                }
            }
            else if (paladin != null) //Step 8: Kill Elite Paladin
            {
                AI.HighPriorityTarget = paladin;
                AI.AI_TargetPos = new Vector3(21, -227, 394);
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
            }
            else if (!PLServer.AnyPlayerHasItemOfName("Level 3 Admin Access Card")) //Step 9: Find level 3 keycard 
            {
                PLRandomChildItem positions = null;
                foreach (PLRandomChildItem teleport in Object.FindObjectsOfType(typeof(PLRandomChildItem)))
                {
                    if (teleport.name == "Keycard_ADMIN_Lvl_3_RCI")
                    {
                        positions = teleport;
                        break;
                    }
                }
                if (targets.Count == 0 && positions != null)
                {
                    List<GameObject> keycards = new List<GameObject>();
                    foreach (PLPickupObject item in positions.gameObject.GetComponentsInChildren<PLPickupObject>(true))
                    {
                        keycards.Add(item.gameObject);
                    }
                    while (keycards.Count > 0)
                    {
                        GameObject obj = keycards[Random.Range(0, keycards.Count - 1)];
                        keycards.Remove(obj);
                        targets.Add(obj.transform.position);
                    }
                    AI.AI_TargetPos = targets[0];
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    targetPos = AI.AI_TargetPos;
                }
                if ((targetPos - pawn.transform.position).magnitude <= 3f)
                {
                    targets.Remove(targets[0]);
                    if (targets.Count > 0)
                    {
                        AI.AI_TargetPos = targets[0];
                        AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                        targetPos = AI.AI_TargetPos;
                    }
                }
                else
                {
                    AI.AI_TargetPos = targetPos;
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
                foreach (PLPickupObject inObj in PLGameStatic.Instance.m_AllPickupObjects)
                {
                    if ((pawn.transform.position - inObj.transform.position).magnitude < 8f && !inObj.PickedUp)
                    {
                        CapBot.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                            {
                            inObj.PickupID
                            });
                        CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                        PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                        targets.Clear();
                    }
                }
            }
            else if (!PLServer.Instance.HasMissionWithID(55401) || !PLServer.Instance.HasMissionWithID(55402)) //Step 10: Get kill Scientist and get medicine Missions 
            {
                if (!PLServer.Instance.HasActiveMissionWithID(55401))
                {
                    AI.AI_TargetPos = new Vector3(-12, -151, 463);
                }
                else
                {
                    AI.AI_TargetPos = new Vector3(-13, -151, 479);
                }
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                if ((pawn.transform.position - new Vector3(-12, -151, 463)).magnitude < 6f && !PLServer.Instance.HasActiveMissionWithID(55401))
                {
                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                        {
                        55401,
                        true
                        });
                }
                else if ((pawn.transform.position - new Vector3(-13, -151, 479)).magnitude < 6f)
                {
                    PLServer.Instance.photonView.RPC("AttemptStartMissionOfTypeID", PhotonTargets.MasterClient, new object[]
                            {
                        55402,
                        true
                            });
                }
            }
            else if (!PLServer.Instance.HasCompletedMissionWithID(55401)) //Step 11: Kill crystal scientists
            {
                List<PLInfectedScientist> crystals = new List<PLInfectedScientist>();
                foreach (PLInfectedScientist crystal in Object.FindObjectsOfType(typeof(PLInfectedScientist)))
                {
                    if (!crystal.IsDead)
                    {
                        crystals.Add(crystal);
                    }
                }
                if (crystals.Count > 0)
                {
                    AI.AI_TargetPos = crystals[0].transform.position;
                    foreach (PLInfectedScientist crystal in crystals)
                    {
                        if ((pawn.transform.position - crystal.transform.position).magnitude < (pawn.transform.position - AI.AI_TargetPos).magnitude)
                        {
                            AI.AI_TargetPos = crystal.transform.position;
                        }
                    }
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                }
            }
            else if (!PLServer.Instance.HasCompletedMissionWithID(55402)) //Step 12: Finish Medicine mission
            {
                if (!PLServer.AnyPlayerHasItemOfName("Medicine Pack"))
                {
                    AI.AI_TargetPos = new Vector3(-130, -151, 459);
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    foreach (PLPickupObject inObj in PLGameStatic.Instance.m_AllPickupObjects)
                    {
                        if ((pawn.transform.position - inObj.transform.position).magnitude < 8f && !inObj.PickedUp)
                        {
                            CapBot.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                                {
                            inObj.PickupID
                                });
                            CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                            PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                        }
                    }
                }
                else
                {
                    AI.AI_TargetPos = new Vector3(-14, -151, 479);
                    AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                    if ((pawn.transform.position - new Vector3(-12, -151, 463)).magnitude < 6f)
                    {
                        PLServer.Instance.AttemptCompleteObjective("mission55402obj2");
                    }
                }
            }
            else if (!PLServer.AnyPlayerHasItemOfName("Data Pad")) //Step 13: Get Data Pad
            {
                AI.AI_TargetPos = new Vector3(-154, -151, 498);
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                foreach (PLPickupComponent component in Object.FindObjectsOfType(typeof(PLPickupComponent)))
                {
                    if ((pawn.transform.position - component.transform.position).magnitude < 8f && !component.PickedUp)
                    {
                        CapBot.photonView.RPC("AttemptToPickupComponentAtID", PhotonTargets.MasterClient, new object[]
                            {
                            component.PickupID
                            });
                        CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                        PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                    }
                }
                foreach (PLPickupObject inObj in PLGameStatic.Instance.m_AllPickupObjects)
                {
                    if ((pawn.transform.position - inObj.transform.position).magnitude < 8f && !inObj.PickedUp)
                    {
                        CapBot.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                            {
                            inObj.PickupID
                            });
                        CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                        PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                    }
                }
                foreach (PLPickupRandomComponent component in Object.FindObjectsOfType(typeof(PLPickupRandomComponent)))
                {
                    if ((pawn.transform.position - component.transform.position).magnitude < 8f && !component.PickedUp)
                    {
                        CapBot.photonView.RPC("AttemptToPickupRandomComponentAtID", PhotonTargets.MasterClient, new object[]
                            {
                            component.PickupID
                            });
                        CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                        PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                    }
                }
            }
            else if (!PLServer.AnyPlayerHasItemOfName("Aberrant Organisms Lab Access Card")) //Step 14: Get Access card
            {
                AI.AI_TargetPos = new Vector3(-120, -151, 501);
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                foreach (PLPickupObject inObj in PLGameStatic.Instance.m_AllPickupObjects)
                {
                    if ((pawn.transform.position - inObj.transform.position).magnitude < 8f && !inObj.PickedUp)
                    {
                        CapBot.photonView.RPC("AttemptToPickupObjectAtID", PhotonTargets.MasterClient, new object[]
                            {
                            inObj.PickupID
                            });
                        CapBot.GetPawn().photonView.RPC("Anim_Pickup", PhotonTargets.Others, new object[0]);
                        PLMusic.PostEvent("play_sx_player_item_pickup", CapBot.GetPawn().gameObject);
                    }
                }
            }
            else //step 15: Kill the crystal guy
            {
                if (pawn.Health / pawn.MaxHealth > 0.25f) AI.AI_TargetPos = new Vector3(-108, -152, 575);
                else AI.AI_TargetPos = new Vector3(-100, -152, 575);
                foreach (PLInterior interior in Object.FindObjectsOfType(typeof(PLInterior)))
                {
                    if (interior.name == "Area_05_Interior")
                    {
                        AI.AI_TargetInterior = interior;
                        break;
                    }
                }
                AI.AI_TargetPos_Raw = AI.AI_TargetPos;
                AI.HighPriorityTarget = PLInGameUI.Instance.BossUI_Target;
            }
        }
        static void Burrow(PLPlayer CapBot)
        {
            if (PLServer.Instance.CurrentCrewCredits >= 100000)
            {
                CapBot.MyBot.AI_TargetPos = new Vector3(212, 64, -38);
                CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                {
                    if (teleport.name == "PLGame")
                    {
                        CapBot.MyBot.AI_TargetTLI = teleport;
                        break;
                    }
                }
                if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                {
                    CapBot.MyBot.EnablePathing = true;
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
                CapBot.MyBot.AI_TargetPos = new Vector3(62, 18, -56);
                CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                PLBurrowArena arena = Object.FindObjectOfType(typeof(PLBurrowArena)) as PLBurrowArena;
                if (arena != null)
                {
                    if (CapBot.GetPawn().SpawnedInArena)
                    {
                        CapBot.MyBot.AI_TargetPos = new Vector3(103, 4, -115);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        CapBot.MyBot.EnablePathing = true;
                    }
                    foreach (PLTeleportationLocationInstance teleport in Object.FindObjectsOfType(typeof(PLTeleportationLocationInstance)))
                    {
                        if (teleport.name == "PLGame")
                        {
                            CapBot.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4 && !CapBot.GetPawn().SpawnedInArena)
                    {
                        CapBot.MyBot.EnablePathing = true;
                    }
                    else if (!arena.ArenaIsActive)
                    {
                        arena.StartArena(0);
                        CapBot.GetPawn().transform.position = new Vector3(103, 4, -115);
                    }
                    else if (arena.ArenaIsActive && CapBot.GetPawn().SpawnedInArena)
                    {
                        CapBot.ActiveMainPriority = new AIPriority(AIPriorityType.E_MAIN, 2, 1);
                        CapBot.MyBot.TickFindInvaderAction(null);
                    }
                }
            }
        }
        static void AtRaces(PLPlayer CapBot, ref float LastAction)
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
                            CapBot.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if (!PLServer.Instance.HasActiveMissionWithID(43499) && !PLServer.Instance.HasActiveMissionWithID(43072) && PLServer.Instance.CurrentCrewCredits >= 1000)
                    {
                        CapBot.MyBot.AI_TargetPos = new Vector3(174, 4, -332);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                        CapBot.MyBot.AI_TargetPos = new Vector3(158, 4, -341);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;

                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                            CapBot.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if ((PLServer.Instance.HasActiveMissionWithID(43499) && !PLServer.Instance.GetMissionWithID(43499).Ended) || (PLServer.Instance.HasActiveMissionWithID(43072) && !PLServer.Instance.GetMissionWithID(43072).Ended))
                    {
                        CapBot.MyBot.AI_TargetPos = new Vector3(174, 4, -332);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                        CapBot.MyBot.AI_TargetPos = new Vector3(162, 6, -335);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
                        }
                        else
                        {
                            CapBot.AttemptToPickupComponentAtID(prize.PickupID);
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
                            CapBot.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if (!PLServer.Instance.HasActiveMissionWithID(43932) && !PLServer.Instance.HasActiveMissionWithID(43938) && PLServer.Instance.CurrentCrewCredits >= 1000)
                    {
                        CapBot.MyBot.AI_TargetPos = new Vector3(123, -15, -345);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                        CapBot.MyBot.AI_TargetPos = new Vector3(132, -15, -278);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;

                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                            CapBot.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if ((PLServer.Instance.HasActiveMissionWithID(43938) && !PLServer.Instance.GetMissionWithID(43938).Ended) || (PLServer.Instance.HasActiveMissionWithID(43932) && !PLServer.Instance.GetMissionWithID(43932).Ended))
                    {
                        CapBot.MyBot.AI_TargetPos = new Vector3(123, -15, -345);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                        CapBot.MyBot.AI_TargetPos = new Vector3(129, -14, -270);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
                        }
                        else
                        {
                            CapBot.AttemptToPickupComponentAtID(prize.PickupID);
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
                            CapBot.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if (!PLServer.Instance.HasActiveMissionWithID(44085) && !PLServer.Instance.HasActiveMissionWithID(44088) && PLServer.Instance.CurrentCrewCredits >= 1000)
                    {
                        CapBot.MyBot.AI_TargetPos = new Vector3(115, -7, -233);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                        CapBot.MyBot.AI_TargetPos = new Vector3(106, -7, -234);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;

                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                            CapBot.MyBot.AI_TargetTLI = teleport;
                            break;
                        }
                    }
                    if ((PLServer.Instance.HasActiveMissionWithID(44085) && !PLServer.Instance.GetMissionWithID(44085).Ended) || (PLServer.Instance.HasActiveMissionWithID(44088) && !PLServer.Instance.GetMissionWithID(44088).Ended))
                    {
                        CapBot.MyBot.AI_TargetPos = new Vector3(115, -7, -233);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
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
                        CapBot.MyBot.AI_TargetPos = new Vector3(110, -6, -226);
                        CapBot.MyBot.AI_TargetPos_Raw = CapBot.MyBot.AI_TargetPos;
                        if ((CapBot.MyBot.AI_TargetPos - CapBot.GetPawn().transform.position).sqrMagnitude > 4)
                        {
                            CapBot.MyBot.EnablePathing = true;
                        }
                        else
                        {
                            CapBot.AttemptToPickupComponentAtID(prize.PickupID);
                        }
                    }
                    LastAction = Time.time;
                    return;
                }
            }
        }
        static void SetNextDestiny()
        {
            if (PLEncounterManager.Instance.PlayerShip == null) return;
            List<PLSectorInfo> destines = new List<PLSectorInfo>();
            List<PLSectorInfo> priorityDestines = new List<PLSectorInfo>();
            PLSectorInfo GWG = PLGlobal.Instance.Galaxy.GetSectorOfVisualIndication(ESectorVisualIndication.GWG);
            float nearestWarpGatedist = 500;
            PLSectorInfo nearestWarpGate = null;
            PLSectorInfo nearestWarpGatetoDest = null;
            PLSectorInfo nearestDestiny = null;
            if (PLEncounterManager.Instance.PlayerShip.GetCombatLevel() > 80 && PLServer.Instance.GetNumFragmentsCollected() >= 4 && PLServer.Instance.CurrentCrewLevel >= 10)
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
                        bool isPriority = false;
                        bool foundSector = false;
                        List<int> sectors = new List<int>();
                        foreach (PLMissionObjective objective in mission.Objectives)
                        {
                            if (objective is PLMissionObjective_CompleteWithinJumpCount)
                            {
                                isPriority = true;
                            }
                            else if (objective is PLMissionObjective_ReachSector && !objective.IsCompleted)
                            {
                                sectors.Add((objective as PLMissionObjective_ReachSector).SectorToReach);
                            }
                        }
                        foreach (PLSectorInfo plsectorInfo in PLGlobal.Instance.Galaxy.AllSectorInfos.Values)
                        {
                            if (plsectorInfo.MissionSpecificID == mission.MissionTypeID && plsectorInfo != GWG && !plsectorInfo.Visited && PLStarmap.ShouldShowSector(plsectorInfo))
                            {
                                if (isPriority) priorityDestines.Add(plsectorInfo);
                                else destines.Add(plsectorInfo);
                                foundSector = true;
                                break;
                            }
                        }
                        if (!foundSector && sectors.Count > 0)
                        {
                            if (isPriority) priorityDestines.Add(PLGlobal.Instance.Galaxy.AllSectorInfos[sectors[0]]);
                            else destines.Add(PLGlobal.Instance.Galaxy.AllSectorInfos[sectors[0]]);
                        }
                        switch (mission.MissionTypeID)
                        {
                            case 0:
                                if (mission.Objectives[0].IsCompleted && mission.Objectives[1].IsCompleted && mission.Objectives[2].IsCompleted)
                                {
                                    destines.Add(PLGlobal.Instance.Galaxy.AllSectorInfos[0]);
                                }
                                break;
                            case 25:
                            case 68:
                            case 71:
                            case 72:
                            case 780:
                            case 2437:
                            case 2580:
                            case 104851:
                                if (mission.Objectives[0].IsCompleted && mission.Objectives[1].IsCompleted)
                                {
                                    destines.Add(PLGlobal.Instance.Galaxy.AllSectorInfos[0]);
                                }
                                break;
                            case 69:
                            case 264:
                            case 683:
                                if (mission.Objectives[0].IsCompleted)
                                {
                                    destines.Add(PLGlobal.Instance.Galaxy.AllSectorInfos[0]);
                                }
                                break;
                        }
                    }
                }
                if (priorityDestines.Count > 0)
                {
                    destines.Clear();
                    destines.AddRange(priorityDestines);
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
                        if (PLServer.Instance.CurrentCrewCredits >= 80000 && plsectorInfo.VisualIndication == ESectorVisualIndication.SPACE_SCRAPYARD && !plsectorInfo.Visited) //Add not visited scrapyards
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
    [HarmonyPatch(typeof(PLBotController), "HandleMovement")]
    class Rotation 
    {
        static void Postfix(PLBotController __instance) 
        {
            if(__instance.Bot_TargetXRot == 0f) 
            {
               __instance.Bot_TargetXRot = __instance.TargetRot.eulerAngles.x;
                __instance.StoredXRot = Mathf.LerpAngle(__instance.StoredXRot, __instance.Bot_TargetXRot, Time.deltaTime * 5f);
            }
        }
    }
    [HarmonyPatch(typeof(PLUIClassSelectionMenu), "Update")]
    class SpawnBot
    {
        public static bool capisbot = false;
        public static float delay = 0f;
        public static bool crewisbot = false;
        static void Postfix()
        {
            if (PLEncounterManager.Instance.PlayerShip != null && PLServer.Instance.GetCachedFriendlyPlayerOfClass(0, PLEncounterManager.Instance.PlayerShip) == null && delay > 5f && PhotonNetwork.isMasterClient && !capisbot)
            {
                capisbot = true;
                PLServer.Instance.ServerAddCrewBotPlayer(0);
                PLServer.Instance.GameHasStarted = true;
                PLServer.Instance.CrewPurchaseLimitsEnabled = false;
                PLGlobal.Instance.LoadedAIData = PLGlobal.Instance.GenerateDefaultPriorities();
                PLServer.Instance.SetCustomCaptainOrderText(0, "Use the WarpGate!", false);
                PLServer.Instance.SetCustomCaptainOrderText(1, "Engage Repair Protocols!", false);
                PLServer.Instance.SetCustomCaptainOrderText(2, "Align and Jump!", false);
                PLServer.Instance.SetCustomCaptainOrderText(3, "Collect Missions!", false);
                PLServer.Instance.SetCustomCaptainOrderText(4, "Explore Planet!", false);
                PLServer.Instance.SetCustomCaptainOrderText(5, "Complete Mission!", false);
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
            SpawnBot.crewisbot = false;
        }
    }
    [HarmonyPatch(typeof(PLTabMenu), "BeginDrag_SCD")]
    class DragComp
    {
        static void Postfix(PLTabMenu __instance, PLTabMenu.ShipComponentDisplay inSCD)
        {
            if (inSCD == null || inSCD.Component == null)
            {
                return;
            }
            if (PLNetworkManager.Instance.LocalPlayer != null && PhotonNetwork.isMasterClient && !inSCD.Component.Slot.Locked && SpawnBot.capisbot)
            {
                PLDraggedShipCompUI.Instance.DraggedComponent = inSCD.Component;
            }
        }
    }
    [HarmonyPatch(typeof(PLOverviewPlayerInfoDisplay), "UpdateButtons")]
    class AddBots 
    {
        static void Postfix(PLOverviewPlayerInfoDisplay __instance) 
        {
            __instance.ButtonsActiveTypes.Clear();
            if (__instance.MyPlayer == null)
            {
                if (PLNetworkManager.Instance.LocalPlayer != null && ((PhotonNetwork.isMasterClient && SpawnBot.capisbot) || PLNetworkManager.Instance.LocalPlayer.GetClassID() == 0))
                {
                    if (PLServer.Instance.GetCachedFriendlyPlayerOfClass(1) == null)
                    {
                        __instance.ButtonsActiveTypes.Add(PLOverviewPlayerInfoDisplay.EPlayerButtonType.E_ADD_BOT_PILOT);
                    }
                    if (PLServer.Instance.GetCachedFriendlyPlayerOfClass(2) == null)
                    {
                        __instance.ButtonsActiveTypes.Add(PLOverviewPlayerInfoDisplay.EPlayerButtonType.E_ADD_BOT_SCI);
                    }
                    if (PLServer.Instance.GetCachedFriendlyPlayerOfClass(3) == null)
                    {
                        __instance.ButtonsActiveTypes.Add(PLOverviewPlayerInfoDisplay.EPlayerButtonType.E_ADD_BOT_WEAP);
                    }
                    if (PLServer.Instance.GetCachedFriendlyPlayerOfClass(4) == null)
                    {
                        __instance.ButtonsActiveTypes.Add(PLOverviewPlayerInfoDisplay.EPlayerButtonType.E_ADD_BOT_ENG);
                    }
                }
            }
            else
            {
                if (__instance.MyPlayer.IsBot && PLNetworkManager.Instance.LocalPlayer != null && ((PhotonNetwork.isMasterClient && SpawnBot.capisbot) || PLNetworkManager.Instance.LocalPlayer.GetClassID() == 0) && __instance.MyPlayer.GetClassID() != 0)
                {
                    __instance.ButtonsActiveTypes.Add(PLOverviewPlayerInfoDisplay.EPlayerButtonType.E_REMOVE_BOT);
                }
                if (SteamManager.Initialized && __instance.MyPlayer.SteamIDIsVisible && __instance.MyPlayer.GetPhotonPlayer() != null && __instance.MyPlayer.GetPhotonPlayer().SteamID != CSteamID.Nil)
                {
                    __instance.ButtonsActiveTypes.Add(PLOverviewPlayerInfoDisplay.EPlayerButtonType.E_ADD_FRIEND);
                }
                if (PLNetworkManager.Instance.LocalPlayer != __instance.MyPlayer && __instance.MyPlayer.TS_ValidClientID && PLVoiceChatManager.Instance.GetIsFullyStarted())
                {
                    __instance.ButtonsActiveTypes.Add(PLOverviewPlayerInfoDisplay.EPlayerButtonType.E_MUTE);
                }
                if (!__instance.MyPlayer.IsBot && PLNetworkManager.Instance.LocalPlayer != null && ((PhotonNetwork.isMasterClient && SpawnBot.capisbot) || PLNetworkManager.Instance.LocalPlayer.GetClassID() == 0) && __instance.MyPlayer.GetPhotonPlayer() != null && __instance.MyPlayer.GetClassID() != 0 && !__instance.MyPlayer.GetPhotonPlayer().isMasterClient)
                {
                    __instance.ButtonsActiveTypes.Add(PLOverviewPlayerInfoDisplay.EPlayerButtonType.E_KICK);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                if (i < __instance.Buttons.Length)
                {
                    __instance.Buttons[i].MyPID = __instance;
                    if (i < __instance.ButtonsActiveTypes.Count)
                    {
                        if (__instance.Buttons[i].m_Label != null && !__instance.Buttons[i].m_Label.gameObject.activeSelf)
                        {
                            __instance.Buttons[i].m_Label.gameObject.SetActive(true);
                        }
                        __instance.Buttons[i].m_Label.text = __instance.GetStringFromButtonType(__instance.ButtonsActiveTypes[i]);
                    }
                    else if (__instance.Buttons[i].m_Label != null && __instance.Buttons[i].m_Label.gameObject.activeSelf)
                    {
                        __instance.Buttons[i].m_Label.gameObject.SetActive(false);
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(PLTabMenu), "LocalPlayerCanEditTalentsOfPlayer")]
    class TalentsOfBots 
    {
        static void Postfix(PLPlayer inPlayer, ref bool __result) 
        {
            if (PLNetworkManager.Instance != null && inPlayer != null && PLNetworkManager.Instance.LocalPlayer != null)
            {
                if (inPlayer.IsBot && inPlayer.TeamID == 0 && SpawnBot.capisbot && PhotonNetwork.isMasterClient)
                {
                    __result = true;
                }
            }
        }
    }
}
