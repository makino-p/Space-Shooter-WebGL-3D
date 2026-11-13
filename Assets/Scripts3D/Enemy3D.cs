using System.Collections.Generic;
using UnityEngine;

public enum Enemy3DType
{
    Basic,      // охотник: гоняется за игроком, таранит
    Weaver,     // наездник спирали: летит по неоновой линии, "дышит" радиусом
    Shooter,    // наездник спирали: стреляет по игроку и сбивает пикапы перед собой
    RearGunner, // охотник: пролетает мимо и стреляет назад, когда игрок позади
    Dodger,     // охотник: уклоняется от выстрелов игрока
    Beamer      // наездник спирали: стреляет лазерным лучом
}

// Враги никогда не зависают в пространстве:
//  - наездники летят вдоль спирали туннеля к игроку, проскакивают мимо, разворачиваются и заходят снова;
//  - охотники преследуют игрока с упреждением на постоянной высокой скорости и ограниченным
//    радиусом поворота, поэтому проскакивают мимо и заходят на новый круг.
public class Enemy3D : Hittable3D
{
    public Enemy3DType Type;
    public bool HasShield;

    private bool _alive = true;
    public override bool IsAlive { get { return _alive; } }

    private Vector3 _velocity;
    private float _speed = 28f;
    private float _spawnTime;
    private GameObject _shieldBubble;
    private Transform _model;

    // наездник спирали
    private bool _isRider;
    private float _helixPhase;
    private float _helixRadius;
    private float _zDir = -1f;           // -1: к игроку (вдоль -Z), +1: обратно
    private float _riderZ;

    // охотник
    private const float ChaserTurnRate = 85f;   // град/с — не даёт развернуться на месте
    private const float RamDistance = 16f;
    private float _overshootUntil;

    // оружие
    private float _nextFire;
    private float _nextPickupCheck;
    private float _nextRearShot;
    private float _nextBeam;
    private Beam3D _beam;

    // уклонение
    private Vector3 _dodgeDir;
    private float _dodgeUntil;
    private float _nextDodgeCheck;

    private static readonly Color ShotColor = new Color(1f, 0.1f, 0.06f);
    private const float ShotSpeed = 70f;

    public static Enemy3D Spawn(Enemy3DType type, Vector3 position, bool shielded, int wave)
    {
        GameObject go = new GameObject("Enemy_" + type);
        go.transform.position = position;
        Enemy3D e = go.AddComponent<Enemy3D>();
        e.Type = type;
        e.HasShield = shielded;
        e.Radius = 3.2f; // модель стала крупнее
        e.Faction = Faction3D.Enemy;

        float baseSpeed;
        switch (type)
        {
            case Enemy3DType.Weaver: baseSpeed = 34f; break;
            case Enemy3DType.Shooter: baseSpeed = 30f; break;
            case Enemy3DType.Beamer: baseSpeed = 26f; break;
            case Enemy3DType.RearGunner: baseSpeed = 36f; break;
            case Enemy3DType.Dodger: baseSpeed = 32f; break;
            default: baseSpeed = 30f; break;
        }
        e._speed = baseSpeed + wave * 1.5f;
        return e;
    }

    void Start()
    {
        _model = Factory3D.BuildEnemyShip(transform, Type).transform;
        _spawnTime = Time.time;
        _nextFire = Time.time + Random.Range(1f, 2.2f);
        _nextBeam = Time.time + Random.Range(2.5f, 4f);

        _isRider = Type == Enemy3DType.Weaver || Type == Enemy3DType.Shooter || Type == Enemy3DType.Beamer;

        if (HasShield)
        {
            _shieldBubble = Factory3D.Part(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 7f,
                                           Factory3D.GlowFlat(new Color(0.15f, 0.45f, 0.6f, 0.3f)));
        }

        Vector3 p = transform.position;
        if (_isRider)
        {
            // встать на ближайшую "нитку" спирали: радиус и фаза подбираются под точку появления
            Tunnel3D t = Tunnel3D.Instance;
            float tunnelR = t != null ? t.Radius : 55f;
            _helixRadius = Mathf.Clamp(new Vector2(p.x, p.y).magnitude, tunnelR * 0.45f, tunnelR * 0.95f);
            float period = t != null ? t.HelixPeriod : 280f;
            float angle = Mathf.Atan2(p.y, p.x);
            _helixPhase = angle - p.z / period * Mathf.PI * 2f;
            _riderZ = p.z;
            _zDir = p.z > PlayerPos().z ? -1f : 1f; // всегда сначала к игроку
        }

        Vector3 toPlayer = PlayerPos() - p;
        _velocity = (toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.back) * _speed;
        transform.rotation = Quaternion.LookRotation(_velocity);
    }

