using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Процедурный этаж: комнаты на сетке, двери между соседними, выход в самой дальней
    /// от старта комнате. Чем выше этаж, тем больше комнат и зомби.
    /// Зомби стоят по комнатам и не знают об игроках, пока не заметят или не услышат.
    public class LevelGenerator
    {
        public const float CELL = 9f;
        const float HEIGHT = 3.2f;
        const float WALL = 0.3f;
        const float DOOR = 2.2f;
        const float DOOR_HEIGHT = 2.4f;
        /// Насколько сдвинута дверь от середины стены, если она у левого или правого края.
        const float DOOR_SIDE = 2.6f;
        const int MAX_ZOMBIES = 150;
        static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        Rng rng = new Rng(0);
        public List<Vector2Int> cells = new List<Vector2Int>();
        public Dictionary<Vector2Int, List<Vector2Int>> links = new Dictionary<Vector2Int, List<Vector2Int>>();
        /// Сдвиг двери вдоль стены для каждой пары соседних комнат: 0 — по центру,
        /// ±DOOR_SIDE — у одного из краёв.
        public Dictionary<(Vector2Int, Vector2Int), float> doorOffsets = new Dictionary<(Vector2Int, Vector2Int), float>();
        public Vector2Int startCell = Vector2Int.zero;
        public Vector2Int exitCell = Vector2Int.zero;
        public Vector3 exitPosition = Vector3.zero;
        public Dictionary<Vector2Int, int> distanceFromStart = new Dictionary<Vector2Int, int>();
        Dictionary<Vector2Int, Dictionary<Vector2Int, Vector2Int>> nextHop = new Dictionary<Vector2Int, Dictionary<Vector2Int, Vector2Int>>();
        /// Что расставлено на этаже (для проверок).
        public readonly List<Zombie> zombies = new List<Zombie>();
        public readonly List<Pickup> pickups = new List<Pickup>();

        public static int RoomCount(int floorNumber) => Mathf.Min(5 + 2 * floorNumber, 45);

        public static float ZombiesPerRoom(int floorNumber, int players) => (2f + 0.3f * floorNumber) * players * 0.75f;

        /// Строит этаж в мире. Возвращает точки старта игроков.
        public List<Vector3> Build(Transform world, int floorNumber, int players, int seed)
        {
            rng = new Rng(seed);
            Layout(RoomCount(floorNumber));
            ComputePaths();
            int far = 0;
            foreach (var c in cells)
            {
                if (distanceFromStart[c] > far)
                {
                    far = distanceFromStart[c];
                    exitCell = c;
                }
            }
            exitPosition = Center(exitCell);
            BuildGeometry(world);
            Populate(floorNumber, players);
            var starts = new List<Vector3>();
            foreach (var offset in new[] { new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(-1, 0, -1), new Vector3(1, 0, -1) })
                starts.Add(Center(startCell) + offset + Vector3.up * 0.1f);
            return starts;
        }

        public Vector3 Center(Vector2Int c) => new Vector3(c.x * CELL, 0f, c.y * CELL);

        public Vector2Int CellOf(Vector3 pos) => new Vector2Int(Mathf.RoundToInt(pos.x / CELL), Mathf.RoundToInt(pos.z / CELL));

        /// Куда идти из точки from, чтобы добраться до точки to: в ту же комнату — прямо,
        /// в другую — к двери в следующую комнату по кратчайшему пути.
        public Vector3 NextWaypoint(Vector3 from, Vector3 to)
        {
            var a = CellOf(from);
            var b = CellOf(to);
            if (a == b || !nextHop.TryGetValue(b, out var hops) || !hops.TryGetValue(a, out var n)) return to;
            var dir = new Vector3(n.x - a.x, 0f, n.y - a.y);
            var door = DoorPosition(a, n);
            // Сначала выйти на линию двери, потом пройти сквозь проём.
            var side = new Vector3(Mathf.Abs(dir.z), 0f, Mathf.Abs(dir.x));
            var off = Vector3.Scale(from - door, side);
            if (off.magnitude > 0.5f && Mathf.Abs(Vector3.Dot(from - door, dir)) > 1f) return door - dir * 1f;
            return door + dir * 1f;
        }

        /// Середина проёма между соседними комнатами a и b.
        public Vector3 DoorPosition(Vector2Int a, Vector2Int b)
        {
            var mid = (Center(a) + Center(b)) / 2f;
            float off = DoorOffset(a, b);
            return mid + (a.x != b.x ? new Vector3(0, 0, off) : new Vector3(off, 0, 0));
        }

        public float DoorOffset(Vector2Int a, Vector2Int b) => doorOffsets.TryGetValue(Pair(a, b), out var o) ? o : 0f;

        static (Vector2Int, Vector2Int) Pair(Vector2Int a, Vector2Int b) =>
            a.x < b.x || (a.x == b.x && a.y < b.y) ? (a, b) : (b, a);

        // --- Планировка -------------------------------------------------------------

        void Layout(int count)
        {
            cells = new List<Vector2Int> { Vector2Int.zero };
            links = new Dictionary<Vector2Int, List<Vector2Int>> { [Vector2Int.zero] = new List<Vector2Int>() };
            int guard = 0;
            while (cells.Count < count && guard < 10000)
            {
                guard++;
                var from = rng.Randf() < 0.6f
                    ? cells[rng.RangeI(Mathf.Max(0, cells.Count - 4), cells.Count - 1)]
                    : cells[rng.Next(cells.Count)];
                var next = from + Dirs[rng.Next(4)];
                if (links.ContainsKey(next)) continue;
                cells.Add(next);
                links[next] = new List<Vector2Int> { from };
                links[from].Add(next);
            }
            // Лишние двери дают кольца и пути отступления.
            foreach (var c in cells)
                foreach (var d in Dirs)
                {
                    var nb = c + d;
                    if (links.ContainsKey(nb) && !links[c].Contains(nb) && rng.Randf() < 0.15f)
                    {
                        links[c].Add(nb);
                        links[nb].Add(c);
                    }
                }
            // Двери не всегда посередине стены: примерно треть у левого края, треть у правого.
            doorOffsets = new Dictionary<(Vector2Int, Vector2Int), float>();
            foreach (var c in cells)
                foreach (var nb in links[c])
                {
                    var key = Pair(c, nb);
                    if (doorOffsets.ContainsKey(key)) continue;
                    float roll = rng.Randf();
                    doorOffsets[key] = roll < 0.4f ? 0f : (roll < 0.7f ? -DOOR_SIDE : DOOR_SIDE);
                }
        }

        void ComputePaths()
        {
            nextHop = new Dictionary<Vector2Int, Dictionary<Vector2Int, Vector2Int>>();
            foreach (var target in cells)
            {
                var hop = new Dictionary<Vector2Int, Vector2Int>();
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(target);
                var dist = new Dictionary<Vector2Int, int> { [target] = 0 };
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    foreach (var nb in links[cur])
                    {
                        if (dist.ContainsKey(nb)) continue;
                        hop[nb] = cur;
                        dist[nb] = dist[cur] + 1;
                        queue.Enqueue(nb);
                    }
                }
                nextHop[target] = hop;
                if (target == startCell) distanceFromStart = dist;
            }
        }

        // --- Геометрия --------------------------------------------------------------

        void BuildGeometry(Transform world)
        {
            WorldEnvironment.Setup(0.05f, 0.045f);
            foreach (var c in cells)
            {
                var b = Center(c);
                WorldEnvironment.Box(world, new Vector3(CELL, 0.5f, CELL), b + new Vector3(0, -0.25f, 0), 0.28f);
                WorldEnvironment.Box(world, new Vector3(CELL, 0.4f, CELL), b + new Vector3(0, HEIGHT + 0.2f, 0), 0.12f);
                foreach (var d in Dirs)
                {
                    var nb = c + d;
                    bool shared = links.ContainsKey(nb);
                    // Общую стену строит только одна из двух комнат.
                    if (shared && (d.x < 0 || d.y < 0)) continue;
                    Wall(world, c, d, links[c].Contains(nb));
                }
                if (c == startCell || c == exitCell || rng.Randf() < 0.45f)
                    WorldEnvironment.Lamp(world, b + Vector3.up * (HEIGHT - 0.4f), b + Vector3.up * (HEIGHT - 0.03f), new Vector3(0.6f, 0.06f, 0.6f), 8f, 1.1f, false);
                if (c != startCell && c != exitCell) Props(world, c);
            }
            ExitMarker(world);
        }

        void Wall(Transform world, Vector2Int c, Vector2Int d, bool door)
        {
            var mid = Center(c) + new Vector3(d.x, 0, d.y) * CELL / 2f + Vector3.up * HEIGHT / 2f;
            bool alongZ = d.x != 0;
            float shade = rng.Range(0.45f, 0.65f);
            if (!door)
            {
                WorldEnvironment.Box(world, alongZ ? new Vector3(WALL, HEIGHT, CELL) : new Vector3(CELL, HEIGHT, WALL), mid, shade);
                return;
            }
            // Проём: стена до двери и стена после неё, сдвиг двери вдоль стены — off.
            float off = DoorOffset(c, c + d);
            foreach (float side in new[] { -1f, 1f })
            {
                float near = off + side * DOOR / 2f;
                float far = side * CELL / 2f;
                float seg = Mathf.Abs(far - near);
                if (seg < 0.01f) continue;
                float along = (near + far) / 2f;
                var offset = alongZ ? new Vector3(0, 0, along) : new Vector3(along, 0, 0);
                WorldEnvironment.Box(world, alongZ ? new Vector3(WALL, HEIGHT, seg) : new Vector3(seg, HEIGHT, WALL), mid + offset, shade);
            }
            float lintelH = HEIGHT - DOOR_HEIGHT;
            var doorMid = mid + (alongZ ? new Vector3(0, 0, off) : new Vector3(off, 0, 0));
            var lintelPos = new Vector3(doorMid.x, DOOR_HEIGHT + lintelH / 2f, doorMid.z);
            WorldEnvironment.Box(world, alongZ ? new Vector3(WALL, lintelH, DOOR) : new Vector3(DOOR, lintelH, WALL), lintelPos, shade);
        }

        /// Мебель по углам комнаты, чтобы проходы между дверями оставались свободными.
        /// Угол, к которому прижата дверь, остаётся пустым.
        bool CornerHasDoor(Vector2Int c, Vector2 corner)
        {
            foreach (var nb in links[c])
            {
                var d = nb - c;
                float off = DoorOffset(c, nb);
                if (off == 0f) continue;
                // Стена по x (дверь сдвинута по z) или по z (дверь сдвинута по x).
                if (d.x != 0 && corner.x == d.x && corner.y == Mathf.Sign(off)) return true;
                if (d.y != 0 && corner.y == d.y && corner.x == Mathf.Sign(off)) return true;
            }
            return false;
        }

        void Props(Transform world, Vector2Int c)
        {
            var b = Center(c);
            var corners = new List<Vector2>();
            foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                if (!CornerHasDoor(c, corner)) corners.Add(corner);
            G.Shuffle(corners, rng.Raw);
            float inset = CELL / 2f - 1.3f;
            for (int i = Mathf.Min(rng.RangeI(1, 3), corners.Count) - 1; i >= 0; i--)
            {
                var p = corners[i] * inset;
                var pos = b + new Vector3(p.x, 0, p.y);
                switch (rng.Next(3))
                {
                    case 0:
                    {
                        float s = rng.Range(0.9f, 1.3f);
                        WorldEnvironment.Box(world, new Vector3(s, s, s), pos + Vector3.up * s / 2f, 0.8f, rng.Range(0f, Mathf.PI));
                        break;
                    }
                    case 1:
                        WorldEnvironment.Box(world, new Vector3(1.6f, 0.9f, 0.9f), pos + Vector3.up * 0.45f, 0.7f, rng.Range(-0.3f, 0.3f));
                        break;
                    default:
                        WorldEnvironment.Box(world, new Vector3(0.7f, 2.1f, 1.3f), pos + Vector3.up * 1.05f, 0.6f);
                        break;
                }
            }
        }

        static Material exitMat;

        /// Выход: светящийся проём лестницы вниз и надпись.
        void ExitMarker(Transform world)
        {
            if (exitMat == null) exitMat = MeshKit.Lit(Color.white, Color.white * 2.5f);
            var glow = MeshKit.Add(world, MeshKit.Box(new Vector3(2.4f, 0.04f, 2.4f)), exitMat, Vector3.zero, default, null, "Exit");
            glow.position = exitPosition + Vector3.up * 0.02f;
            MeshKit.Label(world, "ВЫХОД", exitPosition + Vector3.up * 2.2f, 0.34f);
            MeshKit.PointLight(world, exitPosition + Vector3.up * 1f, 6f, 2f);
        }

        // --- Зомби и предметы -------------------------------------------------------

        void Populate(int floorNumber, int players)
        {
            float perRoom = ZombiesPerRoom(floorNumber, players);
            int total = 0;
            var rooms = new List<Vector2Int>(cells);
            G.Shuffle(rooms, rng.Raw);
            foreach (var c in rooms)
            {
                if (c == startCell) continue;
                int count = (int)(perRoom + rng.Randf());
                // Соседние со стартом комнаты спокойнее, чтобы было время осмотреться.
                bool near = distanceFromStart[c] == 1;
                if (near) count = (int)(count / 2f);
                for (int i = 0; i < count; i++)
                {
                    if (total >= MAX_ZOMBIES) break;
                    var offset = new Vector3(rng.Range(-2.5f, 2.5f), 0.1f, rng.Range(-2.5f, 2.5f));
                    if (near)
                    {
                        // У дальней стены, чтобы не заметили игроков в первую же секунду.
                        var away = ((Vector2)(c - startCell)).normalized;
                        offset += new Vector3(away.x, 0, away.y) * 3f;
                        offset.x = Mathf.Clamp(offset.x, -3.2f, 3.2f);
                        offset.z = Mathf.Clamp(offset.z, -3.2f, 3.2f);
                    }
                    zombies.Add(Zombie.Spawn(Center(c) + offset, rng.Range(0f, 360f), this, false));
                    total++;
                }
            }

            // На первом этаже в стартовой комнате по пистолету на каждого.
            if (floorNumber == 1)
                for (int i = 0; i < players; i++)
                    pickups.Add(Pickup.Spawn("weapon", "pistol", Center(startCell) + new Vector3(-1.5f + i, 0, 2f)));
            float deeper = Mathf.Clamp01((floorNumber - 1) / 6f);
            // Аптечки редкие: не больше одной на 5 комнат, чтобы лечение было ценным.
            int medkitsLeft = Mathf.Max(1, (int)(cells.Count / 5f));
            foreach (var c in cells)
            {
                if (c == startCell) continue;
                float roll = rng.Randf();
                var spot = Center(c) + new Vector3(rng.Range(-2f, 2f), 0, rng.Range(-2f, 2f));
                if (roll < 0.5f) pickups.Add(Pickup.Spawn("ammo", RandomGun(deeper), spot));
                else if (roll < 0.68f) pickups.Add(Pickup.Spawn("weapon", RandomGun(deeper), spot));
                else if (roll < 0.8f && medkitsLeft > 0)
                {
                    medkitsLeft--;
                    pickups.Add(Pickup.Spawn("medkit", "", spot));
                }
            }
        }

        /// Чем глубже этаж, тем чаще попадаются автомат и дробовик.
        string RandomGun(float deeper)
        {
            float r = rng.Randf();
            if (r < 0.5f - 0.25f * deeper) return "pistol";
            if (r < 0.78f - 0.1f * deeper) return "smg";
            return "shotgun";
        }
    }
}
