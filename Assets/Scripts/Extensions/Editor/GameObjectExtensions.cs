using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public static class GameObjectExtensions
{
    private const string CopyMenuPath = "GameObject/Copy All Components";
    private const string PasteMenuPath = "GameObject/Paste All Components";
    private const string CopyContextPath = "CONTEXT/Transform/Copy All Components";
    private const string PasteContextPath = "CONTEXT/Transform/Paste All Components";
    private const int MenuPriority = 49;

    private static Component[] copiedComponents;

    [MenuItem(CopyMenuPath, false, MenuPriority)]
    private static void CopyAllComponentsMenu()
    {
        Selection.activeGameObject.CopyAllComponentsFrom();
    }

    [MenuItem(CopyMenuPath, true)]
    private static bool ValidateCopyAllComponentsMenu()
    {
        return Selection.activeGameObject != null;
    }

    [MenuItem(PasteMenuPath, false, MenuPriority + 1)]
    private static void PasteAllComponentsMenu()
    {
        var targets = Selection.gameObjects;
        if (targets == null || targets.Length == 0)
            return;

        Undo.SetCurrentGroupName("Paste All Components");
        int group = Undo.GetCurrentGroup();
        foreach (GameObject target in targets) target.PasteCopiedComponents();
        Undo.CollapseUndoOperations(group);
    }

    [MenuItem(PasteMenuPath, true)]
    private static bool ValidatePasteAllComponentsMenu()
    {
        return HasCopiedComponents() && Selection.activeGameObject != null;
    }

    [MenuItem(CopyContextPath)]
    private static void CopyAllComponentsContext(MenuCommand command)
    {
        Transform transform = command.context as Transform;
        if (transform != null) transform.gameObject.CopyAllComponentsFrom();
    }

    [MenuItem(PasteContextPath)]
    private static void PasteAllComponentsContext(MenuCommand command)
    {
        Transform transform = command.context as Transform;
        if (transform != null)
            transform.gameObject.PasteCopiedComponents();
    }

    [MenuItem(PasteContextPath, true)]
    private static bool ValidatePasteAllComponentsContext()
    {
        return HasCopiedComponents();
    }

    public static void CopyComponentsTo(this GameObject source, GameObject destination)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));

        CopyComponentsOnto(source.GetComponents<Component>(), destination);
    }

    public static void CopyAllComponentsFrom(this GameObject source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        copiedComponents = source.GetComponents<Component>();
    }

    public static void PasteCopiedComponents(this GameObject destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        if (!HasCopiedComponents())
            return;

        CopyComponentsOnto(copiedComponents, destination);
    }

    private static bool HasCopiedComponents()
    {
        if (copiedComponents == null)
            return false;

        for (int i = 0; i < copiedComponents.Length; i++)
        {
            Component component = copiedComponents[i];
            if (component != null && component is not Transform)
                return true;
        }

        return false;
    }

    private static void CopyComponentsOnto(Component[] sources, GameObject destination)
    {
        foreach (Component source in sources)
        {
            if (source == null || source is Transform)
                continue;

            Type type = source.GetType();
            bool disallowMultiple = Attribute.IsDefined(type, typeof(DisallowMultipleComponent), true);
            Component existing = destination.GetComponent(type);

            if (!ComponentUtility.CopyComponent(source))
                continue;

            if (disallowMultiple && existing != null)
                ComponentUtility.PasteComponentValues(existing);
            else
                ComponentUtility.PasteComponentAsNew(destination);
        }
    }
}