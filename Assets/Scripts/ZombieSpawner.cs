using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Режим «Волны»: зомби лезут волнами, каждая больше предыдущей.
    /// Между волнами в зале появляются патроны, оружие и изредка аптечка.
    public class ZombieSpawner : MonoBehaviour
    {
        public event System.Action<int> WaveStarted;

        const int MAX_ALIVE = 40;
        const float SPAWN_INTERVAL = 0.7f;
        const float REST_BETWEEN_WAVES = 4f;

        public List<Vector3> spawnPoints = new List<Vector3>();
        /// Где раскладывать находки между волнами.
        public List<Vector3> pickupPoints = new List<Vector3>();
        public int players = 1;
        public int wave;
        int toSpawn;
        float spawnTimer;
        float restTimer = 2f;

        void Update()
        {
            int alive = Zombie.All.Count;
            if (toSpawn > 0)
            {
                spawnTimer -= Time.deltaTime;
                if (spawnTimer <= 0f && alive < MAX_ALIVE)
                {
                    SpawnOne();
                    toSpawn--;
                    spawnTimer = SPAWN_INTERVAL;
                }
            }
            else if (alive == 0)
            {
                restTimer -= Time.deltaTime;
                if (restTimer <= 0f) StartWave();
            }
        }

        void StartWave()
        {
            wave++;
            toSpawn = 4 + wave * 3;
            restTimer = REST_BETWEEN_WAVES;
            DropPickups();
            WaveStarted?.Invoke(wave);
        }

        void SpawnOne()
        {
            var point = G.PickList(spawnPoints);
            // Зомби сразу смотрят в зал, к игрокам.
            Zombie.Spawn(point + new Vector3(G.Rand(-1f, 1f), 0.1f, G.Rand(-1f, 1f)), 0f, null, true);
        }

        /// Перед каждой волной в комнате появляются патроны, иногда оружие и редко аптечка.
        /// Перед первой — по пистолету на игрока, ведь в начале у всех только нож.
        void DropPickups()
        {
            foreach (var p in Pickup.All.ToArray()) Destroy(p.gameObject);
            var items = new List<string[]>();
            if (wave == 1)
                for (int i = 0; i < players; i++) items.Add(new[] { "weapon", "pistol" });
            for (int i = 0; i < players; i++) items.Add(new[] { "ammo", G.Pick("pistol", "smg", "shotgun") });
            if (wave % 2 == 0) items.Add(new[] { "weapon", G.Pick("smg", "shotgun") });
            if (wave % 3 == 0) items.Add(new[] { "medkit", "" });
            var spots = new List<Vector3>(pickupPoints);
            G.Shuffle(spots);
            for (int i = 0; i < items.Count; i++)
                Pickup.Spawn(items[i][0], items[i][1], spots[i % spots.Count] + new Vector3(G.Rand(-0.5f, 0.5f), 0, G.Rand(-0.5f, 0.5f)));
        }
    }
}
