using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Подключение скачанных бесплатных ассетов:
//  - NASA Deep Star Maps 2020 (public domain) -> skybox с настоящим звёздным небом;
//  - Poly Haven moon_rock_XX (CC0) -> детальные астероиды с PBR-материалами.
// Настраивает импорт (размеры текстур, normal map, WebGL-ограничения) и создаёт материалы.
public static class ThirdPartyAssets3D
{
    public const string Root = "Assets/ThirdParty";
    public const string SkyTexturePath = Root + "/NASA/milkyway_2020_8k.exr";
    public const string SkyMaterialPath = Root + "/NASA/Skybox_MilkyWay.mat";
    public const string RocksFolder = Root + "/PolyHaven";
    public static readonly string[] RockIds = { "moon_rock_01", "moon_rock_03", "moon_rock_04", "moon_rock_05" };

    static string RockFbx(string id) { return RocksFolder + "/" + id + "/" + id + "_4k.fbx"; }
    static string RockTex(string id, string kind, string ext) { return RocksFolder + "/" + id + "/textures/" + id + "_" + kind + "_4k." + ext; }
    static string RockMat(string id) { return RocksFolder + "/" + id + "/" + id + ".mat"; }
    static string RockMetalSmooth(string id) { return RocksFolder + "/" + id + "/textures/" + id + "_metal_smooth.png"; }

    public static bool HasSky { get { return File.Exists(SkyTexturePath); } }

    public static List<string> AvailableRocks()
    {
        List<string> list = new List<string>();
        foreach (string id in RockIds)
        {
            if (File.Exists(RockFbx(id)) && File.Exists(RockTex(id, "diff", "jpg"))) list.Add(id);
        }
        return list;
    }

    // Нужна ли настройка (файлы есть, а материалов ещё нет).
    public static bool NeedsSetup()
    {
        if (HasSky && !File.Exists(SkyMaterialPath)) return true;
        foreach (string id in AvailableRocks())
        {
            if (!File.Exists(RockMat(id))) return true;
        }
        return false;
    }

    [MenuItem("Tools/Space Shooter 3D/Setup Downloaded Assets (NASA sky, Poly Haven rocks)")]
    public static void SetupMenu()
    {
        Setup();
        AssetDatabase.SaveAssets();
    }

    public static void Setup()
    {
        if (HasSky) SetupSky();
        foreach (string id in AvailableRocks()) SetupRock(id);
        AssetDatabase.SaveAssets();
    }

    // ---------------- Небо ----------------
    static void SetupSky()
    {
        TextureImporter ti = AssetImporter.GetAtPath(SkyTexturePath) as TextureImporter;
        if (ti != null)
        {
            bool changed = false;
            if (ti.textureShape != TextureImporterShape.Texture2D) { ti.textureShape = TextureImporterShape.Texture2D; changed = true; }
            if (ti.maxTextureSize != 8192) { ti.maxTextureSize = 8192; changed = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (ti.wrapModeU != TextureWrapMode.Repeat || ti.wrapModeV != TextureWrapMode.Clamp)
            {
                ti.wrapModeU = TextureWrapMode.Repeat;
                ti.wrapModeV = TextureWrapMode.Clamp;
                changed = true;
            }
            changed |= SetPlatformMax(ti, "WebGL", 4096);
            if (changed) ti.SaveAndReimport();
        }

        Material m = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
        bool isNew = m == null;
        Shader shader = Shader.Find("Skybox/Panoramic");
        if (shader == null)
        {
            Debug.LogError("[Space Shooter 3D] Не найден шейдер Skybox/Panoramic");
            return;
        }
        if (isNew) m = new Material(shader);
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(SkyTexturePath));
        m.SetFloat("_Mapping", 1f);       // Latitude-Longitude
        m.SetFloat("_ImageType", 0f);     // 360°
        m.SetFloat("_Exposure", 1.6f);
        m.SetFloat("_Rotation", 0f);
        m.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
        m.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
        if (isNew) AssetDatabase.CreateAsset(m, SkyMaterialPath);
        else EditorUtility.SetDirty(m);
    }

    // ---------------- Камни ----------------
    static void SetupRock(string id)
    {
        // модель: материалы делаем сами
        ModelImporter mi = AssetImporter.GetAtPath(RockFbx(id)) as ModelImporter;
        if (mi != null)
        {
            bool changed = false;
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (mi.importAnimation) { mi.importAnimation = false; changed = true; }
            if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; changed = true; }
            if (changed) mi.SaveAndReimport();
        }

