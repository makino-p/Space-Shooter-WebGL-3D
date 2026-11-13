using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Автоматическая настройка 3D-проекта (срабатывает после каждой компиляции скриптов):
//  - Game3D — единственная сцена в Build Settings, игра сразу стартует в 3D;
//  - из 2D-спрайтов (корабль игрока, враг, астероид) строятся объёмные меши и материалы;
//  - сцена Assets/Scenes/Game3D.unity создаётся/обновляется, ей назначаются звуки, материалы и меши.
// Вручную: Tools > Space Shooter 3D > Rebuild Game3D Scene / Rebuild Sprite Models.
[InitializeOnLoad]
public static class Setup3DScene
{
    const string ScenePath = "Assets/Scenes/Game3D.unity";
    const string MaterialFolder = "Assets/Materials3D";
    const string ModelFolder = "Assets/Models3D";
    const string LitPath = MaterialFolder + "/Lit3D.mat";
    const string GlowPath = MaterialFolder + "/Glow3D.mat";

    const string SpriteRoot = "Assets/GameDevHQ/FileBase/Projects/Tutorials/Starter_Files/2D_Galaxy_Assets/Sprites/";
    const string PlayerSprite = SpriteRoot + "Player_Turn_Left/Player Turn Left0000.png";
    const string EnemySprite = SpriteRoot + "Enemy_Explode_Sequence/EnemyTurnLeft0000_00000.png";
    const string AsteroidSprite = SpriteRoot + "Asteroid.png";

    const string PlayerMeshPath = ModelFolder + "/PlayerShip.asset";
    const string EnemyMeshPath = ModelFolder + "/EnemyShip.asset";
    const string AsteroidMeshPath = ModelFolder + "/Asteroid.asset";
    const string PlayerMatPath = MaterialFolder + "/PlayerShip.mat";
    const string EnemyMatPath = MaterialFolder + "/EnemyShip.mat";
    const string AsteroidMatPath = MaterialFolder + "/Asteroid.mat";

    static Setup3DScene()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureBuildSettings();

