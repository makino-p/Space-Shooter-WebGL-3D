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

    private static readonly Color Core = new Color(0.85f, 0.97f, 1f);
    private static readonly Color Glow = new Color(0.2f, 0.75f, 1f);
    private static readonly Color Haze = new Color(0.35f, 0.3f, 1f);

    private LineRenderer _core, _glow, _haze;
    private readonly List<Transform> _pulses = new List<Transform>();
    private readonly List<float> _pulseOffsets = new List<float>();
    private const int PulseCount = 14;
    private const float PulseSpan = 1500f;
    private const float PulseSpeed = 240f;

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

    void Build()
    {
        // три слоя: белое ядро, голубое свечение, широкая фиолетовая дымка
        _haze = NewBeam("Beam_Haze", 26f, Haze, 0.16f);
        _glow = NewBeam("Beam_Glow", 7f, Glow, 0.55f);
        _core = NewBeam("Beam_Core", 1.6f, Core, 1f);

        for (int i = 0; i < PulseCount; i++)
        {
            GameObject p = new GameObject("Beam_Pulse");
            p.transform.SetParent(transform, false);
            Factory3D.GlowBall(p.transform, Vector3.zero, 10f, new Color(0.6f, 0.9f, 1f, 0.9f));
            Factory3D.GlowBall(p.transform, Vector3.zero, 3.5f, Color.white);
            _pulses.Add(p.transform);
            _pulseOffsets.Add(i * PulseSpan / PulseCount);
        }
    }

    LineRenderer NewBeam(string name, float width, Color color, float peakAlpha)
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
        lr.sharedMaterial = Factory3D.GlowFlat(Color.white);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 4;

        // яркий рядом с игроком, растворяется на обоих концах
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

        // лёгкое "дыхание" луча
        float breath = 1f + 0.08f * Mathf.Sin(Time.time * 6f) + 0.04f * Mathf.Sin(Time.time * 17f);
        _core.startWidth = _core.endWidth = 1.6f * breath;
        _glow.startWidth = _glow.endWidth = 7f * breath;

        // импульсы бегут вперёд по лучу и исчезают вдали
        for (int i = 0; i < _pulses.Count; i++)
        {
            float local = Mathf.Repeat(_pulseOffsets[i] + Time.time * PulseSpeed, PulseSpan) - BehindLength;
            _pulses[i].localPosition = new Vector3(0f, 0f, local);
            float fade = local < 0f ? Mathf.Clamp01(1f + local / BehindLength) : Mathf.Clamp01(1f - local / (PulseSpan - BehindLength));
            _pulses[i].localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, fade);
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
