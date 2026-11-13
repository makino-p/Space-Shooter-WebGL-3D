using UnityEngine;

// Планеты с процедурными текстурами поверхности и неоновым свечением.
// Каждая планета: сфера (albedo + карта свечения), атмосферный ореол, у гиганта — неоновые кольца.
// Планеты держатся "на горизонте" (следуют за камерой) и медленно вращаются.
public static class Planets3D
{
    public enum Style { NeonGasGiant, NeonCityWorld, IceMoon }

    const int TexW = 512, TexH = 256;

    public static GameObject Build(Style style, Vector3 offsetFromCamera, float diameter, float tiltDeg, int seed)
    {
        GameObject root = new GameObject("Planet_" + style);
        root.transform.position = offsetFromCamera;

        Texture2D albedo, emission;
        Color emissionTint, atmosphere;
        GenerateTextures(style, seed, out albedo, out emission, out emissionTint, out atmosphere);

        Material m = Factory3D.LitBase != null ? new Material(Factory3D.LitBase) : new Material(Shader.Find("Standard"));
        m.mainTexture = albedo;
        m.color = Color.white;
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", style == Style.IceMoon ? 0.55f : 0.25f);
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", emission);
        m.SetColor("_EmissionColor", emissionTint);

        GameObject body = Factory3D.Part(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * diameter, m,
                                         new Vector3(tiltDeg, 0f, tiltDeg * 0.5f));
        body.name = "Surface";
        Spin3D spin = body.AddComponent<Spin3D>();
        spin.DegreesPerSecond = style == Style.NeonGasGiant ? 1.2f : 2f;

        // атмосфера: мягкий ореол чуть больше планеты
        Factory3D.GlowBall(root.transform, Vector3.zero, diameter * 1.45f, new Color(atmosphere.r, atmosphere.g, atmosphere.b, 0.45f));

        if (style == Style.NeonGasGiant)
        {
            AddNeonRing(root.transform, diameter * 0.85f, 3.5f, new Color(1f, 0.25f, 0.85f), tiltDeg);
            AddNeonRing(root.transform, diameter * 0.95f, 1.6f, new Color(0.25f, 0.9f, 1f), tiltDeg);
            AddNeonRing(root.transform, diameter * 1.05f, 0.9f, new Color(0.7f, 0.5f, 1f), tiltDeg);
        }

        root.AddComponent<FollowCamera3D>();
        return root;
    }

    // ---------------- Звезда-источник луча (начало пути) ----------------
    // Неподвижна в мире: раскалённая поверхность, многослойная корона, неоновые кольца,
    // мощный свет и "основание" луча, выходящее из её поверхности.
    public static GameObject BuildBeamSource(Vector3 position, float diameter)
    {
        GameObject root = new GameObject("Beam_Source_Star");
        root.transform.position = position;

        // раскалённая поверхность с гранулами плазмы
        const int w = 512, h = 256;
        Texture2D albedo = new Texture2D(w, h, TextureFormat.RGBA32, true);
        Texture2D emission = new Texture2D(w, h, TextureFormat.RGBA32, true);
        Color32[] a = new Color32[w * h];
        Color32[] e = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float lat = (y + 0.5f) / h * Mathf.PI - Mathf.PI / 2f;
            for (int x = 0; x < w; x++)
            {
                float lon = (x + 0.5f) / w * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                float granule = Fbm(d * 7f, 4);
                float flare = Mathf.Clamp01((Fbm(d * 2.5f + Vector3.one * 5f, 3) - 0.55f) * 5f);
                Color hot = Color.Lerp(new Color(0.35f, 0.7f, 1f), new Color(1f, 1f, 1f), granule);
                hot = Color.Lerp(hot, new Color(0.8f, 0.5f, 1f), flare * 0.6f);
                a[y * w + x] = hot;
                e[y * w + x] = hot * (0.7f + granule * 0.6f);
            }
        }
        albedo.SetPixels32(a); albedo.Apply(true);
        emission.SetPixels32(e); emission.Apply(true);

        Material m = Factory3D.LitBase != null ? new Material(Factory3D.LitBase) : new Material(Shader.Find("Standard"));
        m.mainTexture = albedo;
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", 0.1f);
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", emission);
        m.SetColor("_EmissionColor", new Color(3.2f, 3.2f, 3.2f));

        GameObject body = Factory3D.Part(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * diameter, m);
        body.name = "Star_Surface";
        body.AddComponent<Spin3D>().DegreesPerSecond = 3f;

