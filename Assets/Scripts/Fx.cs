using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieCoop
{
    /// Капли крови и комки мозгов: лёгкие частицы без физики, все рисуются разом
    /// (GPU instancing), чтобы сотни брызг не стоили сотен объектов.
    public class Fx : MonoBehaviour
    {
        public enum Kind { Drop = 0, Brain = 1 }

        public static Fx Instance;
        const int CAPACITY = 6000;

        class Pool
        {
            public Mesh mesh;
            public Material mat;
            public Vector3[] pos = new Vector3[CAPACITY];
            public Vector3[] vel = new Vector3[CAPACITY];
            public float[] life = new float[CAPACITY];
            public float[] scale = new float[CAPACITY];
            public float[] gravity = new float[CAPACITY];
            public int count;
        }

        Pool[] pools;
        readonly Matrix4x4[] batch = new Matrix4x4[1023];

        void Awake()
        {
            Instance = this;
            pools = new[]
            {
                new Pool { mesh = MeshKit.Box(Vector3.one * 0.05f), mat = Gore.BloodMaterial },
                new Pool { mesh = MeshKit.Sphere(0.025f, 0.04f, 6, 3), mat = Gore.BrainMaterial },
            };
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Emit(Kind kind, Vector3 position, Vector3 velocity, float lifetime, float size, float gravity)
        {
            var p = pools[(int)kind];
            if (p.count >= CAPACITY) return;
            int i = p.count++;
            p.pos[i] = position;
            p.vel[i] = velocity;
            p.life[i] = lifetime;
            p.scale[i] = size;
            p.gravity[i] = gravity;
        }

        /// Случайное направление в конусе вокруг dir (spread — полуугол в градусах).
        public static Vector3 Cone(Vector3 dir, float spread)
        {
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
            var axis = Vector3.Cross(dir, Random.onUnitSphere);
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.Cross(dir, Vector3.right);
            return Quaternion.AngleAxis(Random.Range(-spread, spread), axis.normalized) * dir;
        }

        /// Разовый выплеск: все капли сразу.
        public void Burst(Kind kind, Vector3 point, Vector3 dir, int amount, float speedMin, float speedMax, float spread, float lifetime, float gravity, float scaleMin, float scaleMax)
        {
            for (int i = 0; i < Mathf.Max(amount, 1); i++)
                Emit(kind, point, Cone(dir, spread) * Random.Range(speedMin, speedMax), lifetime * Random.Range(0.9f, 1f), Random.Range(scaleMin, scaleMax), gravity);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var p in pools)
            {
                int i = 0;
                while (i < p.count)
                {
                    p.life[i] -= dt;
                    if (p.life[i] <= 0f)
                    {
                        int last = --p.count;
                        p.pos[i] = p.pos[last];
                        p.vel[i] = p.vel[last];
                        p.life[i] = p.life[last];
                        p.scale[i] = p.scale[last];
                        p.gravity[i] = p.gravity[last];
                        continue;
                    }
                    p.vel[i].y -= p.gravity[i] * dt;
                    p.pos[i] += p.vel[i] * dt;
                    i++;
                }
                int n = 0;
                for (int k = 0; k < p.count; k++)
                {
                    batch[n++] = Matrix4x4.TRS(p.pos[k], Quaternion.identity, Vector3.one * p.scale[k]);
                    if (n == batch.Length)
                    {
                        Graphics.DrawMeshInstanced(p.mesh, 0, p.mat, batch, n, null, ShadowCastingMode.Off, false);
                        n = 0;
                    }
                }
                if (n > 0) Graphics.DrawMeshInstanced(p.mesh, 0, p.mat, batch, n, null, ShadowCastingMode.Off, false);
            }
        }
    }

    /// Источник частиц на теле: кровь из раны, след за оторванной частью, капающие мозги.
    /// Направление задаётся в осях узла, частицы потом летят в мире сами по себе.
    public class Emitter : MonoBehaviour
    {
        public Fx.Kind kind = Fx.Kind.Drop;
        public float amount = 18f;
        public float lifetime = 0.7f;
        public Vector3 localDir = Vector3.up;
        public float spread = 20f;
        public float speedMin = 0.8f, speedMax = 2.2f;
        public float gravity = 12f;
        public float scaleMin = 0.4f, scaleMax = 0.9f;
        /// Кровь бьёт толчками, как пульс.
        public bool pulse;
        /// Через сколько секунд перестать, -1 — никогда.
        public float stopAfter = -1f;
        float time, acc;

        void Update()
        {
            float dt = Time.deltaTime;
            time += dt;
            if (stopAfter >= 0f && time >= stopAfter) return;
            if (Fx.Instance == null) return;
            float speed = pulse ? PulseScale(time) : 1f;
            acc += amount / lifetime * speed * dt;
            var dir = transform.TransformDirection(localDir);
            while (acc >= 1f)
            {
                acc -= 1f;
                Fx.Instance.Emit(kind, transform.position, Fx.Cone(dir, spread) * Random.Range(speedMin, speedMax) * speed,
                    lifetime, Random.Range(scaleMin, scaleMax), gravity);
            }
        }

        /// Пульс: за 0.12 с разгоняется до 1.6, за 0.38 с спадает до 0.6.
        static float PulseScale(float t)
        {
            float s = t % 0.5f;
            if (s < 0.12f) return Mathf.Lerp(t < 0.5f ? 1f : 0.6f, 1.6f, s / 0.12f);
            return Mathf.Lerp(1.6f, 0.6f, (s - 0.12f) / 0.38f);
        }
    }
}
