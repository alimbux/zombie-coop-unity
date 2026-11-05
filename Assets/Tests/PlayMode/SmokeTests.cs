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
            // Живучие, как в ролике: голова выдерживает два выстрела из пистолета, третий убивает.
            int headHits = 0;
            while (!z1.dead && headHits < 10)
            {
                z1.TakeHit("head", 34f, z1.HeadCenter(), Vector3.forward);
                headHits++;
            }
            Assert.IsTrue(z1.dead && headHits == 3, "голову пробивает третья пуля пистолета (было " + headHits + ")");

            var z2 = Spawn(new Vector3(6, 0.1f, 3));
            yield return Frames(2);
            var leg = z2.transform.position + Vector3.up * 0.3f;
            z2.TakeHit("leg_l", 200f, leg, Vector3.forward);
            Assert.IsTrue(z2.crawling && !z2.dead && z2.crawlStyle == "right", "без левой ноги ползёт с опорой на правую");
            z2.TakeHit("leg_r", 200f, leg, Vector3.forward);
            Assert.IsTrue(z2.crawlStyle == "arms" && !z2.dead, "без обеих ног ползёт на руках");

            var zr = Spawn(new Vector3(2, 0.1f, 3));
            yield return Frames(2);
            zr.TakeHit("leg_r", 200f, zr.transform.position + Vector3.up * 0.3f, Vector3.forward);
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
            Assert.IsTrue(!zg.TakeHit("head", 22f, center, Vector3.forward) && !zg.dead, "вскрытая голова держит ещё пулю автомата");
            zg.TakeHit("head", 22f, center, Vector3.forward);
            Assert.IsTrue(zg.dead, "вторая пуля во вскрытую голову убивает");

            var z3 = Spawn(new Vector3(8, 0.1f, 3));
            yield return Frames(2);
            int hits = 0;
            while (!z3.dead && hits < 10)
            {
                z3.TakeHit("torso", 34f, z3.transform.position + Vector3.up, Vector3.forward);
                hits++;
            }
            Assert.IsTrue(z3.dead && hits == 8, "торс выдерживает 7 пуль пистолета, восьмая убивает (было " + hits + ")");
            Assert.IsTrue(z3.wounds >= 7 && z3.partWounds["torso"] >= 7, "каждая пуля оставляет рану (" + z3.wounds + ")");
            // Раны лежат на коже, а не висят в воздухе у тела.
            var torsoT = z3.parts["torso"].transform;
            int onSkin = 0, woundCount = 0;
            foreach (Transform w in torsoT)
            {
                if (w.name != "Wound") continue;
                woundCount++;
                var n = w.localRotation * Vector3.up;
                var surf = z3.SurfaceHit(torsoT, w.localPosition + n * 0.3f, -n);
                if (surf != null && Vector3.Dot(w.localPosition - surf.Value.pos, n) < 0.015f) onSkin++;
            }
            Assert.IsTrue(woundCount >= 7 && onSkin == woundCount, "раны на поверхности тела (" + onSkin + " из " + woundCount + ")");

            // Рана на предплечье крепится к локтю и сгибается вместе с ним.
            var za = Spawn(new Vector3(10, 0.1f, 3));
            yield return Frames(2);
            var elbow = za.parts["arm_l"].transform.Find("Elbow");
            za.TakeHit("arm_l", 10f, elbow.position + elbow.TransformDirection(Vector3.down) * 0.15f - Vector3.forward * 0.3f, Vector3.forward);
            Assert.IsTrue(elbow.Cast<Transform>().Any(c => c.name == "Wound"), "рана на предплечье крепится к локтю");

            // Рука отрывается кусками: сначала предплечье с кистью, потом плечо.
            var zl = Spawn(new Vector3(12, 0.1f, 6));
            zl.enabled = false;
            yield return Frames(2);
            float smg = Weapons.Get("smg").damage;
            System.Func<Vector3> armPoint = () => zl.parts["arm_r"].transform.TransformPoint(G.P(0, -0.15f, 0));
            zl.TakeHit("arm_r", smg, armPoint(), Vector3.forward);
            Assert.IsNotNull(zl.parts["arm_r"].transform.Find("Elbow"), "одна пуля автомата руку не трогает");
            zl.TakeHit("arm_r", smg, armPoint(), Vector3.forward);
            Assert.IsTrue(zl.parts.ContainsKey("arm_r") && zl.parts["arm_r"].transform.Find("Elbow") == null, "вторая пуля отрывает предплечье, плечо осталось");
            zl.TakeHit("arm_r", smg, armPoint(), Vector3.forward);
            zl.TakeHit("arm_r", smg, armPoint(), Vector3.forward);
            Assert.IsTrue(!zl.parts.ContainsKey("arm_r") && !zl.dead, "четвёртая пуля отрывает плечо");

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
            Assert.IsTrue(zone == "head" && !z4.dead && p1.mag["pistol"] == 11, "выстрел в голову попал, зомби ещё жив, патрон потрачен (зона «" + zone + "»)");
            for (int i = 0; i < 2; i++)
            {
                p1.fireCooldown = 0f;
                p1.Shoot();
            }
            Assert.IsTrue(z4.dead, "третий выстрел из пистолета в голову убивает");
            p1.mag["pistol"] = 11;

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
    
        [UnityTest]
        public IEnumerator Glass()
        {
            yield return null;
            Root.OpenMenu();
            Assert.IsNotNull(Sfx.Clip("glass_crack"), "звук треска стекла");
            Assert.IsNotNull(Sfx.Clip("glass_break"), "звук разбитого стекла");
            foreach (var (floor, seed) in new[] { (1, 11), (4, 22), (9, 33), (12, 44) })
            {
                var world = new GameObject("TestWorld").transform;
                GameRoot.World = world;
                var gen = new LevelGenerator();
                gen.Build(world, floor, 1, seed);
                string tag = "стекло, этаж " + floor;
                Assert.IsTrue(gen.glassWalls.Count > 0, tag + ": окна есть (" + gen.glassWalls.Count + ")");
                bool outerOk = true, sectionsOk = true;
                foreach (var g in gen.glassWalls)
                {
                    outerOk &= !gen.links.ContainsKey(g.cell + g.dir);
                    sectionsOk &= g.window.sections.Count >= 2 && g.window.sections.Count <= 4;
                }
                Assert.IsTrue(outerOk, tag + ": стекло только в наружных стенах, не в дверях");
                Assert.IsTrue(sectionsOk, tag + ": в окне 2–4 секции");
                int outer = 0;
                foreach (var c in gen.cells)
                    foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                        if (!gen.links.ContainsKey(c + d)) outer++;
                Assert.GreaterOrEqual(gen.glassWalls.Count, Mathf.Min(3, outer), tag + ": окон не меньше трёх");
                // Город вокруг этажа: башни не ближе 25 м от края.
                var city = world.GetComponentInChildren<CityInfo>();
                Assert.IsNotNull(city, tag + ": город построен");
                Assert.Greater(city.towers.Count, 20, tag + ": в городе есть башни");
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (var c in gen.cells)
                {
                    var p = gen.Center(c);
                    minX = Mathf.Min(minX, p.x - LevelGenerator.CELL / 2f);
                    maxX = Mathf.Max(maxX, p.x + LevelGenerator.CELL / 2f);
                    minZ = Mathf.Min(minZ, p.z - LevelGenerator.CELL / 2f);
                    maxZ = Mathf.Max(maxZ, p.z + LevelGenerator.CELL / 2f);
                }
                bool gapOk = true;
                foreach (var t in city.towers)
                {
                    float dx = Mathf.Max(Mathf.Max(minX - t.pos.x, 0f), t.pos.x - maxX);
                    float dz = Mathf.Max(Mathf.Max(minZ - t.pos.y, 0f), t.pos.y - maxZ);
                    gapOk &= new Vector2(dx, dz).magnitude >= 25f + t.r - 0.01f;
                }
                Assert.IsTrue(gapOk, tag + ": башни не ближе 25 м от этажа");
                Object.DestroyImmediate(world.gameObject);
                Zombie.All.Clear();
                Pickup.All.Clear();
                yield return null;
            }

            var w = new GameObject("TestWorld").transform;
            GameRoot.World = w;
            var level = new LevelGenerator();
            level.Build(w, 6, 1, 77);
            yield return Frames(2);
            var win = level.glassWalls[0].window;
            var fired = new List<GlassSection>();
            win.Broken += s => fired.Add(s);
            var sec = win.sections[0];
            var pt = sec.Center;
            var dir = -sec.outward;
            sec.Hit(34f, pt, dir);
            Assert.IsTrue(!sec.broken && sec.hp > 0f, "одно попадание только трескает стекло");
            Assert.GreaterOrEqual(sec.transform.childCount, 2, "на стекле остаётся трещина");
            sec.Hit(34f, pt, dir);
            Assert.IsTrue(sec.broken && fired.Count == 1 && fired[0] == sec, "второе попадание разбивает секцию и шлёт событие");
            foreach (var other in win.sections)
                if (other != sec) Assert.IsFalse(other.broken, "соседняя секция цела");
            yield return Frames(3);
            // Луч снаружи до самой плоскости стекла и чуть внутрь, чтобы не задеть мебель в углу.
            Assert.IsFalse(Physics.Raycast(pt - dir * 2f, dir, 2.3f, Layers.WorldMask, QueryTriggerInteraction.Ignore),
                "сквозь разбитую секцию луч проходит");
            if (win.sections.Count > 1)
            {
                var other = win.sections[1];
                Assert.IsTrue(Physics.Raycast(other.Center - dir * 2f, dir, out var hit, 4f, Layers.WorldMask, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponent<GlassSection>() != null, "целая секция держит луч");
            }
            Object.DestroyImmediate(w.gameObject);
            Zombie.All.Clear();
            Pickup.All.Clear();
            GameRoot.World = null;
        }

        /// Из разбитой секции вылетают и умирают, из целой — нет; вылетевший игрок не поднимается.
        [UnityTest]
        public IEnumerator WindowFall()
        {
            yield return null;
            Root.OpenMenu();
            var world = new GameObject("TestWorld").transform;
            GameRoot.World = world;
            var gen = new LevelGenerator();
            gen.Build(world, 6, 1, 77);
            foreach (var z in gen.zombies.ToArray())
                if (z != null) Object.DestroyImmediate(z.gameObject);
            Zombie.All.Clear();
            yield return Frames(2);
            var win = gen.glassWalls[0].window;
            var outv = win.outward;
            var brokenSec = win.sections[0];
            var intactSec = win.sections[1];
            brokenSec.Hit(34f, brokenSec.Center, -outv);
            brokenSec.Hit(34f, brokenSec.Center, -outv);
            Assert.IsTrue(brokenSec.broken, "окно: секция разбита");
            yield return Frames(3);

            Vector3 Inside(GlassSection s, float meters)
            {
                var p = s.Center - outv * meters;
                p.y = 0.05f;
                return p;
            }

            var camA = new GameObject("CamA").AddComponent<Camera>();
            camA.transform.SetParent(world, false);
            var attacker = Player.Create(0, null, camA, new PlayerHud(), gen.Center(gen.glassWalls[0].cell) + Vector3.up * 0.1f);
            var zf = Spawn(Inside(brokenSec, 1f));
            var causes = new List<string>();
            zf.Died += c => causes.Add(c);
            zf.aware = false;
            zf.lookTimer = 100f;
            yield return Frames(3);
            zf.Kicked(outv, 17f, 8f, attacker);
            yield return new WaitForSeconds(2.5f);
            Assert.IsTrue(zf == null || (zf.dead && zf.falling && zf.transform.position.y < -1f), "зомби, выбитый в разбитое окно, падает и умирает");
            Assert.AreEqual(1, attacker.kills, "падение засчитано как убийство");
            Assert.IsTrue(causes.Contains("fall"), "событие смерти от падения");
            Assert.IsFalse(Zombie.All.Contains(zf), "упавший больше не в списке живых");

            var zi = Spawn(Inside(intactSec, 1f));
            zi.aware = false;
            zi.lookTimer = 100f;
            yield return Frames(3);
            zi.Kicked(outv, 17f, 8f, attacker);
            yield return new WaitForSeconds(2f);
            Assert.IsTrue(!zi.falling && zi.transform.position.y > -0.2f && Vector3.Dot(zi.transform.position - win.transform.position, outv) < 0f,
                "у целой секции зомби остаётся в комнате");
            Object.Destroy(zi.gameObject);

            // Игрок: перешагнул плоскость в разбитой секции.
            var camB = new GameObject("CamB").AddComponent<Camera>();
            camB.transform.SetParent(world, false);
            var hud = new PlayerHud();
            var player = Player.Create(1, null, camB, hud, Inside(brokenSec, 0.8f));
            player.GiveWeapon("pistol", 20);
            yield return Frames(3);
            for (int i = 0; i < 90 && !player.fell; i++)
            {
                // Ввода у тестового игрока нет, поэтому шагаем сдвигом положения.
                player.Teleport(player.transform.position + outv * 0.08f);
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(player.fell && player.downed && player.hp == 0f, "игрок, шагнувший в разбитое окно, выпал и умер");
            // Напарник рядом, но выпавшего поднять нельзя.
            var matePos = attacker.transform.position;
            attacker.Teleport(player.transform.position);
            for (int i = 0; i < 200; i++) player.TryRevive(0.05f);
            attacker.Teleport(matePos);
            Assert.IsTrue(player.downed && player.fell, "выпавшего поднять нельзя");
            var kit = player.GetLoadout();
            Assert.IsTrue(kit.owned.Count == 1 && kit.owned[0] == "knife" && kit.current == "knife" && kit.reserve["pistol"] == 0 && kit.mag["pistol"] == 0,
                "после падения снаряжение — только нож");
            yield return new WaitForSeconds(1.6f);
            Assert.Less(player.transform.position.y, -1f, "выпавший игрок летит вниз");
            Assert.AreEqual("РАЗБИЛСЯ", hud.deadText, "экран «РАЗБИЛСЯ»");
            Object.DestroyImmediate(world.gameObject);
            Zombie.All.Clear();
            Pickup.All.Clear();
            GameRoot.World = null;
        }

        [UnityTest]
        public IEnumerator SideDoors()
        {
            yield return null;
            Root.OpenMenu();
            var world = new GameObject("TestWorld").transform;
            GameRoot.World = world;
            var gen = new LevelGenerator();
            gen.Build(world, 8, 1, 4242);
            int center = gen.doorOffsets.Values.Count(o => o == 0f);
            int side = gen.doorOffsets.Values.Count(o => o != 0f);
            Assert.IsTrue(center > 0 && side > 0, "двери и по центру (" + center + "), и у края стены (" + side + ")");
            Object.DestroyImmediate(world.gameObject);
            Zombie.All.Clear();
            Pickup.All.Clear();
            GameRoot.World = null;
        }

        [UnityTest]
        public IEnumerator CrawlAndBodies()
        {
            yield return StartWaves(1);
            // На коленях ползут медленнее, чем раньше; без ног медленнее, чем на коленях;
            // без ног и без рук — медленнее и ниже всех.
            var cases = new[]
            {
                ("knees", new string[0]), ("left", new[] { "leg_r" }), ("arms", new[] { "leg_l", "leg_r" }),
                ("arms", new[] { "leg_l", "leg_r", "arm_l" }),
            };
            var factors = new List<float>();
            var lows = new List<float>();
            foreach (var c in cases)
            {
                var z = Spawn(new Vector3(-10, 0.1f, -8));
                z.enabled = false;
                z.crawling = true;
                z.crawlStyle = c.Item1;
                foreach (var part in c.Item2) z.parts.Remove(part);
                factors.Add(z.CrawlFactor());
                lows.Add(z.Low());
                Object.Destroy(z.gameObject);
            }
            var stump = Spawn(new Vector3(-10, 0.1f, -8));
            stump.enabled = false;
            stump.crawling = true;
            stump.crawlStyle = "arms";
            foreach (var part in new[] { "leg_l", "leg_r", "arm_l" }) stump.parts.Remove(part);
            Object.DestroyImmediate(stump.parts["arm_r"].transform.Find("Elbow").gameObject);
            factors.Add(stump.CrawlFactor());
            lows.Add(stump.Low());
            Object.Destroy(stump.gameObject);
            Assert.Less(factors[0], 1f, "на коленях медленнее прежнего");
            for (int i = 1; i < factors.Count; i++)
                Assert.Less(factors[i], factors[i - 1], "чем меньше конечностей, тем медленнее ползёт: " + string.Join(", ", factors));
            Assert.IsTrue(lows[3] > lows[2] && lows[4] > lows[3] && lows[2] == 0f, "без рук ползёт ниже: " + string.Join(", ", lows));

            // Раненый оседает на колени, потом ползёт на четвереньках, убитый лежит трупом.
            var zk = Spawn(new Vector3(-6, 0.1f, -6));
            yield return Frames(2);
            zk.StartKneel();
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(zk.crawling && zk.crawlStyle == "knees" && zk.body.pos.y < -0.25f, "раненый оседает на колени (" + zk.body.pos.y + ")");
            var kneelAt = zk.transform.position;
            yield return new WaitForSeconds(3f);
            float crawled = G.Flat(zk.transform.position - kneelAt).magnitude;
            Assert.IsTrue(zk.parts["torso"].rot.x < -0.8f && crawled > 0.3f, "потом ползёт на четвереньках (наклон " + zk.parts["torso"].rot.x + ", прополз " + crawled + " м)");
            zk.TakeHit("torso", 400f, zk.parts["torso"].transform.position, Vector3.forward);
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(zk != null && zk.dead && zk.landed && !Zombie.All.Contains(zk), "ползущий обмякает и остаётся лежать трупом");
            // Ни одна часть тела трупа не уходит под пол.
            Assert.GreaterOrEqual(zk.Lowest(zk.body.transform), -0.002f, "труп не проваливается под пол");

            // Труп дёргается от пуль, изрешечённый разваливается.
            float jerkBefore = zk.jerkV.magnitude;
            zk.HitCorpse("torso", 16f, zk.parts["torso"].transform.position, Vector3.forward, 1f);
            Assert.Greater(zk.jerkV.magnitude, jerkBefore + 0.5f, "труп дёргается от пули");
            for (int i = 0; i < 12 && zk != null && zk.parts.ContainsKey("torso"); i++)
                zk.HitCorpse("torso", 16f, zk.parts["torso"].transform.position, Vector3.forward, 1f);
            yield return null;
            Assert.IsTrue(zk == null, "изрешечённый труп разваливается на куски");

            // Убитый в корпус падает целиком и лежит.
            var zd = Spawn(new Vector3(-2, 0.1f, -6));
            yield return Frames(2);
            zd.TakeHit("torso", 600f, zd.parts["torso"].transform.position, Vector3.forward);
            yield return new WaitForSeconds(2.5f);
            Assert.IsTrue(zd != null && zd.landed && Mathf.Abs(zd.body.rot.x) + Mathf.Abs(zd.body.rot.z) > 1.2f, "убитый падает целиком и лежит трупом");
            Assert.GreaterOrEqual(zd.Lowest(zd.body.transform), -0.002f, "лежащий труп не уходит под пол");
        }

        [Test]
        public void WeaponSounds()
        {
            foreach (var gun in Sfx.Guns)
                foreach (var sound in new[] { "shot_", "reload_", "click_" })
                {
                    var clip = Sfx.Clip(sound + gun);
                    Assert.IsTrue(clip != null && !clip.name.StartsWith(sound + gun + "0"), sound + gun + " — запись, а не синтез");
                }
            foreach (var voice in new[] { "pain", "yelp", "shriek", "death" })
                Assert.IsTrue(Resources.Load<AudioClip>("ZombieCoop/Sounds/" + voice + "_1") != null, "вскрик " + voice + " — запись");
        }
    }
}
