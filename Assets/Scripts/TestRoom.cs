using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Зал для режима «Волны»: тёмный зал 24×24 м с колоннами, ящиками и редкими лампами.
    /// Сюда же падают находки между волнами.
    public static class TestRoom
    {
        const float SIZE = 24f;
        const float HEIGHT = 3.5f;

        public class Layout
        {
            public List<Vector3> playerStarts;
            public List<Vector3> zombieSpawns;
            public List<Vector3> pickupPoints;
        }

        public static Layout Build(Transform world)
        {
            WorldEnvironment.Setup(0.06f, 0.04f);
            float half = SIZE / 2f;
            WorldEnvironment.Box(world, new Vector3(SIZE, 1f, SIZE), new Vector3(0, -0.5f, 0), 0.3f);
            WorldEnvironment.Box(world, new Vector3(SIZE, 0.5f, SIZE), new Vector3(0, HEIGHT + 0.25f, 0), 0.15f);
            WorldEnvironment.Box(world, new Vector3(SIZE, HEIGHT, 0.5f), new Vector3(0, HEIGHT / 2f, -half), 0.55f);
            WorldEnvironment.Box(world, new Vector3(SIZE, HEIGHT, 0.5f), new Vector3(0, HEIGHT / 2f, half), 0.55f);
            WorldEnvironment.Box(world, new Vector3(0.5f, HEIGHT, SIZE), new Vector3(-half, HEIGHT / 2f, 0), 0.55f);
            WorldEnvironment.Box(world, new Vector3(0.5f, HEIGHT, SIZE), new Vector3(half, HEIGHT / 2f, 0), 0.55f);

            foreach (var p in new[] { new Vector2(-6, -6), new Vector2(6, -6), new Vector2(-6, 6), new Vector2(6, 6) })
                WorldEnvironment.Box(world, new Vector3(1f, HEIGHT, 1f), new Vector3(p.x, HEIGHT / 2f, p.y), 0.7f);
            foreach (var p in new[] { new Vector2(-2, 3), new Vector2(3, -1), new Vector2(-8, 1), new Vector2(8, -3), new Vector2(0, -7) })
                WorldEnvironment.Box(world, new Vector3(1.2f, 1.2f, 1.2f), new Vector3(p.x, 0.6f, p.y), 0.8f, G.Rand(0f, Mathf.PI));

            foreach (var p in new[] { new Vector3(0, HEIGHT - 0.3f, 0), new Vector3(-8, HEIGHT - 0.3f, 8), new Vector3(8, HEIGHT - 0.3f, -8) })
                WorldEnvironment.Lamp(world, p, p + Vector3.up * 0.25f, new Vector3(0.6f, 0.08f, 0.6f), 9f, 1.2f, true);

            // Точки те же, что в Godot-версии, с переводом осей: игроки стоят у одной стены
            // и смотрят в зал, зомби лезут с противоположной стороны.
            return new Layout
            {
                playerStarts = Convert(new Vector3(-1, 0.1f, 8), new Vector3(1, 0.1f, 8), new Vector3(-3, 0.1f, 8), new Vector3(3, 0.1f, 8)),
                zombieSpawns = Convert(new Vector3(-10, 0.1f, -10), new Vector3(10, 0.1f, -10), new Vector3(-10, 0.1f, -2), new Vector3(10, 0.1f, -2)),
                pickupPoints = Convert(new Vector3(-4, 0, 6), new Vector3(4, 0, 6), new Vector3(0, 0, 3), new Vector3(-4, 0, 0), new Vector3(4, 0, 2), new Vector3(0, 0, -3)),
            };
        }

        static List<Vector3> Convert(params Vector3[] points)
        {
            var list = new List<Vector3>();
            foreach (var p in points) list.Add(G.P(p));
            return list;
        }
    }
}
