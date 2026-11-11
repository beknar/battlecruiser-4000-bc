using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Puts the chosen frigate (FrigateChoice) into the level in place of the one the scene was
    /// built with (the starting frigate), before anything else in the level wakes up.
    ///
    /// The new ship goes exactly where the old one was. What the level added to the old ship (the
    /// warp drive in the star system) is added to the new one with the same settings. Then every
    /// reference in the level to the old ship, or to anything on it, is pointed at the same thing
    /// on the new ship (the controls, the camera, the map, landing, docking, the ambush, the
    /// orbits...), and the old ship goes without ever having woken.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class PlayerFrigate : MonoBehaviour
    {
        public CarrierController ship;

        /// <summary>The class the level's ship is now.</summary>
        public static string Flying { get; private set; } = "";

        void Awake()
        {
            if (ship == null) return;
            Flying = FrigateClass.Of(ship);
            var roster = FrigateRoster.Get();
            var entry = roster != null ? roster.Find(FrigateChoice.Current) : null;
            if (entry == null || entry.prefab == null || string.Equals(entry.id, Flying, StringComparison.OrdinalIgnoreCase)) return;
            var swapped = Swap(ship, entry.prefab);
            if (swapped == null) return;
            ship = swapped;
            Flying = entry.id;
            Debug.Log($"[PlayerFrigate] flying the {entry.displayName}");
        }

        /// <summary>Swap the frigate `id` in where the player's ship is now, while the level runs
        /// (for the frigates reel: a player's choice starts the level again). Returns the ship.</summary>
        public static CarrierController SwapTo(string id)
        {
            var pf = FindAnyObjectByType<PlayerFrigate>();
            var roster = FrigateRoster.Get();
            var entry = roster != null ? roster.Find(id) : null;
            if (pf == null || pf.ship == null || entry == null || entry.prefab == null) return pf != null ? pf.ship : null;
            var cc = Swap(pf.ship, entry.prefab);
            pf.ship = cc;
            Flying = entry.id;
            foreach (var d in FindObjectsByType<Demo.CarrierDemoInput>(FindObjectsSortMode.None)) d.ShipChanged();
            Debug.Log($"[PlayerFrigate] now flying the {entry.displayName}");
            return cc;
        }

        /// <summary>Replace a ship with one made from a prefab; returns the new one.</summary>
        public static CarrierController Swap(CarrierController old, GameObject prefab)
        {
            var oldGo = old.gameObject;
            oldGo.SetActive(false);   // none of it wakes

            // made under an inactive holder, so it doesn't wake before it has the level's fittings
            // (into the level's own scene: while a level is loading, it may not be the active one yet)
            var holder = new GameObject("frigate (being made)");
            holder.SetActive(false);
            if (oldGo.scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, oldGo.scene);
            var go = Instantiate(prefab, oldGo.transform.position, oldGo.transform.rotation, holder.transform);
            go.name = oldGo.name;
            go.layer = oldGo.layer;
            foreach (var c in oldGo.GetComponents<MonoBehaviour>())
            {
                if (c == null || go.GetComponent(c.GetType()) != null) continue;
                var added = (MonoBehaviour)go.AddComponent(c.GetType());
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(c), added);
            }
            var cc = go.GetComponent<CarrierController>();
            Repoint(oldGo, go);
            go.transform.SetParent(oldGo.transform.parent, true);
            if (go.transform.parent == null && oldGo.scene.IsValid() && go.scene != oldGo.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, oldGo.scene);
            Destroy(holder);
            Destroy(oldGo);
            return cc;
        }

        // ------------------------------------------------------------ references

        /// <summary>Point every reference in the level to the old ship (or anything on it) at the
        /// same thing on the new one: the object at the same place in its hierarchy, or failing
        /// that the first of its kind.</summary>
        static void Repoint(GameObject oldRoot, GameObject newRoot)
        {
            var map = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            UnityEngine.Object Counterpart(UnityEngine.Object o)
            {
                if (map.TryGetValue(o, out var m)) return m;
                Transform t = o is GameObject g ? g.transform : o is Component c ? c.transform : null;
                if (t == null || !(t == oldRoot.transform || t.IsChildOf(oldRoot.transform))) return map[o] = null;
                string path = PathFrom(oldRoot.transform, t);
                var nt = path.Length == 0 ? newRoot.transform : newRoot.transform.Find(path);
                UnityEngine.Object result;
                if (o is GameObject) result = nt != null ? nt.gameObject : newRoot;
                else
                {
                    var type = o.GetType();
                    result = nt != null ? nt.GetComponent(type) : null;
                    if (result == null) result = newRoot.GetComponentInChildren(type, true);
                }
                return map[o] = result;
            }

            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (b == null || b.transform.IsChildOf(oldRoot.transform) || b.transform.IsChildOf(newRoot.transform)) continue;
                for (var type = b.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                    foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsInitOnly) continue;
                        // (also a field of an interface type holding a component: a missile's or a
                        // gun's note of how its target moves, IVelocity)
                        if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType) || f.FieldType.IsInterface)
                        {
                            var v = f.GetValue(b) as UnityEngine.Object;
                            if (v == null) continue;
                            var n = Counterpart(v);
                            if (n != null && f.FieldType.IsInstanceOfType(n)) f.SetValue(b, n);
                        }
                        else if (f.FieldType.IsArray && typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType.GetElementType()))
                        {
                            if (!(f.GetValue(b) is Array arr)) continue;
                            for (int i = 0; i < arr.Length; i++)
                            {
                                var v = arr.GetValue(i) as UnityEngine.Object;
                                if (v == null) continue;
                                var n = Counterpart(v);
                                if (n != null && f.FieldType.GetElementType().IsInstanceOfType(n)) arr.SetValue(n, i);
                            }
                        }
                    }
            }
        }

        static string PathFrom(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }
    }
}
