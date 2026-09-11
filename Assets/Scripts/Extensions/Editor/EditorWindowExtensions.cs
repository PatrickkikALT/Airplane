using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class EditorWindowExtensions
{
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
