using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using CarrierGame.Demo;

namespace CarrierGame.Tests
{
    /// <summary>The mouse wheel over a mode's menu (the mining list among them) moves its
    /// selection and doesn't zoom the camera; with the pointer elsewhere it still zooms.</summary>
    public class ModeMenuWheelTests : InputTestFixture
    {
        /// <summary>A menu of a heading and a dozen rows, tagged 0 to 11.</summary>
        sealed class TestList : IMenuSource
        {
            public string Title => "TEST LIST";
            public string Hint(bool pad) => "";
            public void Rows(List<MenuRow> rows)
            {
                rows.Add(new MenuRow { header = true, text = "ROWS" });
                for (int i = 0; i < 12; i++) rows.Add(new MenuRow { text = $"row {i}", tag = i });
            }
            public void Act(MenuRow row) { }
            public bool Keys(Keyboard k, Gamepad pad, MenuRow row) => false;
        }

        Mouse mouse;
        GameObject cam, menu;

        public override void Setup()
        {
            base.Setup();
            mouse = InputSystem.AddDevice<Mouse>();
            mouse.MakeCurrent();
            cam = new GameObject("orbit camera test");
            cam.AddComponent<OrbitCamera>().distance = 1000f;
            menu = new GameObject("mode menu test");
            menu.AddComponent<ModeMenu>();
        }

        public override void TearDown()
        {
            ModeMenu.Close();
            if (menu != null) Object.Destroy(menu);
            if (cam != null) Object.Destroy(cam);
            base.TearDown();
        }

        IEnumerator Wheel(float y)
        {
            Set(mouse.scroll, new Vector2(0f, y));
            yield return null;
            Set(mouse.scroll, Vector2.zero);
            yield return null;
        }

        IEnumerator PointAt(Vector2 gui)
        {
            Set(mouse.position, new Vector2(gui.x, Screen.height - gui.y));
            yield return null;
        }

        static int Chosen => (int)ModeMenu.Instance.Selected.tag;

        [UnityTest]
        public IEnumerator OverTheMenuTheWheelChoosesAndElsewhereItZooms()
        {
            if (Screen.width < 200 || Screen.height < 200) Assert.Ignore($"no screen to put the menu on ({Screen.width} x {Screen.height})");
            var orbit = cam.GetComponent<OrbitCamera>();
            ModeMenu.Open(new TestList());
            Assert.That(ModeMenu.IsOpen, Is.True);
            yield return null;   // (past the frame it opened in)
            var area = ModeMenu.Area;
            Assert.That(area.width, Is.GreaterThan(0f), "the menu has its place on the screen without an OnGUI");
            yield return PointAt(area.center);
            Assert.That(ModeMenu.WheelHeld, Is.True, "the pointer is over the menu");

            int first = Chosen;
            yield return Wheel(-120f);
            int down = Chosen;
            Assert.That(down, Is.GreaterThan(first), "the wheel down chooses a row further down");
            Assert.That(orbit.distance, Is.EqualTo(1000f), "and doesn't zoom the camera");
            yield return Wheel(120f);
            Assert.That(Chosen, Is.LessThan(down), "the wheel up chooses one further up");
            Assert.That(orbit.distance, Is.EqualTo(1000f), "still no zoom");

            // the pointer off the menu (the bottom left corner): the wheel zooms, the choice stays
            yield return PointAt(new Vector2(2f, Screen.height - 2f));
            Assert.That(ModeMenu.WheelHeld, Is.False, "the pointer is off the menu");
            int stays = Chosen;
            yield return Wheel(120f);
            Assert.That(orbit.distance, Is.LessThan(1000f), "elsewhere the wheel zooms the camera, the menu open or not");
            Assert.That(Chosen, Is.EqualTo(stays), "and the menu's choice stays as it was");
        }
    }
}
