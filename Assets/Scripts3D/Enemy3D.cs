using System.Collections.Generic;
using UnityEngine;

public enum Enemy3DType
{
    Basic,      // летит на игрока, заходит по дуге
    Weaver,     // волнообразная траектория
    Shooter,    // держит дистанцию, стреляет и сбивает пикапы перед собой
    RearGunner, // пролетает мимо и стреляет назад, когда игрок позади
    Dodger,     // уклоняется от выстрелов игрока
    Beamer      // стреляет лазерным лучом
}

public class Enemy3D : Hittable3D
{
    public Enemy3DType Type;
    public bool HasShield;

    private bool _alive = true;
    public override bool IsAlive { get { return _alive; } }

    private Vector3 _velocity;
    private float _speed = 15f;
    private float _turnRate = 70f; // град/с
    private float _spawnTime;
    private float _weavePhase;
    private GameObject _shieldBubble;
    private Transform _model;

    // оружие
    private float _nextFire;
    private float _nextPickupCheck;
    private float _nextRearShot;
    private float _nextBeam;
    private Beam3D _beam;

    // таран / пролёт / уклонение
    private const float RamDistance = 14f;
    private bool _ramming;
    private float _passUntil;
    private Vector3 _dodgeDir;
    private float _dodgeUntil;
    private float _nextDodgeCheck;

    private static readonly Color ShotColor = new Color(1f, 0.25f, 0.15f);
    private const float ShotSpeed = 55f;

    public static Enemy3D Spawn(Enemy3DType type, Vector3 position, bool shielded, int wave)
    {
        GameObject go = new GameObject("Enemy_" + type);
        go.transform.position = position;
        Enemy3D e = go.AddComponent<Enemy3D>();
        e.Type = type;
        e.HasShield = shielded;
        e.Radius = 2.1f;
        e.Faction = Faction3D.Enemy;

        float baseSpeed;
        switch (type)
        {
            case Enemy3DType.Weaver: baseSpeed = 15f; break;
            case Enemy3DType.Shooter: baseSpeed = 13f; break;
            case Enemy3DType.RearGunner: baseSpeed = 19f; break;
            case Enemy3DType.Dodger: baseSpeed = 15f; break;
            case Enemy3DType.Beamer: baseSpeed = 9f; break;
            default: baseSpeed = 16f; break;
        }
        e._speed = baseSpeed + wave * 0.8f;
        return e;
    }

    void Start()
    {
        _model = Factory3D.BuildEnemyShip(transform, Type).transform;
        _spawnTime = Time.time;
        _weavePhase = Random.Range(0f, Mathf.PI * 2f);
        _nextFire = Time.time + Random.Range(1.5f, 3f);
        _nextBeam = Time.time + Random.Range(3f, 5f);

        if (HasShield)
        {
            _shieldBubble = Factory3D.Part(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 5.5f,
                                           Factory3D.GlowFlat(new Color(0.15f, 0.45f, 0.6f, 0.3f)));
        }

        // заход под углом: начальное направление отклонено от игрока
        Vector3 toPlayer = PlayerPos() - transform.position;
        Vector3 axis = Vector3.Cross(toPlayer, Random.onUnitSphere).normalized;
        if (axis.sqrMagnitude < 0.01f) axis = Vector3.up;
        _velocity = Quaternion.AngleAxis(Random.Range(10f, 30f), axis) * toPlayer.normalized * _speed;
        transform.rotation = Quaternion.LookRotation(_velocity);
    }

    Player3D Player { get { return Player3D.Instance; } }
    Vector3 PlayerPos() { return Player != null ? Player.transform.position : Vector3.zero; }

