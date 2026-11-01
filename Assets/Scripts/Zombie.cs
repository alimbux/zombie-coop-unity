using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Зона попадания на теле зомби: по ней пуля понимает, куда попала.
    public class HitZone : MonoBehaviour
    {
        public string zone;
        public Zombie zombie;
    }

    /// Зомби: сгорбленная фигура в рваной одежде, руки тянутся вперёд. Каждая часть тела —
    /// шарнир (шея, плечо, бедро) с несколькими мешами, поэтому зомби шагает, качает головой
    /// и ползёт. Шесть зон попадания, у каждой своя прочность и своя смерть:
    /// голова — мгновенно (вскользь — вскрытый череп и текущие мозги), торс — падение,
    /// рука — отрыв, нога — ползёт на оставшейся, без обеих — на руках.
    public class Zombie : MonoBehaviour
    {
        public static readonly List<Zombie> All = new List<Zombie>();

        public event System.Action<string> Died;

        const float WALK_SPEED = 1.6f;
        const float FAST_SPEED = 2.6f;
        const float CRAWL_SPEED = 0.75f;
        const float ATTACK_RANGE = 1.3f;
        const float ATTACK_DAMAGE = 12f;
        const float ATTACK_INTERVAL = 1f;
        const float GRAVITY = 18f;
        const float HUNCH = -0.25f;
        const float ARM_REACH = 1.45f;
        const int MAX_WOUNDS = 8;
        const float MAX_KNOCK = 14f;
        /// Если отдача разогнала зомби сильнее этого (дробовик в упор), он падает навзничь.
        const float KNOCKDOWN_SPEED = 9f;
        const float KNOCKDOWN_TIME = 1.4f;
        /// Насколько далеко от центра головы должна пройти пуля, чтобы задеть её вскользь.
        const float GLANCE_DISTANCE = 0.09f;

        public class ZoneDef
        {
            public Vector3 pivot, size, center;
            public float hp, mult;
        }

        /// pivot — шарнир (у головы и рук — относительно торса), size/center — зона попадания
        /// относительно шарнира. Всё в осях Godot.
        public static readonly Dictionary<string, ZoneDef> Zones = new Dictionary<string, ZoneDef>
        {
            ["torso"] = new ZoneDef { pivot = new Vector3(0f, 0.88f, 0f), size = new Vector3(0.5f, 0.66f, 0.32f), center = new Vector3(0f, 0.32f, 0f), hp = 110f, mult = 1f },
            ["head"] = new ZoneDef { pivot = new Vector3(0f, 0.6f, 0f), size = new Vector3(0.3f, 0.34f, 0.32f), center = new Vector3(0f, 0.16f, -0.02f), hp = 30f, mult = 3f },
            ["arm_l"] = new ZoneDef { pivot = new Vector3(-0.25f, 0.5f, 0f), size = new Vector3(0.15f, 0.72f, 0.15f), center = new Vector3(0f, -0.34f, 0f), hp = 20f, mult = 1f },
            ["arm_r"] = new ZoneDef { pivot = new Vector3(0.25f, 0.5f, 0f), size = new Vector3(0.15f, 0.72f, 0.15f), center = new Vector3(0f, -0.34f, 0f), hp = 20f, mult = 1f },
            ["leg_l"] = new ZoneDef { pivot = new Vector3(-0.12f, 0.88f, 0f), size = new Vector3(0.2f, 0.88f, 0.24f), center = new Vector3(0f, -0.44f, -0.02f), hp = 26f, mult = 1f },
            ["leg_r"] = new ZoneDef { pivot = new Vector3(0.12f, 0.88f, 0f), size = new Vector3(0.2f, 0.88f, 0.24f), center = new Vector3(0f, -0.44f, -0.02f), hp = 26f, mult = 1f },
        };

        static readonly string[] Arms = { "arm_l", "arm_r" };
        static readonly string[] Legs = { "leg_l", "leg_r" };

        public readonly Dictionary<string, float> zoneHp = new Dictionary<string, float>();
        /// Шарниры частей тела по зонам.
        public readonly Dictionary<string, GJoint> parts = new Dictionary<string, GJoint>();
        public GJoint body;
        public bool dead;
        public bool crawling;
        /// Как ползёт: "left" — опирается на левую ногу, "right" — на правую, "arms" — на руках.
        public string crawlStyle = "";
        /// Походка: "walk" — обычная, "limp" — хромает на одну ногу, "fast" — идёт быстро, рывками.
        public string gait = "walk";
        public bool skullOpen;
        public float attackCooldown;
        public float speedJitter = 1f;
        public LevelGenerator level;
        /// Заметил ли игроков. В режиме волн зомби сразу знают, где все; на этажах — стоят, пока не заметят.
        public bool aware = true;

        float groanTimer;
        public float lookTimer;
        public float phase;
        string limpArm = "";
        string badLeg = "leg_r";
        float headTilt;
        int wounds;
        int stumps;
        /// Сколько прошёл с последней отметки на полу и где была прошлая отметка.
        float trailLeft;
        Vector3 trailFrom;
        /// Отдача от попаданий: скорость, с которой зомби отбрасывает назад, и насколько
        /// он отшатнулся (для позы).
        Vector3 knock;
        float stagger;
        /// Сколько ещё лежит после сбивания с ног, и поворот корпуса от попадания.
        public float knockedDown;
        float flinch;
        /// Кого уже сбил, пока летит от удара (чтобы не толкать одного дважды).
        readonly List<Zombie> bumped = new List<Zombie>();
        /// Насколько расслаблены руки (1 — болтаются свободно: летит от удара или лежит).
        float flail;
        readonly Dictionary<string, float[]> armState = new Dictionary<string, float[]>();
        readonly Dictionary<string, Vector2> armPose = new Dictionary<string, Vector2>();
        /// Вращение всего тела вокруг вертикали, пока летит или лежит после удара.
        float spin;
        /// Угол и скорость опрокидывания при падении и направление падения в осях зомби.
        float fall;
        float fallVel;
        Vector2 fallDir = new Vector2(0f, 1f);
        /// Длина рук для расчёта качания (не видимая): у каждой руки своя частота.
        readonly Dictionary<string, float> armLen = new Dictionary<string, float>();
        readonly Dictionary<string, Vector3[]> shoulderPrev = new Dictionary<string, Vector3[]>();
        Material skin;
        CharacterController cc;
        Vector3 velocity;
        float crawlTween = 1f;
        float crawlFrom;
        float crawlTilt;

        public Vector3 Knock => knock;

        public static Zombie Spawn(Vector3 position, float yawDegrees, LevelGenerator level = null, bool aware = true)
        {
            var go = new GameObject("Zombie");
            go.layer = Layers.ZombieBody;
            go.transform.SetParent(GameRoot.World, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            var z = go.AddComponent<Zombie>();
            z.level = level;
            z.aware = aware;
            return z;
        }

        void Awake()
        {
            All.Add(this);
            armLen["arm_l"] = G.Rand(0.28f, 0.42f);
            armLen["arm_r"] = G.Rand(0.28f, 0.42f);
            cc = gameObject.AddComponent<CharacterController>();
            cc.radius = 0.35f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.minMoveDistance = 0f;
            cc.skinWidth = 0.05f;
            cc.stepOffset = 0.3f;

            speedJitter = G.Rand(0.8f, 1.3f);
            groanTimer = G.Rand(0.5f, 4f);
            phase = G.Rand(0f, G.TAU);
            headTilt = G.Rand(-0.35f, 0.35f);
            if (G.Randf() < 0.25f) limpArm = G.Pick(Arms);
            float roll = G.Randf();
            gait = roll < 0.3f ? "limp" : (roll < 0.5f ? "fast" : "walk");
            badLeg = G.Pick(Legs);
            BuildBody();
            trailFrom = transform.position;
        }

        void OnDestroy() { All.Remove(this); }

        /// Переносит в точку мимо CharacterController (иначе он вернёт объект на старое место).
        public void Teleport(Vector3 position)
        {
            cc.enabled = false;
            transform.position = position;
            cc.enabled = true;
        }

        // --- Внешность ------------------------------------------------------------

        void BuildBody()
        {
            body = MeshKit.Joint(transform, Vector3.zero, "Body");
            // Кожа серая, темнее, чем у живых людей, в трупных пятнах.
            skin = MeshKit.Shade(Mathf.Round(G.Rand(0.3f, 0.46f) / 0.04f) * 0.04f);
            var rot = MeshKit.Shade(0.16f);
            var bone = MeshKit.Shade(0.88f, 0.6f);
            // Мутные светящиеся бельма: в темноте видно только их.
            var milky = Milky;
            var shirt = MeshKit.Shade(G.Pick(0.12f, 0.3f, 0.5f, 0.85f));
            var pants = MeshKit.Shade(G.Pick(0.1f, 0.22f, 0.38f));
            var dark = MeshKit.Shade(0.06f);
            var blood = Gore.BloodMaterial;

            var torso = Pivot("torso", body.transform);
            torso.rot.x = HUNCH;
            torso.Apply();
            var t = torso.transform;
            MeshKit.Add(t, MeshKit.Sphere(0.16f), pants, new Vector3(0, 0.03f, 0), default, new Vector3(1.15f, 0.85f, 0.8f));
            MeshKit.Add(t, MeshKit.Sphere(0.16f), shirt, new Vector3(0, 0.2f, 0), default, new Vector3(1.1f, 1.1f, 0.8f));
            MeshKit.Add(t, MeshKit.Sphere(0.2f), shirt, new Vector3(0, 0.42f, 0), default, new Vector3(1.2f, 1.05f, 0.72f));
            MeshKit.Add(t, MeshKit.Capsule(0.07f, 0.5f), shirt, new Vector3(0, 0.53f, 0), new Vector3(0, 0, Mathf.PI / 2f));
            MeshKit.Add(t, MeshKit.Cylinder(0.05f, 0.06f, 0.14f), skin, new Vector3(0, 0.62f, -0.01f));
            // Рваная рубашка: в дырах видна серая кожа, свисает лоскут.
            float side = G.Pick(-1f, 1f);
            MeshKit.Add(t, MeshKit.Sphere(0.08f), skin, new Vector3(0.08f * side, 0.4f, -0.14f), new Vector3(0, 0, 0.6f), new Vector3(1.3f, 0.8f, 0.3f));
            MeshKit.Add(t, MeshKit.Sphere(0.05f), skin, new Vector3(0.02f * side, 0.3f, -0.135f), default, new Vector3(1.2f, 0.8f, 0.3f));
            MeshKit.Add(t, MeshKit.Box(new Vector3(0.13f, 0.16f, 0.012f)), shirt, new Vector3(-0.1f * side, 0.02f, -0.13f), new Vector3(0.1f, 0, 0.25f * side));
            if (G.Randf() < 0.35f)
            {
                // Засохшие подтёки крови на одежде.
                var at = new Vector3(G.Rand(-0.1f, 0.1f), G.Rand(0.3f, 0.45f), -0.145f);
                for (int i = 0; i < 3; i++)
                    MeshKit.Add(t, MeshKit.Sphere(0.03f), blood, at + new Vector3(G.Rand(-0.03f, 0.03f), -i * 0.04f, 0), default, new Vector3(1f - i * 0.2f, 1.4f, 0.25f));
            }
            // Трупные пятна на шее.
            for (int i = G.RandI(1, 2); i > 0; i--)
                MeshKit.Add(t, MeshKit.Sphere(0.025f), rot, new Vector3(G.Rand(-0.04f, 0.04f), G.Rand(0.6f, 0.66f), -0.05f), default, new Vector3(1.2f, 1f, 0.5f));
            if (G.Randf() < 0.3f)
            {
                // Вспоротый живот: кишки свисают из дыры.
                var gut = Gore.BrainMaterial;
                MeshKit.Add(t, MeshKit.Sphere(0.07f), dark, new Vector3(0.03f, 0.17f, -0.125f), default, new Vector3(1.2f, 0.9f, 0.35f));
                for (int i = 0; i < 3; i++)
                    MeshKit.Add(t, MeshKit.Capsule(0.024f, G.Rand(0.14f, 0.26f)), gut, new Vector3(-0.02f + i * 0.03f, 0.08f - i * 0.02f, -0.15f), new Vector3(G.Rand(-0.3f, 0.3f), 0, G.Rand(-0.4f, 0.4f)));
                MeshKit.Add(t, MeshKit.Sphere(0.03f), blood, new Vector3(0.03f, 0.12f, -0.16f), default, new Vector3(1.6f, 1f, 0.4f));
            }

            var head = Pivot("head", t);
            head.rot = new Vector3(0.15f, 0, headTilt);
            head.Apply();
            var h = head.transform;
            MeshKit.Add(h, MeshKit.Sphere(0.115f), skin, new Vector3(0, 0.12f, -0.03f), default, new Vector3(0.9f, 1f, 1f));
            var cap = MeshKit.Add(h, MeshKit.Sphere(0.125f), skin, new Vector3(0, 0.2f, -0.01f), default, new Vector3(0.95f, 0.75f, 1f), "Cap");
            cap.gameObject.AddComponent<PartMeta>().hitSize = new Vector3(0.22f, 0.12f, 0.22f);
            if (G.Randf() < 0.55f)
            {
                var hair = MeshKit.Shade(0.08f);
                for (int i = G.RandI(3, 6); i > 0; i--)
                {
                    float a = G.Rand(0f, G.TAU);
                    MeshKit.Add(cap, MeshKit.Sphere(0.045f), hair, new Vector3(Mathf.Cos(a) * 0.08f, 0.07f, Mathf.Sin(a) * 0.08f + 0.02f));
                }
            }
            // Отвисшая челюсть, тёмный провал рта, запавшие глаза под надбровьем.
            MeshKit.Add(h, MeshKit.Box(new Vector3(0.14f, 0.045f, 0.11f)), MeshKit.Shade(0.5f), new Vector3(0, 0.025f, -0.07f), new Vector3(0.4f, 0, 0.08f));
            MeshKit.Add(h, MeshKit.Sphere(0.04f), dark, new Vector3(0, 0.065f, -0.11f), default, new Vector3(1.3f, 0.7f, 0.5f));
            MeshKit.Add(h, MeshKit.Box(new Vector3(0.025f, 0.045f, 0.03f)), skin, new Vector3(0, 0.125f, -0.14f), new Vector3(-0.2f, 0, 0));
            MeshKit.Add(h, MeshKit.Box(new Vector3(0.17f, 0.025f, 0.04f)), skin, new Vector3(0, 0.2f, -0.12f), new Vector3(0.2f, 0, 0));
            bool lostEye = G.Randf() < 0.4f;
            foreach (float x in new[] { -0.045f, 0.045f })
            {
                if (lostEye && x > 0f)
                {
                    // Выбитый глаз: глубокая тёмная глазница с кровью.
                    MeshKit.Add(h, MeshKit.Sphere(0.032f), dark, new Vector3(x, 0.162f, -0.105f), default, new Vector3(1.1f, 1f, 0.6f));
                    MeshKit.Add(h, MeshKit.Capsule(0.008f, 0.08f), blood, new Vector3(x, 0.12f, -0.122f));
                    continue;
                }
                MeshKit.Add(h, MeshKit.Sphere(0.024f), dark, new Vector3(x, 0.165f, -0.11f), default, new Vector3(1.2f, 0.8f, 0.6f));
                MeshKit.Add(h, MeshKit.Sphere(0.011f), milky, new Vector3(x, 0.165f, -0.124f));
                MeshKit.Add(h, MeshKit.Box(new Vector3(0.02f, 0.055f, 0.04f)), skin, new Vector3(x * 2.55f, 0.14f, -0.01f));
            }
            AddFaceRot(h, rot, bone, dark);

            foreach (var zone in Arms)
            {
                var arm = Pivot(zone, t).transform;
                var sleeve = G.Randf() < 0.6f ? shirt : skin;
                MeshKit.Add(arm, MeshKit.Capsule(0.056f, 0.34f), sleeve, new Vector3(0, -0.15f, 0));
                MeshKit.Add(arm, MeshKit.Capsule(0.048f, 0.32f), skin, new Vector3(0, -0.44f, -0.02f), new Vector3(0.15f, 0, 0));
                MeshKit.Add(arm, MeshKit.Box(new Vector3(0.075f, 0.1f, 0.035f)), skin, new Vector3(0, -0.63f, -0.04f));
                MeshKit.Add(arm, MeshKit.Box(new Vector3(0.065f, 0.07f, 0.02f)), MeshKit.Shade(0.3f), new Vector3(0, -0.7f, -0.06f), new Vector3(0.6f, 0, 0));
                if (G.Randf() < 0.45f)
                    // Пальцы в крови.
                    MeshKit.Add(arm, MeshKit.Box(new Vector3(0.068f, 0.04f, 0.024f)), blood, new Vector3(0, -0.73f, -0.075f), new Vector3(0.6f, 0, 0));
                if (G.Randf() < 0.25f)
                {
                    // Сломанная кость торчит из предплечья.
                    MeshKit.Add(arm, MeshKit.Capsule(0.016f, 0.12f), bone, new Vector3(0.03f, -0.42f, -0.06f), new Vector3(0.9f, 0, 0.5f));
                    MeshKit.Add(arm, MeshKit.Sphere(0.03f), blood, new Vector3(0.02f, -0.42f, -0.04f), default, new Vector3(1, 1, 0.5f));
                }
                MeshKit.Add(arm, MeshKit.Sphere(0.03f), rot, new Vector3(G.Rand(-0.03f, 0.03f), G.Rand(-0.55f, -0.35f), -0.04f), default, new Vector3(1, 1.4f, 0.5f));
            }

            foreach (var zone in Legs)
            {
                var leg = Pivot(zone, body.transform).transform;
                MeshKit.Add(leg, MeshKit.Capsule(0.076f, 0.46f), pants, new Vector3(0, -0.21f, 0));
                MeshKit.Add(leg, MeshKit.Capsule(0.063f, 0.44f), G.Randf() < 0.7f ? pants : skin, new Vector3(0, -0.62f, 0.02f));
                MeshKit.Add(leg, MeshKit.Box(new Vector3(0.1f, 0.07f, 0.23f)), G.Randf() < 0.75f ? dark : skin, new Vector3(0, -0.85f, -0.05f));
            }
        }

        static Material milkyMat;
        static Material Milky
        {
            get
            {
                if (milkyMat == null) milkyMat = MeshKit.Lit(new Color(0.9f, 0.9f, 0.9f), Color.white * 1.5f);
                return milkyMat;
            }
        }

        /// Гниющее лицо: пятна, оскал зубов, кровавая слюна, у кого-то прогнившая щека.
        void AddFaceRot(Transform head, Material rot, Material bone, Material dark)
        {
            for (int i = G.RandI(1, 3); i > 0; i--)
                MeshKit.Add(head, MeshKit.Sphere(0.03f), rot, new Vector3(G.Rand(-0.08f, 0.08f), G.Rand(0.08f, 0.22f), -0.09f), default, new Vector3(1.2f, 1f, 0.4f));
            // Зубы торчат из оскала.
            for (int i = 0; i < 5; i++)
                MeshKit.Add(head, MeshKit.Box(new Vector3(0.014f, 0.022f, 0.012f)), bone, new Vector3(-0.03f + i * 0.015f, 0.082f, -0.128f));
            if (G.Randf() < 0.55f)
                MeshKit.Add(head, MeshKit.Capsule(0.01f, G.Rand(0.08f, 0.16f)), Gore.BloodMaterial, new Vector3(G.Rand(-0.03f, 0.03f), -0.02f, -0.12f));
            if (G.Randf() < 0.35f)
            {
                // Щека прогнила насквозь: видны зубы сбоку.
                float x = G.Pick(-0.07f, 0.07f);
                MeshKit.Add(head, MeshKit.Sphere(0.035f), dark, new Vector3(x, 0.08f, -0.085f), default, new Vector3(0.6f, 1f, 1f));
                for (int i = 0; i < 3; i++)
                    MeshKit.Add(head, MeshKit.Box(new Vector3(0.01f, 0.018f, 0.012f)), bone, new Vector3(x * 1.08f, 0.075f + (i - 1) * 0.004f, -0.1f + i * 0.015f));
            }
        }

        /// Шарнир части тела с зоной попадания.
        GJoint Pivot(string zone, Transform parent)
        {
            var def = Zones[zone];
            var pivot = MeshKit.Joint(parent, def.pivot, zone);
            var meta = pivot.gameObject.AddComponent<PartMeta>();
            meta.hitSize = def.size;
            meta.hitCenter = def.center;
            var area = new GameObject("Hit " + zone);
            area.layer = Layers.Hitbox;
            area.transform.SetParent(pivot.transform, false);
            area.transform.localPosition = G.P(def.center);
            var box = area.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = def.size;
            var hz = area.AddComponent<HitZone>();
            hz.zone = zone;
            hz.zombie = this;
            parts[zone] = pivot;
            zoneHp[zone] = def.hp;
            return pivot;
        }

        // --- Поведение ------------------------------------------------------------

        void Update()
        {
            if (dead) return;
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            attackCooldown -= delta;
            groanTimer -= delta;
            if (groanTimer <= 0f)
            {
                groanTimer = aware ? G.Rand(4f, 9f) : G.Rand(7f, 15f);
                Sfx.Groan(this, "groan", -4f);
            }
            if (!cc.isGrounded) velocity.y -= GRAVITY * delta;
            else if (velocity.y < 0f) velocity.y = -1f;
            velocity.x = knock.x;
            velocity.z = knock.z;
            knock *= Mathf.Exp(-delta * 4.5f);
            BumpOthers();
            stagger = Mathf.Max(0f, stagger - delta * 2f);
            flinch = Mathf.MoveTowards(flinch, 0f, delta * 3f);
            if (knockedDown > 0f)
            {
                // Лежит после удара и встаёт.
                knockedDown -= delta;
                transform.Rotate(0f, -spin * delta * Mathf.Rad2Deg, 0f);
                spin *= Mathf.Exp(-delta * 2f);
                MoveBody(delta);
                Animate(delta, 0f);
                return;
            }
            if (!aware)
            {
                lookTimer -= delta;
                if (lookTimer <= 0f)
                {
                    lookTimer = 0.3f;
                    LookForPlayers();
                }
                MoveBody(delta);
                Animate(delta, 0f);
                return;
            }
            bool moving = false;
            var target = NearestPlayer();
            if (target != null)
            {
                var to = G.Flat(target.transform.position - transform.position);
                float dist = to.magnitude;
                var goal = level == null ? target.transform.position : level.NextWaypoint(transform.position, target.transform.position);
                var step = G.Flat(goal - transform.position);
                if (step.magnitude > 0.05f) transform.rotation = Quaternion.LookRotation(step.normalized, Vector3.up);
                if (dist > ATTACK_RANGE)
                {
                    var dir = step.normalized;
                    // Пока отшатывается от попаданий, вперёд почти не идёт.
                    float speed = MoveSpeed() * (1f - Mathf.Min(stagger, 0.85f));
                    velocity.x += dir.x * speed;
                    velocity.z += dir.z * speed;
                    moving = true;
                }
                else if (attackCooldown <= 0f)
                {
                    attackCooldown = ATTACK_INTERVAL;
                    Sfx.Groan(this, "growl", 0f);
                    target.TakeDamage(AttackDamage());
                }
            }
            MoveBody(delta);
            LeaveTrail();
            Animate(delta, moving ? 1f : 0f);
        }

        void MoveBody(float delta)
        {
            if (cc.enabled) cc.Move(velocity * delta);
        }

        /// Раненый оставляет за собой капли, ползущий — сплошной размазанный след.
        void LeaveTrail()
        {
            if (!(crawling || wounds > 0 || stumps > 0))
            {
                trailFrom = transform.position;
                return;
            }
            var step = G.Flat(transform.position - trailFrom);
            trailLeft -= step.magnitude;
            trailFrom = transform.position;
            if (trailLeft > 0f || step.magnitude < 0.0001f) return;
            if (crawling)
            {
                trailLeft = G.Rand(0.35f, 0.5f);
                Gore.FloorStain(transform.position, "smear", G.Rand(0.28f, 0.36f), step);
            }
            else
            {
                // Чем больше ран, тем чаще капает.
                int bleeding = wounds + stumps * 3;
                trailLeft = G.Rand(1.6f, 2.6f) / Mathf.Sqrt(bleeding);
                Gore.FloorStain(transform.position + new Vector3(G.Rand(-0.2f, 0.2f), 0, G.Rand(-0.2f, 0.2f)), "drops", G.Rand(0.12f, 0.22f), step);
            }
        }

        /// Скорость с рывками: ползущий зомби продвигается толчками ноги или подтягиваясь руками.
        public float MoveSpeed()
        {
            if (!crawling)
            {
                switch (gait)
                {
                    case "fast":
                        return FAST_SPEED * speedJitter;
                    case "limp":
                        // Шаг здоровой ногой — рывок вперёд, больную подтягивает.
                        return WALK_SPEED * 0.8f * speedJitter * (0.35f + 1.1f * Mathf.Max(0f, Mathf.Sin(phase)));
                }
                return WALK_SPEED * speedJitter;
            }
            float surge = crawlStyle == "arms"
                ? 0.25f + 1.2f * Mathf.Abs(Mathf.Cos(phase))
                : 0.35f + 1.3f * Mathf.Max(0f, Mathf.Sin(phase));
            int arms = (parts.ContainsKey("arm_l") ? 1 : 0) + (parts.ContainsKey("arm_r") ? 1 : 0);
            float factor = crawlStyle == "arms" ? 0.75f : 1f;
            if (arms < 2) factor *= 0.7f;
            return CRAWL_SPEED * speedJitter * surge * factor;
        }

        /// Покачивание и шаги: ноги ходят, руки тянутся, голова болтается.
        void Animate(float delta, float walking)
        {
            float rate = (crawling ? 3f : 4.2f) * speedJitter;
            if (gait == "fast" && !crawling) rate *= 1.5f;
            phase += delta * rate * Mathf.Max(walking, 0.25f);
            float s = Mathf.Sin(phase);
            parts.TryGetValue("torso", out var torso);
            parts.TryGetValue("head", out var head);
            if (crawling)
            {
                AnimateCrawl(delta, s);
                ApplyAll();
                return;
            }
            foreach (var zone in Legs)
            {
                if (!parts.TryGetValue(zone, out var leg)) continue;
                float side = zone == "leg_l" ? 1f : -1f;
                float stride = gait == "fast" ? 0.6f : 0.45f;
                if (gait == "limp" && zone == badLeg)
                {
                    // Больная нога почти не сгибается и волочится, носок вывернут.
                    leg.rot = new Vector3(-0.15f + s * 0.12f * side * walking, 0.35f, zone == "leg_l" ? 0.12f : -0.12f);
                    continue;
                }
                leg.rot.x = s * stride * side * walking;
            }
            foreach (var zone in Arms)
            {
                if (!parts.ContainsKey(zone)) continue;
                float reach = aware ? ARM_REACH : 0.25f;
                if (zone == limpArm) reach = 0.15f;
                float sway = Mathf.Sin(phase * 0.5f + (zone == "arm_l" ? 0f : 1.7f)) * 0.12f;
                // Поза, к которой тянут мышцы; саму руку двигает AnimateFlail.
                armPose[zone] = new Vector2(reach + sway, zone == "arm_l" ? 0.08f : -0.08f);
            }
            if (torso != null)
            {
                torso.rot.z = s * 0.07f;
                torso.rot.x = HUNCH - Mathf.Abs(s) * 0.05f;
                if (gait == "fast")
                    torso.rot.x = HUNCH - 0.25f - Mathf.Abs(s) * 0.08f;
                else if (gait == "limp")
                {
                    // Тело заваливается на больную ногу при каждом шаге.
                    float badSide = badLeg == "leg_l" ? -1f : 1f;
                    torso.rot.z = badSide * (0.08f + Mathf.Max(0f, -s) * 0.22f) * Mathf.Max(walking, 0.3f);
                }
            }
            if (head != null) head.rot.z = headTilt + Mathf.Sin(phase * 0.5f) * 0.12f;
            // Отшатывается назад от попадания: корпус откидывается, голова запрокидывается.
            if (torso != null && stagger > 0f)
            {
                torso.rot.x += stagger * 0.9f;
                if (head != null) head.rot.x = 0.15f + stagger * 0.8f;
            }
            if (torso != null) torso.rot.y = flinch;
            ApplyAll();
            AnimateFlail(delta);
            body.pos.y = Mathf.Abs(s) * 0.03f * walking;
            if (gait == "limp") body.pos.y = -Mathf.Max(0f, -s) * 0.06f * walking;
            AnimateFall(delta);
            body.Apply();
        }

        void ApplyAll()
        {
            foreach (var p in parts.Values) p.Apply();
            body.Apply();
        }

        // Падение — опрокидывание тела вокруг ступней, как у палки, поставленной на пол:
        // толчок даёт начальное вращение, ноги тормозят о пол, а верх тела продолжает
        // лететь, дальше заваливает гравитация (чем ниже, тем быстрее). Падает в ту сторону,
        // куда толкнули, ударяется о пол, немного отскакивает и затихает. Потом встаёт.
        const float FALL_COM = 0.95f;
        const float FALL_GROUND = 1.45f;
        const float GET_UP_STIFF = 40f;

        void StartFall()
        {
            // Направление толчка в осях зомби (Godot: вперёд — -Z).
            var d = G.P(transform.InverseTransformDirection(new Vector3(knock.x, 0f, knock.z)));
            d.y = 0f;
            if (d.magnitude < 0.01f) d = new Vector3(0, 0, 1);
            d = d.normalized;
            if (fall < 0.2f) fallDir = new Vector2(d.x, d.z);
            fallVel = Mathf.Max(fallVel, Mathf.Clamp(knock.magnitude * 0.2f, 0.8f, 4f));
        }

        void AnimateFall(float delta)
        {
            if (knockedDown > 0.35f)
            {
                // Гравитация опрокидывает, торможение ног о пол переворачивает верх вперёд по ходу.
                float alpha = 9.8f / FALL_COM * Mathf.Sin(Mathf.Max(fall, 0.05f));
                alpha += 0.5f * 4.5f * knock.magnitude * Mathf.Cos(fall) / FALL_COM;
                fallVel += alpha * delta;
                fall += fallVel * delta;
                if (fall >= FALL_GROUND)
                {
                    fall = FALL_GROUND;
                    if (fallVel > 1.5f)
                    {
                        // Удар о пол: тело подскакивает, скольжение гасится трением.
                        Sfx.PlayAt("splat", transform.position + Vector3.up * 0.2f, -6f, 0.45f);
                        fallVel = -fallVel * 0.28f;
                        knock *= 0.55f;
                        spin *= 0.4f;
                        if (parts.TryGetValue("head", out var head))
                        {
                            head.rot.x += 0.6f;
                            head.Apply();
                        }
                    }
                    else fallVel = 0f;
                }
            }
            else
            {
                // Поднимается: мышцы тянут обратно в вертикаль.
                fallVel += (-GET_UP_STIFF * fall - 2f * Mathf.Sqrt(GET_UP_STIFF) * fallVel) * delta;
                fall += fallVel * delta;
                if (fall < 0f)
                {
                    fall = 0f;
                    fallVel = 0f;
                }
            }
            body.rot.x = fall * fallDir.y;
            body.rot.z = -fall * fallDir.x;
            if (fall > 0.3f) body.pos.y = 0.12f * Mathf.Clamp01(fall / FALL_GROUND);
        }

        // Руки — маятники на плечах. Каждую руку тянет к позе из анимации пружина-мышца,
        // а на неё действуют гравитация и инерция: плечо резко дёрнулось назад —
        // рука по инерции уходит вперёд, тело тормозит — рука догоняет и качается обратно,
        // тело падает навзничь — руки валятся под своим весом. Пока зомби летит от удара
        // или лежит, мышцы почти расслаблены и руки болтаются свободно.
        const float ARM_STIFF = 150f;
        const float ARM_LIMP = 3f;
        const float ARM_MAX_SPIN = 14f;

        void AnimateFlail(float delta)
        {
            float target = Mathf.Clamp01((knock.magnitude - 1.5f) / 5f);
            if (knockedDown > 0f) target = 1f;
            flail = Mathf.MoveTowards(flail, target, delta * (target > flail ? 10f : 1.2f));
            if (delta <= 0f) return;
            var g = new Vector3(0f, -9.8f, 0f);
            foreach (var zone in Arms)
            {
                if (!parts.TryGetValue(zone, out var arm)) continue;
                var shoulder = arm.transform.position;
                var acc = Vector3.zero;
                if (shoulderPrev.TryGetValue(zone, out var prev))
                {
                    var vel = (shoulder - prev[0]) / delta;
                    acc = Vector3.ClampMagnitude((vel - prev[1]) / delta, 400f);
                    shoulderPrev[zone] = new[] { shoulder, vel };
                }
                else shoulderPrev[zone] = new[] { shoulder, Vector3.zero };
                // Гравитация с учётом ускорения плеча, в осях родителя (корпуса).
                var parent = arm.transform.parent;
                var ge = G.P(parent.InverseTransformDirection(g - acc));
                var gs = G.P(parent.InverseTransformDirection(g));
                if (!armState.TryGetValue(zone, out var st)) st = new[] { arm.rot.x, 0f, arm.rot.z, 0f };
                var pose = armPose.TryGetValue(zone, out var p) ? p : new Vector2(arm.rot.x, arm.rot.z);
                float k = Mathf.Lerp(ARM_STIFF, ARM_LIMP, flail);
                float c = Mathf.Lerp(2f * Mathf.Sqrt(ARM_STIFF), 0.7f, flail);
                float hold = 1f - flail;
                // Вперёд-назад: рука (0, -cos, -sin) после поворота на угол вокруг X.
                float len = armLen[zone];
                float ax = TorqueX(st[0], ge) / len + k * (pose.x - st[0]) - c * st[1] - hold * TorqueX(pose.x, gs) / len;
                // Вбок: рука (sin, -cos, 0) после поворота вокруг Z.
                // Поднятая вперёд рука качается вбок слабее (короче плечо силы) — качания
                // вперёд и вбок связаны, и движение получается неповторяющимся.
                float lever = Mathf.Max(0.2f, Mathf.Abs(Mathf.Cos(st[0])));
                float az = TorqueZ(st[2], ge) * lever / len + k * (pose.y - st[2]) - c * st[3] - hold * TorqueZ(pose.y, gs) / len;
                st[1] = Mathf.Clamp(st[1] + ax * delta, -ARM_MAX_SPIN, ARM_MAX_SPIN);
                st[3] = Mathf.Clamp(st[3] + az * delta, -ARM_MAX_SPIN, ARM_MAX_SPIN);
                st[0] += st[1] * delta;
                st[2] += st[3] * delta;
                // Сустав не выворачивается: упор гасит скорость.
                var limX = new Vector2(-1f, 3.1f);
                var limZ = zone == "arm_l" ? new Vector2(-1.5f, 0.25f) : new Vector2(-0.25f, 1.5f);
                if (st[0] < limX.x || st[0] > limX.y)
                {
                    st[0] = Mathf.Clamp(st[0], limX.x, limX.y);
                    st[1] *= -0.2f;
                }
                if (st[2] < limZ.x || st[2] > limZ.y)
                {
                    st[2] = Mathf.Clamp(st[2], limZ.x, limZ.y);
                    st[3] *= -0.2f;
                }
                armState[zone] = st;
                arm.rot.x = st[0];
                arm.rot.z = st[2];
                arm.Apply();
            }
        }

        static float TorqueX(float angle, Vector3 ge) => -Mathf.Cos(angle) * ge.z + Mathf.Sin(angle) * ge.y;
        static float TorqueZ(float angle, Vector3 ge) => Mathf.Sin(angle) * ge.y + Mathf.Cos(angle) * ge.x;

        void AnimateCrawl(float delta, float s)
        {
            var torso = parts["torso"];
            switch (crawlStyle)
            {
                case "arms":
                    // Лежит на животе и подтягивается руками по очереди: одна тянется вперёд,
                    // другая упирается и тащит тело.
                    if (parts.TryGetValue("arm_l", out var al)) al.rot = new Vector3(2.35f + 0.45f * s, 0, 0.25f);
                    if (parts.TryGetValue("arm_r", out var ar)) ar.rot = new Vector3(2.35f - 0.45f * s, 0, -0.25f);
                    torso.rot = new Vector3(0.25f + Mathf.Abs(s) * 0.06f, s * 0.1f, 0);
                    body.pos.y = 0.1f;
                    break;
                default:
                {
                    // На четвереньках без одной ноги: опирается на уцелевшую, тело заваливается
                    // на её сторону, руки загребают по очереди.
                    string legName = crawlStyle == "left" ? "leg_l" : "leg_r";
                    if (parts.TryGetValue(legName, out var leg)) leg.rot = new Vector3(0.15f + 0.2f * s, 0, 0);
                    foreach (var zone in Arms)
                    {
                        if (!parts.TryGetValue(zone, out var arm)) continue;
                        float ph = zone == "arm_l" ? s : -s;
                        arm.rot = new Vector3(2.3f + 0.35f * ph, 0, zone == "arm_l" ? 0.12f : -0.12f);
                    }
                    float lean = crawlStyle == "left" ? 0.3f : -0.3f;
                    torso.rot = new Vector3(0.1f, lean + s * 0.06f, 0);
                    body.pos.y = 0.16f + Mathf.Max(0f, s) * 0.04f;
                    break;
                }
            }
            if (parts.TryGetValue("head", out var head))
            {
                head.rot.x = -0.8f;
                head.rot.z = headTilt * 0.5f;
            }
            // Падение на землю в начале ползания: тело валится вперёд и подпрыгивает.
            if (crawlTween < 1f)
            {
                crawlTween = Mathf.Min(1f, crawlTween + delta / 0.35f);
                body.rot.x = Mathf.LerpUnclamped(crawlFrom, crawlTilt, G.BounceOut(crawlTween));
            }
        }

        /// Зомби услышал выстрел или его задели: идёт к игрокам.
        public void Alert()
        {
            if (aware || dead) return;
            aware = true;
            groanTimer = G.Rand(0f, 0.6f);
        }

        /// Замечает игрока рядом или в прямой видимости.
        void LookForPlayers()
        {
            var eye = transform.position + Vector3.up * 1.6f;
            foreach (var p in Player.All)
            {
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < 3f)
                {
                    Alert();
                    return;
                }
                if (d < 11f && !Physics.Linecast(eye, p.transform.position + Vector3.up * 1.5f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                {
                    Alert();
                    return;
                }
            }
        }

        // --- Попадания ------------------------------------------------------------

        /// Попадание пули. Возвращает true, если зомби умер.
        public bool TakeHit(string zone, float damage, Vector3 point, Vector3 dir, bool loud = true, float force = 1f)
        {
            if (dead || !zoneHp.ContainsKey(zone)) return false;
            Alert();
            Push(GlanceDir(point, dir), force);
            Gore.Splash(point, dir, 16);
            if (zone == "head" && !skullOpen && IsGlancing(point, dir))
            {
                OpenSkull(point, dir);
                return false;
            }
            zoneHp[zone] -= damage * Zones[zone].mult;
            if (zoneHp[zone] > 0f)
            {
                AddWound(zone, point, dir);
                if (loud)
                {
                    Sfx.PlayAt("splat", point, -8f);
                    Sfx.Groan(this, "pain", -2f);
                }
                return false;
            }
            switch (zone)
            {
                case "head": DieHeadshot(point, dir); break;
                case "torso": DieBody(point, dir); break;
                default: Sever(zone, point, dir); break;
            }
            return dead;
        }

        // Летящий от удара зомби своим телом сбивает тех, в кого врезался, и они летят дальше.
        const float BUMP_SPEED = 5f;
        const float BUMP_RADIUS = 0.85f;
        const float BUMP_KEEP = 0.85f;

        void BumpOthers()
        {
            float speed = knock.magnitude;
            if (speed < BUMP_SPEED)
            {
                bumped.Clear();
                return;
            }
            var dir = knock / speed;
            foreach (var other in All.ToArray())
            {
                if (other == this || other == null || other.dead || bumped.Contains(other)) continue;
                var to = G.Flat(other.transform.position - transform.position);
                if (to.magnitude > BUMP_RADIUS || Vector3.Dot(to, dir) < 0f) continue;
                bumped.Add(other);
                other.bumped.Add(this);
                // Как бильярдные шары: сбитый летит по линии между центрами, ударивший
                // отскакивает в другую сторону. Чем больше вскользь, тем сильнее разлёт.
                var line = to.normalized;
                var pushDir = (line * 0.75f + dir * 0.25f).normalized;
                other.Push(pushDir, speed * BUMP_KEEP);
                var deflect = dir - line * Vector3.Dot(dir, line);
                knock = (dir * 0.4f + deflect * 1.2f).normalized * speed * 0.55f;
                Sfx.PlayAt("splat", other.transform.position + Vector3.up, -6f, 0.7f);
                Sfx.Groan(other, "pain", -2f);
                speed = knock.magnitude;
                if (speed < BUMP_SPEED) return;
            }
        }

        /// Направление толчка от пули: попадание сбоку от центра тела уводит зомби вбок.
        Vector3 GlanceDir(Vector3 point, Vector3 dir)
        {
            var flat = G.Flat(dir).normalized;
            var off = G.Flat(point - transform.position);
            var side = off - flat * Vector3.Dot(off, flat);
            float amount = Mathf.Clamp01(side.magnitude / 0.3f) * 0.6f;
            if (side.magnitude < 0.001f) return flat;
            return (flat + side.normalized * amount).normalized;
        }

        /// Удар ногой: сильный толчок с падением навзничь и немного урона корпусу.
        public void Kicked(Vector3 dir, float speed, float damage)
        {
            if (dead) return;
            Alert();
            bumped.Clear();
            Push(dir, speed);
            // Удар ногой сильнее предела отдачи от пуль.
            var flat = G.Flat(dir).normalized;
            knock = flat * speed * (crawling ? 0.4f : 1f);
            if (!crawling)
            {
                knockedDown = KNOCKDOWN_TIME;
                StartFall();
            }
            if (zoneHp.ContainsKey("torso")) zoneHp["torso"] = Mathf.Max(1f, zoneHp["torso"] - damage);
            Sfx.Groan(this, "pain", 0f);
        }

        /// Отдача: попадание толкает зомби по направлению выстрела. Лежачего — слабее.
        public void Push(Vector3 dir, float force)
        {
            var flat = G.Flat(dir).normalized;
            knock += flat * force * (crawling ? 0.4f : 1f);
            knock = Vector3.ClampMagnitude(knock, MAX_KNOCK);
            stagger = Mathf.Min(1f, stagger + force * 0.25f);
            flinch = Mathf.Clamp(flinch + G.Rand(-0.25f, 0.25f) * force, -0.6f, 0.6f);
            if (force > 4f)
            {
                // Сильный толчок закручивает тело и дёргает каждую руку по-своему:
                // дальше их раскачивают инерция, вращение и гравитация.
                spin += G.Rand(-0.35f, 0.35f) * force;
                foreach (var st in armState.Values)
                {
                    st[1] += G.Rand(-0.6f, 0.6f) * force;
                    st[3] += G.Rand(-0.45f, 0.45f) * force;
                }
            }
            if (!crawling && knockedDown <= 0f && knock.magnitude >= KNOCKDOWN_SPEED)
            {
                knockedDown = KNOCKDOWN_TIME;
                StartFall();
            }
        }

        /// Центр головы в мире.
        public Vector3 HeadCenter()
        {
            return parts["head"].transform.TransformPoint(G.P(Zones["head"].center));
        }

        /// Пуля прошла по краю головы, а не в лоб.
        public bool IsGlancing(Vector3 point, Vector3 dir)
        {
            if (!parts.ContainsKey("head")) return false;
            var toCenter = HeadCenter() - point;
            var d = dir.normalized;
            var miss = toCenter - d * Vector3.Dot(toCenter, d);
            return miss.magnitude > GLANCE_DISTANCE;
        }

        /// Рана: кровавое пятно на теле и струйка крови, бьющая из него несколько секунд.
        void AddWound(string zone, Vector3 point, Vector3 dir)
        {
            if (wounds >= MAX_WOUNDS || !parts.TryGetValue(zone, out var part)) return;
            wounds++;
            var local = G.P(part.transform.InverseTransformPoint(point));
            MeshKit.Add(part.transform, MeshKit.Sphere(0.035f), Gore.BloodMaterial, local, default, null, "Wound");
            var outDir = (local - Zones[zone].center).normalized;
            if (outDir.magnitude < 0.5f) outDir = G.P(part.transform.InverseTransformDirection(-dir));
            Gore.Bleed(part.transform, local, outDir, G.Rand(2.5f, 4f));
        }

        /// Скользящее попадание в голову: верх черепа слетает, мозги вываливаются и стекают.
        /// Голова держится на честном слове: следующее попадание в неё убивает.
        void OpenSkull(Vector3 point, Vector3 dir)
        {
            skullOpen = true;
            zoneHp["head"] = Mathf.Min(zoneHp["head"], 1f);
            var head = parts["head"].transform;
            var cap = head.Find("Cap");
            var top = new Vector3(0, 0.2f, -0.01f);
            if (cap != null)
            {
                top = G.P(cap.localPosition);
                Gore.Detach(cap, dir * 2.5f + Vector3.up * 3f);
                Kill(cap);
            }
            Gore.Brains(head, top);
            Gore.Burst(point, dir + Vector3.up, 20);
            Sfx.PlayAt("crunch", point, -2f, 1.25f);
            Sfx.Groan(this, "pain", 0f);
            speedJitter *= 0.8f;
            headTilt = 0.5f * Mathf.Sign(headTilt != 0f ? headTilt : 1f);
        }

        void Sever(string zone, Vector3 point, Vector3 dir)
        {
            var part = parts[zone];
            Gore.Detach(part.transform, dir * 3f + knock + Vector3.up * 2f);
            Gore.Burst(part.transform.position, dir, 25);
            Sfx.PlayAt("crunch", point, 0f);
            Sfx.Groan(this, "pain", 0f);
            RemovePart(zone);
            if (zone.StartsWith("leg")) StartCrawl();
            if (!(parts.ContainsKey("arm_l") || parts.ContainsKey("arm_r") || parts.ContainsKey("leg_l") || parts.ContainsKey("leg_r")))
                DieBody(point, dir);
        }

        void DieHeadshot(Vector3 point, Vector3 dir)
        {
            dead = true;
            var head = parts["head"].transform;
            var neck = head.position;
            Gore.Detach(head, dir * 6f + knock + Vector3.up * 4f);
            RemovePart("head");
            Gore.Burst(neck, Vector3.up, 45);
            Sfx.PlayAt("crunch", point, 3f, 0.8f);
            Died?.Invoke("head");
            Gore.HitStop(0.06f);
            Collapse(dir * 1.5f + knock * 1.2f);
        }

        void DieBody(Vector3 point, Vector3 dir)
        {
            dead = true;
            Gore.Burst(point, dir, 35);
            Sfx.PlayAt("crunch", point, 0f, 0.9f);
            Died?.Invoke("torso");
            Gore.HitStop(0.04f);
            Collapse(dir * 3f + knock * 1.5f);
        }

        /// Тело разваливается: каждая часть отлетает отдельным физическим куском.
        void Collapse(Vector3 impulse)
        {
            foreach (var zone in new[] { "head", "arm_l", "arm_r", "leg_l", "leg_r", "torso" })
            {
                if (!parts.TryGetValue(zone, out var part)) continue;
                Gore.Detach(part.transform, impulse + new Vector3(G.Rand(-1f, 1f), G.Rand(0f, 1.5f), G.Rand(-1f, 1f)));
                Kill(part.transform);
            }
            parts.Clear();
            cc.enabled = false;
            Destroy(gameObject);
        }

        /// Ползёт: на уцелевшей ноге или, если ног не осталось, на руках.
        void StartCrawl()
        {
            knockedDown = 0f;
            fall = 0f;
            fallVel = 0f;
            bool hasL = parts.ContainsKey("leg_l");
            bool hasR = parts.ContainsKey("leg_r");
            crawlStyle = !(hasL || hasR) ? "arms" : (hasL ? "left" : "right");
            crawling = true;
            crawlTilt = crawlStyle == "arms" ? -1.5f : -1.3f;
            crawlFrom = body.rot.x;
            crawlTween = 0f;
            body.rot.z = 0f;
            body.Apply();
            cc.height = 0.8f;
            cc.center = new Vector3(0f, 0.4f, 0f);
        }

        /// Убирает часть со сцены сразу, чтобы следующая копия тела её уже не включала.
        static void Kill(Transform t)
        {
            t.gameObject.SetActive(false);
            t.SetParent(null, false);
            Destroy(t.gameObject);
        }

        void RemovePart(string zone)
        {
            var part = parts[zone];
            var holder = part.transform.parent;
            var at = part.pos;
            parts.Remove(zone);
            zoneHp.Remove(zone);
            Kill(part.transform);
            // Красный обрубок, из которого ещё какое-то время хлещет кровь.
            MeshKit.Add(holder, MeshKit.Sphere(0.09f), Gore.BloodMaterial, at, default, new Vector3(1, 0.7f, 1), "Stump_" + zone);
            var outDir = zone == "head" ? Vector3.up : (zone.StartsWith("leg") ? Vector3.down : new Vector3(Mathf.Sign(at.x), 0, 0));
            Gore.Bleed(holder, at, outDir, 5f, 2f);
            stumps++;
        }

        float AttackDamage()
        {
            int arms = (parts.ContainsKey("arm_l") ? 1 : 0) + (parts.ContainsKey("arm_r") ? 1 : 0);
            return ATTACK_DAMAGE * Mathf.Max(0.25f, arms / 2f);
        }

        Player NearestPlayer()
        {
            Player best = null;
            float bestDist = float.PositiveInfinity;
            foreach (var p in Player.All)
            {
                if (p.downed) continue;
                float d = (transform.position - p.transform.position).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }
    }
}
