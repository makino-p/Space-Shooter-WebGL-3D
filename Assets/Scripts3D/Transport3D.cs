using System.Collections.Generic;
using UnityEngine;

// Транспортник: мирный грузовой корабль, просто идёт вдоль луча плотным потоком.
// Не стреляет. За уничтожение — 1 очко. Столкновение с ним наносит урон игроку.
public class Transport3D : Hittable3D
{
    public static readonly List<Transport3D> Active = new List<Transport3D>();

    private bool _alive = true;
    public override bool IsAlive { get { return _alive; } }

    private float _dirZ = -1f;
    private float _speed = 70f;
    private float _angle;
    private float _radius;
    private float _wobblePhase;
    private Transform _model;

    public static Transport3D Spawn(Vector3 position, float dirZ, float speed)
    {
        GameObject go = new GameObject("Transport");
        go.transform.position = position;
        Transport3D t = go.AddComponent<Transport3D>();
        t.Faction = Faction3D.Enemy;
        t.Radius = 3.4f;
        t._dirZ = Mathf.Sign(dirZ);
        t._speed = speed;
        t._angle = Mathf.Atan2(position.y, position.x);
        t._radius = new Vector2(position.x, position.y).magnitude;
        t._wobblePhase = Random.Range(0f, Mathf.PI * 2f);
        return t;
    }

    protected override void OnEnable() { base.OnEnable(); Active.Add(this); }
    protected override void OnDisable() { base.OnDisable(); Active.Remove(this); }

    void Start()
    {
        _model = Factory3D.BuildTransport(transform).transform;
        transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, _dirZ));
    }

    void Update()
    {
        if (!_alive) return;
        float dt = Time.deltaTime;

        // идёт вдоль луча, медленно закручиваясь вокруг него
        _angle += 0.15f * _dirZ * dt;
        float r = _radius + Mathf.Sin(Time.time * 0.8f + _wobblePhase) * 1.5f;
        Vector3 p = transform.position;
        p.z += _dirZ * _speed * dt;
        p.x = Mathf.Cos(_angle) * r;
        p.y = Mathf.Sin(_angle) * r;
        transform.position = p;
        _model.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 0.7f + _wobblePhase) * 6f);

        Player3D player = Player3D.Instance;
        if (player != null)
        {
            if (player.IsAlive && Vector3.Distance(player.transform.position, p) < Radius + player.Radius)
            {
                player.Damage();
                Die(false);
                return;
            }
            float rel = p.z - player.transform.position.z;
            if ((_dirZ < 0f && rel < -600f) || (_dirZ > 0f && rel > 1700f) || Mathf.Abs(rel) > 2200f)
            {
                Destroy(gameObject);
            }
        }
    }

    public override bool OnHit(Faction3D from, int damage)
    {
        if (!_alive) return false;
        Die(true);
        return true;
    }

    void Die(bool givePoints)
    {
        _alive = false;
        Factory3D.Explosion(transform.position, 1.1f, new Color(1f, 0.75f, 0.35f), new Vector3(0f, 0f, _dirZ * _speed));
        if (Game3D.Instance != null)
        {
            Game3D.PlaySound(Game3D.Instance.ExplosionClip, 0.45f, Random.Range(1.05f, 1.25f));
            if (givePoints) Game3D.Instance.AddScore(1);
        }
        Destroy(gameObject);
    }
}
