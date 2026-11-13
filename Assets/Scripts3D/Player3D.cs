using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Корабль игрока: свободный полёт в 3D.
// Мышь / стрелки — поворот, W/S — рычаг газа (вверх/вниз), X — полная остановка,
// A/D — стрейф, Q/E — вниз/вверх, Left Shift — форсаж (тратит заряд двигателей),
// Space / ЛКМ — огонь, C — притянуть пикапы.
public class Player3D : Hittable3D
{
    public static Player3D Instance;

    [Header("Flight (ручной газ)")]
    public float MaxSpeed = 38f;          // скорость при 100% газа
    public float ThrottleRate = 0.6f;     // как быстро W/S двигают рычаг газа (доля в секунду)
    public float Acceleration = 16f;      // разгон до скорости рычага
    public float Deceleration = 24f;      // торможение
    public float StrafeSpeed = 14f;
    public float StrafeResponse = 4f;     // плавность стрейфа

    [Header("Steering (плавность)")]
    public float MouseSensitivity = 1.6f;
    public float KeyTurnSpeed = 70f;
    public float RotationSmoothTime = 0.14f; // больше — плавнее и "тяжелее"
    public float ArenaRadius = 170f;         // используется, если нет туннеля

    [Header("Thrusters (Left Shift)")]
    public float BoostMultiplier = 1.8f;
    public float MaxThrusterCharge = 100f;
    public float ThrusterDrain = 35f;
    public float ThrusterRecharge = 25f;
    public float ThrusterRechargeDelay = 0.5f;
    public float ThrusterOverheatCooldown = 2f;

    [Header("Weapons")]
    public int MaxAmmo = 15;
    public float FireRate = 0.18f;
    public float ShotSpeed = 130f;
    public float ShotLifetime = 7f;       // дальность ~900 — астероид можно сбить с любой видимой дистанции
    public float AimAssistRange = 90f;    // на этой дистанции пули сами доворачивают к врагу
    public float AimAssistCone = 22f;     // насколько точно нужно целиться (градусы)
    public float PowerupDuration = 5f;

    [Header("Health")]
    public int MaxLives = 3;
    public int MaxShield = 3;

    public int Lives { get; private set; }
    public int Ammo { get; private set; }
    public int Shield { get; private set; }
    public float ThrusterCharge01 { get { return _thruster / MaxThrusterCharge; } }
    public bool ThrusterOverheated { get { return _overheated; } }
    public bool IsBoosting { get { return _boosting; } }
    public float CurrentSpeed { get { return _speed; } }
    public float Throttle01 { get { return _throttle; } }
    public Vector3 Velocity { get; private set; }

    private bool _alive = true;
    public override bool IsAlive { get { return _alive; } }

    private Transform _model;
    private float _yaw, _pitch, _roll;
    private float _speed;
    private float _canFire;
    private float _thruster;
    private bool _overheated;
    private bool _boosting;
    private float _rechargeAt;
    private float _invulnerableUntil;

    private bool _triple, _radial, _homing;
    private Coroutine _tripleRoutine, _radialRoutine, _homingRoutine;
    private float _speedMultiplier = 1f;
    private bool _speedBoost, _negative;

    private GameObject _shieldBubble;
    private Renderer _shieldRenderer;
    private readonly List<ParticleSystem> _damageSmoke = new List<ParticleSystem>();
    private ParticleSystem[] _trails;

    // носы двух передних пушек корабля (совпадают со спрайтом 2D-версии)
    private static Vector3[] GunPoints
    {
        get
        {
            return Factory3D.HasSpriteModels
                ? new[] { new Vector3(-0.72f, 0.1f, 2.9f), new Vector3(0.72f, 0.1f, 2.9f) }
                : new[] { new Vector3(-1.9f, -0.12f, 1.0f), new Vector3(1.9f, -0.12f, 1.0f) };
        }
    }
    private int _gunIndex;

