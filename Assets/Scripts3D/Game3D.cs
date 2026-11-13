using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Главный объект 3D-сцены. Строит весь мир кодом: свет, камеру, звёзды, игрока, астероиды и UI,
// управляет волнами врагов, боссом, спавном пикапов, очками и концом игры.
public class Game3D : MonoBehaviour
{
    public static Game3D Instance;

    [Header("Assets (назначаются автоматически скриптом Setup3DScene)")]
    public AudioClip LaserClip;
    public AudioClip ExplosionClip;
    public AudioClip PowerupClip;
    public AudioClip MusicClip;
    public Material LitMaterial;
    public Material GlowMaterial;

    [Header("Models from 2D sprites (генерируются скриптом Setup3DScene)")]
    public Mesh PlayerShipMesh;
    public Mesh EnemyShipMesh;
    public Mesh AsteroidMesh;
    public Material PlayerShipMaterial;
    public Material EnemyShipMaterial;
    public Material AsteroidMaterial;

    [Header("Downloaded assets (NASA sky, Poly Haven rocks)")]
    public Material SkyboxMaterial;
    public Mesh[] RockMeshes;
    public Material[] RockMaterials;

    [Header("Waves")]
    public int TotalWaves = 5;          // последняя волна заканчивается боссом
    public int BaseEnemiesPerWave = 4;
    public int ExtraEnemiesPerWave = 3; // каждая следующая волна больше на 3 врага
    public float TimeBetweenWaves = 4f;

    [Header("Pickup weights (больше = чаще)")]
    public int WeightAmmo = 35;      // частый
    public int WeightTripleShot = 12;
    public int WeightSpeed = 10;
    public int WeightShield = 10;
    public int WeightNegative = 10;
    public int WeightHealth = 5;     // редкий
    public int WeightRadial = 4;     // редкий
    public int WeightHoming = 4;     // редкий
    public float MinPickupDelay = 5f;
    public float MaxPickupDelay = 8f;

    public UI3D UI { get; private set; }
    public CameraRig3D CameraRig { get; private set; }
    public int Score { get; private set; }
    public bool IsGameOver { get; private set; }

    private Player3D _player;
    private bool _wavesStarted;
    private bool _stopSpawning;
    private int _currentWave;

    private static AudioSource[] _sfx;
    private static int _sfxIndex;

    void Awake()
    {
        Instance = this;
        Factory3D.LitBase = LitMaterial;
        Factory3D.GlowBase = GlowMaterial;
        Factory3D.SetSpriteModels(PlayerShipMesh, PlayerShipMaterial, EnemyShipMesh, EnemyShipMaterial, AsteroidMesh, AsteroidMaterial);
        Factory3D.SetRockModels(RockMeshes, RockMaterials);
        Hittable3D.All.Clear();
        Projectile3D.Active.Clear();
        Pickup3D.Pickups.Clear();

        BuildEnvironment();
        BuildAudio();
        Tunnel3D tunnel = Tunnel3D.Create();

        UI = gameObject.AddComponent<UI3D>();

        // игрок стоит у входа в туннель, носом к вратам врагов
        GameObject playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(0f, 0f, tunnel.StartZ + 30f);
        playerGo.transform.rotation = Quaternion.identity;
        _player = playerGo.AddComponent<Player3D>();

        CameraRig.SetTarget(playerGo.transform);

        // стартовый астероид прямо по курсу — уничтожь его, чтобы начать
        Asteroid3D.Spawn(new Vector3(0f, 0f, tunnel.StartZ + 75f), 6f, true);
        SpawnAsteroidField(45);
    }

    void Start()
    {
        UI.UpdateScore(0);
        UI.SetWave(0, TotalWaves);
        UI.ShowMessage("DESTROY THE ASTEROID TO BEGIN", 4f, Color.white);
        UI.ShowControlsHint();
    }

