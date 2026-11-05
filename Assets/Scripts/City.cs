using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieCoop
{
    /// Одна башня города (для проверок и отладки).
    public class CityTower
    {
        public Vector2 pos;
        public float r, w, d, yaw, top;
        public int shade;
    }

    /// Данные города на его узле.
    public class CityInfo : MonoBehaviour
    {
        public List<CityTower> towers = new List<CityTower>();
        public int litWindows;
    }

    /// Ночной город из небоскрёбов вокруг этажа, тающий в темноте.
    /// Башни — по одному склеенному мешу на оттенок, окна — по одному на яркость,
    /// без теней и коллизий. Все случайности идут из своего генератора по seed.
    /// Башни тонут в тумане, окна и зарево — нет (отдельный материал без тумана).
    public static class City
    {
        const int TOWER_COUNT_MIN = 100;
        const int TOWER_COUNT_MAX = 150;
        /// Минимальный зазор между краем этажа и основанием башни.
        const float GAP = 25f;
        /// Башни ставятся не дальше этого расстояния от края этажа.
        const float RING_OUTER = 250f;
        const float FOOT_MIN = 12f;
        const float FOOT_MAX = 40f;
        const float BOTTOM_Y = -400f;
        /// Мы высоко: большинство крыш ниже этажа, над горизонтом торчат только отдельные башни.
        const float TOP_MIN = -160f;
        const float TOP_MAX = 90f;
        const float BELOW_SHARE = 0.65f;
        const int MAX_ATTEMPTS = 60000;
        static readonly float[] SHADES = { 0.05f, 0.08f, 0.11f };
        static readonly float[] WINDOW_BRIGHTNESS = { 0.35f, 0.6f, 0.95f };
        const float WINDOW_W = 1.2f;
        const float WINDOW_H = 1.6f;
        const float WINDOW_PITCH_X = 3f;
        const float WINDOW_PITCH_Y = 3.5f;
        const float WINDOW_FIRST_Y = -154.25f;
        const float LIT_CHANCE = 0.07f;
        /// Зарево над городом: кольцо далеко за башнями, светлее у горизонта. Башни в тумане
        /// чернеют и проступают на нём силуэтами. Нуар-фильтр поднимает контраст, поэтому
        /// яркость зарева выше, чем кажется: тёмные тона ниже ~0,12 он уводит в чёрный.
        const float GLOW_RADIUS = 600f;
        const float GLOW_PEAK = 0.3f;
        const float GLOW_PEAK_Y = -40f;
        const float GLOW_BOTTOM_Y = -320f;
        const float GLOW_TOP_Y = 260f;
        const int MAX_WINDOWS = 6000;
        const float FACE_DOT = 0.25f;
        const float ROOF_SLAB_HEIGHT = 1.2f;
        const float ANTENNA_MIN = 6f;
        const float ANTENNA_MAX = 25f;

        /// Строит город вокруг прямоугольника этажа (boundsMin..boundsMax, XZ) и
        /// добавляет его в world. Возвращает узел City.
        public static GameObject Build(Transform world, Vector3 boundsMin, Vector3 boundsMax, int seed)
        {
            var rng = new Rng(seed);
            var bmin = new Vector2(boundsMin.x, boundsMin.z);
            var bmax = new Vector2(boundsMax.x, boundsMax.z);
            var center = (bmin + bmax) * 0.5f;

            var towers = PlaceTowers(rng, bmin, bmax, rng.RangeI(TOWER_COUNT_MIN, TOWER_COUNT_MAX));
            var boxes = new List<Matrix4x4>[SHADES.Length];
            for (int i = 0; i < boxes.Length; i++) boxes[i] = new List<Matrix4x4>();
            var windows = new MeshData[WINDOW_BRIGHTNESS.Length];
            for (int i = 0; i < windows.Length; i++) windows[i] = new MeshData();
            foreach (var t in towers) AddBoxes(rng, t, boxes);
            int lit = 0;
            foreach (var t in towers) lit = AddWindows(rng, t, center, windows, lit);

            var city = new GameObject("City");
            city.transform.SetParent(world, false);
            var info = city.AddComponent<CityInfo>();
            info.towers = towers;
            info.litWindows = lit;
            for (int i = 0; i < SHADES.Length; i++)
            {
                if (boxes[i].Count == 0) continue;
                var mesh = CombineBoxes(boxes[i], "Towers" + i);
                // Цвет задаётся как в Godot (гамма); свойство Color материала Unity переводит в линейный сам.
                Node(city.transform, "Towers" + i, mesh, MeshKit.UnlitVC(new Color(SHADES[i], SHADES[i], SHADES[i]), true, false));
            }
            for (int i = 0; i < WINDOW_BRIGHTNESS.Length; i++)
            {
                if (windows[i].tris.Count == 0) continue;
                float b = WINDOW_BRIGHTNESS[i];
                Node(city.transform, "Windows" + i, windows[i].Build("Windows" + i), MeshKit.UnlitVC(new Color(b, b, b), false, true));
            }
            Node(city.transform, "Glow", Glow(center), MeshKit.UnlitVC(Color.white, false, true));
            return city;
        }

        static void Node(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// Данные меша: вершины, цвета (белые) и треугольники; индексы 32-битные.
        class MeshData
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> tris = new List<int>();

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetColors(c);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        /// Все коробки одного оттенка одним мешем.
        static Mesh CombineBoxes(List<Matrix4x4> transforms, string name)
        {
            var box = MeshKit.Box(Vector3.one);
            var bv = box.vertices;
            var bt = box.triangles;
            var data = new MeshData();
            foreach (var m in transforms)
            {
                int start = data.v.Count;
                for (int i = 0; i < bv.Length; i++)
                {
                    data.v.Add(m.MultiplyPoint3x4(bv[i]));
                    data.c.Add(Color.white);
                }
                for (int i = 0; i < bt.Length; i++) data.tris.Add(start + bt[i]);
            }
            return data.Build(name);
        }

        /// Кольцо зарева вокруг этажа: вертикальный градиент серого, не тонет в тумане.
        static Mesh Glow(Vector2 center)
        {
            var rows = new[]
            {
                new Vector2(GLOW_BOTTOM_Y, 0f),
                new Vector2(GLOW_PEAK_Y - 60f, GLOW_PEAK * 0.6f),
                new Vector2(GLOW_PEAK_Y, GLOW_PEAK),
                new Vector2(GLOW_PEAK_Y + 40f, GLOW_PEAK * 0.7f),
                new Vector2(GLOW_PEAK_Y + 110f, GLOW_PEAK * 0.3f),
                new Vector2(GLOW_TOP_Y, 0f),
            };
            const int segments = 48;
            var data = new MeshData();
            for (int r = 0; r < rows.Length - 1; r++)
            {
                for (int k = 0; k < segments; k++)
                {
                    float a0 = G.TAU * k / segments;
                    float a1 = G.TAU * (k + 1) / segments;
                    var p00 = new Vector3(center.x + Mathf.Cos(a0) * GLOW_RADIUS, rows[r].x, center.y + Mathf.Sin(a0) * GLOW_RADIUS);
                    var p10 = new Vector3(center.x + Mathf.Cos(a1) * GLOW_RADIUS, rows[r].x, center.y + Mathf.Sin(a1) * GLOW_RADIUS);
                    var p01 = new Vector3(p00.x, rows[r + 1].x, p00.z);
                    var p11 = new Vector3(p10.x, rows[r + 1].x, p10.z);
                    // Серый задан как в Godot (гамма), в вершинах Unity нужен линейный.
                    var c0 = new Color(rows[r].y, rows[r].y, rows[r].y, 1f).linear;
                    var c1 = new Color(rows[r + 1].y, rows[r + 1].y, rows[r + 1].y, 1f).linear;
                    int i = data.v.Count;
                    data.v.Add(p00); data.c.Add(c0);
                    data.v.Add(p10); data.c.Add(c0);
                    data.v.Add(p11); data.c.Add(c1);
                    data.v.Add(p01); data.c.Add(c1);
                    data.tris.Add(i); data.tris.Add(i + 1); data.tris.Add(i + 2);
                    data.tris.Add(i); data.tris.Add(i + 2); data.tris.Add(i + 3);
                }
            }
            return data.Build("Glow");
        }

        /// Размещает башни кольцом вокруг этажа. Расстояние от центра башни до прямоугольника
        /// этажа: не меньше GAP + радиус основания (тогда основание целиком дальше GAP) и не больше RING_OUTER.
        static List<CityTower> PlaceTowers(Rng rng, Vector2 bmin, Vector2 bmax, int count)
        {
            var towers = new List<CityTower>();
            var reach = new Vector2(RING_OUTER + FOOT_MAX, RING_OUTER + FOOT_MAX);
            var lo = bmin - reach;
            var hi = bmax + reach;
            int attempts = 0;
            while (towers.Count < count && attempts < MAX_ATTEMPTS)
            {
                attempts++;
                float w = rng.Range(FOOT_MIN, FOOT_MAX);
                float d = rng.Range(FOOT_MIN, FOOT_MAX);
                float r = new Vector2(w, d).magnitude * 0.5f;
                var p = new Vector2(rng.Range(lo.x, hi.x), rng.Range(lo.y, hi.y));
                float dist = RectDistance(p, bmin, bmax);
                if (dist < GAP + r || dist > RING_OUTER) continue;
                bool free = true;
                foreach (var o in towers)
                {
                    if (Vector2.Distance(p, o.pos) < r + o.r + 3f)
                    {
                        free = false;
                        break;
                    }
                }
                if (!free) continue;
                float yaw = rng.Randf() * G.TAU;
                float top = rng.Randf() < BELOW_SHARE ? rng.Range(TOP_MIN, -10f) : rng.Range(0f, TOP_MAX);
                int shade = rng.Next(SHADES.Length);
                towers.Add(new CityTower { pos = p, r = r, w = w, d = d, yaw = yaw, top = top, shade = shade });
            }
            return towers;
        }

        static float RectDistance(Vector2 p, Vector2 bmin, Vector2 bmax)
        {
            float dx = Mathf.Max(Mathf.Max(bmin.x - p.x, 0f), p.x - bmax.x);
            float dz = Mathf.Max(Mathf.Max(bmin.y - p.y, 0f), p.y - bmax.y);
            return new Vector2(dx, dz).magnitude;
        }

        static Quaternion Yaw(float radians) => Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.up);

        /// Корпус башни, при необходимости крыша-плита и антенна (только для башен, торчащих над полом).
        static void AddBoxes(Rng rng, CityTower t, List<Matrix4x4>[] boxes)
        {
            var rot = Yaw(t.yaw);
            float height = t.top - BOTTOM_Y;
            boxes[t.shade].Add(Matrix4x4.TRS(new Vector3(t.pos.x, (t.top + BOTTOM_Y) * 0.5f, t.pos.y), rot, new Vector3(t.w, height, t.d)));
            float roofY = t.top;
            if (t.top > 0f && rng.Randf() < 0.3f)
            {
                boxes[t.shade].Add(Matrix4x4.TRS(new Vector3(t.pos.x, t.top + ROOF_SLAB_HEIGHT * 0.5f, t.pos.y), rot,
                    new Vector3(t.w * 0.7f, ROOF_SLAB_HEIGHT, t.d * 0.7f)));
                roofY += ROOF_SLAB_HEIGHT;
            }
            if (t.top > 0f && rng.Randf() < 0.25f)
            {
                float h = rng.Range(ANTENNA_MIN, ANTENNA_MAX);
                boxes[t.shade].Add(Matrix4x4.TRS(new Vector3(t.pos.x, roofY + h * 0.5f, t.pos.y), Quaternion.identity, new Vector3(0.3f, h, 0.3f)));
            }
        }

        static readonly Vector3[] LocalNormals = { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1) };

        /// Освещённые окна на гранях, обращённых к этажу. Возвращает новое число освещённых окон.
        static int AddWindows(Rng rng, CityTower t, Vector2 center, MeshData[] windows, int lit)
        {
            var rot = Yaw(t.yaw);
            var toCenter = new Vector3(center.x - t.pos.x, 0f, center.y - t.pos.y).normalized;
            var origin = new Vector3(t.pos.x, 0f, t.pos.y);
            foreach (var ln in LocalNormals)
            {
                var worldN = rot * ln;
                if (Vector3.Dot(worldN, toCenter) < FACE_DOT) continue;
                bool alongX = ln.x != 0f;
                float faceW = alongX ? t.d : t.w;
                var tangent = alongX ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
                int cols = Mathf.FloorToInt((faceW - 2f) / WINDOW_PITCH_X);
                if (cols < 1) continue;
                // Ось вдоль грани в мире (окно двустороннее, поэтому порядок вершин не важен).
                var across = Vector3.Cross(Vector3.up, worldN).normalized;
                float yc = WINDOW_FIRST_Y;
                while (yc < t.top - 1.5f)
                {
                    for (int i = 0; i < cols; i++)
                    {
                        if (lit >= MAX_WINDOWS) return lit;
                        if (rng.Randf() >= LIT_CHANCE) continue;
                        float tc = (i - (cols - 1) * 0.5f) * WINDOW_PITCH_X;
                        // Дальние окна тусклее: туман на них не действует, гасим вручную.
                        float far = Mathf.Clamp(1.15f - Vector2.Distance(center, t.pos) / 300f, 0.35f, 1f);
                        float bright = rng.Range(0.3f, 1f) * far;
                        int bucket = bright < 0.5f ? 0 : (bright < 0.8f ? 1 : 2);
                        var local = new Vector3(ln.x * (t.w * 0.5f + 0.05f), yc, ln.z * (t.d * 0.5f + 0.05f)) + tangent * tc;
                        AddQuad(windows[bucket], origin + rot * local, across, worldN);
                        lit++;
                    }
                    yc += WINDOW_PITCH_Y;
                }
            }
            return lit;
        }

        static void AddQuad(MeshData data, Vector3 c, Vector3 across, Vector3 normal)
        {
            var hx = across * (WINDOW_W * 0.5f);
            var hy = Vector3.up * (WINDOW_H * 0.5f);
            int i = data.v.Count;
            data.v.Add(c - hx - hy);
            data.v.Add(c + hx - hy);
            data.v.Add(c + hx + hy);
            data.v.Add(c - hx + hy);
            for (int k = 0; k < 4; k++) data.c.Add(Color.white);
            data.tris.Add(i); data.tris.Add(i + 1); data.tris.Add(i + 2);
            data.tris.Add(i); data.tris.Add(i + 2); data.tris.Add(i + 3);
        }
    }
}
