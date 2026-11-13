using System.Collections.Generic;
using UnityEngine;

public enum PickupType
{
    TripleShot,
    Speed,
    Shield,
    Ammo,
    Health,
    Radial,   // редкий: выстрел во все стороны
    Negative, // негативный: сбой двигателей
    Homing    // редкий: самонаводящиеся ракеты
}

public class Pickup3D : Hittable3D
{
    public static readonly List<Pickup3D> Pickups = new List<Pickup3D>();

    public PickupType Type;

    private Transform _model;
    private Vector3 _drift;
    private float _lifetime = 30f;
    private bool _collected;
    private const float CollectDistance = 4.5f;
    private const float MagnetSpeed = 55f;
    private const float MagnetRange = 150f;

    public static Pickup3D Spawn(PickupType type, Vector3 position)
    {
        GameObject go = new GameObject("Pickup_" + type);
        go.transform.position = position;
        Pickup3D p = go.AddComponent<Pickup3D>();
        p.Type = type;
        p.Faction = Faction3D.Pickup;
        p.Radius = 2f;
        return p;
    }

    protected override void OnEnable() { base.OnEnable(); Pickups.Add(this); }
    protected override void OnDisable() { base.OnDisable(); Pickups.Remove(this); }

    void Start()
    {
        _model = Factory3D.BuildPickup(transform, Type).transform;
        _drift = Random.insideUnitSphere * 2f;
    }

    void Update()
    {
        if (_collected) return;

        _model.Rotate(40f * Time.deltaTime, 90f * Time.deltaTime, 0f, Space.Self);

        Player3D player = Player3D.Instance;
        Vector3 pos = transform.position;
        if (player != null && player.IsAlive)
        {
            Vector3 toPlayer = player.transform.position - pos;
            float dist = toPlayer.magnitude;

            // C — пикапы быстро летят к игроку
            if (Input.GetKey(KeyCode.C) && dist < MagnetRange)
            {
                pos += toPlayer.normalized * Mathf.Min(MagnetSpeed * Time.deltaTime, dist);
            }
            else
            {
                pos += _drift * Time.deltaTime;
            }

            if (dist < CollectDistance)
            {
                Collect(player);
                return;
            }
        }
        transform.position = pos;

        _lifetime -= Time.deltaTime;
        if (_lifetime < 5f)
        {
            _model.gameObject.SetActive(Mathf.FloorToInt(Time.time * 6f) % 2 == 0);
        }
        if (_lifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }

    void Collect(Player3D player)
    {
        _collected = true;
        player.ApplyPickup(Type);
        if (Game3D.Instance != null) Game3D.PlaySound(Game3D.Instance.PowerupClip, 0.8f, Type == PickupType.Negative ? 0.6f : 1f);
        Factory3D.Explosion(transform.position, 0.4f, Factory3D.PickupColor(Type));
        Destroy(gameObject);
    }

    // Пикап сбит выстрелом врага.
    public override bool OnHit(Faction3D from, int damage)
    {
        if (_collected) return false;
        _collected = true;
        Factory3D.Explosion(transform.position, 0.6f, Factory3D.PickupColor(Type));
        Destroy(gameObject);
        return true;
    }
}
