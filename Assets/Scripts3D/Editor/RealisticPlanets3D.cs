using System.IO;
using UnityEditor;
using UnityEngine;

// Реалистичные планеты из текстур Solar System Scope (CC BY 4.0, через Wikimedia Commons):
// настраивает импорт, создаёт материалы на шейдере SpaceShooter/RealisticPlanet и назначает их Game3D.
public static class RealisticPlanets3D
{
    public const string TexFolder = "Assets/ThirdParty/SolarSystemScope";
    const string MatFolder = "Assets/Materials3D/Planets";
    const string ShaderName = "SpaceShooter/RealisticPlanet";

    static string Tex(string name) { return TexFolder + "/" + name; }
    static string Mat(string name) { return MatFolder + "/" + name + ".mat"; }

    public static bool HasTextures { get { return File.Exists(Tex("earth_daymap.jpg")); } }

    public static bool NeedsSetup()
    {
        return HasTextures && !File.Exists(Mat("Earth"));
    }

    public static bool SceneNeedsAssets(string yaml)
    {
        if (!HasTextures) return false;
        int i = yaml.IndexOf("EarthMaterial:");
        return i < 0 || !yaml.Substring(i, System.Math.Min(80, yaml.Length - i)).Contains("guid:");
    }

    [MenuItem("Tools/Space Shooter 3D/Setup Realistic Planets")]
    public static void SetupMenu()
    {
        Setup();
        AssetDatabase.SaveAssets();
    }

    public static void Setup()
    {
        if (!HasTextures) return;
        Directory.CreateDirectory(MatFolder);

        Configure("earth_daymap.jpg", false);
        Configure("earth_nightmap.jpg", false);
        Configure("earth_clouds.jpg", false, srgb: false);
        Configure("earth_normal_map.tif", true);
        Configure("earth_specular_map.tif", false, srgb: false);
        Configure("jupiter.jpg", false);
        Configure("saturn.jpg", false);
        Configure("saturn_ring_alpha.png", false, clampU: true);
        Configure("mars.jpg", false);
        Configure("moon.jpg", false);
        Configure("sun.jpg", false);

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError("[Space Shooter 3D] Не найден шейдер " + ShaderName);
            return;
        }

        PlanetMat("Earth", shader, "earth_daymap.jpg", m =>
        {
            SetTex(m, "_NightTex", "earth_nightmap.jpg", "_HasNight");
            SetTex(m, "_CloudTex", "earth_clouds.jpg", "_HasClouds");
            SetTex(m, "_NormalMap", "earth_normal_map.tif", "_HasNormal");
            SetTex(m, "_SpecMap", "earth_specular_map.tif", "_HasSpec");
            m.SetFloat("_NightBoost", 1.8f);
            m.SetFloat("_NormalStrength", 1.2f);
            m.SetColor("_AtmoColor", new Color(0.35f, 0.62f, 1f));
            m.SetFloat("_AtmoStrength", 1.35f);
            m.SetFloat("_AtmoPower", 3.2f);
        });
        PlanetMat("Jupiter", shader, "jupiter.jpg", m =>
        {
            m.SetColor("_AtmoColor", new Color(0.95f, 0.8f, 0.6f));
            m.SetFloat("_AtmoStrength", 0.55f);
            m.SetFloat("_AtmoPower", 4f);
        });
        PlanetMat("Saturn", shader, "saturn.jpg", m =>
        {
            m.SetColor("_AtmoColor", new Color(0.95f, 0.88f, 0.65f));
            m.SetFloat("_AtmoStrength", 0.5f);
            m.SetFloat("_AtmoPower", 4f);
        });
        PlanetMat("Mars", shader, "mars.jpg", m =>
        {
            m.SetColor("_AtmoColor", new Color(1f, 0.55f, 0.3f));
            m.SetColor("_SunsetColor", new Color(0.4f, 0.6f, 1f)); // на Марсе закаты голубые
            m.SetFloat("_AtmoStrength", 0.6f);
            m.SetFloat("_AtmoPower", 5f);
        });
        PlanetMat("Moon", shader, "moon.jpg", m =>
        {
            m.SetFloat("_AtmoStrength", 0f); // у Луны нет атмосферы
            m.SetFloat("_Brightness", 1.05f);
        });

        // кольца Сатурна: полупрозрачная текстура, видны с обеих сторон
        Material ring = AssetDatabase.LoadAssetAtPath<Material>(Mat("SaturnRings"));
        bool isNew = ring == null;
        if (isNew) ring = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        ring.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex("saturn_ring_alpha.png"));
        ring.SetColor("_TintColor", new Color(0.55f, 0.52f, 0.47f, 0.55f));
        if (isNew) AssetDatabase.CreateAsset(ring, Mat("SaturnRings")); else EditorUtility.SetDirty(ring);

        AssetDatabase.SaveAssets();
    }

    static void PlanetMat(string name, Shader shader, string dayTex, System.Action<Material> setup)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(Mat(name));
        bool isNew = m == null;
        if (isNew) m = new Material(shader);
        m.shader = shader;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex(dayTex)));
        setup(m);
        if (isNew) AssetDatabase.CreateAsset(m, Mat(name)); else EditorUtility.SetDirty(m);
    }

    static void SetTex(Material m, string prop, string file, string flag)
    {
        Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex(file));
        m.SetTexture(prop, t);
        m.SetFloat(flag, t != null ? 1f : 0f);
    }

    // В редакторе — полное качество (до 8K), для сайта (WebGL) — 2K.
    static void Configure(string file, bool normal, bool srgb = true, bool clampU = false)
    {
        TextureImporter ti = AssetImporter.GetAtPath(Tex(file)) as TextureImporter;
        if (ti == null) return;
        bool changed = false;
        TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type) { ti.textureType = type; changed = true; }
        if (!normal && ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; changed = true; }
        if (ti.maxTextureSize != 8192) { ti.maxTextureSize = 8192; changed = true; }
        TextureWrapMode wrap = clampU ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        if (ti.wrapMode != wrap) { ti.wrapMode = wrap; changed = true; }
        if (ti.anisoLevel < 4) { ti.anisoLevel = 4; changed = true; }
        TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings("WebGL");
        if (!ps.overridden || ps.maxTextureSize != 2048)
        {
            ps.overridden = true;
            ps.maxTextureSize = 2048;
            ti.SetPlatformTextureSettings(ps);
            changed = true;
        }
        if (changed) ti.SaveAndReimport();
    }

    public static void AssignTo(Game3D game)
    {
        if (!HasTextures) return;
        game.EarthMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat("Earth"));
        game.JupiterMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat("Jupiter"));
        game.SaturnMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat("Saturn"));
        game.SaturnRingMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat("SaturnRings"));
        game.MarsMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat("Mars"));
        game.MoonMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat("Moon"));
        game.SunTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex("sun.jpg"));
    }
}
