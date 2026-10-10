using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AtOShufflePeek
{
    internal static class Patches
    {
        private static readonly string[] ManagerTypeNames = { "CardPlayerManager", "CardPlayerPairsManager" };

        private static readonly Dictionary<Type, FieldInfo> CardsFields = new Dictionary<Type, FieldInfo>();

        // Pairs game only: indices into cards that are face up (picked or matched).
        private static readonly Dictionary<Type, FieldInfo> SelectedFields = new Dictionary<Type, FieldInfo>();

        public static void Apply(Harmony harmony)
        {
            var enableCardsPostfix = new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(EnableCardsPostfix)));
            var finishSelectionPostfix = new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(FinishSelectionPostfix)));
            int applied = 0;

            foreach (string typeName in ManagerTypeNames)
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    Plugin.Log.LogWarning($"Type {typeName} not found, skipping its patches");
                    continue;
                }

                FieldInfo cardsField = AccessTools.Field(type, "cards");
                if (cardsField == null)
                    Plugin.Log.LogWarning($"{typeName}.cards not found, skipping {typeName}.EnableCards");
                else
                {
                    CardsFields[type] = cardsField;
                    FieldInfo selectedField = AccessTools.Field(type, "cardsSelectedList");
                    if (selectedField != null)
                        SelectedFields[type] = selectedField;
                    if (TryPatch(harmony, type, "EnableCards", enableCardsPostfix))
                        applied++;
                }

                if (TryPatch(harmony, type, "FinishSelection", finishSelectionPostfix))
                    applied++;
            }

            Plugin.Log.LogInfo($"{applied} patches applied");
        }

        private static bool TryPatch(Harmony harmony, Type type, string methodName, HarmonyMethod postfix)
        {
            MethodInfo original = AccessTools.Method(type, methodName);
            if (original == null)
            {
                Plugin.Log.LogWarning($"{type.Name}.{methodName} not found, skipping");
                return false;
            }

            try
            {
                harmony.Patch(original, postfix: postfix);
                Plugin.Log.LogInfo($"Patch applied: {type.Name}.{methodName}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Patching {type.Name}.{methodName} failed: {e}");
                return false;
            }
        }

        private static void EnableCardsPostfix(object __instance)
        {
            try
            {
                Type type = __instance?.GetType();
                if (type == null || !CardsFields.TryGetValue(type, out FieldInfo cardsField))
                    return;
                // Keep the manager's live lists: the pairs game swaps cards between indices after a miss.
                if (!(cardsField.GetValue(__instance) is IList<CardItem> cards))
                    return;
                ICollection<int> faceUp = SelectedFields.TryGetValue(type, out FieldInfo selectedField)
                    ? selectedField.GetValue(__instance) as ICollection<int>
                    : null;

                var slots = new List<CardSlot>();
                for (int i = 0; i < cards.Count; i++)
                {
                    CardItem card = cards[i];
                    if (card == null)
                        continue;
                    Vector3 position = card.transform.position;
                    slots.Add(ReadSlot(card, position.x, position.y, i, faceUp));
                }

                CardLayout layout = CardLayout.Build(slots);
                Plugin.Log.LogInfo($"{type.Name}: {layout.Count} cards in {layout.Rows.Count} row(s)");
                foreach (var row in layout.Rows)
                    Plugin.Log.LogInfo("  " + CardLayout.FormatRow(row, useUnicodeMarkers: false));

                if (Plugin.Overlay != null)
                    Plugin.Overlay.Show(__instance as MonoBehaviour, cards, faceUp, layout);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"EnableCards postfix failed: {e}");
            }
        }

        /// <summary>
        /// Reads the card at <paramref name="index"/> into a slot at the given position. The game's own
        /// cardrevealed flag is set on every card during the shuffle and never cleared, so face-up state
        /// comes from the pairs game's selected list instead; the single-pick game has none.
        /// </summary>
        internal static CardSlot ReadSlot(CardItem card, float x, float y, int index, ICollection<int> faceUp)
        {
            var data = card.GetCardData();
            return new CardSlot(
                x,
                y,
                data != null ? data.CardName : null,
                data != null ? (int)data.CardType : 0,
                faceUp != null && faceUp.Contains(index),
                index,
                card.gameObject.name);
        }

        private static void FinishSelectionPostfix()
        {
            try
            {
                if (Plugin.Overlay != null)
                    Plugin.Overlay.Hide();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"FinishSelection postfix failed: {e}");
            }
        }
    }
}
