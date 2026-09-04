using UnityEditor;
using UnityEngine;

namespace Airplane.Weather.Editor
{
    [CustomEditor(typeof(WeatherManager))]
    public class WeatherManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty weatherSystemProp;
        private SerializedProperty weatherPresetProp;
        private SerializedProperty currentPresetProp;
        private SerializedProperty volumeProp;
        private SerializedProperty transitionDurationProp;
        private void OnEnable()
        {
            volumeProp = serializedObject.FindProperty("volume");
            weatherSystemProp = serializedObject.FindProperty("weatherSystem");
            weatherPresetProp = serializedObject.FindProperty("presets");
            currentPresetProp = serializedObject.FindProperty("currentPreset");
            transitionDurationProp = serializedObject.FindProperty("transitionDuration");
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(volumeProp);
            EditorGUILayout.PropertyField(weatherSystemProp);
            EditorGUILayout.PropertyField(transitionDurationProp);
            EditorGUILayout.PropertyField(weatherPresetProp);

            if (weatherPresetProp.arraySize > 0)
            {
                string[] options = new string[weatherPresetProp.arraySize];
                for (int i = 0; i < weatherPresetProp.arraySize; i++)
                {
                    SerializedProperty presetProp = weatherPresetProp.GetArrayElementAtIndex(i);
                    string name = presetProp.FindPropertyRelative("Name").stringValue;
                    options[i] = name;
                }
                EditorGUI.BeginChangeCheck();
                currentPresetProp.intValue = EditorGUILayout.Popup("Current Preset", currentPresetProp.intValue, options);
                serializedObject.ApplyModifiedProperties();
                if (EditorGUI.EndChangeCheck())
                {
                    WeatherManager weatherManager = serializedObject.targetObject as WeatherManager;
                    weatherManager?.UpdateWeather();
                }
            }
            else
            {
                serializedObject.ApplyModifiedProperties();
            }
        }        
        
    }
}