    Player3D Player { get { return Player3D.Instance; } }
    Vector3 PlayerPos() { return Player != null ? Player.transform.position : Vector3.zero; }

    void Update()
    {
        if (!_alive) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Player3D player = Player;
        bool playerAlive = player != null && player.IsAlive;
        Vector3 toPlayer = PlayerPos() - transform.position;
        float dist = toPlayer.magnitude;
        Vector3 dirToPlayer = dist > 0.01f ? toPlayer / dist : transform.forward;

        Vector3 oldPos = transform.position;
        if (_isRider) MoveAlongSpiral(dt);
        else MoveChaser(dt, dirToPlayer, dist, playerAlive);

        Vector3 v = (transform.position - oldPos) / dt;
        if (v.sqrMagnitude > 1f) _velocity = v;

        // нос по направлению полёта + крен в повороте
        if (_velocity.sqrMagnitude > 1f)
        {
            Quaternion look = Quaternion.LookRotation(_velocity);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, dt * 8f);
        }

        if (playerAlive)
        {
            HandleWeapons(dirToPlayer, dist);
            if (Type == Enemy3DType.Dodger) CheckDodge();

            if (dist < Radius + player.Radius)
            {
                player.Damage();
                Die(false);
                return;
            }
        }

        if (_shieldBubble != null)
        {
            _shieldBubble.transform.localScale = Vector3.one * (7f + Mathf.Sin(Time.time * 4f) * 0.25f);
        }