        // корона в несколько слоёв
        Factory3D.GlowBall(root.transform, Vector3.zero, diameter * 1.35f, new Color(0.85f, 0.95f, 1f, 0.9f));
        Factory3D.GlowBall(root.transform, Vector3.zero, diameter * 2.2f, new Color(0.3f, 0.75f, 1f, 0.55f));
        Factory3D.GlowBall(root.transform, Vector3.zero, diameter * 3.6f, new Color(0.45f, 0.3f, 1f, 0.28f));

        // неоновые кольца вокруг
        Spin3D r1 = AddNeonRing(root.transform, diameter * 0.85f, 2.5f, new Color(0.3f, 0.9f, 1f), 0f).AddComponent<Spin3D>();
        r1.Axis = Vector3.forward; r1.DegreesPerSecond = 8f;
        Spin3D r2 = AddNeonRing(root.transform, diameter * 1.05f, 1.4f, new Color(0.8f, 0.4f, 1f), 30f).AddComponent<Spin3D>();
        r2.Axis = Vector3.forward; r2.DegreesPerSecond = -5f;

        // свет звезды освещает всё начало пути
        Light l = root.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(0.6f, 0.85f, 1f);
        l.range = diameter * 6f;
        l.intensity = 2.5f;
        l.shadows = LightShadows.None;
        return root;
    }

    static GameObject AddNeonRing(Transform parent, float radius, float width, Color color, float tilt)
    {
        GameObject go = new GameObject("Neon_Ring");
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(90f + tilt * 0.6f, 0f, tilt);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        const int points = 128;
        lr.positionCount = points;
        lr.loop = true;
        lr.useWorldSpace = false;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.sharedMaterial = Factory3D.GlowFlat(Color.white);
        lr.startColor = color;
        lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        for (int i = 0; i < points; i++)
        {
            float a = i * Mathf.PI * 2f / points;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
        return go;
    }

    // ---------------- Процедурные текстуры ----------------
    // Шум на сфере (бесшовный по долготе): сумма 2D-шумов по трём плоскостям.
    static float Noise3(Vector3 p)
    {
        return (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z + 11.3f) + Mathf.PerlinNoise(p.z + 73.1f, p.x + 5.9f)) / 3f;
    }

    static float Fbm(Vector3 p, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise3(p) * amp;
            norm += amp;
            amp *= 0.5f;
            p *= 2.03f;
        }
        return sum / norm;
    }

    static void GenerateTextures(Style style, int seed, out Texture2D albedo, out Texture2D emission, out Color emissionTint, out Color atmosphere)
    {
        albedo = new Texture2D(TexW, TexH, TextureFormat.RGBA32, true);
        emission = new Texture2D(TexW, TexH, TextureFormat.RGBA32, true);
        albedo.wrapMode = TextureWrapMode.Repeat;
        emission.wrapMode = TextureWrapMode.Repeat;
        Color32[] a = new Color32[TexW * TexH];
        Color32[] e = new Color32[TexW * TexH];
        Vector3 off = new Vector3(seed * 1.37f, seed * 2.11f, seed * 0.73f);

        for (int y = 0; y < TexH; y++)
        {
            float lat = (y + 0.5f) / TexH * Mathf.PI - Mathf.PI / 2f; // -90..90
            for (int x = 0; x < TexW; x++)
            {
                float lon = (x + 0.5f) / TexW * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                Color ca, ce;
                switch (style)
                {
                    case Style.NeonGasGiant: GasGiant(dir, lat, off, out ca, out ce); break;
                    case Style.NeonCityWorld: CityWorld(dir, lat, lon, off, out ca, out ce); break;
                    default: IceMoon(dir, off, out ca, out ce); break;
                }
                int i = y * TexW + x;
                a[i] = ca;
                e[i] = ce;
            }
        }
        albedo.SetPixels32(a);
        emission.SetPixels32(e);
        albedo.Apply(true);
        emission.Apply(true);

        switch (style)
        {
            case Style.NeonGasGiant: emissionTint = new Color(2.2f, 2.2f, 2.2f); atmosphere = new Color(0.6f, 0.3f, 1f); break;
            case Style.NeonCityWorld: emissionTint = new Color(2.6f, 2.6f, 2.6f); atmosphere = new Color(0.2f, 0.55f, 1f); break;
            default: emissionTint = new Color(1.8f, 1.8f, 1.8f); atmosphere = new Color(0.5f, 0.9f, 1f); break;
        }
    }

    // Газовый гигант: турбулентные полосы + светящиеся неоновые пояса + штормы.
    static void GasGiant(Vector3 d, float lat, Vector3 off, out Color albedo, out Color emission)
    {
        float turb = Fbm(d * 3f + off, 4);
        float band = Mathf.Sin(lat * 14f + turb * 6f);
        Color deep = new Color(0.12f, 0.06f, 0.25f), mid = new Color(0.35f, 0.18f, 0.55f), light = new Color(0.55f, 0.65f, 0.85f);
        float t = band * 0.5f + 0.5f;
        albedo = t < 0.5f ? Color.Lerp(deep, mid, t * 2f) : Color.Lerp(mid, light, (t - 0.5f) * 2f);
        albedo *= 0.75f + turb * 0.5f;

        // тонкие неоновые линии на границах полос
        float line = Mathf.Clamp01(1f - Mathf.Abs(band) * 9f);
        float line2 = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sin(lat * 31f + turb * 9f)) * 14f) * 0.6f;
        Color neonA = new Color(1f, 0.2f, 0.85f), neonB = new Color(0.2f, 0.95f, 1f);
        emission = neonA * line + neonB * line2;

        // светящийся "шторм"
        float storm = Mathf.Clamp01((Fbm(d * 1.5f + off * 2f, 3) - 0.62f) * 8f);
        emission += new Color(1f, 0.5f, 0.1f) * storm * 0.8f;
        albedo.a = 1f;
        emission.a = 1f;
    }

    // Планета-мегаполис: континенты, океаны, неоновая сеть городов и магистралей.
    static void CityWorld(Vector3 d, float lat, float lon, Vector3 off, out Color albedo, out Color emission)
    {
        float h = Fbm(d * 2.2f + off, 5);
        bool land = h > 0.5f;
        if (land)
        {
            float k = Mathf.Clamp01((h - 0.5f) * 5f);
            albedo = Color.Lerp(new Color(0.16f, 0.15f, 0.14f), new Color(0.32f, 0.28f, 0.24f), k);
        }
        else
        {
            albedo = Color.Lerp(new Color(0.02f, 0.05f, 0.12f), new Color(0.05f, 0.12f, 0.25f), h * 2f);
        }
        float ice = Mathf.Clamp01((Mathf.Abs(lat) - 1.25f) * 6f);
        albedo = Color.Lerp(albedo, new Color(0.75f, 0.8f, 0.85f), ice);

        emission = Color.black;
        if (land && ice < 0.5f)
        {
            // сетка магистралей
            float gx = Mathf.Abs(Mathf.Sin(lon * 36f + Fbm(d * 6f, 2) * 3f));
            float gy = Mathf.Abs(Mathf.Sin(lat * 36f + Fbm(d * 6f + Vector3.one * 9f, 2) * 3f));
            float grid = Mathf.Clamp01(1f - Mathf.Min(gx, gy) * 18f);
            // города — яркие кластеры
            float city = Mathf.Clamp01((Fbm(d * 9f + off * 3f, 3) - 0.55f) * 6f);
            float density = Mathf.Clamp01((h - 0.52f) * 6f);
            emission = (new Color(0.15f, 0.9f, 1f) * grid * 0.9f + new Color(1f, 0.55f, 0.15f) * city) * density;
            // розовые неоновые "мегаполисы"
            float mega = Mathf.Clamp01((Fbm(d * 4f + off * 5f, 3) - 0.66f) * 10f);
            emission += new Color(1f, 0.15f, 0.8f) * mega * density;
        }
        albedo.a = 1f;
        emission.a = 1f;
    }

    // Ледяная луна: бледная поверхность, светящиеся голубые трещины.
    static void IceMoon(Vector3 d, Vector3 off, out Color albedo, out Color emission)
    {
        float n = Fbm(d * 3.5f + off, 5);
        albedo = Color.Lerp(new Color(0.55f, 0.65f, 0.75f), new Color(0.88f, 0.93f, 1f), n);
        // "гребневой" шум даёт сеть трещин
        float ridge = 1f - Mathf.Abs(Fbm(d * 5f + off * 2f, 4) * 2f - 1f);
        float crack = Mathf.Clamp01((ridge - 0.86f) * 9f);
        albedo = Color.Lerp(albedo, new Color(0.25f, 0.45f, 0.6f), crack * 0.7f);
        emission = new Color(0.2f, 0.85f, 1f) * crack;
        albedo.a = 1f;
        emission.a = 1f;
    }
}

// Медленное вращение (планеты).
public class Spin3D : MonoBehaviour
{
    public float DegreesPerSecond = 2f;
    public Vector3 Axis = Vector3.up; // кольца крутятся в своей плоскости (Vector3.forward)
    void Update() { transform.Rotate(Axis * DegreesPerSecond * Time.deltaTime, Space.Self); }
}
