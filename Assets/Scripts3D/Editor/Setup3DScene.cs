using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Один раз автоматически создаёт сцену Assets/Scenes/Game3D.unity с объектом Game3D,
// создаёт материалы, назначает звуки и ставит сцену в Build Settings вместо 2D-сцены Game.
// Пересоздать вручную: меню Tools > Space Shooter 3D > Rebuild Game3D Scene.
[InitializeOnLoad]
public static class Setup3DScene
{
    const string ScenePath = "Assets/Scenes/Game3D.unity";
    const string MaterialFolder = "Assets/Materials3D";
    const string LitPath = MaterialFolder + "/Lit3D.mat";
    const string GlowPath = MaterialFolder + "/Glow3D.mat";

    static Setup3DScene()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath))
            {
                Build();
            }
        };
    }

    [MenuItem("Tools/Space Shooter 3D/Rebuild Game3D Scene")]
    public static void Build()
    {
        Material lit = GetOrCreateMaterial(LitPath, "Standard", m =>
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });
        Material glow = GetOrCreateMaterial(GlowPath, "Legacy Shaders/Particles/Additive", m =>
        {
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        });

        Scene active = SceneManager.GetActiveScene();
        bool activeIsUntitled = !active.IsValid() || string.IsNullOrEmpty(active.path);
        if (activeIsUntitled && active.IsValid() && active.isDirty)
        {
            Debug.LogWarning("[Space Shooter 3D] Сохраните текущую сцену и выберите Tools > Space Shooter 3D > Rebuild Game3D Scene.");
            return;
        }
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                  activeIsUntitled ? NewSceneMode.Single : NewSceneMode.Additive);

        GameObject go = new GameObject("Game3D");
        Game3D game = go.AddComponent<Game3D>();
        game.LaserClip = FindClip("laser_shot");
        game.ExplosionClip = FindClip("explosion_sound");
        game.PowerupClip = FindClip("power_up_sound");
        game.MusicClip = FindClip("music_background");
        game.LitMaterial = lit;
        game.GlowMaterial = glow;
        SceneManager.MoveGameObjectToScene(go, scene);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        if (!activeIsUntitled && active != scene)
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[Space Shooter 3D] Сцена " + ScenePath + " создана и добавлена в Build Settings (индекс 1). Откройте её и нажмите Play.");
    }

    static Material GetOrCreateMaterial(string path, string shaderName, System.Action<Material> setup)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        Directory.CreateDirectory(MaterialFolder);
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("[Space Shooter 3D] Не найден шейдер " + shaderName);
            return null;
        }
        m = new Material(shader);
        setup(m);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static AudioClip FindClip(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:AudioClip"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == name)
            {
                return AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            }
        }
        Debug.LogWarning("[Space Shooter 3D] Не найден звук " + name);
        return null;
    }

    // Main_Menu (0) -> Game3D (1). Старая 2D-сцена остаётся в списке, но выключена.
    static void UpdateBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
        EditorBuildSettingsScene menu = null;
        List<EditorBuildSettingsScene> others = new List<EditorBuildSettingsScene>();

        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (s.path == ScenePath) continue;
            if (s.path.EndsWith("Main_Menu.unity")) menu = s;
            else others.Add(new EditorBuildSettingsScene(s.path, false));
        }

        if (menu != null) scenes.Add(menu);
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        scenes.AddRange(others);
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
