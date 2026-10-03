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
                "Двоповерховий офіс Blackpine. Дослідіть кімнати, відновіть живлення генератора в задньому дворі та передайте сигнал.", true),
            new DeadAirChapter("coming-soon", "02", "НЕВІДОМА ЧАСТОТА", "",
                "Наступна глава готується.", false)
        });

        public static DeadAirChapter Find(string id)
        {
            foreach (var chapter in Catalog)
                if (string.Equals(chapter.id, id, StringComparison.Ordinal)) return chapter;
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
