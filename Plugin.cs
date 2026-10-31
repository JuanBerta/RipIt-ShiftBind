using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RipIt.ShiftBind
{
    [BepInPlugin("com.juanberta.riplit.shiftbind", "Rip It! Shift Bind", "1.1.0")]
    public class Plugin : BasePlugin
    {
        public const string HarmonyId = "com.juanberta.riplit.shiftbind";

        internal static ManualLogSource PluginLog;

        public override void Load()
        {
            PluginLog = Log;
            try
            {
                var click = AccessTools.Method(typeof(DragManager), "Click", new Type[] { typeof(UnityEngine.InputSystem.InputAction.CallbackContext) });
                var update = AccessTools.Method(typeof(DragManager), "Update");
                Plugin.PluginLog.LogInfo($"Patch targets: Click={(click != null ? "FOUND" : "NULL")}, Update={(update != null ? "FOUND" : "NULL")}");
                Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, HarmonyId);
                Plugin.PluginLog.LogInfo("Rip It! Shift Bind v1.1.0 loaded (patch applied).");
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.LogError($"Rip It! Shift Bind failed to patch: {ex}");
            }
        }
    }

    /// <summary>
    /// Shift + Left Click on a card:
    ///  - duplicate card  -> instantly sold through the game's native auto-sell pipeline
    ///  - new card        -> binder flips to the card's page and the card is auto-placed in its slot
    /// v1.1.0: dual detection (Click callback + Update input fallback), native AutoSellCard,
    ///         full diagnostic logging and card resolution fallbacks.
    /// </summary>
    internal static class ShiftClickHandler
    {
        // debounce: ignore the same card for 400ms
        private static DraggableCard _lastCard;
        private static float _lastTime;

        public static bool TryHandle(DraggableCard card)
        {
            if (card == null || card.Card == null)
            {
                return false;
            }

            float now = Time.realtimeSinceStartup;
            if (card == _lastCard && now - _lastTime < 0.4f)
            {
                // already consumed this card, block re-entry from the fallback channel
                return true;
            }
            _lastCard = card;
            _lastTime = now;

            Cards.CardInfo info = card.Card;
            Plugin.PluginLog.LogInfo($"Shift+Click on card: page={info.BinderPage} rarity={info.Rarity} edition={info.Edition}");

            try
            {
                if (CardFindManager.CheckIsDupe(info))
                {
                    Plugin.PluginLog.LogInfo("Duplicate card -> native AutoSellCard pipeline.");
                    CardFindManager.AutoSellCard(card);
                    Plugin.PluginLog.LogInfo("Duplicate card handed to auto-sell pipeline.");
                }
                else
                {
                    Plugin.PluginLog.LogInfo("New card -> starting AutoBind.");
                    AutoBindState.Start(card);
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.LogError($"Shift+Click handler failed: {ex}");
                return false;
            }
        }

        public static bool IsShiftHeld()
        {
            try
            {
                Keyboard kb = Keyboard.current;
                return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static DraggableCard GetCurrentCard()
        {
            try
            {
                DraggableCard card = DragManager.CurrentDraggable as DraggableCard;
                if (card != null)
                {
                    return card;
                }

                DragManager dm = UnityEngine.Object.FindObjectOfType<DragManager>();
                if (dm != null)
                {
                    card = dm.GetCurrentDraggable() as DraggableCard;
                    if (card != null)
                    {
                        return card;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.LogWarning($"GetCurrentCard failed: {ex.Message}");
                return null;
            }
        }

        public static DraggableCard FindCardUnderPointer()
        {
            try
            {
                Camera cam = Camera.main;
                Pointer ptr = Pointer.current;
                if (cam == null || ptr == null)
                {
                    return null;
                }

                Vector2 screenPos = ptr.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(screenPos);

                RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
                DraggableCard best = null;
                float bestDist = float.MaxValue;
                foreach (RaycastHit hit in hits)
                {
                    DraggableCard dc = hit.collider.GetComponentInParent<DraggableCard>();
                    if (dc != null && hit.distance < bestDist)
                    {
                        best = dc;
                        bestDist = hit.distance;
                    }
                }
                return best;
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.LogWarning($"FindCardUnderPointer failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Primary channel: the game's own click callback. Runs the handler when Shift is held.
    /// </summary>
    [HarmonyPatch(typeof(DragManager), "Click")]
    internal static class ClickPatch
    {
        private static bool Prefix()
        {
            if (!ShiftClickHandler.IsShiftHeld())
            {
                return true;
            }

            Plugin.PluginLog.LogInfo("Click callback observed with Shift held.");

            DraggableCard card = ShiftClickHandler.GetCurrentCard();
            if (card == null)
            {
                Plugin.PluginLog.LogInfo("CurrentDraggable empty, trying pointer raycast.");
                card = ShiftClickHandler.FindCardUnderPointer();
            }

            if (card == null || card.Card == null)
            {
                Plugin.PluginLog.LogInfo("No card under click; letting the game handle it.");
                return true;
            }

            if (ShiftClickHandler.TryHandle(card))
            {
                // consumed: don't let the normal click start a drag/inspect
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Fallback channel: watches raw input inside DragManager.Update. Fires when the game's
    /// Click callback does not reach us (e.g. the callback never runs our prefix).
    /// Uses wasPressedThisFrame so it only acts on the press, before a drag can start.
    /// </summary>
    [HarmonyPatch(typeof(DragManager), "Update")]
    internal static class UpdatePatch
    {
        private static int _lastFrame = -1;

        private static bool Prefix()
        {
            if (Time.frameCount == _lastFrame)
            {
                return true;
            }
            _lastFrame = Time.frameCount;

            if (!ShiftClickHandler.IsShiftHeld())
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return true;
            }

            Plugin.PluginLog.LogInfo("Fallback: Shift+left press detected in Update.");

            DraggableCard card = ShiftClickHandler.GetCurrentCard();
            if (card == null)
            {
                card = ShiftClickHandler.FindCardUnderPointer();
            }

            if (card == null || card.Card == null)
            {
                Plugin.PluginLog.LogInfo("Fallback: no card found under pointer.");
                return true;
            }

            if (ShiftClickHandler.TryHandle(card))
            {
                // prevent the game from starting a drag on this card this frame
                DragManager.ResetDraggable();
            }
            return true;
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

            Plugin.PluginLog.LogInfo($"AutoBindState started, target page {TargetPage}.");
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
                    Plugin.PluginLog.LogInfo($"Binder resting on wrong page, retrying flip to {TargetPage}.");
                    TryFlip();
                }
                return;
            }

            // resting on the target page -> place the card
            Active = false;
            Plugin.PluginLog.LogInfo($"Auto-binding card to page {TargetPage}.");
            try
            {
                bool bound = Card.AutoBindCard(pages);
                Plugin.PluginLog.LogInfo($"AutoBindCard returned {bound}.");
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.LogError($"AutoBindCard failed: {ex}");
            }
        }

        private static void TryFlip()
        {
            BinderPages pages = DragManager.BinderPages;
            if (pages == null)
            {
                return;
            }

            Plugin.PluginLog.LogInfo($"Flipping binder to page {TargetPage}.");
            try
            {
                pages.SetRoundedPageBinderBot(TargetPage);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.LogError($"SetRoundedPageBinderBot failed: {ex}");
                Active = false;
            }
        }
    }

    [HarmonyPatch(typeof(DragManager), "Update")]
    internal static class UpdatePostfixPatch
    {
        private static void Postfix()
        {
            AutoBindState.Tick();
        }
    }
}
