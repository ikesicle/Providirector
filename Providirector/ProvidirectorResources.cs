using ProvidirectorGame;
using RoR2.UI;
using UnityEngine;
namespace Providirector
{
    static internal class ProvidirectorResources
    {
        public static AssetBundle coreResources;
        public static AssetBundle icons;
        public static Sprite whitePixel;
        public static GameObject hudPrefab;
        public static GameObject barPrefab;
        public static GameObject serverDirectorPrefab;
        public static GameObject clientDirectorPrefab;
        public static void Load(string path)
        {
            coreResources = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "providirectorcore"));
            hudPrefab = coreResources.LoadAsset<GameObject>("ProvidirectorUIRoot");
            serverDirectorPrefab = coreResources.LoadAsset<GameObject>("ServerDirectorPrefab");
            clientDirectorPrefab = coreResources.LoadAsset<GameObject>("ClientDirectorPrefab");
            barPrefab = coreResources.LoadAsset<GameObject>("BarPrefab");
            whitePixel = coreResources.LoadAsset<Sprite>("whitepixel");
            icons = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "monstericons"));
            MonsterIcon.AddIconsFromBundle(icons);
            if (!(coreResources && hudPrefab && serverDirectorPrefab && clientDirectorPrefab && barPrefab && whitePixel)) Debug.Log("Providirector: Failed to load a resource.");
            else Debug.Log("Providirector: Successfully loaded all resources.");

        }
        public static HealthBarStyle GenerateHBS()
        {
            return new HealthBarStyle()
            {
                barPrefab = barPrefab,
                flashOnHealthCritical = true,
                barrierBarStyle = new HealthBarStyle.BarStyle() // Barrier
                {
                    baseColor = new Color(0.91f, 0.796f, 0.024f, 0.3f),
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.3f,
                    sprite = whitePixel
                },
                trailingOverHealthBarStyle = new HealthBarStyle.BarStyle() // Foreground Bar for regular health
                {
                    baseColor = new Color(0.216f, 0.788f, 0.212f),
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                trailingUnderHealthBarStyle = new HealthBarStyle.BarStyle() // Background Bar for regular health
                {
                    baseColor = new Color(0.51f, 0.161f, 0.031f),
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                lowHealthOverStyle = new HealthBarStyle.BarStyle() // Foreground Bar for low health
                {
                    baseColor = new Color(0.769f, 0.259f, 0.071f),
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                lowHealthUnderStyle = new HealthBarStyle.BarStyle() // Background Bar for low health
                {
                    baseColor = new Color(0.51f, 0.161f, 0.031f),
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                flashBarStyle = new HealthBarStyle.BarStyle() // Flashing at low health
                {
                    baseColor = Color.white,
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                instantHealthBarStyle = new HealthBarStyle.BarStyle() // Recently healed health
                {
                    baseColor = new Color(0.569f, 0.941f, 0.561f),
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                cullBarStyle = new HealthBarStyle.BarStyle() // Execute bar, like for Freeze
                {
                    baseColor = new Color(1, 1, 1, 0.5f),
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                curseBarStyle = new HealthBarStyle.BarStyle() // Max Health Reduction
                {
                    baseColor = Color.gray,
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                ospStyle = new HealthBarStyle.BarStyle() // Oneshot Protection indicator
                {
                    baseColor = Color.white,
                    enabled = false,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                },
                shieldBarStyle = new HealthBarStyle.BarStyle() // Shield Bar
                {
                    baseColor = new Color(0.384f, 0.624f, 0.902f),
                    enabled = true,
                    imageType = UnityEngine.UI.Image.Type.Simple,
                    sizeDelta = 1.0f,
                    sprite = whitePixel
                }
            };
        }
    }
}
