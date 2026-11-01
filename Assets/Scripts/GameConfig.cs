using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    public enum GameMode { Escape, Waves }

    /// Снаряжение игрока на начало этажа.
    public class Loadout
    {
        public List<string> owned;
        public string current;
        public Dictionary<string, int> mag;
        public Dictionary<string, int> reserve;
        public int kills;
    }

    /// Настройки партии, которые живут между перезапусками: режим, число игроков, номер этажа
    /// и снаряжение игроков на начало этажа (чтобы после гибели всё вернулось как было).
    public static class GameConfig
    {
        public static GameMode Mode = GameMode.Escape;
        public static int Players = 2;
        public static int FloorNumber = 1;
        /// Снаряжение на начало этажа, по одному на игрока; пустой список — новая партия.
        public static List<Loadout> Loadouts = new List<Loadout>();

        public static void NewGame(GameMode mode, int players)
        {
            Mode = mode;
            Players = Mathf.Clamp(players, 1, 4);
            FloorNumber = 1;
            Loadouts = new List<Loadout>();
        }
    }
}
