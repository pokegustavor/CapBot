using HarmonyLib;
using Steamworks;
using System.Linq;

namespace CapBot
{
    internal class NonCaptainMenu
    {
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
                    if (!__instance.MyPlayer.IsBot && PLNetworkManager.Instance.LocalPlayer != null && ((PhotonNetwork.isMasterClient && SpawnBot.capisbot) || PLNetworkManager.Instance.LocalPlayer.GetClassID() == 0) && __instance.MyPlayer.GetPhotonPlayer() != null && __instance.MyPlayer.GetClassID() != 0 && !__instance.MyPlayer.GetPhotonPlayer().IsMasterClient)
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
    }
}
