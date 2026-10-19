using Airplane.Extensions;
using Airplane.Multiplayer;
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
        NetworkConnection.Disconnect();
        SceneManager.SetActiveScene(SceneManager.GetSceneByBuildIndex(0));
    }
}
