using System;
using System.Collections;
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

        // CardItem and its card data are reached through reflection so the build needs no game assembly.
        private static Type cardItemType;
        private static MethodInfo getCardDataMethod;
        private static Func<object, object> getCardName;
        private static Func<object, object> getCardType;

        public static void Apply(Harmony harmony)
        {
            if (!ResolveCardMembers())
            {
                Plugin.Log.LogInfo("0 patches applied");
                return;
            }

            var enableCardsPostfix = new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(EnableCardsPostfix)));
            var finishSelectionPostfix = new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(FinishSelectionPostfix)));
            var preFinishGamePostfix = new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(PreFinishGamePostfix)));
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
                    {
                        SelectedFields[type] = selectedField;
                        // When the rounds run out the pairs game reveals every remaining card without adding it to cardsSelectedList.
                        if (TryPatch(harmony, type, "PreFinishGame", preFinishGamePostfix))
                            applied++;
                    }
                    if (TryPatch(harmony, type, "EnableCards", enableCardsPostfix))
                        applied++;
                }

                if (TryPatch(harmony, type, "FinishSelection", finishSelectionPostfix))
                    applied++;
            }

            Plugin.Log.LogInfo($"{applied} patches applied");
        }

        private static bool ResolveCardMembers()
        {
            cardItemType = AccessTools.TypeByName("CardItem");
            if (cardItemType == null)
            {
                Plugin.Log.LogError("Type CardItem not found, skipping all patches");
                return false;
            }

            getCardDataMethod = AccessTools.Method(cardItemType, "GetCardData", Type.EmptyTypes);
            if (getCardDataMethod == null)
            {
                Plugin.Log.LogError("CardItem.GetCardData not found, skipping all patches");
                return false;
            }

            Type dataType = getCardDataMethod.ReturnType;
            getCardName = MemberGetter(dataType, "CardName");
            if (getCardName == null)
            {
                Plugin.Log.LogError($"{dataType.Name}.CardName not found, skipping all patches");
                return false;
            }

            getCardType = MemberGetter(dataType, "CardType");
            if (getCardType == null)
            {
                Plugin.Log.LogError($"{dataType.Name}.CardType not found, skipping all patches");
                return false;
            }

            return true;
        }

        // Unity equality also treats a destroyed card as null.
        internal static bool IsCardItem(object value) => value is Component c && c != null && cardItemType.IsInstanceOfType(c);

        private static Func<object, object> MemberGetter(Type type, string name)
        {
            PropertyInfo property = AccessTools.Property(type, name);
            if (property != null)
                return instance => property.GetValue(instance);
            FieldInfo field = AccessTools.Field(type, name);
            if (field != null)
                return instance => field.GetValue(instance);
            return null;
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
                if (!(cardsField.GetValue(__instance) is IList cards))
                    return;
                ICollection<int> faceUp = SelectedFields.TryGetValue(type, out FieldInfo selectedField)
                    ? selectedField.GetValue(__instance) as ICollection<int>
                    : null;

                var slots = new List<CardSlot>();
                for (int i = 0; i < cards.Count; i++)
                {
                    if (!IsCardItem(cards[i]))
                        continue;
                    var card = (Component)cards[i];
                    Vector3 position = card.transform.position;
                    slots.Add(ReadSlot(card, position.x, position.y, i, faceUp, allFaceUp: false));
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
        /// <paramref name="allFaceUp"/> is set once the pairs game has revealed every card at its end.
        /// </summary>
        internal static CardSlot ReadSlot(object card, float x, float y, int index, ICollection<int> faceUp, bool allFaceUp)
        {
            object data = getCardDataMethod.Invoke(card, null);
            return new CardSlot(
                x,
                y,
                data != null ? getCardName(data) as string : null,
                data != null ? Convert.ToInt32(getCardType(data)) : 0,
                allFaceUp || (faceUp != null && faceUp.Contains(index)),
                index,
                ((Component)card).gameObject.name);
        }

        private static void PreFinishGamePostfix(object __instance)
        {
            try
            {
                if (Plugin.Overlay != null)
                    Plugin.Overlay.RevealAll(__instance as MonoBehaviour);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"PreFinishGame postfix failed: {e}");
            }
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
