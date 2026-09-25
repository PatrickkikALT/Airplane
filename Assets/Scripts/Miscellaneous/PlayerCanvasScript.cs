using UnityEngine;

public class PlayerCanvasScript : MonoBehaviour
{
    public static PlayerCanvasScript Instance;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }
}
