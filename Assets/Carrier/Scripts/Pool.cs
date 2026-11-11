using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>
    /// Shots and explosions kept for reuse. A big fight fires hundreds of shots a second, each
    /// one living a second or two; making every one new, and the garbage collector clearing up
    /// after them, costs the most in the web build. A finished one is switched off and put by
    /// (Give), and the next one asked for of the same prefab (Take) is that one again, moved into
    /// place and switched back on, at the prefab's scale.
    ///
    /// What is put by belongs to the scene it is in and goes with it when the level changes. A
    /// reused object must start over in OnEnable (Projectile, Explosion), since Awake and Start
    /// run only once.
    /// </summary>
    public static class Pool
    {
        static readonly Dictionary<GameObject, Stack<GameObject>> free = new Dictionary<GameObject, Stack<GameObject>>();

        /// <summary>How many have been made new, and how many reused (for the self-test).</summary>
        public static int Made { get; private set; }
        public static int Reused { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            free.Clear();
            Made = Reused = 0;
            SceneManager.sceneUnloaded -= Forget;
            SceneManager.sceneUnloaded += Forget;
        }

        static void Forget(Scene _) => free.Clear();

        /// <summary>One of `prefab` at a place: a put-by one if there is one, else a new one.</summary>
        public static T Take<T>(T prefab, Vector3 position, Quaternion rotation) where T : Component
        {
            var key = prefab.gameObject;
            if (free.TryGetValue(key, out var stack))
                while (stack.Count > 0)
                {
                    var go = stack.Pop();
                    if (go == null) continue;              // went with its scene
                    var t = go.transform;
                    t.SetPositionAndRotation(position, rotation);
                    t.localScale = key.transform.localScale;
                    go.SetActive(true);
                    Reused++;
                    return go.GetComponent<T>();
                }
            var made = Object.Instantiate(prefab, position, rotation);
            made.gameObject.AddComponent<Pooled>().prefab = key;
            Made++;
            return made;
        }

        /// <summary>Done with: put by for the next Take (or destroyed, if it didn't come from one).</summary>
        public static void Give(GameObject go)
        {
            if (go == null || !go.activeSelf) return;      // already put by
            if (!go.TryGetComponent(out Pooled p) || p.prefab == null) { Object.Destroy(go); return; }
            go.SetActive(false);
            if (!free.TryGetValue(p.prefab, out var stack)) free[p.prefab] = stack = new Stack<GameObject>();
            stack.Push(go);
        }
    }
}
