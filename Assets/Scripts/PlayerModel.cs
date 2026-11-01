using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Фигура игрока, которую видят напарники. У каждого из четырёх свой силуэт,
    /// чтобы различать их в чёрно-белом мире без цвета:
    /// 1 — светлая куртка, кепка и рюкзак; 2 — тёмное длинное пальто и светлый хвост волос;
    /// 3 — широкий, в каске и жилете со светоотражающими полосами, с бородой;
    /// 4 — высокий, в капюшоне и с тёмной повязкой на лице.
    public class PlayerModel : MonoBehaviour
    {
        public class Style
        {
            public float width, height, top, pants, shoes, skin;
        }

        public static readonly Style[] Styles =
        {
            new Style { width = 1f, height = 1f, top = 0.86f, pants = 0.2f, shoes = 0.95f, skin = 0.78f },
            new Style { width = 0.92f, height = 0.98f, top = 0.12f, pants = 0.62f, shoes = 0.1f, skin = 0.82f },
            new Style { width = 1.2f, height = 1f, top = 0.3f, pants = 0.45f, shoes = 0.08f, skin = 0.7f },
            new Style { width = 0.95f, height = 1.06f, top = 0.52f, pants = 0.85f, shoes = 0.15f, skin = 0.76f },
        };

        public int style;
        readonly List<GJoint> legs = new List<GJoint>();
        readonly List<GJoint> arms = new List<GJoint>();
        GJoint torso, head, gun;
        float phase, down;
        int layer;

        /// Собирает фигуру. layer — слой видимости (своя камера его не рисует).
        public void Build(int index, int bodyLayer)
        {
            layer = bodyLayer;
            gameObject.layer = layer;
            style = index % Styles.Length;
            var st = Styles[style];
            float w = st.width;
            transform.localScale = Vector3.one * st.height;
            var top = Lit(st.top);
            var pants = Lit(st.pants);
            var skin = Lit(st.skin);
            var shoes = Lit(st.shoes);
            var dark = Lit(0.06f);

            foreach (float x in new[] { -0.11f * w, 0.11f * w })
            {
                var leg = MeshKit.Joint(transform, new Vector3(x, 0.9f, 0), "Leg");
                MeshKit.Add(leg.transform, MeshKit.Capsule(0.08f * w, 0.48f), pants, new Vector3(0, -0.22f, 0));
                MeshKit.Add(leg.transform, MeshKit.Capsule(0.07f * w, 0.46f), pants, new Vector3(0, -0.64f, 0));
                MeshKit.Add(leg.transform, MeshKit.Box(new Vector3(0.11f, 0.08f, 0.26f)), shoes, new Vector3(0, -0.87f, -0.05f));
                legs.Add(leg);
            }

            torso = MeshKit.Joint(transform, new Vector3(0, 0.9f, 0), "Torso");
            var t = torso.transform;
            MeshKit.Add(t, MeshKit.Cylinder(0.17f * w, 0.16f * w, 0.2f), pants, new Vector3(0, 0.03f, 0), default, new Vector3(1, 1, 0.75f));
            MeshKit.Add(t, MeshKit.Cylinder(0.21f * w, 0.17f * w, 0.52f), top, new Vector3(0, 0.33f, 0), default, new Vector3(1, 1, 0.72f));
            MeshKit.Add(t, MeshKit.Capsule(0.08f, 0.5f * w), top, new Vector3(0, 0.56f, 0), new Vector3(0, 0, Mathf.PI / 2f));
            MeshKit.Add(t, MeshKit.Cylinder(0.05f, 0.06f, 0.12f), skin, new Vector3(0, 0.63f, 0));

            head = MeshKit.Joint(t, new Vector3(0, 0.64f, 0), "Head");
            var h = head.transform;
            MeshKit.Add(h, MeshKit.Sphere(0.12f), skin, new Vector3(0, 0.13f, 0), default, new Vector3(0.9f, 1.05f, 1f));
            MeshKit.Add(h, MeshKit.Box(new Vector3(0.03f, 0.05f, 0.03f)), skin, new Vector3(0, 0.12f, -0.12f));
            foreach (float x in new[] { -0.045f, 0.045f })
            {
                MeshKit.Add(h, MeshKit.Sphere(0.017f), dark, new Vector3(x, 0.16f, -0.103f));
                MeshKit.Add(h, MeshKit.Box(new Vector3(0.02f, 0.05f, 0.035f)), skin, new Vector3(x * 2.4f, 0.13f, 0f));
            }

            foreach (float side in new[] { -1f, 1f })
            {
                var arm = MeshKit.Joint(t, new Vector3(0.25f * w * side, 0.54f, 0), "Arm");
                MeshKit.Add(arm.transform, MeshKit.Capsule(0.062f * w, 0.32f), top, new Vector3(0, -0.15f, 0));
                MeshKit.Add(arm.transform, MeshKit.Capsule(0.055f * w, 0.3f), top, new Vector3(0, -0.42f, 0));
                MeshKit.Add(arm.transform, MeshKit.Box(new Vector3(0.07f, 0.09f, 0.05f)), skin, new Vector3(0, -0.6f, 0));
                arms.Add(arm);
            }

            // Фонарь на плече горит у живых: игрока видно в темноте издалека, зомби — нет.
            MeshKit.Add(t, MeshKit.Cylinder(0.035f, 0.035f, 0.07f), Lit(0.1f), new Vector3(0.2f * w, 0.6f, -0.1f), new Vector3(Mathf.PI / 2f, 0, 0));
            MeshKit.Add(t, MeshKit.Cylinder(0.03f, 0.03f, 0.01f), Lamp, new Vector3(0.2f * w, 0.6f, -0.14f), new Vector3(Mathf.PI / 2f, 0, 0));
            var glow = MeshKit.PointLight(t, Vector3.zero, 1.4f, 0.5f);
            glow.transform.localPosition = G.P(0.2f * w, 0.6f, -0.3f);

            gun = MeshKit.Joint(t, Vector3.zero, "Gun");
            AddStyleDetails(w, st);
            SetWeapon("knife");
            MeshKit.SetLayers(transform, layer);
        }

        void AddStyleDetails(float w, Style st)
        {
            var dark = Lit(0.08f);
            var light = Lit(0.96f);
            var h = head.transform;
            var t = torso.transform;
            switch (style)
            {
                case 0:
                    // Кепка козырьком вперёд и рюкзак.
                    MeshKit.Add(h, MeshKit.Sphere(0.125f), dark, new Vector3(0, 0.19f, 0f), default, new Vector3(0.95f, 0.55f, 1f));
                    MeshKit.Add(h, MeshKit.Box(new Vector3(0.2f, 0.02f, 0.12f)), dark, new Vector3(0, 0.2f, -0.15f), new Vector3(-0.1f, 0, 0));
                    MeshKit.Add(t, MeshKit.Box(new Vector3(0.32f, 0.38f, 0.16f)), Lit(0.3f), new Vector3(0, 0.36f, 0.2f));
                    MeshKit.Add(t, MeshKit.Box(new Vector3(0.26f, 0.14f, 0.06f)), Lit(0.3f), new Vector3(0, 0.22f, 0.3f));
                    break;
                case 1:
                    // Длинное пальто до колен и светлый хвост волос.
                    MeshKit.Add(t, MeshKit.Cylinder(0.2f, 0.26f, 0.5f), Lit(st.top), new Vector3(0, -0.2f, 0), default, new Vector3(1, 1, 0.75f));
                    MeshKit.Add(t, MeshKit.Box(new Vector3(0.04f, 0.5f, 0.02f)), Lit(0.4f), new Vector3(0, 0.2f, -0.13f));
                    MeshKit.Add(h, MeshKit.Sphere(0.128f), light, new Vector3(0, 0.17f, 0.015f), default, new Vector3(0.95f, 0.85f, 1f));
                    MeshKit.Add(h, MeshKit.Capsule(0.045f, 0.3f), light, new Vector3(0, 0.02f, 0.15f), new Vector3(0.3f, 0, 0));
                    break;
                case 2:
                    // Каска с полями, жилет с полосами, борода.
                    MeshKit.Add(h, MeshKit.Sphere(0.135f), light, new Vector3(0, 0.2f, 0), default, new Vector3(1, 0.7f, 1.05f));
                    MeshKit.Add(h, MeshKit.Cylinder(0.17f, 0.17f, 0.02f), light, new Vector3(0, 0.17f, 0));
                    MeshKit.Add(h, MeshKit.Box(new Vector3(0.17f, 0.1f, 0.08f)), dark, new Vector3(0, 0.05f, -0.08f));
                    foreach (float y in new[] { 0.22f, 0.38f })
                        MeshKit.Add(t, MeshKit.Cylinder(0.205f * w, 0.195f * w, 0.04f), light, new Vector3(0, y, 0), default, new Vector3(1.02f, 1, 0.74f));
                    break;
                case 3:
                    // Капюшон и тёмная повязка на лице.
                    MeshKit.Add(h, MeshKit.Sphere(0.15f), Lit(0.3f), new Vector3(0, 0.15f, 0.055f), default, new Vector3(1f, 1.05f, 1f));
                    MeshKit.Add(t, MeshKit.Sphere(0.14f), Lit(0.3f), new Vector3(0, 0.6f, 0.1f), default, new Vector3(1.2f, 0.6f, 0.8f));
                    MeshKit.Add(h, MeshKit.Box(new Vector3(0.2f, 0.08f, 0.1f)), dark, new Vector3(0, 0.07f, -0.07f));
                    MeshKit.Add(t, MeshKit.Box(new Vector3(0.2f, 0.12f, 0.05f)), Lit(0.35f), new Vector3(0, 0.2f, -0.14f));
                    break;
            }
        }

        // Материал живого человека: чистый тон и лёгкое свечение, чтобы персонаж не сливался
        // с серыми гниющими зомби даже в тёмной комнате.
        static readonly Dictionary<int, Material> lit = new Dictionary<int, Material>();

        public static Material Lit(float value)
        {
            int key = Mathf.RoundToInt(value * 100f);
            if (!lit.TryGetValue(key, out var m))
            {
                var c = new Color(value, value, value);
                m = MeshKit.Lit(c, c * 0.25f + new Color(0.04f, 0.04f, 0.04f), 0f, 0.3f);
                lit[key] = m;
            }
            return m;
        }

        static Material lamp;
        static Material Lamp
        {
            get
            {
                if (lamp == null) lamp = MeshKit.Lit(Color.white, Color.white * 4f);
                return lamp;
            }
        }

        /// Оружие в руках, видимое напарникам.
        public void SetWeapon(string weapon)
        {
            if (gun == null) return;
            foreach (Transform c in gun.transform) Destroy(c.gameObject);
            var mat = MeshKit.Shade(0.12f, 0.5f);
            float length = weapon == "knife" ? 0f : weapon == "smg" ? 0.45f : weapon == "shotgun" ? 0.75f : 0.2f;
            if (length > 0f)
            {
                var b = MeshKit.Add(gun.transform, MeshKit.Box(new Vector3(0.06f, 0.09f, length)), mat, new Vector3(0.14f, 0.44f, -0.45f - length * 0.3f));
                b.gameObject.layer = layer;
            }
        }

        /// Ходьба, прицеливание и падение. speed — горизонтальная скорость, pitch — наклон взгляда
        /// (вверх — плюс), yaw — поворот в градусах Unity.
        /// reload — доля пройденной перезарядки (0..1) или -1: оружие опускается и наклоняется,
        /// левая рука уходит к магазину. kick — удар ногой: 1 в начале, к 0 нога возвращается.
        public void Animate(float delta, float speed, float yaw, float pitch, bool downed, float reload = -1f, float kick = 0f)
        {
            phase += delta * speed * 2.2f;
            float stride = Mathf.Clamp01(speed / 4.5f);
            float s = Mathf.Sin(phase);
            for (int i = 0; i < legs.Count; i++) legs[i].rot.x = s * 0.55f * stride * (i == 0 ? 1f : -1f);
            // Обе руки держат оружие перед собой и следуют за взглядом.
            float aim = 1.35f + pitch * 0.8f;
            arms[0].rot = new Vector3(aim - 0.05f, 0, 0.42f);
            arms[1].rot = new Vector3(aim, 0, -0.08f);
            head.rot.x = pitch * 0.6f;
            gun.rot = new Vector3(pitch * 0.8f, 0, 0);
            if (reload >= 0f)
            {
                float w = Mathf.Sin(reload * Mathf.PI);
                gun.rot += new Vector3(-0.5f, 0, 0.6f) * w;
                arms[0].rot = new Vector3(aim - 0.05f - 0.8f * w, 0, 0.42f - 0.3f * w + Mathf.Sin(reload * 18f) * 0.08f * w);
                head.rot.x -= 0.35f * w;
            }
            torso.rot.x = -0.05f - stride * 0.08f;
            if (kick > 0f)
            {
                float t = 1f - kick;
                float o = G.Smoothstep(0f, 0.1f, t) * (1f - G.Smoothstep(0.35f, 0.75f, t));
                legs[1].rot.x = 1.5f * o;
                legs[0].rot.x = -0.15f * o;
                torso.rot.x += -0.25f * o;
            }
            down = Mathf.MoveTowards(down, downed ? 1f : 0f, delta * 3f);
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * G.R(down * 1.45f, 0f, 0f);
            transform.localPosition = new Vector3(0f, Mathf.Abs(s) * 0.04f * stride + down * 0.2f, 0f);
            foreach (var l in legs) l.Apply();
            foreach (var a in arms) a.Apply();
            torso.Apply();
            head.Apply();
            gun.Apply();
        }
    }
}
