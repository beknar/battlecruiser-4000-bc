using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>Shared look for the demo's on-screen panels: sizes scale with the screen height.</summary>
    public static class Hud
    {
        /// <summary>The player's +/- text size setting.</summary>
        public static float UserScale = 1f;

        /// <summary>A step bigger (+1) or smaller (-1).</summary>
        public static void ChangeTextSize(int dir) => UserScale = Mathf.Clamp(UserScale + 0.15f * dir, 0.5f, 3f);

        /// <summary>Recording a reel: the controls list is left off the screen, nothing asks the
        /// player anything, and messages run on game time.</summary>
        public static bool ReelMode;
        /// <summary>The reel shows the mining marks too (the features reels).</summary>
        public static bool ReelShowsMarks;

        /// <summary>1 at 900 px tall (never below 1), times the user setting.</summary>
        public static float S => Mathf.Max(1f, Screen.height / 900f) * UserScale;

        public static readonly Color TextColor = new Color(0.85f, 0.93f, 1f);

        static Texture2D panel;
        static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();

        /// <summary>A label style at basePx (at 900 px screen height). Call from OnGUI.</summary>
        public static GUIStyle Text(int basePx, bool bold = false, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            int size = Mathf.Max(8, Mathf.RoundToInt(basePx * S));
            int key = size * 100 + (bold ? 50 : 0) + (int)anchor;
            if (!styles.TryGetValue(key, out var st))
            {
                st = new GUIStyle(GUI.skin.label)
                {
                    fontSize = size,
                    richText = true,
                    wordWrap = false,
                    alignment = anchor,
                    fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };
                st.normal.textColor = TextColor;
                styles[key] = st;
            }
            return st;
        }

        public static void Panel(Rect r)
        {
            if (panel == null)
            {
                panel = new Texture2D(1, 1);
                panel.SetPixel(0, 0, new Color(0.02f, 0.04f, 0.08f, 0.72f));
                panel.Apply();
            }
            GUI.DrawTexture(r, panel);
        }

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Four corner brackets around a rectangle.</summary>
        public static void Corners(Rect r, float len, float th, Color c)
        {
            Fill(new Rect(r.xMin, r.yMin, len, th), c);
            Fill(new Rect(r.xMin, r.yMin, th, len), c);
            Fill(new Rect(r.xMax - len, r.yMin, len, th), c);
            Fill(new Rect(r.xMax - th, r.yMin, th, len), c);
            Fill(new Rect(r.xMin, r.yMax - th, len, th), c);
            Fill(new Rect(r.xMin, r.yMax - len, th, len), c);
            Fill(new Rect(r.xMax - len, r.yMax - th, len, th), c);
            Fill(new Rect(r.xMax - th, r.yMax - len, th, len), c);
        }

        public static void Outline(Rect r, float th, Color c)
        {
            Fill(new Rect(r.xMin, r.yMin, r.width, th), c);
            Fill(new Rect(r.xMin, r.yMax - th, r.width, th), c);
            Fill(new Rect(r.xMin, r.yMin, th, r.height), c);
            Fill(new Rect(r.xMax - th, r.yMin, th, r.height), c);
        }
    }
}
