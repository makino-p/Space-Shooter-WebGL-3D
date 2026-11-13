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

    // ---------------- Импортированные корабли (F3 Corvette, Star Sparrow) ----------------
    private static GameObject _playerPrefab;
    private static GameObject[] _enemyPrefabs;
    private static Material[] _enemyMats;
    private static Vector3 _playerEuler, _enemyEuler;
    private static float _playerLength = 7f, _enemyLength = 5f, _bossLength = 26f;
    private static Vector3[] _importedGuns, _importedEngines;

    public static bool HasImportedPlayer { get { return _playerPrefab != null; } }
    public static bool HasImportedEnemies { get { return _enemyPrefabs != null && _enemyPrefabs.Length > 0 && _enemyMats != null && _enemyMats.Length == _enemyPrefabs.Length; } }

    public static void SetImportedShips(GameObject player, Vector3 playerEuler, float playerLength,
                                        GameObject[] enemies, Material[] enemyMats, Vector3 enemyEuler, float enemyLength, float bossLength)
    {
        _playerPrefab = player;
        _playerEuler = playerEuler;
        _playerLength = playerLength;
        _enemyPrefabs = enemies;
        _enemyMats = enemyMats;
        _enemyEuler = enemyEuler;
        _enemyLength = enemyLength;
        _bossLength = bossLength;
    }

    // Ставит модель из префаба: поворот (нос по +Z), длина по Z = length, центр в нуле.
    // Возвращает габариты уже подогнанной модели в локальных координатах parent.
    static Bounds PlaceShipModel(GameObject prefab, Transform parent, Vector3 euler, float length, Material overrideMat)
    {
        GameObject holder = new GameObject(prefab.name + "_Model");
        holder.transform.SetParent(parent, false);

        GameObject inst = Object.Instantiate(prefab);
        inst.name = prefab.name;
        inst.transform.SetParent(holder.transform, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.Euler(euler) * prefab.transform.localRotation;
        inst.transform.localScale = prefab.transform.localScale;

        // лишнее из пака: физика и скрипты
        foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        foreach (Rigidbody rb in inst.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
        foreach (MonoBehaviour mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(mb);

        Renderer[] renderers = inst.GetComponentsInChildren<Renderer>(true);
        if (overrideMat != null)
        {
            foreach (Renderer r in renderers)
            {
                Material[] mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = overrideMat;
                r.sharedMaterials = mats;
            }
        }

        // габариты в координатах holder (по вершинам границ мешей)
        Bounds b = new Bounds();
        bool has = false;
        Matrix4x4 toHolder = holder.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = toHolder * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!has) { b = new Bounds(p, Vector3.zero); has = true; } else b.Encapsulate(p);
            }
        }
        if (!has) return new Bounds(Vector3.zero, Vector3.one * length);

        float scale = b.size.z > 0.0001f ? length / b.size.z : 1f;
        holder.transform.localScale = Vector3.one * scale;
        holder.transform.localPosition = -b.center * scale;
        return new Bounds(Vector3.zero, b.size * scale);
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

    // Точки пушек игрока — передние края корабля (по габаритам модели).
    public static Vector3[] PlayerGunPoints
    {
        get
        {
            if (_importedGuns != null) return _importedGuns;
            return HasSpriteModels
                ? new[] { new Vector3(-0.72f, 0.1f, 2.9f), new Vector3(0.72f, 0.1f, 2.9f) }
                : new[] { new Vector3(-1.9f, -0.12f, 1.0f), new Vector3(1.9f, -0.12f, 1.0f) };
        }
    }

    public static Vector3[] PlayerEnginePoints
    {
        get
        {
            if (_importedEngines != null) return _importedEngines;
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
        string key = "T" + source.GetHashCode() + tint + emission;
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
        if (HasImportedPlayer)
        {
            // Federation Corvette F3
            GameObject model = new GameObject("Ship_Model");
            model.transform.SetParent(parent, false);
            Bounds b = PlaceShipModel(_playerPrefab, model.transform, _playerEuler, _playerLength, null);
            float halfW = b.extents.x, halfL = b.extents.z;
            _importedGuns = new[] { new Vector3(-halfW * 0.35f, 0f, halfL * 0.85f), new Vector3(halfW * 0.35f, 0f, halfL * 0.85f) };
            _importedEngines = new[] { new Vector3(-halfW * 0.18f, 0f, -halfL * 0.98f), new Vector3(halfW * 0.18f, 0f, -halfL * 0.98f) };
            foreach (Vector3 e in _importedEngines)
            {
                GlowBall(model.transform, e + new Vector3(0f, 0f, -0.2f), 1.4f, new Color(0.4f, 0.75f, 1f, 1f));
            }
            return model;
        }
        _importedGuns = null;
        _importedEngines = null;

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
        if (HasImportedEnemies)
        {
            // Star Sparrow: у каждого типа врага своя модель и свой цвет (последний в списке — босс)
            int count = Mathf.Max(1, _enemyPrefabs.Length - 1);
            int i = (int)type % count;
            GameObject model = new GameObject("Enemy_Model");
            model.transform.SetParent(parent, false);
            // враги на 30% крупнее базовой длины — чтобы не терялись на фоне луча и транспортников
            Bounds b = PlaceShipModel(_enemyPrefabs[i], model.transform, _enemyEuler, _enemyLength * 1.3f, _enemyMats[i]);
            Color c = EnemyColor(type);
            GlowBall(model.transform, new Vector3(0f, 0f, -b.extents.z * 0.95f), 2.2f, c);  // двигатель
            // красный инверсионный след — отличает врагов от белых транспортников
            EngineTrail(model.transform, new Vector3(0f, 0f, -b.extents.z), new Color(1f, 0.15f, 0.1f, 0.9f), 1.1f);
            // мягкий красный ореол опасности
            GlowBall(model.transform, Vector3.zero, b.extents.z * 3.2f, new Color(1f, 0.1f, 0.05f, 0.18f));
            if (type == Enemy3DType.RearGunner)
                GlowBall(model.transform, new Vector3(0f, b.extents.y * 0.5f, -b.extents.z), 1.2f, c); // хвостовая пушка
            if (type == Enemy3DType.Beamer)
                GlowBall(model.transform, new Vector3(0f, 0f, b.extents.z * 1.05f), 1.8f, c);          // излучатель луча
            return model;
        }

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
    // ---------------- Транспортник ----------------
    private static GameObject _transportPrefab;
    private static Material _transportMat;

    public static void SetTransportShip(GameObject prefab, Material mat)
    {
        _transportPrefab = prefab;
        _transportMat = mat;
    }

    // Грузовой корабль: корпус + контейнеры по бокам + навигационные огни (красный слева, зелёный справа).
    public static GameObject BuildTransport(Transform parent)
    {
        GameObject model = new GameObject("Transport_Model");
        model.transform.SetParent(parent, false);

        Bounds b;
        if (_transportPrefab != null)
        {
            b = PlaceShipModel(_transportPrefab, model.transform, _enemyEuler, 10f, _transportMat);
        }
        else
        {
            Material hullMat = Lit(new Color(0.55f, 0.57f, 0.6f), 0f, 0.6f, 0.5f);
            Part(PrimitiveType.Cube, model.transform, Vector3.zero, new Vector3(2.2f, 1.6f, 10f), hullMat);
            Part(PrimitiveType.Cube, model.transform, new Vector3(0f, 0.6f, 4.2f), new Vector3(1.6f, 1f, 1.6f), hullMat);
            b = new Bounds(Vector3.zero, new Vector3(2.2f, 1.6f, 10f));
        }

        // грузовые контейнеры разных цветов
        Color[] cargo = { new Color(0.85f, 0.35f, 0.15f), new Color(0.2f, 0.45f, 0.8f), new Color(0.75f, 0.7f, 0.2f), new Color(0.3f, 0.6f, 0.35f) };
        float side = Mathf.Max(b.extents.x * 0.55f, 1.4f);
        for (int i = 0; i < 3; i++)
        {
            float z = Mathf.Lerp(-b.extents.z * 0.55f, b.extents.z * 0.25f, i / 2f);
            Material m = Lit(cargo[Random.Range(0, cargo.Length)], 0f, 0.2f, 0.35f);
            Part(PrimitiveType.Cube, model.transform, new Vector3(-side, -0.4f, z), new Vector3(1.3f, 1.3f, 2.2f), m);
            Part(PrimitiveType.Cube, model.transform, new Vector3(side, -0.4f, z), new Vector3(1.3f, 1.3f, 2.2f), m);
        }

        GlowBall(model.transform, new Vector3(-b.extents.x - 0.3f, 0f, 0f), 1.4f, new Color(1f, 0.1f, 0.1f, 1f));   // левый — красный
        GlowBall(model.transform, new Vector3(b.extents.x + 0.3f, 0f, 0f), 1.4f, new Color(0.1f, 1f, 0.2f, 1f));    // правый — зелёный
        GlowBall(model.transform, new Vector3(0f, 0f, -b.extents.z - 0.4f), 2.6f, new Color(1f, 0.75f, 0.35f, 1f)); // двигатель
        GameObject beacon = GlowBall(model.transform, new Vector3(0f, b.extents.y + 0.4f, 0f), 1.6f, Color.white);
        beacon.AddComponent<Blink3D>();
        return model;
    }

    public static GameObject BuildBoss(Transform parent)
    {
        if (HasImportedEnemies)
        {
            // босс — самый крупный корабль Star Sparrow
            int last = _enemyPrefabs.Length - 1;
            GameObject model = new GameObject("Boss_Model");
            model.transform.SetParent(parent, false);
            Bounds b = PlaceShipModel(_enemyPrefabs[last], model.transform, _enemyEuler, _bossLength, _enemyMats[last]);
            GlowBall(model.transform, new Vector3(0f, 0f, b.extents.z * 0.6f), 8f, new Color(1f, 0.3f, 0.1f, 1f));           // ядро
            GlowBall(model.transform, new Vector3(-b.extents.x * 0.25f, 0f, -b.extents.z), 5f, new Color(1f, 0.35f, 0.15f, 1f));
            GlowBall(model.transform, new Vector3(b.extents.x * 0.25f, 0f, -b.extents.z), 5f, new Color(1f, 0.35f, 0.15f, 1f));
            return model;
        }

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
        tm.font = Compat.DefaultFont;
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
    public static void Explosion(Vector3 position, float size, Color color, Vector3 velocity = default(Vector3))
    {
        Effects3D.Explosion(position, size, color, velocity);
    }

    public static void ShieldHit(Vector3 position, Color color)
    {
        Effects3D.ShieldHit(position, color);
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

    // Далёкие звёзды: локальные частицы, объект следует за камерой (звёзды "на бесконечности").
    public static GameObject Starfield(int count, float minRadius, float maxRadius)
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
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
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
        go.AddComponent<FollowCamera3D>();
        return go;
    }

    // Космическая пыль вокруг игрока: неподвижные в мире частицы дают ощущение скорости.
    public static ParticleSystem SpaceDust(Transform follow)
    {
        GameObject go = new GameObject("Space_Dust");
        go.transform.SetParent(follow, false);
        go.transform.localPosition = new Vector3(0f, 0f, 30f);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 3f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
        main.startColor = new Color(0.8f, 0.9f, 1f, 0.55f);
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 160;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(120f, 120f, 140f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Glow(Color.white);
        ps.Play();
        return ps;
    }
}

// Держит объект на постоянном смещении от камеры (без поворота) —
// так фон (звёзды, планета, туманности) не приближается, сколько ни лети.
public class FollowCamera3D : MonoBehaviour
{
    private Vector3 _offset;
    private bool _init;

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        if (!_init)
        {
            _offset = transform.position - cam.transform.position;
            _init = true;
        }
        transform.position = cam.transform.position + _offset;
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

// Мигающий огонёк (маяк транспортника).
public class Blink3D : MonoBehaviour
{
    private float _phase;
    void Start() { _phase = Random.Range(0f, 1f); }
    void Update()
    {
        bool on = Mathf.Repeat(Time.time * 1.3f + _phase, 1f) < 0.12f;
        transform.localScale = Vector3.one * (on ? 1.6f : 0.01f);
    }
}
