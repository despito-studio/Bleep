using System.Collections.Generic;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirUI
    {
        GameObject clothingPanel;
        UnityEngine.UI.Text clothingCharacter, clothingSummary;
        readonly List<KeyValuePair<int, UnityEngine.UI.Button>> garmentButtons = new List<KeyValuePair<int, UnityEngine.UI.Button>>();
        readonly List<UnityEngine.UI.Button> outfitButtons = new List<UnityEngine.UI.Button>();
        int clothingDraft = -1;
        string clothingClass;
        public int ClothingPreviewMask => Active(clothingPanel) ? clothingDraft : session.State?.Player(session.LocalId)?.clothingMask ?? PlayerPrefs.GetInt("DeadAir.ClothingMask", -1);

        void BuildClothingEditor()
        {
            clothingPanel = FullScreen("Character wardrobe", Color.clear);
            SceneVeil(clothingPanel.transform, .92f);
            var p = Fill(clothingPanel.transform, "Character wardrobe card", 78, 85, 1080, 910, PanelInk, true).transform;
            Text(p, "Title", "ОДЯГ ПЕРСОНАЖА", 42, 38, 36, 940, 68, title: true);
            Button(p, "Close wardrobe", "×", 1003, 24, 44, 44, () => Close(clothingPanel));
            clothingCharacter = Text(p, "Wardrobe character", "", 23, 42, 144, 996, 50);
            clothingCharacter.color = Muted;
            outfitButtons.Add(Button(p, "Service outfit", "СЛУЖБОВИЙ", 42, 218, 320, 59, () => ChooseOutfit(-1)));
            outfitButtons.Add(Button(p, "Casual outfit", "ПОВСЯКДЕННИЙ", 380, 218, 320, 59, () => ChooseOutfit(DeadAirClothing.CasualMask(clothingClass))));
            outfitButtons.Add(Button(p, "No clothing", "БЕЗ ОДЯГУ", 718, 218, 320, 59, () => ChooseOutfit(0)));
            int[] flags = { DeadAirClothing.Shirt, DeadAirClothing.Jacket, DeadAirClothing.Trousers, DeadAirClothing.Shoes, DeadAirClothing.Underwear };
            string[] names = { "Сорочка", "Куртка", "Штани", "Взуття", "Білизна" };
            for (int i = 0; i < flags.Length; i++)
            {
                int flag = flags[i]; string label = names[i];
                var button = Button(p, "Garment " + label, label, 42 + (i % 2) * 508, 326 + (i / 2) * 91, 488, 68, () => ToggleGarment(flag));
                garmentButtons.Add(new KeyValuePair<int, UnityEngine.UI.Button>(flag, button));
            }
            Rule(p, 42, 626, 996);
            clothingSummary = Text(p, "Wardrobe selection", "", 21, 44, 658, 992, 104);
            Button(p, "Apply wardrobe", "ЗБЕРЕГТИ ОБРАЗ", 42, 807, 996, 61, SaveClothing, true);
            clothingPanel.SetActive(false);
        }

        public void ShowClothingEditor()
        {
            if (DeadAirScarePresentation.FullscreenActive || session.State != null && session.State.phase != "lobby") return;
            var actor = session.State?.Player(session.LocalId);
            clothingClass = actor?.characterClass ?? selectedClass;
            clothingDraft = actor?.clothingMask ?? PlayerPrefs.GetInt("DeadAir.ClothingMask", -1);
            if (clothingDraft != -1) clothingDraft = DeadAirClothing.Sanitize(clothingDraft);
            clothingCharacter.text = world.GetClass(clothingClass).name + " / " + world.GetClass(clothingClass).role;
            RefreshClothingEditor(); Open(clothingPanel);
        }

        int EffectiveClothing => clothingDraft == -1 ? DeadAirClothing.DefaultMask(clothingClass) : DeadAirClothing.Sanitize(clothingDraft);

        void ChooseOutfit(int mask)
        {
            clothingDraft = mask == -1 ? -1 : DeadAirClothing.Sanitize(mask);
            RefreshClothingEditor();
        }

        void ToggleGarment(int flag)
        {
            clothingDraft = DeadAirClothing.Sanitize(EffectiveClothing ^ flag);
            RefreshClothingEditor();
        }

        void RefreshClothingEditor()
        {
            int mask = EffectiveClothing;
            for (int i = 0; i < garmentButtons.Count; i++)
            {
                var pair = garmentButtons[i]; bool enabled = (mask & pair.Key) != 0;
                string name = pair.Value.name.Substring("Garment ".Length);
                pair.Value.GetComponentInChildren<UnityEngine.UI.Text>().text = (enabled ? "[×]  " : "[ ]  ") + name;
                var colors = pair.Value.colors; colors.normalColor = enabled ? new Color(.43f, .24f, .17f) : new Color(.14f, .12f, .12f, .8f); pair.Value.colors = colors;
            }
            int selected = clothingDraft == -1 ? 0 : mask == 0 ? 2 : mask == DeadAirClothing.CasualMask(clothingClass) ? 1 : -1;
            for (int i = 0; i < outfitButtons.Count; i++)
            {
                var colors = outfitButtons[i].colors; colors.normalColor = i == selected ? new Color(.53f, .15f, .115f, .94f) : new Color(.14f, .12f, .12f, .8f); outfitButtons[i].colors = colors;
            }
            clothingSummary.text = mask == 0 ? "Без одягу" : clothingDraft == -1 ? "Службовий одяг за обраною професією" : "Власний комплект — " + EnabledGarments(mask);
        }

        static string EnabledGarments(int mask)
        {
            var names = new List<string>();
            if ((mask & DeadAirClothing.Shirt) != 0) names.Add("сорочка");
            if ((mask & DeadAirClothing.Jacket) != 0) names.Add("куртка");
            if ((mask & DeadAirClothing.Trousers) != 0) names.Add("штани");
            if ((mask & DeadAirClothing.Shoes) != 0) names.Add("взуття");
            if ((mask & DeadAirClothing.Underwear) != 0) names.Add("білизна");
            return string.Join(", ", names);
        }

        void SaveClothing()
        {
            session.SetClothing(clothingDraft);
            Close(clothingPanel);
        }
    }
}
