using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The controls box, laid out to be read at a glance: the modes along the top, then a heading
    /// for each part (the mode's keys, then the keys for every mode), and under it a row for each
    /// key: the key on a key cap, in a column of its own, and what it does beside it (wrapped, if
    /// it is long). It keeps above the console: when one column would be too tall, the parts go
    /// side by side, and if that is still too tall the last rows give way to a line saying so.
    /// </summary>
    public partial class ShipHud
    {
        /// <summary>A row of the controls box: a key and what it does, a heading, or a note.</summary>
        public struct HelpRow
        {
            public string key, what;
            public bool heading, note;

            public HelpRow(string key, string what)
            {
                this.key = key;
                this.what = what;
                heading = note = false;
            }

            public static HelpRow Heading(string text) => new HelpRow { what = text, heading = true };
            public static HelpRow Note(string text) => new HelpRow { what = text, note = true };
        }

        /// <summary>The rows as text, a line each (the modes first).</summary>
        static string Flatten(string bar, List<HelpRow> rows)
        {
            var sb = new System.Text.StringBuilder(bar);
            foreach (var r in rows)
                sb.Append('\n').Append(r.heading || r.note ? r.what : $"{r.key}  {r.what}");
            return sb.ToString();
        }

        static readonly Color CapFill = new Color(0.16f, 0.22f, 0.30f, 0.95f), CapEdge = new Color(0.42f, 0.52f, 0.62f, 0.9f),
                              KeyColour = new Color(1f, 0.86f, 0.55f), HeadColour = new Color(1f, 0.75f, 0.44f),
                              RuleColour = new Color(0.33f, 0.38f, 0.43f, 0.9f);

        const string MoreKeys = "more keys: - makes the text smaller, H hides this";

        GUIStyle keyStyle, whatStyle, headStyle, noteStyle;
        float stylesFor = -1f;

        void HelpStyles(float s)
        {
            if (Mathf.Approximately(s, stylesFor) && keyStyle != null) return;
            stylesFor = s;
            keyStyle = new GUIStyle(Hud.Text(14, true, TextAnchor.MiddleCenter)) { wordWrap = false };
            keyStyle.normal.textColor = KeyColour;
            whatStyle = new GUIStyle(Hud.Text(15)) { wordWrap = true };
            headStyle = new GUIStyle(Hud.Text(14, true)) { wordWrap = true };
            headStyle.normal.textColor = HeadColour;
            noteStyle = new GUIStyle(whatStyle) { fontStyle = FontStyle.Italic };
        }

        // the layout, made again only when the rows, the scale, the screen or the room change
        struct Placed
        {
            public int row;
            public float top;
            public Rect cap, what;
        }

        readonly List<Placed> placed = new List<Placed>();
        List<HelpRow> laidRows;
        string laidBar;
        float laidS, laidRoom;
        int laidW, laidH;
        Vector2 laidSize;
        Rect laidBarRect;
        bool laidCut;

        /// <summary>How the controls box was laid out last: its columns, and whether rows had to
        /// give way (for the self-test).</summary>
        public int HelpColumns { get; private set; } = 1;
        public bool HelpCut => laidCut;

        Rect DrawHelp(List<HelpRow> rows, string bar, float s)
        {
            HelpStyles(s);
            float pad = 12f * s;
            // (room down to the console, at the bottom left)
            var console = ShipConsole.Rect;
            float room = (console.height > 0f ? console.yMin : Screen.height) - pad - 10f * s;
            if (!ReferenceEquals(rows, laidRows) || bar != laidBar || !Mathf.Approximately(s, laidS) || Mathf.Abs(room - laidRoom) > 1f
                || Screen.width != laidW || Screen.height != laidH)
                LayOut(rows, bar, s, room);

            var box = new Rect(pad, pad, laidSize.x, laidSize.y);
            Hud.Panel(box);
            GUI.Label(new Rect(box.x + laidBarRect.x, box.y + laidBarRect.y, laidBarRect.width, laidBarRect.height), bar, Hud.Text(17));
            foreach (var p in placed)
            {
                var r = p.row >= 0 ? rows[p.row] : HelpRow.Note(MoreKeys);
                var what = new Rect(box.x + p.what.x, box.y + p.what.y, p.what.width, p.what.height);
                if (r.heading)
                {
                    GUI.Label(what, r.what, headStyle);
                    Hud.Fill(new Rect(what.x, what.yMax - 2f * s, what.width, Mathf.Max(1f, s)), RuleColour);
                    continue;
                }
                if (r.note) { GUI.Label(what, r.what, noteStyle); continue; }
                var cap = new Rect(box.x + p.cap.x, box.y + p.cap.y, p.cap.width, p.cap.height);
                Hud.Fill(cap, CapFill);
                Hud.Outline(cap, Mathf.Max(1f, s), CapEdge);
                GUI.Label(cap, r.key, keyStyle);
                GUI.Label(what, r.what, whatStyle);
            }
            return box;
        }

        void LayOut(List<HelpRow> rows, string bar, float s, float room)
        {
            laidRows = rows;
            laidBar = bar;
            laidS = s;
            laidRoom = room;
            laidW = Screen.width;
            laidH = Screen.height;
            placed.Clear();
            laidCut = false;

            float pad = 10f * s, gap = 10f * s, colGap = 24f * s, capPad = 6f * s, rowGap = 2f * s, headGap = 6f * s;
            Vector2 barSize = Hud.Text(17).CalcSize(new GUIContent(bar));
            laidBarRect = new Rect(pad, pad, barSize.x, barSize.y);
            float top = pad + barSize.y + 6f * s;

            // the key column: as wide as the widest key cap; what they do: as wide as the widest,
            // up to a limit (a long one wraps)
            float keyW = 0f, whatW = 0f;
            foreach (var r in rows)
            {
                if (r.heading) continue;
                if (!r.note) keyW = Mathf.Max(keyW, keyStyle.CalcSize(new GUIContent(r.key)).x + capPad * 2f);
                whatW = Mathf.Max(whatW, whatStyle.CalcSize(new GUIContent(r.what)).x + 2f);
            }
            whatW = Mathf.Min(whatW, Mathf.Min(300f * s, Screen.width * 0.22f));
            // (two columns no wider than the screen: what they do narrower, wrapped, if need be)
            float most = (Screen.width - pad * 4f - colGap) * 0.5f - keyW - gap;
            if (most > 60f * s) whatW = Mathf.Min(whatW, most);
            float colW = keyW + gap + whatW;
            float capH = keyStyle.CalcSize(new GUIContent("Ag")).y + 2f * s;

            float Height(HelpRow r, bool first) =>
                r.heading ? (first ? 0f : headGap) + headStyle.CalcHeight(new GUIContent(r.what), colW) + 4f * s
                : r.note ? noteStyle.CalcHeight(new GUIContent(r.what), colW) + rowGap
                : Mathf.Max(capH, whatStyle.CalcHeight(new GUIContent(r.what), whatW)) + rowGap;

            // the rows flow down a column and on into the next (two at most, so it doesn't cover
            // the top of the screen) when it is full; a heading never ends a column on its own
            const int MostColumns = 2;
            int columns = 1;
            float x = pad, y = top, tallest = 0f;
            bool colEmpty = true;
            void NewColumn()
            {
                tallest = Mathf.Max(tallest, y - top);
                columns++;
                x += colW + colGap;
                y = top;
                colEmpty = true;
            }
            for (int i = 0; i < rows.Count && !laidCut; i++)
            {
                var r = rows[i];
                float h = Height(r, colEmpty);
                bool lastFits = y + h + pad <= room;
                // (a heading needs its first row with it)
                if (r.heading && i + 1 < rows.Count) lastFits = y + h + Height(rows[i + 1], false) + pad <= room;
                // (a column's first row goes in whatever its height: too tall for the room, it is
                // still shown, but the box is said to be cut)
                if (!lastFits && colEmpty && columns >= MostColumns) laidCut = true;
                if (!lastFits && !colEmpty)
                {
                    if (columns < MostColumns) { NewColumn(); h = Height(r, true); }
                    else
                    {
                        // (no more room: the rest give way to a line saying so, rows taken back till it fits)
                        float nh = noteStyle.CalcHeight(new GUIContent(MoreKeys), colW) + rowGap;
                        while (placed.Count > 0 && placed[placed.Count - 1].what.x >= x && y + nh + pad > room)
                        {
                            y = placed[placed.Count - 1].top;
                            placed.RemoveAt(placed.Count - 1);
                        }
                        // (and a heading left with nothing under it)
                        while (placed.Count > 0 && placed[placed.Count - 1].what.x >= x && placed[placed.Count - 1].row >= 0
                               && rows[placed[placed.Count - 1].row].heading)
                        {
                            y = placed[placed.Count - 1].top;
                            placed.RemoveAt(placed.Count - 1);
                        }
                        placed.Add(new Placed { row = -1, top = y, what = new Rect(x, y, colW, nh - rowGap) });
                        y += nh;
                        laidCut = true;
                        break;
                    }
                }
                float lead = r.heading && !colEmpty ? headGap : 0f;
                if (r.heading || r.note)
                    placed.Add(new Placed { row = i, top = y, what = new Rect(x, y + lead, colW, h - lead - (r.heading ? 0f : rowGap)) });
                else
                {
                    float capW = keyStyle.CalcSize(new GUIContent(r.key)).x + capPad * 2f;
                    float wh = h - rowGap;
                    placed.Add(new Placed { row = i, top = y, cap = new Rect(x, y, capW, capH),
                                            what = new Rect(x + keyW + gap, y + Mathf.Max(0f, (capH - whatStyle.CalcHeight(new GUIContent(r.what), whatW)) * 0.5f), whatW, wh) });
                }
                y += h;
                colEmpty = false;
            }
            tallest = Mathf.Max(tallest, y - top);
            HelpColumns = columns;
            float width = Mathf.Max(barSize.x, columns * colW + (columns - 1) * colGap) + pad * 2f;
            laidSize = new Vector2(width, top + tallest + pad);
        }
    }
}