    void Update()
    {
        if (!_alive) return;

        Player3D player = Player;
        bool playerAlive = player != null && player.IsAlive;
        Vector3 toPlayer = PlayerPos() - transform.position;
        float dist = toPlayer.magnitude;
        Vector3 dirToPlayer = dist > 0.01f ? toPlayer / dist : transform.forward;

        Vector3 desired;
        float speed = _speed;
        // вдалеке от игрока (только вылетели из врат) — быстрый пролёт по туннелю
        if (playerAlive && dist > 110f) speed *= 2.8f;
        float turn = _turnRate;

        // ----- агрессия: таран, если игрок близко -----
        _ramming = playerAlive && Type != Enemy3DType.Beamer && dist < RamDistance && Time.time > _passUntil;
        if (_ramming)
        {
            desired = dirToPlayer;
            speed *= 2.2f;
            turn = 200f;
        }
        else if (Time.time < _dodgeUntil)
        {
            desired = _dodgeDir;
            speed = 38f;
            turn = 400f;
        }
        else
        {
            desired = DesiredDirection(dirToPlayer, dist, ref speed, playerAlive);
        }

        // плавный поворот вектора скорости
        Vector3 targetVel = desired.normalized * speed;
        _velocity = Vector3.RotateTowards(_velocity, targetVel, turn * Mathf.Deg2Rad * Time.deltaTime, 30f * Time.deltaTime);
        transform.position += _velocity * Time.deltaTime;
        if (_velocity.sqrMagnitude > 0.1f)
        {
            Quaternion look = Quaternion.LookRotation(_velocity);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 6f);
        }

        if (playerAlive)
        {
            HandleWeapons(dirToPlayer, dist);
            if (Type == Enemy3DType.Dodger) CheckDodge();

            // столкновение с игроком
            if (dist < Radius + player.Radius)
            {
                player.Damage();
                Die(false);
                return;
            }
        }

        if (_shieldBubble != null)
        {
            _shieldBubble.transform.localScale = Vector3.one * (5.5f + Mathf.Sin(Time.time * 4f) * 0.2f);
        }

