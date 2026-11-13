using UnityEngine;

// Реалистичные эффекты: выстрелы, попадания и многослойные взрывы.
// Взрыв = вспышка + огненный шар + дым + искры + обломки + ударная волна + свет.
public static class Effects3D
{
    public static Material SmokeBase; // Legacy Particles/Alpha Blended (назначается из сцены, чтобы шейдер попал в сборку)

    private static Texture2D _puff, _ring;
    private static Material _fireMat, _smokeMat, _ringMat, _sparkMat;

    // ---------------- Текстуры (генерируются кодом) ----------------
    // Клубящаяся "пушистая" клякса для огня и дыма.
    static Texture2D Puff
    {
        get
        {
            if (_puff != null) return _puff;
            const int size = 128;
            _puff = new Texture2D(size, size, TextureFormat.RGBA32, true);
            _puff.wrapMode = TextureWrapMode.Clamp;
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / c, dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = 0f, amp = 0.5f, freq = 3f;
                    for (int o = 0; o < 4; o++)
                    {
                        n += Mathf.PerlinNoise(x / (float)size * freq + 13.1f, y / (float)size * freq + 7.7f) * amp;
                        amp *= 0.5f;
                        freq *= 2f;
                    }
                    float a = Mathf.Clamp01(1f - d * (0.85f + n * 0.5f));
                    a = Mathf.SmoothStep(0f, 1f, a) * Mathf.Clamp01(0.55f + n);
                    float bright = Mathf.Clamp01(0.75f + n * 0.5f);
                    _puff.SetPixel(x, y, new Color(bright, bright, bright, a));
                }
            }
            _puff.Apply();
            return _puff;
        }
    }

    // Мягкое кольцо для ударной волны.
    static Texture2D Ring
    {
        get
        {
            if (_ring != null) return _ring;
            const int size = 128;
            _ring = new Texture2D(size, size, TextureFormat.RGBA32, true);
            _ring.wrapMode = TextureWrapMode.Clamp;
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                    float a = Mathf.Exp(-Mathf.Pow((d - 0.85f) / 0.07f, 2f)) + 0.25f * Mathf.Exp(-Mathf.Pow((d - 0.7f) / 0.15f, 2f));
                    _ring.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            }
            _ring.Apply();
            return _ring;
        }
    }

    static Material FireMat
    {
        get
        {
            if (_fireMat == null)
            {
                _fireMat = new Material(Factory3D.Glow(Color.white));
                _fireMat.mainTexture = Puff;
                _fireMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            return _fireMat;
        }
    }

    static Material SmokeMat
    {
        get
        {
            if (_smokeMat == null)
            {
                Shader s = SmokeBase != null ? SmokeBase.shader : Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                _smokeMat = SmokeBase != null ? new Material(SmokeBase) : new Material(s);
                _smokeMat.mainTexture = Puff;
                _smokeMat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            return _smokeMat;
        }
    }

    static Material RingMat
    {
        get
        {
            if (_ringMat == null)
            {
                _ringMat = new Material(Factory3D.Glow(Color.white));
                _ringMat.mainTexture = Ring;
            }
            return _ringMat;
        }
    }

    static Material SparkMat
    {
        get
        {
            if (_sparkMat == null) _sparkMat = Factory3D.Glow(Color.white);
            return _sparkMat;
        }
    }

    // ---------------- Строитель систем частиц ----------------
    static ParticleSystem NewSystem(string name, Transform root, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static void Burst(ParticleSystem ps, int count, float delay = 0f)
    {
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(delay, (short)Mathf.Clamp(count, 1, 280)) });
        ps.Play();
    }

    static Gradient Grad(Color[] colors, float[] colorTimes, float[] alphas, float[] alphaTimes)
    {
        GradientColorKey[] ck = new GradientColorKey[colors.Length];
        for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i], colorTimes[i]);
        GradientAlphaKey[] ak = new GradientAlphaKey[alphas.Length];
        for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i], alphaTimes[i]);
        Gradient g = new Gradient();
        g.SetKeys(ck, ak);
        return g;
    }

    // ---------------- Взрыв ----------------
    public static void Explosion(Vector3 position, float size, Color color, Vector3 velocity)
    {
        float s = Mathf.Max(0.2f, size);
        GameObject root = new GameObject("Explosion");
        root.transform.position = position;
        Vector3 drift = velocity * 0.35f;

        // 1. вспышка
        Flash(root.transform, Color.Lerp(Color.white, color, 0.25f), s * 13f, 0.12f, 0.3f);

        // 2. огненный шар — клубящиеся языки пламени
        ParticleSystem fire = NewSystem("Fireball", root.transform, FireMat);
        {
            var main = fire.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * s, 9f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f * s, 5f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = fire.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.6f * s;
            var rot = fire.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            var col = fire.colorOverLifetime; col.enabled = true;
            col.color = Grad(
                new[] { new Color(1f, 0.97f, 0.85f), new Color(1f, 0.62f, 0.2f), Color.Lerp(new Color(0.85f, 0.25f, 0.05f), color, 0.3f), new Color(0.12f, 0.05f, 0.03f) },
                new[] { 0f, 0.15f, 0.45f, 1f },
                new[] { 1f, 0.95f, 0.55f, 0f }, new[] { 0f, 0.2f, 0.6f, 1f });
            var sol = fire.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.5f)));
            var limit = fire.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 0.35f; limit.limit = 0.5f * s;
            var vol = fire.velocityOverLifetime; vol.enabled = true; vol.space = ParticleSystemSimulationSpace.World;
            vol.x = drift.x; vol.y = drift.y; vol.z = drift.z;
            Burst(fire, Mathf.RoundToInt(16 + 12 * s));
        }

        // 3. дым — тёмные клубы, расползаются медленно
        ParticleSystem smoke = NewSystem("Smoke", root.transform, SmokeMat);
        {
            var main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f * s, 4f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(3f * s, 6f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = smoke.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.8f * s;
            var rot = smoke.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var col = smoke.colorOverLifetime; col.enabled = true;
            col.color = Grad(
                new[] { new Color(0.35f, 0.3f, 0.28f), new Color(0.2f, 0.2f, 0.22f) }, new[] { 0f, 1f },
                new[] { 0f, 0.55f, 0.35f, 0f }, new[] { 0f, 0.15f, 0.5f, 1f });
            var sol = smoke.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2.2f));
            var limit = smoke.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 0.3f; limit.limit = 0.3f * s;
            var vol = smoke.velocityOverLifetime; vol.enabled = true; vol.space = ParticleSystemSimulationSpace.World;
            vol.x = drift.x * 0.7f; vol.y = drift.y * 0.7f; vol.z = drift.z * 0.7f;
            Burst(smoke, Mathf.RoundToInt(8 + 6 * s), 0.08f);
        }

        // 4. искры — раскалённые трассеры
        ParticleSystem sparks = NewSystem("Sparks", root.transform, SparkMat);
        {
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(18f * s, 48f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f * s, 0.45f * s);
            var shape = sparks.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f * s;
            var col = sparks.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { Color.white, new Color(1f, 0.85f, 0.4f), new Color(1f, 0.4f, 0.1f) }, new[] { 0f, 0.3f, 1f },
                             new[] { 1f, 1f, 0f }, new[] { 0f, 0.6f, 1f });
            var limit = sparks.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 0.12f; limit.limit = 2f * s;
            ParticleSystemRenderer r = sparks.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1.5f;
            Burst(sparks, Mathf.RoundToInt(25 + 20 * s));
        }

        // 5. обломки корпуса
        int debris = Mathf.RoundToInt(3 + 3 * s);
        Material hull = Factory3D.Lit(new Color(0.22f, 0.22f, 0.25f), 0f, 0.6f, 0.4f);
        for (int i = 0; i < debris; i++)
        {
            GameObject chunk = Factory3D.Part(PrimitiveType.Cube, null, position + Random.insideUnitSphere * s,
                                              new Vector3(Random.Range(0.2f, 0.7f), Random.Range(0.1f, 0.4f), Random.Range(0.3f, 0.9f)) * s,
                                              hull, Random.rotation.eulerAngles);
            Factory3D.GlowBall(chunk.transform, Vector3.zero, 2.2f, new Color(1f, 0.5f, 0.15f, 0.9f)); // раскалённый край
            Debris3D d = chunk.AddComponent<Debris3D>();
            d.Velocity = Random.onUnitSphere * Random.Range(8f, 22f) * s + velocity * 0.6f;
            d.Spin = Random.insideUnitSphere * 400f;
            d.Lifetime = Random.Range(1.2f, 2.2f);
        }

        // 6. ударная волна
        Shockwave(root.transform, Color.Lerp(color, Color.white, 0.4f), s * 18f, 0.55f);

        // 7. свет, освещающий всё вокруг
        GameObject lightGo = new GameObject("Flash_Light");
        lightGo.transform.SetParent(root.transform, false);
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = Color.Lerp(new Color(1f, 0.7f, 0.35f), color, 0.3f);
        l.range = 35f * s;
        l.intensity = 6f;
        lightGo.AddComponent<FadeLight3D>();

        Object.Destroy(root, 4f);
    }

    // Попадание в щит врага: голубая вспышка, кольцо, искры.
    public static void ShieldHit(Vector3 position, Color color)
    {
        GameObject root = new GameObject("Shield_Hit");
        root.transform.position = position;
        Flash(root.transform, color, 8f, 0.06f, 0.2f);
        Shockwave(root.transform, color, 9f, 0.35f);
        ImpactSparksAt(root.transform, Vector3.zero, Random.onUnitSphere, color, 18);
        Object.Destroy(root, 2f);
    }

    // ---------------- Выстрелы ----------------
    public static void MuzzleFlash(Vector3 position, Vector3 direction, Color color, bool withLight)
    {
        GameObject root = new GameObject("Muzzle_Flash");
        root.transform.position = position + direction * 0.6f;
        Flash(root.transform, Color.Lerp(color, Color.white, 0.5f), 2.6f, 0.03f, 0.08f);
        if (withLight)
        {
            Light l = root.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = 9f;
            l.intensity = 3f;
        }
        Object.Destroy(root, 0.1f);
    }

    public static void ImpactSparks(Vector3 position, Vector3 normal, Color color)
    {
        GameObject root = new GameObject("Impact");
        root.transform.position = position;
        Flash(root.transform, Color.Lerp(color, Color.white, 0.5f), 4f, 0.03f, 0.12f);
        ImpactSparksAt(root.transform, Vector3.zero, normal, color, 14);
        Object.Destroy(root, 1.2f);
    }

    static void ImpactSparksAt(Transform root, Vector3 localPos, Vector3 normal, Color color, int count)
    {
        ParticleSystem ps = NewSystem("Sparks", root, SparkMat);
        ps.transform.localPosition = localPos;
        if (normal.sqrMagnitude > 0.001f) ps.transform.rotation = Quaternion.LookRotation(normal);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 30f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55f; shape.radius = 0.1f;
        var col = ps.colorOverLifetime; col.enabled = true;
        col.color = Grad(new[] { Color.white, color }, new[] { 0f, 1f }, new[] { 1f, 0f }, new[] { 0f, 1f });
        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        r.lengthScale = 1.2f;
        Burst(ps, count);
    }

    // ---------------- Общие кирпичики ----------------
    static void Flash(Transform root, Color color, float maxSize, float growTime, float lifetime)
    {
        GameObject g = Factory3D.GlowBall(root, Vector3.zero, 0.01f, Color.white);
        FlashFx fx = g.AddComponent<FlashFx>();
        fx.Color = color;
        fx.MaxSize = maxSize;
        fx.GrowTime = growTime;
        fx.Lifetime = lifetime;
    }

    static void Shockwave(Transform root, Color color, float maxSize, float lifetime)
    {
        GameObject g = Factory3D.Part(PrimitiveType.Quad, root, Vector3.zero, Vector3.one * 0.01f, RingMat);
        g.name = "Shockwave";
        g.AddComponent<Billboard3D>();
        Renderer r = g.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        FlashFx fx = g.AddComponent<FlashFx>();
        fx.Color = color;
        fx.MaxSize = maxSize;
        fx.GrowTime = lifetime;
        fx.Lifetime = lifetime;
        fx.EaseOut = true;
    }
}

