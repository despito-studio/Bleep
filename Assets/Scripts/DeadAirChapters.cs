using System;
using System.Collections.Generic;

namespace DeadAir
{
    public sealed class DeadAirChapter
    {
        public readonly string id, number, title, subtitle, description;
        public readonly bool playable;

        public DeadAirChapter(string id, string number, string title, string subtitle, string description, bool playable)
        {
            this.id = id; this.number = number; this.title = title; this.subtitle = subtitle;
            this.description = description; this.playable = playable;
        }
    }

    public static class DeadAirChapters
    {
        public const string DefaultId = "blackpine";
        public static IReadOnlyList<DeadAirChapter> Catalog { get; } = Array.AsReadOnly(new[]
        {
            new DeadAirChapter(DefaultId, "01", "BLACKPINE", "Мертвий ефір",
                "Засніжена станція в горах. Перехопіть чужий ефір, розкрийте таємницю голосів і вимкніть джерело в підвалі. Коза й курка досі чекають доглядача.", true),
            new DeadAirChapter("coming-soon", "02", "ЯПОНСЬКА ШКОЛА", "Японська школа & Тек-Тек",
                "Японська школа Тачібана та традиційний парк напроти. Дослідіть закинуті коридори, зберіть 5 записів доказів журналістів, знаходьте пігулки для розсудку та остерігайтеся примари Тек-Тек.", true)
        });

        public static bool IsChapter2(string id) => id == "coming-soon" || id == "school" || id == "02";

        public static DeadAirChapter Find(string id)
        {
            foreach (var chapter in Catalog)
                if (string.Equals(chapter.id, id, StringComparison.Ordinal) || (IsChapter2(id) && IsChapter2(chapter.id))) return chapter;
            return null;
        }

        public static bool IsPlayable(string id) => Find(id)?.playable == true;

        public static string SelectionError(string id)
        {
            var chapter = Find(id);
            return chapter == null ? "Невідома глава." : !chapter.playable ? "Ця глава ще недоступна." : null;
        }
    }
}
