using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieCoop
{
    /// Игрок от первого лица. Игрок 1 играет на клавиатуре и мыши (и на геймпаде, если
    /// геймпадов хватает на всех), остальные — на своих геймпадах.
    /// Камера живёт в своей части экрана и следует за телом.
    public class Player : MonoBehaviour
    {
        public static readonly List<Player> All = new List<Player>();

        const float SPEED = 4.5f;
        const float GRAVITY = 18f;
        const float EYE_HEIGHT = 1.6f;
        const float DOWNED_EYE_HEIGHT = 0.5f;
        // Радиан на пиксель движения мыши. В Godot было 0.0025, но Unity на macOS отдаёт
        // смещение в «точках», а не в пикселях, и поворот получался вялым.
        const float MOUSE_SENSITIVITY_DEFAULT = 0.006f;
        const string MOUSE_SENSITIVITY_PREF = "mouse_sensitivity";
        static float mouseSensitivity = -1f;

        public static float MouseSensitivity
        {
            get
            {
                if (mouseSensitivity < 0f)
                    mouseSensitivity = PlayerPrefs.GetFloat(MOUSE_SENSITIVITY_PREF, MOUSE_SENSITIVITY_DEFAULT);
                return mouseSensitivity;
            }
            set
            {
                mouseSensitivity = Mathf.Clamp(value, 0.001f, 0.03f);
                PlayerPrefs.SetFloat(MOUSE_SENSITIVITY_PREF, mouseSensitivity);
                PlayerPrefs.Save();
            }
        }
        const float PAD_LOOK_SPEED = 2.8f;
        const float PAD_DEADZONE = 0.18f;
        const float AIM_ASSIST_SLOWDOWN = 0.45f;
        /// Мир, зоны попадания зомби и трупы с оторванными частями (их можно толкать выстрелом).
        const int RAY_MASK = (1 << Layers.World) | (1 << Layers.Hitbox) | (1 << Layers.Debris);
        const float REVIVE_TIME = 3f;
        const float REVIVE_RANGE = 1.6f;
        public const float MAX_HP = 100f;
        const int MAX_PLAYERS = 4;
        // Удар ногой: бьёт ближайшего зомби впереди и отшвыривает его, сбивая с ног.
        // Летящий зомби своим телом сбивает тех, кто стоит за ним (см. Zombie).
        const float KICK_RANGE = 2f;
        const float KICK_ANGLE = 0.75f;
        const float KICK_SPEED = 17f;
        const float KICK_COOLDOWN = 0.7f;
        const float KICK_DAMAGE = 8f;
        /// Насколько сильно нецентральный удар уводит вбок (1 — под 45°).
        const float KICK_SIDE = 0.8f;
        /// Поза оружия в середине перезарядки: нос вверх, низом к центру экрана, чтобы был виден магазин.
        static readonly Vector3 RELOAD_TURN = new Vector3(0.35f, 0f, -0.6f);
        static readonly Vector3 RELOAD_SHIFT = new Vector3(-0.05f, 0.08f, 0f);

        public int index;
        public PlayerInput input;
        public Camera cam;
        public PlayerHud hud;
        public float hp = MAX_HP;
        public bool downed;
        public float reviveProgress;
        public float reloadLeft;
        public float fireCooldown;
        /// Поворот в градусах Unity (вправо — плюс).
        public float yaw;
        /// Наклон взгляда в радианах (вверх — плюс).
        public float pitch;
        public int kills;

        public List<string> owned = new List<string> { "knife" };
        public string current = "knife";
        public Dictionary<string, int> mag = new Dictionary<string, int>();
        public Dictionary<string, int> reserve = new Dictionary<string, int>();

        public float shake;
        float kick;
        bool fireWasPressed, nextWasPressed, shareWasPressed, kickWasPressed;
        Light muzzle;
        Transform weapon;
        PlayerModel model;
        Material gunMat, bladeMat;
        // Части оружия, которые двигаются при перезарядке: магазин (или патрон у дробовика),
        // цевьё дробовика и левая рука, которая приносит новый магазин. Покой — в осях Godot.
        Transform magPart, pump, hand, leg;
        Vector3 magRest, pumpRest, handRest;
        public float kickCooldown;
        float kickAnim;
        CharacterController cc;
        Vector3 velocity;

        public static Player Create(int index, PlayerInput input, Camera cam, PlayerHud hud, Vector3 position)
        {
            var go = new GameObject("Player " + (index + 1));
            go.layer = Layers.Player;
            go.transform.SetParent(GameRoot.World, false);
            go.transform.position = position;
            var p = go.AddComponent<Player>();
            p.Setup(index, input, cam, hud);
            return p;
        }

        void Setup(int playerIndex, PlayerInput playerInput, Camera camera, PlayerHud hudNode)
        {
            index = playerIndex;
            input = playerInput;
            cam = camera;
            hud = hudNode;
            foreach (var w in Weapons.Order)
            {
                mag[w] = 0;
                reserve[w] = 0;
            }
            cc = gameObject.AddComponent<CharacterController>();
            cc.radius = 0.35f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.minMoveDistance = 0f;
            cc.skinWidth = 0.05f;
            cc.stepOffset = 0.3f;
            BuildBody();
            BuildCameraRig();
            BuildWeaponModel();
            All.Add(this);
        }

        void OnDestroy() { All.Remove(this); }

        /// Переносит в точку мимо CharacterController (иначе он вернёт объект на старое место).
        public void Teleport(Vector3 position)
        {
            cc.enabled = false;
            transform.position = position;
            cc.enabled = true;
        }

        public Vector3 Forward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        public Vector3 Right => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        // --- Снаряжение -------------------------------------------------------------

        public Loadout GetLoadout()
        {
            return new Loadout
            {
                owned = new List<string>(owned),
                current = current,
                mag = new Dictionary<string, int>(mag),
                reserve = new Dictionary<string, int>(reserve),
                kills = kills,
            };
        }

        public void SetLoadout(Loadout data)
        {
            if (data == null) return;
            owned = new List<string>(data.owned);
            mag = new Dictionary<string, int>(data.mag);
            reserve = new Dictionary<string, int>(data.reserve);
            kills = data.kills;
            current = "";
            SwitchTo(data.current);
        }

        /// Подобрал оружие: если его не было — берёт в руки, патроны идут в запас.
        public bool GiveWeapon(string w, int ammo)
        {
            bool isNew = !owned.Contains(w);
            if (isNew)
            {
                owned.Add(w);
                owned = owned.OrderBy(Weapons.IndexOf).ToList();
                mag[w] = Mathf.Min(ammo, Weapons.Get(w).mag);
                ammo -= mag[w];
            }
            bool added = GiveAmmo(w, ammo);
            if (isNew) SwitchTo(w);
            return isNew || added;
        }

        public bool GiveAmmo(string w, int ammo)
        {
            int cap = Weapons.Get(w).maxReserve;
            if (reserve[w] >= cap) return false;
            reserve[w] = Mathf.Min(cap, reserve[w] + ammo);
            return true;
        }

        public bool Heal(float amount)
        {
            if (downed || hp >= MAX_HP) return false;
            hp = Mathf.Min(MAX_HP, hp + amount);
            return true;
        }

        public void SwitchTo(string w)
        {
            if (!owned.Contains(w) || (w == current && weapon != null && weapon.childCount > 0)) return;
            current = w;
            reloadLeft = 0f;
            fireCooldown = 0.25f;
            BuildWeaponModel();
        }

        public void CycleWeapon(int step)
        {
            int i = owned.IndexOf(current);
            SwitchTo(owned[G.PosMod(i + step, owned.Count)]);
        }

        public void AddMouseLook(Vector2 relative)
        {
            // В Godot мышь вниз — плюс по Y, в Unity — минус.
            float k = MouseSensitivity;
            yaw += relative.x * k * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch + relative.y * k, -1.4f, 1.4f);
        }

        public void TakeDamage(float amount)
        {
            if (downed) return;
            hp -= amount;
            shake = 0.3f;
            Sfx.PlayFlat("hurt", -3f);
            hud?.FlashDamage();
            if (hp <= 0f)
            {
                hp = 0f;
                downed = true;
                reviveProgress = 0f;
            }
        }

        // --- Каждый кадр ------------------------------------------------------------

        void Update()
        {
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            fireCooldown -= delta;
            kickCooldown -= delta;
            kickAnim = Mathf.Max(0f, kickAnim - delta / KICK_COOLDOWN);
            if (reloadLeft > 0f)
            {
                reloadLeft -= delta;
                if (reloadLeft <= 0f) FinishReload();
            }
            if (input != null)
            {
                int step = input.SensitivityStep;
                if (step != 0)
                {
                    MouseSensitivity *= step > 0 ? 1.25f : 0.8f;
                    hud?.ShowMessage("ЧУВСТВИТЕЛЬНОСТЬ МЫШИ: " + Mathf.RoundToInt(MouseSensitivity / MOUSE_SENSITIVITY_DEFAULT * 100f) + "%", 1.2f);
                }
                AddMouseLook(input.MouseDelta);
                float scroll = input.Scroll;
                if (scroll > 0.1f) CycleWeapon(-1);
                else if (scroll < -0.1f) CycleWeapon(1);
            }
            PadLook(delta);
            Move(delta);
            TryRevive(delta);
            HandleWeapon();
            UpdateCamera(delta);
            model.Animate(delta, G.Flat(cc.velocity).magnitude, yaw, pitch, downed, ReloadProgress(), kickAnim);
            if (hud != null)
            {
                var st = Weapons.Get(current);
                string ammoText = st.melee ? "" : (reloadLeft > 0f ? "перезарядка" : mag[current] + " / " + reserve[current]);
                hud.SetStatus(hp, st.name, ammoText, kills, downed, reviveProgress / REVIVE_TIME);
            }
        }

        void Move(float delta)
        {
            if (!cc.isGrounded) velocity.y -= GRAVITY * delta;
            else if (velocity.y < 0f) velocity.y = -1f;
            var inp = downed || input == null ? Vector2.zero : input.Move(PAD_DEADZONE);
            var dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(inp.x, 0f, inp.y);
            velocity.x = dir.x * SPEED;
            velocity.z = dir.z * SPEED;
            cc.Move(velocity * delta);
        }

        void PadLook(float delta)
        {
            if (input == null) return;
            var look = input.Look;
            if (look.magnitude < PAD_DEADZONE) return;
            float speed = PAD_LOOK_SPEED;
            // Мягкий aim assist: над зомби прицел замедляется, но никуда сам не тянет.
            if (AimZone() != "") speed *= AIM_ASSIST_SLOWDOWN;
            yaw += look.x * speed * delta * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch + look.y * speed * delta, -1.4f, 1.4f);
        }

        void HandleWeapon()
        {
            if (input == null) return;
            bool fire = input.Fire;
            bool reload = input.Reload;
            bool next = input.Next;
            bool share = input.Share;
            bool kickPressed = input.Kick;
            int number = input.NumberKey;
            if (number >= 0) SwitchTo(Weapons.Order[number]);
            if (next && !nextWasPressed) CycleWeapon(1);
            nextWasPressed = next;
            if (share && !shareWasPressed) ShareAmmo();
            shareWasPressed = share;
            if (kickPressed && !kickWasPressed && !downed) Kick();
            kickWasPressed = kickPressed;

            var st = Weapons.Get(current);
            if (reload && !st.melee) StartReload();
            bool trigger = fire && (st.auto || !fireWasPressed);
            fireWasPressed = fire;
            if (!trigger || fireCooldown > 0f || reloadLeft > 0f) return;
            if (st.melee || mag[current] > 0) Shoot();
            else if (reserve[current] > 0) StartReload();
            else
            {
                fireCooldown = 0.4f;
                Sfx.PlayFlat("click_" + current, -2f);
                hud?.ShowMessage("НЕТ ПАТРОНОВ", 1f);
            }
        }

        /// Удар ногой. Возвращает зомби, которого задело, или null.
        public Zombie Kick()
        {
            if (kickCooldown > 0f || downed) return null;
            kickCooldown = KICK_COOLDOWN;
            kickAnim = 1f;
            shake = Mathf.Max(shake, 0.08f);
            Sfx.PlayFlat("slash", -6f, 0.55f);
            var forward = Forward;
            Zombie best = null;
            float bestD = KICK_RANGE;
            foreach (var z in Zombie.All)
            {
                if (z.dead) continue;
                var to = G.Flat(z.transform.position - transform.position);
                float d = to.magnitude;
                if (d < bestD && (d < 0.4f || Vector3.Dot(forward, to / d) > KICK_ANGLE))
                {
                    bestD = d;
                    best = z;
                }
            }
            if (best == null) return null;
            // Удар не по центру уводит зомби вбок: чем дальше от линии взгляда, тем сильнее.
            var right = Right;
            var toBest = best.transform.position - transform.position;
            float off = Mathf.Clamp(Vector3.Dot(toBest, right) / 0.45f, -1f, 1f);
            var dir = (forward + right * off * KICK_SIDE).normalized;
            best.Kicked(dir, KICK_SPEED, KICK_DAMAGE);
            Sfx.PlayAt("crunch", best.transform.position + Vector3.up, -4f, 0.8f);
            return best;
        }

        /// Удар ножом или выстрел из текущего оружия. Возвращает первую задетую зону тела.
        public string Shoot()
        {
            var st = Weapons.Get(current);
            fireCooldown = st.interval;
            kick = 1f;
            if (st.melee) Sfx.PlayFlat("slash", -4f);
            else
            {
                mag[current] -= 1;
                shake = Mathf.Max(shake, 0.12f + (st.pellets > 1 ? 0.1f : 0f));
                muzzle.intensity = 6f;
                // Дробовик громче всех, очередь автомата тише одиночного выстрела, чтобы не глушила.
                Sfx.PlayFlat("shot_" + current, current == "smg" ? -6f : current == "shotgun" ? -3f : -4f);
                AlertZombies(st.noise);
            }
            Physics.SyncTransforms();
            string first = "";
            var hitZombies = new HashSet<Zombie>();
            bool loudDebris = true;
            var ct = cam.transform;
            for (int p = 0; p < st.pellets; p++)
            {
                var dir = (ct.forward + ct.right * G.Rand(-st.spread, st.spread) + ct.up * G.Rand(-st.spread, st.spread)).normalized;
                if (!Ray(dir, st.range, out var hit)) continue;
                if (hit.rigidbody != null && hit.collider.GetComponent<HitZone>() == null)
                {
                    // Трупы и оторванные части отлетают от выстрела.
                    hit.rigidbody.AddForceAtPosition(dir * st.impulse + Vector3.up * 0.6f, hit.point, ForceMode.Impulse);
                    if (loudDebris)
                    {
                        Gore.Splash(hit.point, dir, 6);
                        loudDebris = false;
                    }
                    continue;
                }
                var zone = hit.collider.GetComponent<HitZone>();
                if (zone == null || zone.zombie == null) continue;
                if (zone.zombie.dead)
                {
                    // Труп дёргается от пуль, его можно разорвать на куски.
                    zone.zombie.HitCorpse(zone.zone, st.damage, hit.point, dir, st.knockback);
                    continue;
                }
                bool loud = !hitZombies.Contains(zone.zombie);
                hitZombies.Add(zone.zombie);
                if (zone.zombie.TakeHit(zone.zone, st.damage, hit.point, dir, loud, st.knockback)) kills++;
                if (first == "") first = zone.zone;
            }
            if (first != "") hud?.FlashHit(first == "head");
            return first;
        }

        /// Передаёт половину запаса патронов ближайшему напарнику (до 2,5 м).
        /// Патроны того оружия, что в руках; с ножом в руках — того, которых больше всего.
        /// Возвращает, сколько патронов передано.
        public int ShareAmmo()
        {
            Player mate = null;
            float best = 2.5f;
            foreach (var p in All)
            {
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (p != this && d < best)
                {
                    best = d;
                    mate = p;
                }
            }
            if (mate == null)
            {
                hud?.ShowMessage("ПОДОЙДИ К НАПАРНИКУ", 1f);
                return 0;
            }
            string gun = current;
            if (Weapons.Get(gun).melee)
                foreach (var w in Weapons.Order)
                    if (reserve[w] > reserve[gun]) gun = w;
            int amount = Mathf.CeilToInt(reserve[gun] / 2f);
            int room = Weapons.Get(gun).melee ? 0 : Weapons.Get(gun).maxReserve - mate.reserve[gun];
            amount = Mathf.Min(amount, room);
            if (amount <= 0)
            {
                hud?.ShowMessage("НЕЧЕГО ПЕРЕДАТЬ", 1f);
                return 0;
            }
            reserve[gun] -= amount;
            mate.reserve[gun] += amount;
            string what = Weapons.DisplayName(gun) + " " + amount;
            Sfx.PlayFlat("pickup", -8f);
            hud?.ShowMessage("ОТДАЛ ИГРОКУ " + (mate.index + 1) + ": " + what, 1.5f);
            mate.hud?.ShowMessage("ИГРОК " + (index + 1) + " ДАЛ ТЕБЕ: " + what, 1.5f);
            return amount;
        }

        /// Зона тела под прицелом (нужна для aim assist).
        public string AimZone()
        {
            if (!Ray(cam.transform.forward, 60f, out var hit)) return "";
            var zone = hit.collider.GetComponent<HitZone>();
            if (zone == null || (zone.zombie != null && zone.zombie.dead)) return "";
            return zone.zone;
        }

        bool Ray(Vector3 dir, float length, out RaycastHit hit)
        {
            return Physics.Raycast(cam.transform.position, dir, out hit, length, RAY_MASK, QueryTriggerInteraction.Collide);
        }

        void AlertZombies(float radius)
        {
            foreach (var z in Zombie.All)
                if (Vector3.Distance(z.transform.position, transform.position) < radius) z.Alert();
        }

        public void StartReload()
        {
            int size = Weapons.Get(current).mag;
            if (reloadLeft > 0f || mag[current] >= size || reserve[current] <= 0) return;
            reloadLeft = Weapons.Get(current).reload;
            Sfx.PlayFlat("reload_" + current, -3f);
        }

        void FinishReload()
        {
            int size = Weapons.Get(current).mag;
            int take = Mathf.Min(size - mag[current], reserve[current]);
            mag[current] += take;
            reserve[current] -= take;
        }

        void TryRevive(float delta)
        {
            if (!downed) return;
            bool helped = false;
            foreach (var p in All)
                if (p != this && !p.downed && Vector3.Distance(p.transform.position, transform.position) < REVIVE_RANGE)
                    helped = true;
            reviveProgress = helped ? reviveProgress + delta : 0f;
            if (reviveProgress >= REVIVE_TIME)
            {
                downed = false;
                hp = 50f;
                reviveProgress = 0f;
            }
        }

        // --- Камера и модели --------------------------------------------------------

        void UpdateCamera(float delta)
        {
            shake = Mathf.Max(0f, shake - delta);
            kick = Mathf.Max(0f, kick - delta * 8f);
            muzzle.intensity = Mathf.Max(0f, muzzle.intensity - delta * 120f);
            float eye = downed ? DOWNED_EYE_HEIGHT : EYE_HEIGHT;
            var jitter = new Vector3(G.Rand(-1f, 1f), G.Rand(-1f, 1f), 0f) * shake * 0.06f;
            float recoil = current == "knife" ? 0f : kick * 0.03f;
            cam.transform.SetPositionAndRotation(
                transform.position + Vector3.up * eye + jitter,
                Quaternion.Euler(-(pitch + recoil) * Mathf.Rad2Deg, yaw, 0f));
            if (current == "knife")
            {
                // Взмах ножом: лезвие проходит справа налево.
                weapon.localPosition = G.P(-kick * 0.25f, 0f, -kick * 0.15f);
                weapon.localRotation = G.R(0f, kick * 0.9f, -kick * 0.4f);
            }
            else
            {
                weapon.localPosition = G.P(0f, 0f, kick * 0.08f);
                weapon.localRotation = G.R(kick * 0.25f, 0f, 0f);
                AnimateReload();
            }
            AnimateKick();
        }

        /// Нога в кадре: выходит снизу вперёд и уходит обратно.
        void AnimateKick()
        {
            if (leg == null) BuildLeg();
            float t = 1f - kickAnim;
            float o = 0f;
            if (kickAnim > 0f) o = G.Smoothstep(0f, 0.1f, t) * (1f - G.Smoothstep(0.35f, 0.75f, t));
            leg.gameObject.SetActive(o > 0.01f);
            leg.localPosition = G.P(Vector3.Lerp(new Vector3(0.1f, -1.1f, -0.3f), new Vector3(0.03f, -0.52f, -0.6f), o));
            leg.localRotation = G.R(Mathf.Lerp(-0.3f, 0.55f, o), 0f, 0f);
            if (o > 0.01f) weapon.localPosition += G.P(new Vector3(0.05f, -0.08f, 0.05f) * o);
        }

        void BuildLeg()
        {
            leg = MeshKit.Node(cam.transform, "Leg");
            leg.gameObject.layer = Layers.PlayerView + index;
            var st = PlayerModel.Styles[model.style];
            var pants = MeshKit.Shade(st.pants);
            var shoes = MeshKit.Shade(st.shoes);
            MeshKit.Add(leg, MeshKit.Box(new Vector3(0.17f, 0.17f, 0.7f)), pants, new Vector3(0, 0, 0.3f));
            MeshKit.Add(leg, MeshKit.Box(new Vector3(0.2f, 0.26f, 0.17f)), shoes, new Vector3(0, 0.02f, -0.06f));
            MeshKit.NoShadows(leg);
            leg.gameObject.SetActive(false);
        }

        /// Доля пройденной перезарядки от 0 до 1, или -1, если оружие не перезаряжается.
        public float ReloadProgress()
        {
            var st = Weapons.Get(current);
            if (reloadLeft <= 0f || st.melee) return -1f;
            return Mathf.Clamp01(1f - reloadLeft / st.reload);
        }

        /// Перезарядка в кадре: оружие наклоняется к центру, старый магазин выпадает вниз,
        /// левая рука приносит новый и вбивает его. У дробовика рука по одному
        /// досылает патроны снизу, в конце цевьё передёргивается.
        void AnimateReload()
        {
            float r = ReloadProgress();
            if (hand == null) return;
            if (r < 0f)
            {
                hand.localPosition = G.P(handRest);
                if (magPart != null)
                {
                    magPart.localPosition = G.P(magRest);
                    magPart.gameObject.SetActive(current != "shotgun");
                }
                if (pump != null) pump.localPosition = G.P(pumpRest);
                return;
            }
            float tilt = G.Smoothstep(0f, 0.15f, r) * (1f - G.Smoothstep(0.85f, 1f, r));
            // Поворот вокруг самого оружия, а не камеры, иначе оно заслоняет пол-экрана.
            var pivot = G.P(0.25f, -0.3f, -0.55f);
            var turn = G.R(RELOAD_TURN * tilt);
            weapon.localRotation = turn;
            weapon.localPosition = pivot - turn * pivot + G.P(RELOAD_SHIFT * tilt);
            Vector3 handAt;
            float w;
            if (current == "shotgun")
            {
                // Три патрона: рука подносит снизу к окну, патрон уходит внутрь.
                float load = Mathf.Clamp((r - 0.15f) / 0.6f, 0f, 0.999f);
                float s = (load * 3f) % 1f;
                float up = Mathf.Sin(s * Mathf.PI);
                bool loading = r > 0.15f && r < 0.75f;
                magPart.gameObject.SetActive(loading && s < 0.6f);
                var magPos = magRest + new Vector3(0f, -0.22f * (1f - up), 0.03f * s);
                magPart.localPosition = G.P(magPos);
                handAt = magRest + new Vector3(-0.02f, -0.06f - 0.22f * (1f - up), 0.03f);
                w = G.Smoothstep(0.08f, 0.18f, r) * (1f - G.Smoothstep(0.72f, 0.8f, r));
                var handPos = Vector3.Lerp(handRest, handAt, w);
                // Передёргивание цевья в конце.
                float pumpOut = Mathf.Sin(Mathf.Clamp01((r - 0.78f) / 0.15f) * Mathf.PI);
                var pumpPos = pumpRest + new Vector3(0, 0, 0.13f * pumpOut);
                pump.localPosition = G.P(pumpPos);
                if (pumpOut > 0f) handPos = pumpPos + new Vector3(0f, -0.06f, 0f);
                hand.localPosition = G.P(handPos);
                return;
            }
            // Магазин: выпадает вниз, потом поднимается вместе с рукой и встаёт на место.
            float drop = G.Smoothstep(0.12f, 0.35f, r);
            float insert = G.Smoothstep(0.45f, 0.72f, r);
            var below = new Vector3(0f, -0.45f, 0.04f);
            Vector3 magNow;
            if (r < 0.4f)
            {
                magNow = magRest + below * drop;
                magPart.gameObject.SetActive(drop < 0.95f);
            }
            else
            {
                magPart.gameObject.SetActive(true);
                magNow = magRest + below * (1f - insert);
            }
            magPart.localPosition = G.P(magNow);
            handAt = magNow + new Vector3(0f, -0.06f, 0.02f);
            w = G.Smoothstep(0.32f, 0.45f, r) * (1f - G.Smoothstep(0.78f, 0.92f, r));
            // Удар ладонью по магазину в конце.
            if (r > 0.72f && r < 0.8f) handAt += new Vector3(0, 0.03f * Mathf.Sin((r - 0.72f) / 0.08f * Mathf.PI), 0);
            hand.localPosition = G.P(Vector3.Lerp(handRest, handAt, w));
        }

        void BuildBody()
        {
            int layer = Layers.PlayerBody + index;
            var go = new GameObject("Model");
            go.transform.SetParent(transform, false);
            model = go.AddComponent<PlayerModel>();
            model.Build(index, layer);
            // Номер над головой, чтобы напарник находил игрока в суматохе.
            MeshKit.Label(transform, (index + 1).ToString(), new Vector3(0f, 2.25f, 0f), 0.34f, true, layer);
        }

        void BuildCameraRig()
        {
            cam.fieldOfView = 80f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            int mask = ~0;
            mask &= ~(1 << (Layers.PlayerBody + index));
            for (int i = 0; i < MAX_PLAYERS; i++)
                if (i != index) mask &= ~(1 << (Layers.PlayerView + i));
            cam.cullingMask = mask;

            var fl = new GameObject("Flashlight");
            fl.transform.SetParent(cam.transform, false);
            fl.transform.localPosition = G.P(0.15f, -0.1f, 0f);
            var flashlight = fl.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.range = 20f;
            flashlight.spotAngle = 52f;
            flashlight.innerSpotAngle = 30f;
            flashlight.intensity = 5f;
            flashlight.shadows = LightShadows.Soft;

            muzzle = MeshKit.PointLight(cam.transform, Vector3.zero, 7f, 0f);
            muzzle.transform.localPosition = G.P(0.22f, -0.15f, -0.8f);

            weapon = MeshKit.Node(cam.transform, "Weapon");
            gunMat = MeshKit.Lit(new Color(0.22f, 0.22f, 0.22f), new Color(0.12f, 0.12f, 0.12f), 0f, 0.4f);
            bladeMat = MeshKit.Lit(new Color(0.9f, 0.9f, 0.9f), new Color(0.25f, 0.25f, 0.25f), 0.6f, 0.8f);
        }

        /// Модель оружия в руках из нескольких блоков: узнаётся по силуэту.
        void BuildWeaponModel()
        {
            if (weapon == null) return;
            foreach (Transform c in weapon)
            {
                c.gameObject.SetActive(false);
                Destroy(c.gameObject);
            }
            weapon.DetachChildren();
            magPart = pump = hand = null;
            Material sleeve = MeshKit.Shade(0.3f), skin = MeshKit.Shade(0.75f);
            if (model != null)
            {
                model.SetWeapon(current);
                // Своя рука в рукаве того же тона, что одежда персонажа.
                var st = PlayerModel.Styles[model.style];
                sleeve = MeshKit.Shade(st.top);
                skin = MeshKit.Shade(st.skin);
                AddBlock(new Vector3(0.07f, 0.07f, 0.3f), new Vector3(0.29f, -0.38f, -0.3f), 0.3f, sleeve);
                AddBlock(new Vector3(0.06f, 0.07f, 0.08f), new Vector3(0.27f, -0.33f, -0.47f), 0.3f, skin);
            }
            switch (current)
            {
                case "knife":
                    AddBlock(new Vector3(0.03f, 0.04f, 0.12f), new Vector3(0.24f, -0.27f, -0.42f), 0.4f, gunMat);
                    AddBlock(new Vector3(0.012f, 0.05f, 0.22f), new Vector3(0.24f, -0.22f, -0.58f), 0.4f, bladeMat);
                    return;
                case "pistol":
                    AddBlock(new Vector3(0.06f, 0.08f, 0.26f), new Vector3(0.26f, -0.25f, -0.55f), 0f, gunMat);
                    AddBlock(new Vector3(0.05f, 0.13f, 0.07f), new Vector3(0.26f, -0.33f, -0.48f), 0.25f, gunMat);
                    magPart = Part(new Vector3(0.26f, -0.4f, -0.465f), out magRest);
                    AddBlock(new Vector3(0.042f, 0.1f, 0.055f), Vector3.zero, 0.25f, bladeMat, magPart);
                    break;
                case "smg":
                    AddBlock(new Vector3(0.07f, 0.1f, 0.5f), new Vector3(0.24f, -0.24f, -0.6f), 0f, gunMat);
                    AddBlock(new Vector3(0.05f, 0.12f, 0.07f), new Vector3(0.24f, -0.33f, -0.44f), 0.25f, gunMat);
                    magPart = Part(new Vector3(0.24f, -0.36f, -0.62f), out magRest);
                    AddBlock(new Vector3(0.04f, 0.2f, 0.06f), Vector3.zero, 0.15f, bladeMat, magPart);
                    break;
                case "shotgun":
                    AddBlock(new Vector3(0.08f, 0.08f, 0.75f), new Vector3(0.23f, -0.23f, -0.7f), 0f, gunMat);
                    AddBlock(new Vector3(0.07f, 0.14f, 0.2f), new Vector3(0.23f, -0.3f, -0.3f), 0.3f, gunMat);
                    pump = Part(new Vector3(0.23f, -0.29f, -0.62f), out pumpRest);
                    AddBlock(new Vector3(0.1f, 0.07f, 0.25f), Vector3.zero, 0f, bladeMat, pump);
                    // Патрон в левой руке, его видно только при перезарядке.
                    magPart = Part(new Vector3(0.23f, -0.3f, -0.45f), out magRest);
                    AddBlock(new Vector3(0.035f, 0.035f, 0.08f), Vector3.zero, 0f, bladeMat, magPart);
                    magPart.gameObject.SetActive(false);
                    break;
            }
            // Левая рука прячется под кадром и появляется на время перезарядки.
            hand = Part(new Vector3(0.1f, -0.9f, -0.45f), out handRest);
            AddBlock(new Vector3(0.075f, 0.075f, 0.34f), new Vector3(-0.06f, -0.06f, 0.2f), 0.5f, sleeve, hand);
            AddBlock(new Vector3(0.07f, 0.065f, 0.09f), Vector3.zero, 0f, skin, hand);
        }

        Transform Part(Vector3 pos, out Vector3 rest)
        {
            rest = pos;
            var n = MeshKit.Node(weapon, "Part", pos);
            n.gameObject.layer = Layers.PlayerView + index;
            return n;
        }

        void AddBlock(Vector3 size, Vector3 pos, float tilt, Material mat, Transform parent = null)
        {
            var b = MeshKit.Add(parent != null ? parent : weapon, MeshKit.Box(size), mat, pos, new Vector3(tilt, 0f, 0f));
            b.gameObject.layer = Layers.PlayerView + index;
            b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
