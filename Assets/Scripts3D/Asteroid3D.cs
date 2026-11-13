using UnityEngine;

// Астероид. Стартовый астероид нужно уничтожить, чтобы начались волны (как в 2D-версии).
// Остальные астероиды — препятствия: столкновение наносит урон.
public class Asteroid3D : Hittable3D
{
    public bool IsStarter;
    public float Size = 3f;

    private int _hp = 1;
    private bool _alive = true;
    private Transform _model;
    private Vector3 _spin;
    private Vector3 _drift;

    public override bool IsAlive { get { return _alive; } }

    public static Asteroid3D Spawn(Vector3 position, float size, bool starter)
    {
        GameObject go = new GameObject(starter ? "Start_Asteroid" : "Asteroid");
        go.transform.position = position;
        Asteroid3D a = go.AddComponent<Asteroid3D>();
        a.IsStarter = starter;
        a.Size = size;
        a.Faction = Faction3D.Neutral;
        a.Radius = size * 0.55f;
        a._hp = 1; // любой астероид разрушается одним выстрелом
        return a;
    }

    void Start()
    {
        _model = Factory3D.BuildAsteroid(transform, Size).transform;
        _model.rotation = Random.rotation;
        _spin = Random.insideUnitSphere * 25f;
        _drift = IsStarter ? Vector3.zero : Random.insideUnitSphere * 1.5f;
    }

    void Update()
    {
        if (!_alive) return;
        _model.Rotate(_spin * Time.deltaTime, Space.World);
        transform.position += _drift * Time.deltaTime;

        Player3D p = Player3D.Instance;
        if (!IsStarter && p != null && p.IsAlive && Vector3.Distance(p.transform.position, transform.position) < Radius + p.Radius)
        {
            p.Damage();
            Break();
        }
    }

    public override bool OnHit(Faction3D from, int damage)
    {
        if (!_alive) return false;
        _hp -= damage;
        Factory3D.Explosion(transform.position, 0.3f, new Color(0.8f, 0.6f, 0.4f));
        if (_hp <= 0) Break();
        return true;
    }

    void Break()
    {
        _alive = false;
        Factory3D.Explosion(transform.position, Size * 0.4f, new Color(1f, 0.6f, 0.3f));
        if (Game3D.Instance != null)
        {
            Game3D.PlaySound(Game3D.Instance.ExplosionClip, 0.6f, 0.7f);
            // волны больше не зависят от астероида — они стартуют после 5 с полёта (Game3D)
        }
        Destroy(gameObject);
    }
}
