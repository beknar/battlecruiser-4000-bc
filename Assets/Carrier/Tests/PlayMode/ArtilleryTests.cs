using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>The Maw's artillery and nuclear missiles (ArtilleryShell, ArtilleryGun, NukeMissile,
    /// NukeLauncher, MawTactics) and the siege's strike on a city (VraskBombardment, as far as it
    /// goes without a planet): a shell's flight time and the point it is aimed at, its landing
    /// there (and missing a ship that moved), the blast falling off with distance and sparing the
    /// side that fired it, the round chosen for each target, a nuclear blast in space hurting a
    /// whole frigate without destroying it, a nuclear strike's reach covering a whole city and
    /// its ruins lasting longer than a kinetic strike's, the silos' stock, and the spotting rule.</summary>
    public class ArtilleryTests : PlayModeFixture
    {
        /// <summary>A ship: a hull, a box to be hit, and (optionally) a shield, armour, a side.</summary>
        Health Ship(string name, Vector3 at, float hull = 6000f, float shield = 0f, bool armour = false, bool hostile = false, float size = 60f)
        {
            var go = Make(name, at);
            go.SetActive(false);
            var h = go.AddComponent<Health>();
            h.maxHealth = hull;
            h.respawnDelay = 60f;
            go.AddComponent<BoxCollider>().size = Vector3.one * size;
            if (shield > 0f)
            {
                var sg = new GameObject("Shield");
                sg.transform.SetParent(go.transform, false);
                sg.AddComponent<SphereCollider>().radius = size;   // (a shield needs its collider)
                sg.AddComponent<Shield>().capacity = shield;
            }
            if (armour)
            {
                var a = go.AddComponent<Armor>();
                a.rating = 0.4f;
                a.plating = 1400f;
            }
            if (hostile)
            {
                var info = go.AddComponent<TargetInfo>();
                info.faction = "Hostile";
                info.group = VraskShip.Faction;
            }
            go.SetActive(true);
            return h;
        }

        /// <summary>A shell to fire (inactive, as a prefab would be kept).</summary>
        ArtilleryShell ShellPrefab(float speed = 2000f, float damage = 700f, float radius = 500f)
        {
            var go = Make("ShellPrefab");
            go.SetActive(false);
            var s = go.AddComponent<ArtilleryShell>();
            s.speed = speed;
            s.damage = damage;
            s.blastRadius = radius;
            s.edge = 0.2f;
            s.maxFlight = 40f;
            return s;
        }

        NukeMissile NukePrefab()
        {
            var go = Make("NukePrefab");
            go.SetActive(false);
            var m = go.AddComponent<NukeMissile>();
            m.damage = 3000f;
            m.blastRadius = 1200f;
            m.edge = 0.3f;
            m.maxSpeed = 320f;
            return m;
        }

        [Test]
        public void TheShellIsAimedWhereTheTargetWillBe()
        {
            Vector3 from = Vector3.zero, at = new Vector3(0f, 0f, 10000f), v = new Vector3(120f, 0f, 0f);
            Vector3 aim = ArtilleryGun.Predict(from, at, v, 550f, out float flight);
            Assert.That(flight, Is.GreaterThan(15f), "10 km at 550 m/s: many seconds");
            Assert.That(Vector3.Distance(aim, at + v * flight), Is.LessThan(5f), "where the target will be after the flight");
            Assert.That(Vector3.Distance(from, aim) / 550f, Is.EqualTo(flight).Within(0.01f), "the flight is the distance to that point at the shell's speed");
            Assert.That(ArtilleryGun.Predict(from, at, Vector3.zero, 550f, out float still), Is.EqualTo(at), "a still target: where it is");
            Assert.That(still, Is.EqualTo(10000f / 550f).Within(0.01f));
        }

        [Test]
        public void AShellTakesItsTimeToArrive()
        {
            var prefab = ShellPrefab(speed: 500f);
            var s = ArtilleryShell.Launch(prefab, Vector3.zero, new Vector3(0f, 0f, 6000f), HitKind.Artillery, null);
            Assert.That(s, Is.Not.Null);
            Assert.That(s.gameObject.activeSelf, Is.True);
            Assert.That(s.Flight, Is.EqualTo(12f).Within(0.01f), "6 km at 500 m/s");
            Assert.That(s.Aim, Is.EqualTo(new Vector3(0f, 0f, 6000f)));
            Object.Destroy(s.gameObject);
        }

        [UnityTest]
        public IEnumerator AShellLandsWhereItWasAimedAndCanBeDodged()
        {
            var prefab = ShellPrefab(speed: 4000f, damage: 600f, radius: 400f);
            var sitting = Ship("Sitting", new Vector3(0f, 0f, 2000f));
            var dodger = Ship("Dodger", new Vector3(3000f, 0f, 2000f));
            yield return null;
            var a = ArtilleryShell.Launch(prefab, Vector3.zero, sitting.transform.position, HitKind.Artillery, null);
            var b = ArtilleryShell.Launch(prefab, Vector3.zero, dodger.transform.position, HitKind.Artillery, null);
            yield return null;
            // the dodger turns away as the shell flies
            dodger.transform.position += new Vector3(0f, 1500f, 0f);
            float until = Time.time + 5f;
            while (Time.time < until && (a != null || b != null)) yield return null;
            Assert.That(sitting.Current, Is.LessThan(sitting.maxHealth), "the ship that stayed put is hit");
            Assert.That(dodger.Current, Is.EqualTo(dodger.maxHealth), "the one that moved isn't");
        }

        [Test]
        public void TheBlastFallsOffWithDistance()
        {
            Assert.That(ArtilleryShell.Falloff(0f, 500f, 0.2f), Is.EqualTo(1f));
            Assert.That(ArtilleryShell.Falloff(500f / 6f, 500f, 0.2f), Is.EqualTo(1f).Within(1e-4f), "all of it near the centre");
            Assert.That(ArtilleryShell.Falloff(500f, 500f, 0.2f), Is.EqualTo(0.2f).Within(1e-4f), "the edge's share at the edge");
            float mid = ArtilleryShell.Falloff(300f, 500f, 0.2f);
            Assert.That(mid, Is.InRange(0.2f, 1f));
            Assert.That(ArtilleryShell.Falloff(400f, 500f, 0.2f), Is.LessThan(mid), "less further out");
            Assert.That(ArtilleryShell.Falloff(501f, 500f, 0.2f), Is.EqualTo(0f), "nothing beyond");

            var near = Ship("Near", new Vector3(30f, 0f, 0f), size: 20f);
            var far = Ship("Far", new Vector3(380f, 0f, 0f), size: 20f);
            var out_ = Ship("Out", new Vector3(900f, 0f, 0f), size: 20f);
            var own = Ship("Own side", new Vector3(0f, 40f, 0f), hostile: true, size: 20f);
            var owner = Ship("Firer", new Vector3(0f, 0f, -5000f), hostile: true, size: 20f);
            var r = ArtilleryShell.Blast(Vector3.zero, 500f, 600f, 0.2f, HitKind.Artillery, owner.transform);
            Assert.That(r.Hit(near) && r.Hit(far), Is.True, "every ship in the blast");
            Assert.That(r.Hit(out_), Is.False, "none outside it");
            Assert.That(r.Hit(own), Is.False, "the side that fired it is spared");
            float lostNear = near.maxHealth - near.Current, lostFar = far.maxHealth - far.Current;
            Assert.That(lostNear, Is.EqualTo(600f).Within(1f), "full damage near the centre");
            Assert.That(lostFar, Is.GreaterThan(0f).And.LessThan(lostNear), "less at the edge");
            Assert.That(out_.Current, Is.EqualTo(out_.maxHealth));
            Assert.That(own.Current, Is.EqualTo(own.maxHealth));
            Assert.That(near.LastHitKind, Is.EqualTo(HitKind.Artillery));
        }

        [Test]
        public void TheRoundSuitsTheTarget()
        {
            var shielded = Ship("Shielded", Vector3.zero, shield: 2000f);
            Assert.That(ArtilleryGun.ChooseRound(shielded.transform), Is.EqualTo(ArtilleryGun.Round.Nullifier), "a shield up: the nullifier");
            shielded.GetComponentInChildren<Shield>().SetOffline(true);
            Assert.That(ArtilleryGun.ChooseRound(shielded.transform), Is.EqualTo(ArtilleryGun.Round.Penetrator), "its shield down: the penetrator");
            var armoured = Ship("Armoured", new Vector3(500f, 0f, 0f), armour: true);
            Assert.That(ArtilleryGun.ChooseRound(armoured.transform), Is.EqualTo(ArtilleryGun.Round.Penetrator), "armour: the penetrator");
            var bare = Ship("Bare", new Vector3(1000f, 0f, 0f));
            Assert.That(ArtilleryGun.ChooseRound(bare.transform), Is.EqualTo(ArtilleryGun.Round.HighExplosive), "nothing to beat: high explosive");
            Assert.That(ArtilleryGun.KindOf(ArtilleryGun.Round.Nullifier), Is.EqualTo(HitKind.Nullifier));
            Assert.That(ArtilleryGun.KindOf(ArtilleryGun.Round.Penetrator), Is.EqualTo(HitKind.Penetrator));
            Assert.That(ArtilleryGun.KindOf(ArtilleryGun.Round.HighExplosive), Is.EqualTo(HitKind.Artillery));
            // (the nullifier strips a shield three times as hard; the penetrator goes through armour)
            Assert.That(DamageTable.Shield(HitKind.Nullifier), Is.GreaterThan(DamageTable.Shield(HitKind.Penetrator)));
            Assert.That(DamageTable.Pierce(HitKind.Penetrator), Is.GreaterThan(DamageTable.Pierce(HitKind.Nullifier)));
        }

        [Test]
        public void ANuclearBlastInSpaceHurtsAWholeFrigateButDoesntDestroyIt()
        {
            // the weakest frigate whole (the Lancer: 4,500 hull, 5,000 shield) at ground zero
            var lancer = Ship("Lancer", Vector3.zero, hull: 4500f, shield: 5000f, armour: true);
            var shield = lancer.GetComponentInChildren<Shield>();
            var r = ArtilleryShell.Blast(Vector3.zero, 1200f, 3000f, 0.3f, HitKind.Nuclear, null);
            Assert.That(r.Hit(lancer), Is.True);
            Assert.That(lancer.Dead, Is.False, "not destroyed");
            Assert.That(shield.Strength, Is.LessThan(shield.capacity), "but hurt");
            // and with its shield already down: still not destroyed
            var bare = Ship("Lancer, shield down", new Vector3(5000f, 0f, 0f), hull: 4500f, shield: 5000f, armour: true);
            bare.GetComponentInChildren<Shield>().SetOffline(true);
            ArtilleryShell.Blast(bare.transform.position, 1200f, 3000f, 0.3f, HitKind.Nuclear, null);
            Assert.That(bare.Dead, Is.False);
            Assert.That(bare.Current, Is.LessThan(bare.maxHealth), "its hull takes it");
            // a whole fleet bunched within 1.2 km is all hit
            var other = Ship("Wingman", new Vector3(0f, 900f, 0f), hull: 4500f);
            var r2 = ArtilleryShell.Blast(Vector3.zero, 1200f, 3000f, 0.3f, HitKind.Nuclear, null);
            Assert.That(r2.Hit(other), Is.True, "it reaches 1.2 km");
        }

        [Test]
        public void ANuclearStrikeCoversAWholeCityAndItsRuinsLastLonger()
        {
            // a city's layout: its buildings round its pad, the farthest 850 m out
            var layout = Make("City");
            void Building(Vector3 at) { var b = new GameObject("Tower"); b.transform.SetParent(layout.transform, false); b.transform.localPosition = at; }
            var pad = new GameObject("Outpost");
            pad.transform.SetParent(layout.transform, false);
            pad.transform.localPosition = new Vector3(3000f, 0f, 0f);       // (the pad isn't a building)
            Building(new Vector3(700f, 0f, 320f));
            Building(new Vector3(220f, 0f, -820f));
            Building(new Vector3(-430f, 0f, 0f));
            float reach = Demo.OrbitalStrikes.SettlementReach(layout);
            Assert.That(reach, Is.EqualTo(new Vector2(220f, -820f).magnitude).Within(1f), "the farthest building, not the pad");
            var siege = Make("Siege").AddComponent<Demo.VraskBombardment>();
            Assert.That(reach + siege.nukeMargin, Is.GreaterThan(reach + 100f), "a nuclear blast reaches past it");
            Assert.That(siege.nukeRebuildMinutes, Is.GreaterThan(Demo.OrbitalStrikes.BuildingRebuildMinutes * 2f),
                        "its ruins last far longer than a kinetic strike's hour");
            Assert.That(siege.nukeCasualtiesPerBuilding, Is.GreaterThan(Demo.OrbitalStrikes.CasualtiesPerBuilding));
            Assert.That(siege.nukePopulationShare, Is.GreaterThan(0f), "and its people are killed");
            // a ruin is remembered as long as it was struck for
            long key = WorldState.BuildingKey("Test City", 3);
            var mem = WorldState.Body("Test Body");
            mem.destroyed.Add(key, siege.nukeRebuildMinutes);
            Assert.That(mem.destroyed.RebuiltAt(key) - WorldState.Clock, Is.EqualTo(siege.nukeRebuildMinutes * 60.0).Within(1e-3));
            Assert.That(mem.destroyed.Contains(key), Is.True);
        }

        [Test]
        public void OnlyAWorldWithAirIsBesieged()
        {
            var planet = Make("Planet").AddComponent<WarpPoint>();
            planet.kind = WarpPoint.Kind.Planet;
            planet.bodyRadius = 240000f;
            var moon = Make("Moon").AddComponent<WarpPoint>();
            moon.kind = WarpPoint.Kind.Moon;
            moon.bodyRadius = 70000f;
            Assert.That(Demo.VraskBombardment.HasAir(planet), Is.True);
            Assert.That(Demo.VraskBombardment.HasAir(moon), Is.False);
            Assert.That(Demo.VraskBombardment.HasAir(null), Is.False);
        }

        [UnityTest]
        public IEnumerator TheSilosHoldTwoAndAreFilledOnlyOnRespawn()
        {
            var maw = Ship("Maw", Vector3.zero, hull: 3000f, hostile: true);
            var target = Ship("Target", new Vector3(0f, 0f, 20000f));
            var silos = maw.gameObject.AddComponent<NukeLauncher>();
            silos.nukePrefab = NukePrefab();
            silos.capacity = 2;
            silos.Restock();
            yield return null;
            Assert.That(silos.Stock, Is.EqualTo(2));
            var a = silos.LaunchNow(target.transform);
            var b = silos.LaunchNow(target.transform);
            var c = silos.LaunchNow(target.transform);
            Assert.That(a != null && b != null, Is.True, "two go");
            Assert.That(c, Is.Null, "the third doesn't: there are no more");
            Assert.That(silos.Stock, Is.EqualTo(0));
            Assert.That(silos.Launched, Is.EqualTo(2));
            Assert.That(silos.Launch(target.transform), Is.Null);
            if (a != null) Object.Destroy(a.gameObject);
            if (b != null) Object.Destroy(b.gameObject);
            // destroyed and back: full again
            maw.Damage(1e6f);
            Assert.That(maw.Dead, Is.True);
            Assert.That(silos.Stock, Is.EqualTo(0), "not while it is destroyed");
            maw.RespawnNow();
            Assert.That(silos.Stock, Is.EqualTo(2), "refilled when it comes back");
        }

        [UnityTest]
        public IEnumerator PointDefenceCanShootANukeDown()
        {
            var maw = Ship("Maw", Vector3.zero, hostile: true);
            var target = Ship("Target", new Vector3(0f, 0f, 9000f), hull: 4500f, shield: 5000f);
            var nuke = NukeLauncher.Fire(NukePrefab(), new Vector3(0f, 0f, 100f), Quaternion.identity, maw.transform, target.transform);
            yield return null;
            Assert.That(nuke.Hostile, Is.True, "a Vrask nuke is fair game for the CIWS");
            int det0 = NukeMissile.Detonations;
            nuke.Shoot(nuke.toughness + 1f, target.transform);
            yield return null;
            Assert.That(nuke == null, Is.True, "shot down");
            Assert.That(NukeMissile.Detonations, Is.EqualTo(det0), "no nuclear blast");
            Assert.That(target.Current, Is.EqualTo(target.maxHealth));
        }

        [Test]
        public void TheMawFiresOnlyAtWhatAnotherShipHasSpotted()
        {
            var target = Make("Target", new Vector3(0f, 0f, 0f)).transform;
            var mawGo = Make("Maw", new Vector3(0f, 0f, -10000f));
            var maw = mawGo.AddComponent<VraskShip>();
            maw.role = VraskShip.Role.Maw;
            Assert.That(MawTactics.IsSpotted(target, maw, 6000f, out _), Is.False, "on its own it has no firing solution, 10 km off");
            var skinkGo = Make("Skink", new Vector3(0f, 0f, -7000f));
            var skink = skinkGo.AddComponent<VraskShip>();
            skink.role = VraskShip.Role.Skink;
            Assert.That(MawTactics.IsSpotted(target, maw, 6000f, out _), Is.False, "a Skink 7 km off can't see it either");
            skinkGo.transform.position = new Vector3(0f, 0f, -5000f);
            Assert.That(MawTactics.IsSpotted(target, maw, 6000f, out var by), Is.True, "within its 6 km it can");
            Assert.That(by, Is.EqualTo(skink));
            // two Maws keep one beat
            Assert.That(MawTactics.SalvoTick(16f), Is.EqualTo(Mathf.FloorToInt(Time.time / 16f)));
        }
    }
}
