using System.Collections.Generic;
using UnityEngine;

// Бесконечный неоновый путь из спиральных линий вдоль оси Z.
// Геометрия строится на отрезок вокруг игрока и "перескакивает" вперёд ровно на период узора,
// поэтому шов не виден, а концы плавно растворяются в темноте — кажется, что путь уходит в бесконечность.
// Стены не твёрдые: можно вылетать наружу. Враги прилетают издалека по этому пути.
public class Tunnel3D : MonoBehaviour
{
    public static Tunnel3D Instance;

    public float Radius = 55f;               // радиус спирали (диаметр 110)
    public int HelixCount = 3;               // спиралей в каждую сторону
    public float HelixPeriod = 280f;         // длина одного витка
    public float RingSpacing = 70f;          // кольца через каждые 70 (период узора кратен 280)
    public float BehindLength = 300f;        // сколько пути видно позади
    public float AheadLength = 1100f;        // сколько пути видно впереди
    public float ArenaRadius = 260f;         // как далеко от оси можно улететь
    public float EnemySpawnDistance = 420f;  // на каком расстоянии впереди появляются враги

    // Для совместимости с остальным кодом: "старт" и середина пути относительно игрока.
    public float StartZ { get { return CurrentPlayerZ - 30f; } }
    public float EndZ { get { return CurrentPlayerZ + EnemySpawnDistance; } }

    // точка далеко впереди на пути — оттуда прилетают враги
    public Vector3 GatePosition { get { return new Vector3(0f, 0f, CurrentPlayerZ + EnemySpawnDistance); } }

    private readonly List<LineRenderer> _rings = new List<LineRenderer>();
    private readonly List<float> _ringZ = new List<float>();

    private static readonly Color Violet = new Color(0.65f, 0.25f, 1f);
    private static readonly Color Amber = new Color(1f, 0.55f, 0.08f);
    private static readonly Color Ice = new Color(0.45f, 0.9f, 1f);

    float CurrentPlayerZ
    {
        get { return Player3D.Instance != null ? Player3D.Instance.transform.position.z : 0f; }
    }

    public static Tunnel3D Create()
    {
        GameObject go = new GameObject("Neon_Path");
        Tunnel3D t = go.AddComponent<Tunnel3D>();
        t.Build();
        return t;
    }

    void Awake() { Instance = this; }

    float FromZ { get { return -BehindLength - HelixPeriod; } }
    float ToZ { get { return AheadLength + HelixPeriod; } }

    void Build()
    {
        for (int h = 0; h < HelixCount; h++)
        {
            float phase = h * Mathf.PI * 2f / HelixCount;
            BuildHelix(phase, 1f, Violet);
            BuildHelix(phase + Mathf.PI / HelixCount, -1f, Amber);
        }

        for (float z = FromZ; z <= ToZ + 0.01f; z += RingSpacing)
        {
            _rings.Add(BuildRing(z));
            _ringZ.Add(z);
        }
    }

    LineRenderer NewLine(string name, int points, float width)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.positionCount = points;
        lr.useWorldSpace = false;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.sharedMaterial = Factory3D.GlowFlat(Color.white);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 2;
        return lr;
    }

    // Прозрачность вдоль линии: растворяется у обоих концов, ярче всего рядом с игроком.
    Gradient FadeGradient(Color color, float peakAlpha)
    {
        float len = ToZ - FromZ;
        float playerT = (0f - FromZ) / len;
        float behindFade = (-BehindLength * 0.4f - FromZ) / len;
        float aheadFade = (AheadLength * 0.55f - FromZ) / len;

        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0f, Mathf.Max(0.01f, behindFade - 0.05f)),
                new GradientAlphaKey(peakAlpha, playerT),
                new GradientAlphaKey(peakAlpha * 0.8f, aheadFade),
                new GradientAlphaKey(0f, 1f)
            });
        return g;
    }

    void BuildHelix(float phase, float direction, Color color)
    {
        int points = Mathf.CeilToInt((ToZ - FromZ) / 4f) + 1;
        LineRenderer lr = NewLine("Helix", points, 0.9f);
        for (int i = 0; i < points; i++)
        {
            float z = Mathf.Lerp(FromZ, ToZ, i / (points - 1f));
            float a = phase + direction * z / HelixPeriod * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a) * Radius, z));
        }
        lr.colorGradient = FadeGradient(color, 0.95f);
    }

    LineRenderer BuildRing(float z)
    {
        const int points = 96;
        LineRenderer lr = NewLine("Ring", points, 0.6f);
        lr.loop = true;
        for (int i = 0; i < points; i++)
        {
            float a = i * Mathf.PI * 2f / points;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a) * Radius, z));
        }
        return lr;
    }

    void LateUpdate()
    {
        float pz = CurrentPlayerZ;

        // перескок ровно на период узора — визуально ничего не меняется
        float anchor = Mathf.Floor(pz / HelixPeriod) * HelixPeriod;
        transform.position = new Vector3(0f, 0f, anchor);

        // кольца: бегущие огни уходят вперёд (куда лететь) и растворяются вдали
        float t = Time.time * 1.1f;
        for (int i = 0; i < _rings.Count; i++)
        {
            float worldZ = anchor + _ringZ[i];
            float rel = worldZ - pz;

            float fade;
            if (rel < 0f) fade = Mathf.Clamp01(1f + rel / (BehindLength * 0.6f));
            else fade = Mathf.Clamp01(1f - rel / (AheadLength * 0.85f));
            fade = fade * fade;

            float wave = Mathf.Repeat(t - worldZ / (RingSpacing * 4f), 1.4f);
            float glow = Mathf.Clamp01(1f - wave * 2.2f);

            Color c = Color.Lerp(Ice * 0.6f, Color.white, glow);
            c.a = (0.35f + 0.65f * glow) * fade;
            _rings[i].startColor = c;
            _rings[i].endColor = c;
            float w = 0.5f + 0.9f * glow;
            _rings[i].startWidth = w;
            _rings[i].endWidth = w;
        }
    }

    // ---------------- Геометрия для остальных систем ----------------
    public Vector3 ClosestPointOnAxis(Vector3 p)
    {
        return new Vector3(0f, 0f, p.z);
    }

    // Путь бесконечный вдоль Z; ограничено только расстояние от оси.
    public Vector3 ClampToArena(Vector3 p, out bool clamped)
    {
        Vector2 r = new Vector2(p.x, p.y);
        clamped = r.magnitude > ArenaRadius;
        if (clamped)
        {
            r = r.normalized * ArenaRadius;
            p = new Vector3(r.x, r.y, p.z);
        }
        return p;
    }

    public bool IsInsideTunnel(Vector3 p, float margin)
    {
        return new Vector2(p.x, p.y).magnitude < Radius + margin;
    }
}
