using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The guns' muzzle flashes and impacts, from the Asset Store pack "Realistic Gun VFX" (Hivemind),
    /// its URP version's effects copied into the ignored Assets/Carrier/Kit/GunFX (licensed: never
    /// in the repo). A clone without it gets none, and its guns show the bolts' own glow and the
    /// small explosion as before.
    ///
    /// Each effect is made into the game's own prefab beside it (Kit/GunFX/Game): a GunFx (pooled,
    /// played once) holding the pack's effect, sized for a 1 m calibre (a round's fxScale is its
    /// bolt's width), its particles scaled with it, played once, without gravity (most fights are in
    /// space), without collisions or the pack's decals (cheaper, and nothing for them to land on),
    /// and its lights (on the turrets' flash only) brought to that size.
    /// </summary>
    public static partial class CarrierBuilder
    {
        const string GunFxPack = Root + "/Kit/GunFX/Art/Prefabs";
        const string GunFxOut = Root + "/Kit/GunFX/Game";
        // (how many times the pack's own size, per metre of calibre: its flashes are a rifle's, and a
        // frigate's barrels are metres across)
        const float MuzzlePerMetre = 40f, ImpactPerMetre = 35f;

        static readonly Dictionary<string, GunFx> gunFx = new Dictionary<string, GunFx>();

        /// <summary>The game's version of one of the pack's effects (made once a build), or null
        /// without the pack.</summary>
        static GunFx GunFxOf(string name, string packPath, float perMetre, bool lights)
        {
            if (gunFx.TryGetValue(name, out var made) && made != null) return made;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{GunFxPack}/{packPath}");
            if (src == null) return null;
            var root = new GameObject(name);
            var fx = root.AddComponent<GunFx>();
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale = Vector3.one * perMetre;

            // (the pack's decals: projected onto whatever is behind, which a ship's hull isn't made for)
            foreach (var b in inst.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b != null && b.GetType().Name.Contains("Decal")).ToArray())
                if (b != null) Object.DestroyImmediate(b.gameObject);
            float life = 0.2f;
            foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = false;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.gravityModifier = 0f;
                var col = ps.collision;
                col.enabled = false;
                var lm = ps.lights;
                lm.enabled = false;
                var sub = ps.subEmitters;
                // (sub-emitters spawned by collisions have nothing to spawn them now)
                for (int i = sub.subEmittersCount - 1; i >= 0; i--)
                    if (sub.GetSubEmitterType(i) == ParticleSystemSubEmitterType.Collision) sub.RemoveSubEmitter(i);
                life = Mathf.Max(life, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            }
            foreach (var l in inst.GetComponentsInChildren<Light>(true).ToArray())
            {
                if (!lights)
                {
                    // (URP's extra light data goes first: it requires the light)
                    foreach (var extra in l.GetComponents<MonoBehaviour>()) Object.DestroyImmediate(extra);
                    Object.DestroyImmediate(l);
                    continue;
                }
                l.range *= perMetre;
                l.shadows = LightShadows.None;
            }
            fx.life = life;
            Directory.CreateDirectory(GunFxOut);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{GunFxOut}/{name}.prefab").GetComponent<GunFx>();
            Object.DestroyImmediate(root);
            gunFx[name] = saved;
            Log($"gun fx {name}: from {packPath}, {life:F1} s");
            return saved;
        }

        /// <summary>Make the gun effects again and give them to the rounds already built, without
        /// building everything (to try a new size or a different effect from the pack).</summary>
        [MenuItem("Tools/Carrier/Rebuild Gun FX")]
        public static void RebuildGunFx()
        {
            gunFx.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
            {
                var round = AssetDatabase.LoadAssetAtPath<Projectile>(AssetDatabase.GUIDToAssetPath(guid));
                if (round == null) continue;
                string kind = round.name == "CiwsBolt" ? "ciws" : round.name == "EnemyCannonShell" ? "cannon"
                            : round.name.StartsWith("FrigateBolt") ? "turret" : null;
                if (kind != null) GiveGunFx(round, kind, round.fxScale);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("gun fx rebuilt: " + string.Join(", ", gunFx.Keys));
        }

        /// <summary>A round's flash and impacts, if the pack is there: a turret's (heavy), the CIWS's
        /// (light and quick) or an enemy cannon's.</summary>
        static void GiveGunFx(Projectile round, string kind, float calibre)
        {
            if (round == null) return;
            switch (kind)
            {
                case "turret":
                    round.muzzleFx = GunFxOf("GunFx_MuzzleTurret", "MuzzleFlashes/Sniper/NS_Muzzle_Sniper_01.prefab", MuzzlePerMetre, true);
                    round.impactFx = GunFxOf("GunFx_ImpactMetal", "ImpactFX/Metal/NS_Impact_Metal_Med.prefab", ImpactPerMetre, false);
                    round.impactRockFx = GunFxOf("GunFx_ImpactRock", "ImpactFX/Rock/NS_Impact_Rock_Med.prefab", ImpactPerMetre, false);
                    break;
                case "ciws":
                    round.muzzleFx = GunFxOf("GunFx_MuzzleCiws", "MuzzleFlashes/SMG/NS_Muzzle_SMG_01.prefab", MuzzlePerMetre, false);
                    round.impactFx = GunFxOf("GunFx_ImpactMetalLight", "ImpactFX/Metal/NS_Impact_Metal_Low.prefab", ImpactPerMetre, false);
                    round.impactRockFx = GunFxOf("GunFx_ImpactRockLight", "ImpactFX/Rock/NS_Impact_Rock_Low.prefab", ImpactPerMetre, false);
                    break;
                case "cannon":
                    round.muzzleFx = GunFxOf("GunFx_MuzzleCannon", "MuzzleFlashes/AR/NS_Muzzle_AR_01.prefab", MuzzlePerMetre, false);
                    round.impactFx = GunFxOf("GunFx_ImpactMetal", "ImpactFX/Metal/NS_Impact_Metal_Med.prefab", ImpactPerMetre, false);
                    round.impactRockFx = GunFxOf("GunFx_ImpactRock", "ImpactFX/Rock/NS_Impact_Rock_Med.prefab", ImpactPerMetre, false);
                    break;
            }
            round.fxScale = calibre;
            EditorUtility.SetDirty(round);
        }
    }
}
