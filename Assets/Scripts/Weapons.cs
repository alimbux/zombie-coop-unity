using System.Collections.Generic;

namespace ZombieCoop
{
    public class WeaponStats
    {
        public string name;
        public bool melee;
        public float damage, interval, range;
        public int mag;
        public bool auto;
        public int pellets;
        public float spread, reload;
        public int pickupAmmo, maxReserve;
        public float noise;
        /// На сколько м/с одно попадание (одна дробина) толкает живого зомби.
        public float knockback;
        /// Толчок трупу или оторванной части.
        public float impulse;
    }

    /// Характеристики оружия. Нож есть всегда и не тратит патроны,
    /// остальное нужно найти на уровне вместе с патронами.
    /// У дробовика десять дробин с широким разлётом, поэтому в упор он толкает сильнее всего
    /// и сбивает с ног.
    public static class Weapons
    {
        public static readonly string[] Order = { "knife", "pistol", "smg", "shotgun" };

        public static readonly Dictionary<string, WeaponStats> Stats = new Dictionary<string, WeaponStats>
        {
            ["knife"] = new WeaponStats
            {
                name = "НОЖ", melee = true, damage = 45f, interval = 0.45f, range = 1.9f,
                mag = 0, auto = false, pellets = 1, spread = 0f, reload = 0f,
                pickupAmmo = 0, maxReserve = 0, noise = 0f, knockback = 3f, impulse = 7f,
            },
            ["pistol"] = new WeaponStats
            {
                name = "ПИСТОЛЕТ", melee = false, damage = 34f, interval = 0.22f, range = 60f,
                mag = 12, auto = false, pellets = 1, spread = 0.004f, reload = 1.1f,
                pickupAmmo = 24, maxReserve = 96, noise = 18f, knockback = 4f, impulse = 9f,
            },
            ["smg"] = new WeaponStats
            {
                name = "АВТОМАТ", melee = false, damage = 22f, interval = 0.09f, range = 60f,
                mag = 30, auto = true, pellets = 1, spread = 0.025f, reload = 1.6f,
                pickupAmmo = 60, maxReserve = 240, noise = 22f, knockback = 1f, impulse = 5f,
            },
            ["shotgun"] = new WeaponStats
            {
                name = "ДРОБОВИК", melee = false, damage = 16f, interval = 0.8f, range = 30f,
                mag = 6, auto = false, pellets = 10, spread = 0.15f, reload = 1.8f,
                pickupAmmo = 12, maxReserve = 48, noise = 26f, knockback = 2.6f, impulse = 7f,
            },
        };

        public static WeaponStats Get(string weapon) => Stats[weapon];
        public static string DisplayName(string weapon) => Stats[weapon].name;
        public static int IndexOf(string weapon) => System.Array.IndexOf(Order, weapon);
    }
}
