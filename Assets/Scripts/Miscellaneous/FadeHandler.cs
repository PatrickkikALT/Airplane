using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class FadeHandler : MonoBehaviour
{
    [SerializeField] private Image blackScreen;


    [SerializeField] private Animator fadeAnimator;
    [SerializeField] private float fadeDuration;

    public void StartFadeIn()
    {
        StartCoroutine(FadeIn());
    }

    public void StartFadeOut()
    {
        StartCoroutine(FadeOut());
    }
    public IEnumerator FadeIn()
    {
        float t = 0;
        while (t < 1)
        {
            t += Time.deltaTime / fadeDuration;
            blackScreen.color = new Color(blackScreen.color.r, blackScreen.color.g, blackScreen.color.b, t);
            yield return null;
        }
    }

    public IEnumerator FadeOut()
    {
        float t = 1;

        while (t > 0)
        {
            t -= Time.deltaTime / fadeDuration;
            blackScreen.color = new Color(blackScreen.color.r, blackScreen.color.g, blackScreen.color.b, t);
            yield return null;
        }
    }
}
