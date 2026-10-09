namespace DeadAir
{
    public static class DeadAirLootSummary
    {
        public static bool IsRecoverable(WorldData world, string item)
        {
            var objective = world?.Objective(item);
            if (objective == null || item == "ladder" || item == "van-map" || item == "exit") return false;
            return item.StartsWith("tape-") || item.StartsWith("fuse-") || item.StartsWith("fuel-canister") ||
                item.StartsWith("key-") || item.StartsWith("keycard-") || item == DeadAirPrisonBook.Id;
        }

        public static int CollectedCount(WorldData world, RoomState state)
        {
            if (world?.objects == null || state == null) return 0;
            int count = 0;
            foreach (var objective in world.objects)
                if (IsRecoverable(world, objective.id) && state.Collected(objective.id)) count++;
            return count;
        }
    }
}
