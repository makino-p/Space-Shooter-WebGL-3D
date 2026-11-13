using System.Collections.Generic;
using UnityEngine;

// Строит все 3D-модели и эффекты из примитивов Unity — отдельные модели не нужны.
public static class Factory3D
{
    public static Material LitBase;   // Standard (с включённой эмиссией)
    public static Material GlowBase;  // Legacy Particles/Additive

    private static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
    private static Texture2D _softDot;

    // ---------------- Материалы ----------------
    public static Material Lit(Color color, float emission = 0f, float metallic = 0.3f, float smoothness = 0.6f)
    {
        string key = "L" + color + emission + metallic + smoothness;
        Material m;
        if (_cache.TryGetValue(key, out m)) return m;

        m = LitBase != null ? new Material(LitBase) : new Material(Shader.Find("Standard"));
        m.color = color;
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * emission);
        _cache[key] = m;
        return m;
    }

    public static Material Glow(Color color)
    {
        string key = "G" + color;
        Material m;
        if (_cache.TryGetValue(key, out m)) return m;

        m = GlowBase != null ? new Material(GlowBase) : new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        m.SetColor("_TintColor", color);
        m.mainTexture = SoftDot;
        _cache[key] = m;
        return m;
    }

    // Аддитивное свечение без текстуры — для щитов-пузырей.
    public static Material GlowFlat(Color color)
    {
        string key = "F" + color;
        Material m;
        if (_cache.TryGetValue(key, out m)) return m;

        m = GlowBase != null ? new Material(GlowBase) : new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        m.SetColor("_TintColor", color);
        m.mainTexture = Texture2D.whiteTexture;
        _cache[key] = m;
        return m;
    }

    // Мягкая круглая точка для частиц и свечения.
    public static Texture2D SoftDot
    {
        get
        {
            if (_softDot == null)
            {
                const int size = 64;
                _softDot = new Texture2D(size, size, TextureFormat.RGBA32, false);
                _softDot.wrapMode = TextureWrapMode.Clamp;
                float c = (size - 1) / 2f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a;
                        _softDot.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }
                _softDot.Apply();
            }
            return _softDot;
        }
    }

    // ---------------- Детали ----------------
    public static GameObject Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale,
                                  Material material, Vector3 euler = default(Vector3))
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Collider col = g.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(euler);
        g.transform.localScale = scale;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = material;
        return g;
    }

    public static GameObject GlowBall(Transform parent, Vector3 localPos, float size, Color color)
    {
        GameObject g = Part(PrimitiveType.Quad, parent, localPos, Vector3.one * size, Glow(color));
        g.name = "Glow";
        g.AddComponent<Billboard3D>();
        Renderer r = g.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return g;
    }

    // ---------------- Модели из 2D-спрайтов ----------------
    private static Mesh _playerMesh, _enemyMesh, _asteroidMesh;
    private static Material _playerMat, _enemyMat, _asteroidMat;

    // Масштабы: меш построен в юнитах спрайта (512px / 100 ppu).
    private const float PlayerModelScale = 1.4f;
    private const float EnemyModelScale = 1.7f;
    private const float AsteroidSpriteDiameter = 2.25f; // видимая ширина камня на спрайте

    public static bool HasSpriteModels { get { return _playerMesh != null && _playerMat != null; } }

    public static void SetSpriteModels(Mesh player, Material playerMat, Mesh enemy, Material enemyMat, Mesh asteroid, Material asteroidMat)
    {
        _playerMesh = player; _playerMat = playerMat;
        _enemyMesh = enemy; _enemyMat = enemyMat;
        _asteroidMesh = asteroid; _asteroidMat = asteroidMat;
    }

    public static Vector3[] PlayerEnginePoints
    {
        get
        {
            return HasSpriteModels
                ? new[] { new Vector3(-0.28f, 0f, -2.35f), new Vector3(0.28f, 0f, -2.35f) }
                : new[] { new Vector3(-0.55f, -0.1f, -1.8f), new Vector3(0.55f, -0.1f, -1.8f) };
        }
    }

    static GameObject MeshPart(Mesh mesh, Material material, Transform parent, Vector3 localPos, float scale, Vector3 euler)
    {
        GameObject g = new GameObject(mesh.name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(euler);
        g.transform.localScale = Vector3.one * scale;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = material;
        return g;
    }

    // Копия материала спрайта с оттенком (кэшируется).
    static Material Tinted(Material source, Color tint, float emission)
    {
        string key = "T" + source.GetInstanceID() + tint + emission;
        Material m;
        if (_cache.TryGetValue(key, out m)) return m;
        m = new Material(source);
        m.color = tint;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", tint * emission);
        _cache[key] = m;
        return m;
    }

    // ---------------- Корабль игрока ----------------
    public static GameObject BuildPlayerShip(Transform parent)
    {
        if (HasSpriteModels)
        {
            GameObject model = new GameObject("Ship_Model");
            model.transform.SetParent(parent, false);
            // спрайт: нос вверх = +Z в 3D, картинка смотрит вверх
            MeshPart(_playerMesh, _playerMat, model.transform, Vector3.zero, PlayerModelScale, Vector3.zero);
            foreach (Vector3 e in PlayerEnginePoints)
            {
                GlowBall(model.transform, e + new Vector3(0, 0, -0.2f), 1.3f, new Color(1f, 0.6f, 0.2f, 1f));
            }
            return model;
        }
        return BuildPrimitivePlayerShip(parent);
    }

    static GameObject BuildPrimitivePlayerShip(Transform parent)
    {
        GameObject root = new GameObject("Ship_Model");
        root.transform.SetParent(parent, false);
        Transform t = root.transform;

        Material hull = Lit(new Color(0.75f, 0.8f, 0.9f), 0f, 0.6f, 0.75f);
        Material dark = Lit(new Color(0.15f, 0.17f, 0.22f), 0f, 0.5f, 0.5f);
        Material accent = Lit(new Color(0.1f, 0.6f, 1f), 1.2f);
        Material cockpit = Lit(new Color(0.2f, 0.9f, 1f), 2f, 0.1f, 0.95f);

        Part(PrimitiveType.Capsule, t, new Vector3(0, 0, 0.2f), new Vector3(0.9f, 1.6f, 0.7f), hull, new Vector3(90, 0, 0));
        Part(PrimitiveType.Sphere, t, new Vector3(0, 0.32f, 0.9f), new Vector3(0.55f, 0.4f, 0.9f), cockpit);
        // крылья
        Part(PrimitiveType.Cube, t, new Vector3(-1.5f, -0.05f, -0.3f), new Vector3(2.4f, 0.1f, 1.1f), hull, new Vector3(0, -12, -6));
        Part(PrimitiveType.Cube, t, new Vector3(1.5f, -0.05f, -0.3f), new Vector3(2.4f, 0.1f, 1.1f), hull, new Vector3(0, 12, 6));
        Part(PrimitiveType.Cube, t, new Vector3(-2.6f, 0.05f, -0.4f), new Vector3(0.12f, 0.5f, 1.0f), accent);
        Part(PrimitiveType.Cube, t, new Vector3(2.6f, 0.05f, -0.4f), new Vector3(0.12f, 0.5f, 1.0f), accent);
        // хвост
        Part(PrimitiveType.Cube, t, new Vector3(0, 0.55f, -1.1f), new Vector3(0.1f, 0.8f, 0.9f), dark, new Vector3(-20, 0, 0));
        // двигатели
        for (int i = -1; i <= 1; i += 2)
        {
            Part(PrimitiveType.Cylinder, t, new Vector3(0.55f * i, -0.1f, -1.2f), new Vector3(0.45f, 0.45f, 0.45f), dark, new Vector3(90, 0, 0));
            GlowBall(t, new Vector3(0.55f * i, -0.1f, -1.75f), 1.1f, new Color(0.3f, 0.7f, 1f, 1f));
        }
        // пушки
        Part(PrimitiveType.Cylinder, t, new Vector3(-1.9f, -0.12f, 0.4f), new Vector3(0.1f, 0.5f, 0.1f), dark, new Vector3(90, 0, 0));
        Part(PrimitiveType.Cylinder, t, new Vector3(1.9f, -0.12f, 0.4f), new Vector3(0.1f, 0.5f, 0.1f), dark, new Vector3(90, 0, 0));
        return root;
    }

    // ---------------- Враги ----------------
    public static Color EnemyColor(Enemy3DType type)
    {
        switch (type)
        {
            case Enemy3DType.Weaver: return new Color(0.3f, 1f, 0.4f);
            case Enemy3DType.Shooter: return new Color(1f, 0.6f, 0.15f);
            case Enemy3DType.RearGunner: return new Color(0.7f, 0.35f, 1f);
            case Enemy3DType.Dodger: return new Color(0.2f, 0.85f, 1f);
            case Enemy3DType.Beamer: return new Color(1f, 0.25f, 0.7f);
            default: return new Color(1f, 0.25f, 0.2f);
        }
    }

    public static GameObject BuildEnemyShip(Transform parent, Enemy3DType type)
    {
        if (_enemyMesh != null && _enemyMat != null)
        {
            GameObject model = new GameObject("Enemy_Model");
            model.transform.SetParent(parent, false);
            Color c = EnemyColor(type);
            // каждый тип врага слегка окрашен в свой цвет, исходная картинка сохраняется
            Color tint = type == Enemy3DType.Basic ? Color.white : Color.Lerp(Color.white, c, 0.55f);
            // в 2D враг летел вниз, поэтому нос спрайта смотрит вниз — разворачиваем на 180°
            MeshPart(_enemyMesh, Tinted(_enemyMat, tint, 0.08f), model.transform, Vector3.zero, EnemyModelScale, new Vector3(0, 180, 0));

            GlowBall(model.transform, new Vector3(0f, 0.35f, 1.2f), 1.3f, c);                 // "глаз" спереди
            GlowBall(model.transform, new Vector3(-0.6f, 0f, -2.2f), 0.9f, new Color(1f, 0.3f, 0.2f, 1f)); // двигатели
            GlowBall(model.transform, new Vector3(0.6f, 0f, -2.2f), 0.9f, new Color(1f, 0.3f, 0.2f, 1f));

            switch (type)
            {
                case Enemy3DType.RearGunner:
                    GlowBall(model.transform, new Vector3(0f, 0.3f, -2.6f), 1.2f, c); // хвостовая пушка
                    break;
                case Enemy3DType.Beamer:
                    GlowBall(model.transform, new Vector3(0f, 0f, 2.4f), 1.6f, c);    // излучатель луча
                    break;
                case Enemy3DType.Dodger:
                    GlowBall(model.transform, new Vector3(-1.9f, 0f, 0.4f), 0.8f, c);
                    GlowBall(model.transform, new Vector3(1.9f, 0f, 0.4f), 0.8f, c);
                    break;
            }
            return model;
        }
        return BuildPrimitiveEnemyShip(parent, type);
    }

    static GameObject BuildPrimitiveEnemyShip(Transform parent, Enemy3DType type)
    {
        GameObject root = new GameObject("Enemy_Model");
        root.transform.SetParent(parent, false);
        Transform t = root.transform;

        Color c = EnemyColor(type);
        Material hull = Lit(new Color(0.22f, 0.22f, 0.26f), 0f, 0.7f, 0.6f);
        Material accent = Lit(c, 1.5f);

        // корпус-"тарелка"
        Part(PrimitiveType.Sphere, t, Vector3.zero, new Vector3(2.4f, 0.8f, 2.4f), hull);
        Part(PrimitiveType.Cylinder, t, Vector3.zero, new Vector3(2.9f, 0.06f, 2.9f), accent);
        Part(PrimitiveType.Sphere, t, new Vector3(0, 0.2f, 0.9f), new Vector3(0.6f, 0.4f, 0.6f), accent);
        GlowBall(t, new Vector3(0, 0.2f, 1.1f), 1.4f, c);

        switch (type)
        {
            case Enemy3DType.Basic:
                Part(PrimitiveType.Cube, t, new Vector3(-1.5f, 0, -0.3f), new Vector3(1.2f, 0.12f, 0.8f), hull, new Vector3(0, 30, 0));
                Part(PrimitiveType.Cube, t, new Vector3(1.5f, 0, -0.3f), new Vector3(1.2f, 0.12f, 0.8f), hull, new Vector3(0, -30, 0));
                break;
            case Enemy3DType.Weaver:
                Part(PrimitiveType.Cube, t, new Vector3(0, 0, -1.2f), new Vector3(0.6f, 0.6f, 0.6f), accent, new Vector3(45, 0, 45));
                Part(PrimitiveType.Cube, t, new Vector3(-1.4f, 0, 0), new Vector3(0.12f, 1.4f, 0.6f), accent);
                Part(PrimitiveType.Cube, t, new Vector3(1.4f, 0, 0), new Vector3(0.12f, 1.4f, 0.6f), accent);
                break;
            case Enemy3DType.Shooter:
                Part(PrimitiveType.Cylinder, t, new Vector3(-0.7f, -0.25f, 1.2f), new Vector3(0.18f, 0.7f, 0.18f), hull, new Vector3(90, 0, 0));
                Part(PrimitiveType.Cylinder, t, new Vector3(0.7f, -0.25f, 1.2f), new Vector3(0.18f, 0.7f, 0.18f), hull, new Vector3(90, 0, 0));
                break;
            case Enemy3DType.RearGunner:
                Part(PrimitiveType.Cylinder, t, new Vector3(0, 0.1f, -1.4f), new Vector3(0.3f, 0.7f, 0.3f), accent, new Vector3(90, 0, 0));
                GlowBall(t, new Vector3(0, 0.1f, -2.1f), 1f, c);
                break;
            case Enemy3DType.Dodger:
                Part(PrimitiveType.Cube, t, new Vector3(-1.8f, 0, 0), new Vector3(1.6f, 0.05f, 0.4f), accent, new Vector3(0, 0, 15));
                Part(PrimitiveType.Cube, t, new Vector3(1.8f, 0, 0), new Vector3(1.6f, 0.05f, 0.4f), accent, new Vector3(0, 0, -15));
                break;
            case Enemy3DType.Beamer:
                Part(PrimitiveType.Cylinder, t, new Vector3(0, -0.1f, 1.6f), new Vector3(0.35f, 1.0f, 0.35f), hull, new Vector3(90, 0, 0));
                GlowBall(t, new Vector3(0, -0.1f, 2.6f), 1.2f, c);
                break;
        }
        return root;
    }

    // ---------------- Босс ----------------
    public static GameObject BuildBoss(Transform parent)
    {
        if (_enemyMesh != null && _enemyMat != null)
        {
            // босс — огромный вражеский корабль из того же спрайта, в красных тонах
            GameObject model = new GameObject("Boss_Model");
            model.transform.SetParent(parent, false);
            MeshPart(_enemyMesh, Tinted(_enemyMat, new Color(1f, 0.55f, 0.5f), 0.12f), model.transform,
                     Vector3.zero, EnemyModelScale * 4.5f, new Vector3(0, 180, 0));
            GlowBall(model.transform, new Vector3(0f, 1.2f, 5.5f), 7f, new Color(1f, 0.3f, 0.1f, 1f)); // ядро
            for (int i = -1; i <= 1; i += 2)
            {
                GlowBall(model.transform, new Vector3(2.8f * i, 0f, -8.5f), 4f, new Color(1f, 0.35f, 0.15f, 1f)); // двигатели
                GlowBall(model.transform, new Vector3(6.5f * i, 0.8f, 2f), 2.5f, new Color(1f, 0.5f, 0.1f, 1f));  // орудия
            }
            return model;
        }
        return BuildPrimitiveBoss(parent);
    }

    static GameObject BuildPrimitiveBoss(Transform parent)
    {
        GameObject root = new GameObject("Boss_Model");
        root.transform.SetParent(parent, false);
        Transform t = root.transform;

        Material hull = Lit(new Color(0.25f, 0.2f, 0.22f), 0f, 0.8f, 0.55f);
        Material plate = Lit(new Color(0.45f, 0.12f, 0.12f), 0.2f, 0.6f, 0.5f);
        Material core = Lit(new Color(1f, 0.2f, 0.1f), 3f);

        Part(PrimitiveType.Sphere, t, Vector3.zero, new Vector3(9f, 6f, 9f), hull);
        Part(PrimitiveType.Cylinder, t, Vector3.zero, new Vector3(16f, 0.5f, 16f), plate);
        Part(PrimitiveType.Sphere, t, new Vector3(0, 0, 4.2f), new Vector3(2.6f, 2.6f, 2.6f), core);
        GlowBall(t, new Vector3(0, 0, 5.2f), 7f, new Color(1f, 0.3f, 0.1f, 1f));

        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f;
            Vector3 dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
            Part(PrimitiveType.Cube, t, dir * 7.5f, new Vector3(1.6f, 2.4f, 1.6f), hull, new Vector3(0, a, 0));
            Part(PrimitiveType.Cylinder, t, dir * 7.5f + Vector3.up * 1.6f, new Vector3(0.4f, 1.2f, 0.4f), plate, new Vector3(0, 0, 0));
            GlowBall(t, dir * 7.5f + Vector3.up * 2.9f, 2f, new Color(1f, 0.4f, 0.1f, 1f));
        }
        Part(PrimitiveType.Cylinder, t, new Vector3(0, 3.6f, 0), new Vector3(2.5f, 1.2f, 2.5f), plate);
        Part(PrimitiveType.Cylinder, t, new Vector3(0, -3.6f, 0), new Vector3(2.5f, 1.2f, 2.5f), plate);
        return root;
    }

    // ---------------- Пикапы ----------------
    public static Color PickupColor(PickupType type)
    {
        switch (type)
        {
            case PickupType.TripleShot: return new Color(1f, 0.9f, 0.2f);
            case PickupType.Speed: return new Color(0.2f, 0.6f, 1f);
            case PickupType.Shield: return new Color(0.3f, 0.9f, 1f);
            case PickupType.Ammo: return new Color(1f, 0.55f, 0.1f);
            case PickupType.Health: return new Color(0.2f, 1f, 0.35f);
            case PickupType.Radial: return new Color(1f, 1f, 0.85f);
            case PickupType.Negative: return new Color(1f, 0.1f, 0.1f);
            case PickupType.Homing: return new Color(1f, 0.3f, 1f);
        }
        return Color.white;
    }

    public static string PickupLabel(PickupType type)
    {
        switch (type)
        {
            case PickupType.TripleShot: return "3";
            case PickupType.Speed: return "S";
            case PickupType.Shield: return "O";
            case PickupType.Ammo: return "A";
            case PickupType.Health: return "+";
            case PickupType.Radial: return "R";
            case PickupType.Negative: return "!";
            case PickupType.Homing: return "H";
        }
        return "?";
    }

    public static GameObject BuildPickup(Transform parent, PickupType type)
    {
        GameObject root = new GameObject("Pickup_Model");
        root.transform.SetParent(parent, false);
        Color c = PickupColor(type);

        PrimitiveType shape = type == PickupType.Negative ? PrimitiveType.Cube : PrimitiveType.Sphere;
        Part(shape, root.transform, Vector3.zero, Vector3.one * 1.4f, Lit(c, 1.6f, 0.2f, 0.8f), new Vector3(45, 0, 45));
        Part(PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(2.2f, 0.12f, 0.12f), Lit(c * 0.6f, 0.8f));
        Part(PrimitiveType.Cube, root.transform, Vector3.zero, new Vector3(0.12f, 2.2f, 0.12f), Lit(c * 0.6f, 0.8f));
        GlowBall(root.transform, Vector3.zero, 4f, new Color(c.r, c.g, c.b, 0.6f));

        GameObject label = new GameObject("Label");
        label.transform.SetParent(root.transform, false);
        label.transform.localPosition = new Vector3(0, 1.9f, 0);
        TextMesh tm = label.AddComponent<TextMesh>();
        tm.text = PickupLabel(type);
        tm.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        tm.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.characterSize = 0.25f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = c;
        label.AddComponent<Billboard3D>();
        return root;
    }

    // ---------------- Астероид ----------------
    // ---------------- Скачанные камни Poly Haven ----------------
    private static Mesh[] _rockMeshes;
    private static Material[] _rockMats;

    public static void SetRockModels(Mesh[] meshes, Material[] materials)
    {
        _rockMeshes = meshes;
        _rockMats = materials;
    }

    static bool HasRocks
    {
        get { return _rockMeshes != null && _rockMats != null && _rockMeshes.Length > 0 && _rockMeshes.Length == _rockMats.Length; }
    }

    public static GameObject BuildAsteroid(Transform parent, float size)
    {
        if (HasRocks)
        {
            // фотоскан камня, отмасштабированный под нужный размер и отцентрованный
            int i = Random.Range(0, _rockMeshes.Length);
            Mesh mesh = _rockMeshes[i];
            Bounds b = mesh.bounds;
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float scale = maxDim > 0.0001f ? size / maxDim : 1f;

            GameObject model = new GameObject("Asteroid_Model");
            model.transform.SetParent(parent, false);
            GameObject rock = MeshPart(mesh, _rockMats[i], model.transform, -b.center * scale, scale, Vector3.zero);
            rock.name = "Rock_Scan";
            return model;
        }

        if (_asteroidMesh != null && _asteroidMat != null)
        {
            GameObject model = new GameObject("Asteroid_Model");
            model.transform.SetParent(parent, false);
            // немного разные пропорции, чтобы астероиды не были одинаковыми
            GameObject rock = MeshPart(_asteroidMesh, _asteroidMat, model.transform, Vector3.zero, size / AsteroidSpriteDiameter, Vector3.zero);
            rock.transform.localScale = Vector3.Scale(rock.transform.localScale,
                new Vector3(Random.Range(0.85f, 1.15f), Random.Range(0.85f, 1.2f), Random.Range(0.85f, 1.15f)));
            return model;
        }
        return BuildPrimitiveAsteroid(parent, size);
    }

    static GameObject BuildPrimitiveAsteroid(Transform parent, float size)
    {
        GameObject root = new GameObject("Asteroid_Model");
        root.transform.SetParent(parent, false);
        Material rock = Lit(new Color(0.42f, 0.38f, 0.34f), 0f, 0.05f, 0.15f);
        Part(PrimitiveType.Sphere, root.transform, Vector3.zero, new Vector3(1f, 0.85f, 0.95f) * size, rock);
        for (int i = 0; i < 5; i++)
        {
            Vector3 p = Random.onUnitSphere * size * 0.38f;
            Part(PrimitiveType.Sphere, root.transform, p, Vector3.one * size * Random.Range(0.35f, 0.55f), rock);
        }
        return root;
    }

    // ---------------- Эффекты ----------------
    public static void Explosion(Vector3 position, float size, Color color)
    {
        GameObject go = new GameObject("Explosion");
        go.transform.position = position;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f * size, 14f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f * size, 2.2f * size);
        main.startColor = new ParticleSystem.MinMaxGradient(color, new Color(1f, 0.95f, 0.6f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(40 * Mathf.Clamp(size, 0.5f, 3f))) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f * size;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.4f), new GradientColorKey(new Color(0.2f, 0.05f, 0f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Glow(Color.white);

        ps.Play();
        Object.Destroy(go, 2f);

        // вспышка света
        GameObject lightGo = new GameObject("Flash");
        lightGo.transform.position = position;
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = 25f * size;
        l.intensity = 4f;
        lightGo.AddComponent<FadeLight3D>();
    }

    public static ParticleSystem EngineTrail(Transform parent, Vector3 localPos, Color color, float size)
    {
        GameObject go = new GameObject("Engine_Trail");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.LookRotation(Vector3.back);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 0.35f;
        main.startSpeed = 6f;
        main.startSize = size;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 60;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 4f;
        shape.radius = 0.1f;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Glow(Color.white);
        ps.Play();
        return ps;
    }

    public static ParticleSystem Smoke(Transform parent, Vector3 localPos)
    {
        GameObject go = new GameObject("Damage_Smoke");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 0.8f;
        main.startSpeed = 2f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.1f, 0.8f), new Color(0.4f, 0.4f, 0.4f, 0.5f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 25;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Glow(Color.white);
        ps.Play();
        return ps;
    }

    public static void Starfield(int count, float minRadius, float maxRadius)
    {
        GameObject go = new GameObject("Starfield");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 100000f;
        main.startSpeed = 0f;
        main.maxParticles = count;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Glow(Color.white);
        ps.Play();

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            ep.position = Random.onUnitSphere * Random.Range(minRadius, maxRadius);
            ep.startSize = Random.Range(0.8f, 3.2f);
            float tint = Random.value;
            ep.startColor = tint < 0.15f ? new Color(0.7f, 0.8f, 1f) : (tint < 0.25f ? new Color(1f, 0.85f, 0.7f) : Color.white);
            ep.startLifetime = 100000f;
            ps.Emit(ep, 1);
        }
    }
}

// Квад всегда повёрнут к камере.
public class Billboard3D : MonoBehaviour
{
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            transform.rotation = cam.transform.rotation;
        }
    }
}

// Затухающая вспышка света от взрыва.
public class FadeLight3D : MonoBehaviour
{
    private Light _light;
    private float _start;

    void Start()
    {
        _light = GetComponent<Light>();
        _start = _light.intensity;
        Destroy(gameObject, 0.5f);
    }

    void Update()
    {
        _light.intensity = Mathf.MoveTowards(_light.intensity, 0f, _start * Time.deltaTime * 2.2f);
    }
}
