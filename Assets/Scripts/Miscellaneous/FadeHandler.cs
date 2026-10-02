using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class FadeHandler : MonoBehaviour
{
    [SerializeField] private Image blackScreen;
    [SerializeField] private float fadeDuration = 1.25f;

    public IEnumerator FadeIn()
    {
        yield return Fade(1f);
    }

    public IEnumerator FadeOut()
    {
        yield return Fade(0f);
    }

    private IEnumerator Fade(float target)
    {
        if (!blackScreen)
            yield break;

        float duration = Mathf.Max(0.05f, fadeDuration);
        Color color = blackScreen.color;
        float start = color.a;
        float t = 0f;
        blackScreen.raycastTarget = true;

        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            color.a = Mathf.Lerp(start, target, Mathf.Clamp01(t));
            blackScreen.color = color;
            yield return null;
        }

        color.a = target;
        blackScreen.color = color;
        blackScreen.raycastTarget = target > 0.01f;
    }
}
