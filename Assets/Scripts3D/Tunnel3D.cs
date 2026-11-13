using System.Collections.Generic;
using UnityEngine;

// Бесконечный яркий энергетический луч вдоль оси Z — главный ориентир в пространстве.
// Луч строится на отрезок вокруг игрока и следует за ним, концы растворяются в темноте.
// По лучу бегут импульсы энергии (показывают, куда лететь). Вдоль луча идут транспортники,
// а "наездники" вьются вокруг него по невидимой спирали радиусом Radius.
public class Tunnel3D : MonoBehaviour
{
    public static Tunnel3D Instance;

    public float Radius = 55f;               // радиус невидимой спирали вокруг луча (для врагов-наездников)
    public float HelixPeriod = 280f;         // длина витка этой спирали
    public float BehindLength = 400f;        // сколько луча видно позади
    public float AheadLength = 1600f;        // сколько луча видно впереди
    public float ArenaRadius = 260f;         // как далеко от луча можно улететь
    public float EnemySpawnDistance = 420f;

    public float StartZ { get { return CurrentPlayerZ - 30f; } }
    public float EndZ { get { return CurrentPlayerZ + EnemySpawnDistance; } }
    public Vector3 GatePosition { get { return new Vector3(0f, 0f, CurrentPlayerZ + EnemySpawnDistance); } }


    // ---------------- Визуал луча ----------------
    // Слои (все с гауссовым затуханием поперёк — края растворяются, "поверхности" нет):
    //   ядро (раскалённо-белое), свечение (голубое), текущая плазма (волокна бегут вдоль луча),
    //   мягкий ореол. Плюс мерцание, сгустки энергии и движущиеся источники света.
    private LineRenderer _core, _glow, _plasma, _bloom;
    private Material _plasmaMat;
    private readonly List<Transform> _pulses = new List<Transform>();
    private readonly List<float> _pulseOffsets = new List<float>();
    private readonly List<Light> _lights = new List<Light>();
    private const int PulseCount = 10;
    private const float PulseSpan = 1500f;
    private const float PulseSpeed = 320f;

    private static Texture2D _crossTex, _plasmaTex;

    float CurrentPlayerZ
    {
        get { return Player3D.Instance != null ? Player3D.Instance.transform.position.z : 0f; }
    }

    public static Tunnel3D Create()
    {
        GameObject go = new GameObject("Energy_Beam");
        Tunnel3D t = go.AddComponent<Tunnel3D>();
        t.Build();
        return t;
    }

    void Awake() { Instance = this; }

    // Поперечный профиль: гаусс (v = поперёк ширины линии).
    static Texture2D CrossTexture
    {
        get
        {
            if (_crossTex != null) return _crossTex;
            const int h = 64;
            _crossTex = new Texture2D(4, h, TextureFormat.RGBA32, false);
            _crossTex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;          // -1..1
                float a = Mathf.Exp(-v * v * 4.5f);           // гаусс
                a = Mathf.Clamp01((a - 0.012f) / 0.988f);     // точно ноль на краю
                for (int x = 0; x < 4; x++) _crossTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            _crossTex.Apply();
            return _crossTex;
        }
    }

