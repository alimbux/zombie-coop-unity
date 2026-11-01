using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Звуки, синтезированные кодом: без сторонних файлов и лицензий.
    /// Выстрел, попадание, хруст отрыва, мычание, рык и сдавленный крик зомби, удар по игроку, щелчки затвора.
    /// У каждого звука несколько вариантов, чтобы повторы не резали слух.
    public static class Sfx
    {
        const int RATE = 22050;
        const int MAX_GROANS = 6;
        /// Общая громкость: 0 дБ звучат на 0.7, чтобы громким звукам было куда расти.
        const float MASTER = 0.7f;

        static readonly Dictionary<string, AudioClip[]> cache = new Dictionary<string, AudioClip[]>();
        static int activeGroans;

        public static readonly string[] All = { "shot", "splat", "crunch", "groan", "growl", "pain", "hurt", "click", "reload", "slash", "pickup" };

        /// Генерирует все звуки заранее, чтобы первый выстрел не подтормаживал.
        public static void PreloadAll()
        {
            foreach (var s in All) Clip(s);
        }

        public static AudioClip Clip(string sound)
        {
            if (!cache.TryGetValue(sound, out var variants))
            {
                variants = new AudioClip[4];
                for (int v = 0; v < 4; v++)
                    variants[v] = ToClip(sound + v, Synth(sound, v));
                cache[sound] = variants;
            }
            return variants[Random.Range(0, variants.Length)];
        }

        /// Звук в точке мира: громкость и панорама зависят от расстояния до слушателя.
        public static AudioSource PlayAt(string sound, Vector3 pos, float volumeDb = 0f, float pitch = 1f)
        {
            var go = new GameObject("Sfx " + sound);
            if (GameRoot.World != null) go.transform.SetParent(GameRoot.World, false);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = Clip(sound);
            src.volume = Mathf.Clamp01(MASTER * G.Db(volumeDb));
            src.pitch = pitch * Random.Range(0.94f, 1.06f);
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 6f;
            src.maxDistance = 45f;
            src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, src.clip.length / src.pitch + 0.1f);
            return src;
        }

        /// Звук «в голове» игрока: свой выстрел и свою боль слышно одинаково громко на любой части экрана.
        public static void PlayFlat(string sound, float volumeDb = 0f, float pitch = 1f)
        {
            var go = new GameObject("Sfx " + sound);
            if (GameRoot.World != null) go.transform.SetParent(GameRoot.World, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = Clip(sound);
            src.volume = Mathf.Clamp01(MASTER * G.Db(volumeDb));
            src.pitch = pitch * Random.Range(0.95f, 1.05f);
            src.spatialBlend = 0f;
            src.Play();
            Object.Destroy(go, src.clip.length / src.pitch + 0.1f);
        }

        /// Голос зомби. Мычания одновременно звучит не больше MAX_GROANS, чтобы толпа не превращалась в кашу.
        public static void Groan(Component zombie, string sound = "groan", float volumeDb = -4f)
        {
            var pos = zombie.transform.position + Vector3.up * 1.5f;
            if (sound != "groan")
            {
                PlayAt(sound, pos, volumeDb, Random.Range(0.9f, 1.1f));
                return;
            }
            if (activeGroans >= MAX_GROANS) return;
            activeGroans++;
            var src = PlayAt(sound, pos, volumeDb, Random.Range(0.85f, 1.15f));
            src.gameObject.AddComponent<GroanToken>();
        }

        class GroanToken : MonoBehaviour
        {
            void OnDestroy() { activeGroans = Mathf.Max(0, activeGroans - 1); }
        }

        public static void ResetVoices() { activeGroans = 0; }

        // --- Синтез -----------------------------------------------------------

        static float[] Synth(string sound, int variant)
        {
            var rng = new Rng(StableHash(sound) + variant * 7919);
            switch (sound)
            {
                case "shot": return Shot(rng);
                case "splat": return Splat(rng, 0.25f);
                case "crunch": return Crunch(rng);
                case "groan": return Moan(rng);
                case "growl": return Growl(rng);
                case "pain": return Pain(rng);
                case "hurt": return Hurt(rng);
                case "click": return ClickSound(rng, 0f, 0.05f);
                case "reload": return Mix(ClickSound(rng, 0f, 0.5f), ClickSound(rng, 0.35f, 0.5f));
                case "slash": return Slash(rng);
                case "pickup": return Pickup();
            }
            return new float[1];
        }

        static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s) h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        static float[] Shot(Rng rng)
        {
            int n = (int)(RATE * 0.45f);
            var output = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float noise = rng.Range(-1f, 1f);
                // Яркий щелчок в начале, потом тёмный хвост эха.
                lp += (noise - lp) * Mathf.Lerp(0.95f, 0.06f, Mathf.Min(t / 0.12f, 1f));
                float crack = noise * Mathf.Exp(-t * 220f);
                float body = lp * Mathf.Exp(-t * 11f);
                float thump = Mathf.Sin(G.TAU * 55f * t) * Mathf.Exp(-t * 22f);
                output[i] = crack * 0.9f + body * 0.9f + thump * 0.9f;
            }
            return output;
        }

        static float[] Splat(Rng rng, float length)
        {
            int n = (int)(RATE * length);
            var output = new float[n];
            float lp = 0f;
            float freq = rng.Range(90f, 140f);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                lp += (rng.Range(-1f, 1f) - lp) * 0.18f;
                phase += freq * (1f - t * 2f) / RATE;
                float wet = lp * 2.2f * Mathf.Exp(-t * 16f);
                float thud = Mathf.Sin(G.TAU * phase) * Mathf.Exp(-t * 30f);
                output[i] = wet + thud * 0.7f;
            }
            return output;
        }

        static float[] Crunch(Rng rng)
        {
            int n = (int)(RATE * 0.35f);
            var output = Splat(rng, 0.35f);
            // Хруст: короткие резкие треск-зёрна поверх мокрого удара.
            int grains = rng.RangeI(6, 10);
            for (int g = 0; g < grains; g++)
            {
                int start = (int)(rng.Range(0f, 0.18f) * RATE);
                int length = (int)(rng.Range(0.004f, 0.012f) * RATE);
                float amp = rng.Range(0.4f, 0.9f);
                for (int j = 0; j < length; j++)
                    if (start + j < n)
                        output[start + j] += rng.Range(-1f, 1f) * amp * (1f - (float)j / length);
            }
            return output;
        }

        /// Голосовой синтез: импульсы «связок» проходят через резонансы гласной (форманты),
        /// как во рту. Ключи: {доля времени, F1, F2, F3, громкость}. fry — хрип (каждый
        /// второй импульс слабее), breath — придыхание, muffle — закрытый рот (мычание).
        static float[] Voice(Rng rng, float length, Vector3 pitch, float[][] keys, float breath, float fry, float jitter, float muffle)
        {
            int n = (int)(RATE * length);
            var output = new float[n];
            float phase = 0f;
            bool odd = false;
            float prevG = 0f, drift = 0f, lp = 0f;
            float[] bandwidth = { 90f, 130f, 200f };
            float[] weight = { 1f, 0.7f, 0.45f };
            var y1 = new float[3];
            var y2 = new float[3];
            var k = new float[4];
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                // Высота тона: от начала к пику на 30% и вниз к концу, плюс дрожание.
                float f0 = u < 0.3f ? Mathf.LerpUnclamped(pitch.x, pitch.y, u / 0.3f) : Mathf.LerpUnclamped(pitch.y, pitch.z, (u - 0.3f) / 0.7f);
                drift = Mathf.Clamp(drift + rng.Range(-1f, 1f) * 0.02f, -1f, 1f);
                f0 *= 1f + drift * jitter + 0.02f * Mathf.Sin(G.TAU * 4.5f * u * length);
                phase += f0 / RATE;
                if (phase >= 1f)
                {
                    phase -= 1f;
                    odd = !odd;
                }
                float g = 0f;
                if (phase < 0.4f) g = 0.5f * (1f - Mathf.Cos(Mathf.PI * phase / 0.4f));
                else if (phase < 0.56f) g = Mathf.Cos(Mathf.PI * 0.5f * (phase - 0.4f) / 0.16f);
                float src = (g - prevG) * 12f * (odd ? 1f - fry * 0.6f : 1f);
                prevG = g;
                src += rng.Range(-1f, 1f) * breath;
                Key(keys, u, k);
                float sum = 0f;
                for (int f = 0; f < 3; f++)
                {
                    float theta = G.TAU * k[f] / RATE;
                    float r = Mathf.Exp(-Mathf.PI * bandwidth[f] / RATE);
                    float v = (1f - r) * src + 2f * r * Mathf.Cos(theta) * y1[f] - r * r * y2[f];
                    y2[f] = y1[f];
                    y1[f] = v;
                    // Без поправки высокие форманты почти не слышны, и голос превращается в гул.
                    sum += v * weight[f] * 2f * Mathf.Sin(theta);
                }
                lp += (sum - lp) * (1f - muffle);
                output[i] = lp * k[3];
            }
            return output;
        }

        /// Значение ключей гласной в момент u (линейная интерполяция).
        static void Key(float[][] keys, float u, float[] result)
        {
            for (int j = 1; j < keys.Length; j++)
            {
                if (u <= keys[j][0])
                {
                    var a = keys[j - 1];
                    var b = keys[j];
                    float w = (u - a[0]) / Mathf.Max(b[0] - a[0], 0.0001f);
                    for (int i = 0; i < 4; i++) result[i] = Mathf.LerpUnclamped(a[i + 1], b[i + 1], w);
                    return;
                }
            }
            var last = keys[keys.Length - 1];
            for (int i = 0; i < 4; i++) result[i] = last[i + 1];
        }

        /// Мычание: закрытый рот «м-м» переходит в глухое «у-о» и обратно.
        static float[] Moan(Rng rng)
        {
            float s = rng.Range(0.85f, 1.15f);
            float length = rng.Range(1.3f, 2f);
            return Voice(rng, length, new Vector3(95, 112, 78) * s, new[]
            {
                new[] { 0f, 260f, 1000f, 2300f, 0f },
                new[] { 0.12f, 270f, 950f, 2300f, 0.6f },
                new[] { 0.35f, 330f, 800f, 2300f, 0.9f },
                new[] { 0.55f, 430f, 850f, 2400f, 1f },
                new[] { 0.85f, 350f, 800f, 2300f, 0.7f },
                new[] { 1f, 270f, 950f, 2300f, 0f },
            }, 0.2f, 0.55f, 0.03f, 0.4f);
        }

        /// Рык при атаке: короткое хриплое «а-гх».
        static float[] Growl(Rng rng)
        {
            float s = rng.Range(0.9f, 1.1f);
            return Voice(rng, 0.5f, new Vector3(90, 105, 70) * s, new[]
            {
                new[] { 0f, 600f, 1100f, 2400f, 0f },
                new[] { 0.08f, 650f, 1150f, 2400f, 1f },
                new[] { 0.7f, 550f, 1000f, 2400f, 0.8f },
                new[] { 1f, 450f, 950f, 2300f, 0f },
            }, 0.55f, 0.9f, 0.05f, 0.25f);
        }

        /// Сдавленный крик от попадания: резкое «э-а», которое обрывается в горле.
        static float[] Pain(Rng rng)
        {
            float s = rng.Range(0.85f, 1.2f);
            float length = rng.Range(0.4f, 0.55f);
            var output = Voice(rng, length, new Vector3(220, 290, 170) * s, new[]
            {
                new[] { 0f, 600f, 1700f, 2600f, 0f },
                new[] { 0.04f, 700f, 1600f, 2600f, 1f },
                new[] { 0.45f, 750f, 1450f, 2550f, 0.9f },
                new[] { 0.58f, 650f, 1300f, 2500f, 0.7f },
                new[] { 0.62f, 500f, 1200f, 2400f, 0f },
                new[] { 1f, 500f, 1200f, 2400f, 0f },
            }, 0.45f, 0.35f, 0.06f, 0f);
            // Напряжение в голосе и хриплый выдох «гх» после обрыва.
            float lp = 0f;
            for (int i = 0; i < output.Length; i++)
            {
                float u = (float)i / output.Length;
                output[i] *= 1f + 0.25f * Mathf.Sin(G.TAU * 28f * u * length);
                if (u > 0.64f && u < 0.82f)
                {
                    lp += (rng.Range(-1f, 1f) - lp) * 0.25f;
                    output[i] += lp * 0.06f * (1f - (u - 0.64f) / 0.18f);
                }
            }
            return output;
        }

        /// Взмах ножом: шипящий «вжух», нарастает и стихает.
        static float[] Slash(Rng rng)
        {
            int n = (int)(RATE * 0.2f);
            var output = new float[n];
            float lp = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                float noise = rng.Range(-1f, 1f);
                lp += (noise - lp) * Mathf.Lerp(0.15f, 0.6f, u);
                float bright = lp - prev;
                prev = lp;
                float env = Mathf.Sin(Mathf.PI * u);
                output[i] = (lp * 0.6f + bright * 1.5f) * env * env;
            }
            return output;
        }

        /// Подбор предмета: два коротких звонких сигнала.
        static float[] Pickup()
        {
            int n = (int)(RATE * 0.18f);
            var output = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float f = t < 0.07f ? 880f : 1320f;
                float local = t < 0.07f ? t : t - 0.07f;
                output[i] = Mathf.Sin(G.TAU * f * t) * Mathf.Exp(-local * 30f) * 0.6f;
            }
            return output;
        }

        static float[] Hurt(Rng rng)
        {
            int n = (int)(RATE * 0.3f);
            var output = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                lp += (rng.Range(-1f, 1f) - lp) * 0.1f;
                output[i] = (Mathf.Sin(G.TAU * 85f * t) * 0.9f + lp * 1.5f) * Mathf.Exp(-t * 18f);
            }
            return output;
        }

        static float[] ClickSound(Rng rng, float at, float total)
        {
            int n = (int)(RATE * Mathf.Max(total, at + 0.05f));
            var output = new float[n];
            int start = (int)(at * RATE);
            float prev = 0f;
            int len = (int)(RATE * 0.03f);
            for (int j = 0; j < len && start + j < n; j++)
            {
                float noise = rng.Range(-1f, 1f);
                float t = (float)j / RATE;
                output[start + j] = (noise - prev) * 0.5f * Mathf.Exp(-t * 200f) + Mathf.Sin(G.TAU * 1800f * t) * 0.3f * Mathf.Exp(-t * 300f);
                prev = noise;
            }
            return output;
        }

        static float[] Mix(float[] a, float[] b)
        {
            var output = new float[Mathf.Max(a.Length, b.Length)];
            for (int i = 0; i < a.Length; i++) output[i] = a[i];
            for (int i = 0; i < b.Length; i++) output[i] += b[i];
            return output;
        }

        /// Нормализация до 0.9, чтобы громкие звуки не хрипели от перегруза.
        static AudioClip ToClip(string name, float[] samples)
        {
            float peak = 0.001f;
            foreach (var v in samples) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = 0.9f / peak;
            var data = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++) data[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            var clip = AudioClip.Create(name, data.Length, 1, RATE, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
