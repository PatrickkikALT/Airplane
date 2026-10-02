using TMPro;
using UnityEngine;

public class RoundScoreHud : MonoBehaviour
{
    public static RoundScoreHud Instance;

    [SerializeField] private TMP_Text scoreText;

    private void Awake()
    {
        Instance = this;
        SetScore(0);
    }

    public void SetScore(int points)
    {
        if (scoreText)
            scoreText.text = points.ToString();
    }
}