    // Плазма: тонкие волокна вдоль луча (u — вдоль, v — поперёк), тайлится по длине.
    static Texture2D PlasmaTexture
    {
        get
        {
            if (_plasmaTex != null) return _plasmaTex;
            const int w = 256, h = 64;
            _plasmaTex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            _plasmaTex.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;
                float across = Mathf.Exp(-v * v * 3f);
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w;
                    // бесшовно по u: шум на окружности
                    float cx = Mathf.Cos(u * Mathf.PI * 2f) * 2f, cy = Mathf.Sin(u * Mathf.PI * 2f) * 2f;
                    float n = Mathf.PerlinNoise(cx + y * 0.35f, cy + 3.1f) * 0.6f
                            + Mathf.PerlinNoise(cx * 2.3f + 9.7f, cy * 2.3f + y * 0.9f) * 0.4f;
                    float filament = Mathf.Pow(Mathf.Clamp01(n * 1.6f - 0.35f), 2.2f);
                    _plasmaTex.SetPixel(x, y, new Color(1f, 1f, 1f, filament * across));
                }
            }
            _plasmaTex.Apply();
            return _plasmaTex;
        }
    }

    Material BeamMaterial(Texture tex)
    {
        Material baseMat = Factory3D.GlowFlat(Color.white);
        Material m = new Material(baseMat);
        m.mainTexture = tex;
        m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        return m;
    }

    // ---------------- Начало луча: яркая звезда-источник ----------------
    public float SourceZ = -330f;          // где стоит звезда (позади старта игрока)
    public float SourceDiameter = 170f;
    public float MinZ { get { return SourceZ + SourceDiameter * 0.5f + 40f; } } // за звезду не улететь

    void BuildSource(Material cross)
    {
        Planets3D.BuildBeamSource(new Vector3(0f, 0f, SourceZ), SourceDiameter);

        // неподвижное "основание" луча: выходит прямо из поверхности звезды и растворяется вперёд
        float start = SourceZ;
        float length = 900f;
        FixedBeam("Beam_Origin_Glow", start, length, 4.5f, new Color(0.5f, 0.88f, 1f), cross);
        FixedBeam("Beam_Origin_Core", start, length, 1.2f, Color.white, cross);
    }

    void FixedBeam(string name, float startZ, float length, float width, Color color, Material mat)
    {
        GameObject go = new GameObject(name);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, new Vector3(0f, 0f, startZ));
        lr.SetPosition(1, new Vector3(0f, 0f, startZ + length));
        lr.startWidth = width * 1.6f; // у звезды луч шире — "вырывается" из неё
        lr.endWidth = width;
        lr.sharedMaterial = mat;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.15f), new GradientColorKey(color, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) });
        lr.colorGradient = g;
    }

    void Build()
    {
        Material cross = BeamMaterial(CrossTexture);
        BuildSource(cross);
        _plasmaMat = BeamMaterial(PlasmaTexture);

        _bloom = NewBeam("Beam_Bloom", 11f, new Color(0.25f, 0.45f, 1f), 0.22f, cross, LineTextureMode.Stretch);
        _glow = NewBeam("Beam_Glow", 3.2f, new Color(0.45f, 0.85f, 1f), 0.85f, cross, LineTextureMode.Stretch);
        _plasma = NewBeam("Beam_Plasma", 2.2f, new Color(0.7f, 0.95f, 1f), 1f, _plasmaMat, LineTextureMode.Tile);
        _core = NewBeam("Beam_Core", 0.8f, new Color(1f, 1f, 1f), 1f, cross, LineTextureMode.Stretch);

        // сгустки энергии — компактные, без огромных шаров
        for (int i = 0; i < PulseCount; i++)
        {
            GameObject p = new GameObject("Beam_Pulse");
            p.transform.SetParent(transform, false);
            Factory3D.GlowBall(p.transform, Vector3.zero, 4.5f, new Color(0.55f, 0.85f, 1f, 0.8f));
            Factory3D.GlowBall(p.transform, Vector3.zero, 1.3f, Color.white);
            _pulses.Add(p.transform);
            _pulseOffsets.Add(i * PulseSpan / PulseCount);
        }

        // свет от луча, подсвечивающий корабли рядом (несколько точечных источников вокруг игрока)
        for (int i = 0; i < 3; i++)
        {
            GameObject lg = new GameObject("Beam_Light");
            lg.transform.SetParent(transform, false);
            Light l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.5f, 0.8f, 1f);
            l.range = 45f;
            l.intensity = 1.6f;
            l.shadows = LightShadows.None;
            _lights.Add(l);
        }
    }

    LineRenderer NewBeam(string name, float width, Color color, float peakAlpha, Material mat, LineTextureMode mode)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 5;
        lr.SetPosition(0, new Vector3(0f, 0f, -BehindLength));
        lr.SetPosition(1, new Vector3(0f, 0f, -BehindLength * 0.4f));
        lr.SetPosition(2, Vector3.zero);
        lr.SetPosition(3, new Vector3(0f, 0f, AheadLength * 0.5f));
        lr.SetPosition(4, new Vector3(0f, 0f, AheadLength));
        lr.startWidth = width;
        lr.endWidth = width;
        lr.sharedMaterial = mat;
        lr.textureMode = mode;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 0;

        // ярко рядом с игроком, растворяется на обоих концах
        float len = AheadLength + BehindLength;
        float playerT = BehindLength / len;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(peakAlpha, playerT * 0.7f),
                new GradientAlphaKey(peakAlpha, playerT + (1f - playerT) * 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        lr.colorGradient = g;
        return lr;
    }

    void LateUpdate()
    {
        float pz = CurrentPlayerZ;
        transform.position = new Vector3(0f, 0f, pz);
        float t = Time.time;

        // мерцание энергии: смесь медленного шума и быстрой дрожи
        float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(t * 3f, 0.37f) + 0.04f * Mathf.Sin(t * 47f);
        _core.startWidth = _core.endWidth = 0.8f * flicker;
        _glow.startWidth = _glow.endWidth = 3.2f * (0.95f + 0.1f * Mathf.PerlinNoise(t * 1.7f, 4.2f));
        _plasma.startWidth = _plasma.endWidth = 2.2f * flicker;
        _bloom.startWidth = _bloom.endWidth = 11f * (0.92f + 0.12f * Mathf.PerlinNoise(t * 0.8f, 7.9f));

        // плазма течёт вперёд вдоль луча (сдвиг текстуры; плотность волокон — на единицу длины)
        _plasmaMat.mainTextureScale = new Vector2(0.03f, 1f);
        _plasmaMat.mainTextureOffset = new Vector2(-(t * 9f + pz * 0.03f), 0f);

        // сгустки энергии бегут вперёд и растворяются вдали
        for (int i = 0; i < _pulses.Count; i++)
        {
            float local = Mathf.Repeat(_pulseOffsets[i] + t * PulseSpeed, PulseSpan) - BehindLength;
            _pulses[i].localPosition = new Vector3(0f, 0f, local);
            float fade = local < 0f ? Mathf.Clamp01(1f + local / BehindLength) : Mathf.Clamp01(1f - local / (PulseSpan - BehindLength));
            _pulses[i].localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, fade);
        }

        // источники света: впереди, рядом и позади игрока — корабли у луча подсвечиваются
        for (int i = 0; i < _lights.Count; i++)
        {
            _lights[i].transform.localPosition = new Vector3(0f, 0f, (i - 1) * 40f);
            _lights[i].intensity = 1.6f * flicker;
        }
    }

    // ---------------- Геометрия для остальных систем ----------------
    public Vector3 ClosestPointOnAxis(Vector3 p)
    {
        return new Vector3(0f, 0f, p.z);
    }

    // Луч бесконечный вдоль Z; ограничено только расстояние от него.
    public Vector3 ClampToArena(Vector3 p, out bool clamped)
    {
        Vector2 r = new Vector2(p.x, p.y);
        clamped = r.magnitude > ArenaRadius;
        if (clamped)
        {
            r = r.normalized * ArenaRadius;
            p = new Vector3(r.x, r.y, p.z);
        }
        // путь начинается у звезды-источника: дальше назад лететь нельзя
        if (p.z < MinZ)
        {
            p.z = MinZ;
            clamped = true;
        }
        return p;
    }

    // ---------------- Непроницаемый луч ----------------
    public float BeamBarrierRadius = 4f; // "твёрдая" толщина луча (примерно ширина свечения)

    // Не даёт кораблю пройти сквозь луч: выталкивает на ту сторону, откуда он подлетел,
    // и ловит быстрый пролёт насквозь за один кадр (проверяется весь отрезок движения).
    public Vector3 PushOutOfBeam(Vector3 pos, Vector3 prevPos, float shipRadius, out bool blocked)
    {
        float minR = BeamBarrierRadius + shipRadius;
        Vector2 a = new Vector2(prevPos.x, prevPos.y);
        Vector2 b = new Vector2(pos.x, pos.y);
        blocked = false;

        // ближайшая к лучу точка отрезка движения (в плоскости XY)
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(-Vector2.Dot(a, ab) / ab.sqrMagnitude) : 1f;
        Vector2 closest = a + ab * t;
        if (closest.magnitude >= minR) return pos;

        blocked = true;
        // сторона, с которой корабль подлетел
        Vector2 side = a.sqrMagnitude > 1e-4f ? a.normalized : (b.sqrMagnitude > 1e-4f ? b.normalized : Vector2.down);
        // оставляем скольжение вдоль луча и по окружности, убираем только движение "внутрь"
        Vector2 tangential = b - side * Vector2.Dot(b, side);
        Vector2 pushed = side * minR + tangential;
        if (pushed.magnitude < minR) pushed = pushed.normalized * minR;
        return new Vector3(pushed.x, pushed.y, pos.z);
    }

    public bool IsInsideTunnel(Vector3 p, float margin)
    {
        return new Vector2(p.x, p.y).magnitude < Radius + margin;
    }
}