    void Awake()
    {
        Instance = this;
        Faction = Faction3D.Player;
        Radius = 1.7f;
        Lives = MaxLives;
        Ammo = MaxAmmo;
        _thruster = MaxThrusterCharge;
        _speed = 0f;      // старт: корабль стоит на месте
        _throttle = 0f;

        _model = Factory3D.BuildPlayerShip(transform).transform;
        Vector3[] engines = Factory3D.PlayerEnginePoints;
        _trails = new ParticleSystem[engines.Length];
        for (int i = 0; i < engines.Length; i++)
        {
            _trails[i] = Factory3D.EngineTrail(_model, engines[i], new Color(1f, 0.6f, 0.25f, 0.9f), 0.6f);
        }

        _shieldBubble = Factory3D.Part(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 7.5f, Factory3D.GlowFlat(new Color(0.15f, 0.4f, 0.55f, 0.25f)));
        _shieldRenderer = _shieldBubble.GetComponent<Renderer>();
        _shieldBubble.SetActive(false);
    }

    void Start()
    {
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = 0f;
        RefreshUI();
    }

    void Update()
    {
        if (!_alive) return;

        HandleCursor();
        HandleRotation();
        HandleThrusters();
        HandleMovement();
        HandleShooting();
        UpdateVisuals();
    }

    // ---------------- Управление ----------------
    void HandleCursor()
    {
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    float _yawInput;
    float _targetYaw, _targetPitch;
    float _yawVelocity, _pitchVelocity;
    bool _rotationInitialized;

    // Ввод двигает "целевое" направление, а корабль плавно догоняет его (SmoothDamp) —
    // поэтому поворот и прицел движутся мягко, без рывков.
    void HandleRotation()
    {
        if (!_rotationInitialized)
        {
            _targetYaw = _yaw;
            _targetPitch = _pitch;
            _rotationInitialized = true;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            // ограничение резких скачков мыши (особенно в WebGL)
            float mx = Mathf.Clamp(Input.GetAxis("Mouse X"), -8f, 8f);
            float my = Mathf.Clamp(Input.GetAxis("Mouse Y"), -8f, 8f);
            _targetYaw += mx * MouseSensitivity;
            _targetPitch -= my * MouseSensitivity;
        }
        float keyYaw = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        float keyPitch = (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f);
        _targetYaw += keyYaw * KeyTurnSpeed * Time.deltaTime;
        _targetPitch += keyPitch * KeyTurnSpeed * Time.deltaTime;
        _targetPitch = Mathf.Clamp(_targetPitch, -75f, 75f);

        // не даём цели убегать слишком далеко вперёд от корабля
        _targetYaw = _yaw + Mathf.Clamp(Mathf.DeltaAngle(_yaw, _targetYaw), -60f, 60f);

        _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVelocity, RotationSmoothTime);
        _pitch = Mathf.SmoothDampAngle(_pitch, _targetPitch, ref _pitchVelocity, RotationSmoothTime);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        _yawInput = Mathf.Clamp(_yawVelocity / 120f, -1f, 1f);
    }

    void HandleThrusters()
    {
        bool wantBoost = Input.GetKey(KeyCode.LeftShift) && !_overheated && _thruster > 0f;
        _boosting = wantBoost;
        if (wantBoost)
        {
            _thruster -= ThrusterDrain * Time.deltaTime;
            _rechargeAt = Time.time + ThrusterRechargeDelay;
            if (_thruster <= 0f)
            {
                _thruster = 0f;
                _overheated = true;
                _boosting = false;
                _rechargeAt = Time.time + ThrusterOverheatCooldown;
            }
        }
        else if (Time.time >= _rechargeAt && _thruster < MaxThrusterCharge)
        {
            _thruster = Mathf.Min(MaxThrusterCharge, _thruster + ThrusterRecharge * Time.deltaTime);
            if (_thruster >= MaxThrusterCharge) _overheated = false;
        }
    }

    float _strafeInput;
    float _verticalInput;
    float _throttle; // 0..1, при старте 0 — корабль стоит на месте

