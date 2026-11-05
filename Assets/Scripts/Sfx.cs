using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Звуки игры. Почти все синтезированы кодом: попадание, хруст отрыва, мычание и рык зомби, удар по игроку.
    /// Вскрики зомби от попаданий (pain, yelp, shriek, death) — записи с freesound (Resources/ZombieCoop/Sounds, CC0).
    /// Оружие звучит настоящими записями (там же, CC0, см. LICENSE-weapons.md): у пистолета, автомата
    /// и дробовика свои выстрел, перезарядка и сухой щелчок пустого магазина.
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
        public static readonly string[] Guns = { "pistol", "smg", "shotgun" };
        const string RECORDED_DIR = "ZombieCoop/Sounds/";
        /// Звуки попаданий по телу: у каждого шесть вариантов, а при проигрывании ещё и
        /// случайная высота, поэтому две пули подряд почти никогда не звучат одинаково.
        ///   hit_flesh — пуля в мясо: хлёсткий шлепок и глухой удар;
        ///   hit_bone — по кости: сухой стук и треск;
        ///   hit_skull — по черепу: тяжёлый костяной стук и хлюп;
        ///   squelch — мокрое глухое чавканье (большая рана, выход пули, труп);
        ///   spatter — кровь брызгами шлёпает по полу и стенам;
        ///   tear — мясо рвётся (отрыв предплечья, руки, ноги).
        public static readonly string[] HitSounds = { "hit_flesh", "hit_bone", "hit_skull", "squelch", "spatter", "tear" };
        /// Вскрики зомби — звучат только от попаданий (мычание при ходьбе и рык при атаке прежние):
        ///   yelp — короткий резкий вскрик «ах!» от пули;
        ///   shriek — пронзительный хриплый визг (оторвало руку, раскололо череп);
        ///   gurgle — бульканье кровью в горле;
        ///   howl — протяжный вой, когда подогнулись колени;
        ///   death — предсмертный хрип, тон падает и переходит в клёкот;
        ///   snarl — рёв боли сквозь зубы.
        public static readonly string[] VoiceSounds = { "yelp", "shriek", "gurgle", "howl", "death", "snarl" };

        /// Генерирует все звуки заранее, чтобы первый выстрел не подтормаживал.
        public static void PreloadAll()
        {
            foreach (var s in All) Clip(s);
            foreach (var s in HitSounds) Clip(s);
            foreach (var s in VoiceSounds) Clip(s);
            foreach (var gun in Guns)
                foreach (var s in new[] { "shot_", "reload_", "click_" })
                    Clip(s + gun);
        }

        public static AudioClip Clip(string sound)
        {
            if (!cache.TryGetValue(sound, out var variants))
            {
                variants = Recorded(sound);
                if (variants.Length == 0)
                {
                    int count = System.Array.IndexOf(HitSounds, sound) >= 0 || System.Array.IndexOf(VoiceSounds, sound) >= 0 ? 6 : 4;
                    variants = new AudioClip[count];
                    for (int v = 0; v < count; v++)
                        variants[v] = ToClip(sound + v, Synth(sound, v));
                }
                cache[sound] = variants;
            }
            return variants[Random.Range(0, variants.Length)];
        }

        /// Записанный звук: sound.wav или варианты sound_1.wav … sound_8.wav.
        static AudioClip[] Recorded(string sound)
        {
            var found = new List<AudioClip>();
            var single = Resources.Load<AudioClip>(RECORDED_DIR + sound);
            if (single != null) found.Add(single);
            for (int v = 1; v <= 8; v++)
            {
                var clip = Resources.Load<AudioClip>(RECORDED_DIR + sound + "_" + v);
                if (clip != null) found.Add(clip);
            }
            return found.ToArray();
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
        /// voice — высота голоса этого зомби: у каждого свой, от низкого хрипа до сиплого крика.
        public static void Groan(Component zombie, string sound = "groan", float volumeDb = -4f, float voice = 1f)
        {
            var pos = zombie.transform.position + Vector3.up * 1.5f;
            if (sound == "growl")
            {
                PlayAt(sound, pos, volumeDb, Random.Range(0.9f, 1.1f));
                return;
            }
            if (sound != "groan")
            {
                PlayAt(sound, pos, volumeDb, voice * Random.Range(0.9f, 1.02f));
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
            // Если записей оружия нет, звучит прежний синтезированный выстрел или щелчок.
            string head = sound.Split('_')[0];
            if (head == "shot" || head == "reload" || head == "click") sound = head;
            var output = SynthRaw(sound, rng);
            // Попадания и шипение глушим: звонкие верха делают их игрушечными.
            if (System.Array.IndexOf(HitSounds, sound) >= 0 || sound == "snarl" || sound == "gurgle" || sound == "splat" || sound == "crunch")
                output = Dark(output, sound == "hit_bone" || sound == "hit_skull" || sound == "crunch" ? 0.22f : 0.3f);
            return output;
        }

        static float[] SynthRaw(string sound, Rng rng)
        {
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
                case "hit_flesh": return HitFlesh(rng);
                case "hit_bone": return HitBone(rng, rng.Range(150f, 240f), 0.2f);
                case "hit_skull": return Mix(HitBone(rng, rng.Range(95f, 150f), 0.3f), Scaled(Squelch(rng, 0.3f), 0.5f));
                case "squelch": return Squelch(rng, rng.Range(0.3f, 0.45f));
                case "spatter": return Spatter(rng);
                case "tear": return Tear(rng);
                case "yelp": return Yelp(rng);
                case "shriek": return Shriek(rng);
                case "gurgle": return Gurgle(rng);
                case "howl": return Howl(rng);
                case "death": return DeathRattle(rng);
                case "snarl": return Snarl(rng);
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
        static float[] VoicePlain(Rng rng, float length, Vector3 pitch, float[][] keys, float breath, float fry, float jitter, float muffle)
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

        /// Голос для вскриков: как VoicePlain, но глотка ниже, хрип грубее, и сверху мрачная
        /// обработка (Grim). Мычание и рык звучат прежним VoicePlain.
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
                float src = (g - prevG) * 12f * (odd ? 1f - Mathf.Max(fry, 0.6f) * 0.75f : 1f);
                prevG = g;
                src += rng.Range(-1f, 1f) * breath;
                Key(keys, u, k);
                float sum = 0f;
                for (int f = 0; f < 3; f++)
                {
                    // Глотка большая и разорванная: все резонансы ниже, чем у живого человека.
                    float theta = G.TAU * k[f] * 0.78f / RATE;
                    float r = Mathf.Exp(-Mathf.PI * bandwidth[f] / RATE);
                    float v = (1f - r) * src + 2f * r * Mathf.Cos(theta) * y1[f] - r * r * y2[f];
                    y2[f] = y1[f];
                    y1[f] = v;
                    sum += v * weight[f] * 2f * Mathf.Sin(theta);
                }
                lp += (sum - lp) * (1f - Mathf.Max(muffle, 0.2f));
                output[i] = lp * k[3];
            }
            return Grim(rng, output);
        }

        /// Делает голос мрачным: грязный перегруз связок, срез звонких верхов и глухой
        /// отзвук, как в бетонном коридоре. Без этого синтез звучит чисто и «мультяшно».
        static float[] Grim(Rng rng, float[] dry)
        {
            int tail = (int)(0.35f * RATE);
            var output = new float[dry.Length + tail];
            float peak = 0.0001f;
            foreach (var v in dry) peak = Mathf.Max(peak, Mathf.Abs(v));
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < dry.Length; i++)
            {
                float x = Tanh(dry[i] / peak * 2.6f);
                // Шершавость: громкость каждого мгновения слегка дёргается от шума.
                x *= 0.8f + 0.2f * rng.Randf();
                lp += (x - lp) * 0.32f;
                lp2 += (lp - lp2) * 0.5f;
                output[i] = lp2 * peak * 0.85f;
            }
            // Отзвук: несколько затухающих задержек, ниже и глуше оригинала.
            float[] delays = { 0.031f, 0.047f, 0.071f, 0.113f };
            float[] gains = { 0.32f, 0.24f, 0.17f, 0.11f };
            var wet = (float[])output.Clone();
            for (int d = 0; d < delays.Length; d++)
            {
                int off = (int)(delays[d] * RATE);
                float echoLp = 0f;
                for (int i = off; i < output.Length; i++)
                {
                    echoLp += (wet[i - off] - echoLp) * 0.2f;
                    output[i] += echoLp * gains[d];
                }
            }
            return output;
        }

        /// Рвёт голос: громкость прыгает от сглаженного шума (как кровь и хрипы в горле),
        /// без ровной синусоидальной дрожи. depth — насколько глубоко, smooth — плавность.
        static void Roughen(Rng rng, float[] output, int from, float depth, float smooth)
        {
            float g = 1f, target = 1f;
            for (int i = from; i < output.Length; i++)
            {
                if ((i - from) % 400 == 0) target = rng.Randf();
                g += (target - g) * smooth;
                output[i] *= 1f - depth + depth * Mathf.Clamp(g * 1.6f - 0.2f, 0f, 1f);
            }
        }

        static float Tanh(float x) => (float)System.Math.Tanh(x);

        /// Короткий вскрик «ах!»: резкая атака, тон подскакивает и сразу обрывается.
        static float[] Yelp(Rng rng)
        {
            float s = rng.Range(0.8f, 1.25f);
            var vowels = new[] { new[] { 750f, 1250f }, new[] { 600f, 1700f }, new[] { 500f, 1000f } };
            var vowel = vowels[rng.Next(3)];
            float length = rng.Range(0.18f, 0.3f);
            return Voice(rng, length, new Vector3(105, 135, 80) * s, new[]
            {
                new[] { 0f, vowel[0], vowel[1], 2600f, 0f },
                new[] { 0.06f, vowel[0], vowel[1], 2600f, 1f },
                new[] { 0.6f, vowel[0] * 0.9f, vowel[1] * 0.95f, 2500f, 0.8f },
                new[] { 1f, vowel[0] * 0.7f, vowel[1] * 0.9f, 2400f, 0f },
            }, 0.5f, rng.Range(0.2f, 0.6f), 0.08f, 0f);
        }

        /// Визг: высокий, срывающийся, с хрипом; тон ползёт вверх и ломается.
        static float[] Shriek(Rng rng)
        {
            float s = rng.Range(0.85f, 1.2f);
            float length = rng.Range(0.7f, 1.1f);
            var output = Voice(rng, length, new Vector3(120, 175, 85) * s, new[]
            {
                new[] { 0f, 650f, 1900f, 2900f, 0f },
                new[] { 0.05f, 800f, 1800f, 2900f, 1f },
                new[] { 0.4f, 900f, 1500f, 2800f, 1f },
                new[] { 0.75f, 700f, 1300f, 2700f, 0.8f },
                new[] { 1f, 550f, 1200f, 2600f, 0f },
            }, 0.75f, 0.75f, 0.14f, 0f);
            // Голос рвётся неровно: громкость скачет от случайного шума, а не по синусу.
            Roughen(rng, output, 0, 0.45f, 0.004f);
            return output;
        }

        /// Бульканье: низкий клокочущий голос, кровь пузырится в горле.
        static float[] Gurgle(Rng rng)
        {
            float s = rng.Range(0.8f, 1.15f);
            float length = rng.Range(0.55f, 0.9f);
            var output = Voice(rng, length, new Vector3(62, 72, 48) * s, new[]
            {
                new[] { 0f, 400f, 900f, 2300f, 0f },
                new[] { 0.1f, 450f, 850f, 2300f, 0.9f },
                new[] { 0.7f, 380f, 800f, 2200f, 0.8f },
                new[] { 1f, 300f, 750f, 2200f, 0f },
            }, 0.3f, 0.9f, 0.12f, 0.3f);
            // Голос захлёбывается кровью: громкость рвётся неровно, под ним мокрое хлюпанье.
            Roughen(rng, output, 0, 0.75f, 0.0025f);
            return Mix(output, Scaled(Squelch(rng, length), 0.3f));
        }

        /// Протяжный вой «у-а-а-у»: тон медленно поднимается и тоскливо падает.
        static float[] Howl(Rng rng)
        {
            float s = rng.Range(0.85f, 1.15f);
            float length = rng.Range(1.2f, 1.7f);
            return Voice(rng, length, new Vector3(70, 98, 50) * s, new[]
            {
                new[] { 0f, 320f, 800f, 2300f, 0f },
                new[] { 0.15f, 350f, 750f, 2300f, 0.8f },
                new[] { 0.45f, 700f, 1150f, 2500f, 1f },
                new[] { 0.8f, 450f, 850f, 2300f, 0.7f },
                new[] { 1f, 330f, 780f, 2200f, 0f },
            }, 0.35f, 0.6f, 0.06f, 0.15f);
        }

        /// Предсмертный хрип: стон опускается, становится скрипучим и рассыпается в клёкот.
        static float[] DeathRattle(Rng rng)
        {
            float s = rng.Range(0.85f, 1.15f);
            float length = rng.Range(1f, 1.5f);
            var output = Voice(rng, length, new Vector3(80, 85, 32) * s, new[]
            {
                new[] { 0f, 550f, 1100f, 2400f, 0f },
                new[] { 0.08f, 600f, 1100f, 2400f, 1f },
                new[] { 0.5f, 450f, 900f, 2300f, 0.7f },
                new[] { 1f, 300f, 800f, 2200f, 0f },
            }, 0.45f, 0.95f, 0.1f, 0.2f);
            // Ближе к концу голос распадается на неровные хрипы.
            int from = (int)(output.Length * 0.45f);
            Roughen(rng, output, from, 0.9f, 0.002f);
            for (int i = from; i < output.Length; i++) output[i] *= 1.3f;
            return output;
        }

        /// Злобное шипение-рык: резкий выдох сквозь зубы и короткий хриплый рык.
        static float[] Snarl(Rng rng)
        {
            float s = rng.Range(0.9f, 1.15f);
            float length = rng.Range(0.5f, 0.75f);
            var output = Voice(rng, length, new Vector3(64, 78, 52) * s, new[]
            {
                new[] { 0f, 500f, 1700f, 2700f, 0f },
                new[] { 0.25f, 500f, 1700f, 2700f, 0.1f },
                new[] { 0.35f, 650f, 1400f, 2600f, 1f },
                new[] { 0.8f, 600f, 1300f, 2500f, 0.8f },
                new[] { 1f, 500f, 1200f, 2400f, 0f },
            }, 0.7f, 1f, 0.08f, 0f);
            // Шипение в начале.
            var bp = new Band { q = 0.6f };
            int hiss = (int)(length * 0.4f * RATE);
            for (int i = 0; i < hiss; i++)
            {
                float u = (float)i / hiss;
                output[i] += bp.Step(rng.Range(-1f, 1f), 1600f) * Mathf.Sin(Mathf.PI * u) * 0.5f;
            }
            return output;
        }

        /// Простой фильтр (state variable): возвращает полосу вокруг частоты fc.
        class Band
        {
            public float low, band, q = 0.7f;

            public float Step(float x, float fc)
            {
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(fc, 8000f) / 22050f);
                low += f * band;
                float high = x - low - q * band;
                band += f * high;
                return band;
            }
        }

        /// Пуля в мясо: короткий хлёсткий шлепок (полоса шума повыше), под ним глухой
        /// удар, падающий по высоте, и мокрый хвост, который «булькает» с неровной частотой.
        static float[] HitFlesh(Rng rng)
        {
            int n = (int)(RATE * rng.Range(0.18f, 0.28f));
            var output = new float[n];
            var slap = new Band { q = rng.Range(0.4f, 0.9f) };
            float slapFc = rng.Range(650f, 1400f);
            float lp = 0f;
            float thudF = rng.Range(45f, 75f);
            float phase = 0f;
            float wob = rng.Range(18f, 45f);
            float wetAmp = rng.Range(0.7f, 1.4f);
            float slapDecay = rng.Range(80f, 120f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float noise = rng.Range(-1f, 1f);
                float hit = slap.Step(noise, slapFc) * Mathf.Exp(-t * slapDecay) * 2.2f;
                phase += thudF * (1f - t * 2.5f) / RATE;
                float thud = Mathf.Sin(G.TAU * phase) * Mathf.Exp(-t * 26f);
                lp += (noise - lp) * 0.14f;
                float wet = lp * Mathf.Exp(-t * 15f) * (0.55f + 0.45f * Mathf.Abs(Mathf.Sin(G.TAU * wob * t * 0.5f + rng.Randf() * 0.4f))) * wetAmp * 2f;
                output[i] = hit + thud * 0.85f + wet;
            }
            return output;
        }

        /// Удар по кости: несколько негармоничных обертонов (дерево, а не металл), быстро
        /// гаснут; в начале — сухие треск-зёрна, под ними немного мяса.
        static float[] HitBone(Rng rng, float f0, float length)
        {
            int n = (int)(RATE * length);
            var flesh = Scaled(HitFlesh(rng), 0.45f);
            var output = new float[n];
            System.Array.Copy(flesh, output, Mathf.Min(n, flesh.Length));
            float[] ratios = { 1f, rng.Range(2.1f, 2.5f), rng.Range(3.4f, 4.1f), rng.Range(5.2f, 6.3f) };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float knock = 0f;
                for (int k = 0; k < ratios.Length; k++)
                    knock += Mathf.Sin(G.TAU * f0 * ratios[k] * t + rng.Range(-0.6f, 0.6f)) * Mathf.Exp(-t * (55f + k * 30f)) / (1f + k);
                output[i] += Tanh(knock * 2f) * 0.8f;
            }
            int grains = rng.RangeI(3, 6);
            for (int g = 0; g < grains; g++)
            {
                int start = (int)(rng.Range(0f, 0.035f) * RATE);
                int lenG = (int)(rng.Range(0.002f, 0.008f) * RATE);
                float amp = rng.Range(0.5f, 1f);
                for (int j = 0; j < lenG; j++)
                    if (start + j < n) output[start + j] += rng.Range(-1f, 1f) * amp * (1f - (float)j / lenG);
            }
            return output;
        }

        /// Мокрое чавканье: шум сквозь полосу, которая гуляет по высоте, и пузыри —
        /// короткие свисты вверх, как лопающаяся пена.
        static float[] Squelch(Rng rng, float length)
        {
            int n = (int)(RATE * length);
            var output = new float[n];
            var bp = new Band { q = 0.5f };
            float fLo = rng.Range(140f, 260f);
            float fHi = rng.Range(380f, 700f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / RATE;
                float u = t / length;
                float fc = Mathf.Lerp(fLo, fHi, Mathf.Sin(Mathf.PI * u) * (0.6f + 0.4f * Mathf.Sin(t * 31f)));
                float env = Mathf.Min(t / 0.01f, 1f) * Mathf.Exp(-u * 3.5f);
                output[i] = bp.Step(rng.Range(-1f, 1f), fc) * env * 2.5f;
            }
            int bubbles = rng.RangeI(4, 9);
            for (int b = 0; b < bubbles; b++)
            {
                int start = (int)(rng.Range(0f, length * 0.8f) * RATE);
                float dur = rng.Range(0.01f, 0.03f);
                float fc = rng.Range(160f, 420f);
                float amp = rng.Range(0.4f, 0.9f);
                var wet = new Band { q = 0.35f };
                int len = (int)(dur * RATE);
                for (int j = 0; j < len; j++)
                {
                    if (start + j >= n) break;
                    float v = (float)j / len;
                    output[start + j] += wet.Step(rng.Range(-1f, 1f), fc) * Mathf.Sin(Mathf.PI * v) * amp * 2f;
                }
            }
            return output;
        }

        /// Брызги: десятки мелких капель шлёпают по полу и стене, чаще сразу, потом реже.
        static float[] Spatter(Rng rng)
        {
            int n = (int)(RATE * 0.45f);
            var output = new float[n];
            int drops = rng.RangeI(12, 26);
            for (int d = 0; d < drops; d++)
            {
                float r = rng.Randf();
                int start = (int)((0.02f + r * r * 0.38f) * RATE);
                int lenD = (int)(rng.Range(0.003f, 0.012f) * RATE);
                float amp = rng.Range(0.2f, 1f);
                var bp = new Band { q = 0.8f };
                float fc = rng.Range(500f, 1800f);
                for (int j = 0; j < lenD; j++)
                {
                    if (start + j >= n) break;
                    float v = (float)j / lenD;
                    output[start + j] += bp.Step(rng.Range(-1f, 1f), fc) * amp * (1f - v) * 3f;
                }
            }
            return output;
        }

        /// Мясо рвётся: густой треск волокон нарастает и обрывается, под ним мокрый гул.
        static float[] Tear(Rng rng)
        {
            float length = rng.Range(0.4f, 0.6f);
            int n = (int)(RATE * length);
            var sq = Scaled(Squelch(rng, length), 0.7f);
            var output = new float[n];
            System.Array.Copy(sq, output, Mathf.Min(n, sq.Length));
            int grains = rng.RangeI(45, 80);
            for (int g = 0; g < grains; g++)
            {
                float u = Mathf.Sqrt(rng.Randf()) * 0.85f;
                int start = (int)(u * length * RATE);
                int lenG = (int)(rng.Range(0.002f, 0.01f) * RATE);
                float amp = rng.Range(0.2f, 0.9f) * (0.4f + u);
                var bp = new Band { q = 0.6f };
                float fc = rng.Range(350f, 1300f);
                for (int j = 0; j < lenG; j++)
                {
                    if (start + j >= n) break;
                    output[start + j] += bp.Step(rng.Range(-1f, 1f), fc) * amp * 2.5f;
                }
            }
            // Последний рывок: глухой хлопок, когда кусок отделяется.
            int pop = (int)(length * 0.82f * RATE);
            int popLen = (int)(0.08f * RATE);
            for (int j = 0; j < popLen; j++)
            {
                if (pop + j >= n) break;
                float t = (float)j / RATE;
                output[pop + j] += Mathf.Sin(G.TAU * 60f * t) * Mathf.Exp(-t * 30f) * 0.9f;
            }
            return output;
        }

        /// Двойной фильтр низких частот: оставляет мясо и удар, убирает «стеклянный» звон.
        static float[] Dark(float[] a, float k)
        {
            var output = (float[])a.Clone();
            float l1 = 0f, l2 = 0f;
            for (int i = 0; i < output.Length; i++)
            {
                l1 += (output[i] - l1) * k;
                l2 += (l1 - l2) * k;
                output[i] = l2;
            }
            return output;
        }

        static float[] Scaled(float[] a, float k)
        {
            var output = (float[])a.Clone();
            for (int i = 0; i < output.Length; i++) output[i] *= k;
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
            return VoicePlain(rng, length, new Vector3(95, 112, 78) * s, new[]
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
            return VoicePlain(rng, 0.5f, new Vector3(90, 105, 70) * s, new[]
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
            var output = Voice(rng, length, new Vector3(100, 128, 75) * s, new[]
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
