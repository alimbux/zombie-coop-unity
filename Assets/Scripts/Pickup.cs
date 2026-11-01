using System.Collections.Generic;
using UnityEngine;

namespace ZombieCoop
{
    /// Предмет на уровне: оружие, патроны или аптечка. Подбирается, если игрок подошёл
    /// и предмет ему пригодится (полный запас патронов или полное здоровье — остаётся лежать).
    public class Pickup : MonoBehaviour
    {
        public static readonly List<Pickup> All = new List<Pickup>();
        const float MEDKIT_HEAL = 40f;
        const float RADIUS = 0.9f;

        /// "weapon", "ammo" или "medkit".
        public string kind = "ammo";
        public string weapon = "pistol";
        Transform model;
        float recheck;
        readonly HashSet<Player> inside = new HashSet<Player>();

        public static Pickup Spawn(string kind, string weapon, Vector3 position)
        {
            var go = new GameObject("Pickup " + kind);
            go.transform.SetParent(GameRoot.World, false);
            go.transform.position = position;
            var p = go.AddComponent<Pickup>();
            p.kind = kind;
            p.weapon = weapon;
            p.Build();
            return p;
        }

        void Awake() { All.Add(this); }
        void OnDestroy() { All.Remove(this); }

        void Update()
        {
            if (model == null) return;
            model.localRotation = Quaternion.Euler(0f, Time.time * 1.5f * Mathf.Rad2Deg, 0f);
            model.localPosition = new Vector3(0f, 0.55f + Mathf.Sin(Time.time * 3f) * 0.05f, 0f);
            // Подбирает, как только игрок вошёл. Если игрок стоял на предмете с полным
            // запасом, а потом потратил патроны — подберёт, не сходя с места.
            recheck -= Time.deltaTime;
            bool timed = recheck <= 0f;
            if (timed) recheck = 0.5f;
            var center = transform.position + Vector3.up * 0.6f;
            foreach (var p in Player.All)
            {
                var nearest = p.transform.position + Vector3.up * Mathf.Clamp(center.y - p.transform.position.y, 0.35f, 1.45f);
                bool near = Vector3.Distance(nearest, center) < RADIUS + 0.35f;
                bool entered = near && !inside.Contains(p);
                if (near) inside.Add(p); else inside.Remove(p);
                if ((entered || (near && timed)) && TryGive(p)) return;
            }
        }

        public string LabelText()
        {
            switch (kind)
            {
                case "weapon": return Weapons.DisplayName(weapon);
                case "ammo": return "ПАТРОНЫ: " + Weapons.DisplayName(weapon);
            }
            return "АПТЕЧКА";
        }

        /// Пытается отдать предмет игроку. true — предмет подобран.
        public bool TryGive(Player player)
        {
            if (player.downed) return false;
            bool taken = false;
            switch (kind)
            {
                case "weapon": taken = player.GiveWeapon(weapon, Weapons.Get(weapon).pickupAmmo); break;
                case "ammo": taken = player.GiveAmmo(weapon, Weapons.Get(weapon).pickupAmmo); break;
                case "medkit": taken = player.Heal(MEDKIT_HEAL); break;
            }
            if (taken)
            {
                Sfx.PlayFlat("pickup", -4f);
                player.hud?.ShowMessage("+ " + LabelText(), 1.5f);
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
            return taken;
        }

        void Build()
        {
            model = MeshKit.Node(transform, "Model");
            var light = Mat(new Color(0.95f, 0.95f, 0.95f), 0.6f);
            var dark = Mat(new Color(0.12f, 0.12f, 0.12f), 0.1f);
            switch (kind)
            {
                case "weapon":
                {
                    float length = weapon == "smg" ? 0.6f : weapon == "shotgun" ? 0.9f : weapon == "pistol" ? 0.35f : 0.4f;
                    Block(new Vector3(0.1f, 0.12f, length), Vector3.zero, light);
                    Block(new Vector3(0.08f, 0.2f, 0.1f), new Vector3(0, -0.14f, length * 0.25f), light);
                    break;
                }
                case "ammo":
                    Block(new Vector3(0.4f, 0.25f, 0.28f), Vector3.zero, light);
                    Block(new Vector3(0.42f, 0.06f, 0.3f), Vector3.zero, dark);
                    break;
                case "medkit":
                    Block(new Vector3(0.45f, 0.3f, 0.3f), Vector3.zero, light);
                    Block(new Vector3(0.3f, 0.32f, 0.08f), Vector3.zero, dark);
                    Block(new Vector3(0.08f, 0.32f, 0.3f), Vector3.zero, dark);
                    break;
            }
            MeshKit.Label(transform, LabelText(), new Vector3(0f, 1.2f, 0f), 0.14f);
            // Слабый свет, чтобы предмет было видно в тёмной комнате.
            var glow = MeshKit.PointLight(transform, Vector3.zero, 2f, 0.6f);
            glow.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        }

        void Block(Vector3 size, Vector3 pos, Material mat)
        {
            MeshKit.Add(model, MeshKit.Box(size), mat, pos);
        }

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        static Material Mat(Color color, float emission)
        {
            string key = color.r + "/" + emission;
            if (!mats.TryGetValue(key, out var m))
            {
                m = MeshKit.Lit(color, color * emission);
                mats[key] = m;
            }
            return m;
        }
    }
}
