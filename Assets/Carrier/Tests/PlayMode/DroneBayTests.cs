using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>A drone bay (DroneBay): drones launched in a fight, a few at a time and no more
    /// than its limit out of its stock; called home, each back is stock again; one destroyed is
    /// lost for good; each is told to co-op as made during play.</summary>
    public class DroneBayTests : PlayModeFixture
    {
        Transform player;
        int spawned, gone;

        [SetUp]
        public void Player()
        {
            player = Make("Player ship").transform;
            player.position = new Vector3(0f, 0f, 5000f);
            PlayerShips.Add(player);
            spawned = gone = 0;
            CoopSpawn.OnSpawned = (go, key) => spawned++;
            CoopSpawn.OnGone = go => gone++;
        }

        [TearDown]
        public void NoPlayer()
        {
            PlayerShips.Remove(player);
            CoopSpawn.OnSpawned = null;
            CoopSpawn.OnGone = null;
        }

        DroneBay Bay(int stock = 4, int maxOut = 3)
        {
            var go = Make("Carrier ship");
            var b = go.AddComponent<DroneBay>();
            b.stock = stock;
            b.maxOut = maxOut;
            b.perLaunch = 2;
            b.launchInterval = 0.05f;
            return b;
        }

        [UnityTest]
        public IEnumerator ItLaunchesAFewAtATimeUpToItsLimit()
        {
            Assume.That(CoopSpawnables.Find("EnemyDrone"), Is.Not.Null, "the drone prefab (Resources/CoopSpawnables)");
            var b = Bay();
            yield return null;
            Assert.That(b.Out.Count, Is.EqualTo(2), "two at a launch");
            yield return new WaitForSeconds(0.2f);
            Assert.That(b.Out.Count, Is.EqualTo(3), "no more than three out");
            Assert.That(b.Stock, Is.EqualTo(1));
            Assert.That(b.Launched, Is.EqualTo(3));
            Assert.That(spawned, Is.EqualTo(3), "each told to co-op");
            foreach (var d in b.Out)
            {
                Assert.That(d.style, Is.EqualTo(CombatPilot.Style.Fighter));
                Assert.That(d.GetComponent<TargetInfo>().IsSmallCraft, Is.True, "small craft: the players' CIWS goes for it");
                Assert.That(d.GetComponent<Health>().respawnDelay, Is.EqualTo(0f), "lost for good when destroyed");
            }
        }

        [UnityTest]
        public IEnumerator CalledHomeEachBackIsStockAgain()
        {
            Assume.That(CoopSpawnables.Find("EnemyDrone"), Is.Not.Null);
            var b = Bay();
            yield return new WaitForSeconds(0.2f);
            int out0 = b.Out.Count;
            b.recall = true;
            float until = Time.time + 10f;
            while (b.Out.Count > 0 && Time.time < until) yield return null;
            Assert.That(b.Out, Is.Empty, "all home");
            Assert.That(b.Recovered, Is.EqualTo(out0));
            Assert.That(b.Stock, Is.EqualTo(4), "the stock full again");
            Assert.That(gone, Is.EqualTo(out0), "each going told to co-op");
        }

        [UnityTest]
        public IEnumerator OneDestroyedIsLostForGood()
        {
            Assume.That(CoopSpawnables.Find("EnemyDrone"), Is.Not.Null);
            var b = Bay(stock: 2, maxOut: 2);
            yield return null;
            Assert.That(b.Out.Count, Is.EqualTo(2));
            var h = b.Out[0].GetComponent<Health>();
            h.Damage(1e9f, h.transform.position, HitKind.Cannon);
            yield return null;
            yield return null;
            Assert.That(b.Lost, Is.EqualTo(1));
            Assert.That(b.Out.Count, Is.EqualTo(1));
            b.recall = true;
            float until = Time.time + 10f;
            while (b.Out.Count > 0 && Time.time < until) yield return null;
            Assert.That(b.Stock, Is.EqualTo(1), "one back: the lost one isn't");
        }

        [UnityTest]
        public IEnumerator OutOfTheFightItLaunchesNone()
        {
            var b = Bay();
            player.position = new Vector3(0f, 0f, 20000f);   // (beyond its 9 km)
            yield return null;
            yield return null;
            Assert.That(b.Out, Is.Empty);
            Assert.That(b.Stock, Is.EqualTo(4));
        }
    }
}
