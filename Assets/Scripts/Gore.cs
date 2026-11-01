using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieCoop
{
    /// Кровь и расчленение: брызги, пятна на полу и стенах по направлению выстрела,
    /// отлетающие части тела с кровавым следом и короткая пауза (hit-stop) на убийстве.
    public static class Gore
    {
        const int MAX_STAINS = 400;
        const int MAX_DEBRIS = 80;

        static Material bloodMat, brainMat;
        static int layer;
        public static readonly List<GameObject> Stains = new List<GameObject>();
        static readonly List<GameObject> debris = new List<GameObject>();

        public static Material BloodMaterial
        {
            get
            {
                if (bloodMat == null) bloodMat = MeshKit.Unlit(new Color(0.9f, 0f, 0.03f));
                return bloodMat;
            }
        }

        /// Слегка розоватые мозги: фильтр оставляет им немного красного, но не как у крови.
        public static Material BrainMaterial
        {
            get
            {
                if (brainMat == null) brainMat = MeshKit.Lit(new Color(0.78f, 0.5f, 0.52f), new Color(0.12f, 0.05f, 0.05f), 0f, 0.65f);
                return brainMat;
            }
        }

        public static void Clear()
        {
            Stains.Clear();
            debris.Clear();
        }

        static void Particles(Vector3 point, Vector3 dir, int amount, float speed)
        {
            if (Fx.Instance == null) return;
            Fx.Instance.Burst(Fx.Kind.Drop, point, dir, amount, speed * 0.4f, speed, 35f, 0.9f, 14f, 0.5f, 1.3f);
        }

        /// Небольшое попадание: брызги по направлению пули и пятно на стене за целью.
        public static void Splash(Vector3 point, Vector3 dir, int amount)
        {
            Particles(point, dir, amount, 4f);
            var d = dir.normalized;
            // За целью — потёк, вытянутый по полёту пули; под ней — россыпь капель.
            StainRay(point, point + d * 4f, G.Rand(0.18f, 0.35f), "streak", d);
            StainRay(point, point + Vector3.down * 3f, G.Rand(0.15f, 0.3f), G.Pick("drops", "drops", "splat"), d);
        }

        /// Отрыв или смерть: много крови, несколько пятен вокруг.
        public static void Burst(Vector3 point, Vector3 dir, int amount)
        {
            Particles(point, dir + Vector3.up * 0.6f, amount, 6f);
            var d = dir.normalized;
            if (d.y < 0.7f) StainRay(point, point + d * 5f, G.Rand(0.35f, 0.65f), "splat", d);
            // Под телом лужа, вокруг — кляксы и длинные потёки по направлению удара.
            StainRay(point, point + Vector3.down * 3f, G.Rand(0.4f, 0.7f), "pool");
            for (int i = 0; i < 3; i++)
            {
                var spread = new Vector3(G.Rand(-1.5f, 1.5f), 0f, G.Rand(-1.5f, 1.5f)) + new Vector3(d.x, 0f, d.z);
                StainRay(point + spread * 0.5f, point + spread + Vector3.down * 3f, G.Rand(0.2f, 0.5f), G.Pick("splat", "streak", "drops"), spread);
            }
        }

        /// Превращает часть тела в отдельный физический объект, который отлетает от импульса.
        /// Часть — меш или шарнир с несколькими мешами; размер куска берётся из PartMeta.
        public static Rigidbody Detach(Transform part, Vector3 impulse)
        {
            var go = new GameObject("Debris " + part.name);
            go.layer = Layers.Debris;
            go.transform.SetParent(GameRoot.World, false);
            go.transform.SetPositionAndRotation(part.position, part.rotation);
            var copy = Object.Instantiate(part.gameObject, go.transform, false);
            copy.name = part.name;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = part.localScale;
            Strip(copy.transform);

            var col = go.AddComponent<BoxCollider>();
            var meta = part.GetComponent<PartMeta>();
            if (meta != null)
            {
                col.size = meta.hitSize;
                col.center = G.P(meta.hitCenter);
            }
            else
            {
                col.size = Vector3.one * 0.2f;
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 2f;
            rb.maxAngularVelocity = 20f;
            rb.AddForce(impulse, ForceMode.Impulse);
            rb.angularVelocity = new Vector3(G.Rand(-9f, 9f), G.Rand(-9f, 9f), G.Rand(-9f, 9f));
            BloodTrail(go.transform);
            Track(debris, go, MAX_DEBRIS);
            return rb;
        }

        /// Убирает из копии зоны попадания и источники крови.
        static void Strip(Transform node)
        {
            foreach (var c in node.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var z in node.GetComponentsInChildren<HitZone>(true)) Object.DestroyImmediate(z);
            foreach (var e in node.GetComponentsInChildren<Emitter>(true)) Object.DestroyImmediate(e);
            foreach (var s in node.GetComponentsInChildren<Stretch>(true)) Object.DestroyImmediate(s);
        }

        /// Рана, из которой толчками бьёт кровь: брызги летят из точки на теле и падают на пол.
        /// localPos и outDir — в осях Godot узла, к которому прикреплена рана.
        public static Emitter Bleed(Transform holder, Vector3 localPos, Vector3 outDir, float seconds, float strength = 1f)
        {
            var node = MeshKit.Node(holder, "Bleed", localPos);
            var e = node.gameObject.AddComponent<Emitter>();
            e.kind = Fx.Kind.Drop;
            e.amount = Mathf.Floor(18f * strength);
            e.lifetime = 0.7f;
            e.localDir = G.P(outDir).normalized;
            e.spread = 20f;
            e.speedMin = 0.8f * strength;
            e.speedMax = 2.2f * strength;
            e.gravity = 12f;
            e.scaleMin = 0.4f;
            e.scaleMax = 0.9f;
            e.pulse = true;
            e.stopAfter = seconds;
            return e;
        }

        /// Вскрытый череп: серо-розовые мозги наружу, они стекают по лицу и падают комками.
        /// top — в осях Godot головы.
        public static void Brains(Transform head, Vector3 top)
        {
            var mat = BrainMaterial;
            Vector3[] offsets =
            {
                new Vector3(-0.045f, 0.05f, 0f), new Vector3(0.045f, 0.055f, -0.02f), new Vector3(0f, 0.07f, 0.03f),
                new Vector3(0.01f, 0.045f, -0.06f), new Vector3(-0.02f, 0.03f, 0.06f),
            };
            foreach (var o in offsets)
                MeshKit.Add(head, MeshKit.Sphere(0.06f, 0.09f, 8, 4), mat, top + o, default, null, "Lobe");
            MeshKit.Add(head, MeshKit.Cylinder(0.11f, 0.12f, 0.03f), BloodMaterial, top + Vector3.up * 0.02f, default, null, "Rim");
            // Струйка мозгов по щеке: медленно вытягивается вниз.
            var strand = MeshKit.Add(head, MeshKit.Capsule(0.03f, 0.22f), mat, top + new Vector3(0.08f, -0.04f, -0.06f), default, new Vector3(1f, 0.15f, 1f), "Strand");
            var st = strand.gameObject.AddComponent<Stretch>();
            st.toScaleY = 1f;
            st.toPosY = top.y - 0.13f;
            st.duration = 3f;
            // Комки мозгов и кровь капают, пока зомби жив.
            var dripNode = MeshKit.Node(head, "Drip", top + new Vector3(0f, -0.02f, -0.05f));
            var drip = dripNode.gameObject.AddComponent<Emitter>();
            drip.kind = Fx.Kind.Brain;
            drip.amount = 8f;
            drip.lifetime = 1f;
            drip.localDir = G.P(new Vector3(0f, -1f, -0.4f)).normalized;
            drip.spread = 45f;
            drip.speedMin = 0.2f;
            drip.speedMax = 0.8f;
            drip.gravity = 9f;
            drip.scaleMin = 0.6f;
            drip.scaleMax = 1.4f;
            Bleed(head, top, new Vector3(0f, 1f, -0.3f), 6f, 0.7f);
        }

        /// Короткое замедление времени на фатальном попадании.
        public static void HitStop(float duration)
        {
            if (GameRoot.Instance != null) GameRoot.Instance.HitStop(duration);
        }

        static void BloodTrail(Transform rb)
        {
            var e = rb.gameObject.AddComponent<Emitter>();
            e.kind = Fx.Kind.Drop;
            e.amount = 24f;
            e.lifetime = 0.6f;
            e.localDir = Vector3.up;
            e.spread = 60f;
            e.speedMin = 0.5f;
            e.speedMax = 1.5f;
            e.gravity = 14f;
            e.scaleMin = 1f;
            e.scaleMax = 1f;
            e.stopAfter = 1.2f;
        }

        /// Пятно там, куда луч от from к to упёрся в стену или пол. kind — форма из BloodStains,
        /// along — направление полёта крови: потёки и мазки вытягиваются вдоль него.
        public static GameObject StainRay(Vector3 from, Vector3 to, float radius, string kind = "splat", Vector3 along = default)
        {
            var delta = to - from;
            if (!Physics.Raycast(from, delta.normalized, out var hit, delta.magnitude, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                return null;
            var normal = hit.normal;
            // Ось Z квадрата — «вниз по текстуре»: разворачиваем её по направлению брызг,
            // на стене без направления кровь стекает вниз, на полу — случайный поворот.
            var flow = along - normal * Vector3.Dot(along, normal);
            if (flow.magnitude < 0.05f && Mathf.Abs(normal.y) < 0.7f)
                flow = Vector3.down - normal * Vector3.Dot(Vector3.down, normal);
            Quaternion rot;
            if (flow.magnitude > 0.05f) rot = Quaternion.LookRotation(flow.normalized, normal);
            else rot = Quaternion.AngleAxis(G.Rand(0f, 360f), normal) * Quaternion.FromToRotation(Vector3.up, normal);

            var go = new GameObject("Stain " + kind);
            go.transform.SetParent(GameRoot.World, false);
            go.AddComponent<MeshFilter>().sharedMesh = BloodStains.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BloodStains.Material(kind);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // Каждое следующее пятно чуть выше прежних, чтобы наложенные не мерцали.
            layer = (layer + 1) % 40;
            go.transform.SetPositionAndRotation(hit.point + normal * (0.004f + layer * 0.0002f), rot);
            float stretch = 1f;
            if (kind == "streak") stretch = G.Rand(1.6f, 2.6f);
            else if (kind == "smear") stretch = G.Rand(1.2f, 1.8f);
            float w = radius * G.Rand(0.8f, 1.25f);
            go.transform.localScale = new Vector3(w, 1f, w * stretch * G.Rand(0.9f, 1.15f));
            Track(Stains, go, MAX_STAINS);
            return go;
        }

        /// Пятно на полу под точкой (капли с ран, след ползущего).
        public static GameObject FloorStain(Vector3 point, string kind, float radius, Vector3 along = default)
        {
            return StainRay(point + Vector3.up * 0.3f, point + Vector3.down * 2f, radius, kind, along);
        }

        static void Track(List<GameObject> list, GameObject obj, int limit)
        {
            list.Add(obj);
            while (list.Count > limit)
            {
                var old = list[0];
                list.RemoveAt(0);
                if (old != null) Object.Destroy(old);
            }
        }
    }

    /// Плавно вытягивает струйку мозгов вниз по лицу.
    public class Stretch : MonoBehaviour
    {
        public float toScaleY = 1f;
        public float toPosY;
        public float duration = 3f;
        float t, fromScaleY, fromPosY;

        void Start()
        {
            fromScaleY = transform.localScale.y;
            fromPosY = transform.localPosition.y;
        }

        void Update()
        {
            t = Mathf.Min(1f, t + Time.deltaTime / duration);
            var s = transform.localScale;
            s.y = Mathf.Lerp(fromScaleY, toScaleY, t);
            transform.localScale = s;
            var p = transform.localPosition;
            p.y = Mathf.Lerp(fromPosY, toPosY, t);
            transform.localPosition = p;
            if (t >= 1f) enabled = false;
        }
    }
}
