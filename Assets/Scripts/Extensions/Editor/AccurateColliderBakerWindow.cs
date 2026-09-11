using UnityEditor;
using UnityEngine;

namespace Airplane.Destruction.Editor
{
    public sealed class AccurateColliderBakerWindow : EditorWindow
    {
        private float _voxelSize = 2.5f;
        private int _maxVoxelsPerAxis = 24;
        private int _maxColliders = 16;
        private bool _solidFill = true;
        private bool _removeMeshColliders = true;
        private bool _addDestructible;

        [MenuItem("Tools/Airplane/Bake Accurate Colliders")]
        public static void Open()
        {
            var window = GetWindow<AccurateColliderBakerWindow>("Collider Baker");
            window.minSize = new Vector2(360f, 220f);
            window.Show();
        }

        [MenuItem("GameObject/Airplane/Bake Accurate Colliders", false, 49)]
        private static void BakeFromMenu()
        {
            BakeSelection(2.5f, 24, 16, true, true, false);
        }

        [MenuItem("GameObject/Airplane/Bake Accurate Colliders", true)]
        private static bool ValidateBakeFromMenu()
        {
            return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
        }

        [MenuItem("CONTEXT/Transform/Bake Accurate Colliders")]
        private static void BakeFromContext(MenuCommand command)
        {
            var transform = command.context as Transform;
            if (!transform)
                return;
            BakeOne(transform.gameObject, 2.5f, 24, 16, true, true, false);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Compound box colliders from mesh shape", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6f);
            _voxelSize = EditorGUILayout.Slider("Voxel Size (m)", _voxelSize, 0.5f, 40f);
            _maxVoxelsPerAxis = EditorGUILayout.IntSlider("Max Voxels Per Axis", _maxVoxelsPerAxis, 6, 40);
            _maxColliders = EditorGUILayout.IntSlider("Max Colliders", _maxColliders, 1, 48);
            _solidFill = EditorGUILayout.Toggle("Fill Solid Interior", _solidFill);
            _removeMeshColliders = EditorGUILayout.Toggle("Remove Mesh Colliders", _removeMeshColliders);
            _addDestructible = EditorGUILayout.Toggle("Add Destructible Mesh", _addDestructible);

            EditorGUILayout.Space(8f);
            int selected = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
            using (new EditorGUI.DisabledScope(selected == 0))
            {
                if (GUILayout.Button($"Bake Selected ({selected})"))
                    BakeSelection(_voxelSize, _maxVoxelsPerAxis, _maxColliders, _solidFill, _removeMeshColliders, _addDestructible);
            }

            if (GUILayout.Button("Clear Baked Colliders On Selected"))
                ClearSelection();
        }

        private static void BakeSelection(
            float voxelSize,
            int maxVoxels,
            int maxColliders,
            bool solidFill,
            bool removeMeshColliders,
            bool addDestructible)
        {
            GameObject[] targets = Selection.gameObjects;
            if (targets == null || targets.Length == 0)
                return;

            Undo.SetCurrentGroupName("Bake Accurate Colliders");
            int group = Undo.GetCurrentGroup();
            int total = 0;
            for (int i = 0; i < targets.Length; i++)
                total += BakeOne(targets[i], voxelSize, maxVoxels, maxColliders, solidFill, removeMeshColliders, addDestructible);
            Undo.CollapseUndoOperations(group);
            Debug.Log($"Baked {total} box colliders on {targets.Length} object(s).");
        }

        private static int BakeOne(
            GameObject target,
            float voxelSize,
            int maxVoxels,
            int maxColliders,
            bool solidFill,
            bool removeMeshColliders,
            bool addDestructible)
        {
            if (!target)
                return 0;

            Undo.RegisterFullObjectHierarchyUndo(target, "Bake Accurate Colliders");
            int count = CompoundBoxColliderBaker.Bake(
                target.transform,
                voxelSize,
                maxVoxels,
                solidFill,
                removeMeshColliders,
                maxColliders);

            Transform child = target.transform.Find(CompoundBoxColliderBaker.ChildName);
            if (child)
                Undo.RegisterCreatedObjectUndo(child.gameObject, "Bake Accurate Colliders");

            if (addDestructible && !target.GetComponent<DestructibleMesh>())
                Undo.AddComponent<DestructibleMesh>(target);

            return count;
        }

        private static void ClearSelection()
        {
            GameObject[] targets = Selection.gameObjects;
            if (targets == null)
                return;

            Undo.SetCurrentGroupName("Clear Baked Colliders");
            int group = Undo.GetCurrentGroup();
            for (int i = 0; i < targets.Length; i++)
            {
                if (!targets[i])
                    continue;
                Undo.RegisterFullObjectHierarchyUndo(targets[i], "Clear Baked Colliders");
                CompoundBoxColliderBaker.Clear(targets[i].transform);
            }

            Undo.CollapseUndoOperations(group);
        }
    }
}
