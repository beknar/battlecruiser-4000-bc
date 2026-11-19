using System.IO;
using CarrierGame;
using UnityEditor;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The fires on wrecks and on settlements struck from orbit, from the Asset Store pack
    /// "Realistic Fire VFX" (Hivemind): its URP version's VFX Graph fires, copied into the ignored
    /// Assets/Carrier/Kit/FireFX by Tools/stage_firefx.py (licensed: never in the repo). A clone
    /// without it gets no FireKit, and nothing burns.
    ///
    /// Each fire is made into the game's own prefab (Kit/FireFX/Game): a FireFx holding the pack's
    /// effect (without its scripts, which weren't copied: FireFx does their work), the blaze with
    /// a flickering light of its own, and its distance cut-off out of the way. The FireKit holding them goes in Kit/FireFX/Resources, where
    /// the game finds it.
    /// </summary>
    public static partial class CarrierBuilder
    {
        const string FirePack = Root + "/Kit/FireFX/Prefabs";
        const string FireOut = Root + "/Kit/FireFX/Game";
        const string FireKitPath = Root + "/Kit/FireFX/Resources/FireKit.asset";

        /// <summary>The game's version of one of the pack's fires, or null without the pack.</summary>
        static FireFx FireFxOf(string name, string packPath, float lightIntensity, float lightRange, float linger, float smokeDensity = 1f)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{FirePack}/{packPath}");
            if (src == null) return null;
            var root = new GameObject(name);
            var fx = root.AddComponent<FireFx>();
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var l in inst.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject);
            // (the pack kills its particles farther than MaxDistance from the camera, in the effect's
            // own units: a few hundred metres at the game's sizes. FireFx stops drawing it instead.)
            foreach (var v in inst.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true))
            {
                var so = new SerializedObject(v);
                var floats = so.FindProperty("m_PropertySheet.m_Float.m_Array");
                for (int i = 0; i < floats.arraySize; i++)
                {
                    var e = floats.GetArrayElementAtIndex(i);
                    string n = e.FindPropertyRelative("m_Name").stringValue;
                    var value = e.FindPropertyRelative("m_Value");
                    if (n == "MaxDistance") value.floatValue = 1e6f;
                    // (thicker smoke where asked: the pack's is a thin haze, lost at the game's distances)
                    else if (n == "Smoke_Density") value.floatValue *= smokeDensity;
                    else continue;
                    e.FindPropertyRelative("m_Overridden").boolValue = true;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (lightIntensity > 0f)
            {
                var lgo = new GameObject("Light");
                lgo.transform.SetParent(root.transform, false);
                lgo.transform.localPosition = Vector3.up * 2f;
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.55f, 0.2f);
                l.intensity = lightIntensity;
                l.range = lightRange;
                l.shadows = LightShadows.None;
            }
            fx.lightIntensity = lightIntensity;
            fx.lightRange = lightRange;
            fx.linger = linger;
            Directory.CreateDirectory(FireOut);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{FireOut}/{name}.prefab").GetComponent<FireFx>();
            Object.DestroyImmediate(root);
            Log($"fire fx {name}: from {packPath}");
            return saved;
        }

        /// <summary>Make the fires and the FireKit the game finds them by (none without the pack).</summary>
        [MenuItem("Tools/Carrier/Rebuild Fire FX")]
        public static void BuildFireKit()
        {
            var blaze = FireFxOf("FireFx_Blaze", "LargeScaleFire/VFX_LargeScaleFire01.prefab", 6f, 14f, 8f);
            if (blaze == null)
            {
                // (no pack: no kit, so nothing burns, rather than a kit of nothing)
                if (AssetDatabase.LoadAssetAtPath<FireKit>(FireKitPath) != null) AssetDatabase.DeleteAsset(FireKitPath);
                Log("fire fx: no Realistic Fire VFX in Kit/FireFX (Tools/stage_firefx.py): no fires");
                return;
            }
            var kit = AssetDatabase.LoadAssetAtPath<FireKit>(FireKitPath);
            if (kit == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FireKitPath));
                kit = ScriptableObject.CreateInstance<FireKit>();
                AssetDatabase.CreateAsset(kit, FireKitPath);
            }
            kit.blaze = blaze;
            kit.groundFire = FireFxOf("FireFx_GroundFire", "GroundFire/VFX_GroundFire_Med.prefab", 0f, 0f, 6f);
            kit.smoulder = FireFxOf("FireFx_Smoulder", "Aftermath/VFX_AftermathMed.prefab", 0f, 0f, 10f, 3f);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
        }
    }
}
