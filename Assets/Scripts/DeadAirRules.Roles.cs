using System;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirRules
    {
        const float AttackWindup = .85f, AttackRecovery = 1.2f;
        float attackRecoveryUntil, lastPerceptionTick;

        bool UseEngineerTool(PlayerState player)
        {
            // Recovery uses the actual authored or dropped fuse. It never invents
            // a third fuse, grants fuel or bypasses a locked room through a wall.
            if (world.objects != null)
                foreach (var obj in world.objects)
                    if (obj.id.StartsWith("fuse-", StringComparison.Ordinal) && !State.Collected(obj.id) &&
                        Near(player, obj.x, obj.y, obj.z, 2.8f, 0, obj.id))
                    {
                        State.fuses.Add(obj.id); State.collected.Add(obj.id);
                        if (!player.HasItem(obj.id)) player.inventory.Add(obj.id);
                        State.message = "КЛЮЧ: запобіжник 380В вийнято з кріплення (" + State.fuses.Count + "/2).";
                        UpdateQuest(); return true;
                    }
            for (int index = 0; index < State.droppedItems.Count; index++)
            {
                var item = State.droppedItems[index];
                if (item.itemId == null || !item.itemId.StartsWith("fuse-", StringComparison.Ordinal) ||
                    !Near(player, item.x, item.y, item.z, 2.8f, 1)) continue;
                if (!player.HasItem(item.itemId)) player.inventory.Add(item.itemId);
                if (!State.fuses.Contains(item.itemId)) State.fuses.Add(item.itemId);
                State.droppedItems.RemoveAt(index);
                State.message = "КЛЮЧ: загублений запобіжник відновлено."; UpdateQuest(); return true;
            }
            var breaker = world.Objective("breaker");
            if (breaker != null && Near(player, breaker.x, breaker.y, breaker.z, 2.8f, 0, breaker.id))
            {
                if (!State.power || State.fuses.Count < 2)
                {
                    State.message = "КЛЮЧ: щитку потрібні живлення генератора і два справжні запобіжники.";
                    return false;
                }
                State.breakerOn = State.quest.breakerOn = true;
                nextBreakerTrip = State.elapsed + BreakerProtectionSeconds * 2;
                State.message = "КЛЮЧ: контакти закріплено. Щиток захищено на 240 секунд.";
                return true;
            }
            var generator = world.Objective("power");
            if (generator != null && Near(player, generator.x, generator.y, generator.z, 2.8f, 0, generator.id))
            {
                if (State.canisters.Count < RequiredFuel)
                {
                    State.message = IsBlackpine ? "КЛЮЧ: механізм справний; потрібна одна каністра дизелю біля генератора." : "КЛЮЧ: механізм справний; потрібні всі три каністри дизелю.";
                    return false;
                }
                State.power = State.quest.power = true;
                if (!State.collected.Contains(generator.id)) State.collected.Add(generator.id);
                State.message = "КЛЮЧ: паливний насос прокачано, генератор запущено.";
                if (IsBlackpine) BeginBlackpineBuildUp();
                UpdateQuest(); return true;
            }
            var enemy = State.enemy;
            if (enemy.active && enemy.behaviour != "vanished" && enemy.behaviour != "peek" &&
                SameFloor(player.y, enemy.y) && Dist(player.x, player.z, enemy.x, enemy.z) < 2.2f &&
                !SegmentBlocked(player.x, player.y, player.z, enemy.x, enemy.y, enemy.z))
            {
                // This is an explicit physical defence, never a flashlight stun.
                enemy.stunnedUntil = State.elapsed + 1.75f * world.GetClass(player.characterClass).bonuses.stunBonus;
                enemy.attackPlayerId = null; enemy.attackStartedAt = -100;
                State.message = "КЛЮЧ: Диктора відштовхнуто. Відступайте!";
                return true;
            }
            State.message = "КЛЮЧ: поруч немає запобіжника, щитка, генератора чи Диктора.";
            return false;
        }

        void UseRecorder(PlayerState player)
        {
            var enemy = State.enemy;
            float range = 18;
            if (!SameFloor(player.y, enemy.y)) range *= .4f;
            if (PhysicalSegmentBlocked(new Vector3(-player.x, player.y + 1.1f, player.z),
                new Vector3(-enemy.x, enemy.y + 1.1f, enemy.z))) range *= .72f;
            float distance = Vector3.Distance(new Vector3(player.x, player.y, player.z), new Vector3(enemy.x, enemy.y, enemy.z));
            bool audible = enemy.active && enemy.behaviour != "vanished" && distance <= range;
            if (audible) PublishRoleIntel(player, enemy.x, enemy.y, enemy.z, "enemy", 12);
            else { player.roleIntelUntil = 0; player.roleIntelKind = null; }
            // Playback comes from the real recorder. The enemy remembers this
            // position while the journalist can leave quietly behind cover.
            HearNoise(player.x, player.y, player.z, 28, 12);
            State.message = audible ? "ДИКТОФОН: шум Диктора записано. Відтворення відволікає його сюди — відійдіть тихо." :
                "ДИКТОФОН: поряд не чути Диктора. Гучний запис відтворено для відволікання.";
        }

        void UseCompass(PlayerState player)
        {
            var objective = NextCompassObjective(player);
            if (objective == null) { player.roleIntelUntil = 0; player.roleIntelKind = null; State.message = "КОМПАС: завдання завершено."; return; }
            PublishRoleIntel(player, objective.x, objective.y, objective.z, "objective:" + objective.id, 20);
            State.message = "КОМПАС: напрямок — " + objective.label + ".";
        }

        ObjectiveData NextCompassObjective(PlayerState player)
        {
            if (IsBlackpine)
            {
                var chapter = State.blackpine;
                if (chapter.resonatorStopped) return world.Objective("exit");
                if (chapter.intercepted) return DeadAirBlackpine.Objective(world, chapter.antennaDisconnected ? DeadAirBlackpine.ResonatorId : DeadAirBlackpine.AntennaId);
                if (!State.ladderPlaced)
                    return player.HasItem("ladder") || State.Collected("ladder") ? new ObjectiveData { id = "ladder-spot", label = "Вікно директора · встановити драбину", x = -9, y = 1, z = 7 } : world.Objective("ladder");
                if (!State.Collected("key-main")) return world.Objective("key-main");
            }
            if (State.broadcast) return world.Objective("exit");
            if (State.canisters.Count < RequiredFuel && !State.power) return NearestUncollectedObjective(player, "fuel-canister");
            if (!State.power) return world.Objective("power");
            if (State.fuses.Count < 2) return NearestUncollectedObjective(player, "fuse-");
            if (!State.breakerOn) return world.Objective("breaker");
            if (State.tapes.Count < 3) return NearestUncollectedObjective(player, "tape-");
            return world.Objective("broadcast");
        }

        ObjectiveData NearestUncollectedObjective(PlayerState player, string prefix)
        {
            ObjectiveData result = null; float nearest = float.PositiveInfinity;
            if (world.objects == null) return null;
            foreach (var authored in world.objects)
            {
                var obj = IsBlackpine ? DeadAirBlackpine.EvidenceObjective(world, State, authored) : authored;
                if (!obj.id.StartsWith(prefix, StringComparison.Ordinal) || State.Collected(obj.id)) continue;
                float distance = Vector3.SqrMagnitude(new Vector3(obj.x - player.x, (obj.y - player.y) * 2, obj.z - player.z));
                if (distance < nearest) { nearest = distance; result = obj; }
            }
            return result;
        }

        void PublishRoleIntel(PlayerState player, float x, float y, float z, string kind, float seconds)
        {
            player.roleIntelX = x; player.roleIntelY = y; player.roleIntelZ = z;
            player.roleIntelKind = kind; player.roleIntelUntil = State.elapsed + seconds;
        }

        void ClearEnemyAttack()
        {
            State.enemy.attackPlayerId = null; State.enemy.attackStartedAt = -100;
        }

        void DownAttackedPlayer(PlayerState player)
        {
            player.down = true; player.sanity = 0; State.scare++;
            if (CarryingChicken(player)) ReleaseChicken(player);
            if (IsBlackpine && State.blackpine.operationPlayerId == player.id) ClearBlackpineOperation();
            ClearMushroomTrip(player);
            player.camera = player.flashlight = player.sprinting = player.crouching = player.mapOpen = player.uniqueItemEquipped = false;
            player.uniqueActionCancelled = true;
            State.message = player.name + " втратив зв’язок. Підніміть напарника!";
            Dirty = true;
        }
    }
}