    void HandleMovement()
    {
        // рычаг газа: W прибавляет, S убавляет, X — полный стоп. Без нажатий газ остаётся как есть.
        if (Input.GetKey(KeyCode.W)) _throttle += ThrottleRate * Time.deltaTime;
        if (Input.GetKey(KeyCode.S)) _throttle -= ThrottleRate * 1.3f * Time.deltaTime;
        if (Input.GetKeyDown(KeyCode.X)) _throttle = 0f;
        _throttle = Mathf.Clamp01(_throttle);

        // форсаж (Shift) работает как рывок вперёд даже с места
        float target = _throttle * MaxSpeed;
        if (_boosting) target = Mathf.Max(target, MaxSpeed * 0.6f) * BoostMultiplier;
        float rate = target > _speed ? Acceleration * (_boosting ? 2f : 1f) : Deceleration;
        _speed = Mathf.MoveTowards(_speed, target, rate * Time.deltaTime);

        float strafe = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float vertical = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
        _strafeInput = Mathf.MoveTowards(_strafeInput, strafe, StrafeResponse * Time.deltaTime);
        _verticalInput = Mathf.MoveTowards(_verticalInput, vertical, StrafeResponse * Time.deltaTime);

        Vector3 v = transform.forward * _speed * _speedMultiplier
                  + transform.right * _strafeInput * StrafeSpeed * _speedMultiplier
                  + transform.up * _verticalInput * StrafeSpeed * _speedMultiplier;
        Velocity = v;
        Vector3 pos = transform.position + v * Time.deltaTime;

        // граница арены: вокруг туннеля (из него можно вылетать, но не слишком далеко)
        bool clamped;
        if (Tunnel3D.Instance != null)
        {
            pos = Tunnel3D.Instance.ClampToArena(pos, out clamped);
        }
        else
        {
            clamped = pos.magnitude > ArenaRadius;
            if (clamped) pos = pos.normalized * ArenaRadius;
        }
        if (clamped && Game3D.Instance != null)
        {
            Game3D.Instance.UI.ShowMessage("RETURN TO THE TUNNEL", 0.6f, new Color(1f, 0.8f, 0.3f));
        }
        transform.position = pos;
    }

