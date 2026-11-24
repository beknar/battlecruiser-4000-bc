using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using CarrierGame.Demo;

namespace CarrierGame.Tests
{
    /// <summary>The mouse wheel zooms the orbit camera, but not under the station panel, which
    /// scrolls its list with it.</summary>
    public class CameraWheelTests : InputTestFixture
    {
        Mouse mouse;
        GameObject cam;

        public override void Setup()
        {
            base.Setup();
            mouse = InputSystem.AddDevice<Mouse>();
            mouse.MakeCurrent();
            cam = new GameObject("orbit camera test");
            cam.AddComponent<OrbitCamera>().distance = 1000f;
        }

        public override void TearDown()
        {
            InputFocus.Release(InputFocus.Panel.Industry);
            if (cam != null) Object.Destroy(cam);
            base.TearDown();
        }

        IEnumerator Wheel()
        {
            Set(mouse.scroll, new Vector2(0f, 120f));
            yield return null;
            Set(mouse.scroll, Vector2.zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheWheelZoomsOnlyWhenTheStationPanelIsShut()
        {
            var orbit = cam.GetComponent<OrbitCamera>();
            Assert.That(InputFocus.Take(InputFocus.Panel.Industry), Is.True);
            yield return Wheel();
            Assert.That(orbit.distance, Is.EqualTo(1000f), "no zoom under the station panel");

            InputFocus.Release(InputFocus.Panel.Industry);
            yield return null;
            yield return null;
            yield return Wheel();
            Assert.That(orbit.distance, Is.LessThan(1000f), "the wheel zooms in once the panel is shut");
        }
    }
}
