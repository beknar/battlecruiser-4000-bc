using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The kinds of weapon against a ship's layers (DamageTable): the shield, armour
    /// (Armor) and the hull; and how a kind goes over the co-op wire.</summary>
    public class DamageTableTests
    {
        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
            Health.Intercept = null;
        }

        Health Ship(float hull, float shield = 0f, float armourRating = 0f, float plating = 0f)
        {
            var go = new GameObject("ship");
            made.Add(go);
            var h = go.AddComponent<Health>();
            h.maxHealth = hull;
            h.Repair();
            if (armourRating > 0f)
            {
                var a = go.AddComponent<Armor>();
                a.rating = armourRating;
                a.SetPlating(plating);
            }
            if (shield > 0f)
            {
                var s = new GameObject("shield");
                s.transform.SetParent(go.transform);
                s.AddComponent<SphereCollider>();   // (a shield needs its collider)
                var sh = s.AddComponent<Shield>();
                sh.SetCapacity(shield);
                sh.SetOffline(false);
            }
            return h;
        }

        [Test]
        public void EveryKindHasARowAndOldKindsAreAsTheyWere()
        {
            Assert.That(DamageTable.Count, Is.EqualTo(10), "the nine, and the EMP (appended: the old kinds keep their numbers)");
            Assert.That((int)HitKind.Emp, Is.EqualTo(9));
            foreach (HitKind k in System.Enum.GetValues(typeof(HitKind)))
                Assert.That(DamageTable.Label(k), Is.Not.Empty);
            foreach (var k in new[] { HitKind.Projectile, HitKind.Cannon, HitKind.Missile, HitKind.Beam })
            {
                Assert.That(DamageTable.Shield(k), Is.EqualTo(1f));
                Assert.That(DamageTable.Hull(k), Is.EqualTo(1f));
            }
        }

        [Test]
        public void WithNoArmourOrShieldAHitIsAsBefore()
        {
            var h = Ship(1000f);
            h.Damage(100f, Vector3.zero, HitKind.Cannon);
            Assert.That(h.Current, Is.EqualTo(900f).Within(0.01f));
        }

        [Test]
        public void IonIsHardOnShieldsAndLightOnTheHull()
        {
            Assert.That(DamageTable.Shield(HitKind.Ion), Is.GreaterThan(1f));
            Assert.That(DamageTable.Component(HitKind.Ion), Is.GreaterThan(1f));
            var h = Ship(1000f);
            h.Damage(100f, Vector3.zero, HitKind.Ion);
            Assert.That(h.Current, Is.EqualTo(975f).Within(0.01f));
        }

        [Test]
        public void ANullifierStripsAShieldButBarelyScratchesTheHull()
        {
            Assert.That(DamageTable.Shield(HitKind.Nullifier), Is.EqualTo(3f));
            var h = Ship(1000f);
            h.Damage(100f, Vector3.zero, HitKind.Nullifier);
            Assert.That(h.Current, Is.EqualTo(970f).Within(0.01f));
        }

        [Test]
        public void ArmourStopsItsShareAndWearsAway()
        {
            var h = Ship(10000f, 0f, 0.5f, 100f);
            var a = h.GetComponent<Armor>();
            h.Damage(100f, Vector3.zero, HitKind.Projectile);
            Assert.That(h.Current, Is.EqualTo(9950f).Within(0.01f), "whole, it stops half");
            Assert.That(a.Integrity, Is.EqualTo(0.5f).Within(0.001f), "the 50 it stopped wore half of its 100");
            h.Damage(100f, Vector3.zero, HitKind.Projectile);
            Assert.That(h.Current, Is.EqualTo(9950f - 75f).Within(0.01f), "half worn, it stops a quarter");
            h.Repair();
            Assert.That(a.Integrity, Is.EqualTo(1f), "a repair makes it whole");
        }

        [Test]
        public void APenetratorGoesStraightThroughArmour()
        {
            var h = Ship(10000f, 0f, 0.5f, 1000f);
            h.Damage(100f, Vector3.zero, HitKind.Penetrator);
            Assert.That(h.Current, Is.EqualTo(10000f - 120f).Within(0.01f));
            Assert.That(h.GetComponent<Armor>().Integrity, Is.EqualTo(1f), "and doesn't wear it");
        }

        [Test]
        public void ShieldsTakeEachKindAtItsOwnRate()
        {
            var h = Ship(1000f, 600f);
            var sh = h.GetComponentInChildren<Shield>();
            h.Damage(100f, Vector3.zero, HitKind.Nullifier);
            Assert.That(sh.Strength, Is.EqualTo(300f).Within(0.01f), "a nullifier's 100 takes 300 off the shield");
            Assert.That(h.Current, Is.EqualTo(1000f).Within(0.01f));
            h.Damage(200f, Vector3.zero, HitKind.Nullifier);
            // (600 against the shield's 300: 300 of it through, which is 100 of the hit, at 0.3 to the hull)
            Assert.That(h.Current, Is.EqualTo(1000f - 30f).Within(0.01f));
            Assert.That(sh.HitsByKind[(int)HitKind.Nullifier], Is.EqualTo(2), "every kind is counted");
        }

        [Test]
        public void NewKindsTravelInKxAndOldGamesSeeAnOldKind()
        {
            foreach (HitKind k in System.Enum.GetValues(typeof(HitKind)))
            {
                var (kd, kx) = DamageTable.ToWire(k);
                Assert.That(kd, Is.InRange(0, 3), "an old game reads kd as one of the four it knows");
                Assert.That(DamageTable.FromWire(kd, kx), Is.EqualTo(k));
            }
            // from an old game (no kx)
            Assert.That(DamageTable.FromWire((int)HitKind.Missile, 0), Is.EqualTo(HitKind.Missile));
            Assert.That(DamageTable.FromWire(99, 0), Is.EqualTo(HitKind.Cannon));
            Assert.That(DamageTable.FromWire(1, 99), Is.EqualTo(HitKind.Cannon), "a kind from a newer game this one doesn't know");
        }
    }
}