            bool modelsMissing = !File.Exists(PlayerMeshPath) || !File.Exists(EnemyMeshPath) || !File.Exists(AsteroidMeshPath);
            if (!File.Exists(ScenePath))
            {
                Build();
            }
            else if (modelsMissing || SceneNeedsAssets() || ThirdPartyAssets3D.NeedsSetup() || ImportedShips3D.NeedsSetup() || RealisticPlanets3D.NeedsSetup())
            {
                if (modelsMissing) BuildSpriteModels();
                ThirdPartyAssets3D.Setup();
                ImportedShips3D.Setup();
                RealisticPlanets3D.Setup();
                AssignAssetsToScene();
            }
        };
    }

    // ---------------- Сцена ----------------
    [MenuItem("Tools/Space Shooter 3D/Rebuild Game3D Scene")]
    public static void Build()
    {
        BuildSpriteModels();
        ThirdPartyAssets3D.Setup();
        ImportedShips3D.Setup();
        RealisticPlanets3D.Setup();

        Scene active = SceneManager.GetActiveScene();
        bool activeIsUntitled = !active.IsValid() || string.IsNullOrEmpty(active.path);
        if (activeIsUntitled && active.IsValid() && active.isDirty)
        {
            Debug.LogWarning("[Space Shooter 3D] Сохраните текущую сцену и выберите Tools > Space Shooter 3D > Rebuild Game3D Scene.");
            return;
        }
        if (active.IsValid() && active.path == ScenePath)
        {
            // сцена уже открыта — просто обновить в ней назначения
            AssignAssetsToScene();
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                  activeIsUntitled ? NewSceneMode.Single : NewSceneMode.Additive);
        GameObject go = new GameObject("Game3D");
        Assign(go.AddComponent<Game3D>());
        SceneManager.MoveGameObjectToScene(go, scene);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        if (!activeIsUntitled && active != scene)
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        EnsureBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[Space Shooter 3D] Сцена " + ScenePath + " создана. Нажмите Play.");
    }

    // Назначить звуки/материалы/меши объекту Game3D в сцене (открывая её при необходимости).
    static void AssignAssetsToScene()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && string.IsNullOrEmpty(active.path))
            {
                if (active.isDirty)
                {
                    Debug.LogWarning("[Space Shooter 3D] Сохраните текущую сцену и выберите Tools > Space Shooter 3D > Rebuild Game3D Scene.");
                    return;
                }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                openedHere = true;
            }
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Game3D game = root.GetComponent<Game3D>();
            if (game != null)
            {
                Assign(game);
                EditorUtility.SetDirty(game);
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedHere) EditorSceneManager.CloseScene(scene, true);
        Debug.Log("[Space Shooter 3D] Модели из спрайтов назначены сцене Game3D.");
    }

    static bool SceneNeedsAssets()
    {
        // в YAML сцены поле пустое, если меш не назначен
        string yaml = File.ReadAllText(ScenePath);
        bool spriteMissing = !yaml.Contains("PlayerShipMesh: {fileID: 4300000") || !yaml.Contains("AsteroidMesh: {fileID: 4300000");
        bool skyMissing = ThirdPartyAssets3D.HasSky && !yaml.Contains("SkyboxMaterial: {fileID: 2100000");
        int ri = yaml.IndexOf("RockMaterials:");
        bool rocksAssigned = ri >= 0 && yaml.Substring(ri, System.Math.Min(60, yaml.Length - ri)).Contains("{fileID: 2100000");
        bool rocksMissing = ThirdPartyAssets3D.AvailableRocks().Count > 0 && !rocksAssigned;
        bool smokeMissing = !yaml.Contains("SmokeMaterial: {fileID: 2100000");
        return spriteMissing || skyMissing || rocksMissing || smokeMissing || ImportedShips3D.SceneNeedsAssets(yaml)
            || Soundtracks3D.SceneNeedsAssets(yaml) || RealisticPlanets3D.SceneNeedsAssets(yaml);
    }

    static void Assign(Game3D game)
    {
        game.LaserClip = FindClip("laser_shot");
        game.ExplosionClip = FindClip("explosion_sound");
        game.PowerupClip = FindClip("power_up_sound");
        game.MusicClip = FindClip("music_background");
        game.LitMaterial = GetOrCreateMaterial(LitPath, "Standard", m =>
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });
        game.GlowMaterial = GetOrCreateMaterial(GlowPath, "Legacy Shaders/Particles/Additive", m =>
        {
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        });
        game.SmokeMaterial = GetOrCreateMaterial(MaterialFolder + "/Smoke3D.mat", "Legacy Shaders/Particles/Alpha Blended", m =>
        {
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        });

        game.PlayerShipMesh = AssetDatabase.LoadAssetAtPath<Mesh>(PlayerMeshPath);
        game.EnemyShipMesh = AssetDatabase.LoadAssetAtPath<Mesh>(EnemyMeshPath);
        game.AsteroidMesh = AssetDatabase.LoadAssetAtPath<Mesh>(AsteroidMeshPath);
        game.PlayerShipMaterial = AssetDatabase.LoadAssetAtPath<Material>(PlayerMatPath);
        game.EnemyShipMaterial = AssetDatabase.LoadAssetAtPath<Material>(EnemyMatPath);
        game.AsteroidMaterial = AssetDatabase.LoadAssetAtPath<Material>(AsteroidMatPath);
        ThirdPartyAssets3D.AssignTo(game);
        ImportedShips3D.AssignTo(game);
        Soundtracks3D.AssignTo(game);
        RealisticPlanets3D.AssignTo(game);
    }

    // ---------------- Модели из спрайтов ----------------
    [MenuItem("Tools/Space Shooter 3D/Rebuild Sprite Models")]
    public static void RebuildSpriteModelsMenu()
    {
        BuildSpriteModels();
        AssignAssetsToScene();
    }

    static void BuildSpriteModels()
    {
        Directory.CreateDirectory(ModelFolder);
        Directory.CreateDirectory(MaterialFolder);

        SpriteMeshGenerator.Settings ship = new SpriteMeshGenerator.Settings
        {
            Grid = 96, PixelsPerUnit = 100f, MaxHeight = 0.42f, EdgeHeight = 0.04f,
            BottomFactor = 0.55f, LumaRelief = 0.16f, RoundProfile = false
        };
        SpriteMeshGenerator.Settings rock = new SpriteMeshGenerator.Settings
        {
            Grid = 64, PixelsPerUnit = 100f, MaxHeight = 0f, EdgeHeight = 0.05f,
            BottomFactor = 0.9f, LumaRelief = 0.25f, RoundProfile = true
        };

        SaveMesh(PlayerSprite, ship, PlayerMeshPath, "PlayerShip");
        SaveMesh(EnemySprite, ship, EnemyMeshPath, "EnemyShip");
        SaveMesh(AsteroidSprite, rock, AsteroidMeshPath, "Asteroid");

        SaveMaterial(PlayerMatPath, PlayerSprite, 0.45f, 0.5f);
        SaveMaterial(EnemyMatPath, EnemySprite, 0.5f, 0.55f);
        SaveMaterial(AsteroidMatPath, AsteroidSprite, 0.05f, 0.2f);
        AssetDatabase.SaveAssets();
    }

    static void SaveMesh(string pngPath, SpriteMeshGenerator.Settings settings, string meshPath, string name)
    {
        if (!File.Exists(pngPath))
        {
            Debug.LogError("[Space Shooter 3D] Не найден спрайт " + pngPath);
            return;
        }

        // читаем исходный PNG напрямую — не нужно включать Read/Write у текстуры
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(pngPath));
        Mesh mesh = SpriteMeshGenerator.Build(tex.GetPixels32(), tex.width, tex.height, settings, name);
        Object.DestroyImmediate(tex);

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, meshPath);
        }
    }

    static void SaveMaterial(string matPath, string pngPath, float metallic, float smoothness)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        bool isNew = m == null;
        if (isNew) m = new Material(Shader.Find("Standard"));

        m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
        m.color = Color.white;
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        if (isNew) AssetDatabase.CreateAsset(m, matPath);
        else EditorUtility.SetDirty(m);
    }

    // ---------------- Остальное ----------------
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

    // Единственная сцена в сборке — Game3D, поэтому игра сразу запускается в 3D.
    static void EnsureBuildSettings()
    {
        EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
        if (current.Length == 1 && current[0].path == ScenePath && current[0].enabled) return;
        if (!File.Exists(ScenePath)) return;
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}