// Быстро растущая и гаснущая вспышка (через MaterialPropertyBlock — без новых материалов).
public class FlashFx : MonoBehaviour
{
    public Color Color = Color.white;
    public float MaxSize = 5f;
    public float GrowTime = 0.1f;
    public float Lifetime = 0.3f;
    public bool EaseOut;

    private float _t;
    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    void Start()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        _t += Time.deltaTime;
        float grow = Mathf.Clamp01(_t / Mathf.Max(GrowTime, 0.001f));
        if (EaseOut) grow = 1f - (1f - grow) * (1f - grow);
        transform.localScale = Vector3.one * Mathf.Lerp(0.05f, MaxSize, grow);

        float alpha = 1f - Mathf.Clamp01(_t / Mathf.Max(Lifetime, 0.001f));
        Color c = Color;
        c.a = alpha * alpha;
        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_TintColor", c);
            _renderer.SetPropertyBlock(_mpb);
        }
        if (_t >= Lifetime) Destroy(gameObject);
    }
}

// Обломок: летит по инерции, кувыркается, остывает и уменьшается.
public class Debris3D : MonoBehaviour
{
    public Vector3 Velocity;
    public Vector3 Spin;
    public float Lifetime = 1.5f;

    private float _t;
    private Vector3 _scale;

    void Start() { _scale = transform.localScale; }

    void Update()
    {
        _t += Time.deltaTime;
        transform.position += Velocity * Time.deltaTime;
        Velocity *= 1f - 0.6f * Time.deltaTime;
        transform.Rotate(Spin * Time.deltaTime, Space.Self);
        float k = Mathf.Clamp01(_t / Lifetime);
        transform.localScale = _scale * (1f - k * k);
        if (_t >= Lifetime) Destroy(gameObject);
    }
}
