using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieCoop
{
    /// Общая обстановка: чёрный фон, слабый рассеянный свет и чёрный туман,
    /// в котором тонут дальние углы.
    public static class WorldEnvironment
    {
        /// ambient — сила рассеянного света (как ambient_light_energy в Godot).
        public static void Setup(float ambient, float fogDensity)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            float a = Mathf.LinearToGammaSpace(ambient);
            RenderSettings.ambientLight = new Color(a, a, a);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogColor = Color.black;
            RenderSettings.skybox = null;
            RenderSettings.reflectionIntensity = 0f;
            QualitySettings.pixelLightCount = 8;
            QualitySettings.shadowDistance = 25f;
        }

        static readonly System.Collections.Generic.Dictionary<int, Material> walls = new System.Collections.Generic.Dictionary<int, Material>();

        /// Серый матовый материал стены, пола, мебели (оттенок округлён до 0.05).
        public static Material Shade(float shade)
        {
            int key = Mathf.RoundToInt(shade * 20f);
            if (!walls.TryGetValue(key, out var m))
            {
                float v = key / 20f;
                m = MeshKit.Lit(new Color(v, v, v), Color.black, 0f, 0.05f);
                walls[key] = m;
            }
            return m;
        }

        /// Твёрдый блок мира: стена, пол, потолок, мебель.
        public static GameObject Box(Transform world, Vector3 size, Vector3 pos, float shade, float rotY = 0f)
        {
            var go = new GameObject("Block");
            go.layer = Layers.World;
            go.isStatic = true;
            go.transform.SetParent(world, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, rotY * Mathf.Rad2Deg, 0f);
            go.transform.localScale = size;
            go.AddComponent<BoxCollider>();
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(Vector3.one);
            go.AddComponent<MeshRenderer>().sharedMaterial = Shade(shade);
            return go;
        }

        static Material lampMat;

        /// Плафон под потолком и свет от него.
        public static void Lamp(Transform world, Vector3 lightPos, Vector3 fixturePos, Vector3 fixtureSize, float range, float intensity, bool shadows)
        {
            MeshKit.PointLight(world, lightPos, range, intensity, shadows);
            if (lampMat == null) lampMat = MeshKit.Lit(Color.white, Color.white * 3f);
            var f = MeshKit.Add(world, MeshKit.Box(fixtureSize), lampMat, Vector3.zero, default, null, "Fixture");
            f.position = fixturePos;
            f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
