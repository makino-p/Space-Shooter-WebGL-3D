using System.Collections.Generic;
using UnityEngine;

// Неоновый туннель из спиральных линий вдоль оси Z.
// Начало — у игрока, в дальнем конце — красные "врата", откуда прилетают враги.
// Бегущие по кольцам огни показывают, куда лететь. Стены не твёрдые — из туннеля можно вылетать.
public class Tunnel3D : MonoBehaviour
{
    public static Tunnel3D Instance;

    public float StartZ = -140f;
    public float EndZ = 380f;
    public float Radius = 28f;
    public int HelixCount = 3;          // спирали в каждую сторону
    public float TurnsPerUnit = 1f / 140f;
    public float RingSpacing = 40f;
    public float ArenaRadius = 210f;    // как далеко от оси туннеля можно улететь

    public Vector3 GatePosition { get { return new Vector3(0f, 0f, EndZ + 25f); } }
    public Vector3 Axis { get { return Vector3.forward; } }

    private readonly List<LineRenderer> _rings = new List<LineRenderer>();
    private readonly List<LineRenderer> _helices = new List<LineRenderer>();
    private Transform _gate;
    private static readonly Color Cyan = new Color(0.1f, 0.9f, 1f);
    private static readonly Color Magenta = new Color(1f, 0.15f, 0.85f);
    private static readonly Color GateRed = new Color(1f, 0.15f, 0.1f);

    public static Tunnel3D Create()
    {
        GameObject go = new GameObject("Neon_Tunnel");
        Tunnel3D t = go.AddComponent<Tunnel3D>();
        t.Build();
        return t;
    }

    void Awake() { Instance = this; }

    void Build()
    {
        // спирали: голубые по часовой, розовые — против, образуют решётку
        for (int h = 0; h < HelixCount; h++)
        {
            float phase = h * Mathf.PI * 2f / HelixCount;
            _helices.Add(BuildHelix(phase, 1f, Cyan));
            _helices.Add(BuildHelix(phase + Mathf.PI / HelixCount, -1f, Magenta));
        }

        // кольца
        for (float z = StartZ; z <= EndZ + 0.01f; z += RingSpacing)
        {
            _rings.Add(BuildRing(z, Radius, 0.35f, Cyan));
        }

        BuildGate();
    }

    LineRenderer NewLine(string name, int points, float width, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.positionCount = points;
        lr.useWorldSpace = false;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.sharedMaterial = Factory3D.GlowFlat(Color.white);
        lr.startColor = color;
        lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 2;
        return lr;
    }

    LineRenderer BuildHelix(float phase, float direction, Color color)
    {
        int points = Mathf.CeilToInt((EndZ - StartZ) / 3f) + 1;
        LineRenderer lr = NewLine("Helix", points, 0.45f, color);
        for (int i = 0; i < points; i++)
        {
            float z = Mathf.Lerp(StartZ, EndZ, i / (points - 1f));
            float a = phase + direction * (z - StartZ) * TurnsPerUnit * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * Radius, Mathf.Sin(a) * Radius, z));
        }
        // спираль ярче у врат, тусклее у старта
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                  new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0.9f, 1f) });
        lr.colorGradient = g;
        return lr;
    }

    LineRenderer BuildRing(float z, float radius, float width, Color color)
    {
        const int points = 64;
        LineRenderer lr = NewLine("Ring", points, width, color);
        lr.loop = true;
        for (int i = 0; i < points; i++)
        {
            float a = i * Mathf.PI * 2f / points;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z));
        }
        return lr;
    }

    void BuildGate()
    {
        GameObject gate = new GameObject("Enemy_Gate");
        gate.transform.SetParent(transform, false);
        gate.transform.position = GatePosition;
        _gate = gate.transform;

        LineRenderer outer = BuildRing(0f, Radius * 1.25f, 1.6f, GateRed);
        outer.transform.SetParent(_gate, false);
        LineRenderer inner = BuildRing(0f, Radius * 0.9f, 0.8f, new Color(1f, 0.45f, 0.1f));
        inner.transform.SetParent(_gate, false);
        Factory3D.GlowBall(_gate, Vector3.zero, Radius * 3.2f, new Color(1f, 0.1f, 0.05f, 0.35f));

        // маркер-надпись
        GameObject label = new GameObject("Gate_Label");
        label.transform.SetParent(_gate, false);
        label.transform.localPosition = new Vector3(0f, Radius * 1.6f, 0f);
        TextMesh tm = label.AddComponent<TextMesh>();
        tm.text = "ENEMY GATE";
        tm.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        tm.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.fontSize = 64;
        tm.characterSize = 1.2f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = GateRed;
        label.AddComponent<Billboard3D>();
    }

    void Update()
    {
        // бегущие огни: импульс идёт от старта к вратам — "лети туда"
        float t = Time.time * 1.2f;
        for (int i = 0; i < _rings.Count; i++)
        {
            float wave = Mathf.Repeat(t - i * 0.18f, 1.6f);
            float glow = Mathf.Clamp01(1f - wave * 2.5f);
            Color c = Color.Lerp(Cyan * 0.55f, Color.white, glow);
            c.a = 0.45f + 0.55f * glow;
            _rings[i].startColor = c;
            _rings[i].endColor = c;
            float w = 0.35f + 0.5f * glow;
            _rings[i].startWidth = w;
            _rings[i].endWidth = w;
        }

        if (_gate != null)
        {
            _gate.Rotate(0f, 0f, 20f * Time.deltaTime, Space.Self);
            float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 3f);
            _gate.localScale = Vector3.one * pulse;
        }
    }

    // ---------------- Геометрия для остальных систем ----------------
    public Vector3 ClosestPointOnAxis(Vector3 p)
    {
        return new Vector3(0f, 0f, Mathf.Clamp(p.z, StartZ - 40f, EndZ + 60f));
    }

    // Держит точку в пределах арены вокруг туннеля (можно вылететь из туннеля, но не улететь в бесконечность).
    public Vector3 ClampToArena(Vector3 p, out bool clamped)
    {
        Vector3 c = ClosestPointOnAxis(p);
        Vector3 d = p - c;
        clamped = false;
        if (d.magnitude > ArenaRadius)
        {
            p = c + d.normalized * ArenaRadius;
            clamped = true;
        }
        if (p.z < StartZ - 80f) { p.z = StartZ - 80f; clamped = true; }
        if (p.z > EndZ + 90f) { p.z = EndZ + 90f; clamped = true; }
        return p;
    }

    public bool IsInsideTunnel(Vector3 p, float margin)
    {
        return p.z > StartZ && p.z < EndZ && new Vector2(p.x, p.y).magnitude < Radius + margin;
    }
}
