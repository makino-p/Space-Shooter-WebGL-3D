using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Подключение импортированных паков кораблей:
//  - Federation Corvette F3  -> корабль игрока (материалы пака на Standard работают как есть);
//  - Star Sparrow (Ebal Studios) -> враги и босс. Родные материалы пака сделаны в ShaderGraph и в Built-in
//    рендере розовые, поэтому создаём для них Standard-материалы из текстур пака.
public static class ImportedShips3D
{
    const string F3Root = "Assets/F3_Corvette";
    const string PlayerPrefabPath = F3Root + "/Prefabs/F3_Grey Variant.prefab";

    const string SparrowRoot = "Assets/StarSparrow";
    const string SparrowExamples = SparrowRoot + "/Prefabs/Examples/";
    const string SparrowStdTextures = SparrowRoot + "/Legacy/Standard/Textures/";
    const string SparrowNormal = SparrowRoot + "/Textures/StarSparrow_Normal.png";
    const string SparrowMatFolder = "Assets/Materials3D/StarSparrow";

    // Цвета Star Sparrow по типам врагов (порядок = Enemy3DType) + босс последним.
    public static readonly string[] EnemyColors = { "Red", "Green", "Orange", "Purple", "Cyan", "Black", "Grey" };
    // Модели кораблей для каждого типа врага и для босса (номера примеров пака).
    public static readonly int[] EnemyShipNumbers = { 3, 8, 14, 21, 27, 33, 40 };

    public static bool HasF3 { get { return File.Exists(PlayerPrefabPath); } }
    public static bool HasSparrow { get { return Directory.Exists(SparrowExamples); } }

    public static bool NeedsSetup()
    {
        if (!HasSparrow) return false;
        foreach (string c in EnemyColors)
        {
            if (!File.Exists(MatPath(c))) return true;
        }
        return false;
    }

    static string MatPath(string color) { return SparrowMatFolder + "/StarSparrow_" + color + ".mat"; }

    [MenuItem("Tools/Space Shooter 3D/Setup Imported Ships (F3 Corvette, Star Sparrow)")]
    public static void SetupMenu()
    {
        Setup();
        AssetDatabase.SaveAssets();
    }

    public static void Setup()
    {
        if (HasF3) LimitWebGLTextures(F3Root + "/Textures", 2048);
        if (HasSparrow) SetupSparrowMaterials();
        AssetDatabase.SaveAssets();
    }

    static void SetupSparrowMaterials()
    {
        Directory.CreateDirectory(SparrowMatFolder);

        TextureImporter normalImporter = AssetImporter.GetAtPath(SparrowNormal) as TextureImporter;
        if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }

        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(SparrowNormal);
        Texture2D metal = AssetDatabase.LoadAssetAtPath<Texture2D>(SparrowStdTextures + "StarSparrow_MetallicSmoothness_Standard.png");
        Texture2D emission = AssetDatabase.LoadAssetAtPath<Texture2D>(SparrowStdTextures + "StarSparrow_Emission.png");

        foreach (string color in EnemyColors)
        {
            string path = MatPath(color);
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(Shader.Find("Standard"));

            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(SparrowStdTextures + "StarSparrow_" + color + ".png"));
            m.color = Color.white;
            if (metal != null)
            {
                m.SetTexture("_MetallicGlossMap", metal);
                m.SetFloat("_GlossMapScale", 1f);
                m.EnableKeyword("_METALLICGLOSSMAP");
            }
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            m.EnableKeyword("_EMISSION");
            if (emission != null)
            {
                m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", new Color(1.6f, 1.6f, 1.6f));
            }
            else
            {
                m.SetColor("_EmissionColor", Color.black);
            }
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
        }

        LimitWebGLTextures(SparrowRoot + "/Legacy/Standard/Textures", 1024);
        LimitWebGLTextures(SparrowRoot + "/Textures", 1024);
    }

    // Для сайта (WebGL) уменьшаем тяжёлые текстуры; в редакторе остаётся полное качество.
    static void LimitWebGLTextures(string folder, int max)
    {
        if (!Directory.Exists(folder)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) continue;
            TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings("WebGL");
            if (ps.overridden && ps.maxTextureSize == max) continue;
            ps.overridden = true;
            ps.maxTextureSize = max;
            ti.SetPlatformTextureSettings(ps);
            ti.SaveAndReimport();
        }
    }

    public static bool SceneNeedsAssets(string sceneYaml)
    {
        return (HasF3 && !FieldHasGuid(sceneYaml, "PlayerShipPrefab:"))
            || (HasSparrow && !FieldHasGuid(sceneYaml, "EnemyShipPrefabs:"));
    }

    static bool FieldHasGuid(string yaml, string field)
    {
        int i = yaml.IndexOf(field);
        return i >= 0 && yaml.Substring(i, System.Math.Min(90, yaml.Length - i)).Contains("guid:");
    }

    public static void AssignTo(Game3D game)
    {
        game.PlayerShipPrefab = HasF3 ? AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) : null;

        List<GameObject> ships = new List<GameObject>();
        List<Material> mats = new List<Material>();
        if (HasSparrow)
        {
            for (int i = 0; i < EnemyShipNumbers.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SparrowExamples + "StarSparrow" + EnemyShipNumbers[i] + ".prefab");
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath(EnemyColors[i]));
                if (prefab == null || mat == null) continue;
                ships.Add(prefab);
                mats.Add(mat);
            }
        }
        game.EnemyShipPrefabs = ships.ToArray();
        game.EnemyShipMaterials = mats.ToArray();
    }
}
