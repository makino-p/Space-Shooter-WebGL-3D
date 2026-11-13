using System.Collections.Generic;
using UnityEngine;

public enum Faction3D
{
    Player,
    Enemy,
    Pickup,
    Neutral // астероиды
}

// Всё, во что можно попасть. Попадания считаются геометрически (отрезок полёта снаряда vs сфера),
// поэтому быстрые снаряды не пролетают сквозь цели и физика Unity не нужна.
public abstract class Hittable3D : MonoBehaviour
{
    public static readonly List<Hittable3D> All = new List<Hittable3D>();

    public Faction3D Faction = Faction3D.Enemy;
    public float Radius = 2f;

    public virtual bool IsAlive { get { return true; } }

    protected virtual void OnEnable() { All.Add(this); }
    protected virtual void OnDisable() { All.Remove(this); }

    // Возвращает true, если снаряд нужно уничтожить.
    public abstract bool OnHit(Faction3D from, int damage);

    // Ближайшее пересечение отрезка a->b со сферой цели (с запасом radiusPad).
    public static Hittable3D SegmentHit(Vector3 a, Vector3 b, float radiusPad, Faction3D shooter, ICollection<Hittable3D> exclude = null)
    {
        Hittable3D best = null;
        float bestT = float.MaxValue;
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;

        for (int i = 0; i < All.Count; i++)
        {
            Hittable3D h = All[i];
            if (h == null || !h.IsAlive || !CanHit(shooter, h.Faction)) continue;
            if (exclude != null && exclude.Contains(h)) continue; // пробивающая пуля не бьёт одну цель дважды

            Vector3 c = h.transform.position;
            float t = len2 > 0.0001f ? Mathf.Clamp01(Vector3.Dot(c - a, ab) / len2) : 0f;
            Vector3 closest = a + ab * t;
            float r = h.Radius + radiusPad;
            if ((c - closest).sqrMagnitude <= r * r && t < bestT)
            {
                bestT = t;
                best = h;
            }
        }
        return best;
    }

    public static bool CanHit(Faction3D shooter, Faction3D target)
    {
        if (shooter == Faction3D.Player) return target == Faction3D.Enemy || target == Faction3D.Neutral;
        if (shooter == Faction3D.Enemy) return target == Faction3D.Player || target == Faction3D.Pickup;
        return false;
    }
}