        float relZ = transform.position.z - PlayerPos().z;
        if (relZ > 1200f || relZ < -700f || new Vector2(transform.position.x, transform.position.y).magnitude > 600f)
        {
            Destroy(gameObject);
        }
    }

    // ---------------- Наездник спирали ----------------
    void MoveAlongSpiral(float dt)
    {
        Tunnel3D t = Tunnel3D.Instance;
        float period = t != null ? t.HelixPeriod : 280f;
        float pz = PlayerPos().z;

        // туда-обратно вдоль пути: проскочил игрока — разворот, улетел далеко вперёд — снова к игроку
        if (_zDir < 0f && _riderZ < pz - 130f) _zDir = 1f;
        else if (_zDir > 0f && _riderZ > pz + 320f) _zDir = -1f;

        float speed = _speed;
        if (Mathf.Abs(_riderZ - pz) > 260f) speed *= 1.8f; // далеко — догоняет быстрее

        _riderZ += _zDir * speed * dt;
        float r = _helixRadius;
        if (Type == Enemy3DType.Weaver)
        {
            r += Mathf.Sin((Time.time - _spawnTime) * 2.5f) * 14f; // "дышит" — то к оси, то к стенке
        }
        float a = _helixPhase + _riderZ / period * Mathf.PI * 2f;
        Vector3 target = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, _riderZ);

        transform.position = Vector3.MoveTowards(transform.position, target, speed * 2f * dt);
    }

    // ---------------- Охотник ----------------
    void MoveChaser(float dt, Vector3 dirToPlayer, float dist, bool playerAlive)
    {
        float speed = _speed;
        float turn = ChaserTurnRate;
        Vector3 desired;

        if (!playerAlive)
        {
            desired = transform.forward;
        }
        else if (Time.time < _dodgeUntil)
        {
            desired = (_velocity.normalized + _dodgeDir * 2f).normalized;
            speed *= 1.4f;
            turn = 360f;
        }
        else if (Time.time < _overshootUntil)
        {
            // проскочил мимо игрока — уходит на новый заход, не тормозя
            desired = _velocity.normalized;
        }
        else
        {
            // упреждение: летит туда, где игрок будет
            Player3D p = Player;
            float lead = Mathf.Clamp(dist / Mathf.Max(speed, 1f), 0f, 1.2f);
            Vector3 aimPoint = p.transform.position + p.Velocity * lead;
            desired = (aimPoint - transform.position).normalized;

            if (dist < RamDistance)
            {
                // таран: резкий бросок
                speed *= 1.6f;
                turn = 160f;
            }

            // уже проскочил (игрок сзади и близко) — заход на новый круг
            if (dist < 25f && Vector3.Dot(_velocity.normalized, dirToPlayer) < -0.2f)
            {
                _overshootUntil = Time.time + (Type == Enemy3DType.RearGunner ? 1.8f : 1.1f);
            }
        }

        if (dist > 180f) speed *= 1.8f; // далеко — нагоняет

        Vector3 newDir = Vector3.RotateTowards(_velocity.normalized, desired, turn * Mathf.Deg2Rad * dt, 0f);
        transform.position += newDir * speed * dt;
    }

    // ---------------- Оружие ----------------
    void HandleWeapons(Vector3 dirToPlayer, float dist)
    {
        switch (Type)
        {
            case Enemy3DType.Shooter:
                ShootPickupsInFront();
                if (Time.time >= _nextFire && dist < 160f)
                {
                    FireAtPlayer(transform.position + transform.forward * 3f);
                    _nextFire = Time.time + Random.Range(1.1f, 1.9f);
                }
                break;

            case Enemy3DType.RearGunner:
                ShootPickupsInFront();
                // враг "знает", что игрок позади него — стреляет назад из хвостовой пушки
                float behind = Vector3.Dot(transform.forward, dirToPlayer);
                if (behind < -0.5f && dist < 90f && Time.time >= _nextRearShot)
                {
                    FireAtPlayer(transform.position - transform.forward * 3f);
                    _nextRearShot = Time.time + 0.45f;
                }
                break;

            case Enemy3DType.Beamer:
                if (_beam == null && Time.time >= _nextBeam && dist < 160f)
                {
                    Vector3 origin = new Vector3(0f, 0f, 3f);
                    Vector3 worldOrigin = transform.TransformPoint(origin);
                    Vector3 aim = (PlayerPos() - worldOrigin).normalized;
                    _beam = Beam3D.Spawn(transform, origin, aim, 180f, 1.0f, 0.7f, 1.4f, Factory3D.EnemyColor(Type));
                    _nextBeam = Time.time + Random.Range(4f, 5.5f);
                }
                break;

            case Enemy3DType.Basic:
                // простые охотники изредка постреливают на подлёте
                if (Time.time >= _nextFire && dist < 90f && Vector3.Dot(transform.forward, dirToPlayer) > 0.8f)
                {
                    FireAtPlayer(transform.position + transform.forward * 3f);
                    _nextFire = Time.time + Random.Range(2.5f, 4f);
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
            if (dist < 90f && Vector3.Angle(transform.forward, d) < 14f)
            {
                Vector3 from = transform.position + transform.forward * 3f;
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
            if (dist > 45f) continue;

            Vector3 v = p.Velocity.normalized;
            float along = Vector3.Dot(rel, v);
            if (along <= 0f) continue; // уже пролетел

            Vector3 miss = rel - v * along; // насколько снаряд промахнётся, если не уклоняться
            if (miss.magnitude < Radius + 3f)
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

    // ---------------- Попадания ----------------
    public override bool OnHit(Faction3D from, int damage)
    {
        if (!_alive) return false;

        if (HasShield)
        {
            HasShield = false;
            if (_shieldBubble != null) Destroy(_shieldBubble);
            Factory3D.ShieldHit(transform.position, new Color(0.3f, 0.8f, 1f));
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
        Factory3D.Explosion(transform.position, 1.4f, Factory3D.EnemyColor(Type), _velocity);
        if (Game3D.Instance != null)
        {
            Game3D.PlaySound(Game3D.Instance.ExplosionClip, 0.7f, Random.Range(0.9f, 1.15f));
            if (givePoints) Game3D.Instance.AddScore(Type == Enemy3DType.Basic ? 10 : 20);
        }
        Destroy(gameObject);
    }
}