    void HandleShooting()
    {
        bool fire = Input.GetKey(KeyCode.Space) || (Input.GetMouseButton(0) && Cursor.lockState == CursorLockMode.Locked);
        if (!fire || Time.time < _canFire) return;
        _canFire = Time.time + FireRate;

        if (Ammo <= 0)
        {
            _canFire = Time.time + 0.35f;
            if (Game3D.Instance != null)
            {
                Game3D.Instance.UI.ShowOutOfAmmo();
                Game3D.PlaySound(Game3D.Instance.LaserClip, 0.35f, 2.2f); // короткий "пустой" щелчок
            }
            return;
        }

        Ammo--;
        Color shotColor = new Color(0.3f, 0.85f, 1f);
        Vector3 gun = transform.TransformPoint(GunPoints[_gunIndex]);
        _gunIndex = 1 - _gunIndex;
        Vector3 aim = AimDirection(gun);

        if (_homing)
        {
            foreach (Vector3 gp in GunPoints)
            {
                Vector3 p = transform.TransformPoint(gp);
                Projectile3D.Spawn(Faction3D.Player, p, AimDirection(p), 70f, new Color(1f, 0.35f, 1f), 4f, 1.3f, true);
            }
        }
        else if (_radial)
        {
            const int count = 30;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < count; i++)
            {
                float y = 1f - (i / (count - 1f)) * 2f;
                float r = Mathf.Sqrt(1f - y * y);
                float th = golden * i;
                Vector3 dir = transform.TransformDirection(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r));
                Projectile3D.Spawn(Faction3D.Player, transform.position + dir * 2.5f, dir, 90f, new Color(1f, 1f, 0.7f), 1.6f);
            }
        }
        else if (_triple)
        {
            for (int i = -1; i <= 1; i++)
            {
                Vector3 dir = Quaternion.AngleAxis(i * 4f, transform.up) * aim;
                Projectile3D.Spawn(Faction3D.Player, transform.position + transform.forward * 2f + transform.right * i * 1.5f, dir, ShotSpeed, new Color(1f, 0.9f, 0.3f), ShotLifetime).EnableAimAssist(AimAssistRange, AimAssistCone);
            }
        }
        else
        {
            Projectile3D.Spawn(Faction3D.Player, gun, aim, ShotSpeed, shotColor, ShotLifetime).EnableAimAssist(AimAssistRange, AimAssistCone);
        }

        if (Game3D.Instance != null)
        {
            Game3D.PlaySound(Game3D.Instance.LaserClip, 0.45f, Random.Range(0.95f, 1.1f));
            Game3D.Instance.UI.UpdateAmmo(Ammo, MaxAmmo);
        }
    }

    // Выстрелы сходятся в точке прицела перед кораблём.
    public Vector3 AimPoint { get { return transform.position + transform.forward * 90f; } }

    Vector3 AimDirection(Vector3 from)
    {
        return (AimPoint - from).normalized;
    }

    void UpdateVisuals()
    {
        // крен модели при повороте и стрейфе
        float targetRoll = -_yawInput * 40f - _strafeInput * 25f;
        _roll = Mathf.Lerp(_roll, targetRoll, Time.deltaTime * 5f);
        _model.localRotation = Quaternion.Euler(0f, 0f, _roll);

        if (_shieldBubble.activeSelf)
        {
            float pulse = 7.5f + Mathf.Sin(Time.time * 5f) * 0.15f;
            _shieldBubble.transform.localScale = Vector3.one * pulse;
        }

        foreach (ParticleSystem ps in _trails)
        {
            var main = ps.main;
            main.startSize = _boosting ? 1.1f : 0.6f;
            main.startSpeed = _boosting ? 14f : 6f;
        }

        if (Game3D.Instance != null)
        {
            Game3D.Instance.UI.UpdateThruster(ThrusterCharge01, _overheated);
            Game3D.Instance.UI.UpdateThrottle(_throttle, _speed * _speedMultiplier);
        }
    }

    // ---------------- Урон и здоровье ----------------
    public override bool OnHit(Faction3D from, int damage)
    {
        Damage();
        return true;
    }

    public void Damage()
    {
        if (!_alive || Time.time < _invulnerableUntil) return;
        _invulnerableUntil = Time.time + 0.5f;

        if (Shield > 0)
        {
            Shield--;
            UpdateShieldVisual();
            if (Game3D.Instance != null) Game3D.Instance.CameraRig.Shake(0.15f, 0.15f);
            return;
        }

        Lives--;
        if (Game3D.Instance != null)
        {
            Game3D.Instance.CameraRig.Shake(0.3f, 0.45f);
            Game3D.PlaySound(Game3D.Instance.ExplosionClip, 0.6f, 1.3f);
        }
        Factory3D.Explosion(transform.position + transform.forward * 1.5f, 0.6f, new Color(1f, 0.5f, 0.1f));

        if (Lives == 2) _damageSmoke.Add(Factory3D.Smoke(_model, new Vector3(0.9f, 0.1f, -0.6f)));
        if (Lives == 1) _damageSmoke.Add(Factory3D.Smoke(_model, new Vector3(-0.9f, 0.1f, -0.6f)));

        RefreshUI();

        if (Lives <= 0)
        {
            Lives = 0;
            Die();
        }
    }

    void Die()
    {
        _alive = false;
        Factory3D.Explosion(transform.position, 2.5f, new Color(1f, 0.6f, 0.2f));
        if (Game3D.Instance != null)
        {
            Game3D.PlaySound(Game3D.Instance.ExplosionClip, 1f, 0.8f);
            Game3D.Instance.CameraRig.Shake(0.6f, 0.8f);
            Game3D.Instance.OnPlayerDeath();
        }
        _model.gameObject.SetActive(false);
        _shieldBubble.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Heal()
    {
        if (Lives >= MaxLives) return;
        Lives++;
        if (_damageSmoke.Count > 0)
        {
            ParticleSystem last = _damageSmoke[_damageSmoke.Count - 1];
            _damageSmoke.RemoveAt(_damageSmoke.Count - 1);
            if (last != null) Destroy(last.gameObject);
        }
        RefreshUI();
    }

    void UpdateShieldVisual()
    {
        _shieldBubble.SetActive(Shield > 0);
        Color c = Shield >= 3 ? new Color(0.3f, 0.8f, 1f, 0.35f)
                : Shield == 2 ? new Color(1f, 0.85f, 0.2f, 0.35f)
                : new Color(1f, 0.2f, 0.2f, 0.4f);
        _shieldRenderer.sharedMaterial = Factory3D.GlowFlat(c * 0.5f);
        if (Game3D.Instance != null) Game3D.Instance.UI.UpdateShield(Shield, MaxShield);
    }

    void RefreshUI()
    {
        if (Game3D.Instance == null) return;
        Game3D.Instance.UI.UpdateLives(Lives, MaxLives);
        Game3D.Instance.UI.UpdateAmmo(Ammo, MaxAmmo);
        Game3D.Instance.UI.UpdateShield(Shield, MaxShield);
    }

    // ---------------- Паверапы ----------------
    public void ApplyPickup(PickupType type)
    {
        UI3D ui = Game3D.Instance != null ? Game3D.Instance.UI : null;
        switch (type)
        {
            case PickupType.TripleShot:
                RestartTimer(ref _tripleRoutine, TimedFlag(v => _triple = v, PowerupDuration));
                if (ui != null) ui.ShowMessage("TRIPLE SHOT", 1.2f, Factory3D.PickupColor(type));
                break;
            case PickupType.Speed:
                if (!_speedBoost) StartCoroutine(SpeedRoutine(1.5f, PowerupDuration, false));
                if (ui != null) ui.ShowMessage("SPEED BOOST", 1.2f, Factory3D.PickupColor(type));
                break;
            case PickupType.Shield:
                Shield = MaxShield;
                UpdateShieldVisual();
                if (ui != null) ui.ShowMessage("SHIELD 3/3", 1.2f, Factory3D.PickupColor(type));
                break;
            case PickupType.Ammo:
                Ammo = MaxAmmo;
                if (ui != null) { ui.UpdateAmmo(Ammo, MaxAmmo); ui.ShowMessage("AMMO REFILLED", 1.2f, Factory3D.PickupColor(type)); }
                break;
            case PickupType.Health:
                Heal();
                if (ui != null) ui.ShowMessage("+1 LIFE", 1.2f, Factory3D.PickupColor(type));
                break;
            case PickupType.Radial:
                RestartTimer(ref _radialRoutine, TimedFlag(v => _radial = v, PowerupDuration));
                if (ui != null) ui.ShowMessage("NOVA SHOT", 1.2f, Factory3D.PickupColor(type));
                break;
            case PickupType.Homing:
                RestartTimer(ref _homingRoutine, TimedFlag(v => _homing = v, PowerupDuration));
                if (ui != null) ui.ShowMessage("HOMING MISSILES", 1.2f, Factory3D.PickupColor(type));
                break;
            case PickupType.Negative:
                Ammo = Mathf.Max(0, Ammo - 3);
                if (ui != null) { ui.UpdateAmmo(Ammo, MaxAmmo); ui.ShowMessage("ENGINE MALFUNCTION", 1.5f, Factory3D.PickupColor(type)); }
                if (!_negative) StartCoroutine(SpeedRoutine(0.5f, 4f, true));
                if (Game3D.Instance != null) Game3D.Instance.CameraRig.Shake(0.4f, 0.25f);
                break;
        }
    }

    void RestartTimer(ref Coroutine routine, IEnumerator work)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(work);
    }

    IEnumerator TimedFlag(System.Action<bool> set, float duration)
    {
        set(true);
        yield return new WaitForSeconds(duration);
        set(false);
    }

    IEnumerator SpeedRoutine(float multiplier, float duration, bool negative)
    {
        if (negative) _negative = true; else _speedBoost = true;
        _speedMultiplier *= multiplier;
        yield return new WaitForSeconds(duration);
        _speedMultiplier /= multiplier;
        if (negative) _negative = false; else _speedBoost = false;
    }

    public bool IsMalfunctioning { get { return _negative; } }
}
