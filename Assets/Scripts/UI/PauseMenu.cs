using System;
using Airplane.Extensions;
using Airplane.Multiplayer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseMenu : SingletonMonoBehaviour<PauseMenu>
{
    [SerializeField] private GameObject pauseMenu;
    [SerializeField] private GameObject settingsMenu;
    public void TogglePauseMenu()
    {
        pauseMenu.SetActive((!settingsMenu.activeSelf) && !pauseMenu.activeSelf);
    }

    public void Leave()
    {
        ulong currentClient = NetworkedAircraft.Local.OwnerClientId;
        if (NetworkManager.Singleton.IsHost)
            NetworkManager.Singleton.Shutdown();
        else
            NetworkManager.Singleton.DisconnectClient(currentClient);
        SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(0));
    }
}
