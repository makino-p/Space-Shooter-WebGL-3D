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
    public Material SmokeMaterial;   // Legacy Particles/Alpha Blended — для дыма взрывов

    [Header("Models from 2D sprites (генерируются скриптом Setup3DScene)")]
    public Mesh PlayerShipMesh;
    public Mesh EnemyShipMesh;
    public Mesh AsteroidMesh;
    public Material PlayerShipMaterial;
    public Material EnemyShipMaterial;
    public Material AsteroidMaterial;

    [Header("Imported ships (F3 Corvette — игрок, Star Sparrow — враги и босс)")]
    public GameObject PlayerShipPrefab;
    public Vector3 PlayerShipEuler = Vector3.zero;   // доворот модели, чтобы нос смотрел по +Z
    public float PlayerShipLength = 7f;
    public GameObject[] EnemyShipPrefabs;            // по одному на тип врага, последний — босс
    public Material[] EnemyShipMaterials;
    public Vector3 EnemyShipEuler = Vector3.zero;
    public float EnemyShipLength = 5.5f;
    public float BossShipLength = 26f;
    public GameObject TransportShipPrefab;
    public Material TransportShipMaterial;

    [Header("Transports (поток вдоль луча)")]
    public float TransportInterval = 0.9f;   // как часто появляются
    public int MaxTransports = 28;
    public float TransportMinSpeed = 55f;
    public float TransportMaxSpeed = 95f;

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
    private float _movingTime;              // сколько секунд игрок уже летит
    public float WaveStartAfterMoving = 5f; // враги начинают появляться после 5 с движения
    private bool _stopSpawning;
    private int _currentWave;

    private static AudioSource[] _sfx;
    private static int _sfxIndex;

    void Awake()
    {
        Instance = this;
        Factory3D.LitBase = LitMaterial;
        Factory3D.GlowBase = GlowMaterial;
        Effects3D.SmokeBase = SmokeMaterial;
        Factory3D.SetSpriteModels(PlayerShipMesh, PlayerShipMaterial, EnemyShipMesh, EnemyShipMaterial, AsteroidMesh, AsteroidMaterial);
        Factory3D.SetRockModels(RockMeshes, RockMaterials);
        Factory3D.SetImportedShips(PlayerShipPrefab, PlayerShipEuler, PlayerShipLength,
                                   EnemyShipPrefabs, EnemyShipMaterials, EnemyShipEuler, EnemyShipLength, BossShipLength);
        Factory3D.SetTransportShip(TransportShipPrefab, TransportShipMaterial);
        Transport3D.Active.Clear();
        Hittable3D.All.Clear();
        Projectile3D.Active.Clear();
        Pickup3D.Pickups.Clear();

        BuildEnvironment();
        BuildAudio();
        Tunnel3D tunnel = Tunnel3D.Create();

        UI = gameObject.AddComponent<UI3D>();

        // игрок стоит на оси пути, носом туда, откуда прилетают враги
        GameObject playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(0f, -22f, 0f); // чуть ниже луча, чтобы он был виден
        playerGo.transform.rotation = Quaternion.identity;
        _player = playerGo.AddComponent<Player3D>();
        Factory3D.SpaceDust(playerGo.transform);

        CameraRig.SetTarget(playerGo.transform);

        SpawnAsteroidField(45);
    }

    void Start()
    {
        UI.UpdateScore(0);
        UI.SetWave(0, TotalWaves);
        UI.ShowMessage("FLY (W) - ENEMIES ARRIVE IN 5 SECONDS", 2f, Color.white);
        UI.ShowControlsHint();
        StartCoroutine(TransportRoutine());
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
        // фон следует за камерой, поэтому на бесконечном пути он всегда "на горизонте"
        Planets3D.Build(Planets3D.Style.NeonGasGiant, new Vector3(-700f, -220f, 950f), 520f, 18f, 3);
        Planets3D.Build(Planets3D.Style.NeonCityWorld, new Vector3(950f, 320f, 1250f), 300f, -12f, 7);
        Planets3D.Build(Planets3D.Style.IceMoon, new Vector3(380f, -420f, 700f), 140f, 25f, 11);

        if (SkyboxMaterial == null)
        {
            Factory3D.GlowBall(null, new Vector3(500f, 200f, -300f), 700f, new Color(0.6f, 0.15f, 0.5f, 0.25f)).AddComponent<FollowCamera3D>();
            Factory3D.GlowBall(null, new Vector3(-200f, 400f, -500f), 600f, new Color(0.15f, 0.35f, 0.6f, 0.2f)).AddComponent<FollowCamera3D>();
            Factory3D.GlowBall(null, new Vector3(300f, -350f, 400f), 500f, new Color(0.7f, 0.35f, 0.1f, 0.18f)).AddComponent<FollowCamera3D>();
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
                    float z = Random.Range(-80f, 600f);
                    Vector2 dir = Random.insideUnitCircle.normalized;
                    float r = Random.Range(tunnel.Radius + 8f, tunnel.Radius + 110f);
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

        // два источника для плавного перехода между треками
        _musicA = gameObject.AddComponent<AudioSource>();
        _musicB = gameObject.AddComponent<AudioSource>();
        foreach (AudioSource s in new[] { _musicA, _musicB })
        {
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
        }
        PlayNextTrack(0.5f);
    }

    // ---------------- Музыка: новый случайный трек на каждой волне ----------------
    [Header("Music (саундтреки, меняются на каждой волне)")]
    public AudioClip[] MusicTracks;
    public float MusicVolume = 0.45f;
    public float MusicCrossfade = 2f;

    private AudioSource _musicA, _musicB;
    private bool _musicOnA;
    private int _currentTrack = -1;
    private Coroutine _musicFade;

    // Выбирает случайный трек, отличный от текущего, и плавно переходит на него.
    void PlayNextTrack(float fadeTime)
    {
        AudioClip next = null;
        int count = MusicTracks != null ? MusicTracks.Length : 0;
        if (count > 0)
        {
            int index = Random.Range(0, count);
            if (count > 1 && index == _currentTrack) index = (index + Random.Range(1, count)) % count;
            _currentTrack = index;
            next = MusicTracks[index];
        }
        else if (_currentTrack < 0)
        {
            next = MusicClip; // запасной вариант — старая фоновая музыка
            _currentTrack = 0;
        }
        if (next == null || _musicA == null) return;

        AudioSource from = _musicOnA ? _musicA : _musicB;
        AudioSource to = _musicOnA ? _musicB : _musicA;
        _musicOnA = !_musicOnA;

        to.clip = next;
        to.volume = 0f;
        to.time = 0f;
        to.Play();

        if (_musicFade != null) StopCoroutine(_musicFade);
        _musicFade = StartCoroutine(Crossfade(from, to, fadeTime));
    }

    IEnumerator Crossfade(AudioSource from, AudioSource to, float time)
    {
        float t = 0f;
        float fromStart = from.volume;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Mathf.Max(time, 0.01f));
            to.volume = MusicVolume * k;
            from.volume = fromStart * (1f - k);
            yield return null;
        }
        to.volume = MusicVolume;
        from.volume = 0f;
        from.Stop();
        _musicFade = null;
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
        RecycleAsteroids();
        TrackPlayerMovement();
    }

    // Волны стартуют, когда игрок суммарно пролетел WaveStartAfterMoving секунд (стоять на месте не считается).
    void TrackPlayerMovement()
    {
        if (_wavesStarted || _player == null || !_player.IsAlive) return;
        if (_player.Velocity.magnitude > 2f)
        {
            _movingTime += Time.deltaTime;
            if (_movingTime >= WaveStartAfterMoving)
            {
                StartWaves();
            }
        }
    }

    // Бесконечный путь: астероиды, оставшиеся далеко позади, переносятся далеко вперёд.
    float _nextRecycle;
    void RecycleAsteroids()
    {
        if (Time.time < _nextRecycle || _player == null || Tunnel3D.Instance == null) return;
        _nextRecycle = Time.time + 0.5f;

        float pz = _player.transform.position.z;
        Tunnel3D tunnel = Tunnel3D.Instance;
        foreach (Hittable3D h in Hittable3D.All)
        {
            Asteroid3D a = h as Asteroid3D;
            if (a == null || a.IsStarter || !a.IsAlive) continue;
            float rel = a.transform.position.z - pz;
            if (rel < -220f || rel > 900f)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                float r = Random.Range(tunnel.Radius + 8f, tunnel.Radius + 110f);
                a.transform.position = new Vector3(dir.x * r, dir.y * r, pz + Random.Range(260f, 600f));
            }
        }
    }

    // ---------------- Транспортники ----------------
    // Плотный поток грузовых кораблей вдоль луча — идёт всю игру, независимо от волн.
    IEnumerator TransportRoutine()
    {
        // сразу немного кораблей, чтобы путь не был пустым
        for (int i = 0; i < 10; i++) SpawnTransport(true);
        while (!IsGameOver)
        {
            if (Transport3D.Active.Count < MaxTransports)
            {
                SpawnTransport(false);
            }
            yield return new WaitForSeconds(TransportInterval * Random.Range(0.6f, 1.4f));
        }
    }

    void SpawnTransport(bool initial)
    {
        if (_player == null) return;
        float pz = _player.transform.position.z;

        bool fromAhead = Random.value < 0.7f; // большинство идёт навстречу игроку
        float z;
        if (initial) z = pz + Random.Range(-150f, 900f);
        else z = fromAhead ? pz + Random.Range(1100f, 1500f) : pz - Random.Range(350f, 450f);
        float dirZ = fromAhead ? -1f : 1f;

        // идут вплотную к лучу
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float r = Random.Range(5f, 13f);
        Vector3 pos = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, z);
        Transport3D.Spawn(pos, dirZ, Random.Range(TransportMinSpeed, TransportMaxSpeed));
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
            if (h != null && h.IsAlive && (h is Enemy3D || h is Boss3D)) n++; // транспортники не считаются
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
            PlayNextTrack(MusicCrossfade); // новая волна — новый трек
            yield return new WaitForSeconds(2f);

            int count = BaseEnemiesPerWave + (wave - 1) * ExtraEnemiesPerWave;
            float delay = Mathf.Max(0.6f, 1.8f - 0.25f * (wave - 1));
            // враги выходят группами по 2–3 корабля — так их легче заметить
            int spawned = 0;
            while (spawned < count && !_stopSpawning)
            {
                int group = Mathf.Min(count - spawned, Random.Range(2, 4));
                Vector3 basePos = EnemySpawnPoint();
                for (int g = 0; g < group; g++)
                {
                    Enemy3DType type = PickEnemyType(wave);
                    bool shielded = Random.value < 0.12f * (wave - 1);
                    Vector3 offset = new Vector3(Random.Range(-9f, 9f), Random.Range(-9f, 9f), g * 14f);
                    SpawnEnemy(type, basePos + offset, shielded);
                }
                spawned += group;
                yield return new WaitForSeconds(delay * group);
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
                PlayNextTrack(MusicCrossfade); // босс — тоже смена трека
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
        Transform p = _player != null ? _player.transform : null;
        if (Tunnel3D.Instance != null && p != null)
        {
            // вдоль луча впереди игрока, в своей "полосе" 24–45 м от луча —
            // дальше, чем поток транспортников (до 13 м), и вне яркой дымки луча
            float dirZ = p.forward.z >= -0.2f ? 1f : -1f;
            float z = p.position.z + dirZ * Random.Range(110f, 170f);
            // ближе к той стороне луча, где игрок, чтобы враги были в кадре
            float playerAngle = Mathf.Atan2(p.position.y, p.position.x);
            float angle = playerAngle + Random.Range(-1.2f, 1.2f);
            float r = Random.Range(24f, 45f);
            return new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, z);
        }

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
        if (Tunnel3D.Instance != null && _player != null)
        {
            // появляется далеко впереди по курсу и встаёт в центре пути перед игроком
            Transform p = _player.transform;
            float dir = p.forward.z >= 0f ? 1f : -1f;
            go.transform.position = new Vector3(0f, 0f, p.position.z + dir * 320f);
            boss.StopPosition = new Vector3(0f, 0f, p.position.z + dir * 130f);
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
            Transform pt = _player.transform;
            // прямо перед игроком, в центре экрана — пикап хорошо виден и до него легко долететь
            pos = pt.position + pt.forward * Random.Range(55f, 85f) + pt.right * Random.Range(-14f, 14f) + pt.up * Random.Range(-9f, 9f);
        }
        if (Tunnel3D.Instance != null)
        {
            // только не за пределами арены
            bool clamped;
            pos = Tunnel3D.Instance.ClampToArena(pos, out clamped);
        }
        else if (pos.magnitude > 160f) pos = pos.normalized * 160f;
        Pickup3D.Spawn(chosen, pos);
    }
}
