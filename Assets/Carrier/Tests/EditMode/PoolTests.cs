using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>Shots and explosions kept for reuse (Pool).</summary>
    public class PoolTests
    {
        readonly List<GameObject> made = new List<GameObject>();
        GameObject prefab;

        [SetUp]
        public void Prefab()
        {
            prefab = new GameObject("Bolt");
            prefab.transform.localScale = new Vector3(2f, 2f, 2f);
            made.Add(prefab);
        }

        [TearDown]
        public void Clear()
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
        }

        Transform Take(Vector3 at)
        {
            var t = Pool.Take(prefab.transform, at, Quaternion.identity);
            made.Add(t.gameObject);
            return t;
        }

        [Test]
        public void OnePutByIsTheNextOneTaken()
        {
            var first = Take(Vector3.zero);
            first.localScale *= 5f;                      // a big blast, scaled by whoever set it off
            Pool.Give(first.gameObject);
            Assert.That(first.gameObject.activeSelf, Is.False);

            var again = Take(new Vector3(10f, 0f, 0f));
            Assert.That(again, Is.SameAs(first));
            Assert.That(again.gameObject.activeSelf, Is.True);
            Assert.That(again.position, Is.EqualTo(new Vector3(10f, 0f, 0f)));
            Assert.That(again.localScale, Is.EqualTo(prefab.transform.localScale), "back to the prefab's scale");
        }

        [Test]
        public void WithNonePutByANewOneIsMade()
        {
            var a = Take(Vector3.zero);
            var b = Take(Vector3.zero);
            Assert.That(b, Is.Not.SameAs(a));
        }

        [Test]
        public void OneIsntPutByTwice()
        {
            var a = Take(Vector3.zero);
            Pool.Give(a.gameObject);
            Pool.Give(a.gameObject);
            Assert.That(Take(Vector3.zero), Is.SameAs(a));
            Assert.That(Take(Vector3.zero), Is.Not.SameAs(a), "only one of it was put by");
        }
    }
}
