using System.Collections.Generic;
using UnityEngine;

// Щит как окраска корпуса: поверх мешей корабля кладётся светящаяся цветная "плёнка"
// точно по форме модели (копии мешей с аддитивным материалом). Цвет = сила щита,
// плёнка мягко пульсирует, при попадании вспыхивает.
public class HullTint3D : MonoBehaviour
{
    private readonly List<Renderer> _overlays = new List<Renderer>();
    private MaterialPropertyBlock _mpb;
    private Color _color = Color.cyan;
    private bool _active;
    private float _flashUntil;

    public void Init()
    {
        _mpb = new MaterialPropertyBlock();
        Material glow = Factory3D.GlowFlat(Color.white);

        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<Billboard3D>() != null) continue; // светящиеся точки/двигатели не трогаем
            MeshRenderer src = mf.GetComponent<MeshRenderer>();
            if (src == null) continue;

            GameObject o = new GameObject("Hull_Tint");
            o.transform.SetParent(mf.transform, false);
            o.transform.localScale = Vector3.one * 1.012f; // чуть больше корпуса, чтобы не мерцало
            o.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            MeshRenderer r = o.AddComponent<MeshRenderer>();
            Material[] mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = glow;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            _overlays.Add(r);
        }
    }

    // level: 0 — щита нет; 3 — голубой, 2 — жёлтый, 1 — красный.
    public void SetShield(int level)
    {
        if (level <= 0)
        {
            _active = false;
            SetEnabled(false);
            return;
        }
        _color = level >= 3 ? new Color(0.25f, 0.85f, 1f)
               : level == 2 ? new Color(1f, 0.8f, 0.15f)
               : new Color(1f, 0.2f, 0.15f);
        _active = true;
        SetEnabled(true);
    }

    public void SetColor(Color color)
    {
        _color = color;
        _active = true;
        SetEnabled(true);
    }

    public void Flash()
    {
        _flashUntil = Time.time + 0.15f;
        SetEnabled(true);
    }

    void SetEnabled(bool on)
    {
        foreach (Renderer r in _overlays) if (r != null) r.enabled = on;
    }

    void Update()
    {
        bool flashing = Time.time < _flashUntil;
        if (!_active && !flashing)
        {
            if (_overlays.Count > 0 && _overlays[0] != null && _overlays[0].enabled) SetEnabled(false);
            return;
        }

        float pulse = 0.32f + 0.12f * Mathf.Sin(Time.time * 5f);
        Color c = flashing ? Color.Lerp(_color, Color.white, 0.6f) : _color;
        c.a = flashing ? 0.9f : pulse;
        foreach (Renderer r in _overlays)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_TintColor", c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
