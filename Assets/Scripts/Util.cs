using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Перевод из осей Godot-версии в оси Unity и мелкие помощники.
    /// В Godot вперёд — это -Z, в Unity — +Z. Все позы фигур, позиции деталей и
    /// углы шарниров перенесены из Godot как есть и переводятся здесь:
    /// позиция (x, y, z) → (x, y, -z), углы Эйлера в радианах (x, y, z) → (-x, -y, z) в градусах.
    /// Порядок поворотов (сначала Z, потом X, потом Y) у обоих движков одинаковый.
    public static class G
    {
        public const float TAU = Mathf.PI * 2f;

        public static Vector3 P(Vector3 v) => new Vector3(v.x, v.y, -v.z);
        public static Vector3 P(float x, float y, float z) => new Vector3(x, y, -z);

        public static Quaternion R(Vector3 e) =>
            Quaternion.Euler(-e.x * Mathf.Rad2Deg, -e.y * Mathf.Rad2Deg, e.z * Mathf.Rad2Deg);
        public static Quaternion R(float x, float y, float z) => R(new Vector3(x, y, z));

        public static float Rand(float a, float b) => Random.Range(a, b);
        /// Целое от a до b включительно.
        public static int RandI(int a, int b) => Random.Range(a, b + 1);
        public static float Randf() => Random.value;
        public static T Pick<T>(params T[] items) => items[Random.Range(0, items.Length)];
        public static T PickList<T>(IList<T> items) => items[Random.Range(0, items.Count)];

        public static void Shuffle<T>(IList<T> list, System.Random rng = null)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng != null ? rng.Next(i + 1) : Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public static float Smoothstep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// Громкость в децибелах → множитель.
        public static float Db(float db) => Mathf.Pow(10f, db / 20f);

        public static int PosMod(int a, int b) => ((a % b) + b) % b;

        /// Пружинистое затухание, как TRANS_BOUNCE + EASE_OUT в Godot.
        public static float BounceOut(float t)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }

    /// Генератор случайных чисел с теми же вызовами, что RandomNumberGenerator в Godot.
    public class Rng
    {
        readonly System.Random r;
        public Rng(int seed) { r = new System.Random(seed); }
        public float Randf() => (float)r.NextDouble();
        public float Range(float a, float b) => a + (b - a) * (float)r.NextDouble();
        /// Целое от a до b включительно.
        public int RangeI(int a, int b) => r.Next(a, b + 1);
        public int Next(int n) => r.Next(n);
        public System.Random Raw => r;
    }

    /// Слои Unity: физика и видимость. Номера совпадают с ProjectSettings/TagManager.asset.
    public static class Layers
    {
        public const int World = 0;
        public const int ZombieBody = 8;
        public const int Hitbox = 9;
        public const int Player = 10;
        public const int Debris = 11;
        /// Лежащий труп: держится на полу, живые и игроки проходят сквозь него.
        public const int Corpse = 20;
        /// Фигура игрока i (её не видит своя камера).
        public const int PlayerBody = 12;
        /// Оружие от первого лица игрока i (его видит только своя камера).
        public const int PlayerView = 16;

        public const int WorldMask = 1 << World;

        public static void SetupCollisions()
        {
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(Hitbox, i, true);
                for (int k = 0; k < 4; k++)
                {
                    Physics.IgnoreLayerCollision(PlayerBody + k, i, true);
                    Physics.IgnoreLayerCollision(PlayerView + k, i, true);
                }
            }
            Physics.IgnoreLayerCollision(Debris, Player, true);
            Physics.IgnoreLayerCollision(Debris, ZombieBody, true);
            Physics.IgnoreLayerCollision(Debris, World, false);
            Physics.IgnoreLayerCollision(Debris, Debris, false);
            for (int i = 0; i < 32; i++)
                if (i != World) Physics.IgnoreLayerCollision(Corpse, i, true);
        }
    }
}
