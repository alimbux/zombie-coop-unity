using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZombieCoop
{
    /// Вся игра в одном объекте: меню, сборка уровня по выбранному режиму (этажи или волны),
    /// деление экрана на 1–4 части и переходы между этажами. Каждая часть экрана —
    /// своя камера, которая рисует в текстуру; текстура выводится на экран через нуар-фильтр,
    /// поверх — интерфейс игрока. Сцена не нужна: всё создаётся кодом при запуске.
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance;
        public static Transform World;

        const float EXIT_RADIUS = 2f;
        static readonly Color BloodColor = new Color(0.9f, 0f, 0.03f);

        enum State { Menu, Game }
        State state;

        Camera screenCam;
        AudioListener menuListener;
        readonly List<Camera> cameras = new List<Camera>();
        readonly List<RenderTexture> targets = new List<RenderTexture>();
        readonly List<Material> noir = new List<Material>();
        public readonly List<Player> players = new List<Player>();
        public readonly List<PlayerHud> huds = new List<PlayerHud>();
        public LevelGenerator level;
        public ZombieSpawner spawner;
        bool finishing;
        /// Номер запуска: отложенный перезапуск не сработает, если игрок уже ушёл в меню.
        int generation;
        Coroutine hitStop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            new GameObject("Zombie Coop").AddComponent<GameRoot>();
        }

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Камеры из открытой сцены (если есть) не нужны: игра рисует всё сама.
            foreach (var c in Camera.allCameras) c.gameObject.SetActive(false);
            Layers.SetupCollisions();
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                Debug.LogWarning("Zombie Coop: URP не назначен, материалы будут розовыми. Меню «Zombie Coop → Настроить URP».");
            screenCam = new GameObject("Screen").AddComponent<Camera>();
            screenCam.transform.SetParent(transform, false);
            screenCam.cullingMask = 0;
            screenCam.clearFlags = CameraClearFlags.SolidColor;
            screenCam.backgroundColor = Color.black;
            screenCam.depth = 100;
            menuListener = gameObject.AddComponent<AudioListener>();
            Sfx.PreloadAll();
            OpenMenu();
        }

        // --- Меню -----------------------------------------------------------------

        GameMode menuMode;
        int menuPlayers;
        /// Строка фокуса: 0 — режим, 1 — число игроков, 2 — «НАЧАТЬ».
        int focusRow = 2;
        float navRepeat;

        public void OpenMenu()
        {
            ClearWorld();
            state = State.Menu;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            menuMode = GameConfig.Mode;
            menuPlayers = GameConfig.Players;
            focusRow = 2;
            screenCam.backgroundColor = Color.black;
            menuListener.enabled = true;
        }

        public void SetMode(GameMode mode) { menuMode = mode; }
        public void SetPlayers(int count) { menuPlayers = Mathf.Clamp(count, 1, 4); }

        public void StartFromMenu()
        {
            GameConfig.NewGame(menuMode, menuPlayers);
            StartGame();
        }

        void MenuInput()
        {
            var kb = Keyboard.current;
            int dx = 0, dy = 0;
            bool press = false;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) dy = -1;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) dy = 1;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) dx = -1;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) dx = 1;
                press |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            }
            navRepeat -= Time.unscaledDeltaTime;
            foreach (var pad in Gamepad.all)
            {
                if (pad.dpad.up.wasPressedThisFrame) dy = -1;
                if (pad.dpad.down.wasPressedThisFrame) dy = 1;
                if (pad.dpad.left.wasPressedThisFrame) dx = -1;
                if (pad.dpad.right.wasPressedThisFrame) dx = 1;
                var stick = pad.leftStick.ReadValue();
                if (navRepeat <= 0f && stick.magnitude > 0.6f)
                {
                    if (Mathf.Abs(stick.y) > Mathf.Abs(stick.x)) dy = stick.y > 0 ? -1 : 1;
                    else dx = stick.x > 0 ? 1 : -1;
                    navRepeat = 0.25f;
                }
                press |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
            }
            if (dy != 0) focusRow = Mathf.Clamp(focusRow + dy, 0, 2);
            if (dx != 0)
            {
                if (focusRow == 0) menuMode = dx < 0 ? GameMode.Escape : GameMode.Waves;
                else if (focusRow == 1) menuPlayers = Mathf.Clamp(menuPlayers + dx, 1, 4);
            }
            if (press && focusRow == 2) StartFromMenu();
        }

        void DrawMenu(float s)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float cx = Screen.width / 2f;
            float y = Screen.height / 2f - 380f * s;
            Text(new Rect(0, y, Screen.width, 80 * s), "ZOMBIE COOP", 64, Color.white, s); y += 84 * s;
            Text(new Rect(0, y, Screen.width, 30 * s), "кооператив против зомби", 22, BloodColor, s); y += 70 * s;
            Text(new Rect(0, y, Screen.width, 28 * s), "РЕЖИМ", 20, new Color(0.6f, 0.6f, 0.6f), s); y += 40 * s;
            float bw = 360 * s, bh = 56 * s, gap = 12 * s;
            if (Button(new Rect(cx - bw - gap / 2, y, bw, bh), "ВЫБРАТЬСЯ ИЗ ЗДАНИЯ", menuMode == GameMode.Escape, focusRow == 0 && menuMode == GameMode.Escape, s))
            { menuMode = GameMode.Escape; focusRow = 0; }
            if (Button(new Rect(cx + gap / 2, y, bw, bh), "ВОЛНЫ", menuMode == GameMode.Waves, focusRow == 0 && menuMode == GameMode.Waves, s))
            { menuMode = GameMode.Waves; focusRow = 0; }
            y += bh + 18 * s;
            string hint = menuMode == GameMode.Escape
                ? "Этажи растут, зомби бродят по комнатам. Ищи оружие, патроны и выход."
                : "Один зал, зомби лезут волнами. Продержись как можно дольше.";
            Text(new Rect(0, y, Screen.width, 26 * s), hint, 18, new Color(0.6f, 0.6f, 0.6f), s); y += 48 * s;
            Text(new Rect(0, y, Screen.width, 28 * s), "ИГРОКОВ", 20, new Color(0.6f, 0.6f, 0.6f), s); y += 40 * s;
            float pw = 90 * s;
            float startX = cx - (pw * 4 + gap * 3) / 2f;
            for (int n = 1; n <= 4; n++)
            {
                if (Button(new Rect(startX + (n - 1) * (pw + gap), y, pw, bh), n.ToString(), menuPlayers == n, focusRow == 1 && menuPlayers == n, s))
                { menuPlayers = n; focusRow = 1; }
            }
            y += bh + 34 * s;
            if (Button(new Rect(cx - bw / 2, y, bw, bh), "НАЧАТЬ", false, focusRow == 2, s)) StartFromMenu();
            y += bh + 28 * s;
            Text(new Rect(0, y, Screen.width, 90 * s),
                "Игрок 1 — клавиатура и мышь (или геймпад, если их хватает на всех), остальные — геймпады.\n" +
                "Стрельба — ЛКМ / RT, смена оружия — 1–4, Q, колесо / Y, перезарядка — R / X, удар ногой — F, ПКМ / RB,\n" +
                "отдать патроны напарнику — G / B, поднять упавшего — стоять рядом, чувствительность мыши — «-» / «=», меню — Esc / Back.",
                16, new Color(0.55f, 0.55f, 0.55f), s);
        }

        static GUIStyle textStyle;

        static void Text(Rect r, string text, int size, Color color, float s)
        {
            if (textStyle == null) textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            textStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * s));
            textStyle.normal.textColor = color;
            GUI.Label(r, text, textStyle);
        }

        /// Кнопка меню: выбранное — инверсией, в фокусе — красная рамка.
        static bool Button(Rect r, string text, bool on, bool focus, float s)
        {
            float border = (focus ? 3f : 2f) * Mathf.Max(1f, s);
            GUI.color = focus ? BloodColor : Color.white;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = on ? Color.white : Color.black;
            GUI.DrawTexture(new Rect(r.x + border, r.y + border, r.width - border * 2, r.height - border * 2), Texture2D.whiteTexture);
            GUI.color = Color.white;
            Text(r, text, 26, on ? Color.black : Color.white, s);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }
            return false;
        }

        // --- Игра -----------------------------------------------------------------

        void ClearWorld()
        {
            generation++;
            if (World != null)
            {
                World.gameObject.SetActive(false);
                Destroy(World.gameObject);
                World = null;
            }
            Zombie.All.Clear();
            Player.All.Clear();
            Pickup.All.Clear();
            Gore.Clear();
            Sfx.ResetVoices();
            foreach (var rt in targets)
                if (rt != null) rt.Release();
            targets.Clear();
            cameras.Clear();
            players.Clear();
            huds.Clear();
            level = null;
            spawner = null;
        }

        public void StartGame()
        {
            ClearWorld();
            state = State.Game;
            finishing = false;
            Time.timeScale = 1f;
            menuListener.enabled = false;
            screenCam.backgroundColor = Color.white;
            World = new GameObject("World").transform;
            World.gameObject.AddComponent<Fx>();
            int count = GameConfig.Players;
            BuildScreens(count);

            List<Vector3> starts;
            if (GameConfig.Mode == GameMode.Escape)
            {
                level = new LevelGenerator();
                starts = level.Build(World, GameConfig.FloorNumber, count, Random.Range(0, int.MaxValue));
            }
            else
            {
                var layout = TestRoom.Build(World);
                starts = layout.playerStarts;
                spawner = World.gameObject.AddComponent<ZombieSpawner>();
                spawner.players = count;
                spawner.spawnPoints = layout.zombieSpawns;
                spawner.pickupPoints = layout.pickupPoints;
                spawner.WaveStarted += OnWaveStarted;
            }

            var pads = new List<Gamepad>(Gamepad.all);
            for (int i = 0; i < count; i++)
            {
                var input = new PlayerInput(PadFor(i, count, pads), i == 0);
                var player = Player.Create(i, input, cameras[i], huds[i], starts[i]);
                if (i < GameConfig.Loadouts.Count) player.SetLoadout(GameConfig.Loadouts[i]);
                players.Add(player);
            }
            cameras[0].gameObject.AddComponent<AudioListener>();

            if (level != null)
                foreach (var hud in huds)
                {
                    hud.SetLevel("ЭТАЖ " + GameConfig.FloorNumber);
                    hud.ShowMessage("НАЙДИ ВЫХОД", 3f);
                }
        }

        /// Игрок 1 всегда может играть на клавиатуре и мыши. Если геймпадов хватает на всех,
        /// у каждого свой геймпад; если нет — игрок 1 без геймпада, остальные берут их по порядку.
        public static Gamepad PadFor(int i, int count, IList<Gamepad> pads)
        {
            if (pads.Count >= count) return pads[i];
            if (i == 0) return null;
            return i - 1 < pads.Count ? pads[i - 1] : null;
        }

        void BuildScreens(int count)
        {
            var shader = Resources.Load<Shader>("ZombieCoop/Noir");
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Camera " + (i + 1));
                go.transform.SetParent(World, false);
                var cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cameras.Add(cam);
                targets.Add(null);
                huds.Add(new PlayerHud());
                if (noir.Count <= i)
                {
                    var m = new Material(shader);
                    m.SetFloat("_Seed", i * 17f);
                    noir.Add(m);
                }
            }
        }

        /// Делит экран: один игрок — весь экран, двое — пополам, трое и четверо — на четыре части.
        /// Между частями — тонкие белые полосы.
        Rect Cell(int i, int count)
        {
            const float gap = 4f;
            float w = Screen.width, h = Screen.height;
            if (count == 1) return new Rect(0, 0, w, h);
            float cw = (w - gap) / 2f;
            if (count == 2) return new Rect(i * (cw + gap), 0, cw, h);
            float ch = (h - gap) / 2f;
            return new Rect((i % 2) * (cw + gap), (i / 2) * (ch + gap), cw, ch);
        }

        void UpdateTargets()
        {
            for (int i = 0; i < cameras.Count; i++)
            {
                var r = Cell(i, cameras.Count);
                int w = Mathf.Max(16, Mathf.RoundToInt(r.width));
                int h = Mathf.Max(16, Mathf.RoundToInt(r.height));
                var rt = targets[i];
                if (rt == null || rt.width != w || rt.height != h)
                {
                    if (rt != null) rt.Release();
                    rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default) { name = "Player " + (i + 1) };
                    rt.Create();
                    targets[i] = rt;
                    cameras[i].targetTexture = rt;
                    cameras[i].aspect = (float)w / h;
                }
            }
        }

        void Update()
        {
            if (state == State.Menu)
            {
                MenuInput();
                return;
            }
            GameInput();
            if (state != State.Game) return;
            UpdateTargets();
            foreach (var hud in huds) hud.Tick(Time.deltaTime);
            if (finishing) return;
            int alive = 0, atExit = 0;
            foreach (var p in players)
            {
                if (p.downed) continue;
                alive++;
                if (level != null)
                {
                    var d = new Vector2(p.transform.position.x - level.exitPosition.x, p.transform.position.z - level.exitPosition.z);
                    if (d.magnitude < EXIT_RADIUS) atExit++;
                }
            }
            if (alive == 0) StartCoroutine(Restart());
            else if (level != null && atExit == alive) StartCoroutine(FloorComplete());
            else if (level != null && atExit > 0)
                foreach (var hud in huds) hud.ShowMessage("ВЫХОД НАЙДЕН — ВСЕ К ВЫХОДУ", 0.3f);
        }

        void GameInput()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState != CursorLockMode.Locked &&
                (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            bool back = kb != null && kb.escapeKey.wasPressedThisFrame;
            foreach (var pad in Gamepad.all) back |= pad.selectButton.wasPressedThisFrame;
            if (back) OpenMenu();
        }

        void OnGUI()
        {
            float s = Screen.height / 900f;
            if (state == State.Menu)
            {
                DrawMenu(s);
                return;
            }
            int count = cameras.Count;
            if (Event.current.type == EventType.Repaint)
            {
                for (int i = 0; i < count; i++)
                    if (targets[i] != null) Graphics.DrawTexture(Cell(i, count), targets[i], noir[i]);
                if (count == 3)
                {
                    // Пустая четвёртая клетка при трёх игроках.
                    GUI.color = Color.black;
                    GUI.DrawTexture(Cell(3, 4), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }
            float hs = count > 2 ? s * 0.75f : s;
            for (int i = 0; i < huds.Count; i++) huds[i].Draw(Cell(i, count), hs);
        }

        void OnWaveStarted(int number)
        {
            foreach (var hud in huds)
            {
                hud.SetLevel("ВОЛНА " + number);
                hud.ShowMessage("ВОЛНА " + number);
            }
        }

        IEnumerator FloorComplete()
        {
            finishing = true;
            GameConfig.Loadouts = new List<Loadout>();
            foreach (var p in players) GameConfig.Loadouts.Add(p.GetLoadout());
            GameConfig.FloorNumber++;
            foreach (var hud in huds) hud.ShowMessage("ЭТАЖ ПРОЙДЕН", 3f);
            int gen = generation;
            yield return new WaitForSecondsRealtime(1.5f);
            if (state == State.Game && gen == generation) StartGame();
        }

        /// Все пали: этаж начинается заново (новая планировка, снаряжение как было на входе),
        /// волны — с первой.
        IEnumerator Restart()
        {
            finishing = true;
            if (GameConfig.Mode == GameMode.Waves) GameConfig.Loadouts = new List<Loadout>();
            foreach (var hud in huds) hud.ShowMessage("ВСЕ ПАЛИ", 3f);
            int gen = generation;
            yield return new WaitForSecondsRealtime(2.5f);
            if (state == State.Game && gen == generation) StartGame();
        }

        /// Короткое замедление времени на фатальном попадании.
        public void HitStop(float duration)
        {
            if (hitStop != null) StopCoroutine(hitStop);
            hitStop = StartCoroutine(HitStopRoutine(duration));
        }

        IEnumerator HitStopRoutine(float duration)
        {
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = 1f;
            hitStop = null;
        }
    }
}
