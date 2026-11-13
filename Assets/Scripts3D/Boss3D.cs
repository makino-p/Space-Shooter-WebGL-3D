using System.Collections;
using UnityEngine;

// Босс финальной волны: прилетает в центр арены и остаётся там.
// Атаки по кругу: сферический залп, веер прицельных выстрелов, три лазерных луча, вызов подкрепления.
public class Boss3D : Hittable3D
{
    public int MaxHealth = 60;
    public Vector3 StopPosition = Vector3.zero;
    public float EnterSpeed = 14f;

    private int _health;
    private bool _alive = true;
    private bool _arrived;
    private Transform _model;
    private float _flashUntil;
    private Vector3 _baseScale = Vector3.one;

    public override bool IsAlive { get { return _alive; } }
    public float Health01 { get { return (float)_health / MaxHealth; } }

    private static readonly Color ShotColor = new Color(1f, 0.45f, 0.1f);

    void Start()
    {
        Faction = Faction3D.Enemy;
        Radius = 10f;
        _health = MaxHealth;
        _model = Factory3D.BuildBoss(transform).transform;
        _baseScale = _model.localScale;
        if (Game3D.Instance != null) Game3D.Instance.UI.ShowBossHealth(1f);
    }

    Player3D Player { get { return Player3D.Instance; } }

    void Update()
    {
        if (!_alive) return;

        Player3D p = Player;
        if (p != null && p.IsAlive)
        {
            // медленно разворачивается к игроку
            Quaternion look = Quaternion.LookRotation(p.transform.position - transform.position);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 25f * Time.deltaTime);

            if (Vector3.Distance(p.transform.position, transform.position) < Radius + p.Radius)
            {
                p.Damage();
            }
        }

        if (!_arrived)
        {
            transform.position = Vector3.MoveTowards(transform.position, StopPosition, EnterSpeed * Time.deltaTime);
            if ((transform.position - StopPosition).sqrMagnitude < 0.01f)
            {
                _arrived = true;
                StartCoroutine(AttackLoop());
            }
        }

