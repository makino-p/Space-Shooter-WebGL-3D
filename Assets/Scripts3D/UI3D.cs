using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Весь интерфейс 3D-версии, создаётся кодом.
public class UI3D : MonoBehaviour
{
    private Canvas _canvas;
    private Font _font;

    private Text _score, _lives, _wave, _ammo, _shield, _message, _outOfAmmo, _gameOver, _hint;
    private RectTransform _thrusterFill;
    private Image _thrusterFillImage;
    private Text _thrusterLabel;
    private GameObject _bossBar;
    private RectTransform _bossFill;
    private RectTransform _crosshair;
    private bool _crosshairPlaced;
    private RectTransform _throttleFill;
    private Text _throttleLabel;
    private Coroutine _messageRoutine, _ammoRoutine;

    private readonly List<Image> _markers = new List<Image>();
    private const int MaxMarkers = 48;

    void Awake()
    {
        _font = Compat.DefaultFont;

        GameObject canvasGo = new GameObject("UI_Canvas");
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        Transform root = canvasGo.transform;

        _score = MakeText(root, "Score", new Vector2(0, 1), new Vector2(20, -20), 26, TextAnchor.UpperLeft);
        _lives = MakeText(root, "Lives", new Vector2(0, 1), new Vector2(20, -52), 22, TextAnchor.UpperLeft);
        _wave = MakeText(root, "Wave", new Vector2(0, 1), new Vector2(20, -80), 20, TextAnchor.UpperLeft);
        _ammo = MakeText(root, "Ammo", new Vector2(1, 1), new Vector2(-20, -20), 26, TextAnchor.UpperRight);
        _shield = MakeText(root, "Shield", new Vector2(1, 1), new Vector2(-20, -52), 22, TextAnchor.UpperRight);
        _message = MakeText(root, "Message", new Vector2(0.5f, 0.5f), new Vector2(0, 140), 40, TextAnchor.MiddleCenter);
        _outOfAmmo = MakeText(root, "OutOfAmmo", new Vector2(0.5f, 0.5f), new Vector2(0, -90), 30, TextAnchor.MiddleCenter);
        _outOfAmmo.text = "OUT OF AMMO!";
        _outOfAmmo.color = new Color(1f, 0.3f, 0.3f);
        _outOfAmmo.gameObject.SetActive(false);
        _gameOver = MakeText(root, "GameOver", new Vector2(0.5f, 0.5f), new Vector2(0, 30), 56, TextAnchor.MiddleCenter);
        _gameOver.gameObject.SetActive(false);
        _hint = MakeText(root, "Hint", new Vector2(0.5f, 0f), new Vector2(0, 70), 17, TextAnchor.LowerCenter);
        _hint.color = new Color(1f, 1f, 1f, 0.8f);
        _message.gameObject.SetActive(false);

        BuildThrusterBar(root);
        BuildThrottleBar(root);

        // благодарности авторам ассетов (CC BY 4.0 требует указать авторство)
        Text credits = MakeText(root, "Credits", new Vector2(1f, 0f), new Vector2(-12f, 8f), 11, TextAnchor.LowerRight);
        credits.text = "Planet textures: Solar System Scope (CC BY 4.0)  |  Star map: NASA/GSFC SVS  |  Rocks: Poly Haven (CC0)";
        credits.color = new Color(1f, 1f, 1f, 0.45f);
        BuildCrosshair(root);
    }

    Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 pos, int size, TextAnchor align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        RectTransform r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = anchor;
        r.pivot = anchor;
        r.anchoredPosition = pos;
        r.sizeDelta = new Vector2(900f, size * 1.6f);
        Text t = go.GetComponent<Text>();
        t.font = _font;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        return t;
    }

    Image MakeImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    void BuildThrusterBar(Transform root)
    {
        Image bg = MakeImage(root, "Thruster_Bar", new Color(0f, 0f, 0f, 0.6f));
        RectTransform r = bg.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
        r.anchoredPosition = new Vector2(20f, 24f);
        r.sizeDelta = new Vector2(260f, 18f);

        _thrusterFillImage = MakeImage(r, "Fill", new Color(0.2f, 0.9f, 1f));
        _thrusterFill = _thrusterFillImage.rectTransform;
        _thrusterFill.anchorMin = Vector2.zero;
        _thrusterFill.anchorMax = Vector2.one;
        _thrusterFill.offsetMin = new Vector2(2f, 2f);
        _thrusterFill.offsetMax = new Vector2(-2f, -2f);

        _thrusterLabel = MakeText(r, "Label", new Vector2(0f, 1f), new Vector2(0f, 22f), 15, TextAnchor.LowerLeft);
        _thrusterLabel.text = "THRUSTER [SHIFT]";
    }

    // Вертикальный индикатор рычага газа слева внизу (над полоской двигателей).
    void BuildThrottleBar(Transform root)
    {
        Image bg = MakeImage(root, "Throttle_Bar", new Color(0f, 0f, 0f, 0.6f));
        RectTransform r = bg.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = Vector2.zero;
        r.anchoredPosition = new Vector2(20f, 80f);
        r.sizeDelta = new Vector2(22f, 150f);

        Image fill = MakeImage(r, "Fill", new Color(0.4f, 1f, 0.5f));
        _throttleFill = fill.rectTransform;
        _throttleFill.anchorMin = Vector2.zero;
        _throttleFill.anchorMax = new Vector2(1f, 0f);
        _throttleFill.offsetMin = new Vector2(2f, 2f);
        _throttleFill.offsetMax = new Vector2(-2f, -2f);

        _throttleLabel = MakeText(r, "Label", new Vector2(0f, 0f), new Vector2(32f, 0f), 15, TextAnchor.LowerLeft);
    }

    public void UpdateThrottle(float throttle01, float speed)
    {
        if (_throttleFill == null) return;
        throttle01 = Mathf.Clamp01(throttle01);
        _throttleFill.anchorMax = new Vector2(1f, throttle01);
        _throttleLabel.text = "THROTTLE " + Mathf.RoundToInt(throttle01 * 100f) + "%\nSPEED " + Mathf.RoundToInt(speed) +
                              (throttle01 <= 0.001f && speed < 0.5f && Time.timeSinceLevelLoad < 2f ? "\n<color=#ffcc55>W - go</color>" : "");
        _throttleLabel.supportRichText = true;
    }

    void BuildCrosshair(Transform root)
    {
        GameObject go = new GameObject("Crosshair", typeof(RectTransform));
        go.transform.SetParent(root, false);
        _crosshair = go.GetComponent<RectTransform>();
        _crosshair.sizeDelta = new Vector2(40f, 40f);
        Color c = new Color(0.6f, 1f, 1f, 0.85f);
        Vector2[] offsets = { new Vector2(-12, 0), new Vector2(12, 0), new Vector2(0, -12), new Vector2(0, 12) };
        Vector2[] sizes = { new Vector2(12, 2), new Vector2(12, 2), new Vector2(2, 12), new Vector2(2, 12) };
        for (int i = 0; i < 4; i++)
        {
            Image img = MakeImage(_crosshair, "Line", c);
            img.rectTransform.anchoredPosition = offsets[i];
            img.rectTransform.sizeDelta = sizes[i];
        }
        Image dot = MakeImage(_crosshair, "Dot", c);
        dot.rectTransform.sizeDelta = new Vector2(3, 3);
    }

    // ---------------- Обновления HUD ----------------
    public void UpdateScore(int score) { _score.text = "SCORE  " + score; }

    public void UpdateLives(int lives, int max)
    {
        string s = "LIVES  ";
        for (int i = 0; i < max; i++) s += i < lives ? "[#] " : "[  ] ";
        _lives.text = s;
        _lives.color = lives <= 1 ? new Color(1f, 0.4f, 0.35f) : Color.white;
    }

    public void SetWave(int wave, int total)
    {
        _wave.text = wave <= 0 ? "" : "WAVE  " + wave + " / " + total;
    }

    public void UpdateAmmo(int ammo, int max)
    {
        _ammo.text = "AMMO  " + ammo + " / " + max;
        _ammo.color = ammo == 0 ? new Color(1f, 0.3f, 0.3f) : (ammo <= 4 ? new Color(1f, 0.8f, 0.3f) : Color.white);
    }

    public void UpdateShield(int shield, int max)
    {
        _shield.gameObject.SetActive(shield > 0);
        _shield.text = "SHIELD  " + shield + " / " + max;
        _shield.color = shield >= 3 ? new Color(0.4f, 0.9f, 1f) : (shield == 2 ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.35f, 0.3f));
    }

    public void UpdateThruster(float charge01, bool overheated)
    {
        charge01 = Mathf.Clamp01(charge01);
        _thrusterFill.anchorMax = new Vector2(charge01, 1f);
        if (overheated)
        {
            bool blink = Mathf.FloorToInt(Time.time * 6f) % 2 == 0;
            _thrusterFillImage.color = new Color(1f, 0.25f, 0.2f);
            _thrusterLabel.text = "THRUSTER OVERHEAT";
            _thrusterLabel.color = blink ? new Color(1f, 0.3f, 0.3f) : Color.white;
        }
        else
        {
            _thrusterFillImage.color = charge01 < 0.3f ? new Color(1f, 0.85f, 0.2f) : new Color(0.2f, 0.9f, 1f);
            _thrusterLabel.text = "THRUSTER [SHIFT]";
            _thrusterLabel.color = Color.white;
        }
    }

    public void ShowMessage(string text, float duration, Color color)
    {
        if (_messageRoutine != null) StopCoroutine(_messageRoutine);
        _message.text = text;
        _message.color = color;
        _message.gameObject.SetActive(true);
        if (duration > 0f) _messageRoutine = StartCoroutine(HideAfter(_message.gameObject, duration));
    }

    public void ShowOutOfAmmo()
    {
        if (_ammoRoutine != null) StopCoroutine(_ammoRoutine);
        _outOfAmmo.gameObject.SetActive(true);
        _ammoRoutine = StartCoroutine(HideAfter(_outOfAmmo.gameObject, 0.8f));
    }

    IEnumerator HideAfter(GameObject go, float t)
    {
        yield return new WaitForSeconds(t);
        go.SetActive(false);
    }

    public void ShowControlsHint()
    {
        _hint.text = "MOUSE / ARROWS - steer    W/S - throttle up/down    X - full stop    A/D - strafe    Q/E - down/up\n" +
                     "SPACE / LMB - fire    RMB (hold) - spin    SHIFT - boost    C - pull pickups    click to capture mouse, ESC to release\n" +
                     "Follow the energy beam - enemies come along it after 5 seconds of flight";
        StartCoroutine(HideAfter(_hint.gameObject, 2f));
    }

    public void ShowGameOver(bool win)
    {
        _gameOver.gameObject.SetActive(true);
        _gameOver.text = win ? "YOU WIN!\n<size=24>R - play again</size>"
                             : "GAME OVER\n<size=24>R - restart</size>";
        _gameOver.supportRichText = true;
        _gameOver.color = win ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.3f, 0.3f);
        if (!win) StartCoroutine(FlickerGameOver());
        _crosshair.gameObject.SetActive(false);
    }

    IEnumerator FlickerGameOver()
    {
        while (true)
        {
            _gameOver.enabled = true;
            yield return new WaitForSeconds(0.6f);
            _gameOver.enabled = false;
            yield return new WaitForSeconds(0.3f);
        }
    }

    public void ShowBossHealth(float health01)
    {
        if (_bossBar == null)
        {
            Image bg = MakeImage(_canvas.transform, "Boss_Bar", new Color(0f, 0f, 0f, 0.65f));
            _bossBar = bg.gameObject;
            RectTransform r = bg.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -24f);
            r.sizeDelta = new Vector2(520f, 22f);

            Image fill = MakeImage(r, "Fill", new Color(1f, 0.25f, 0.2f));
            _bossFill = fill.rectTransform;
            _bossFill.anchorMin = Vector2.zero;
            _bossFill.anchorMax = Vector2.one;
            _bossFill.offsetMin = new Vector2(2f, 2f);
            _bossFill.offsetMax = new Vector2(-2f, -2f);

            Text label = MakeText(r, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, 15, TextAnchor.MiddleCenter);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.text = "MOTHERSHIP";
        }
        _bossBar.SetActive(true);
        _bossFill.anchorMax = new Vector2(Mathf.Clamp01(health01), 1f);
    }

    public void HideBossHealth()
    {
        if (_bossBar != null) _bossBar.SetActive(false);
    }

    // ---------------- Прицел и маркеры целей ----------------
    void LateUpdate()
    {
        Camera cam = Camera.main;
        Player3D player = Player3D.Instance;
        if (cam == null) return;

        if (player != null && player.IsAlive)
        {
            Vector3 sp = cam.WorldToScreenPoint(player.AimPoint);
            _crosshair.gameObject.SetActive(sp.z > 0f);
            // прицел плавно догоняет точку наведения
            Vector3 target = new Vector3(sp.x, sp.y, 0f);
            if (!_crosshairPlaced)
            {
                _crosshair.position = target;
                _crosshairPlaced = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
                _crosshair.position = Vector3.Lerp(_crosshair.position, target, k);
            }
        }

        UpdateMarkers(cam);
    }

    // Стрелки навигации: показываются только для целей ВНЕ поля зрения,
    // стоят у края экрана и указывают, куда повернуть. На видимых врагах ничего не рисуется.
    void UpdateMarkers(Camera cam)
    {
        int used = 0;
        float margin = 46f;
        Vector2 screen = new Vector2(Screen.width, Screen.height);
        Vector2 center = screen * 0.5f;
        float uiScale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
        Vector3 camPos = cam.transform.position;

        foreach (Hittable3D h in Hittable3D.All)
        {
            if (used >= MaxMarkers) break;
            if (h == null || !h.IsAlive) continue;
            bool isEnemy = h.Faction == Faction3D.Enemy;
            bool isPickup = h.Faction == Faction3D.Pickup;
            if (h is Transport3D) continue; // транспортники мирные и их много — стрелки не нужны
            if (!isEnemy && !isPickup) continue;

            Vector3 sp = cam.WorldToScreenPoint(h.transform.position);
            bool behind = sp.z < 0f;
            bool onScreen = !behind && sp.x > 0f && sp.x < screen.x && sp.y > 0f && sp.y < screen.y;
            if (onScreen) continue; // видим — стрелка не нужна

            float worldDist = Vector3.Distance(camPos, h.transform.position);
            if (isPickup && worldDist > 220f) continue; // далёкие пикапы не засоряют экран

            // направление на цель в плоскости экрана (для целей позади — зеркально)
            Vector2 d = new Vector2(sp.x, sp.y) - center;
            if (behind) d = -d;
            if (d.sqrMagnitude < 0.01f) d = Vector2.down;
            float sx = (center.x - margin) / Mathf.Max(Mathf.Abs(d.x), 0.001f);
            float sy = (center.y - margin) / Mathf.Max(Mathf.Abs(d.y), 0.001f);
            Vector2 pos = center + d * Mathf.Min(sx, sy);

            Image arrow = GetMarker(used++);
            Color c;
            bool isBoss = h is Boss3D;
            if (isEnemy) c = isBoss ? new Color(1f, 0.15f, 0.1f) : new Color(1f, 0.35f, 0.25f);
            else
            {
                Pickup3D p = h as Pickup3D;
                c = p != null ? Factory3D.PickupColor(p.Type) : Color.white;
            }
            // ближе — ярче и крупнее, плюс лёгкая пульсация
            float near = Mathf.Clamp01(1f - worldDist / 300f);
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 8f + used);
            c.a = 0.55f + 0.45f * near;
            arrow.color = c;

            float size = (isEnemy ? 26f : 18f) * (0.8f + 0.5f * near) * pulse * uiScale;
            if (isBoss) size *= 1.8f;
            arrow.rectTransform.sizeDelta = new Vector2(size, size) / uiScale;
            arrow.rectTransform.position = new Vector3(pos.x, pos.y, 0f);
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f; // спрайт стрелки смотрит вверх
            arrow.rectTransform.rotation = Quaternion.Euler(0f, 0f, angle);
            arrow.gameObject.SetActive(true);
        }

        for (int i = used; i < _markers.Count; i++)
        {
            _markers[i].gameObject.SetActive(false);
        }
    }

    Image GetMarker(int index)
    {
        while (_markers.Count <= index)
        {
            Image img = MakeImage(_canvas.transform, "Nav_Arrow", Color.white);
            img.sprite = ArrowSprite;
            img.transform.SetAsFirstSibling();
            Outline o = img.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.7f);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            _markers.Add(img);
        }
        return _markers[index];
    }

    private static Sprite _arrowSprite;

    // Стрелка-наконечник (смотрит вверх), рисуется кодом.
    static Sprite ArrowSprite
    {
        get
        {
            if (_arrowSprite != null) return _arrowSprite;
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f;   // -0.5..0.5
                    float v = (y + 0.5f) / size;          // 0..1 снизу вверх
                    // наконечник: ширина сужается к верху, снизу вырез-"ласточкин хвост"
                    float half = 0.46f * (1f - v);
                    float notch = 0.28f - Mathf.Abs(u) * 0.9f;
                    bool inside = Mathf.Abs(u) <= half && v >= Mathf.Max(0.04f, notch);
                    float edge = Mathf.Clamp01((half - Mathf.Abs(u)) * size * 0.8f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, inside ? edge : 0f));
                }
            }
            tex.Apply();
            _arrowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _arrowSprite;
        }
    }
}