        ConfigureTexture(RockTex(id, "diff", "jpg"), false, true, 4096, 2048);
        ConfigureTexture(RockTex(id, "nor_gl", "jpg"), true, false, 4096, 2048);
        ConfigureTexture(RockTex(id, "ao", "jpg"), false, false, 2048, 1024);

        string metalSmooth = RockMetalSmooth(id);
        if (!File.Exists(metalSmooth)) BuildMetalSmooth(RockTex(id, "rough", "jpg"), metalSmooth);
        ConfigureTexture(metalSmooth, false, false, 2048, 1024);

        Material m = AssetDatabase.LoadAssetAtPath<Material>(RockMat(id));
        bool isNew = m == null;
        if (isNew) m = new Material(Shader.Find("Standard"));

        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(RockTex(id, "diff", "jpg")));
        m.color = new Color(0.9f, 0.88f, 0.86f);

        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(RockTex(id, "nor_gl", "jpg"));
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1.2f);
            m.EnableKeyword("_NORMALMAP");
        }

        Texture2D ao = AssetDatabase.LoadAssetAtPath<Texture2D>(RockTex(id, "ao", "jpg"));
        if (ao != null)
        {
            m.SetTexture("_OcclusionMap", ao);
            m.SetFloat("_OcclusionStrength", 1f);
        }

        Texture2D ms = AssetDatabase.LoadAssetAtPath<Texture2D>(metalSmooth);
        if (ms != null)
        {
            m.SetTexture("_MetallicGlossMap", ms);
            m.SetFloat("_GlossMapScale", 1f);
            m.EnableKeyword("_METALLICGLOSSMAP");
        }
        else
        {
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Glossiness", 0.15f);
        }
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        if (isNew) AssetDatabase.CreateAsset(m, RockMat(id));
        else EditorUtility.SetDirty(m);
    }

    // Standard берёт гладкость из альфы metallic-карты: делаем её из roughness (smooth = 1 - rough).
    static void BuildMetalSmooth(string roughPath, string outPath)
    {
        TextureImporter ti = AssetImporter.GetAtPath(roughPath) as TextureImporter;
        if (ti == null) return;
        ti.isReadable = true;
        ti.sRGBTexture = false;
        ti.maxTextureSize = 2048;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();

        Texture2D rough = AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath);
        if (rough == null) return;
        Color[] src = rough.GetPixels();
        Color32[] dst = new Color32[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            byte smooth = (byte)Mathf.Clamp(Mathf.RoundToInt((1f - Mathf.Clamp01(src[i].r)) * 255f), 0, 255);
            dst[i] = new Color32(0, 0, 0, smooth);
        }
        Texture2D outTex = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false, true);
        outTex.SetPixels32(dst);
        outTex.Apply();
        File.WriteAllBytes(outPath, outTex.EncodeToPNG());
        Object.DestroyImmediate(outTex);

        // исходник шероховатости больше не нужен в сборке в полном размере
        ti.isReadable = false;
        ti.maxTextureSize = 256;
        ti.SaveAndReimport();
        AssetDatabase.ImportAsset(outPath);
    }

    static void ConfigureTexture(string path, bool isNormal, bool sRGB, int maxSize, int webglMax)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        bool changed = false;
        TextureImporterType type = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type) { ti.textureType = type; changed = true; }
        if (!isNormal && ti.sRGBTexture != sRGB) { ti.sRGBTexture = sRGB; changed = true; }
        if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; changed = true; }
        changed |= SetPlatformMax(ti, "WebGL", webglMax);
        if (changed) ti.SaveAndReimport();
    }

    static bool SetPlatformMax(TextureImporter ti, string platform, int max)
    {
        TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings(platform);
        if (ps.overridden && ps.maxTextureSize == max) return false;
        ps.overridden = true;
        ps.maxTextureSize = max;
        ti.SetPlatformTextureSettings(ps);
        return true;
    }

    // ---------------- Назначение в Game3D ----------------
    public static void AssignTo(Game3D game)
    {
        game.SkyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);

        List<Mesh> meshes = new List<Mesh>();
        List<Material> mats = new List<Material>();
        foreach (string id in AvailableRocks())
        {
            Mesh mesh = null;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(RockFbx(id)))
            {
                Mesh candidate = o as Mesh;
                if (candidate != null && (mesh == null || candidate.vertexCount > mesh.vertexCount)) mesh = candidate;
            }
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(RockMat(id));
            if (mesh != null && mat != null)
            {
                meshes.Add(mesh);
                mats.Add(mat);
            }
        }
        game.RockMeshes = meshes.ToArray();
        game.RockMaterials = mats.ToArray();
    }
}
