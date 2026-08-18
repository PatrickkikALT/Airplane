// using System;
// using UnityEditor;
// using UnityEditor.EditorTools;
// using UnityEngine;
// using UnityEngine.SceneManagement;
// using Utils.Core.SceneLockTool;
//
// [EditorTool("Lock Scene")]
// public class LockSceneEditorTool : EditorTool
// {
//     private Texture2D lockIconOn;
//     private Texture2D lockIconOff;
//     
//     public override GUIContent toolbarIcon => new GUIContent(SceneLockTool.IsSceneLocked(SceneManager.GetActiveScene().name) ? lockIconOff : lockIconOn);
//
//     public void OnEnable()
//     {
//         lockIconOn = EditorGUIUtility.IconContent("Locked").image as Texture2D;
//         lockIconOff = EditorGUIUtility.IconContent("Unlocked").image as Texture2D;
//     }
//
//     public override void OnActivated()
//     {
//     }
// }
