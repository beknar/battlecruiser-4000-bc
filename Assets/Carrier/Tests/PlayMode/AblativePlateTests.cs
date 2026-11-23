using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>A hull's armour plates (AblativePlates): on the hull, and only its own, even on a
    /// ship copied from one whose plates were already on (as a band's extra ships are).</summary>
    public class AblativePlateTests
    {
        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in made) if (go != null) Object.Destroy(go);
            made.Clear();
        }

        GameObject Hull()
        {
            var go = new GameObject("plated ship");
            made.Add(go);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(20f, 10f, 80f);
            go.AddComponent<Armor>().SetPlating(1000f);
            go.AddComponent<AblativePlates>().count = 8;
            return go;
        }

        static List<Transform> Plates(GameObject go) =>
            go.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith(AblativePlates.PlateName)).ToList();

        [UnityTest]
        public IEnumerator ACopiedShipWearsOnlyItsOwnPlatesOnItsHull()
        {
            var first = Hull();
            yield return null;
            Assert.That(Plates(first).Count, Is.EqualTo(8), "the first ship's plates");
            var copy = Object.Instantiate(first);
            made.Add(copy);
            yield return null;
            yield return null;
            var plates = Plates(copy);
            Assert.That(plates.Count, Is.EqualTo(8), "the copy's own plates, not the first ship's as well");
            Assert.That(copy.GetComponent<AblativePlates>().Attached, Is.EqualTo(8));
            foreach (var p in plates)
            {
                var local = copy.transform.InverseTransformPoint(p.position);
                Assert.That(Mathf.Abs(local.x), Is.LessThanOrEqualTo(10.5f), $"{p.name} on the hull's side, not stood off it");
                Assert.That(Mathf.Abs(local.y), Is.LessThanOrEqualTo(5.5f), $"{p.name} on the hull's top or belly, not stood off it");
            }
        }

        [UnityTest]
        public IEnumerator PlatesWaitForTheHullToBeWhole()
        {
            // a hull of parts (as a ship that breaks up is), off to one side as the plates go on
            var go = new GameObject("chunked ship");
            made.Add(go);
            var chunks = new GameObject("Chunks").transform;
            chunks.SetParent(go.transform, false);
            for (int k = 0; k < 3; k++)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(part.GetComponent<Collider>());
                part.name = $"Chunk{k}";
                part.transform.SetParent(chunks, false);
                part.transform.localPosition = new Vector3(0f, 0f, (k - 1) * 20f);
                part.transform.localScale = Vector3.one * 20f;
            }
            chunks.localPosition = new Vector3(100f, 0f, 0f);
            go.AddComponent<Armor>().SetPlating(1000f);
            go.AddComponent<AblativePlates>().count = 9;
            yield return null;
            Assert.That(Plates(go).Count, Is.EqualTo(0), "no plates round a hull that is out of place");
            // back in place: the plates go on (tried again every quarter second)
            chunks.localPosition = Vector3.zero;
            yield return new WaitForSeconds(0.6f);
            var plates = Plates(go);
            Assert.That(plates.Count, Is.EqualTo(9));
            foreach (var p in plates)
            {
                var local = go.transform.InverseTransformPoint(p.position);
                Assert.That(new Vector2(local.x, local.y).magnitude, Is.LessThanOrEqualTo(16f), $"{p.name} on the hull, not where the hull was");
            }
        }
    }
}