    // ---------------- Мир ----------------
    void BuildEnvironment()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.18f, 0.2f, 0.28f);
        RenderSettings.fog = false;

        // камера
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 2000f;
        cam.fieldOfView = 65f;
        camGo.AddComponent<AudioListener>();
        CameraRig = camGo.AddComponent<CameraRig3D>();

        // солнце и заполняющий свет
        GameObject sun = new GameObject("Sun");
        Light l = sun.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = new Color(1f, 0.93f, 0.85f);
        l.intensity = 1.25f;
        l.shadows = LightShadows.None;
        sun.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

        GameObject fill = new GameObject("Fill_Light");
        Light f = fill.AddComponent<Light>();
        f.type = LightType.Directional;
        f.color = new Color(0.35f, 0.45f, 1f);
        f.intensity = 0.45f;
        f.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(-30f, 140f, 0f);

        // настоящее звёздное небо NASA, если ассет скачан
        if (SkyboxMaterial != null)
        {
            RenderSettings.skybox = SkyboxMaterial;
            cam.clearFlags = CameraClearFlags.Skybox;
            // немного близких звёзд-частиц для ощущения движения
            Factory3D.Starfield(500, 250f, 500f);
        }
        else
        {
            Factory3D.Starfield(1800, 600f, 900f);
        }

        // далёкая планета и туманности для ориентации в пространстве
        GameObject planetRoot = new GameObject("Planet");
        planetRoot.transform.position = new Vector3(-380f, -120f, 520f);
        Factory3D.Part(PrimitiveType.Sphere, planetRoot.transform, Vector3.zero, Vector3.one * 260f,
                       Factory3D.Lit(new Color(0.25f, 0.4f, 0.75f), 0.05f, 0f, 0.2f));
        Factory3D.GlowBall(planetRoot.transform, Vector3.zero, 420f, new Color(0.2f, 0.4f, 1f, 0.35f));

        if (SkyboxMaterial == null)
        {
            Factory3D.GlowBall(null, new Vector3(500f, 200f, -300f), 700f, new Color(0.6f, 0.15f, 0.5f, 0.25f));
            Factory3D.GlowBall(null, new Vector3(-200f, 400f, -500f), 600f, new Color(0.15f, 0.35f, 0.6f, 0.2f));
            Factory3D.GlowBall(null, new Vector3(300f, -350f, 400f), 500f, new Color(0.7f, 0.35f, 0.1f, 0.18f));
        }
    }

    void SpawnAsteroidField(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 pos;
            int guard = 0;
            Tunnel3D tunnel = Tunnel3D.Instance;
            do
            {
                if (tunnel != null)
                {
                    // пояс астероидов вокруг туннеля, внутри туннеля чисто
                    float z = Random.Range(tunnel.StartZ, tunnel.EndZ);
                    Vector2 dir = Random.insideUnitCircle.normalized;
                    float r = Random.Range(tunnel.Radius + 12f, tunnel.Radius + 140f);
                    pos = new Vector3(dir.x * r, dir.y * r, z);
                }
                else
                {
                    pos = Random.insideUnitSphere * 160f;
                }
                guard++;
            }
            // не ставить на линию старта
            while (guard < 20 && tunnel == null && (pos.magnitude < 30f || (Mathf.Abs(pos.x) < 15f && Mathf.Abs(pos.y) < 15f && pos.z < -20f)));
            Asteroid3D.Spawn(pos, Random.Range(3f, 11f), false);
        }
    }

    void BuildAudio()
    {
        _sfx = new AudioSource[10];
        for (int i = 0; i < _sfx.Length; i++)
        {
            _sfx[i] = gameObject.AddComponent<AudioSource>();
            _sfx[i].playOnAwake = false;
        }

        if (MusicClip != null)
        {
            AudioSource music = gameObject.AddComponent<AudioSource>();
            music.clip = MusicClip;
            music.loop = true;
            music.volume = 0.35f;
            music.Play();
        }
    }

    public static void PlaySound(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null || _sfx == null) return;
        AudioSource s = _sfx[_sfxIndex];
        _sfxIndex = (_sfxIndex + 1) % _sfx.Length;
        if (s == null) return;
        s.pitch = pitch;
        s.PlayOneShot(clip, volume);
    }

    // ---------------- Очки и конец игры ----------------
    public void AddScore(int points)
    {
        Score += points;
        UI.UpdateScore(Score);
    }

    public void OnPlayerDeath()
    {
        IsGameOver = true;
        _stopSpawning = true;
        UI.ShowGameOver(false);
    }

    public void OnBossDefeated()
    {
        _stopSpawning = true;
        IsGameOver = true;
        UI.ShowGameOver(true);
    }

    void Update()
    {
        if (IsGameOver)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }
    }

    // ---------------- Волны ----------------
    public void StartWaves()
    {
        if (_wavesStarted) return;
        _wavesStarted = true;
        StartCoroutine(WaveRoutine());
        StartCoroutine(PickupRoutine());
    }

    int AliveEnemies()
    {
        int n = 0;
        foreach (Hittable3D h in Hittable3D.All)
        {
            if (h != null && h.IsAlive && h.Faction == Faction3D.Enemy) n++;
        }
        return n;
    }

    IEnumerator WaveRoutine()
    {
        yield return new WaitForSeconds(1.5f);

        for (int wave = 1; wave <= TotalWaves && !_stopSpawning; wave++)
        {
            _currentWave = wave;
            bool isFinal = wave == TotalWaves;
            UI.SetWave(wave, TotalWaves);
            UI.ShowMessage(isFinal ? "FINAL WAVE" : "WAVE " + wave, 2.5f, Color.white);
            yield return new WaitForSeconds(2f);

            int count = BaseEnemiesPerWave + (wave - 1) * ExtraEnemiesPerWave;
            float delay = Mathf.Max(0.6f, 1.8f - 0.25f * (wave - 1));
            for (int i = 0; i < count && !_stopSpawning; i++)
            {
                Enemy3DType type = PickEnemyType(wave);
                bool shielded = Random.value < 0.12f * (wave - 1);
                SpawnEnemy(type, EnemySpawnPoint(), shielded);
                yield return new WaitForSeconds(delay);
            }

            while (!_stopSpawning && AliveEnemies() > 0)
            {
                yield return new WaitForSeconds(0.5f);
            }
            if (_stopSpawning) yield break;

            if (isFinal)
            {
                UI.ShowMessage("WARNING: BOSS INCOMING", 3f, new Color(1f, 0.3f, 0.3f));
                yield return new WaitForSeconds(3f);
                SpawnBoss();
            }
            else
            {
                UI.ShowMessage("WAVE " + wave + " CLEARED", 2f, new Color(0.5f, 1f, 0.6f));
                yield return new WaitForSeconds(TimeBetweenWaves);
            }
        }
    }

    // Новые типы врагов открываются с каждой волной.
    Enemy3DType PickEnemyType(int wave)
    {
        List<Enemy3DType> pool = new List<Enemy3DType> { Enemy3DType.Basic, Enemy3DType.Basic, Enemy3DType.Weaver };
        if (wave >= 2) { pool.Add(Enemy3DType.Shooter); pool.Add(Enemy3DType.Shooter); }
        if (wave >= 3) { pool.Add(Enemy3DType.Dodger); pool.Add(Enemy3DType.RearGunner); }
        if (wave >= 4) { pool.Add(Enemy3DType.Beamer); }
        return pool[Random.Range(0, pool.Count)];
    }

    // Враги вылетают из красных врат в дальнем конце туннеля.
    Vector3 EnemySpawnPoint()
    {
        if (Tunnel3D.Instance != null)
        {
            Vector2 disc = Random.insideUnitCircle * Tunnel3D.Instance.Radius * 0.8f;
            return Tunnel3D.Instance.GatePosition + new Vector3(disc.x, disc.y, Random.Range(0f, 20f));
        }

        Transform p = _player != null ? _player.transform : null;
        Vector3 center = p != null ? p.position : Vector3.zero;
        Vector3 fwd = p != null ? p.forward : Vector3.forward;
        Vector3 dir = Quaternion.Euler(Random.Range(-35f, 35f), Random.Range(-70f, 70f), 0f) * fwd;
        return center + dir.normalized * Random.Range(110f, 140f);
    }

    public Enemy3D SpawnEnemy(Enemy3DType type, Vector3 position, bool shielded)
    {
        return Enemy3D.Spawn(type, position, shielded, _currentWave);
    }

    void SpawnBoss()
    {
        GameObject go = new GameObject("Boss");
        Boss3D boss = go.AddComponent<Boss3D>();
        if (Tunnel3D.Instance != null)
        {
            // выходит из врат и занимает центр туннеля
            go.transform.position = Tunnel3D.Instance.GatePosition + Vector3.forward * 30f;
            boss.StopPosition = new Vector3(0f, 0f, (Tunnel3D.Instance.StartZ + Tunnel3D.Instance.EndZ) * 0.5f + 40f);
        }
        else
        {
            Vector3 away = _player != null ? -_player.transform.position.normalized : Vector3.forward;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            go.transform.position = away * 170f;
            boss.StopPosition = Vector3.zero;
        }
    }

    // ---------------- Пикапы ----------------
    IEnumerator PickupRoutine()
    {
        yield return new WaitForSeconds(3f);
        while (!_stopSpawning)
        {
            SpawnRandomPickup();
            yield return new WaitForSeconds(Random.Range(MinPickupDelay, MaxPickupDelay));
        }
    }

    void SpawnRandomPickup()
    {
        int ammoWeight = WeightAmmo;
        if (_player != null && _player.Ammo == 0) ammoWeight *= 3; // без патронов Ammo выпадает чаще

        PickupType[] types = { PickupType.Ammo, PickupType.TripleShot, PickupType.Speed, PickupType.Shield,
                               PickupType.Negative, PickupType.Health, PickupType.Radial, PickupType.Homing };
        int[] weights = { ammoWeight, WeightTripleShot, WeightSpeed, WeightShield,
                          WeightNegative, WeightHealth, WeightRadial, WeightHoming };

        int total = 0;
        foreach (int w in weights) total += w;
        int roll = Random.Range(0, total);
        PickupType chosen = PickupType.Ammo;
        for (int i = 0; i < types.Length; i++)
        {
            if (roll < weights[i]) { chosen = types[i]; break; }
            roll -= weights[i];
        }

        Vector3 pos = Vector3.zero;
        if (_player != null)
        {
            pos = _player.transform.position + _player.transform.forward * 60f + Random.insideUnitSphere * 25f;
        }
        if (Tunnel3D.Instance != null)
        {
            // пикапы появляются впереди игрока, внутри или рядом с туннелем
            bool clamped;
            pos = Tunnel3D.Instance.ClampToArena(pos, out clamped);
            Vector2 r = new Vector2(pos.x, pos.y);
            if (r.magnitude > Tunnel3D.Instance.Radius * 1.5f) r = r.normalized * Tunnel3D.Instance.Radius * 1.5f;
            pos = new Vector3(r.x, r.y, pos.z);
        }
        else if (pos.magnitude > 160f) pos = pos.normalized * 160f;
        Pickup3D.Spawn(chosen, pos);
    }
}
