using UnityEngine;

// Лазерный луч: сначала тонкий мигающий прицел (луч фиксирует направление), затем мощный луч.
// Попадание по игроку считается как расстояние от игрока до отрезка луча.
public class Beam3D : MonoBehaviour
{
    private Transform _owner;
    private Vector3 _localOrigin;
    private Vector3 _direction;
    private float _length;
    private float _warmup;
    private float _active;
    private float _width;
    private float _timer;
    private bool _isActive;
    private bool _hasHit;
    private LineRenderer _line;
    private Color _color;

    public static Beam3D Spawn(Transform owner, Vector3 localOrigin, Vector3 worldDirection, float length,
                               float warmup, float active, float width, Color color)
    {
        GameObject go = new GameObject("Beam");
        Beam3D b = go.AddComponent<Beam3D>();
        b._owner = owner;
        b._localOrigin = localOrigin;
        b._direction = worldDirection.normalized;
        b._length = length;
        b._warmup = warmup;
        b._active = active;
        b._width = width;
        b._color = color;

        b._line = go.AddComponent<LineRenderer>();
        b._line.positionCount = 2;
        b._line.useWorldSpace = true;
        b._line.sharedMaterial = Factory3D.Glow(Color.white);
        b._line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        b._line.receiveShadows = false;
        b.UpdateLine(0.08f, 0.4f);
        return b;
    }

    Vector3 Origin()
    {
        return _owner != null ? _owner.TransformPoint(_localOrigin) : transform.position;
    }

    void UpdateLine(float width, float alpha)
    {
        Vector3 o = Origin();
        _line.SetPosition(0, o);
        _line.SetPosition(1, o + _direction * _length);
        _line.startWidth = width;
        _line.endWidth = width;
        Color c = _color;
        c.a = alpha;
        _line.startColor = c;
        _line.endColor = c;
    }

    void Update()
    {
        if (_owner == null)
        {
            Destroy(gameObject);
            return;
        }

        _timer += Time.deltaTime;
        if (!_isActive)
        {
            float blink = Mathf.PingPong(Time.time * 8f, 1f);
            UpdateLine(0.08f + 0.06f * blink, 0.25f + 0.5f * blink);
            if (_timer >= _warmup)
            {
                _isActive = true;
                _timer = 0f;
                Game3D.PlaySound(Game3D.Instance != null ? Game3D.Instance.LaserClip : null, 0.6f, 0.5f);
            }
            return;
        }

        float pulse = 1f + 0.2f * Mathf.Sin(Time.time * 50f);
        UpdateLine(_width * pulse, 1f);
        CheckPlayerHit();

        if (_timer >= _active)
        {
            Destroy(gameObject);
        }
    }

    void CheckPlayerHit()
    {
        if (_hasHit) return;
        Player3D player = Player3D.Instance;
        if (player == null || !player.IsAlive) return;

        Vector3 o = Origin();
        Vector3 p = player.transform.position;
        float t = Mathf.Clamp(Vector3.Dot(p - o, _direction), 0f, _length);
        float dist = Vector3.Distance(p, o + _direction * t);
        if (dist < player.Radius + _width * 0.5f)
        {
            _hasHit = true;
            player.Damage();
        }
    }

    public void Stop()
    {
        Destroy(gameObject);
    }
}