        float relZ = transform.position.z - PlayerPos().z;
        if (relZ > 900f || relZ < -500f || new Vector2(transform.position.x, transform.position.y).magnitude > 500f)
        {
            Destroy(gameObject);
        }
    }

    // ----- движение по типу врага -----
    Vector3 DesiredDirection(Vector3 dirToPlayer, float dist, ref float speed, bool playerAlive)
    {
        if (!playerAlive)
        {
            return transform.forward;
        }

        Vector3 side = Vector3.Cross(dirToPlayer, Vector3.up);
        if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(dirToPlayer, Vector3.right);
        side.Normalize();
        Vector3 up = Vector3.Cross(side, dirToPlayer).normalized;

        switch (Type)
        {
            case Enemy3DType.Weaver:
                {
                    float t = (Time.time - _spawnTime) * 2.2f + _weavePhase;
                    return dirToPlayer + side * Mathf.Sin(t) * 1.3f + up * Mathf.Cos(t * 0.5f) * 0.5f;
                }

            case Enemy3DType.Shooter:
            case Enemy3DType.Dodger:
                return KeepDistance(dirToPlayer, side, dist, Type == Enemy3DType.Shooter ? 28f : 20f,
                                    Type == Enemy3DType.Shooter ? 45f : 35f);

            case Enemy3DType.Beamer:
                if (_beam != null) { speed *= 0.2f; }
                return KeepDistance(dirToPlayer, side, dist, 45f, 65f);

            case Enemy3DType.RearGunner:
                // атакующий заход: пролететь вплотную мимо игрока, потом развернуться
                if (Time.time < _passUntil)
                {
                    return transform.forward;
                }
                if (dist < 18f)
                {
                    _passUntil = Time.time + 2.5f;
                    return transform.forward;
                }
                return dirToPlayer + side * 0.25f;

            default:
                return dirToPlayer;
        }
    }

    Vector3 KeepDistance(Vector3 dirToPlayer, Vector3 side, float dist, float min, float max)
    {
        if (dist > max) return dirToPlayer + side * 0.3f;
        if (dist < min) return -dirToPlayer + side;
        return side + dirToPlayer * 0.1f; // кружит вокруг игрока
    }

    // ----- оружие -----
    void HandleWeapons(Vector3 dirToPlayer, float dist)
    {
        switch (Type)
        {
            case Enemy3DType.Shooter:
                ShootPickupsInFront();
                if (Time.time >= _nextFire && dist < 90f)
                {
                    FireAtPlayer(transform.position + transform.forward * 2.2f);
                    _nextFire = Time.time + Random.Range(1.4f, 2.4f);
                }
                break;

            case Enemy3DType.RearGunner:
                ShootPickupsInFront();
                // враг "знает", что игрок позади него — стреляет назад из хвостовой пушки
                float behind = Vector3.Dot(transform.forward, dirToPlayer);
                if (behind < -0.6f && dist < 70f && Time.time >= _nextRearShot)
                {
                    FireAtPlayer(transform.position - transform.forward * 2.4f);
                    _nextRearShot = Time.time + 0.6f;
                }
                break;

            case Enemy3DType.Beamer:
                if (_beam == null && Time.time >= _nextBeam && dist < 120f)
                {
                    Vector3 origin = new Vector3(0f, -0.1f, 2.6f);
                    Vector3 worldOrigin = transform.TransformPoint(origin);
                    Vector3 aim = (PlayerPos() - worldOrigin).normalized;
                    _beam = Beam3D.Spawn(transform, origin, aim, 150f, 1.1f, 0.7f, 1.3f, Factory3D.EnemyColor(Type));
                    _nextBeam = Time.time + Random.Range(5f, 6.5f);
                }
                break;
        }
    }

    void FireAtPlayer(Vector3 from)
    {
        Player3D p = Player;
        if (p == null) return;
        // упреждение по скорости игрока
        float t = Vector3.Distance(from, p.transform.position) / ShotSpeed;
        Vector3 predicted = p.transform.position + p.Velocity * t * 0.8f;
        Projectile3D.Spawn(Faction3D.Enemy, from, (predicted - from).normalized, ShotSpeed, ShotColor, 3.5f, 1.2f);
        Game3D.PlaySound(Game3D.Instance != null ? Game3D.Instance.LaserClip : null, 0.18f, 0.7f);
    }

    // Пикап прямо по курсу врага — сбить его, пока игрок не забрал.
    void ShootPickupsInFront()
    {
        if (Time.time < _nextPickupCheck) return;
        _nextPickupCheck = Time.time + 0.3f;

        foreach (Pickup3D pickup in Pickup3D.Pickups)
        {
            if (pickup == null) continue;
            Vector3 d = pickup.transform.position - transform.position;
            float dist = d.magnitude;
            if (dist < 75f && Vector3.Angle(transform.forward, d) < 14f)
            {
                Vector3 from = transform.position + transform.forward * 2.2f;
                Projectile3D.Spawn(Faction3D.Enemy, from, (pickup.transform.position - from).normalized, ShotSpeed, ShotColor, 3f, 1.2f);
                _nextPickupCheck = Time.time + 1f;
                return;
            }
        }
    }

    // Dodger: видит летящий в него выстрел и уходит в сторону.
    void CheckDodge()
    {
        if (Time.time < _nextDodgeCheck || Time.time < _dodgeUntil) return;
        _nextDodgeCheck = Time.time + 0.08f;

        foreach (Projectile3D p in Projectile3D.Active)
        {
            if (p == null || p.Faction != Faction3D.Player) continue;
            Vector3 rel = transform.position - p.transform.position;
            float dist = rel.magnitude;
            if (dist > 35f) continue;

            Vector3 v = p.Velocity.normalized;
            float along = Vector3.Dot(rel, v);
            if (along <= 0f) continue; // уже пролетел

            Vector3 miss = rel - v * along; // насколько снаряд промахнётся, если не уклоняться
            if (miss.magnitude < Radius + 2.5f)
            {
                Vector3 dodge = miss.sqrMagnitude > 0.01f ? miss.normalized : Vector3.Cross(v, Vector3.up).normalized;
                if (dodge.sqrMagnitude < 0.01f) dodge = Vector3.right;
                _dodgeDir = dodge;
                _dodgeUntil = Time.time + 0.45f;
                _nextDodgeCheck = Time.time + 0.7f;
                return;
            }
        }
    }

    // ----- попадания -----
    public override bool OnHit(Faction3D from, int damage)
    {
        if (!_alive) return false;

        if (HasShield)
        {
            HasShield = false;
            if (_shieldBubble != null) Destroy(_shieldBubble);
            Factory3D.Explosion(transform.position, 0.5f, new Color(0.3f, 0.8f, 1f));
            return true;
        }

        Die(true);
        return true;
    }

    void Die(bool givePoints)
    {
        if (!_alive) return;
        _alive = false;
        if (_beam != null) _beam.Stop();
        Factory3D.Explosion(transform.position, 1.2f, Factory3D.EnemyColor(Type));
        if (Game3D.Instance != null)
        {
            Game3D.PlaySound(Game3D.Instance.ExplosionClip, 0.7f, Random.Range(0.9f, 1.15f));
            if (givePoints) Game3D.Instance.AddScore(Type == Enemy3DType.Basic ? 10 : 20);
        }
        Destroy(gameObject);
    }
}
