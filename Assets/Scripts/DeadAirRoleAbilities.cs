using UnityEngine;

namespace DeadAir
{
    // The item animation is shorter than these host-owned reuse timers.
    public static class DeadAirRoleAbilities
    {
        public static float Cooldown(string role) => role == "operator" ? 7 : role == "engineer" ? 12 :
            role == "journalist" ? 18 : 8;
        public static string Help(string role) => role == "operator" ? "Сигарети: особиста дія, 12 у пачці." :
            role == "engineer" ? "Ключ: дістаньте запобіжник поруч, запустіть заправлений генератор або захистіть щиток; зблизька — відштовхніть Диктора." :
            role == "journalist" ? "Диктофон: пеленг близького Диктора та гучне відтворення, що відволікає його на місце запису." :
            "Компас: напрямок до наступного потрібного предмета або завдання без шуму.";
        public static float ReadyIn(PlayerState player, float elapsed) => player == null ? 0 : Mathf.Max(0, player.roleReadyAt - elapsed);
        public static bool HasIntel(PlayerState player, float elapsed) => player != null && player.roleIntelUntil > elapsed &&
            !string.IsNullOrEmpty(player.roleIntelKind);
        public static Vector3 IntelPosition(PlayerState player) => new Vector3(-player.roleIntelX, player.roleIntelY, player.roleIntelZ);
    }
}
