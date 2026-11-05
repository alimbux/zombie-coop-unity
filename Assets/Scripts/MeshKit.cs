using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieCoop
{
    /// Шарнир (плечо, бедро, шея, корпус): узел, вокруг которого вращается часть тела.
    /// pos и rot хранятся в осях Godot-версии (см. G), Apply() переносит их в Transform.
    public class GJoint : MonoBehaviour
    {
        public Vector3 pos;
        public Vector3 rot;

        public void Apply()
        {
            transform.localPosition = G.P(pos);
            transform.localRotation = G.R(rot);
        }
    }

    /// Размер и центр куска, если часть тела отлетит физическим объектом (в осях Godot).
    public class PartMeta : MonoBehaviour
    {
        public Vector3 hitSize = Vector3.one * 0.2f;
        public Vector3 hitCenter = Vector3.zero;
    }

    public enum ShapeKind { Sphere, Capsule, Box, Cylinder }

    /// Из какой фигуры сделан меш: нужно, чтобы класть раны на поверхность тела
    /// и не пускать части тела под пол.
    public struct ShapeInfo
    {
        public ShapeKind kind;
        public float radius, height, top, bottom;
        public Vector3 size;
    }

    /// Простые меши для фигур людей и зомби: капсулы, сферы, усечённые цилиндры, коробки.
    /// Меши одного размера переиспользуются, чтобы сотня зомби не плодила сотни копий.
    public static class MeshKit
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        public static readonly Dictionary<Mesh, ShapeInfo> Shapes = new Dictionary<Mesh, ShapeInfo>();
        static Shader lit, unlit, text;

        public static Shader LitShader => lit ? lit : (lit = Resources.Load<Shader>("ZombieCoop/Lit"));
        public static Shader UnlitShader => unlit ? unlit : (unlit = Resources.Load<Shader>("ZombieCoop/Unlit"));
        public static Shader TextShader => text ? text : (text = Resources.Load<Shader>("ZombieCoop/Text"));

        // --- Материалы ---------------------------------------------------------

        /// Матовый материал заданной яркости (мир чёрно-белый, цвет только у крови).
        public static Material Shade(float value, float roughness = 1f)
        {
            string key = string.Format("s{0:F2}/{1:F2}", value, roughness);
            if (!materials.TryGetValue(key, out var m))
            {
                m = Lit(new Color(value, value, value), Color.black, 0f, 1f - roughness);
                materials[key] = m;
            }
            return m;
        }

        public static Material Lit(Color albedo, Color emission, float metallic = 0f, float smoothness = 0f)
        {
            var m = new Material(LitShader);
            m.SetColor("_Color", albedo);
            m.SetColor("_EmissionColor", emission);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smoothness);
            m.enableInstancing = true;
            return m;
        }

        public static Material Unlit(Color color, Texture tex = null, float cutoff = 0f, bool doubleSided = false)
        {
            var m = new Material(UnlitShader);
            m.SetColor("_Color", color);
            if (tex != null) m.SetTexture("_MainTex", tex);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.enableInstancing = true;
            return m;
        }

        // --- Меши --------------------------------------------------------------

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();

            public int Add(Vector3 p, Vector3 normal, Vector2 tex = default)
            {
                v.Add(p); n.Add(normal); uv.Add(tex);
                return v.Count - 1;
            }

            /// Треугольник лицом наружу: порядок вершин подбирается по нормалям.
            public void Tri(int a, int b, int c)
            {
                if (a == b || b == c || a == c) return;
                Vector3 face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (face.sqrMagnitude < 1e-14f) return;
                Vector3 hint = n[a] + n[b] + n[c];
                if (Vector3.Dot(face, hint) < 0f) { t.Add(a); t.Add(c); t.Add(b); }
                else { t.Add(a); t.Add(b); t.Add(c); }
            }

            public void Quad(int a, int b, int c, int d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            /// Полоса колец: rows[i] — вершины i-го кольца (последняя повторяет первую).
            public void Rings(List<int[]> rows)
            {
                for (int i = 0; i + 1 < rows.Count; i++)
                    for (int j = 0; j + 1 < rows[i].Length; j++)
                        Quad(rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        public static Mesh Capsule(float radius, float height)
        {
            string key = string.Format("c{0:F3}/{1:F3}", radius, height);
            if (cache.TryGetValue(key, out var mesh)) return mesh;
            height = Mathf.Max(height, radius * 2f);
            const int radial = 8, capRings = 4;
            var b = new Builder();
            var rows = new List<int[]>();
            float half = height / 2f - radius;
            for (int k = 0; k <= capRings * 2 + 1; k++)
            {
                // Верхняя полусфера, потом нижняя: угол от полюса 0..90° и 90..180°.
                bool top = k <= capRings;
                float phi = top ? Mathf.PI / 2f * k / capRings : Mathf.PI / 2f + Mathf.PI / 2f * (k - capRings - 1) / capRings;
                float yOff = top ? half : -half;
                var row = new int[radial + 1];
                for (int j = 0; j <= radial; j++)
                {
                    float th = G.TAU * j / radial;
                    var nrm = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    row[j] = b.Add(nrm * radius + Vector3.up * yOff, nrm);
                }
                rows.Add(row);
            }
            b.Rings(rows);
            mesh = b.Build(key);
            cache[key] = mesh;
            Shapes[mesh] = new ShapeInfo { kind = ShapeKind.Capsule, radius = radius, height = height };
            return mesh;
        }

        /// Сфера; если height не равна 2·radius — сплюснутая или вытянутая по вертикали.
        public static Mesh Sphere(float radius, float height = -1f, int radial = 10, int rings = 6)
        {
            if (height < 0f) height = radius * 2f;
            string key = string.Format("s{0:F3}/{1:F3}/{2}/{3}", radius, height, radial, rings);
            if (cache.TryGetValue(key, out var mesh)) return mesh;
            var b = new Builder();
            var rows = new List<int[]>();
            float hy = height / 2f;
            for (int i = 0; i <= rings; i++)
            {
                float phi = Mathf.PI * i / rings;
                var row = new int[radial + 1];
                for (int j = 0; j <= radial; j++)
                {
                    float th = G.TAU * j / radial;
                    var p = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th) * radius, Mathf.Cos(phi) * hy, Mathf.Sin(phi) * Mathf.Sin(th) * radius);
                    var nrm = new Vector3(p.x / (radius * radius), p.y / (hy * hy), p.z / (radius * radius)).normalized;
                    if (i == 0) nrm = Vector3.up;
                    if (i == rings) nrm = Vector3.down;
                    row[j] = b.Add(p, nrm);
                }
                rows.Add(row);
            }
            b.Rings(rows);
            mesh = b.Build(key);
            cache[key] = mesh;
            Shapes[mesh] = new ShapeInfo { kind = ShapeKind.Sphere, radius = radius, height = height };
            return mesh;
        }

        public static Mesh Cylinder(float top, float bottom, float height)
        {
            string key = string.Format("y{0:F3}/{1:F3}/{2:F3}", top, bottom, height);
            if (cache.TryGetValue(key, out var mesh)) return mesh;
            const int radial = 10;
            var b = new Builder();
            var rows = new List<int[]>();
            float h = height / 2f;
            float slope = (bottom - top) / height;
            for (int i = 0; i < 2; i++)
            {
                float r = i == 0 ? top : bottom;
                float y = i == 0 ? h : -h;
                var row = new int[radial + 1];
                for (int j = 0; j <= radial; j++)
                {
                    float th = G.TAU * j / radial;
                    var nrm = new Vector3(Mathf.Cos(th), slope, Mathf.Sin(th)).normalized;
                    row[j] = b.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r), nrm);
                }
                rows.Add(row);
            }
            b.Rings(rows);
            for (int i = 0; i < 2; i++)
            {
                float r = i == 0 ? top : bottom;
                if (r <= 0f) continue;
                float y = i == 0 ? h : -h;
                var up = i == 0 ? Vector3.up : Vector3.down;
                int c = b.Add(new Vector3(0, y, 0), up);
                int first = -1, prev = -1;
                for (int j = 0; j <= radial; j++)
                {
                    float th = G.TAU * j / radial;
                    int k = b.Add(new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r), up);
                    if (prev >= 0) b.Tri(c, prev, k);
                    if (first < 0) first = k;
                    prev = k;
                }
            }
            mesh = b.Build(key);
            cache[key] = mesh;
            Shapes[mesh] = new ShapeInfo { kind = ShapeKind.Cylinder, top = top, bottom = bottom, height = height };
            return mesh;
        }

        public static Mesh Box(Vector3 size)
        {
            string key = string.Format("b{0:F3}/{1:F3}/{2:F3}", size.x, size.y, size.z);
            if (cache.TryGetValue(key, out var mesh)) return mesh;
            var b = new Builder();
            Vector3 h = size / 2f;
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nrm in normals)
            {
                Vector3 u = Mathf.Abs(nrm.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(nrm, u);
                var c = Vector3.Scale(nrm, h);
                Vector3 hu = Vector3.Scale(u, h), hw = Vector3.Scale(w, h);
                int a = b.Add(c - hu - hw, nrm, new Vector2(0, 0));
                int bb = b.Add(c + hu - hw, nrm, new Vector2(1, 0));
                int cc = b.Add(c + hu + hw, nrm, new Vector2(1, 1));
                int d = b.Add(c - hu + hw, nrm, new Vector2(0, 1));
                b.Quad(a, bb, cc, d);
            }
            mesh = b.Build(key);
            cache[key] = mesh;
            Shapes[mesh] = new ShapeInfo { kind = ShapeKind.Box, size = size };
            return mesh;
        }

        /// Квадрат 2×2 м в плоскости XZ лицом вверх. Текстура: v растёт вдоль +Z.
        public static Mesh Plane2()
        {
            const string key = "plane2";
            if (cache.TryGetValue(key, out var mesh)) return mesh;
            var b = new Builder();
            int a = b.Add(new Vector3(-1, 0, -1), Vector3.up, new Vector2(0, 0));
            int c = b.Add(new Vector3(1, 0, -1), Vector3.up, new Vector2(1, 0));
            int d = b.Add(new Vector3(1, 0, 1), Vector3.up, new Vector2(1, 1));
            int e = b.Add(new Vector3(-1, 0, 1), Vector3.up, new Vector2(0, 1));
            b.Quad(a, c, d, e);
            mesh = b.Build(key);
            cache[key] = mesh;
            return mesh;
        }

        // --- Узлы --------------------------------------------------------------

        /// Добавляет меш в узел. pos и rot — в осях Godot, scale задаёт вытягивание.
        public static Transform Add(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 rot = default, Vector3? scale = null, string name = "Mesh")
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = G.P(pos);
            go.transform.localRotation = G.R(rot);
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return go.transform;
        }

        public static GJoint Joint(Transform parent, Vector3 pos, string name = "Joint")
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var j = go.AddComponent<GJoint>();
            j.pos = pos;
            j.Apply();
            return j;
        }

        public static Transform Node(Transform parent, string name, Vector3 pos = default)
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
            }
            go.transform.localPosition = G.P(pos);
            return go.transform;
        }

        /// Ставит слой всем объектам внутри узла (свой персонаж не виден своей камере).
        public static void SetLayers(Transform node, int layer)
        {
            node.gameObject.layer = layer;
            foreach (Transform c in node) SetLayers(c, layer);
        }

        public static void NoShadows(Transform node)
        {
            foreach (var r in node.GetComponentsInChildren<MeshRenderer>(true))
                r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public static Light PointLight(Transform parent, Vector3 worldPos, float range, float intensity, bool shadows = false)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = intensity;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }

        // --- Текст в мире ------------------------------------------------------

        static Font font;
        public static Font Font
        {
            get
            {
                // Unity 2022.2 и новее — LegacyRuntime.ttf, раньше встроенный шрифт назывался Arial.ttf.
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return font;
            }
        }

        /// Надпись, всегда повёрнутая к камере. height — высота букв в метрах.
        public static Billboard Label(Transform parent, string text, Vector3 localPos, float height, bool throughWalls = false, int layer = -1)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.layer = layer >= 0 ? layer : parent.gameObject.layer;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = 64;
            tm.characterSize = 0.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.text = text;
            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(TextShader);
            mat.mainTexture = Font.material.mainTexture;
            mat.SetFloat("_ZTest", throughWalls ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
            mat.renderQueue = throughWalls ? 4000 : 3000;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var bb = go.AddComponent<Billboard>();
            bb.height = height;
            return bb;
        }
    }

    /// Надпись поворачивается к той камере, которая её сейчас рисует,
    /// поэтому на каждой части экрана текст читается прямо.
    public class Billboard : MonoBehaviour
    {
        public float height = 0.4f;
        bool fitted;
        Renderer rend;
        TextMesh tm;

        void Awake()
        {
            rend = GetComponent<Renderer>();
            tm = GetComponent<TextMesh>();
            Font.textureRebuilt += OnFontRebuilt;
        }

        void OnDestroy() { Font.textureRebuilt -= OnFontRebuilt; }

        void OnFontRebuilt(Font f)
        {
            if (tm != null && f == tm.font && rend != null)
                rend.sharedMaterial.mainTexture = f.material.mainTexture;
        }

        void LateUpdate()
        {
            if (fitted || rend == null) return;
            // Подгоняем масштаб так, чтобы высота строки была ровно height метров.
            float h = rend.localBounds.size.y;
            if (h > 0.0001f)
            {
                var parentScale = transform.parent ? transform.parent.lossyScale.y : 1f;
                transform.localScale = Vector3.one * (height / h / Mathf.Max(parentScale, 0.0001f));
                fitted = true;
            }
        }

        // URP: перед отрисовкой каждой камеры разворачиваем все надписи к ней.
        static readonly List<Billboard> all = new List<Billboard>();
        static bool hooked;

        void OnEnable()
        {
            all.Add(this);
            if (!hooked)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                hooked = true;
            }
        }

        void OnDisable() { all.Remove(this); }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            var rot = cam.transform.rotation;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null) all[i].transform.rotation = rot;
        }

        // Встроенный конвейер (на случай, если URP не назначен).
        void OnWillRenderObject()
        {
            var cam = Camera.current;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
