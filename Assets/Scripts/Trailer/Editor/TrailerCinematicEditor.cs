using UnityEditor;
using UnityEngine;

namespace Airplane.Trailer.Editor
{
    [CustomEditor(typeof(TrailerCinematic))]
    public sealed class TrailerCinematicEditor : UnityEditor.Editor
    {
        [MenuItem("Tools/Airplane/Render Trailer to MP4")]
        public static void RenderTrailerMenu()
        {
            TrailerCinematic.StartRecordingNextPlay = true;
            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
            else
            {
                TrailerCinematic cinematic = Object.FindFirstObjectByType<TrailerCinematic>();
                if (cinematic)
                    cinematic.BeginMp4Recording();
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            TrailerCinematic cinematic = (TrailerCinematic)target;

            if (Application.isPlaying && cinematic.IsRecording)
            {
                if (GUILayout.Button("Stop MP4 Recording", GUILayout.Height(30f)))
                    cinematic.StopMp4Recording();
            }
            else
            {
                if (GUILayout.Button("Render Trailer to MP4", GUILayout.Height(30f)))
                {
                    if (Application.isPlaying)
                        cinematic.BeginMp4Recording();
                    else
                    {
                        TrailerCinematic.StartRecordingNextPlay = true;
                        EditorApplication.isPlaying = true;
                    }
                }
            }
        }
    }
}
