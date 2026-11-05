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
    /// шарнир (шея, плечо, бедро, локоть, колено) с несколькими мешами, поэтому зомби шагает,
    /// качает головой, падает на колени и ползёт. Шесть зон попадания, у каждой своя прочность:
    /// голова (вскользь или разбитая — вскрытый череп и текущие мозги), торс, руки (сначала
    /// отрывается предплечье, потом плечо), ноги — ползёт на оставшейся, без обеих — на руках.
    /// Убитый падает целиком и лежит трупом, труп дёргается от пуль и разваливается на куски.
    public class Zombie : MonoBehaviour
    {
        /// Живые зомби.
        public static readonly List<Zombie> All = new List<Zombie>();
        /// Лежащие трупы (самые старые убираются).
        public static readonly List<Zombie> Corpses = new List<Zombie>();

        public event System.Action<string> Died;

        /// Насколько тише вскрики от попаданий и ударов (Олег попросил их приглушить). Мычание и рык атаки не меняются.
        const float CRY_DB = -6f;
        const float WALK_SPEED = 1.6f;
        const float FAST_SPEED = 2.6f;
        const float CRAWL_SPEED = 0.75f;
        const float ATTACK_RANGE = 1.3f;
        const float ATTACK_DAMAGE = 12f;
        const float ATTACK_INTERVAL = 1f;
        const float GRAVITY = 18f;
        const float HUNCH = -0.25f;
        const float ARM_REACH = 1.45f;
        const int MAX_WOUNDS = 22;
        /// Сколько ран на части тела, после которых одежда в этом месте пропитывается кровью.
        const int SOAK_EVERY = 3;
        /// Локоть: где у руки шарнир предплечья (по оси руки от плеча).
        const float ELBOW_Y = -0.3f;
        /// Когда у руки остаётся меньше этой доли прочности, пули отрывают предплечье с кистью.
        const float FOREARM_TEAR = 0.6f;
        const float MAX_KNOCK = 14f;
        /// Если отдача разогнала зомби сильнее этого (дробовик в упор), он падает навзничь.
        const float KNOCKDOWN_SPEED = 9f;
        const float KNOCKDOWN_TIME = 1.4f;
        /// Насколько далеко от центра головы должна пройти пуля, чтобы задеть её вскользь.
        const float GLANCE_DISTANCE = 0.09f;
        /// На сколько опускается тело, когда зомби стоит на коленях, и где колено в осях тела.
        const float KNEEL_SINK = 0.4f;
        static readonly Vector3 KNEE_POS = new Vector3(0f, 0.46f, 0f);
        /// Сколько трупов лежит на уровне одновременно: самые старые убираются.
        const int MAX_CORPSES = 30;
        /// Прочность трупа: сколько ещё нужно всадить, чтобы он развалился на куски.
        const float CORPSE_HP = 160f;
        /// Ниже этой высоты зомби считается падающим за край этажа.
        const float FALL_TRIGGER_Y = -1.5f;
        /// Минимальная скорость вылета наружу, м/с.
        const float FALL_OUT_SPEED = 2f;
        /// Ниже этой высоты упавший исчезает.
        const float FALL_DESPAWN_Y = -30f;

        public class ZoneDef
        {
            public Vector3 pivot, size, center;
            public float hp, mult;
        }

        /// pivot — шарнир (у головы и рук — относительно торса), size/center — зона попадания
        /// относительно шарнира. Всё в осях Godot.
        public static readonly Dictionary<string, ZoneDef> Zones = new Dictionary<string, ZoneDef>
        {
            ["torso"] = new ZoneDef { pivot = new Vector3(0f, 0.88f, 0f), size = new Vector3(0.5f, 0.66f, 0.32f), center = new Vector3(0f, 0.32f, 0f), hp = 260f, mult = 1f },
            ["head"] = new ZoneDef { pivot = new Vector3(0f, 0.6f, 0f), size = new Vector3(0.3f, 0.34f, 0.32f), center = new Vector3(0f, 0.16f, -0.02f), hp = 110f, mult = 1.5f },
            ["arm_l"] = new ZoneDef { pivot = new Vector3(-0.25f, 0.5f, 0f), size = new Vector3(0.15f, 0.72f, 0.15f), center = new Vector3(0f, -0.34f, 0f), hp = 70f, mult = 1f },
            ["arm_r"] = new ZoneDef { pivot = new Vector3(0.25f, 0.5f, 0f), size = new Vector3(0.15f, 0.72f, 0.15f), center = new Vector3(0f, -0.34f, 0f), hp = 70f, mult = 1f },
            ["leg_l"] = new ZoneDef { pivot = new Vector3(-0.12f, 0.88f, 0f), size = new Vector3(0.2f, 0.88f, 0.24f), center = new Vector3(0f, -0.44f, -0.02f), hp = 90f, mult = 1f },
            ["leg_r"] = new ZoneDef { pivot = new Vector3(0.12f, 0.88f, 0f), size = new Vector3(0.2f, 0.88f, 0.24f), center = new Vector3(0f, -0.44f, -0.02f), hp = 90f, mult = 1f },
        };

        static readonly string[] Arms = { "arm_l", "arm_r" };
        static readonly string[] Legs = { "leg_l", "leg_r" };

        public readonly Dictionary<string, float> zoneHp = new Dictionary<string, float>();
        /// Шарниры частей тела по зонам.
        public readonly Dictionary<string, GJoint> parts = new Dictionary<string, GJoint>();
        public GJoint body;
        public bool dead;
        public bool crawling;
        /// Как ползёт: "knees" — на коленях и четвереньках, "left" — опирается на левую ногу,
        /// "right" — на правую, "arms" — на руках.
        public string crawlStyle = "";
        /// Походка: "walk" — обычная, "limp" — хромает на одну ногу, "fast" — идёт быстро, рывками.
        public string gait = "walk";
        public bool skullOpen;
        public float attackCooldown;
        public float speedJitter = 1f;
        /// Высота голоса: у каждого зомби свой, от низкого хрипа до сиплого крика.
        public float voice = 1f;
        public LevelGenerator level;
        /// Заметил ли игроков. В режиме волн зомби сразу знают, где все; на этажах — стоят, пока не заметят.
        public bool aware = true;
        /// Вылетел из окна и падает: тело больше ни с чем не сталкивается.
        public bool falling;
        /// Игрок, который последним ударил или ударил ногой: ему засчитывается падение.
        public Player lastAttacker;

        float groanTimer;
        public float lookTimer;
        public float phase;
        string limpArm = "";
        string badLeg = "leg_r";
        float headTilt;
        public int wounds;
        /// Сколько ран на каждой части тела (для пропитывания одежды кровью).
        public readonly Dictionary<string, int> partWounds = new Dictionary<string, int>();
        /// Насколько разворочена каждая часть тела (0 — нет, 1 — сильно, 2 — в клочья).
        readonly Dictionary<string, int> goreStage = new Dictionary<string, int>();
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
        /// Коленные суставы: голень с ботинком сгибается относительно бедра.
        public readonly Dictionary<string, GJoint> knees = new Dictionary<string, GJoint>();
        /// Пружины рывков от попаданий: отклонение корпуса (наклон, поворот, крен) и головы
        /// (кивок, крен). Пружины мягкие, поэтому тело дёргается, перелетает и качается обратно.
        public Vector3 jerk, jerkV;
        public Vector2 hjerk, hjerkV;
        /// Стоит на коленях после ранения: сколько ещё, прежде чем опуститься на руки и поползти.
        float kneel;
        /// Насколько опустилось тело (подогнулись колени).
        float sink;
        /// Смерть: "buckle" — подгибаются колени, "topple" — валится, "slump" — ползущий
        /// обмякает, "lying" — лежит трупом.
        public string deathStage = "";
        float deathTimer;
        /// Вокруг какой точки (в осях тела) опрокидывается: ступни или колени.
        Vector3 fallPivot;
        Vector3 torsoRest;
        public bool landed;
        /// Середина лежащего трупа.
        public Vector3 corpseCenter;

        Vector3 fallOutDir = Vector3.forward;
        float fallSpin = 3f;
        Vector3 fallAxis = Vector3.right;

        public Vector3 Knock => knock;
        public float Fall => fall;

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
            voice = G.Rand(0.78f, 1.05f);
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

        void OnDestroy()
        {
            All.Remove(this);
            Corpses.Remove(this);
        }

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
                // Предплечье с кистью на локте: пули отрывают его раньше, чем всю руку.
                var elbowJoint = MeshKit.Joint(arm, new Vector3(0, ELBOW_Y, 0), "Elbow");
                var elbowMeta = elbowJoint.gameObject.AddComponent<PartMeta>();
                elbowMeta.hitSize = new Vector3(0.12f, 0.42f, 0.12f);
                elbowMeta.hitCenter = new Vector3(0, -0.2f, -0.02f);
                var elbow = elbowJoint.transform;
                MeshKit.Add(elbow, MeshKit.Capsule(0.048f, 0.32f), skin, new Vector3(0, -0.14f, -0.02f), new Vector3(0.15f, 0, 0));
                MeshKit.Add(elbow, MeshKit.Box(new Vector3(0.075f, 0.1f, 0.035f)), skin, new Vector3(0, -0.33f, -0.04f));
                MeshKit.Add(elbow, MeshKit.Box(new Vector3(0.065f, 0.07f, 0.02f)), MeshKit.Shade(0.3f), new Vector3(0, -0.4f, -0.06f), new Vector3(0.6f, 0, 0));
                if (G.Randf() < 0.45f)
                    // Пальцы в крови.
                    MeshKit.Add(elbow, MeshKit.Box(new Vector3(0.068f, 0.04f, 0.024f)), blood, new Vector3(0, -0.43f, -0.075f), new Vector3(0.6f, 0, 0));
                if (G.Randf() < 0.25f)
                {
                    // Сломанная кость торчит из предплечья.
                    MeshKit.Add(elbow, MeshKit.Capsule(0.016f, 0.12f), bone, new Vector3(0.03f, -0.12f, -0.06f), new Vector3(0.9f, 0, 0.5f));
                    MeshKit.Add(elbow, MeshKit.Sphere(0.03f), blood, new Vector3(0.02f, -0.12f, -0.04f), default, new Vector3(1, 1, 0.5f));
                }
                MeshKit.Add(elbow, MeshKit.Sphere(0.03f), rot, new Vector3(G.Rand(-0.03f, 0.03f), G.Rand(-0.25f, -0.05f), -0.04f), default, new Vector3(1, 1.4f, 0.5f));
            }

            foreach (var zone in Legs)
            {
                var leg = Pivot(zone, body.transform).transform;
                MeshKit.Add(leg, MeshKit.Capsule(0.076f, 0.46f), pants, new Vector3(0, -0.21f, 0));
                // Голень на коленном суставе: зомби может упасть на колени и ползти на четвереньках.
                var knee = MeshKit.Joint(leg, new Vector3(0, -0.42f, 0), "Knee");
                MeshKit.Add(knee.transform, MeshKit.Capsule(0.063f, 0.44f), G.Randf() < 0.7f ? pants : skin, new Vector3(0, -0.2f, 0.02f));
                MeshKit.Add(knee.transform, MeshKit.Box(new Vector3(0.1f, 0.07f, 0.23f)), G.Randf() < 0.75f ? dark : skin, new Vector3(0, -0.43f, -0.05f));
                knees[zone] = knee;
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
            float delta = Time.deltaTime;
            if (delta <= 0f) return;
            // Ушёл под этаж без окна (например, труп соскользнул с края): тоже падение.
            if (!falling && transform.position.y < FALL_TRIGGER_Y)
            {
                var away = G.Flat(knock);
                FallOut(away.magnitude > 0.1f ? away : Vector3.forward);
            }
            if (falling)
            {
                FallOutProcess(delta);
                return;
            }
            if (dead)
            {
                CorpseProcess(delta);
                return;
            }
            attackCooldown -= delta;
            groanTimer -= delta;
            if (groanTimer <= 0f)
            {
                groanTimer = aware ? G.Rand(4f, 9f) : G.Rand(7f, 15f);
                // Бродячее мычание — прежнее, новые вскрики звучат только от попаданий.
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
                KeepAboveFloor();
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
                KeepAboveFloor();
                return;
            }
            if (kneel > 0f) kneel -= delta;
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
                    // Пока отшатывается от попаданий, вперёд почти не идёт; стоя на коленях — стоит.
                    float speed = MoveSpeed() * (1f - Mathf.Min(stagger, 0.85f));
                    if (kneel > 0f) speed = 0f;
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
            KeepAboveFloor();
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
            float surge;
            if (crawlStyle == "knees")
                // На четвереньках: шаг рукой и коленом — рывок, между шагами почти стоит.
                surge = 0.4f + 1.1f * Mathf.Abs(Mathf.Sin(phase));
            else if (crawlStyle == "arms")
                surge = 0.25f + 1.2f * Mathf.Abs(Mathf.Cos(phase));
            else
                surge = 0.35f + 1.3f * Mathf.Max(0f, Mathf.Sin(phase));
            return CRAWL_SPEED * speedJitter * surge * CrawlFactor();
        }

        /// Во сколько раз медленнее обычного ползёт. На коленях и четвереньках — вдвое медленнее
        /// прежнего, на одной ноге — ещё медленнее, без ног — на руках, и чем меньше от рук
        /// осталось, тем медленнее: обрубок без предплечья почти не тянет.
        public float CrawlFactor()
        {
            float factor = 0.7f;
            if (crawlStyle == "arms") factor = 0.36f;
            else if (crawlStyle == "left" || crawlStyle == "right") factor = 0.6f;
            return factor * (0.25f + 0.375f * ArmStrength());
        }

        /// Сколько рук осталось: целая рука — 1, обрубок без предплечья — 0,4.
        float ArmStrength()
        {
            float total = 0f;
            foreach (var zone in Arms)
                if (parts.TryGetValue(zone, out var arm))
                    total += arm.transform.Find("Elbow") != null ? 1f : 0.4f;
            return total;
        }

        /// Насколько тело прижато к полу: 0 — все руки целы, 1 — почти ничего не осталось.
        /// Без ног и рук зомби волочится плашмя, голову от пола почти не поднимает.
        public float Low() => Mathf.Clamp01((2f - ArmStrength()) / 1.6f);

        /// Покачивание и шаги: ноги ходят, руки тянутся, голова болтается.
        void Animate(float delta, float walking)
        {
            float rate = (crawling ? 3f * (0.55f + 0.45f * CrawlFactor()) : 4.2f) * speedJitter;
            if (gait == "fast" && !crawling) rate *= 1.5f;
            phase += delta * rate * Mathf.Max(walking, 0.25f);
            float s = Mathf.Sin(phase);
            parts.TryGetValue("torso", out var torso);
            parts.TryGetValue("head", out var head);
            UpdateJerk(delta);
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
            // Отшатывается от попадания: корпус дёргается туда, куда толкнула пуля, перелетает
            // и качается обратно, голова мотается отдельно.
            if (torso != null)
            {
                torso.rot.x += stagger * 0.35f + jerk.x;
                torso.rot.z += jerk.z;
                torso.rot.y = flinch + jerk.y;
            }
            if (head != null)
            {
                head.rot.x = 0.15f + stagger * 0.3f + hjerk.x;
                head.rot.z += hjerk.y;
            }
            foreach (var k in knees.Values) k.rot.x = 0f;
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
            foreach (var k in knees.Values) k.Apply();
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
            if (knockedDown > 0f || dead) target = 1f;
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
            if (crawlStyle != "knees")
            {
                float tilt = crawlStyle == "arms" ? -1.5f : -1.45f;
                body.rot.x = Mathf.MoveTowards(body.rot.x, tilt, delta * 6f);
            }
            switch (crawlStyle)
            {
                case "knees":
                    AnimateKnees(s, delta);
                    return;
                case "arms":
                {
                    // Лежит на животе и подтягивается руками по очереди: одна тянется вперёд,
                    // другая упирается и тащит тело.
                    // Чем меньше осталось от рук, тем ниже: живот и грудь волочатся по полу,
                    // обрубки скребут, а не подтягивают.
                    float low = Low();
                    if (parts.TryGetValue("arm_l", out var al)) al.rot = new Vector3(Mathf.Lerp(2.35f, 2f, low) + 0.45f * s, 0, 0.25f);
                    if (parts.TryGetValue("arm_r", out var ar)) ar.rot = new Vector3(Mathf.Lerp(2.35f, 2f, low) - 0.45f * s, 0, -0.25f);
                    torso.rot = new Vector3(Mathf.Lerp(0.25f, 0.04f, low) + Mathf.Abs(s) * Mathf.Lerp(0.06f, 0.02f, low), s * Mathf.Lerp(0.1f, 0.16f, low), 0);
                    body.pos.y = Mathf.Lerp(0.1f, 0.03f, low);
                    break;
                }
                default:
                {
                    // Без одной ноги: лежит на животе, завалившись на бок уцелевшей ноги,
                    // подтягивается руками по очереди и отталкивается коленом.
                    string legName = crawlStyle == "left" ? "leg_l" : "leg_r";
                    if (parts.TryGetValue(legName, out var leg)) leg.rot = new Vector3(0.1f + 0.18f * s, 0, 0);
                    float low = Low();
                    foreach (var zone in Arms)
                    {
                        if (!parts.TryGetValue(zone, out var arm)) continue;
                        float ph = zone == "arm_l" ? s : -s;
                        arm.rot = new Vector3(Mathf.Lerp(2.35f, 2f, low) + 0.45f * ph, 0, zone == "arm_l" ? 0.25f : -0.25f);
                    }
                    float lean = crawlStyle == "left" ? 0.2f : -0.2f;
                    torso.rot = new Vector3(Mathf.Lerp(0.06f, 0f, low) + Mathf.Abs(s) * 0.04f, lean + s * 0.06f, 0);
                    body.pos.y = Mathf.Lerp(0.06f, 0.03f, low) + Mathf.Max(0f, s) * 0.02f;
                    break;
                }
            }
            if (parts.TryGetValue("head", out var head))
            {
                head.rot.x = Mathf.Lerp(-0.8f, -0.35f, Low()) + hjerk.x;
                head.rot.z = headTilt * 0.5f + hjerk.y;
            }
            torso.rot += jerk * 0.6f;
            string legLeft = crawlStyle == "left" ? "leg_l" : "leg_r";
            if (knees.TryGetValue(legLeft, out var kn)) kn.rot.x = -0.35f - 0.25f * s;
        }

        /// На коленях, потом на четвереньках. Сначала тело оседает (подгибаются колени),
        /// зомби покачивается, стоя на коленях, руки висят. Потом валится вперёд на руки
        /// и ползёт: рука и противоположное колено шагают вместе, спина ходит вверх-вниз.
        void AnimateKnees(float s, float delta)
        {
            var torso = parts["torso"];
            sink = Mathf.MoveTowards(sink, KNEEL_SINK, delta * 2.6f);
            float bend = sink / KNEEL_SINK;
            // 1 — стоит на коленях прямо, 0 — на четвереньках.
            float up = G.Smoothstep(0f, 0.45f, kneel);
            float down = 1f - up;
            body.rot = Vector3.zero;
            body.pos = new Vector3(0, -sink + down * Mathf.Abs(s) * 0.03f, 0);
            foreach (var zone in Legs)
            {
                if (!parts.TryGetValue(zone, out var leg)) continue;
                float side = zone == "leg_l" ? 1f : -1f;
                leg.rot = new Vector3(down * (0.15f + 0.22f * s * side), 0, 0.05f * side);
                if (knees.TryGetValue(zone, out var kn)) kn.rot.x = -1.5f * bend + down * 0.18f * s * side;
            }
            float sway = Mathf.Sin(phase * 0.7f);
            torso.rot = new Vector3(Mathf.Lerp(HUNCH - 0.1f, -1.08f, down) + jerk.x, jerk.y + down * s * 0.08f, up * sway * 0.14f + jerk.z);
            foreach (var zone in Arms)
            {
                if (!parts.TryGetValue(zone, out var arm)) continue;
                float side = zone == "arm_l" ? 1f : -1f;
                float hang = 0.25f + sway * 0.1f * side;
                arm.rot = new Vector3(Mathf.Lerp(hang, 1.08f - 0.38f * s * side, down), 0, 0.1f * side);
            }
            if (parts.TryGetValue("head", out var head))
            {
                head.rot.x = Mathf.Lerp(0.25f + sway * 0.1f, 0.95f, down) + hjerk.x;
                head.rot.z = headTilt + hjerk.y;
            }
        }

        /// Зомби услышал выстрел или его задели: идёт к игрокам.
        public void Alert()
        {
            if (aware || dead) return;
            aware = true;
            groanTimer = G.Rand(1.5f, 3f);
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
        public bool TakeHit(string zone, float damage, Vector3 point, Vector3 dir, bool loud = true, float force = 1f, Player attacker = null)
        {
            if (dead || !zoneHp.ContainsKey(zone)) return false;
            if (attacker != null) lastAttacker = attacker;
            Alert();
            Push(GlanceDir(point, dir), force);
            Jolt(zone, point, dir, force);
            Gore.Splash(point, dir, 24);
            Gore.Chunks(point, dir, G.RandI(2, 5) + (zone == "head" ? 3 : 0));
            if (zone == "head" && !skullOpen && IsGlancing(point, dir))
            {
                OpenSkull(point, dir);
                return false;
            }
            var def = Zones[zone];
            zoneHp[zone] -= damage * def.mult;
            if (zoneHp[zone] > 0f)
            {
                AddWound(zone, point, dir);
                if (zone.StartsWith("arm") && zoneHp[zone] < def.hp * FOREARM_TEAR) TearForearm(zone, dir);
                // Разбитая пулями голова раскалывается: верх черепа слетает, но зомби ещё жив.
                if (zone == "head" && !skullOpen && zoneHp["head"] < def.hp * 0.5f) OpenSkull(point, dir);
                if (loud)
                {
                    HitSound(zone, point);
                    // Почти каждое попадание — вскрик: короткий «ах», сдавленный крик, рёв боли,
                    // бульканье кровью (голова, корпус).
                    if (G.Randf() < 0.8f)
                    {
                        var cries = zone == "head" || zone == "torso"
                            ? new[] { "pain", "yelp", "gurgle", "snarl", "yelp" }
                            : new[] { "pain", "yelp", "snarl", "yelp" };
                        Say(G.Pick(cries), -2f + CRY_DB);
                    }
                }
                // Простреленная нога или изрешечённый корпус: подгибаются колени,
                // зомби падает на колени и дальше ползёт на четвереньках.
                if (zone.StartsWith("leg") && G.Randf() < 0.6f) StartKneel();
                else if (zone == "torso" && zoneHp[zone] < def.hp * 0.45f && G.Randf() < 0.5f) StartKneel();
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
                if (lastAttacker != null) other.lastAttacker = lastAttacker;
                // Как бильярдные шары: сбитый летит по линии между центрами, ударивший
                // отскакивает в другую сторону. Чем больше вскользь, тем сильнее разлёт.
                var line = to.normalized;
                var pushDir = (line * 0.75f + dir * 0.25f).normalized;
                other.Push(pushDir, speed * BUMP_KEEP);
                var deflect = dir - line * Vector3.Dot(dir, line);
                knock = (dir * 0.4f + deflect * 1.2f).normalized * speed * 0.55f;
                Sfx.PlayAt("splat", other.transform.position + Vector3.up, -6f, 0.7f);
                other.Say(G.Pick("pain", "yelp"), -2f + CRY_DB);
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
        public void Kicked(Vector3 dir, float speed, float damage, Player attacker = null)
        {
            if (dead) return;
            if (attacker != null) lastAttacker = attacker;
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
            Say(G.Pick("pain", "yelp", "growl"), CRY_DB);
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

        // --- Пол, рывки, колени ----------------------------------------------------

        /// Пол непроницаем: ни одна часть тела ползущего не уходит под него. Если корпус
        /// или голова ушли вниз, всё тело приподнимается; если рука или нога — сустав
        /// поворачивается ровно настолько, чтобы она легла на пол, а не сквозь него.
        const float FLOOR_GAP = 0.005f;

        public void KeepAboveFloor()
        {
            if (body == null || falling) return;
            if (!(crawling || dead || knockedDown > 0f || fall > 0.05f))
            {
                // Идущий: руки до пола не достают, проверить только ступни — если ушли
                // в пол, приподнять всё тело.
                if (!cc.isGrounded) return;
                float standY = transform.position.y + FLOOR_GAP;
                float feet = float.PositiveInfinity;
                foreach (var zone in Legs)
                    if (parts.TryGetValue(zone, out var leg)) feet = Mathf.Min(feet, Lowest(leg.transform));
                if (feet < standY)
                {
                    body.pos.y += standY - feet;
                    body.Apply();
                }
                return;
            }
            float floorY = FloorHeight() + FLOOR_GAP;
            if (float.IsNegativeInfinity(floorY)) return;
            if (!parts.TryGetValue("torso", out var torso)) return;
            float core = Lowest(torso.transform, true);
            if (parts.TryGetValue("head", out var head)) core = Mathf.Min(core, Lowest(head.transform));
            if (core < floorY)
            {
                body.pos.y += floorY - core;
                body.Apply();
            }
            foreach (var zone in Limbs)
            {
                if (!parts.TryGetValue(zone, out var limb)) continue;
                if (knees.TryGetValue(zone, out var knee)) LiftJoint(knee, knee.transform, floorY);
                float before = limb.rot.x;
                LiftJoint(limb, limb.transform, floorY);
                if (armState.TryGetValue(zone, out var st) && limb.rot.x != before)
                {
                    // Рука-маятник упёрлась в пол: дальше качается от пола, а не сквозь него.
                    st[0] = limb.rot.x;
                    st[1] *= -0.2f;
                }
            }
            // Если сустав не смог вывести конечность из пола (упёрлась), поднять всё тело.
            float low = float.PositiveInfinity;
            foreach (var zone in Limbs)
                if (parts.TryGetValue(zone, out var limb)) low = Mathf.Min(low, Lowest(limb.transform));
            if (low < floorY)
            {
                body.pos.y += floorY - low;
                body.Apply();
            }
        }

        static readonly string[] Limbs = { "arm_l", "arm_r", "leg_l", "leg_r" };

        /// Высота пола под зомби (луч вниз только по стенам и полу уровня).
        float FloorHeight()
        {
            var from = transform.position + Vector3.up * 0.6f;
            if (!Physics.Raycast(from, Vector3.down, out var hit, 1.6f, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                return float.NegativeInfinity;
            return hit.point.y;
        }

        /// Поворачивает сустав вокруг его оси x на наименьший угол, при котором всё,
        /// что на нём висит, оказывается над полом.
        static readonly float[] LiftSteps = { 0.03f, 0.06f, 0.1f, 0.16f, 0.25f, 0.4f, 0.6f, 0.9f };

        void LiftJoint(GJoint joint, Transform subtree, float floorY)
        {
            float start = joint.rot.x;
            if (Lowest(subtree) >= floorY) return;
            float best = start;
            float bestY = float.NegativeInfinity;
            foreach (float step in LiftSteps)
            {
                foreach (float sign in new[] { 1f, -1f })
                {
                    joint.rot.x = start + step * sign;
                    joint.Apply();
                    float y = Lowest(subtree);
                    if (y >= floorY)
                    {
                        // Уточнить: между прежним углом и найденным.
                        float lo = 0f, hi = step;
                        for (int i = 0; i < 5; i++)
                        {
                            float mid = (lo + hi) / 2f;
                            joint.rot.x = start + mid * sign;
                            joint.Apply();
                            if (Lowest(subtree) >= floorY) hi = mid;
                            else lo = mid;
                        }
                        joint.rot.x = start + hi * sign;
                        joint.Apply();
                        return;
                    }
                    if (y > bestY)
                    {
                        bestY = y;
                        best = joint.rot.x;
                    }
                }
            }
            joint.rot.x = best;
            joint.Apply();
        }

        /// Самая низкая точка мешей части тела в мире. ownOnly — без дочерних суставов
        /// (у корпуса это голова и руки).
        public float Lowest(Transform node, bool ownOnly = false)
        {
            float y = float.PositiveInfinity;
            foreach (Transform child in node)
            {
                if (!child.gameObject.activeSelf) continue;
                var mf = child.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    y = Mathf.Min(y, MeshBottom(child, mf.sharedMesh));
                    if (child.childCount > 0) y = Mathf.Min(y, Lowest(child));
                }
                else
                {
                    if (ownOnly && IsPart(child)) continue;
                    y = Mathf.Min(y, Lowest(child));
                }
            }
            return y;
        }

        bool IsPart(Transform t)
        {
            foreach (var p in parts.Values)
                if (p.transform == t) return true;
            return false;
        }

        static float MeshBottom(Transform t, Mesh mesh)
        {
            if (mesh == null) return float.PositiveInfinity;
            if (!MeshKit.Shapes.TryGetValue(mesh, out var shape))
            {
                var r = t.GetComponent<Renderer>();
                return r != null ? r.bounds.min.y : float.PositiveInfinity;
            }
            var m = t.localToWorldMatrix;
            Vector3 ex = m.GetColumn(0), ey = m.GetColumn(1), ez = m.GetColumn(2);
            float oy = m.m13;
            // Насколько оси меша вытянуты по вертикали мира.
            var up = new Vector3(ex.y, ey.y, ez.y);
            switch (shape.kind)
            {
                case ShapeKind.Sphere:
                    return oy - shape.radius * up.magnitude;
                case ShapeKind.Capsule:
                {
                    float half = Mathf.Max(0f, shape.height / 2f - shape.radius);
                    return oy - Mathf.Abs(ey.y) * half - shape.radius * up.magnitude;
                }
                case ShapeKind.Cylinder:
                {
                    float r = Mathf.Max(shape.top, shape.bottom);
                    return oy - Mathf.Abs(ey.y) * shape.height / 2f - r * new Vector2(ex.y, ez.y).magnitude;
                }
                default:
                    return oy - (Mathf.Abs(ex.y) * shape.size.x + Mathf.Abs(ey.y) * shape.size.y + Mathf.Abs(ez.y) * shape.size.z) / 2f;
            }
        }

        // Пружины рывков: тянут отклонение обратно к нулю, но слабо затухают — тело
        // перелетает положение равновесия и пару раз качается.
        const float JERK_STIFF = 120f;
        const float JERK_DAMP = 9f;
        const float HEAD_STIFF = 150f;
        const float HEAD_DAMP = 8f;

        void UpdateJerk(float delta)
        {
            jerkV += (-JERK_STIFF * jerk - JERK_DAMP * jerkV) * delta;
            jerk += jerkV * delta;
            jerk = new Vector3(Mathf.Clamp(jerk.x, -1.2f, 1f), Mathf.Clamp(jerk.y, -0.9f, 0.9f), Mathf.Clamp(jerk.z, -0.8f, 0.8f));
            hjerkV += (-HEAD_STIFF * hjerk - HEAD_DAMP * hjerkV) * delta;
            hjerk += hjerkV * delta;
            hjerk = new Vector2(Mathf.Clamp(hjerk.x, -1f, 1.3f), Mathf.Clamp(hjerk.y, -0.9f, 0.9f));
        }

        /// Рывок от пули: куда и как дёрнется тело, зависит от зоны и точки попадания.
        /// В грудь — корпус откидывается назад, голова запрокидывается; в живот — сгибается
        /// пополам; в бок — разворачивает и кренит; в голову — голову резко отбрасывает;
        /// в ногу — заваливается вперёд на раненую ногу; в руку — руку отшвыривает.
        void Jolt(string zone, Vector3 point, Vector3 dir, float force)
        {
            // Направление и точка в осях зомби (как в Godot-версии: вперёд — -Z).
            var ld = G.P(transform.InverseTransformDirection(dir.normalized));
            float f = Mathf.Clamp(force, 0.6f, 8f);
            var lp = G.P(transform.InverseTransformPoint(point));
            switch (zone)
            {
                case "head":
                    hjerkV += new Vector2(ld.z * 4.5f, -ld.x * 3f + G.Rand(-1.5f, 1.5f)) * f;
                    jerkV += new Vector3(ld.z * 1f, 0, -ld.x * 0.6f) * f;
                    break;
                case "torso":
                {
                    bool belly = parts.TryGetValue("torso", out var torso) && torso.transform.InverseTransformPoint(point).y < 0.22f;
                    if (belly && G.Randf() < 0.7f)
                    {
                        // В живот: сгибается пополам, голова падает вперёд.
                        jerkV.x -= Mathf.Abs(ld.z) * 2.6f * f;
                        hjerkV.x -= 1.6f * f;
                    }
                    else
                    {
                        jerkV.x += ld.z * 2.8f * f;
                        hjerkV.x += ld.z * 2.4f * f;
                    }
                    jerkV.z += -ld.x * 1.6f * f;
                    jerkV.y += Mathf.Clamp(-lp.x * 6f, -1.5f, 1.5f) * Mathf.Sign(Mathf.Abs(ld.z) > 0.1f ? ld.z : 1f) * f;
                    break;
                }
                case "leg_l":
                case "leg_r":
                {
                    float side = zone == "leg_l" ? 1f : -1f;
                    jerkV += new Vector3(-1.6f, 0, side * 1.4f) * f;
                    hjerkV.x -= 1f * f;
                    break;
                }
                case "arm_l":
                case "arm_r":
                    if (armState.TryGetValue(zone, out var st))
                    {
                        st[1] -= ld.z * 3f * f;
                        st[3] += -ld.x * 2f * f;
                    }
                    jerkV.y += (zone == "arm_l" ? 0.8f : -0.8f) * ld.z * f;
                    break;
            }
            jerkV = Vector3.ClampMagnitude(jerkV, 16f);
            hjerkV = Vector2.ClampMagnitude(hjerkV, 18f);
        }

        /// Подогнулись колени: зомби оседает на колени, покачивается и опускается на руки.
        public void StartKneel()
        {
            if (crawling || dead || knockedDown > 0f || fall > 0.05f) return;
            crawling = true;
            crawlStyle = "knees";
            kneel = G.Rand(0.7f, 1.4f);
            stagger = 0f;
            armState.Clear();
            cc.height = 1.1f;
            cc.center = new Vector3(0f, 0.55f, 0f);
            Say("howl", -1f + CRY_DB);
        }

        /// Голос этого зомби: звук со своей высотой.
        void Say(string sound, float volumeDb = -2f)
        {
            Sfx.Groan(this, sound, volumeDb, voice);
        }

        // --- Раны -----------------------------------------------------------------

        /// Рваная рана, а не пятно: чёрная дыра, вокруг неровный край из клочьев крови и мяса,
        /// по телу веером брызги в сторону полёта пули, вниз тянется потёк. Пуля в корпус или
        /// голову часто выходит насквозь — со спины рана больше, с вырванным мясом.
        /// Каждые несколько ран на части тела пропитывают кровью ещё кусок одежды, а сильно
        /// изрешечённая часть тела разворочена: рёбра наружу, кость из руки или ноги.
        /// Здесь все точки и направления — в осях Unity узла-держателя.
        void AddWound(string zone, Vector3 point, Vector3 dir)
        {
            if (!parts.TryGetValue(zone, out var partJoint)) return;
            var part = partJoint.transform;
            partWounds[zone] = (partWounds.TryGetValue(zone, out var n) ? n : 0) + 1;
            if (partWounds[zone] % SOAK_EVERY == 0) Soak(part);
            AddGoreStage(zone);
            if (wounds >= MAX_WOUNDS) return;
            wounds++;
            var local = part.InverseTransformPoint(point);
            var localDir = part.InverseTransformDirection(dir).normalized;
            // Зона попадания — коробка, а тело круглое: рану кладём точно на видимую
            // поверхность и крепим к тому суставу, который двигается (предплечье, голень),
            // иначе при сгибе рана остаётся висеть в воздухе.
            var hit = SurfaceHit(part, local - localDir * 0.6f, localDir);
            if (hit == null)
            {
                // Пуля чиркнула по краю зоны мимо тела: рану кладём туда, где тело ближе всего.
                var toCenter = G.P(Zones[zone].center) - local;
                if (toCenter.magnitude > 0.01f)
                {
                    toCenter = toCenter.normalized;
                    hit = SurfaceHit(part, local - toCenter * 0.6f, toCenter);
                }
            }
            if (hit == null) return;
            var h = hit.Value;
            var holder = h.holder;
            var dirH = holder.InverseTransformDirection(dir).normalized;
            float big = zone == "torso" ? 1.6f : (zone == "head" ? 1.3f : 1.1f);
            WoundMesh(holder, h.pos, h.normal, dirH, big, false, h.radius);
            if (G.Randf() < 0.6f)
            {
                // Потёк вниз от раны, прижат к телу.
                var down = holder.InverseTransformDirection(Vector3.down);
                var slide = down - h.normal * Vector3.Dot(down, h.normal);
                if (slide.magnitude > 0.2f)
                {
                    slide = slide.normalized;
                    float length = G.Rand(0.08f, 0.16f);
                    float reach = length * 0.5f;
                    var at = h.pos + slide * reach - h.normal * (reach * reach / (2f * h.radius) - 0.004f);
                    var run = AddLocal(holder, MeshKit.Capsule(0.01f, length), Gore.BloodMaterial, at,
                        Quaternion.LookRotation(h.normal, slide), new Vector3(1f, 0.2f, 0.35f));
                    ScaleTo.Run(run, Vector3.one, G.Rand(1.5f, 3f));
                }
            }
            if ((zone == "torso" || zone == "head") && G.Randf() < 0.65f)
            {
                // Выходное отверстие на другой стороне: больше и рванее.
                var back = SurfaceHit(part, local + localDir * 0.8f, -localDir);
                if (back != null && back.Value.holder == holder)
                    WoundMesh(holder, back.Value.pos, back.Value.normal, dirH, big * 1.5f, true, back.Value.radius);
            }
            if (wounds <= 12 || G.Randf() < 0.3f)
                Gore.Bleed(holder, G.P(h.pos), G.P(h.normal), G.Rand(2.5f, 4f));
        }

        public struct SurfacePoint
        {
            public Transform holder;
            public Vector3 pos, normal;
            public float radius;
        }

        /// Где луч (в осях части тела) впервые входит в видимое тело: шары, капсулы, коробки
        /// из которых собрана часть. Возвращает сустав-держатель, точку и нормаль в его осях
        /// и радиус кривизны поверхности (чтобы прижать края раны к круглому телу).
        public SurfacePoint? SurfaceHit(Transform part, Vector3 from, Vector3 dir)
        {
            SurfacePoint? best = null;
            float bestT = float.PositiveInfinity;
            var invPart = part.worldToLocalMatrix;
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>())
            {
                if (!IsBodyMesh(mf.transform, part, out var shape)) continue;
                var xf = invPart * mf.transform.localToWorldMatrix;
                var inv = xf.inverse;
                var o = inv.MultiplyPoint3x4(from);
                var d = inv.MultiplyVector(dir);
                if (!RayShape(shape, o, d, out float t, out var nrm, out float r) || t >= bestT) continue;
                bestT = t;
                var n = inv.transpose.MultiplyVector(nrm).normalized;
                var holder = mf.transform.parent;
                var hx = invPart * holder.localToWorldMatrix;
                var hinv = hx.inverse;
                var hp = hinv.MultiplyPoint3x4(from + dir * t);
                var hn = hx.transpose.MultiplyVector(n).normalized;
                float sc = Mathf.Min(((Vector3)xf.GetColumn(0)).magnitude, Mathf.Min(((Vector3)xf.GetColumn(1)).magnitude, ((Vector3)xf.GetColumn(2)).magnitude));
                best = new SurfacePoint { holder = holder, pos = hp, normal = hn, radius = Mathf.Max(0.04f, r * sc) };
            }
            return best;
        }

        static bool IsBodyMesh(Transform mi, Transform part, out ShapeInfo shape)
        {
            shape = default;
            var mf = mi.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !MeshKit.Shapes.TryGetValue(mf.sharedMesh, out shape)) return false;
            if (shape.kind == ShapeKind.Cylinder) return false;
            var n = mi.parent;
            while (n != null && n != part)
            {
                if (n.name == "Wound") return false;
                n = n.parent;
            }
            return n == part;
        }

        /// Пересечение луча o + d·t с фигурой меша в его собственных осях.
        /// Возвращает t, нормаль n и радиус кривизны r.
        static bool RayShape(ShapeInfo m, Vector3 o, Vector3 d, out float t, out Vector3 n, out float r)
        {
            t = -1f;
            n = Vector3.zero;
            r = 1f;
            switch (m.kind)
            {
                case ShapeKind.Sphere:
                    t = RaySphere(o, d, Vector3.zero, m.radius);
                    if (t < 0f) return false;
                    n = (o + d * t).normalized;
                    r = m.radius;
                    return true;
                case ShapeKind.Capsule:
                {
                    float rad = m.radius;
                    float h = Mathf.Max(m.height * 0.5f - rad, 0f);
                    float best = -1f;
                    // Боковая стенка: бесконечный цилиндр вдоль Y, только между полусферами.
                    float a = d.x * d.x + d.z * d.z;
                    if (a > 1e-9f)
                    {
                        float b = o.x * d.x + o.z * d.z;
                        float c = o.x * o.x + o.z * o.z - rad * rad;
                        float disc = b * b - a * c;
                        if (disc >= 0f)
                        {
                            float tc = (-b - Mathf.Sqrt(disc)) / a;
                            if (tc > 0f && Mathf.Abs(o.y + d.y * tc) <= h) best = tc;
                        }
                    }
                    foreach (float cy in new[] { h, -h })
                    {
                        float ts = RaySphere(o, d, new Vector3(0, cy, 0), rad);
                        if (ts > 0f && (best < 0f || ts < best)) best = ts;
                    }
                    if (best < 0f) return false;
                    var p = o + d * best;
                    t = best;
                    n = (p - new Vector3(0, Mathf.Clamp(p.y, -h, h), 0)).normalized;
                    r = rad;
                    return true;
                }
                case ShapeKind.Box:
                {
                    var half = m.size * 0.5f;
                    float t0 = float.NegativeInfinity, t1 = float.PositiveInfinity;
                    int axis = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        if (Mathf.Abs(d[k]) < 1e-9f)
                        {
                            if (Mathf.Abs(o[k]) > half[k]) return false;
                            continue;
                        }
                        float ta = (-half[k] - o[k]) / d[k];
                        float tb = (half[k] - o[k]) / d[k];
                        if (ta > tb)
                        {
                            float tmp = ta;
                            ta = tb;
                            tb = tmp;
                        }
                        if (ta > t0)
                        {
                            t0 = ta;
                            axis = k;
                        }
                        t1 = Mathf.Min(t1, tb);
                    }
                    if (t0 > t1 || t0 <= 0f) return false;
                    t = t0;
                    n[axis] = -Mathf.Sign(d[axis]);
                    r = 1f;
                    return true;
                }
            }
            return false;
        }

        static float RaySphere(Vector3 o, Vector3 d, Vector3 c, float r)
        {
            var oc = o - c;
            float a = Vector3.Dot(d, d);
            float b = Vector3.Dot(oc, d);
            float cc = Vector3.Dot(oc, oc) - r * r;
            float disc = b * b - a * cc;
            if (disc < 0f) return -1f;
            float t = (-b - Mathf.Sqrt(disc)) / a;
            return t > 0f ? t : -1f;
        }

        /// Меш в осях Unity узла (раны строятся прямо в его осях).
        static Transform AddLocal(Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var t = MeshKit.Add(parent, mesh, mat, Vector3.zero);
            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = scale;
            return t;
        }

        static Quaternion Yaw(float radians) => Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.up);

        /// Сама рана в осях узла: at — точка на поверхности, outDir — наружу, along — полёт пули.
        void WoundMesh(Transform part, Vector3 at, Vector3 outDir, Vector3 along, float size, bool exit, float curve = 0.15f)
        {
            var w = new GameObject("Wound").transform;
            w.gameObject.layer = part.gameObject.layer;
            w.SetParent(part, false);
            w.localPosition = at;
            w.localRotation = Quaternion.FromToRotation(Vector3.up, outDir) * Yaw(G.Rand(0f, G.TAU));
            // Дыра: чёрная глубина, вокруг тёмное мясо.
            AddLocal(w, MeshKit.Sphere(0.02f * size), Gore.HoleMaterial, new Vector3(0, 0.006f, 0), Quaternion.identity, new Vector3(1f, 0.35f, G.Rand(1f, 1.5f)));
            AddLocal(w, MeshKit.Sphere(0.032f * size), Gore.DarkBloodMaterial, Vector3.zero, Quaternion.identity, new Vector3(1.2f, 0.3f, 1f));
            // Рваный край: клочья разного размера и формы.
            int shreds = G.RandI(5, 8) + (exit ? 3 : 0);
            for (int i = 0; i < shreds; i++)
            {
                float a = G.TAU * i / shreds + G.Rand(-0.3f, 0.3f);
                float r = G.Rand(0.024f, 0.045f) * size;
                var mat = G.Pick(Gore.BloodMaterial, Gore.DarkBloodMaterial, Gore.MeatMaterial);
                AddLocal(w, MeshKit.Sphere(G.Rand(0.01f, 0.022f) * size), mat,
                    new Vector3(Mathf.Cos(a) * r, G.Rand(0f, 0.006f) - Sag(r, curve), Mathf.Sin(a) * r), Yaw(-a),
                    new Vector3(G.Rand(0.6f, 1.8f), 0.35f, G.Rand(0.5f, 1.4f)));
            }
            // Мясо торчит наружу (из выходной раны — больше).
            int meat = G.RandI(1, 2) + (exit ? 3 : 0);
            for (int i = 0; i < meat; i++)
            {
                float a = G.Rand(0f, G.TAU);
                float r = G.Rand(0f, 0.03f) * size;
                AddLocal(w, MeshKit.Box(new Vector3(G.Rand(0.012f, 0.03f), G.Rand(0.01f, 0.03f), G.Rand(0.012f, 0.03f)) * size), Gore.MeatMaterial,
                    new Vector3(Mathf.Cos(a) * r, G.Rand(0.004f, 0.014f) * size - Sag(r, curve), Mathf.Sin(a) * r),
                    Quaternion.Euler(G.Rand(-0.8f, 0.8f) * Mathf.Rad2Deg, G.Rand(0f, 360f), G.Rand(-0.8f, 0.8f) * Mathf.Rad2Deg), Vector3.one);
            }
            // Брызги на коже рядом с раной в сторону полёта пули. Лежат плоско и прижаты
            // к изгибу тела: далеко от раны не уходят, чтобы не торчать в воздухе.
            var tv = Quaternion.Inverse(w.localRotation) * along;
            tv.y = 0f;
            var tangent = tv.magnitude > 0.05f ? tv.normalized : Vector3.right;
            int drops = G.RandI(2, 4);
            for (int i = 0; i < drops; i++)
            {
                var d = exit ? Yaw(G.Rand(0f, G.TAU)) * Vector3.right : Yaw(G.Rand(-0.7f, 0.7f)) * tangent;
                float dist = Mathf.Min(G.Rand(0.045f, 0.08f) * size, curve * 0.6f);
                AddLocal(w, MeshKit.Sphere(G.Rand(0.006f, 0.011f) * size), Gore.BloodMaterial,
                    d * dist + new Vector3(0, 0.001f - Sag(dist, curve), 0), Yaw(Mathf.Atan2(d.x, d.z)),
                    new Vector3(1f, 0.2f, G.Rand(1.4f, 2.2f)));
            }
            w.localScale = Vector3.one * 0.4f;
            ScaleTo.Run(w, Vector3.one, 0.12f);
        }

        /// На сколько опустить деталь, отстоящую от центра раны на r, чтобы она легла на круглое тело.
        static float Sag(float r, float curve) => r * r / (2f * curve);

        /// Сильно изрешечённая часть тела разворочена. Ступени по оставшейся прочности.
        /// Координаты здесь — в осях Godot-версии, как при сборке тела.
        void AddGoreStage(string zone)
        {
            if (!zoneHp.ContainsKey(zone) || !parts.TryGetValue(zone, out var partJoint)) return;
            float frac = zoneHp[zone] / Zones[zone].hp;
            int stage = 0;
            if (frac < 0.55f) stage = 1;
            if (frac < 0.25f) stage = 2;
            int had = goreStage.TryGetValue(zone, out var g) ? g : 0;
            if (stage <= had) return;
            goreStage[zone] = stage;
            var part = partJoint.transform;
            switch (zone)
            {
                case "torso":
                    if (stage >= 1 && had < 1)
                    {
                        // Грудь разворочена: тёмная яма, в ней рёбра.
                        float side = G.Pick(-1f, 1f);
                        var at = new Vector3(0.08f * side, 0.36f, -0.14f);
                        MeshKit.Add(part, MeshKit.Sphere(0.09f), Gore.DarkBloodMaterial, at, default, new Vector3(1f, 1.1f, 0.35f));
                        MeshKit.Add(part, MeshKit.Sphere(0.06f), Gore.HoleMaterial, at + new Vector3(0, 0, -0.012f), default, new Vector3(1f, 1f, 0.3f));
                        for (int i = 0; i < 3; i++)
                            MeshKit.Add(part, MeshKit.Capsule(0.012f, 0.13f), Gore.BoneMaterial, at + new Vector3(0, 0.05f - i * 0.045f, -0.025f), new Vector3(0, 0, Mathf.PI / 2f + G.Rand(-0.2f, 0.2f)));
                        for (int i = 0; i < 4; i++)
                            MeshKit.Add(part, MeshKit.Box(new Vector3(0.03f, 0.025f, 0.03f)), Gore.MeatMaterial, at + new Vector3(G.Rand(-0.07f, 0.07f), G.Rand(-0.07f, 0.07f), -0.03f), new Vector3(G.Randf(), G.Randf(), G.Randf()));
                    }
                    if (stage >= 2)
                    {
                        // Живот вспорот: кишки свисают.
                        MeshKit.Add(part, MeshKit.Sphere(0.08f), Gore.HoleMaterial, new Vector3(-0.02f, 0.12f, -0.13f), default, new Vector3(1.3f, 0.9f, 0.3f));
                        for (int i = 0; i < 4; i++)
                        {
                            var gut = MeshKit.Add(part, MeshKit.Capsule(0.026f, G.Rand(0.18f, 0.34f)), Gore.MeatMaterial, new Vector3(-0.06f + i * 0.035f, 0.03f - i * 0.02f, -0.16f), new Vector3(G.Rand(-0.4f, 0.4f), 0, G.Rand(-0.5f, 0.5f)), new Vector3(1f, 0.2f, 1f));
                            ScaleTo.Run(gut, Vector3.one, G.Rand(0.6f, 1.4f));
                        }
                    }
                    break;
                case "arm_l":
                case "arm_r":
                case "leg_l":
                case "leg_r":
                {
                    // Кость перебита и торчит из мяса.
                    var holder = part;
                    float y = zone.StartsWith("arm") ? G.Rand(-0.35f, -0.2f) : G.Rand(-0.35f, -0.15f);
                    if (zone.StartsWith("arm") && part.Find("Elbow") == null) y = G.Rand(-0.25f, -0.1f);
                    if (zone.StartsWith("leg") && knees.TryGetValue(zone, out var knee) && stage >= 2)
                    {
                        holder = knee.transform;
                        y = G.Rand(-0.25f, -0.1f);
                    }
                    var at = new Vector3(0, y, -0.06f);
                    MeshKit.Add(holder, MeshKit.Sphere(0.05f), Gore.DarkBloodMaterial, at + new Vector3(0, 0, 0.01f), default, new Vector3(1.2f, 1.4f, 0.6f));
                    MeshKit.Add(holder, MeshKit.Capsule(0.017f, 0.13f), Gore.BoneMaterial, at + new Vector3(0.01f, 0.02f, -0.03f), new Vector3(G.Rand(0.6f, 1.2f), 0, G.Rand(-0.6f, 0.6f)));
                    for (int i = 0; i < 3; i++)
                        MeshKit.Add(holder, MeshKit.Box(new Vector3(0.025f, 0.02f, 0.025f)), Gore.MeatMaterial, at + new Vector3(G.Rand(-0.03f, 0.03f), G.Rand(-0.04f, 0.04f), -0.025f), new Vector3(G.Randf(), G.Randf(), G.Randf()));
                    break;
                }
                case "head":
                    if (stage >= 1 && had < 1)
                    {
                        // Щека разорвана: дыра в лице, видны зубы.
                        float x = G.Pick(-0.06f, 0.06f);
                        MeshKit.Add(part, MeshKit.Sphere(0.045f), Gore.HoleMaterial, new Vector3(x, 0.09f, -0.09f), default, new Vector3(0.8f, 1f, 0.6f));
                        MeshKit.Add(part, MeshKit.Sphere(0.055f), Gore.DarkBloodMaterial, new Vector3(x, 0.09f, -0.08f), default, new Vector3(0.9f, 1.1f, 0.5f));
                        for (int i = 0; i < 4; i++)
                            MeshKit.Add(part, MeshKit.Box(new Vector3(0.012f, 0.02f, 0.012f)), Gore.BoneMaterial, new Vector3(x * 0.9f, 0.075f + (i % 2) * 0.03f, -0.11f + i * 0.008f));
                    }
                    break;
            }
        }

        /// Одна из деталей части тела (рукав, штанина, кусок рубашки) пропитывается кровью.
        void Soak(Transform part)
        {
            var meshes = new List<MeshRenderer>();
            foreach (var m in part.GetComponentsInChildren<MeshRenderer>())
                if (m.sharedMaterial != Gore.DarkBloodMaterial && m.sharedMaterial != Gore.BloodMaterial && m.name != "Cap")
                    meshes.Add(m);
            if (meshes.Count == 0) return;
            G.PickList(meshes).sharedMaterial = G.Randf() < 0.7f ? Gore.DarkBloodMaterial : Gore.BloodMaterial;
        }

        /// Скользящее попадание в голову или голова уже разбита пулями: верх черепа слетает,
        /// мозги вываливаются и стекают. Голова держится, но вдвое слабее.
        void OpenSkull(Vector3 point, Vector3 dir)
        {
            skullOpen = true;
            zoneHp["head"] = Mathf.Max(1f, Mathf.Min(zoneHp["head"], Zones["head"].hp * 0.5f));
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
            Say("shriek", CRY_DB);
            speedJitter *= 0.8f;
            headTilt = 0.5f * Mathf.Sign(headTilt != 0f ? headTilt : 1f);
        }

        /// Звук попадания зависит от зоны: голова — стук черепа, руки и ноги — то мясо,
        /// то кость, корпус — мясо, иногда с чавканьем. Сверху иногда шлёпают брызги.
        /// Каждый раз случайный вариант и высота, так что очередь не звучит как одна пуля.
        void HitSound(string zone, Vector3 point)
        {
            float pitch = G.Rand(0.82f, 1.18f);
            switch (zone)
            {
                case "head":
                    Sfx.PlayAt("hit_skull", point, -4f, pitch);
                    break;
                case "torso":
                    Sfx.PlayAt("hit_flesh", point, -5f, pitch * 0.92f);
                    if (G.Randf() < 0.35f) Sfx.PlayAt("squelch", point, -10f, G.Rand(0.85f, 1.2f));
                    break;
                default:
                    Sfx.PlayAt(G.Randf() < 0.4f ? "hit_bone" : "hit_flesh", point, -6f, pitch * 1.08f);
                    break;
            }
            if (G.Randf() < 0.45f) Sfx.PlayAt("spatter", point + Vector3.down, -13f, G.Rand(0.9f, 1.15f));
        }

        /// Предплечье с кистью отрывается: улетает куском, из локтя торчит раздробленная
        /// кость в клочьях мяса, хлещет кровь. Рука становится короче, попасть в неё труднее.
        public void TearForearm(string zone, Vector3 dir)
        {
            if (!parts.TryGetValue(zone, out var armJoint)) return;
            var arm = armJoint.transform;
            var elbow = arm.Find("Elbow");
            if (elbow == null) return;
            var at = elbow.position;
            Gore.Detach(elbow, dir * 3.5f + knock + Vector3.up * 2f);
            Kill(elbow);
            // Раны и потёки, что были на предплечье, уходят вместе с ним.
            var drop = new List<Transform>();
            foreach (Transform child in arm)
                if (child.GetComponent<BoxCollider>() == null && child.localPosition.y < ELBOW_Y + 0.02f) drop.Add(child);
            foreach (var c in drop) Kill(c);
            RaggedStump(arm, new Vector3(0, ELBOW_Y, 0), Vector3.down, 0.8f);
            Gore.Burst(at, dir, 22);
            Gore.Chunks(at, dir, 8);
            Sfx.PlayAt("tear", at, -1f, G.Rand(0.9f, 1.15f));
            Sfx.PlayAt("hit_bone", at, -5f, G.Rand(0.8f, 1f));
            Say("shriek", CRY_DB);
            armLen[zone] = armLen[zone] * 0.55f;
            // Зона попадания — только плечо.
            var size = new Vector3(0.15f, Mathf.Abs(ELBOW_Y) + 0.04f, 0.15f);
            foreach (Transform child in arm)
            {
                var box = child.GetComponent<BoxCollider>();
                if (box == null) continue;
                child.localPosition = G.P(0, ELBOW_Y / 2f, 0);
                box.size = size;
            }
            var meta = arm.GetComponent<PartMeta>();
            if (meta != null)
            {
                meta.hitSize = size;
                meta.hitCenter = new Vector3(0, ELBOW_Y / 2f, 0);
            }
        }

        /// Рваный обрубок: раздробленная кость, клочья мяса и кожи вокруг, бьёт кровь.
        /// at и outDir — в осях Godot-версии.
        void RaggedStump(Transform holder, Vector3 at, Vector3 outDir, float size = 1f)
        {
            MeshKit.Add(holder, MeshKit.Sphere(0.055f * size), Gore.DarkBloodMaterial, at, default, new Vector3(1.1f, 0.6f, 1.1f));
            MeshKit.Add(holder, MeshKit.Capsule(0.017f * size, 0.11f * size), Gore.BoneMaterial, at + outDir * 0.05f * size, new Vector3(G.Rand(-0.3f, 0.3f), 0, G.Rand(-0.3f, 0.3f)));
            int count = G.RandI(5, 8);
            for (int i = 0; i < count; i++)
            {
                float a = G.Rand(0f, G.TAU);
                float r = G.Rand(0.03f, 0.06f) * size;
                var mat = G.Pick(Gore.MeatMaterial, Gore.BloodMaterial, Gore.DarkBloodMaterial, skin);
                MeshKit.Add(holder, MeshKit.Box(new Vector3(G.Rand(0.015f, 0.035f), G.Rand(0.03f, 0.07f), G.Rand(0.015f, 0.035f)) * size), mat,
                    at + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r) + outDir * G.Rand(0f, 0.04f) * size,
                    new Vector3(G.Rand(-0.7f, 0.7f), a, G.Rand(-0.7f, 0.7f)));
            }
            Gore.Bleed(holder, at, outDir, 5f, 1.6f);
        }

        void Sever(string zone, Vector3 point, Vector3 dir)
        {
            var part = parts[zone];
            Gore.Detach(part.transform, dir * 3f + knock + Vector3.up * 2f);
            Gore.Burst(part.transform.position, dir, 25);
            Gore.Chunks(part.transform.position, dir, 10);
            Sfx.PlayAt("crunch", point, 0f);
            Sfx.PlayAt("tear", point, -3f, G.Rand(0.8f, 1.05f));
            Say("shriek", CRY_DB);
            RemovePart(zone);
            if (dead) return;
            if (zone.StartsWith("leg")) StartCrawl();
            if (!(parts.ContainsKey("arm_l") || parts.ContainsKey("arm_r") || parts.ContainsKey("leg_l") || parts.ContainsKey("leg_r")))
                DieBody(point, dir);
        }

        /// Для проверок: отрыв части тела, как от пуль.
        public void SeverForTest(string zone, Vector3 point, Vector3 dir) => Sever(zone, point, dir);

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
            BecomeCorpse(dir, "head");
        }

        public void DieBody(Vector3 point, Vector3 dir)
        {
            dead = true;
            Gore.Burst(point, dir, 35);
            Sfx.PlayAt("crunch", point, 0f, 0.9f);
            Died?.Invoke("torso");
            Gore.HitStop(0.04f);
            BecomeCorpse(dir, "torso");
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
            Corpses.Remove(this);
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
            kneel = 0f;
            sink = 0f;
            foreach (var k in knees.Values)
            {
                k.rot.x = 0f;
                k.Apply();
            }
            // Валится ничком: наклон доводит анимация ползания, чтобы пол проверялся
            // уже после поворота тела.
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
            knees.Remove(zone);
            Kill(part.transform);
            // Рваный обрубок: кость, клочья мяса и кожи, из него ещё какое-то время хлещет кровь.
            MeshKit.Add(holder, MeshKit.Sphere(0.08f), Gore.BloodMaterial, at, default, new Vector3(1, 0.6f, 1), "Stump_" + zone);
            var outDir = zone == "head" ? Vector3.up : (zone.StartsWith("leg") ? Vector3.down : new Vector3(Mathf.Sign(at.x), 0, 0));
            RaggedStump(holder, at, outDir, 1.4f);
            Gore.Bleed(holder, at, outDir, 5f, 2f);
            stumps++;
        }

        // --- Смерть и трупы -------------------------------------------------------

        /// Тело не разлетается на куски, а падает целиком и остаётся лежать. Как падает:
        /// от выстрела в голову чаще валится как подкошенный по направлению пули, иногда
        /// сначала подгибаются колени; убитый в корпус оседает на колени, качается и падает
        /// лицом вперёд; сбитый дробью продолжает лететь; ползущий просто обмякает.
        void BecomeCorpse(Vector3 dir, string cause)
        {
            dead = true;
            // Без головы кричать нечем; убитый в корпус хрипит перед смертью.
            if (parts.ContainsKey("head")) Say("death", -3f + CRY_DB);
            All.Remove(this);
            // Живые и игроки проходят сквозь труп, пол держит.
            gameObject.layer = Layers.Corpse;
            cc.radius = 0.25f;
            cc.height = 0.5f;
            cc.center = new Vector3(0f, 0.25f, 0f);
            armState.Clear();
            shoulderPrev.Clear();
            flail = 1f;
            if (zoneHp.ContainsKey("torso")) zoneHp["torso"] = CORPSE_HP;
            Corpses.Add(this);
            while (Corpses.Count > MAX_CORPSES)
            {
                var old = Corpses[0];
                Corpses.RemoveAt(0);
                if (old != null) Destroy(old.gameObject);
            }
            if (parts.TryGetValue("torso", out var torso)) torsoRest = torso.rot;
            landed = false;
            if (crawling)
            {
                deathStage = "slump";
                deathTimer = 0f;
                return;
            }
            if (knockedDown > 0f || knock.magnitude >= KNOCKDOWN_SPEED * 0.8f)
            {
                // Уже летит или падает: просто не встанет.
                if (fall < 0.05f) StartFall();
                deathStage = "topple";
                return;
            }
            float buckleChance = cause == "head" ? 0.4f : 0.75f;
            if (G.Randf() < buckleChance)
            {
                deathStage = "buckle";
                deathTimer = G.Rand(0.35f, 0.75f);
            }
            else ToppleAlong(dir, 1.2f);
        }

        void ToppleAlong(Vector3 dir, float speed)
        {
            var d = G.P(transform.InverseTransformDirection(new Vector3(dir.x, 0f, dir.z)));
            d.y = 0f;
            if (d.magnitude < 0.01f) d = new Vector3(0, 0, 1);
            d = d.normalized;
            fallDir = new Vector2(d.x, d.z);
            fallPivot = Vector3.zero;
            fallVel = Mathf.Max(fallVel, speed);
            deathStage = "topple";
        }

        /// Вылет из окна: живой умирает (убийство засчитывается тому, кто его толкнул),
        /// труп и ползущий просто падают. Повторный вызов ничего не меняет.
        public void FallOut(Vector3 outward)
        {
            if (falling) return;
            falling = true;
            var o = G.Flat(outward);
            fallOutDir = o.magnitude > 0.01f ? o.normalized : Vector3.forward;
            // Фасад и соседей больше не задеваем, и в зоне падения тело уже не считается.
            if (cc != null) cc.enabled = false;
            velocity.y = Mathf.Min(velocity.y, 0f);
            fallSpin = G.Rand(2f, 4f);
            fallAxis = Quaternion.AngleAxis(G.Rand(-0.5f, 0.5f) * Mathf.Rad2Deg, Vector3.up) * Vector3.Cross(fallOutDir, Vector3.up);
            if (dead) return;
            dead = true;
            All.Remove(this);
            if (!Corpses.Contains(this)) Corpses.Add(this);
            while (Corpses.Count > MAX_CORPSES)
            {
                var old = Corpses[0];
                Corpses.RemoveAt(0);
                if (old != null && old != this) Destroy(old.gameObject);
            }
            // Прощальный крик остаётся в точке вылета и затихает, пока зомби летит вниз.
            Sfx.PlayAt(G.Pick("shriek", "death"), transform.position + Vector3.up, CRY_DB + 4f, voice);
            if (lastAttacker != null) lastAttacker.kills++;
            Died?.Invoke("fall");
        }

        void FallOutProcess(float delta)
        {
            velocity.y -= GRAVITY * delta;
            float along = Mathf.Max(Vector3.Dot(knock, fallOutDir) * Mathf.Exp(-delta), FALL_OUT_SPEED);
            knock = fallOutDir * along;
            transform.position += new Vector3(knock.x, velocity.y, knock.z) * delta;
            if (fallAxis.magnitude > 0.01f)
                transform.Rotate(fallAxis.normalized, fallSpin * delta * Mathf.Rad2Deg, Space.World);
            if (transform.position.y < FALL_DESPAWN_Y)
            {
                Corpses.Remove(this);
                Destroy(gameObject);
            }
        }

        void CorpseProcess(float delta)
        {
            if (!cc.isGrounded) velocity.y -= GRAVITY * delta;
            else if (velocity.y < 0f) velocity.y = -1f;
            velocity.x = knock.x;
            velocity.z = knock.z;
            // Лежачее тело быстро тормозит о пол.
            knock *= Mathf.Exp(-delta * (deathStage != "lying" ? 4.5f : 9f));
            MoveBody(delta);
            UpdateJerk(delta);
            AnimateDead(delta);
            KeepAboveFloor();
        }

        void AnimateDead(float delta)
        {
            parts.TryGetValue("torso", out var torso);
            switch (deathStage)
            {
                case "buckle":
                {
                    deathTimer -= delta;
                    sink = Mathf.MoveTowards(sink, KNEEL_SINK, delta * 2.4f);
                    float bend = sink / KNEEL_SINK;
                    foreach (var kv in knees)
                    {
                        if (parts.TryGetValue(kv.Key, out var leg)) leg.rot.x = Mathf.MoveTowards(leg.rot.x, 0f, delta * 3f);
                        kv.Value.rot.x = -1.5f * bend;
                    }
                    // Обмякает: корпус клонится вперёд и покачивается, голова падает.
                    torsoRest.x = Mathf.Lerp(torsoRest.x, -0.55f, delta * 3f);
                    torsoRest.z = Mathf.Sin(phase * 2f) * 0.12f;
                    phase += delta * 3f;
                    body.rot = Vector3.zero;
                    body.pos = new Vector3(0, -sink, 0);
                    if (deathTimer <= 0f)
                    {
                        // Валится вперёд, немного вбок, вокруг коленей.
                        fallDir = new Vector2(G.Rand(-0.5f, 0.5f), -1f).normalized;
                        fallPivot = KNEE_POS;
                        fall = 0f;
                        fallVel = 0.4f;
                        deathStage = "topple";
                    }
                    break;
                }
                case "slump":
                    // Ползущий просто обмякает и ложится плашмя.
                    sink = Mathf.MoveTowards(sink, 0f, delta * 2f);
                    foreach (var k in knees.Values) k.rot.x = Mathf.MoveTowards(k.rot.x, -0.2f, delta * 3f);
                    foreach (var zone in Legs)
                        if (parts.TryGetValue(zone, out var leg)) leg.rot.x = Mathf.MoveTowards(leg.rot.x, 0f, delta * 3f);
                    body.rot.x = Mathf.MoveTowards(body.rot.x, -1.5f, delta * 4f);
                    body.rot.z = 0f;
                    body.pos.y = Mathf.MoveTowards(body.pos.y, 0.14f, delta * 2f);
                    torsoRest = Vector3.MoveTowards(torsoRest, new Vector3(0.05f, 0, 0), delta * 3f);
                    if (!landed && body.rot.x <= -1.45f) Land();
                    break;
                case "topple":
                case "lying":
                    AnimateCorpseFall(delta);
                    break;
            }
            if (torso != null) torso.rot = torsoRest + jerk;
            if (parts.TryGetValue("head", out var head))
            {
                var rest = HeadRest();
                head.rot.x = rest.x + hjerk.x;
                head.rot.z = rest.z + hjerk.y;
            }
            ApplyAll();
            AnimateFlail(delta);
            if (torso != null) corpseCenter = torso.transform.position;
        }

        Vector3 HeadRest()
        {
            if (deathStage == "buckle") return new Vector3(-0.5f, 0, headTilt);
            return new Vector3(0.1f, 0, headTilt * 1.5f);
        }

        /// Тело валится вокруг ступней (или коленей, если уже на них осело): гравитация
        /// опрокидывает, у пола удар, отскок, и дальше лежит. Ноги разгибаются, корпус
        /// выпрямляется, пока летит.
        const float CORPSE_GROUND = 1.48f;

        void AnimateCorpseFall(float delta)
        {
            float com = fallPivot == Vector3.zero ? 0.95f : 0.55f;
            if (deathStage == "topple")
            {
                fallVel += 9.8f / com * Mathf.Sin(Mathf.Max(fall, 0.08f)) * delta;
                fall += fallVel * delta;
                if (fall >= CORPSE_GROUND)
                {
                    fall = CORPSE_GROUND;
                    if (fallVel > 1.2f)
                    {
                        fallVel = -fallVel * 0.25f;
                        hjerkV.x += 6f;
                        jerkV.x += fallDir.y * 3f;
                        if (!landed) Land();
                    }
                    else
                    {
                        fallVel = 0f;
                        deathStage = "lying";
                        if (!landed) Land();
                    }
                }
            }
            torsoRest = Vector3.MoveTowards(torsoRest, Vector3.zero, delta * 2f);
            foreach (var kv in knees)
            {
                float target = fall > 1.2f ? -0.15f : kv.Value.rot.x;
                kv.Value.rot.x = Mathf.MoveTowards(kv.Value.rot.x, target, delta * 3f);
                if (parts.TryGetValue(kv.Key, out var leg)) leg.rot.x = Mathf.MoveTowards(leg.rot.x, 0f, delta * 2f);
            }
            body.rot = new Vector3(fall * fallDir.y, 0f, -fall * fallDir.x);
            var p = fallPivot;
            // Опрокидывание вокруг точки p: поворот считается в осях Godot-версии.
            var turned = G.P(G.R(body.rot) * G.P(p));
            body.pos = new Vector3(0, -sink, 0) + p - turned;
            body.pos.y += 0.12f * Mathf.Clamp01(fall / CORPSE_GROUND);
        }

        /// Тело ударилось о пол: глухой шлепок и лужа крови, которая растекается из-под него.
        void Land()
        {
            landed = true;
            Sfx.PlayAt("splat", transform.position + Vector3.up * 0.2f, -4f, 0.45f);
            var at = transform.position;
            if (parts.TryGetValue("torso", out var torso)) at = torso.transform.position;
            corpseCenter = at;
            var pool = Gore.FloorStain(at, "pool", G.Rand(0.2f, 0.28f));
            if (pool == null) return;
            // Лужа растекается, но не дальше края пола или ступеньки.
            var t = pool.transform;
            var grown = t.localScale;
            foreach (float k in new[] { G.Rand(2.8f, 3.6f), 2.2f, 1.5f })
            {
                if (Gore.Covered(t.position, t.rotation, t.localScale.x * k, t.localScale.z * k))
                {
                    grown = t.localScale * k;
                    break;
                }
            }
            grown.y = 1f;
            ScaleTo.Run(t, grown, G.Rand(4f, 7f), true);
        }

        /// Попадание в труп: тело дёргается, брызжет кровь, его немного сдвигает.
        /// Можно отстрелить голову или конечность, а изрешечённый труп разваливается на куски.
        public void HitCorpse(string zone, float damage, Vector3 point, Vector3 dir, float force = 1f)
        {
            if (!dead || !zoneHp.ContainsKey(zone)) return;
            Gore.Splash(point, dir, 16);
            Gore.Chunks(point, dir, G.RandI(1, 3));
            Jolt(zone, point, dir, force * 1.4f);
            var flat = new Vector3(dir.x, 0f, dir.z).normalized;
            knock = Vector3.ClampMagnitude(knock + flat * force * 0.2f, 4f);
            zoneHp[zone] -= damage * Zones[zone].mult;
            if (zoneHp[zone] > 0f)
            {
                AddWound(zone, point, dir);
                // Пуля в труп: глуше и мокрее, чем в живого.
                Sfx.PlayAt(G.Pick("hit_flesh", "squelch"), point, -9f, G.Rand(0.7f, 0.95f));
                return;
            }
            if (zone == "torso")
            {
                Gore.Burst(point, dir, 30);
                Sfx.PlayAt("crunch", point, -2f, 0.9f);
                Collapse(dir * 3f + knock);
                return;
            }
            var part = parts[zone];
            Gore.Detach(part.transform, dir * 3f + Vector3.up * 2f);
            Gore.Burst(part.transform.position, dir, 20);
            Sfx.PlayAt("crunch", point, -2f);
            RemovePart(zone);
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
