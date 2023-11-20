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
        public static GameObject debugPanelPrefab;
        public static GameObject serverDirectorPrefab;
        public static GameObject clientDirectorPrefab;
        public static void Load(string path)
        {
            coreResources = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "providirectorcore")); // Apparently it doesn't copy folders????
            hudPrefab = coreResources.LoadAsset<GameObject>("ProvidirectorUIRoot");
            serverDirectorPrefab = coreResources.LoadAsset<GameObject>("ServerDirectorPrefab");
            clientDirectorPrefab = coreResources.LoadAsset<GameObject>("ClientDirectorPrefab");
            barPrefab = coreResources.LoadAsset<GameObject>("BarPrefab");
            whitePixel = coreResources.LoadAsset<Sprite>("whitepixel");
            debugPanelPrefab = coreResources.LoadAsset<GameObject>("DebugInfoPanel");
            icons = AssetBundle.LoadFromFile(System.IO.Path.Combine(path, "monstericons")); // ???
            MonsterIcon.AddIconsFromBundle(icons);
            if (!(coreResources && hudPrefab && serverDirectorPrefab && clientDirectorPrefab && barPrefab && whitePixel && debugPanelPrefab)) Debug.Log("Providirector: Failed to load a resource.");
            else Debug.Log("Providirector: Successfully loaded all resources.");

        }
        public static HealthBarStyle GenerateHBS()
        {
            HealthBarStyle ret = ScriptableObject.CreateInstance<HealthBarStyle>();
            ret.barPrefab = barPrefab;
            ret.flashOnHealthCritical = true;
            ret.barrierBarStyle = new HealthBarStyle.BarStyle() // Barrier
            {
                baseColor = new Color(0.91f, 0.796f, 0.024f, 0.3f),
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.3f,
                sprite = whitePixel
            };
            ret.trailingOverHealthBarStyle = new HealthBarStyle.BarStyle() // Foreground Bar for regular health
            {
                baseColor = new Color(0.216f, 0.788f, 0.212f),
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            ret.trailingUnderHealthBarStyle = new HealthBarStyle.BarStyle() // Background Bar for regular health
            {
                baseColor = new Color(0.51f, 0.161f, 0.031f),
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            ret.lowHealthOverStyle = new HealthBarStyle.BarStyle() // Foreground Bar for low health
            {
                baseColor = new Color(0.769f, 0.259f, 0.071f),
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            ret.lowHealthUnderStyle = new HealthBarStyle.BarStyle() // Background Bar for low health
            {
                baseColor = new Color(0.51f, 0.161f, 0.031f),
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            ret.flashBarStyle = new HealthBarStyle.BarStyle() // Flashing at low health
            {
                baseColor = Color.white,
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            ret.instantHealthBarStyle = new HealthBarStyle.BarStyle() // Recently healed health
            {
                baseColor = new Color(0.569f, 0.941f, 0.561f),
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            ret.cullBarStyle = new HealthBarStyle.BarStyle() // Execute bar, like for Freeze
            {
                baseColor = new Color(1, 1, 1, 0.5f),
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.2f,
                sprite = whitePixel
            };
            ret.curseBarStyle = new HealthBarStyle.BarStyle() // Max Health Reduction
            {
                baseColor = Color.gray,
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            ret.ospStyle = new HealthBarStyle.BarStyle() // Oneshot Protection indicator
            {
                baseColor = Color.white,
                enabled = false,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            ret.shieldBarStyle = new HealthBarStyle.BarStyle() // Shield Bar
            {
                baseColor = new Color(0.384f, 0.624f, 0.902f),
                enabled = true,
                imageType = UnityEngine.UI.Image.Type.Simple,
                sizeDelta = 1.0f,
                sprite = whitePixel
            };
            return ret;
        }
    }
}
