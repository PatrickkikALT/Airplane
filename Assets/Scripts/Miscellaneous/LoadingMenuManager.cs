using System.Collections;
using Airplane.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadingMenuManager : SingletonMonoBehaviour<LoadingMenuManager>
{
    [Header("Shader Prewarming")]
    [SerializeField] private ShaderVariantCollection shaderVariants;
    [SerializeField] private SceneAsset targetScene;
    
    public static void StartLoadingScreen()
    {
        Instance.StartCoroutine(Instance.StartLoading());
    }

    private IEnumerator StartLoading()
    {
        if (shaderVariants && !shaderVariants.isWarmedUp)
        {
            UpdateLoadingUI("Compiling Shaders...");
            shaderVariants.WarmUp();
            yield return null; 
        }

        UpdateLoadingUI("Loading World...");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetScene.name);
        asyncLoad.allowSceneActivation = false;

        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        asyncLoad.allowSceneActivation = true;
    }

    private void UpdateLoadingUI(string message)
    {
        Debug.Log($"[Loading]: {message}");
    }
}