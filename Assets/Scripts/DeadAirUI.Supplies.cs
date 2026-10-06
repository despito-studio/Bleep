using UnityEngine;
using UnityEngine.UI;

namespace DeadAir
{
    public sealed partial class DeadAirUI
    {
        GameObject bookPanel;
        Text suppliesText, bagSuppliesText, bookBody, bookPageLabel;
        Button previousBookPage, nextBookPage;
        int bookPage;

        void BuildSupplies()
        {
            suppliesText = Text(hudPanel.transform, "Flashlight supplies", "", 15, 1070, 790, 790, 28, TextAnchor.MiddleRight);
            suppliesText.color = Muted;
            bagSuppliesText = Text(inventoryPanel.transform.Find("Inventory card"), "Bag supplies", "", 16, 42, 139, 644, 35);
            bagSuppliesText.color = Muted;
            bookPanel = Modal("Prison journal", DeadAirPrisonBook.Title, 1100, 820, out var card);
            bookBody = Text(card, "Journal text", "", 22, 50, 157, 1000, 494);
            bookBody.color = Ink; bookBody.lineSpacing = 1.18f;
            previousBookPage = Button(card, "Previous page", "‹ ПОПЕРЕДНЯ", 50, 714, 280, 56, () => { bookPage--; RefreshBook(); });
            bookPageLabel = Text(card, "Journal page", "", 18, 350, 714, 400, 56, TextAnchor.MiddleCenter);
            nextBookPage = Button(card, "Next page", "НАСТУПНА ›", 770, 714, 280, 56, () => { bookPage++; RefreshBook(); });
        }

        static string StackItemName(string itemId, PlayerState player) => itemId == DeadAirSupplies.BatteryItem ?
            "Батарейки ×" + player.spareBatteries : FormatItemName(itemId);

        void RefreshSupplies(RoomState state, PlayerState playerState)
        {
            int charge = Mathf.CeilToInt(Mathf.Clamp01(playerState.flashlightCharge / DeadAirSupplies.FlashlightSeconds) * 100);
            suppliesText.text = playerState.HasItem("flashlight") ?
                "ЛІХТАР " + charge + "%  ·  БАТАРЕЙКИ " + playerState.spareBatteries : "БАТАРЕЙКИ " + playerState.spareBatteries;
            suppliesText.color = charge <= 15 && playerState.HasItem("flashlight") ? Gold : Muted;
            bagSuppliesText.text = "МОНЕТКИ: " + playerState.coins + "    /    ЗАПАСНІ БАТАРЕЙКИ: " + playerState.spareBatteries;
            if (playerState.SelectedItem == "flashlight" || playerState.SelectedItem == DeadAirSupplies.BatteryItem)
                roleItemText.text = "R — замінити батарейку";
            else if (playerState.SelectedItem == DeadAirPrisonBook.Id) roleItemText.text = "R — прочитати щоденник";
            if (Active(bookPanel) && (playerState.down || !playerState.HasItem(DeadAirPrisonBook.Id))) Close(bookPanel);
        }

        public void ShowBook()
        {
            var state = session.State;
            var actor = state?.Player(session.LocalId);
            if (state == null || state.phase != "playing" || actor == null || actor.down || actor.ladderClimbing ||
                !string.IsNullOrEmpty(actor.hiding) || !actor.HasItem(DeadAirPrisonBook.Id)) return;
            bookPage = 0; RefreshBook(); Open(bookPanel);
        }

        void RefreshBook()
        {
            bookPage = Mathf.Clamp(bookPage, 0, DeadAirPrisonBook.Pages.Length - 1);
            bookBody.text = DeadAirPrisonBook.Pages[bookPage];
            bookPageLabel.text = (bookPage + 1) + " / " + DeadAirPrisonBook.Pages.Length;
            previousBookPage.interactable = bookPage > 0;
            nextBookPage.interactable = bookPage + 1 < DeadAirPrisonBook.Pages.Length;
        }
    }
}
