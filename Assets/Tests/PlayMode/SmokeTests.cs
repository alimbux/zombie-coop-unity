using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieCoop.Tests
{
    /// Быстрая проверка игры (Window → General → Test Runner → PlayMode → Run All).
    /// То же, что tests/smoke_test.gd в Godot-версии: генератор этажей строит связный уровень,
    /// ранения работают по зонам, оружие тратит и находит патроны, патронами можно поделиться.
    public class SmokeTests
    {
        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static GameRoot Root => GameRoot.Instance;

        IEnumerator StartWaves(int players)
        {
            yield return null;
            Assert.IsNotNull(Root, "GameRoot запускается сам");
            GameConfig.NewGame(GameMode.Waves, players);
            Root.StartGame();
            Root.spawner.enabled = false;
            yield return Frames(3);
        }

        static Zombie Spawn(Vector3 pos)
        {
            return Zombie.Spawn(pos, 0f, null, true);
        }

        [TearDown]
        public void BackToMenu()
        {
            if (Root != null) Root.OpenMenu();
        }

        [UnityTest]
        public IEnumerator Menu()
        {
            yield return null;
            Root.OpenMenu();
            Root.SetMode(GameMode.Waves);
            Root.SetPlayers(3);
            Root.StartFromMenu();
            yield return Frames(2);
            Assert.AreEqual(GameMode.Waves, GameConfig.Mode);
            Assert.AreEqual(3, GameConfig.Players);
            Assert.AreEqual(3, Root.players.Count, "трое игроков на трёх частях экрана");
        }

        [UnityTest]
        public IEnumerator Generator()
        {
            yield return null;
            Root.OpenMenu();
            foreach (int floor in new[] { 1, 4, 12 })
            foreach (int players in new[] { 1, 4 })
            {
                var world = new GameObject("TestWorld").transform;
                GameRoot.World = world;
                var gen = new LevelGenerator();
                var starts = gen.Build(world, floor, players, 1000 + floor * 7 + players);
                string tag = "этаж " + floor + ", игроков " + players;
                Assert.AreEqual(Mathf.Min(5 + 2 * floor, 45), gen.cells.Count, tag + ": число комнат");
                Assert.GreaterOrEqual(starts.Count, players, tag + ": стартовые точки");
                Assert.AreNotEqual(gen.startCell, gen.exitCell, tag + ": выход не в стартовой комнате");
                // Из любой комнаты по подсказкам навигации можно дойти до старта.
                foreach (var c in gen.cells)
                {
                    var pos = gen.Center(c);
                    int steps = 0;
                    while (gen.CellOf(pos) != gen.startCell && steps < 100)
                    {
                        pos = gen.NextWaypoint(pos, gen.Center(gen.startCell));
                        steps++;
                    }
                    Assert.AreEqual(gen.startCell, gen.CellOf(pos), tag + ": путь к игрокам из " + c);
                }
                Assert.Greater(gen.zombies.Count, 0, tag + ": есть зомби");
                Assert.IsFalse(gen.zombies.Any(z => gen.CellOf(z.transform.position) == gen.startCell), tag + ": в стартовой комнате нет зомби");
                int medkits = gen.pickups.Count(p => p.kind == "medkit");
                Assert.LessOrEqual(medkits, Mathf.Max(1, gen.cells.Count / 5), tag + ": аптечки редкие");
                Object.DestroyImmediate(world.gameObject);
                Zombie.All.Clear();
                Pickup.All.Clear();
                yield return null;
            }
            GameRoot.World = null;
        }

        [UnityTest]
        public IEnumerator Wounds()
        {
            yield return StartWaves(2);

            var z1 = Spawn(new Vector3(4, 0.1f, 3));
            yield return Frames(2);
            Assert.IsTrue(z1.TakeHit("head", 34f, z1.HeadCenter(), Vector3.forward), "попадание в голову убивает");

            var z2 = Spawn(new Vector3(6, 0.1f, 3));
            yield return Frames(2);
            var leg = z2.transform.position + Vector3.up * 0.3f;
            z2.TakeHit("leg_l", 34f, leg, Vector3.forward);
            z2.TakeHit("leg_l", 34f, leg, Vector3.forward);
            Assert.IsTrue(z2.crawling && !z2.dead && z2.crawlStyle == "right", "без левой ноги ползёт с опорой на правую");
            z2.TakeHit("leg_r", 60f, leg, Vector3.forward);
            Assert.IsTrue(z2.crawlStyle == "arms" && !z2.dead, "без обеих ног ползёт на руках");

            var zr = Spawn(new Vector3(2, 0.1f, 3));
            yield return Frames(2);
            zr.TakeHit("leg_r", 60f, zr.transform.position + Vector3.up * 0.3f, Vector3.forward);
            Assert.AreEqual("left", zr.crawlStyle, "без правой ноги ползёт с опорой на левую");
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue(!zr.dead && zr.body.rot.x < -1f, "ползущий лежит лицом вниз");

            // Походки: хромой в среднем медленнее, быстрый — быстрее обычного.
            var zw = Spawn(new Vector3(-8, 0.1f, 9));
            yield return null;
            var avg = new Dictionary<string, float>();
            foreach (var g in new[] { "walk", "limp", "fast" })
            {
                zw.gait = g;
                float total = 0f;
                for (int i = 0; i < 60; i++)
                {
                    zw.phase = G.TAU * i / 60f;
                    total += zw.MoveSpeed();
                }
                avg[g] = total / 60f;
            }
            Object.Destroy(zw.gameObject);
            Assert.IsTrue(avg["fast"] > avg["walk"] * 1.4f && avg["limp"] < avg["walk"], "быстрые быстрее, хромые медленнее");

            // Вскользь по голове: череп вскрыт, зомби жив; второе попадание в голову убивает.
            var zg = Spawn(new Vector3(-4, 0.1f, 3));
            yield return Frames(2);
            var center = zg.HeadCenter();
            var edge = center + new Vector3(0.13f, 0, 0);
            Assert.IsTrue(zg.IsGlancing(edge, Vector3.forward) && !zg.IsGlancing(center, Vector3.forward), "край головы — вскользь, центр — нет");
            Assert.IsFalse(zg.TakeHit("head", 34f, edge, Vector3.forward), "вскользь по голове зомби жив");
            Assert.IsTrue(zg.skullOpen, "череп вскрыт");
            Assert.IsTrue(zg.TakeHit("head", 22f, center, Vector3.forward), "второе попадание во вскрытую голову убивает");

            var z3 = Spawn(new Vector3(8, 0.1f, 3));
            yield return Frames(2);
            int hits = 0;
            while (!z3.dead && hits < 10)
            {
                z3.TakeHit("torso", 34f, z3.transform.position + Vector3.up, Vector3.forward);
                hits++;
            }
            Assert.IsTrue(z3.dead && hits == 4, "торс убивает за 4 попадания (было " + hits + ")");

            // Следы крови разной формы, текстуры нарисованы.
            var kinds = new HashSet<string>(Gore.Stains.Where(s => s != null).Select(s => s.GetComponent<MeshRenderer>().sharedMaterial.name));
            Assert.GreaterOrEqual(kinds.Count, 3, "пятна крови разной формы");
            foreach (var kind in BloodStains.Kinds)
            {
                float cover = BloodStains.Coverage(BloodStains.Texture(kind, 0));
                Assert.IsTrue(cover > 0.02f && cover < 0.95f, "форма «" + kind + "» нарисована");
            }
        }

        [UnityTest]
        public IEnumerator WeaponsAndAmmo()
        {
            yield return StartWaves(2);
            var p1 = Root.players[0];
            var p2 = Root.players[1];
            Assert.AreEqual("knife", p1.current);
            CollectionAssert.AreEqual(new[] { "knife" }, p1.owned, "в начале только нож");
            Assert.IsTrue(p1.GiveWeapon("pistol", 24) && p1.current == "pistol", "подобрал пистолет и взял в руки");
            Assert.AreEqual(12, p1.mag["pistol"]);
            Assert.AreEqual(12, p1.reserve["pistol"]);
            p1.GiveAmmo("pistol", 500);
            Assert.AreEqual(Weapons.Get("pistol").maxReserve, p1.reserve["pistol"], "запас не больше предела");

            foreach (var z in Zombie.All.ToArray()) Object.Destroy(z.gameObject);
            p1.shake = 0f;
            p1.yaw = 0f;
            p1.pitch = 0f;
            yield return Frames(2);
            var z4 = Spawn(p1.transform.position + p1.Forward * 4f);
            z4.enabled = false;
            yield return Frames(2);
            // Голова ровно на линии взгляда.
            z4.Teleport(z4.transform.position + Vector3.up * (p1.cam.transform.position.y - z4.HeadCenter().y));
            yield return null;
            p1.fireCooldown = 0f;
            string zone = p1.Shoot();
            Assert.IsTrue(zone == "head" && z4.dead, "выстрел из пистолета в голову (зона «" + zone + "»)");
            Assert.AreEqual(11, p1.mag["pistol"], "выстрел тратит патрон");

            // Передача патронов: p2 рядом с p1.
            p2.Teleport(p1.transform.position + Vector3.right);
            yield return null;
            int before = p1.reserve["pistol"];
            int given = p1.ShareAmmo();
            Assert.IsTrue(given > 0 && p2.reserve["pistol"] == given && p1.reserve["pistol"] == before - given, "половина запаса ушла напарнику");

            // Аптечка лечит раненого и не тратится на здорового.
            Assert.IsFalse(p2.Heal(40f), "здоровому аптечка не нужна");
            p2.TakeDamage(50f);
            Assert.IsTrue(p2.Heal(40f) && Mathf.Approximately(p2.hp, 90f), "аптечка лечит 40");

            // Перезарядка: пустой магазин наполняется из запаса.
            p1.mag["pistol"] = 0;
            p1.StartReload();
            yield return new WaitForSeconds(Weapons.Get("pistol").reload + 0.2f);
            Assert.AreEqual(12, p1.mag["pistol"], "перезарядка наполнила магазин");
        }

        [UnityTest]
        public IEnumerator Knockback()
        {
            yield return StartWaves(1);
            var dist = new Dictionary<string, float>();
            foreach (var w in new[] { "smg", "shotgun" })
            {
                var zk = Spawn(new Vector3(w == "smg" ? -9 : -4, 0.1f, -3));
                zk.aware = false;
                zk.lookTimer = 100f;
                yield return Frames(3);
                var start = zk.transform.position;
                int pushes = w == "shotgun" ? Weapons.Get(w).pellets : 2;
                for (int i = 0; i < pushes; i++) zk.Push(Vector3.back, Weapons.Get(w).knockback);
                if (w == "shotgun") Assert.IsTrue(zk.knockedDown > 0f, "дробовик в упор сбивает с ног");
                yield return new WaitForSeconds(0.75f);
                dist[w] = Vector3.Distance(G.Flat(zk.transform.position), G.Flat(start));
                Object.Destroy(zk.gameObject);
            }
            Assert.IsTrue(dist["shotgun"] > 2f && dist["shotgun"] > dist["smg"] * 3f,
                "отдача: дробовик " + dist["shotgun"] + " м, автомат " + dist["smg"] + " м");

            // Удар ногой: летящий зомби сбивает стоящего за ним.
            var p = Root.players[0];
            p.yaw = 0f;
            var front = Spawn(p.transform.position + p.Forward * 1.2f);
            var back = Spawn(p.transform.position + p.Forward * 2.0f);
            front.aware = back.aware = false;
            front.lookTimer = back.lookTimer = 100f;
            yield return Frames(2);
            p.kickCooldown = 0f;
            Assert.AreEqual(front, p.Kick(), "удар ногой задевает ближнего");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(back.knockedDown > 0f || back.Knock.magnitude > 1f, "летящий сбивает того, кто стоит за ним");
        }
    }
}