        // вспышка при попадании
        float s = Time.time < _flashUntil ? 1.04f : 1f;
        _model.localScale = Vector3.Lerp(_model.localScale, _baseScale * s, Time.deltaTime * 20f);
        _model.Rotate(0f, 0f, 8f * Time.deltaTime, Space.Self);
    }

    IEnumerator AttackLoop()
    {
        yield return new WaitForSeconds(1f);
        int cycle = 0;
        while (_alive)
        {
            yield return StartCoroutine(SphereBurst());
            yield return new WaitForSeconds(1.2f);
            if (!_alive) yield break;

            yield return StartCoroutine(AimedSpread());
            yield return new WaitForSeconds(1.2f);
            if (!_alive) yield break;

            yield return StartCoroutine(BeamVolley());
            yield return new WaitForSeconds(1.5f);
            if (!_alive) yield break;

            cycle++;
            if (Health01 < 0.5f && cycle % 2 == 0 && Game3D.Instance != null)
            {
                // вызов подкрепления на второй половине боя
                for (int i = 0; i < 2; i++)
                {
                    Vector3 pos = transform.position + Random.onUnitSphere * (Radius + 6f);
                    Game3D.Instance.SpawnEnemy(Enemy3DType.Basic, pos, false);
                }
                Game3D.Instance.UI.ShowMessage("REINFORCEMENTS!", 1.5f, new Color(1f, 0.4f, 0.3f));
            }
        }
    }

    // Атака 1: сфера из 40 снарядов во все стороны, две волны.
    IEnumerator SphereBurst()
    {
        for (int wave = 0; wave < 2; wave++)
        {
            const int count = 40;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            float offset = wave * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float y = 1f - (i / (count - 1f)) * 2f;
                float r = Mathf.Sqrt(1f - y * y);
                float th = golden * i + offset;
                Vector3 dir = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
                Projectile3D.Spawn(Faction3D.Enemy, transform.position + dir * (Radius + 1f), dir, 28f, ShotColor, 7f, 2f);
            }
            Game3D.PlaySound(Game3D.Instance != null ? Game3D.Instance.LaserClip : null, 0.6f, 0.45f);
            yield return new WaitForSeconds(0.8f);
        }
    }

    // Атака 2: 4 залпа по 7 выстрелов конусом в сторону игрока.
    IEnumerator AimedSpread()
    {
        for (int volley = 0; volley < 4; volley++)
        {
            Player3D p = Player;
            if (p == null || !p.IsAlive) yield break;
            Vector3 from = transform.position + transform.forward * (Radius + 1f);
            Vector3 aim = (p.transform.position - from).normalized;
            Vector3 side = Vector3.Cross(aim, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 up = Vector3.Cross(side, aim);

            Projectile3D.Spawn(Faction3D.Enemy, from, aim, 60f, ShotColor, 5f, 1.6f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Vector3 dir = (aim + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.12f).normalized;
                Projectile3D.Spawn(Faction3D.Enemy, from, dir, 60f, ShotColor, 5f, 1.6f);
            }
            Game3D.PlaySound(Game3D.Instance != null ? Game3D.Instance.LaserClip : null, 0.5f, 0.6f);
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Атака 3: три луча — в игрока и по бокам от него (между ними можно увернуться).
    IEnumerator BeamVolley()
    {
        Player3D p = Player;
        if (p == null || !p.IsAlive) yield break;

        Vector3 target = p.transform.position;
        Vector3 aim = (target - transform.position).normalized;
        Vector3 side = Vector3.Cross(aim, Vector3.up).normalized;
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;

        Vector3[] targets = { target, target + side * 12f, target - side * 12f };
        foreach (Vector3 t in targets)
        {
            Vector3 dir = (t - transform.position).normalized;
            Vector3 localOrigin = transform.InverseTransformPoint(transform.position + dir * (Radius + 0.5f));
            Beam3D.Spawn(transform, localOrigin, dir, 240f, 1.3f, 1.0f, 2.6f, new Color(1f, 0.25f, 0.15f));
        }
        yield return new WaitForSeconds(2.5f);
    }

    public override bool OnHit(Faction3D from, int damage)
    {
        if (!_alive) return false;
        if (!_arrived)
        {
            return true; // неуязвим, пока заходит на позицию
        }

        _health -= damage;
        _flashUntil = Time.time + 0.06f;
        Factory3D.Explosion(transform.position + (Player != null ? (Player.transform.position - transform.position).normalized * Radius : Vector3.zero),
                            0.4f, new Color(1f, 0.7f, 0.3f));
        if (Game3D.Instance != null) Game3D.Instance.UI.ShowBossHealth(Health01);

        if (_health <= 0)
        {
            StartCoroutine(DeathSequence());
        }
        return true;
    }

    IEnumerator DeathSequence()
    {
        _alive = false;
        foreach (Beam3D b in Compat.FindAll<Beam3D>()) b.Stop();
        if (Game3D.Instance != null)
        {
            Game3D.Instance.UI.HideBossHealth();
            Game3D.Instance.AddScore(500);
        }

        for (int i = 0; i < 8; i++)
        {
            Factory3D.Explosion(transform.position + Random.insideUnitSphere * Radius, 2f, new Color(1f, 0.5f, 0.15f));
            if (Game3D.Instance != null)
            {
                Game3D.PlaySound(Game3D.Instance.ExplosionClip, 0.8f, Random.Range(0.6f, 1f));
                Game3D.Instance.CameraRig.Shake(0.3f, 0.3f);
            }
            yield return new WaitForSeconds(0.25f);
        }
        Factory3D.Explosion(transform.position, 6f, new Color(1f, 0.8f, 0.4f));
        if (Game3D.Instance != null) Game3D.Instance.OnBossDefeated();
        Destroy(gameObject);
    }
}
