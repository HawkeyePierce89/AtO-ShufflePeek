using System;
using System.Collections.Generic;
using System.Linq;

namespace AtOShufflePeek
{
    public enum CardKind
    {
        Normal,
        Injury,
        Boon
    }

    /// <summary>One face-down card as seen after the shuffle. Plain values only, no Unity types.</summary>
    public sealed class CardSlot
    {
        public const int InjuryType = 18;
        public const int BoonType = 21;

        public CardSlot(float x, float y, string name, int cardType, bool revealed, int sourceIndex, string fallbackName = null)
        {
            X = x;
            Y = y;
            Name = name;
            CardType = cardType;
            Revealed = revealed;
            SourceIndex = sourceIndex;
            FallbackName = fallbackName;
        }

        public float X { get; }
        public float Y { get; }
        public string Name { get; }
        public int CardType { get; }
        public bool Revealed { get; }

        /// <summary>Index into the manager's cards list, used to refresh the revealed state every frame.</summary>
        public int SourceIndex { get; }

        public string FallbackName { get; }

        /// <summary>1-based position counted from the top-left; assigned by <see cref="CardLayout.Build"/>.</summary>
        public int Number { get; internal set; }

        public CardKind Kind => CardLayout.Classify(CardType);

        public string DisplayName =>
            !string.IsNullOrEmpty(Name) ? Name
            : !string.IsNullOrEmpty(FallbackName) ? FallbackName
            : "?";
    }

    public sealed class CardLayout
    {
        public const string Separator = " · ";

        public static readonly CardLayout Empty = new CardLayout(new List<IReadOnlyList<CardSlot>>());

        private CardLayout(IReadOnlyList<IReadOnlyList<CardSlot>> rows)
        {
            Rows = rows;
        }

        /// <summary>Rows top to bottom, each ordered left to right.</summary>
        public IReadOnlyList<IReadOnlyList<CardSlot>> Rows { get; }

        public int Count => Rows.Sum(r => r.Count);

        public bool IsEmpty => Count == 0;

        public static CardLayout Build(IEnumerable<CardSlot> slots, float rowTolerance = 0.5f)
        {
            if (slots == null)
                return Empty;

            var rows = new List<List<CardSlot>>();
            float currentY = 0f;
            foreach (var slot in slots.Where(s => s != null).OrderByDescending(s => s.Y))
            {
                if (rows.Count == 0 || Math.Abs(currentY - slot.Y) > rowTolerance)
                {
                    rows.Add(new List<CardSlot>());
                    currentY = slot.Y;
                }
                rows[rows.Count - 1].Add(slot);
            }

            var ordered = new List<IReadOnlyList<CardSlot>>(rows.Count);
            int number = 1;
            foreach (var row in rows)
            {
                var sorted = row.OrderBy(s => s.X).ToList();
                foreach (var slot in sorted)
                    slot.Number = number++;
                ordered.Add(sorted);
            }
            return new CardLayout(ordered);
        }

        public static CardKind Classify(int cardType)
        {
            switch (cardType)
            {
                case CardSlot.InjuryType: return CardKind.Injury;
                case CardSlot.BoonType: return CardKind.Boon;
                default: return CardKind.Normal;
            }
        }

        public static string MarkerFor(CardKind kind, bool useUnicodeMarkers)
        {
            switch (kind)
            {
                case CardKind.Injury: return useUnicodeMarkers ? "✗" : "x";
                case CardKind.Boon: return useUnicodeMarkers ? "✓" : "+";
                default: return string.Empty;
            }
        }

        public static string FormatEntry(CardSlot slot, bool useUnicodeMarkers)
        {
            string text = slot.Number + " " + slot.DisplayName;
            string marker = MarkerFor(slot.Kind, useUnicodeMarkers);
            return marker.Length == 0 ? text : text + " " + marker;
        }

        public static string FormatRow(IEnumerable<CardSlot> row, bool useUnicodeMarkers)
        {
            if (row == null)
                return string.Empty;
            return string.Join(Separator, row.Select(s => FormatEntry(s, useUnicodeMarkers)));
        }

        public const string RevealedColor = "#9E9E9E";
        public const string InjuryColor = "#FF5252";
        public const string BoonColor = "#69F0AE";
        public const string NormalColor = "#FFFFFF";

        /// <summary>Rich-text colour for an entry: grey once face up, otherwise by kind.</summary>
        public static string ColorFor(CardKind kind, bool revealed)
        {
            if (revealed)
                return RevealedColor;
            switch (kind)
            {
                case CardKind.Injury: return InjuryColor;
                case CardKind.Boon: return BoonColor;
                default: return NormalColor;
            }
        }

        /// <summary>
        /// IMGUI rich-text row. <paramref name="isRevealed"/> maps a slot to its live revealed state;
        /// when null the captured <see cref="CardSlot.Revealed"/> is used.
        /// </summary>
        public static string FormatRichRow(IEnumerable<CardSlot> row, bool useUnicodeMarkers, Func<CardSlot, bool> isRevealed = null)
        {
            if (row == null)
                return string.Empty;
            return string.Join(Separator, row.Select(s =>
            {
                bool revealed = isRevealed != null ? isRevealed(s) : s.Revealed;
                return "<color=" + ColorFor(s.Kind, revealed) + ">" + FormatEntry(s, useUnicodeMarkers) + "</color>";
            }));
        }
    }
}
