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
        int supplyCharge=-1,supplyBatteries=-1,supplyCoins=-1,stackBatteries=-1;
        bool supplyHasFlashlight;
        string batteryStackName;

        void BuildSupplies()
        {
            suppliesText = Text(hudPanel.transform, "Flashlight supplies", "", 17, 1070, 782, 790, 32, TextAnchor.MiddleRight);
            suppliesText.color = Muted;
            bagSuppliesText = Text(journalInventoryPage, "Bag supplies", "", 15, 42, 139, 644, 35);
            bagSuppliesText.color = JournalMuted;
            bookPanel = Modal("Prison journal", DeadAirPrisonBook.Title, 1100, 820, out var card);
            bookBody = Text(card, "Journal text", "", 22, 50, 157, 1000, 494);
            bookBody.color = Ink; bookBody.lineSpacing = 1.18f;
            previousBookPage = Button(card, "Previous page", "‹ ПОПЕРЕДНЯ", 50, 714, 280, 56, () => { bookPage--; RefreshBook(); });
            bookPageLabel = Text(card, "Journal page", "", 18, 350, 714, 400, 56, TextAnchor.MiddleCenter);
            nextBookPage = Button(card, "Next page", "НАСТУПНА ›", 770, 714, 280, 56, () => { bookPage++; RefreshBook(); });
        }

        string StackItemName(string itemId, PlayerState player)
        {
            if(itemId!=DeadAirSupplies.BatteryItem)return FormatItemName(itemId);
            if(player.spareBatteries!=stackBatteries||batteryStackName==null)
            {stackBatteries=player.spareBatteries;batteryStackName="Батарейки ×"+stackBatteries;}
            return batteryStackName;
        }

        void RefreshSupplies(RoomState state, PlayerState playerState)
        {
            int charge = Mathf.CeilToInt(Mathf.Clamp01(playerState.flashlightCharge / DeadAirSupplies.FlashlightSeconds) * 100);
            bool hasFlashlight=playerState.HasItem("flashlight");
            if(charge!=supplyCharge||playerState.spareBatteries!=supplyBatteries||hasFlashlight!=supplyHasFlashlight)
                suppliesText.text = hasFlashlight ? "ЛІХТАР " + charge + "%" : "";
            suppliesText.color = charge <= 15 && hasFlashlight ? Gold : Muted;
            if(playerState.coins!=supplyCoins||playerState.spareBatteries!=supplyBatteries)
                bagSuppliesText.text = "МОНЕТКИ: " + playerState.coins + "    /    ЗАПАСНІ БАТАРЕЙКИ: " + playerState.spareBatteries;
            supplyCharge=charge;supplyBatteries=playerState.spareBatteries;supplyCoins=playerState.coins;supplyHasFlashlight=hasFlashlight;
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
