using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Airplane.UI.Editor
{
    public static class AircraftStatPanelEditor
    {
        [MenuItem("Airplane/UI/Create Aircraft Stat Panel")]
        public static void Create()
        {
            AircraftSelector selector = Selection.activeGameObject
                ? Selection.activeGameObject.GetComponent<AircraftSelector>()
                : null;
            if (!selector)
                selector = Object.FindAnyObjectByType<AircraftSelector>(FindObjectsInactive.Include);
            if (!selector)
            {
                Debug.LogError("No Aircraft Selector in the open scene.");
                return;
            }

            SerializedObject serialized = new SerializedObject(selector);
            SerializedProperty labelProperty = serialized.FindProperty("label");
            TMP_Text existingLabel = labelProperty != null ? labelProperty.objectReferenceValue as TMP_Text : null;
            RectTransform namePlate = existingLabel ? existingLabel.rectTransform.parent as RectTransform : null;
            if (!namePlate)
                namePlate = selector.transform as RectTransform;
            Transform parent = namePlate.parent ? namePlate.parent : namePlate;

            Transform existingPanel = namePlate.Find("Stat Panel");
            if (!existingPanel)
                existingPanel = parent.Find("Stat Panel");
            if (existingPanel && existingPanel.parent == parent)
            {
                PlaceBeside(existingPanel as RectTransform, namePlate);
                Selection.activeGameObject = existingPanel.gameObject;
                return;
            }

            Undo.SetCurrentGroupName("Create Aircraft Stat Panel");
            int undoGroup = Undo.GetCurrentGroup();
            TMP_FontAsset font = existingLabel ? existingLabel.font : TMP_Settings.defaultFontAsset;

            GameObject panel = CreateUi("Stat Panel", parent);
            if (existingPanel)
            {
                Undo.SetTransformParent(existingPanel, parent, "Create Aircraft Stat Panel");
                Object.DestroyImmediate(existingPanel.gameObject);
            }

            PlaceBeside(panel.GetComponent<RectTransform>(), namePlate);
            panel.transform.SetSiblingIndex(namePlate.GetSiblingIndex() + 1);
            VerticalLayoutGroup column = panel.AddComponent<VerticalLayoutGroup>();
            column.spacing = 4f;
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            LayoutElement panelLayout = panel.AddComponent<LayoutElement>();
            panelLayout.preferredHeight = 196f;
            panelLayout.flexibleWidth = 1f;

            TMP_Text mass = CreateRow(panel.transform, "Mass", font);
            TMP_Text speed = CreateRow(panel.transform, "Top speed", font);
            TMP_Text zeroThrust = CreateRow(panel.transform, "Zero thrust", font);
            TMP_Text thrust = CreateRow(panel.transform, "Thrust", font);
            TMP_Text roll = CreateRow(panel.transform, "Roll", font);
            TMP_Text pitch = CreateRow(panel.transform, "Pitch", font);

            serialized.FindProperty("massStat").objectReferenceValue = mass;
            serialized.FindProperty("speedStat").objectReferenceValue = speed;
            serialized.FindProperty("zeroThrustStat").objectReferenceValue = zeroThrust;
            serialized.FindProperty("thrustStat").objectReferenceValue = thrust;
            serialized.FindProperty("rollStat").objectReferenceValue = roll;
            serialized.FindProperty("pitchStat").objectReferenceValue = pitch;
            serialized.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);

            EditorSceneManager.MarkSceneDirty(selector.gameObject.scene);
            Selection.activeGameObject = panel;
        }

        private static void PlaceBeside(RectTransform panel, RectTransform namePlate)
        {
            if (!panel || !namePlate || panel == namePlate)
                return;

            float width = Mathf.Max(0.18f, namePlate.anchorMax.x - namePlate.anchorMin.x);
            float right = namePlate.anchorMax.x + width;
            if (right <= 1f)
            {
                panel.anchorMin = new Vector2(namePlate.anchorMax.x, namePlate.anchorMin.y);
                panel.anchorMax = new Vector2(right, namePlate.anchorMax.y);
            }
            else
            {
                panel.anchorMin = new Vector2(Mathf.Max(0f, namePlate.anchorMin.x - width), namePlate.anchorMin.y);
                panel.anchorMax = new Vector2(namePlate.anchorMin.x, namePlate.anchorMax.y);
            }

            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            panel.anchoredPosition = Vector2.zero;
            panel.localScale = Vector3.one;
            panel.localRotation = Quaternion.identity;
        }

        private static TMP_Text CreateRow(Transform parent, string title, TMP_FontAsset font)
        {
            GameObject row = CreateUi(title, parent);
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 28f;

            CreateText(row.transform, "Title", title, font, TextAlignmentOptions.MidlineLeft, 140f);
            return CreateText(row.transform, "Value", "—", font, TextAlignmentOptions.MidlineRight, 0f);
        }

        private static TMP_Text CreateText(Transform parent, string name, string value, TMP_FontAsset font, TextAlignmentOptions align, float preferredWidth)
        {
            GameObject textObject = CreateUi(name, parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = font;
            text.fontSize = 24f;
            text.color = Color.white;
            text.alignment = align;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            LayoutElement layout = textObject.AddComponent<LayoutElement>();
            if (preferredWidth > 0f)
                layout.preferredWidth = preferredWidth;
            else
                layout.flexibleWidth = 1f;
            return text;
        }

        private static GameObject CreateUi(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Aircraft Stat Panel");
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }
    }
}
