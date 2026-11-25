using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The sky's close-ups: in each system, pictures of the sky alone (no ships, planets or star in
    /// the way), with the game's bloom and tonemapping, to compare by eye: toward the galaxy's
    /// band, along it, and away from it into the deep sky. "-skytest" (or ?skytest) takes only
    /// these, in whichever system is open (add -system ninehells for the Nine Hells'); the full
    /// self-test and the Nine Hells' take them too.
    /// </summary>
    public partial class SystemTest
    {
        bool skyOnly;

        /// <summary>The sky's pictures, `{prefix}_band`, `_along` and `_deep`, and whether the sky
        /// is the space sky's shader (logged as a check). Returns through `ok`.</summary>
        IEnumerator SkyShots(string prefix, System.Action<bool> ok = null)
        {
            var sky = RenderSettings.skybox;
            bool spaceSky = sky != null && sky.shader != null && sky.shader.name == "Carrier/SpaceSky";
            bool masks = spaceSky && sky.IsKeywordEnabled("_SKY_MASKS");
            Log($"{(spaceSky ? "ok" : "FAIL")}: the sky is {(sky != null ? sky.name : "none")} ({(sky != null && sky.shader != null ? sky.shader.name : "no shader")}), " +
                $"the galaxy's masks {(masks ? "on" : "off")}");
            ok?.Invoke(spaceSky);
            if (!spaceSky) yield break;

            // where the galaxy's band lies: its frame is the sky's turned by the clouds' own (the
            // band is that frame's XZ plane; its slow turn keeps it there)
            var frame = Quaternion.Euler(sky.GetVector("_SkyEuler")) * Quaternion.Euler(sky.GetVector("_CloudEuler"));
            var go = new GameObject("Sky close-up camera");
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            cam.depth = (main != null ? main.depth : 0f) + 10f;
            cam.cullingMask = 0;                 // (the sky alone)
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 1000f;
            cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            if (main != null) go.transform.position = main.transform.position;
            foreach (var (view, dir) in new[] { ("band", frame * Vector3.right), ("along", frame * Vector3.forward), ("deep", frame * Vector3.up) })
            {
                var up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
                go.transform.rotation = Quaternion.LookRotation(dir, up);
                yield return new WaitForSeconds(0.4f);
                Shot($"{prefix}_{view}");
                yield return null;
            }
            Destroy(go);
        }

        /// <summary>-skytest: only the sky's close-ups, in the system that is open.</summary>
        IEnumerator SkyTest()
        {
            bool good = false;
            yield return SkyShots($"sky_{StarSystems.Current.id}", r => good = r);
            Log($"{(good ? "PASS" : "FAIL")}: the sky of {StarSystems.Current.name}");
        }
    }
}
