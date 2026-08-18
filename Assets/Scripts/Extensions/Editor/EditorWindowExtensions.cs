using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class EditorWindowExtensions
{
    /// <summary>
    /// Checks if a specific type of EditorWindow is currently open in the editor.
    /// </summary>
    /// <typeparam name="T">The type of the EditorWindow to check for.</typeparam>
    /// <param name="window">
    /// An output parameter that holds the reference to the found window if it is open,
    /// or null if no window of the specified type is currently open.
    /// </param>
    /// <returns>
    /// Returns true if a window of the specified type is currently open; otherwise, false.
    /// </returns>
    public static bool IsWindowOpen<T>(out T window) where T : EditorWindow
    {
        T[] windows = Resources.FindObjectsOfTypeAll<T>();
        if (windows.Length > 0)
        {
            window = windows[0];
            return true;
        }
        window = null;
        return false;
    }
    
    
}
