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
                if (__instance == null || !CardsFields.TryGetValue(__instance.GetType(), out FieldInfo cardsField))
                    return;
                if (!(cardsField.GetValue(__instance) is IEnumerable source))
                    return;

                var cards = new List<CardItem>();
                var slots = new List<CardSlot>();
                foreach (object element in source)
                {
                    var card = element as CardItem;
                    int index = cards.Count;
                    cards.Add(card);
                    if (card == null)
                        continue;

                    var data = card.GetCardData();
                    Vector3 position = card.transform.position;
                    slots.Add(new CardSlot(
                        position.x,
                        position.y,
                        data != null ? data.CardName : null,
                        data != null ? (int)data.CardType : 0,
                        card.cardrevealed,
                        index,
                        card.gameObject.name));
                }

                CardLayout layout = CardLayout.Build(slots);
                string managerName = __instance.GetType().Name;
                Plugin.Log.LogInfo($"{managerName}: {layout.Count} cards in {layout.Rows.Count} row(s)");
                foreach (var row in layout.Rows)
                    Plugin.Log.LogInfo("  " + CardLayout.FormatRow(row, useUnicodeMarkers: false));

                if (Plugin.Overlay != null)
                    Plugin.Overlay.Show(__instance as MonoBehaviour, cards, layout);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"EnableCards postfix failed: {e}");
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
