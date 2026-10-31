using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RipIt.ShiftBind
{
    [BepInPlugin("com.juanberta.riplit.shiftbind", "Rip It! Shift Bind", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public const string HarmonyId = "com.juanberta.riplit.shiftbind";

        internal static ManualLogSource PluginLog;

        public override void Load()
        {
            PluginLog = Log;
            Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, HarmonyId);
            Plugin.PluginLog.LogInfo("Rip It! Shift Bind v1.0.0 loaded.");
        }
    }

    /// <summary>
    /// Shift + Left Click on a card:
    ///  - duplicate card  -> instantly sold (same pipeline as dropping it on the sell slot)
    ///  - new card        -> binder flips to the card's page and the card is auto-placed in its slot
    /// </summary>
    [HarmonyPatch(typeof(DragManager), "Click")]
    internal static class ClickPatch
    {
        // simple debounce: ignore the same card for 350ms
        private static DraggableCard _lastCard;
        private static float _lastTime;

        private static bool Prefix()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !(kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
            {
                // normal click, let the game handle it
                return true;
            }

            DraggableCard card = DragManager.CurrentDraggable as DraggableCard;
            if (card == null || card.Card == null)
            {
                return true;
            }

            float now = Time.realtimeSinceStartup;
            if (card == _lastCard && now - _lastTime < 0.35f)
            {
                return false;
            }
            _lastCard = card;
            _lastTime = now;

            Cards.CardInfo info = card.Card;
            Plugin.PluginLog.LogInfo($"Shift+Click on card: page={info.BinderPage} rarity={info.Rarity} edition={info.Edition}");

            if (CardFindManager.CheckIsDupe(info))
            {
                // duplicate -> sell it like a manual drop on the sell slot
                SellSlot sellSlot = UnityEngine.Object.FindObjectOfType<SellSlot>();
                if (sellSlot != null)
                {
                    sellSlot.Sell(card);
                    Plugin.PluginLog.LogInfo("Duplicate card sent to sell.");
                }
                else
                {
                    Plugin.PluginLog.LogWarning("SellSlot not found in scene, duplicate not sold.");
                }
            }
            else
            {
                // new card -> flip to page and auto-bind
                AutoBindState.Start(card);
            }

            // consume the click so the normal drag doesn't start
            return false;
        }
    }

    /// <summary>
    /// Tiny state machine that waits for the binder to finish flipping to the card's page,
    /// then calls AutoBindCard. Driven from DragManager.Update via a Harmony Postfix.
    /// </summary>
    internal static class AutoBindState
    {
        public static bool Active;
        public static DraggableCard Card;
        public static int TargetPage;
        public static float TimeoutAt;
        private static bool _retriedFlip;

        public static void Start(DraggableCard card)
        {
            if (card == null || card.Card == null)
            {
                return;
            }

            Card = card;
            TargetPage = card.Card.BinderPage;
            TimeoutAt = Time.realtimeSinceStartup + 8f;
            _retriedFlip = false;
            Active = true;

            TryFlip();
        }

        public static void Tick()
        {
            if (!Active)
            {
                return;
            }

            BinderPages pages = DragManager.BinderPages;
            if (pages == null || Card == null || Card.Card == null)
            {
                Plugin.PluginLog.LogWarning("AutoBind state invalid, aborting.");
                Active = false;
                return;
            }

            if (Time.realtimeSinceStartup > TimeoutAt)
            {
                Plugin.PluginLog.LogWarning("AutoBind timed out, giving up.");
                Active = false;
                return;
            }

            if (pages.IsFlipping)
            {
                // still flipping, wait
                return;
            }

            if (!pages.IsOnPage(TargetPage))
            {
                // reached a resting state but not the target page yet -> flip again (one retry)
                if (!_retriedFlip)
                {
                    _retriedFlip = true;
                    TryFlip();
                }
                return;
            }

            // resting on the target page -> place the card
            Active = false;
            Plugin.PluginLog.LogInfo($"Auto-binding card to page {TargetPage}.");
            Card.AutoBindCard(pages);
        }

        private static void TryFlip()
        {
            BinderPages pages = DragManager.BinderPages;
            if (pages == null)
            {
                return;
            }

            Plugin.PluginLog.LogInfo($"Flipping binder to page {TargetPage}.");
            pages.SetRoundedPageBinderBot(TargetPage);
        }
    }

    [HarmonyPatch(typeof(DragManager), "Update")]
    internal static class UpdatePatch
    {
        private static void Postfix()
        {
            AutoBindState.Tick();
        }
    }
}
