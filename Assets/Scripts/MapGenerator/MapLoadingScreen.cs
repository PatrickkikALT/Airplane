using System.Collections;
using Airplane.Multiplayer;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MapLoadingScreen : MonoBehaviour
{
    [SerializeField] private GameObject loadingRoot;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Slider progress;
    [SerializeField] private ShaderVariantCollection shaderVariants;
    [SerializeField] [Min(1)] private int variantsPerFrame = 16;

    private void Awake()
    {
        MapLoadGate.Hold();
        Show();
    }

    private void Start()
    {
        StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        yield return null;

        yield return CompileShaders();
        yield return GenerateMap();

        MapLoadGate.Release();
        yield return WaitForLocalAircraft();
        Hide();
    }

    private IEnumerator CompileShaders()
    {
        if (!shaderVariants)
        {
            Debug.LogWarning("MapLoadingScreen has no Shader Variant Collection assigned.", this);
            yield break;
        }

        if (shaderVariants.variantCount == 0 || shaderVariants.isWarmedUp)
            yield break;

        int total = shaderVariants.variantCount;
        int previous = -1;
        int stalled = 0;

        while (!shaderVariants.isWarmedUp)
        {
            bool done = shaderVariants.WarmUpProgressively(Mathf.Max(1, variantsPerFrame));
            int warmed = shaderVariants.warmedUpVariantCount;
            SetStatus("Compiling shaders  " + warmed + "/" + total);
            SetProgress(total == 0 ? 1f : warmed / (float)total);

            if (done || shaderVariants.isWarmedUp)
                break;

            if (warmed == previous)
            {
                stalled++;
                if (stalled > 5)
                {
                    shaderVariants.WarmUp();
                    SetProgress(1f);
                    break;
                }
            }
            else
            {
                stalled = 0;
            }

            previous = warmed;
            yield return null;
        }
    }

    private IEnumerator GenerateMap()
    {
        MapGenerator generator = GetComponent<MapGenerator>();
        if (!generator)
            generator = FindAnyObjectByType<MapGenerator>();
        if (!generator)
            yield break;

        SetStatus("Generating map");
        SetProgress(0f);
        generator.StartSpawningIslands();

        while (!generator.IsComplete)
        {
            int target = Mathf.Max(generator.IslandTarget, 1);
            SetStatus("Generating map  " + generator.IslandSpawned + "/" + generator.IslandTarget);
            SetProgress(generator.IslandTarget == 0 ? 1f : generator.IslandSpawned / (float)target);
            yield return null;
        }

        SetStatus("Generating map  " + generator.IslandSpawned + "/" + generator.IslandTarget);
        SetProgress(1f);
    }

    private IEnumerator WaitForLocalAircraft()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager || !manager.IsListening || !manager.IsClient)
            yield break;

        while (!NetworkedAircraft.Local)
        {
            if (!manager.IsListening)
                yield break;

            SetStatus("Loading player");
            yield return null;
        }
    }

    private void Show()
    {
        if (!loadingRoot || loadingRoot == gameObject)
            return;
        loadingRoot.SetActive(true);
    }

    private void Hide()
    {
        if (!loadingRoot || loadingRoot == gameObject)
            return;
        loadingRoot.SetActive(false);
    }

    private void SetStatus(string value)
    {
        if (!statusText || statusText.text == value)
            return;
        statusText.text = value;
    }

    private void SetProgress(float value)
    {
        if (!progress)
            return;
        progress.value = Mathf.Clamp01(value);
    }
}
