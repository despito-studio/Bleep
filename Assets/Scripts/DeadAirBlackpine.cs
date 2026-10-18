using System;
using System.Text;
using UnityEngine;

namespace DeadAir
{
    // Data coordinates, shared by interaction, presentation, compass and host rules.
    public static class DeadAirBlackpine
    {
        public const string AntennaId = "blackpine-antenna", ResonatorId = "blackpine-resonator";
        public const int RequiredFuel = 1;
        public const float CaptureSeconds = 8, AntennaSeconds = 7, ResonatorSeconds = 10;
        public static bool Active(RoomState state) => state != null && state.chapterId == DeadAirChapters.DefaultId;
        public static ObjectiveData[] Objectives(WorldData world) => new[]
        {
            new ObjectiveData { id = AntennaId, type = "blackpine-operation", label = "Роз'єднати антенний вузол · задній двір", x = 21, y = 1.15f, z = -59 },
            new ObjectiveData { id = ResonatorId, type = "blackpine-operation", label = "Зупинити Резонатор · підвал", x = 2.5f, y = -2.95f, z = -38 }
        };
        public static ObjectiveData Objective(WorldData world, string id)
        {
            if (id == AntennaId || id == ResonatorId)
                foreach (var item in Objectives(world)) if (item.id == id) return item;
            return null;
        }
        public static float OperationSeconds(string id) => id == AntennaId ? AntennaSeconds : id == ResonatorId ? ResonatorSeconds : CaptureSeconds;
        static int VariantSeed(RoomState state)
        {
            int seed = 0;
            if (state != null && state.code != null) foreach (char c in state.code) seed = (seed * 31 + c) & 0x7fffffff;
            return seed;
        }
        public static float TargetFrequency(RoomState state)
        {
            int variant = VariantSeed(state) % 3;
            return variant == 0 ? 94.8f : variant == 1 ? 96.4f : 101.2f;
        }
        public static ObjectiveData EvidenceObjective(WorldData world, RoomState state, ObjectiveData original)
        {
            if (original == null || world == null || !Active(state) || (VariantSeed(state) & 1) == 0 ||
                original.id != "tape-2" && original.id != "tape-3") return original;
            var other = world.Objective(original.id == "tape-2" ? "tape-3" : "tape-2");
            return other == null ? original : new ObjectiveData { id = original.id, type = original.type, label = original.label,
                x = other.x, y = other.y, z = other.z };
        }
        public static bool HasAllEvidence(RoomState state) => state.tapes.Contains("tape-1") && state.tapes.Contains("tape-2") && state.tapes.Contains("tape-3");
        public static string EvidenceTitle(string id) => id == "tape-1" ? "Нічна зміна" : id == "tape-2" ? "Зайвий голос" : id == "tape-3" ? "Останній ефір" : "Запис Blackpine";
        public static string EvidenceText(string id, RoomState state = null)
        {
            if (id == "tape-1") return "«Ми вимкнули мікрофони. Голос залишився. Воно сліпе — знаходить нас по кроках і рації. Якщо почуєте своє ім'я, спершу перевірте, хто кличе».";
            if (id == "tape-2") return "«Антена більше нічого не передає. Вона приймає відповідь із підвалу. Щоб розірвати петлю, роз'єднайте вузол у задньому дворі, потім вимкніть Резонатор унизу».";
            if (id == "tape-3") return "«Аварійна несуча — " + TargetFrequency(state).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " МГц. Захопіть її приймачем в апаратній. Поки не побачите джерело на приладі, вимикати його марно. Не відповідайте голосу».";
            return "";
        }
        public static string Knowledge(RoomState state)
        {
            var text = new StringBuilder();
            foreach (var id in new[] { "tape-1", "tape-2", "tape-3" })
                if (state.tapes.Contains(id)) text.AppendLine(EvidenceTitle(id)).AppendLine(EvidenceText(id, state)).AppendLine();
            return text.Length == 0 ? "Доказів поки немає. Знайдіть записи останньої зміни Blackpine." : text.ToString().TrimEnd();
        }
        public static string ObjectiveText(RoomState state)
        {
            var chapter = state.blackpine;
            if (chapter != null && chapter.resonatorStopped) return "Поверніться всією командою до фургона. Голос ще лишився у рації.";
            if (chapter != null && chapter.intercepted)
                return !chapter.antennaDisconnected ? "Роз'єднайте антенний вузол у задньому дворі. Диктор повторює голоси вашої команди." : "Зупиніть Резонатор у підвалі. Збережений прогрес дозволяє відступити й повернутися.";
            if (!state.ladderPlaced) return "Візьміть драбину у фургоні та встановіть її під вікном директора.";
            if (!state.Collected("key-main")) return "Через вікно директора знайдіть головну картку в ящику столу. Відкрийте вхід для друзів.";
            if (state.canisters.Count < RequiredFuel && !state.power) return "Візьміть одну каністру біля генератора в задньому дворі.";
            if (!state.power) return "Запустіть генератор у задньому дворі. Однієї каністри вистачить.";
            if (state.fuses.Count < 2) return "Знайдіть два запобіжники: інженерна на 1 поверсі та серверна на 2 поверсі.";
            if (!state.breakerOn) return "Увімкніть рубильник у підвалі: приймачу потрібне живлення станції.";
            if (!HasAllEvidence(state)) return "Дослідіть три записи: архів на 1 поверсі, студія та диспетчерська на 2 поверсі. Вони пояснять, що тут сталося.";
            if (!state.calibrated) return "Налаштуйте приймач в апаратній за частотою із касети «Останній ефір».";
            return "Захопіть чужий сигнал в апаратній. Залишайтеся біля приймача, доки захоплення не завершиться.";
        }
    }
}
