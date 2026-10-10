using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AtOShufflePeek.Tests
{
    public class LayoutTests
    {
        private static CardSlot Slot(float x, float y, string name, int type = 0, int index = 0, bool revealed = false, string fallback = null)
            => new CardSlot(x, y, name, type, revealed, index, fallback);

        [Fact]
        public void SingleRow_InShuffledIndexOrder_IsSortedByX()
        {
            var slots = new[]
            {
                Slot(3f, 0f, "C", index: 0),
                Slot(-3f, 0f, "A", index: 1),
                Slot(6f, 0f, "D", index: 2),
                Slot(0f, 0f, "B", index: 3),
            };

            var layout = CardLayout.Build(slots);

            Assert.Single(layout.Rows);
            Assert.Equal(new[] { "A", "B", "C", "D" }, layout.Rows[0].Select(s => s.Name));
            Assert.Equal(new[] { 1, 2, 3, 4 }, layout.Rows[0].Select(s => s.Number));
            Assert.Equal(new[] { 1, 3, 0, 2 }, layout.Rows[0].Select(s => s.SourceIndex));
        }

        [Fact]
        public void PairsGrid_WithFirstIndicesOnBottomRow_IsThreeRowsTopFirst()
        {
            // indices 0-3 on the bottom row (y = -2), 4-7 middle (y = 0), 8-11 top (y = 2); x deliberately reversed
            var slots = new List<CardSlot>();
            float[] ys = { -2f, 0f, 2f };
            int index = 0;
            foreach (float y in ys)
                for (int col = 3; col >= 0; col--)
                {
                    slots.Add(Slot(col * 2f, y, "card" + index, index: index));
                    index++;
                }

            var layout = CardLayout.Build(slots);

            Assert.Equal(3, layout.Rows.Count);
            Assert.Equal(12, layout.Count);
            Assert.All(layout.Rows[0], s => Assert.Equal(2f, s.Y));
            Assert.All(layout.Rows[2], s => Assert.Equal(-2f, s.Y));
            Assert.Equal(Enumerable.Range(1, 12), layout.Rows.SelectMany(r => r).Select(s => s.Number));
            Assert.Equal(new[] { 11, 10, 9, 8 }, layout.Rows[0].Select(s => s.SourceIndex));
            Assert.Equal(new[] { 3, 2, 1, 0 }, layout.Rows[2].Select(s => s.SourceIndex));
            Assert.All(layout.Rows, r => Assert.True(r.Zip(r.Skip(1), (a, b) => a.X < b.X).All(ok => ok)));
        }

        [Fact]
        public void SmallYJitter_StaysInOneRow()
        {
            var slots = new[]
            {
                Slot(0f, 1.0f, "A"),
                Slot(1f, 1.2f, "B"),
                Slot(2f, 0.8f, "C"),
                Slot(3f, 1.1f, "D"),
            };

            var layout = CardLayout.Build(slots, rowTolerance: 0.5f);

            Assert.Single(layout.Rows);
            Assert.Equal(new[] { "A", "B", "C", "D" }, layout.Rows[0].Select(s => s.Name));
        }

        [Fact]
        public void YGapLargerThanTolerance_SplitsRows()
        {
            var slots = new[]
            {
                Slot(0f, 0f, "Low"),
                Slot(0f, 0.6f, "High"),
            };

            var layout = CardLayout.Build(slots, rowTolerance: 0.5f);

            Assert.Equal(2, layout.Rows.Count);
            Assert.Equal("High", layout.Rows[0][0].Name);
            Assert.Equal("Low", layout.Rows[1][0].Name);
        }

        [Fact]
        public void YGapEqualToTolerance_StaysInOneRow()
        {
            var layout = CardLayout.Build(new[] { Slot(0f, 0f, "Low"), Slot(1f, 0.5f, "High") }, rowTolerance: 0.5f);

            Assert.Single(layout.Rows);
        }

        [Fact]
        public void SlowDrift_IsMeasuredFromFirstCardOfRow()
        {
            // neighbouring gaps are 0.3, but the third card is 0.6 below the row's first card
            var layout = CardLayout.Build(new[] { Slot(0f, 1f, "A"), Slot(1f, 0.7f, "B"), Slot(2f, 0.4f, "C") }, rowTolerance: 0.5f);

            Assert.Equal(2, layout.Rows.Count);
            Assert.Equal(new[] { "A", "B" }, layout.Rows[0].Select(s => s.Name));
            Assert.Equal("C", layout.Rows[1][0].Name);
        }

        [Theory]
        [InlineData(18, CardKind.Injury)]
        [InlineData(21, CardKind.Boon)]
        [InlineData(0, CardKind.Normal)]
        [InlineData(19, CardKind.Normal)]
        public void Classify_MapsCardTypes(int type, CardKind expected)
        {
            Assert.Equal(expected, CardLayout.Classify(type));
            Assert.Equal(expected, Slot(0f, 0f, "X", type).Kind);
        }

        [Theory]
        [InlineData(CardKind.Injury, true, "✗")]
        [InlineData(CardKind.Boon, true, "✓")]
        [InlineData(CardKind.Normal, true, "")]
        [InlineData(CardKind.Injury, false, "x")]
        [InlineData(CardKind.Boon, false, "+")]
        [InlineData(CardKind.Normal, false, "")]
        public void MarkerFor_ReturnsExpectedMarker(CardKind kind, bool unicode, string expected)
        {
            Assert.Equal(expected, CardLayout.MarkerFor(kind, unicode));
        }

        [Fact]
        public void FormatRow_Unicode_UsesCheckAndCross()
        {
            var layout = CardLayout.Build(new[]
            {
                Slot(0f, 0f, "Wound", 18),
                Slot(1f, 0f, "Blessing", 21),
                Slot(2f, 0f, "Strike"),
            });

            Assert.Equal("1 Wound ✗ · 2 Blessing ✓ · 3 Strike", CardLayout.FormatRow(layout.Rows[0], useUnicodeMarkers: true));
        }

        [Fact]
        public void FormatRow_Ascii_UsesXAndPlus()
        {
            var layout = CardLayout.Build(new[]
            {
                Slot(0f, 0f, "Wound", 18),
                Slot(1f, 0f, "Blessing", 21),
                Slot(2f, 0f, "Strike"),
            });

            Assert.Equal("1 Wound x · 2 Blessing + · 3 Strike", CardLayout.FormatRow(layout.Rows[0], useUnicodeMarkers: false));
        }

        [Fact]
        public void FormatRow_SingleNormalCard_HasNoMarkerOrSeparator()
        {
            var layout = CardLayout.Build(new[] { Slot(0f, 0f, "Strike") });

            Assert.Equal("1 Strike", CardLayout.FormatRow(layout.Rows[0], true));
            Assert.Equal("1 Strike", CardLayout.FormatRow(layout.Rows[0], false));
        }

        [Fact]
        public void FallbackName_UsedWhenNameMissing()
        {
            var layout = CardLayout.Build(new[]
            {
                Slot(0f, 0f, null, fallback: "CardItem(Clone)"),
                Slot(1f, 0f, "", fallback: "Other"),
                Slot(2f, 0f, "Real", fallback: "Ignored"),
                Slot(3f, 0f, null),
            });

            Assert.Equal(new[] { "CardItem(Clone)", "Other", "Real", "?" }, layout.Rows[0].Select(s => s.DisplayName));
            Assert.Equal("1 CardItem(Clone) · 2 Other · 3 Real · 4 ?", CardLayout.FormatRow(layout.Rows[0], false));
        }

        [Fact]
        public void EmptyInput_YieldsEmptyLayout()
        {
            var layout = CardLayout.Build(new CardSlot[0]);

            Assert.True(layout.IsEmpty);
            Assert.Empty(layout.Rows);
            Assert.Equal(0, layout.Count);
            Assert.True(CardLayout.Build(null).IsEmpty);
        }

        [Theory]
        [InlineData(CardKind.Injury, false, CardLayout.InjuryColor)]
        [InlineData(CardKind.Boon, false, CardLayout.BoonColor)]
        [InlineData(CardKind.Normal, false, CardLayout.NormalColor)]
        [InlineData(CardKind.Injury, true, CardLayout.RevealedColor)]
        [InlineData(CardKind.Boon, true, CardLayout.RevealedColor)]
        [InlineData(CardKind.Normal, true, CardLayout.RevealedColor)]
        public void ColorFor_RevealedIsGrey_OtherwiseByKind(CardKind kind, bool revealed, string expected)
        {
            Assert.Equal(expected, CardLayout.ColorFor(kind, revealed));
        }

        [Fact]
        public void FormatRichRow_WrapsEntriesInColorTags()
        {
            var layout = CardLayout.Build(new[]
            {
                Slot(0f, 0f, "Wound", 18),
                Slot(1f, 0f, "Blessing", 21),
                Slot(2f, 0f, "Strike"),
            });

            Assert.Equal(
                "<color=#FF5252>1 Wound ✗</color> · <color=#69F0AE>2 Blessing ✓</color> · <color=#FFFFFF>3 Strike</color>",
                CardLayout.FormatRichRow(layout.Rows[0], useUnicodeMarkers: true));
            Assert.Equal(
                "<color=#FF5252>1 Wound x</color> · <color=#69F0AE>2 Blessing +</color> · <color=#FFFFFF>3 Strike</color>",
                CardLayout.FormatRichRow(layout.Rows[0], useUnicodeMarkers: false));
        }

        [Fact]
        public void FormatRichRow_RevealedEntryIsGrey()
        {
            var layout = CardLayout.Build(new[]
            {
                new CardSlot(0f, 0f, "Wound", 18, revealed: false, sourceIndex: 0),
                new CardSlot(1f, 0f, "Strike", 0, revealed: true, sourceIndex: 1),
            });

            Assert.Equal(
                "<color=#FF5252>1 Wound x</color> · <color=#9E9E9E>2 Strike</color>",
                CardLayout.FormatRichRow(layout.Rows[0], false));
        }

        [Fact]
        public void Refresh_ReplacesEntriesButKeepsPositionsAndNumbers()
        {
            // index 0 sits on the right, index 1 on the left; after a swap index 1 holds the Wound
            var layout = CardLayout.Build(new[]
            {
                Slot(2f, 0f, "Wound", 18, index: 0),
                Slot(0f, 0f, "Strike", index: 1),
            });
            var live = new Dictionary<int, (string name, int type, bool revealed)>
            {
                [0] = ("Strike", 0, true),
                [1] = ("Wound", 18, false),
            };

            var refreshed = layout.Refresh(s =>
                new CardSlot(s.X, s.Y, live[s.SourceIndex].name, live[s.SourceIndex].type, live[s.SourceIndex].revealed, s.SourceIndex));

            Assert.Equal(new[] { 1, 0 }, refreshed.Rows[0].Select(s => s.SourceIndex));
            Assert.Equal(new[] { 1, 2 }, refreshed.Rows[0].Select(s => s.Number));
            Assert.Equal(
                "<color=#FF5252>1 Wound x</color> · <color=#9E9E9E>2 Strike</color>",
                CardLayout.FormatRichRow(refreshed.Rows[0], false));
            Assert.Equal("1 Strike · 2 Wound x", CardLayout.FormatRow(layout.Rows[0], false));
        }

        [Fact]
        public void Refresh_NullResultKeepsPreviousEntry()
        {
            var layout = CardLayout.Build(new[] { Slot(0f, 0f, "Strike", index: 3) });

            var refreshed = layout.Refresh(s => null);

            Assert.Same(layout.Rows[0][0], refreshed.Rows[0][0]);
            Assert.Equal(1, refreshed.Rows[0][0].Number);
        }

        [Fact]
        public void NullSlots_AreIgnored()
        {
            var layout = CardLayout.Build(new[] { null, Slot(0f, 0f, "A"), null });

            Assert.Equal(1, layout.Count);
            Assert.Equal(1, layout.Rows[0][0].Number);
        }
    }
}
