using System;
using System.Collections.Generic;
using UnityEngine;

namespace AtOShufflePeek
{
    public class OverlayBehaviour : MonoBehaviour
    {
        private const float Padding = 8f;
        private const float LineSpacing = 2f;
        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0.7f);

        private CardLayout layout;
        private MonoBehaviour manager;
        private IList<CardItem> cards;
        private ICollection<int> faceUp;
        private bool allFaceUp;
        private bool? useUnicodeMarkers;
        private bool errorLogged;

        private readonly List<string> lines = new List<string>();
        private readonly List<Vector2> sizes = new List<Vector2>();

        public bool Visible { get; private set; }

        /// <param name="cards">The manager's live cards list; each layout slot re-reads its index every frame.</param>
        /// <param name="faceUp">The pairs game's live list of face-up indices, or null.</param>
        public void Show(MonoBehaviour manager, IList<CardItem> cards, ICollection<int> faceUp, CardLayout layout)
        {
            this.manager = manager;
            this.cards = cards;
            this.faceUp = faceUp;
            allFaceUp = false;
            errorLogged = false;
            this.layout = layout != null && !layout.IsEmpty ? layout : null;
            Visible = this.layout != null;
        }

        public void Hide()
        {
            Visible = false;
            layout = null;
            manager = null;
            cards = null;
            faceUp = null;
            allFaceUp = false;
        }

        /// <summary>Marks every card face up once the given manager has revealed them all at the end of its game.</summary>
        public void RevealAll(MonoBehaviour manager)
        {
            if (layout != null && manager != null && manager == this.manager)
                allFaceUp = true;
        }

        private void Update()
        {
            try
            {
                // Unity's overloaded == reports destroyed objects as null.
                if (layout == null)
                    return;
                if (manager == null)
                {
                    Hide();
                    return;
                }

                if (Plugin.ToggleKey.Value.IsDown())
                    Visible = !Visible;
                if (Visible)
                    layout = layout.Refresh(Reread);
            }
            catch (Exception e)
            {
                Fail("Update", e);
            }
        }

        private void OnGUI()
        {
            try
            {
                if (!Plugin.Enabled.Value || !Visible || layout == null)
                    return;

                var style = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = Plugin.FontSize.Value,
                    wordWrap = false
                };

                bool unicode = DetectGlyphs(style);

                lines.Clear();
                sizes.Clear();
                float width = 0f;
                float height = 0f;
                foreach (var row in layout.Rows)
                {
                    string line = CardLayout.FormatRichRow(row, unicode);
                    Vector2 size = style.CalcSize(new GUIContent(line));
                    lines.Add(line);
                    sizes.Add(size);
                    width = Mathf.Max(width, size.x);
                    height += size.y + LineSpacing;
                }
                if (lines.Count > 0)
                    height -= LineSpacing;

                float x = Plugin.PositionX.Value;
                float y = Plugin.PositionY.Value;

                Color previous = GUI.color;
                GUI.color = BackgroundColor;
                GUI.DrawTexture(new Rect(x, y, width + Padding * 2f, height + Padding * 2f), Texture2D.whiteTexture);
                GUI.color = previous;

                float lineY = y + Padding;
                for (int i = 0; i < lines.Count; i++)
                {
                    GUI.Label(new Rect(x + Padding, lineY, sizes[i].x, sizes[i].y), lines[i], style);
                    lineY += sizes[i].y + LineSpacing;
                }
            }
            catch (Exception e)
            {
                Fail("OnGUI", e);
            }
        }

        private bool DetectGlyphs(GUIStyle style)
        {
            if (useUnicodeMarkers.HasValue)
                return useUnicodeMarkers.Value;

            bool supported = false;
            Font font = style.font != null ? style.font : GUI.skin.font;
            if (font != null)
            {
                font.RequestCharactersInTexture("✓✗", style.fontSize);
                supported = font.HasCharacter('✓') && font.HasCharacter('✗');
            }

            useUnicodeMarkers = supported;
            Plugin.Log.LogInfo(supported
                ? "Overlay markers: Unicode (✓/✗)"
                : "Overlay markers: ASCII (+/x), font lacks ✓/✗");
            return supported;
        }

        // While a swap is in flight the same card can briefly sit at two indices; it settles when the move ends.
        private CardSlot Reread(CardSlot slot)
        {
            CardItem card = cards[slot.SourceIndex];
            return card != null ? Patches.ReadSlot(card, slot.X, slot.Y, slot.SourceIndex, faceUp, allFaceUp) : slot;
        }

        private void Fail(string where, Exception e)
        {
            if (!errorLogged)
            {
                errorLogged = true;
                Plugin.Log.LogError($"Overlay {where} failed, hiding overlay: {e}");
            }
            Hide();
        }
    }
}
