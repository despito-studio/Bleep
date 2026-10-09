using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirUI
    {
        Transform journalInventoryPage;
        UnityEngine.UI.Text journalChapter, journalChecklist, journalTotals;
        string journalRenderedChapter;
        int journalSignature = int.MinValue;
        static Color JournalInk => new Color(.20f, .17f, .13f);
        static Color JournalMuted => new Color(.43f, .36f, .28f);

        UnityEngine.UI.Text JournalText(Transform parent, string name, string value, int size, float x, float y, float width, float height, bool title = false)
        {
            var label = Text(parent, name, value, size, x, y, width, height, title: title);
            label.color = JournalInk; return label;
        }

        UnityEngine.UI.Button JournalButton(Transform parent, string name, string value, float x, float y, float width, float height, System.Action action)
        {
            var button = Button(parent, name, value, x, y, width, height, action);
            var colors = button.colors;
            colors.normalColor = new Color(.82f, .77f, .65f);
            colors.highlightedColor = colors.selectedColor = new Color(.69f, .59f, .44f);
            colors.pressedColor = new Color(.62f, .51f, .37f);
            colors.disabledColor = new Color(.84f, .79f, .68f, .50f);
            button.colors = colors;
            button.GetComponentInChildren<UnityEngine.UI.Text>().color = JournalInk;
            return button;
        }

        void BuildFieldJournal()
        {
            inventoryPanel = FullScreen("Inventory", new Color(.015f, .012f, .009f, .72f));
            Fill(inventoryPanel.transform, "Book shadow", 210, 108, 1520, 900, new Color(0, 0, 0, .58f));
            var cover = Fill(inventoryPanel.transform, "Field journal cover", 200, 90, 1520, 900, new Color(.22f, .15f, .10f), true).transform;
            Fill(cover, "Left page edges", 15, 28, 733, 855, new Color(.66f, .58f, .43f));
            Fill(cover, "Right page edges", 772, 28, 733, 855, new Color(.66f, .58f, .43f));
            var left = Fill(cover, "Mission page", 20, 20, 724, 860, new Color(.91f, .86f, .73f)).transform;
            journalInventoryPage = Fill(cover, "Inventory card", 776, 20, 724, 860, new Color(.94f, .90f, .79f)).transform;
            Fill(cover, "Book binding", 744, 20, 32, 860, new Color(.50f, .41f, .28f));
            Fill(cover, "Binding fold", 754, 20, 10, 860, new Color(.29f, .23f, .15f, .38f));
            Fill(left, "Page fold shade", 707, 0, 17, 860, new Color(.49f, .40f, .25f, .18f));
            Fill(journalInventoryPage, "Page fold shade", 0, 0, 17, 860, new Color(.49f, .40f, .25f, .14f));
            var close = Button(cover, "Close inventory", "×", 1442, -17, 54, 54, () => Close(inventoryPanel), true);
            close.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 30;
            JournalText(left, "Journal title", "ПОЛЬОВИЙ ЖУРНАЛ", 40, 42, 36, 640, 66, true);
            journalChapter = JournalText(left, "Chapter record", "", 18, 44, 110, 638, 54);
            journalChapter.color = JournalMuted;
            JournalText(left, "Current task caption", "ПОТОЧНЕ ЗАВДАННЯ", 15, 44, 184, 638, 26).color = JournalMuted;
            objectiveText = JournalText(left, "Objective", "", 26, 44, 221, 638, 85, true);
            Fill(left, "Mission divider", 44, 318, 636, 1, new Color(.34f, .27f, .18f, .3f));
            journalChecklist = JournalText(left, "Operation checklist", "", 20, 44, 346, 638, 192);
            journalChecklist.lineSpacing = 1.25f;
            JournalText(left, "Findings caption", "ЗНАХІДКИ КОМАНДИ", 15, 44, 565, 638, 28).color = JournalMuted;
            evidenceText = JournalText(left, "Evidence counts", "", 21, 44, 608, 638, 36);
            fuelEvidenceText = JournalText(left, "Fuel counts", "", 21, 44, 650, 638, 36);
            fuseEvidenceText = JournalText(left, "Fuse counts", "", 21, 44, 692, 638, 36);
            journalTotals = JournalText(left, "Collected and extracted", "", 16, 44, 744, 638, 68);
            JournalText(left, "Map board note", "План офісу — на дошці у фургоні.", 14, 44, 815, 638, 28).color = JournalMuted;

            var p = journalInventoryPage;
            JournalText(p, "Inventory title", "ОСОБИСТІ РЕЧІ", 38, 42, 36, 644, 68, true);
            for (int i = 0; i < 12; i++)
            {
                int index = i; float x = 42 + (i % 4) * 166, y = 192 + (i / 4) * 104;
                var button = JournalButton(p, "Inventory item " + i, "", x, y, 148, 88, () => { bagSelection = bagPage * 12 + index; RefreshInventory(); });
                var icon = Art(button.transform, "Item icon", "tape", 56, 11, 38, 38, JournalInk);
                var label = button.transform.Find("Label").GetComponent<UnityEngine.UI.Text>();
                label.fontSize = 13; label.rectTransform.anchoredPosition = new Vector2(3, -55); label.rectTransform.sizeDelta = new Vector2(142, 25);
                bagSlots.Add(button); bagIcons.Add(icon); bagLabels.Add(label);
            }
            previousInventoryPage = JournalButton(p, "Previous inventory page", "‹", 42, 512, 58, 42, () => { bagPage = Mathf.Max(0, bagPage - 1); bagSelection = bagPage * 12; RefreshInventory(); });
            inventoryPageLabel = Text(p, "Inventory page", "", 15, 110, 512, 510, 42, TextAnchor.MiddleCenter); inventoryPageLabel.color = JournalMuted;
            nextInventoryPage = JournalButton(p, "Next inventory page", "›", 628, 512, 58, 42, () => { var me = session.State?.Player(session.LocalId); if (me != null && ((bagPage + 1) * 12) < me.inventory.Count) bagPage++; bagSelection = bagPage * 12; RefreshInventory(); });
            Fill(p, "Inventory divider", 42, 579, 644, 1, new Color(.34f, .27f, .18f, .3f));
            inventoryPreview = Art(p, "Selected item", "tape", 43, 611, 66, 66, JournalInk);
            inventoryName = JournalText(p, "Item name", "", 27, 130, 608, 548, 47, true);
            inventoryDescription = JournalText(p, "Item description", "", 16, 130, 650, 548, 116); inventoryDescription.color = JournalMuted;
            inventoryEquipLabel = JournalButton(p, "Equip item", "ОБРАТИ ПРЕДМЕТ", 42, 774, 644, 51, EquipInventorySelection).GetComponentInChildren<UnityEngine.UI.Text>();
            inventoryPanel.SetActive(false);
        }

        void RefreshJournal(RoomState state, PlayerState actor)
        {
            if (state == null || actor == null) return;
            if (journalRenderedChapter != state.chapterId)
            {
                var chapter = DeadAirChapters.Find(state.chapterId) ?? DeadAirChapters.Find(DeadAirChapters.DefaultId);
                journalChapter.text = "БЮРО-9 / Глава " + chapter.number + "\n" + chapter.title;
                journalRenderedChapter = state.chapterId;
            }
            int signature = state.tapes.Count + state.canisters.Count * 4 + state.fuses.Count * 16 + state.keycards.Count * 64 + state.collected.Count * 256 + state.securedItems.Count * 4096;
            signature = signature * 32 + (state.power ? 1 : 0) + (state.breakerOn ? 2 : 0) + (state.calibrated ? 4 : 0) + (state.broadcast ? 8 : 0);
            if (signature == journalSignature) return;
            journalSignature = signature;
            objectiveText.text = state.broadcast ? "Поверніться до фургона" : state.breakerOn ? "Передайте сигнал на 96.4 МГц" : state.power ?
                (state.fuses.Count >= 2 ? "Увімкніть рубильник у підвалі" : "Знайдіть запобіжники для щитка") : state.canisters.Count >= 3 ?
                "Увімкніть генератор у дворі" : "Знайдіть дизель для генератора";
            journalChecklist.text = Check(state.power, "Запустити генератор") + "\n" + Check(state.breakerOn, "Повернути освітлення") + "\n" +
                Check(state.tapes.Count >= 3, "Знайти три касети") + "\n" + Check(state.calibrated, "Налаштувати 96.4 МГц") + "\n" + Check(state.broadcast, "Передати записи");
            evidenceText.text = "Касети                 " + state.tapes.Count + " / 3";
            fuelEvidenceText.text = "Каністри дизеля        " + state.canisters.Count + " / 3";
            fuseEvidenceText.text = "Запобіжники            " + state.fuses.Count + " / 2";
            journalTotals.text = "Зібрано речей: " + DeadAirLootSummary.CollectedCount(world, state) +
                "\nВинесено до фургона: " + state.securedItems.Count + "\nПередано записів: " + (state.broadcast ? "3 / 3" : "0 / 3");
        }

        static string Check(bool done, string text) => (done ? "[×]  " : "[ ]  ") + text;
    }
}
