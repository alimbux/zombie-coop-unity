using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieCoop
{
    /// Секция стекла: тонкий коллайдер на слое мира (пули в него попадают), сама хранит прочность
    /// и держит на себе трещины и пятна крови. Нож и удар ногой стекло не бьют.
    public class GlassSection : MonoBehaviour
    {
        public GlassWindow window;
        public float hp = 60f;
        public bool broken;
        public float width = 1f;
        public float height = 1f;
        /// Наружу из здания.
        public Vector3 outward = Vector3.forward;
        public Transform glass;
        public BoxCollider box;

        /// Центр секции в мире.
        public Vector3 Center => transform.position;

        /// Попадание пули: трещина, а когда прочность кончилась — осколки.
        public void Hit(float damage, Vector3 point, Vector3 dir)
        {
            if (broken) return;
            hp -= damage;
            window.OnSectionHit(this, point, dir);
        }
    }

    /// Удаляет меш вместе с объектом (трещины и осколки делают свои меши).
    class OwnedMesh : MonoBehaviour
    {
        void OnDestroy()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        }
    }

    /// Окно во всю наружную стену этажа: тёмная рама и 2–4 стеклянные секции.
    /// Пуля трескает стекло (трещина остаётся на месте попадания), после нескольких попаданий
    /// секция разлетается осколками, и в проём можно пройти. Нож и удар ногой стекло не бьют.
    /// Локальные оси окна: Z — наружу из здания, X — вдоль стены, масштаб везде 1.
    public class GlassWindow : MonoBehaviour
    {
        public const float CELL = 9f;
        public const float HEIGHT = 3.2f;
        const float FRAME_DEPTH = 0.24f;
        const float FRAME_BAR = 0.08f;
        const float GLASS_THICKNESS = 0.04f;
        const float SECTION_HP = 60f;
        const int MIN_SHARDS = 20;
        const int MAX_SHARDS = 30;
        /// Полоса за разбитой секцией, где тело, перешагнувшее плоскость стекла, падает вниз.
        const float FALL_DEPTH = 1.5f;

        static Material glassMat, frameMat, crackMat;
        static Texture2D sheen;
        static Mesh quad;

        /// Куда смотрит окно (шаг по сетке от комнаты к улице).
        public Vector2Int dir = new Vector2Int(0, 1);
        public Vector3 outward = Vector3.forward;
        public readonly List<GlassSection> sections = new List<GlassSection>();
        /// Разбитая секция окна.
        public event System.Action<GlassSection> Broken;
        readonly List<GlassSection> fallZones = new List<GlassSection>();
        static readonly int FallMask = (1 << Layers.Player) | (1 << Layers.ZombieBody) | (1 << Layers.Corpse);

        /// Строит окно в направлении d. Положение (середина стены) задаётся снаружи.
        public void Setup(Vector2Int d)
        {
            dir = d;
            outward = new Vector3(d.x, 0f, d.y);
            transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
            int count = G.Pick(2, 3, 4);
            float innerW = CELL - FRAME_BAR * (count + 1);
            float sw = innerW / count;
            float sh = HEIGHT - FRAME_BAR * 2f;
            var frame = new GameObject("Frame");
            frame.layer = Layers.World;
            frame.transform.SetParent(transform, false);
            // Нижняя планка только для вида: в разбитый проём можно выпасть, не цепляясь за неё.
            Bar(frame.transform, new Vector3(CELL, FRAME_BAR, FRAME_DEPTH), new Vector3(0f, -HEIGHT / 2f + FRAME_BAR / 2f, 0f), false);
            Bar(frame.transform, new Vector3(CELL, FRAME_BAR, FRAME_DEPTH), new Vector3(0f, HEIGHT / 2f - FRAME_BAR / 2f, 0f), true);
            for (int i = 0; i < count + 1; i++)
            {
                float x = -CELL / 2f + FRAME_BAR / 2f + i * (sw + FRAME_BAR);
                Bar(frame.transform, new Vector3(FRAME_BAR, sh, FRAME_DEPTH), new Vector3(x, 0f, 0f), true);
            }
            for (int i = 0; i < count; i++)
            {
                float x = -CELL / 2f + FRAME_BAR + sw / 2f + i * (sw + FRAME_BAR);
                sections.Add(MakeSection(new Vector3(x, 0f, 0f), sw, sh));
            }
        }

        void Bar(Transform frame, Vector3 size, Vector3 pos, bool solid)
        {
            var go = new GameObject("Bar");
            go.layer = Layers.World;
            go.transform.SetParent(frame, false);
            go.transform.localPosition = pos;
            if (solid) go.AddComponent<BoxCollider>().size = size;
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = FrameMaterial;
        }

        GlassSection MakeSection(Vector3 pos, float sw, float sh)
        {
            var go = new GameObject("GlassSection");
            go.layer = Layers.World;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var s = go.AddComponent<GlassSection>();
            s.window = this;
            s.hp = SECTION_HP;
            s.width = sw;
            s.height = sh;
            s.outward = outward;
            s.box = go.AddComponent<BoxCollider>();
            s.box.size = new Vector3(sw, sh, GLASS_THICKNESS);
            var g = new GameObject("Glass");
            g.layer = Layers.World;
            g.transform.SetParent(go.transform, false);
            g.transform.localScale = new Vector3(sw, sh, 1f);
            g.AddComponent<MeshFilter>().sharedMesh = Quad();
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GlassMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            s.glass = g.transform;
            return s;
        }

        // --- Попадания --------------------------------------------------------------

        public void OnSectionHit(GlassSection s, Vector3 point, Vector3 hitDir)
        {
            if (s.hp <= 0f)
            {
                Shatter(s, point, hitDir);
                return;
            }
            AddCrack(s, s.transform.InverseTransformPoint(point));
            Sfx.PlayAt("glass_crack", point, -2f);
        }

        void Shatter(GlassSection s, Vector3 point, Vector3 hitDir)
        {
            s.broken = true;
            s.box.enabled = false;
            s.glass.gameObject.SetActive(false);
            // Трещины и пятна крови, прилипшие к стеклу, исчезают вместе с ним.
            var gone = new List<GameObject>();
            foreach (Transform child in s.transform)
                if (child != s.glass) gone.Add(child.gameObject);
            foreach (var c in gone)
            {
                c.SetActive(false);
                Destroy(c);
            }
            SpawnShards(s, hitDir);
            Stubs(s);
            Sfx.PlayAt("glass_break", point, 2f);
            fallZones.Add(s);
            Broken?.Invoke(s);
        }

        // --- Падение в проём ----------------------------------------------------------

        /// Зона сразу за плоскостью разбитой секции. Тело, чей центр перешёл плоскость,
        /// вылетает наружу: у него вызывается FallOut(outward). Проверяем в FixedUpdate
        /// через OverlapBox: триггеры с CharacterController срабатывают нестабильно.
        void FixedUpdate()
        {
            for (int i = 0; i < fallZones.Count; i++)
            {
                var s = fallZones[i];
                if (s == null) continue;
                var center = s.transform.position + outward * (FALL_DEPTH / 2f);
                var half = new Vector3(Mathf.Max(s.width - 0.1f, 0.1f) / 2f, s.height / 2f, FALL_DEPTH / 2f);
                var found = Physics.OverlapBox(center, half, transform.rotation, FallMask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < found.Length; k++) CheckFall(found[k]);
            }
        }

        void CheckFall(Collider c)
        {
            if (c == null) return;
            var zombie = c.GetComponentInParent<Zombie>();
            if (zombie != null)
            {
                if (!zombie.falling && Vector3.Dot(zombie.transform.position - transform.position, outward) > 0f)
                    zombie.FallOut(outward);
                return;
            }
            var player = c.GetComponentInParent<Player>();
            if (player != null && !player.fell && Vector3.Dot(player.transform.position - transform.position, outward) > 0f)
                player.FallOut(outward);
        }

        // --- Осколки ------------------------------------------------------------------

        void SpawnShards(GlassSection s, Vector3 hitDir)
        {
            var push = hitDir.sqrMagnitude > 0.0001f ? hitDir.normalized : -outward;
            Transform parent = GameRoot.World != null ? GameRoot.World : transform.parent;
            int count = G.RandI(MIN_SHARDS, MAX_SHARDS);
            for (int i = 0; i < count; i++)
            {
                float size = G.Rand(0.05f, 0.35f);
                var local = new Vector3(G.Rand(-s.width / 2f, s.width / 2f) * 0.95f, G.Rand(-s.height / 2f, s.height / 2f) * 0.95f, 0f);
                var go = new GameObject("Shard");
                // Осколки не попадают в лучи игрока и не мешают телам, только падают на пол.
                go.layer = Layers.Shard;
                if (parent != null) go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(s.transform.TransformPoint(local),
                    Quaternion.Euler(G.Rand(-0.6f, 0.6f) * Mathf.Rad2Deg, G.Rand(0f, 360f), G.Rand(-0.6f, 0.6f) * Mathf.Rad2Deg));
                go.AddComponent<BoxCollider>().size = new Vector3(size * 0.6f, 0.02f, size * 0.6f);
                go.AddComponent<MeshFilter>().sharedMesh = Triangle(size, G.Rand(0.4f, 1f), G.Rand(0.2f, 0.8f));
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GlassMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.AddComponent<OwnedMesh>();
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                rb.maxAngularVelocity = 20f;
                rb.AddForce(push * G.Rand(1f, 4f) + new Vector3(G.Rand(-1.2f, 1.2f), G.Rand(-0.2f, 1.4f), G.Rand(-1.2f, 1.2f)), ForceMode.VelocityChange);
                rb.angularVelocity = new Vector3(G.Rand(-6f, 6f), G.Rand(-6f, 6f), G.Rand(-6f, 6f));
                Destroy(go, G.Rand(4f, 6f));
            }
        }

        /// Острые обломки, торчащие из нижней и верхней планки (только для вида, без столкновений).
        void Stubs(GlassSection s)
        {
            int count = G.RandI(3, 6);
            for (int i = 0; i < count; i++)
            {
                bool up = i % 2 == 0;
                float size = G.Rand(0.06f, 0.2f);
                var go = new GameObject("Stub");
                go.layer = Layers.World;
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Triangle(size, G.Rand(0.5f, 1f), G.Rand(0.1f, 0.9f));
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GlassMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.AddComponent<OwnedMesh>();
                float x = s.transform.localPosition.x + G.Rand(-s.width / 2f, s.width / 2f) * 0.95f;
                float y = up ? -s.height / 2f : s.height / 2f;
                go.transform.localPosition = new Vector3(x, y, G.Rand(-0.01f, 0.01f));
                // Основание на планке, остриё вверх (у верхней планки — вниз).
                go.transform.localRotation = Quaternion.Euler(0f, 0f, (G.Rand(-0.3f, 0.3f) + (up ? 0f : Mathf.PI)) * Mathf.Rad2Deg);
            }
        }

        /// Плоский треугольник с основанием по X: острый осколок.
        static Mesh Triangle(float size, float heightK, float tipK)
        {
            var m = new Mesh { name = "GlassShard" };
            m.vertices = new[]
            {
                new Vector3(-size / 2f, 0f, 0f),
                new Vector3(size / 2f, 0f, 0f),
                new Vector3((tipK - 0.5f) * size, size * heightK, 0f),
            };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(tipK, 1f) };
            m.triangles = new[] { 0, 1, 2 };
            m.RecalculateBounds();
            return m;
        }

        // --- Трещины ------------------------------------------------------------------

        /// Паутина трещин вокруг точки попадания: тёмная дырка, лучи и два неровных кольца.
        void AddCrack(GlassSection s, Vector3 at)
        {
            float hx = s.width / 2f - 0.01f;
            float hy = s.height / 2f - 0.01f;
            var c = new Vector2(Mathf.Clamp(at.x, -hx, hx), Mathf.Clamp(at.y, -hy, hy));
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            var hole = Shade(0.03f);
            for (int k = 0; k < 8; k++)
            {
                float a0 = G.TAU * k / 8f;
                float a1 = G.TAU * (k + 1) / 8f;
                Tri(verts, cols, tris, c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.022f, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.022f, hole);
            }
            int rays = G.RandI(8, 11);
            float baseAngle = G.Rand(0f, G.TAU);
            var lengths = new float[rays];
            var angles = new Vector2[rays];
            var rayColor = Shade(0.92f);
            for (int k = 0; k < rays; k++)
            {
                float a = baseAngle + G.TAU * k / rays + G.Rand(-0.2f, 0.2f);
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float length = G.Rand(0.3f, 0.7f);
                // Луч не выходит за край секции.
                if (Mathf.Abs(d.x) > 0.001f) length = Mathf.Min(length, (hx - Mathf.Sign(d.x) * c.x) / Mathf.Abs(d.x));
                if (Mathf.Abs(d.y) > 0.001f) length = Mathf.Min(length, (hy - Mathf.Sign(d.y) * c.y) / Mathf.Abs(d.y));
                length = Mathf.Max(length, 0.02f);
                angles[k] = d;
                lengths[k] = length;
                Line(verts, cols, tris, c + d * 0.02f, c + d * length, 0.007f, rayColor);
            }
            var ringColor = Shade(0.78f);
            foreach (float ringR in new[] { G.Rand(0.07f, 0.1f), G.Rand(0.16f, 0.22f) })
            {
                for (int k = 0; k < rays; k++)
                {
                    int k2 = (k + 1) % rays;
                    if (lengths[k] < ringR || lengths[k2] < ringR) continue;
                    var p0 = c + angles[k] * ringR * G.Rand(0.9f, 1.1f);
                    var p1 = c + angles[k2] * ringR * G.Rand(0.9f, 1.1f);
                    Line(verts, cols, tris, p0, p1, 0.005f, ringColor);
                }
            }
            var mesh = new Mesh { name = "GlassCrack" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("Crack");
            go.layer = Layers.World;
            go.transform.SetParent(s.transform, false);
            // Чуть впереди стекла; материал двусторонний, поэтому видно с обеих сторон.
            go.transform.localPosition = new Vector3(0f, 0f, 0.006f + 0.0004f * (s.transform.childCount % 20));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CrackMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<OwnedMesh>();
        }

        /// Цвет вершины: значения заданы как в Godot (гамма), в вершинах Unity нужны линейные.
        static Color Shade(float v) => new Color(v, v, v, 1f).linear;

        static void Tri(List<Vector3> verts, List<Color> cols, List<int> tris, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int i = verts.Count;
            verts.Add(new Vector3(a.x, a.y, 0f));
            verts.Add(new Vector3(b.x, b.y, 0f));
            verts.Add(new Vector3(c.x, c.y, 0f));
            cols.Add(color);
            cols.Add(color);
            cols.Add(color);
            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
        }

        static void Line(List<Vector3> verts, List<Color> cols, List<int> tris, Vector2 a, Vector2 b, float w, Color color)
        {
            var dirv = b - a;
            if (dirv.sqrMagnitude < 1e-10f) return;
            var side = new Vector2(dirv.y, -dirv.x).normalized * w * 0.5f;
            Tri(verts, cols, tris, a - side, a + side, b + side, color);
            Tri(verts, cols, tris, a - side, b + side, b - side, color);
        }

        // --- Материалы ----------------------------------------------------------------

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "GlassQuad" };
            quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return quad;
        }

        /// Прозрачный серый материал с косыми бликами, с двух сторон.
        public static Material GlassMaterial
        {
            get
            {
                if (glassMat == null)
                {
                    glassMat = new Material(MeshKit.GlassShader);
                    glassMat.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
                    glassMat.SetTexture("_MainTex", SheenTexture());
                }
                return glassMat;
            }
        }

        /// Прозрачная текстура с мягкими косыми полосами отражения разной ширины.
        static Texture2D SheenTexture()
        {
            if (sheen != null) return sheen;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            // Полосы: {положение, ширина, сила}.
            float[][] bands = { new[] { 0.18f, 0.05f, 0.22f }, new[] { 0.3f, 0.015f, 0.16f }, new[] { 0.62f, 0.09f, 0.14f }, new[] { 0.8f, 0.02f, 0.2f } };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                // В Godot строка 0 — верх картинки, в Unity — низ.
                int gy = size - 1 - y;
                for (int x = 0; x < size; x++)
                {
                    float v = (x + gy * 0.6f) / size;
                    float t = v - Mathf.Floor(v);
                    float a = 0.07f;
                    foreach (var b in bands)
                    {
                        float d = Mathf.Abs(t - b[0]) / b[1];
                        a += b[2] * Mathf.Exp(-d * d);
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp(a, 0f, 0.4f));
                }
            }
            tex.SetPixels(pixels);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            sheen = tex;
            return sheen;
        }

        static Material FrameMaterial
        {
            get
            {
                if (frameMat == null) frameMat = MeshKit.Lit(new Color(0.17f, 0.17f, 0.17f), Color.black, 0.4f, 0.3f);
                return frameMat;
            }
        }

        static Material CrackMaterial
        {
            get
            {
                if (crackMat == null) crackMat = MeshKit.UnlitVC(Color.white, true, true);
                return crackMat;
            }
        }
    }
}
