using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Формы кровавых пятен. Рисуются один раз при первом использовании в маленькие текстуры
    /// с прозрачностью и кладутся на пол и стены плоскими квадратами:
    ///   splat — клякса с рваным краем, лучами и брызгами вокруг;
    ///   streak — вытянутый потёк с каплями по направлению выстрела;
    ///   drops — россыпь капель разного размера;
    ///   pool — большая неровная лужа;
    ///   smear — размазанный след (ползущий зомби, тело, которое протащило по полу).
    public static class BloodStains
    {
        public const int SIZE = 128;
        const int VARIANTS = 3;
        public static readonly string[] Kinds = { "splat", "streak", "drops", "pool", "smear" };

        /// Свежая кровь яркая, подсохшая темнее: фильтр оставляет оба тона красными.
        static readonly Color[] Tones = { new Color(0.9f, 0f, 0.03f), new Color(0.62f, 0f, 0.02f), new Color(0.45f, 0f, 0.015f) };

        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        public static Mesh Mesh => MeshKit.Plane2();

        /// Случайный материал пятна данного вида.
        public static Material Material(string kind)
        {
            int variant = UnityEngine.Random.Range(0, VARIANTS);
            int tone = UnityEngine.Random.Range(0, Tones.Length);
            string key = kind + "/" + variant + "/" + tone;
            if (!materials.TryGetValue(key, out var m))
            {
                m = MeshKit.Unlit(Tones[tone], Texture(kind, variant), 0.5f, true);
                m.name = kind;
                materials[key] = m;
            }
            return m;
        }

        public static Texture2D Texture(string kind, int variant)
        {
            string key = kind + variant;
            if (textures.TryGetValue(key, out var cached)) return cached;
            var rng = new Rng(key.Length * 977 + kind[0] * 131 + kind[kind.Length - 1] * 17 + variant * 7919);
            var mask = new byte[SIZE * SIZE];
            switch (kind)
            {
                case "splat":
                    Blob(mask, rng, Vector2.zero, 0.42f, 0.18f, 7);
                    for (int i = rng.RangeI(4, 8); i > 0; i--)
                        Ray(mask, rng, rng.Range(0f, G.TAU), 0.35f, rng.Range(0.7f, 0.95f), rng.Range(0.03f, 0.06f));
                    Satellites(mask, rng, rng.RangeI(8, 16), 0.55f, 0.97f, 0.015f, 0.06f);
                    break;
                case "streak":
                {
                    // Капля-«головастик»: толстое начало, хвост и брызги вдоль направления (+V текстуры).
                    float y = -0.75f, r = 0.22f;
                    while (y < 0.9f && r > 0.012f)
                    {
                        Disc(mask, new Vector2(rng.Range(-0.03f, 0.03f), y), r);
                        y += r * 0.6f;
                        r *= rng.Range(0.86f, 0.95f);
                    }
                    for (int i = rng.RangeI(6, 12); i > 0; i--)
                    {
                        float t = rng.Range(-0.2f, 1f);
                        Disc(mask, new Vector2(rng.Range(-0.25f, 0.25f) * (1f - t * 0.5f), -0.5f + t * 1.4f), rng.Range(0.012f, 0.045f));
                    }
                    break;
                }
                case "drops":
                    for (int i = rng.RangeI(10, 18); i > 0; i--)
                    {
                        float a = rng.Range(0f, G.TAU);
                        float d = Mathf.Sqrt(rng.Randf()) * 0.85f;
                        var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                        Blob(mask, rng, c, rng.Range(0.025f, 0.11f), 0.12f, 4);
                    }
                    break;
                case "pool":
                    Blob(mask, rng, Vector2.zero, 0.5f, 0.14f, 4);
                    Blob(mask, rng, new Vector2(rng.Range(-0.3f, 0.3f), rng.Range(-0.3f, 0.3f)), 0.3f, 0.2f, 5);
                    Satellites(mask, rng, rng.RangeI(3, 7), 0.65f, 0.95f, 0.02f, 0.07f);
                    break;
                case "smear":
                {
                    // Сплошная мазня с рваными краями: по краям — полосы от пальцев и одежды,
                    // концы обрываются неровно, рядом брызги.
                    float p0 = rng.Range(0f, G.TAU), p1 = rng.Range(0f, G.TAU), p2 = rng.Range(0f, G.TAU);
                    for (int px = 0; px < SIZE; px++)
                        for (int py = 0; py < SIZE; py++)
                        {
                            float u = px / (float)SIZE * 2f - 1f;
                            float v = py / (float)SIZE * 2f - 1f;
                            float width = 0.42f + 0.1f * Mathf.Sin(v * 5f + p0) + 0.05f * Mathf.Sin(v * 13f + p1);
                            float endV = 0.8f + 0.12f * Mathf.Sin(u * 9f + p2);
                            if (Mathf.Abs(v) > endV) continue;
                            float au = Mathf.Abs(u) / width;
                            if (au < 0.62f) mask[py * SIZE + px] = 255;
                            else if (au < 1f)
                            {
                                float streaks = Mathf.Sin(u * 38f + Mathf.Sin(v * 4f + p1) * 1.5f);
                                if (streaks > -0.1f + (au - 0.62f) * 2f) mask[py * SIZE + px] = 255;
                            }
                        }
                    Satellites(mask, rng, rng.RangeI(4, 8), 0.5f, 0.9f, 0.015f, 0.04f);
                    break;
                }
            }
            var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
            {
                name = kind,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px32 = new Color32[SIZE * SIZE];
            for (int i = 0; i < px32.Length; i++) px32[i] = new Color32(255, 255, 255, mask[i]);
            tex.SetPixels32(px32);
            tex.Apply();
            textures[key] = tex;
            return tex;
        }

        /// Доля закрашенных пикселей (для проверки, что форма нарисована).
        public static float Coverage(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int filled = 0;
            foreach (var p in px) if (p.a > 127) filled++;
            return filled / (float)px.Length;
        }

        /// Пятно с неровным краем: радиус колеблется по нескольким гармоникам.
        static void Blob(byte[] mask, Rng rng, Vector2 center, float radius, float rough, int harmonics)
        {
            var amps = new float[harmonics, 3];
            for (int k = 0; k < harmonics; k++)
            {
                amps[k, 0] = k + 2;
                amps[k, 1] = rng.Range(0.2f, 1f) * rough / (1f + k * 0.3f);
                amps[k, 2] = rng.Range(0f, G.TAU);
            }
            float reach = radius * (1f + rough * 2f);
            EachPixel(mask, center, reach, p =>
            {
                var d = p - center;
                float a = Mathf.Atan2(d.y, d.x);
                float r = radius;
                for (int h = 0; h < harmonics; h++) r += radius * amps[h, 1] * Mathf.Sin(amps[h, 0] * a + amps[h, 2]);
                return d.magnitude < r;
            });
        }

        /// Луч-выплеск от центра к краю, сужается к концу и заканчивается каплей.
        static void Ray(byte[] mask, Rng rng, float angle, float start, float end, float width)
        {
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            const int steps = 12;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Disc(mask, dir * Mathf.Lerp(start, end, t), width * (1f - t * 0.7f));
            }
            Disc(mask, dir * (end + width), width * rng.Range(0.6f, 1.1f));
        }

        static void Satellites(byte[] mask, Rng rng, int count, float near, float far, float small, float big)
        {
            for (int i = 0; i < count; i++)
            {
                float a = rng.Range(0f, G.TAU);
                Disc(mask, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rng.Range(near, far), rng.Range(small, big));
            }
        }

        static void Disc(byte[] mask, Vector2 center, float radius)
        {
            EachPixel(mask, center, radius, p => Vector2.Distance(p, center) < radius);
        }

        /// Закрашивает пиксели в квадрате вокруг центра, для которых inside(p) истинно.
        static void EachPixel(byte[] mask, Vector2 center, float reach, Func<Vector2, bool> inside)
        {
            int x0 = Mathf.Clamp((int)((center.x - reach + 1f) * 0.5f * SIZE), 0, SIZE - 1);
            int x1 = Mathf.Clamp((int)((center.x + reach + 1f) * 0.5f * SIZE) + 1, 0, SIZE - 1);
            int y0 = Mathf.Clamp((int)((center.y - reach + 1f) * 0.5f * SIZE), 0, SIZE - 1);
            int y1 = Mathf.Clamp((int)((center.y + reach + 1f) * 0.5f * SIZE) + 1, 0, SIZE - 1);
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    var p = new Vector2((px + 0.5f) / SIZE * 2f - 1f, (py + 0.5f) / SIZE * 2f - 1f);
                    if (inside(p)) mask[py * SIZE + px] = 255;
                }
        }
    }
}
