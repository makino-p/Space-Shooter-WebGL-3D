using System.Collections.Generic;
using UnityEngine;

public class Projectile3D : MonoBehaviour
{
    public static readonly List<Projectile3D> Active = new List<Projectile3D>();

    public Faction3D Faction;
    public Vector3 Velocity;
    public bool Homing;

    private float _speed;
    private float _lifetime;
    private float _radius;
    private int _damage = 1;
    private Transform _target;
    private float _retarget;
    private const float HomingTurnRate = 200f;

    public static Projectile3D Spawn(Faction3D faction, Vector3 position, Vector3 direction, float speed, Color color,
                                     float lifetime = 2.5f, float size = 1f, bool homing = false)
    {
        GameObject go = new GameObject(faction == Faction3D.Player ? "Player_Shot" : "Enemy_Shot");
        go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(direction);

        // светящийся "болт": вытянутая капсула + ореол
        Factory3D.Part(PrimitiveType.Capsule, go.transform, Vector3.zero, new Vector3(0.22f, 1.1f, 0.22f) * size,
                       Factory3D.Lit(color, 4f), new Vector3(90, 0, 0));
        Factory3D.GlowBall(go.transform, Vector3.zero, 2.2f * size, new Color(color.r, color.g, color.b, 0.8f));

        Projectile3D p = go.AddComponent<Projectile3D>();
        p.Faction = faction;
        p._speed = speed;
        p.Velocity = direction.normalized * speed;
        p._lifetime = lifetime;
        p._radius = 0.35f * size;
        p.Homing = homing;
        return p;
    }

    // ---------- Аим-ассист ----------
    // Если в момент выстрела враг близко и почти по прицелу — пуля сама доворачивает к нему.
    private Hittable3D _assistTarget;
    private const float AssistTurnRate = 540f;

    public Projectile3D EnableAimAssist(float range, float coneDegrees)
    {
        Vector3 dir = Velocity.normalized;
        float best = float.MaxValue;
        foreach (Hittable3D h in Hittable3D.All)
        {
            if (h == null || !h.IsAlive || h.Faction != Faction3D.Enemy) continue;
            Vector3 d = h.transform.position - transform.position;
            float dist = d.magnitude;
            if (dist > range + h.Radius || dist < 0.01f) continue;
            // для близких целей конус шире (учитываем размер цели)
            float angle = Vector3.Angle(dir, d);
            float allowed = coneDegrees + Mathf.Atan2(h.Radius, dist) * Mathf.Rad2Deg;
            if (angle > allowed) continue;
            float score = angle * 2f + dist * 0.1f;
            if (score < best)
            {
                best = score;
                _assistTarget = h;
            }
        }
        return this;
    }

    void UpdateAimAssist(float dt)
    {
        if (_assistTarget == null || !_assistTarget.IsAlive)
        {
            _assistTarget = null;
            return;
        }
        Vector3 desired = (_assistTarget.transform.position - transform.position).normalized * _speed;
        Velocity = Vector3.RotateTowards(Velocity, desired, AssistTurnRate * Mathf.Deg2Rad * dt, 0f);
    }

    void OnEnable() { Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    void Update()
    {
        float dt = Time.deltaTime;
        _lifetime -= dt;
        if (_lifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (Homing)
        {
            UpdateHoming(dt);
        }
        else if (_assistTarget != null)
        {
            UpdateAimAssist(dt);
        }

        Vector3 from = transform.position;
        Vector3 to = from + Velocity * dt;

        Hittable3D hit = Hittable3D.SegmentHit(from, to, _radius, Faction);
        if (hit != null)
        {
            if (hit.OnHit(Faction, _damage))
            {
                Destroy(gameObject);
                return;
            }
        }

        transform.position = to;
        if (Velocity.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(Velocity);
        }
    }

    void UpdateHoming(float dt)
    {
        _retarget -= dt;
        if (_target == null || _retarget <= 0f)
        {
            _target = FindClosestEnemy();
            _retarget = 0.25f;
        }
        if (_target == null) return;

        Vector3 desired = (_target.position - transform.position).normalized * _speed;
        Velocity = Vector3.RotateTowards(Velocity, desired, HomingTurnRate * Mathf.Deg2Rad * dt, 0f);
    }

    // Ближайшая живая цель, по возможности впереди снаряда.
    Transform FindClosestEnemy()
    {
        Transform best = null;
        float bestScore = float.MaxValue;
        Vector3 fwd = Velocity.normalized;
        foreach (Hittable3D h in Hittable3D.All)
        {
            if (h == null || !h.IsAlive || h.Faction != Faction3D.Enemy) continue;
            Vector3 d = h.transform.position - transform.position;
            float dist = d.magnitude;
            float facing = Vector3.Dot(fwd, d / Mathf.Max(dist, 0.01f));
            float score = dist * (facing > 0f ? 1f : 3f);
            if (score < bestScore)
            {
                bestScore = score;
                best = h.transform;
            }
        }
        return best;
    }
}
