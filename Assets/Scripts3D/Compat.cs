using UnityEngine;

// Код, который отличается между Unity 2019 и Unity 6 — чтобы проект компилировался в обеих версиях.
public static class Compat
{
    private static Font _font;

    // В Unity 2022.2+ встроенный Arial.ttf удалён, вместо него LegacyRuntime.ttf.
    public static Font DefaultFont
    {
        get
        {
            if (_font == null)
            {
#if UNITY_2022_2_OR_NEWER
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }
            return _font;
        }
    }

    // FindObjectsOfType устарел в Unity 6 — используем FindObjectsByType.
    public static T[] FindAll<T>() where T : Object
    {
#if UNITY_6000_6_OR_NEWER
        return Object.FindObjectsByType<T>();
#elif UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>();
#endif
    }
}
