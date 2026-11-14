using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CarrierGame.Tests
{
    /// <summary>The orbits carrying what is free in space: a ship posted far off, by another
    /// planet, is where it should be when the player gets there in one step (a jump, or a warp's
    /// last step): put back exactly from where it is kept in doubles, and not moved again.</summary>
    public class OrbitalSystemTests : PlayModeFixture
    {
        double savedGM;
        Orbit home, there;

        [SetUp]
        public void SaveGM() => savedGM = OrbitalSystem.StarGM;

        [TearDown]
        public void PutBack()
        {
            OrbitalSystem.StarGM = savedGM;
            // (the runner lifts the frames out of the system, to the top of the scene)
            if (home != null) Object.Destroy(home.gameObject);
            if (there != null) Object.Destroy(there.gameObject);
        }

        static Orbit Planet(Transform sys, string name, Vector3 laidOut, double semiMajor, Vector3 periapsis, Vector3 ahead)
        {
            var go = new GameObject(name);
            go.transform.SetParent(sys, false);
            go.transform.position = laidOut;
            var o = go.AddComponent<Orbit>();
            o.semiMajor = semiMajor;
            o.periapsis = periapsis;
            o.ahead = ahead;
            o.reach = 50000f;
            return o;
        }

        [UnityTest]
        public IEnumerator AShipPostedFarOffIsWhereItShouldBeWhenThePlayerJumpsToIt()
        {
            OrbitalSystem.StarGM = 1e13;       // (slow orbits: a few metres a frame)
            // laid out small, two planets 10,000 km from the star at their real size, a quarter
            // of the way round from each other: the player at one, a ship posted 6 km off the other
            var sys = Make("System");
            home = Planet(sys.transform, "Home (orbit)", new Vector3(30000f, 0f, 0f), 1e7, Vector3.right, Vector3.forward);
            there = Planet(sys.transform, "There (orbit)", new Vector3(0f, 0f, 60000f), 1e7, Vector3.forward, Vector3.left);
            var player = Make("Player", new Vector3(30000f, 0f, 1000f));
            var posted = Make("Posted ship", new Vector3(0f, 0f, 54000f));
            posted.AddComponent<OrbitHome>().home = there.transform;
            var runner = Make("Orbits");
            runner.SetActive(false);
            var os = runner.AddComponent<OrbitalSystem>();
            os.system = sys.transform;
            os.player = player.transform;
            runner.SetActive(true);
            yield return null;
            yield return null;

            Assert.That(Vector3.Distance(player.transform.position, posted.transform.position), Is.GreaterThan(OrbitalSystem.FarLimit),
                        "the posted ship is far off, kept in doubles");
            Assert.That(os.FarCount, Is.GreaterThanOrEqualTo(1), "kept far off");
            Assert.That(Vector3.Distance(posted.transform.position, there.transform.position), Is.EqualTo(6000f).Within(5f),
                        "6 km off its planet");

            // the player jumps to 8 km off it, in one step
            player.transform.position = posted.transform.position + Vector3.right * 8000f;
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(player.transform.position, posted.transform.position), Is.EqualTo(8000f).Within(50f),
                        "the posted ship is still 8 km off the player");
            Assert.That(Vector3.Distance(posted.transform.position, there.transform.position), Is.EqualTo(6000f).Within(50f),
                        "and still 6 km off its planet");
            Assert.That(os.Anchor, Is.EqualTo(there.transform), "the player is carried with the planet it is at");
        }
    }
}
