using System;
using System.Linq;
using UnityEngine;

namespace DeadAir
{
    // One instance runs only on the host (also for a solo game). Clients receive snapshots.
    public sealed partial class DeadAirRules
    {
        readonly WorldData world;
        readonly System.Random random = new System.Random();
        readonly Func<double> movementTime;
        sealed class MovementBudget { public double at; public float distance; }
        readonly System.Collections.Generic.Dictionary<string, MovementBudget> movementBudgets = new System.Collections.Generic.Dictionary<string, MovementBudget>();
        public readonly RoomState State = new RoomState();
        float nextScare = 35, nextPublish;
        System.Collections.Generic.List<int>[] navigation;
        readonly System.Collections.Generic.List<int> enemyRoute = new System.Collections.Generic.List<int>();
        int enemyRouteCursor;
        Vector3 routeTarget;
        float nextRouteAttempt;
        readonly System.Collections.Generic.HashSet<string> inspectedLockers = new System.Collections.Generic.HashSet<string>();
        readonly System.Collections.Generic.Dictionary<string, float> spiderCooldowns = new System.Collections.Generic.Dictionary<string, float>();
        float nextPeek = 35, encounterEnds, verificationLockerRoll = -1;
        Vector3 encounterReturnPosition;
        Vector3 patrolDestination;
        float nextPatrol;
        float nextBreakerTrip = 120;
        int breakerTrips;
        public const float BreakerProtectionSeconds = 120, BreakerCheckSeconds = 60;
        public const int MaximumBreakerTrips = 2;
        readonly System.Collections.Generic.Dictionary<string,float> footstepsAt = new System.Collections.Generic.Dictionary<string,float>();
        const float DoorPassageDelay = .6f, SpiderChance = .25f, SpiderCooldown = 45;
        public bool Dirty { get; private set; }

        public DeadAirRules(WorldData world, string hostId, string name, string classId, bool solo, bool creative,
            string chapterId = DeadAirChapters.DefaultId, Func<double> movementTime = null)
        {
            string chapterError = DeadAirChapters.SelectionError(chapterId);
            if (chapterError != null) throw new ArgumentException(chapterError, nameof(chapterId));
            this.world = world;
            this.movementTime = movementTime ?? (() => Time.realtimeSinceStartupAsDouble);
            State.chapterId = chapterId;
            State.code = new string(Enumerable.Range(0, 5).Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[random.Next(32)]).ToArray());
            State.hostId = hostId;
            State.solo = solo;
            State.creative = creative;
            State.spawnCountdown = creative ? 999999 : 35;
            State.message = "Сигнал установлено. Очікуємо на операторів.";
            foreach (var d in world.doors)
                State.doors.Add(new DoorState { id = d.id, locked = !string.IsNullOrEmpty(d.requires), requiredKey = d.requires, openSign = 1f });
            ResetDrawers();
            AddPlayer(hostId, name, classId);
        }

        public bool AddPlayer(string id, string name, string classId)
        {
            if (State.solo || State.players.Count >= 4 || State.phase == "won" || State.phase == "lost")
            {
                if (State.players.Count != 0) return false;
            }
            var cls = world.GetClass(classId);
            int index = State.players.Count;
            var spawn = DeadAirVanPresentation.SpawnPoint(world, index);
            var pState = new PlayerState
            {
                id = id, name = string.IsNullOrWhiteSpace(name) ? "Оператор" : name.Trim().Substring(0, Mathf.Min(20, name.Trim().Length)),
                characterClass = cls.id,
                x = spawn.x, y = spawn.y, z = spawn.z,
                flashlight = true, yaw = DeadAirVanPresentation.SpawnYaw
            };
            pState.inventory.Add("flashlight");
            ResetRoleItem(pState);
            State.players.Add(pState);
            ResetMovementBudget(pState);
            if (State.players.Count > 1) State.paused = false;
            State.message = State.players[index].name + " приєднався до ефіру.";
            Dirty = true;
            return true;
        }

        public void RemovePlayer(string id)
        {
            State.players.RemoveAll(p => p.id == id);
            movementBudgets.Remove(id);
            inspectedLockers.RemoveWhere(key => key.StartsWith(id + "\0", StringComparison.Ordinal));
            spiderCooldowns.Remove(id);
            footstepsAt.Remove(id);
            if (State.hostId == id && State.players.Count > 0) State.hostId = State.players[0].id;
            CheckDefeat();
            Dirty = true;
        }

        public void SetClass(string id, string classId)
        {
            var p = State.Player(id);
            if (State.phase != "lobby" || p == null || !world.classes.Any(c => c.id == classId)) return;
            p.characterClass = classId;
            ResetRoleItem(p);
            Dirty = true;
        }

        static void ResetRoleItem(PlayerState player)
        {
            player.inventory.RemoveAll(DeadAirRoleItems.IsRoleItem);
            var item = DeadAirRoleItems.ForClass(player.characterClass);
            if(item != null)player.inventory.Add(item);
            player.selectedSlot = Mathf.Clamp(player.selectedSlot, 0, Mathf.Max(0, player.inventory.Count - 1));
            player.uniqueItemEquipped = player.mapOpen = false;
            player.uniqueActionSequence = 0;
            player.uniqueActionAt = -100;
            player.uniqueActionCancelled = true;
            player.cigarettesRemaining = player.characterClass == "operator" ? 12 : 0;
            player.roleReadyAt = player.roleIntelUntil = 0;
            player.roleIntelKind = null;
        }

        bool CanUseRoleItem(PlayerState player)
        {
            return State.phase == "playing" && !State.paused && player != null &&
                !player.down && !player.ladderClimbing && string.IsNullOrEmpty(player.hiding);
        }

        public void EquipUnique(string id, bool equipped)
        {
            var player = State.Player(id);
            if (!CanUseRoleItem(player)) return;
            // Compatibility for older captures/peers: use the same inventory slot path.
            int slot=equipped?player.inventory.IndexOf(DeadAirRoleItems.ForClass(player.characterClass)):0;
            if(slot>=0)SelectSlot(id,slot);
        }

        public void EquipMap(string id, bool open)
        {
            var player = State.Player(id);
            // A modal map must publish its equipment state even while a solo round is paused.
            if (State.phase != "playing" || player == null || player.down || player.ladderClimbing || !string.IsNullOrEmpty(player.hiding)) return;
            if (open && !CanReadVanMap(player)) return;
            player.mapOpen = open;
            if (open)
            {
                player.camera = player.uniqueItemEquipped = player.flashlight = false;
                player.uniqueActionCancelled = true;
            }
            else player.uniqueItemEquipped = player.HasSelectedRoleItem && !player.camera;
            Dirty = true;
        }

        public void SetClothing(string id, int mask)
        {
            var player = State.Player(id);
            if (State.phase != "lobby" || player == null) return;
            player.clothingMask = DeadAirClothing.Sanitize(mask);
            Dirty = true;
        }

        bool CanReadVanMap(PlayerState player)
        {
            var board = world.Objective("van-map");
            return board != null && player != null && !player.down && !player.ladderClimbing &&
                string.IsNullOrEmpty(player.hiding) && DeadAirVanPresentation.IsInside(world, player.x, player.z) &&
                Near(player, board.x, board.y, board.z, 2.5f, 0, "van-map");
        }

        public string UseUnique(string id)
        {
            var player = State.Player(id);
            if (!CanUseRoleItem(player)) return "Особистий предмет зараз недоступний.";
            if (!player.uniqueItemEquipped || !player.HasSelectedRoleItem || player.camera || player.mapOpen)
                return "Оберіть особистий предмет в інвентарі.";
            bool cigarettes = player.characterClass == "operator";
            if (cigarettes && player.cigarettesRemaining <= 0) return "Пачка сигарет порожня.";
            float cooldown = DeadAirRoleAbilities.Cooldown(player.characterClass);
            if (State.elapsed < player.roleReadyAt || State.elapsed - player.uniqueActionAt < cooldown)
                return "Особистий предмет буде готовий через " + Mathf.CeilToInt(Mathf.Max(player.roleReadyAt - State.elapsed,
                    cooldown - (State.elapsed - player.uniqueActionAt))) + " с.";
            if (cigarettes) player.cigarettesRemaining--;
            player.uniqueActionAt = State.elapsed;
            player.roleReadyAt = State.elapsed + cooldown;
            player.uniqueActionSequence++;
            player.uniqueActionCancelled = false;
            if (player.characterClass == "engineer")
            {
                bool worked = UseEngineerTool(player);
                HearNoise(player.x, player.y, player.z, worked ? 14 : 4, worked ? 8 : 3);
            }
            else if (player.characterClass == "journalist") UseRecorder(player);
            else if (player.characterClass == "dispatcher") UseCompass(player);
            Dirty = true;
            return null;
        }

        public string SelectChapter(string id, string chapterId)
        {
            if (id != State.hostId) return "Главу обирає господар сеансу.";
            if (State.phase != "lobby") return "Главу можна змінити лише перед початком сеансу.";
            string error = DeadAirChapters.SelectionError(chapterId);
            if (error != null) return error;
            State.chapterId = chapterId;
            Dirty = true;
            return null;
        }

        public string Start(string id, string chapterId = null)
        {
            if (id != State.hostId) return "Сеанс починає господар кімнати.";
            if (State.phase != "lobby") return "Сеанс уже розпочато.";
            if (chapterId != null)
            {
                string selectionError = SelectChapter(id, chapterId);
                if (selectionError != null) return selectionError;
            }
            string error = DeadAirChapters.SelectionError(State.chapterId);
            if (error != null) return error;
            Reset();
            return null;
        }

        public string Restart(string id)
        {
            if (id != State.hostId) return "Сеанс повторює господар кімнати.";
            if (State.phase != "won" && State.phase != "lost") return "Сеанс ще не завершено.";
            string error = DeadAirChapters.SelectionError(State.chapterId);
            if (error != null) return error;
            Reset();
            return null;
        }

        void Reset()
        {
            State.phase = "playing";
            State.paused = false;
            State.tapes.Clear(); State.fuses.Clear(); State.keycards.Clear(); State.canisters.Clear();
            State.collected.Clear();
            State.securedItems.Clear();
            State.droppedItems.Clear();
            ResetDrawers();
            State.canister = State.valveOpened = State.calibrated = State.power = State.broadcast = false;
            State.ladderPlaced = State.breakerOn = State.directorDrawerOpen = false;
            State.windowBroken = State.quest.windowBroken = true;
            State.quest = new QuestState();
            State.enemy = new EnemyState { x = 0, y = 0, z = -34 };
            enemyRoute.Clear();enemyRouteCursor=0;nextRouteAttempt=0;
            inspectedLockers.Clear();spiderCooldowns.Clear();footstepsAt.Clear();nextPeek=35;encounterEnds=0;nextPatrol=0;nextBreakerTrip=BreakerProtectionSeconds;breakerTrips=0;
            foreach (var door in State.doors) { door.open = false; door.openedAt=-100; door.openSign = 1f; door.locked = !string.IsNullOrEmpty(door.requiredKey); }
            State.elapsed = 0;
            State.departureStartedAt = 0;
            State.scare = 0;
            State.radioSequence = 0;
            State.radioSenderId = null;
            bool isCh2 = DeadAirChapters.IsChapter2(State.chapterId);
            State.spawnCountdown = State.creative ? 999999 : isCh2 ? 10 : 35;
            State.message = State.creative ? "РЕЖИМ ДОСЛІДЖЕННЯ: загроза пасивна." :
                isCh2 ? "ЯПОНСЬКА ШКОЛА: Дослідіть школу й парк. Шукайте пігулки для розсудку!" :
                "Група прибула. До прориву сигналу 35 секунд.";
            if (isCh2) State.enemy = new EnemyState { x = 0, y = 0.1f, z = -10f };
            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                if (isCh2)
                {
                    p.x = 0; p.y = 0.5f; p.z = 36f;
                    p.yaw = Mathf.PI; p.pitch = 0;
                }
                else
                {
                    var spawn = DeadAirVanPresentation.SpawnPoint(world, i);
                    p.x = spawn.x; p.y = spawn.y; p.z = spawn.z;
                    p.yaw = DeadAirVanPresentation.SpawnYaw; p.pitch = 0;
                }
                p.sanity = p.stamina = 100;
                p.down = p.sprinting = p.crouching = p.camera = p.ladderClimbing = false;
                p.inventory.Clear();
                p.inventory.Add("flashlight");
                p.selectedSlot = 0;
                p.flashlight = true;
                p.flashlightCharge = DeadAirSupplies.FlashlightSeconds;
                p.spareBatteries = p.coins = p.flashlightReloadSerial = 0;
                p.hiding = null;
                p.invulnerableUntil = 8;
                ResetRoleItem(p);
                p.moveSequence = 0;
                p.spiderScareSerial=p.spiderScareAt=0;
                p.mushroomTripStartedAt=p.mushroomTripUntil=0;
                p.mushroomTripSerial=0;
                ResetMovementBudget(p);
            }
            nextScare = 35;
            Dirty = true;
        }

        public void Pause(string id, bool paused)
        {
            if (id != State.hostId || State.phase != "playing" || State.players.Count != 1) return;
            State.paused = paused;
            Dirty = true;
        }

        public string ExecuteCommand(string playerId, string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return null;
            var parts = commandLine.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            string cmd = parts[0].ToLowerInvariant();
            var p = State.Player(playerId);
            if (p == null) return "Гравця не знайдено";

            if (cmd == "noclip" || cmd == "nc")
            {
                p.noclip = !p.noclip;
                Dirty = true;
                return p.noclip ? "Noclip УВІМКНЕНО (WASD для польоту, Space = вгору, Ctrl/C = вниз, Shift = прискорення)" : "Noclip ВИМКНЕНО";
            }
            if (cmd == "give")
            {
                if (parts.Length < 2) return "Використання: give <ladder|key-main|key-basement|fuel|fuse|all>";
                string item = parts[1].ToLowerInvariant();
                if (item == "all")
                {
                    if (!p.inventory.Contains("ladder")) p.inventory.Add("ladder");
                    if (!p.inventory.Contains("key-main")) p.inventory.Add("key-main");
                    if (!p.inventory.Contains("key-basement")) p.inventory.Add("key-basement");
                    if (!p.inventory.Contains("keycard-guard")) p.inventory.Add("keycard-guard");
                    if (!p.inventory.Contains("keycard-engineer")) p.inventory.Add("keycard-engineer");
                    if (!p.inventory.Contains("fuel-canister")) p.inventory.Add("fuel-canister");
                    if (!p.inventory.Contains("fuel-canister-2")) p.inventory.Add("fuel-canister-2");
                    if (!p.inventory.Contains("fuel-canister-3")) p.inventory.Add("fuel-canister-3");
                    if (!p.inventory.Contains("fuse-1")) p.inventory.Add("fuse-1");
                    if (!p.inventory.Contains("fuse-2")) p.inventory.Add("fuse-2");
                    State.collected.Add("ladder");
                    State.collected.Add("key-main");
                    State.collected.Add("key-basement");
                    if (!State.canisters.Contains("fuel-canister")) State.canisters.Add("fuel-canister");
                    if (!State.canisters.Contains("fuel-canister-2")) State.canisters.Add("fuel-canister-2");
                    if (!State.canisters.Contains("fuel-canister-3")) State.canisters.Add("fuel-canister-3");
                    if (!State.fuses.Contains("fuse-1")) State.fuses.Add("fuse-1");
                    if (!State.fuses.Contains("fuse-2")) State.fuses.Add("fuse-2");
                    State.canister = State.quest.fuelAdded = true;
                    Dirty = true;
                    return "Видано всі ключі, драбину, запобіжники та дизельне пальне!";
                }
                if (item == "ladder" || item == "драбина")
                {
                    if (!p.inventory.Contains("ladder")) p.inventory.Add("ladder");
                    State.collected.Add("ladder");
                    Dirty = true;
                    return "Драбину додано в інвентар!";
                }
                if (item == "key-main" || item == "key_main" || item == "mainkey" || item == "key")
                {
                    if (!p.inventory.Contains("key-main")) p.inventory.Add("key-main");
                    State.collected.Add("key-main");
                    Dirty = true;
                    return "Головний ключ станції додано в інвентар!";
                }
                if (item == "key-basement" || item == "key_basement" || item == "basementkey")
                {
                    if (!p.inventory.Contains("key-basement")) p.inventory.Add("key-basement");
                    State.collected.Add("key-basement");
                    Dirty = true;
                    return "Ключ від підвалу додано в інвентар!";
                }
                if (item == "fuel" || item == "canister" || item == "fuel-canister" || item == "паливо" || item == "каністра")
                {
                    if (!p.inventory.Contains("fuel-canister")) p.inventory.Add("fuel-canister");
                    if (!p.inventory.Contains("fuel-canister-2")) p.inventory.Add("fuel-canister-2");
                    if (!p.inventory.Contains("fuel-canister-3")) p.inventory.Add("fuel-canister-3");
                    if (!State.canisters.Contains("fuel-canister")) State.canisters.Add("fuel-canister");
                    if (!State.canisters.Contains("fuel-canister-2")) State.canisters.Add("fuel-canister-2");
                    if (!State.canisters.Contains("fuel-canister-3")) State.canisters.Add("fuel-canister-3");
                    State.canister = State.quest.fuelAdded = true;
                    Dirty = true;
                    return "3 каністри дизельного палива додано в інвентар!";
                }
                if (item == "fuse" || item == "fuses" || item == "запобіжник")
                {
                    if (!p.inventory.Contains("fuse-1")) p.inventory.Add("fuse-1");
                    if (!p.inventory.Contains("fuse-2")) p.inventory.Add("fuse-2");
                    if (!State.fuses.Contains("fuse-1")) State.fuses.Add("fuse-1");
                    if (!State.fuses.Contains("fuse-2")) State.fuses.Add("fuse-2");
                    Dirty = true;
                    return "2 запобіжники 380В додано в інвентар!";
                }
                if (!p.inventory.Contains(item)) p.inventory.Add(item);
                State.collected.Add(item);
                Dirty = true;
                return "Предмет '" + item + "' додано в інвентар";
            }
            if (cmd == "tp" || cmd == "teleport")
            {
                if (parts.Length < 2) return "Використання: tp <director|basement|van|front|generator>";
                string target = parts[1].ToLowerInvariant();
                if (target == "director") { p.x = -12f; p.y = 4.2f; p.z = -1.5f; }
                else if (target == "basement") { p.x = 0f; p.y = -4.2f; p.z = -54f; }
                else if (target == "van") { p.x = -8.5f; p.y = 0f; p.z = 24f; }
                else if (target == "front") { p.x = 0f; p.y = 0f; p.z = 12f; }
                else if (target == "generator") { p.x = -11f; p.y = 0f; p.z = -65f; }
                else if (parts.Length >= 4 && float.TryParse(parts[1], out float tx) && float.TryParse(parts[2], out float ty) && float.TryParse(parts[3], out float tz))
                {
                    p.x = tx; p.y = ty; p.z = tz;
                }
                else return "Невідома локація для телепортації";
                p.yaw = 0; p.pitch = 0;
                Dirty = true;
                return "Телепортовано до: " + target;
            }
            if (cmd == "power")
            {
                State.power = State.quest.power = !State.power;
                Dirty = true;
                return State.power ? "Генератор увімкнено!" : "Генератор вимкнено";
            }
            if (cmd == "breaker")
            {
                State.breakerOn = State.quest.breakerOn = !State.breakerOn;
                if (State.breakerOn) ProtectBreaker();
                Dirty = true;
                return State.breakerOn ? "Рубильник увімкнено (світло працює)!" : "Рубильник вимкнено";
            }
            if (cmd == "heal" || cmd == "god")
            {
                p.sanity = p.stamina = 100;
                p.down = false;
                p.invulnerableUntil = State.elapsed + 999999f;
                Dirty = true;
                return "Повне здоров'я, розсудок та безсмертя активовано!";
            }
            if (cmd == "help")
            {
                return "Доступні команди:\n- noclip / nc — режим польоту крізь стіни\n- tp <director|basement|van|front|generator> — телепортація\n- give <ladder|key-main|key-basement|all> — видати предмети\n- power — перемкнути живлення генератора\n- breaker — перемкнути рубильник світла\n- heal — відновити розсудок та стаміну\n- clear — очистити екран консолі";
            }
            return "Невідома команда '" + cmd + "'. Введіть 'help' для списку команд.";
        }

        public void Move(string id, NetPacket input)
        {
            if (State.phase != "playing" || State.paused || State.elapsed < DeadAirVanPresentation.ArrivalDuration) return;
            var p = State.Player(id);
            if (p == null || p.down) return;
            if(input.moveSequence>0)
            {
                if(input.moveSequence<=p.moveSequence)return;
                // Acknowledge rejected inputs too, so prediction can reconcile to the
                // authoritative position without mistaking network latency for an error.
                p.moveSequence=input.moveSequence;
                Dirty=true;
            }
            if (p.ladderClimbing) return;
            if (float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y) ||
                float.IsNaN(input.z) || float.IsInfinity(input.z) ||
                float.IsNaN(input.yaw) || float.IsInfinity(input.yaw) ||
                float.IsNaN(input.pitch) || float.IsInfinity(input.pitch)) return;
            if (Mathf.Abs(input.x) > 100 || Mathf.Abs(input.z) > 100 || input.y < -15f || input.y > 35) return;
            p.noclip = input.noclip;
            p.crouching = input.crouching && string.IsNullOrEmpty(p.hiding);
            p.sprinting = input.sprinting && !p.crouching && (State.creative || p.stamina > 2) && string.IsNullOrEmpty(p.hiding);
            if (string.IsNullOrEmpty(p.hiding))
            {
                var delta = Vector3.Distance(new Vector3(p.x, p.y, p.z), new Vector3(input.x, input.y, input.z));
                float moveSeconds=movementBudgets.TryGetValue(id,out var lastBudget)?Mathf.Max(.04f,(float)(movementTime()-lastBudget.at)):.1f;
                if (!p.noclip && (!MovementWithinBudget(p, delta) || !Walkable(input.x, input.y, input.z) ||
                    SegmentBlocked(p.x, p.y, p.z, input.x, input.y, input.z))) return;
                if (movementBudgets.TryGetValue(id, out var budget))
                    budget.distance = Mathf.Max(0, budget.distance - delta);
                p.x = input.x; p.y = input.y; p.z = input.z;
                p.yaw = input.yaw; p.pitch = Mathf.Clamp(input.pitch, -1.35f, 1.35f);
                if(delta>.025f&&(!footstepsAt.TryGetValue(id,out float footstepAt)||State.elapsed>=footstepAt))
                {
                    // monsterHearingRange is the operator's listening perk; using it
                    // here would accidentally make the dispatcher louder to enemies.
                    float radius = p.crouching ? 1.2f : p.sprinting ? 22 : 6;
                    HearNoise(p.x,p.y,p.z,radius,p.sprinting?8:p.crouching?2:5);
                    footstepsAt[id]=State.elapsed+(p.sprinting ? .32f : p.crouching ? .75f : .55f);
                }
            }
            else
            {
                var locker = world.Locker(p.hiding);
                float centerYaw = locker != null ? locker.FacingYaw : p.yaw;
                p.yaw = centerYaw + Mathf.Clamp(Mathf.DeltaAngle(centerYaw * Mathf.Rad2Deg, input.yaw * Mathf.Rad2Deg), -18, 18) * Mathf.Deg2Rad;
                p.pitch = Mathf.Clamp(input.pitch, -14 * Mathf.Deg2Rad, 14 * Mathf.Deg2Rad);
            }
            p.flashlight = input.flashlight && string.IsNullOrEmpty(p.hiding) && DeadAirSupplies.HasCharge(p) &&
                (p.SelectedItem == "flashlight" || input.camera);
            p.camera = input.camera && string.IsNullOrEmpty(p.hiding);
            p.mapOpen = input.mapOpen && CanReadVanMap(p);
            if (p.mapOpen) p.camera = p.flashlight = false;
            if (p.mapOpen || p.camera || !string.IsNullOrEmpty(p.hiding))
            {
                p.uniqueItemEquipped = false;
                p.uniqueActionCancelled = true;
            }
            else p.uniqueItemEquipped = p.HasSelectedRoleItem;
        }

        float MaximumMovementSpeed(PlayerState actor) => (actor.crouching ? DeadAirLocomotion.CrouchSpeed :
            actor.sprinting ? DeadAirLocomotion.RunSpeed * world.GetClass(actor.characterClass).bonuses.speedMultiplier : DeadAirLocomotion.WalkSpeed) * 1.12f;

        void ResetMovementBudget(PlayerState actor)
        {
            movementBudgets[actor.id]=new MovementBudget{at=movementTime(),distance=MaximumMovementSpeed(actor)*.2f+.15f};
        }

        bool MovementWithinBudget(PlayerState actor,float distance)
        {
            if(!movementBudgets.TryGetValue(actor.id,out var budget)){ResetMovementBudget(actor);budget=movementBudgets[actor.id];}
            double now=movementTime();
            float speed=MaximumMovementSpeed(actor);
            // Credit covers several missed render/network frames, but repeated packets
            // cannot buy another fixed-size teleport. Walls and floor checks still apply.
            budget.distance=Mathf.Min(speed*.75f+.2f,budget.distance+speed*(float)Math.Max(0,now-budget.at));
            budget.at=now;
            return distance<=budget.distance+.001f;
        }

        bool Walkable(float x, float y, float z)
        {
            if (DeadAirChapters.IsChapter2(State.chapterId))
                return x >= -45f && x <= 45f && z >= -25f && z <= 100f && y >= -1f && y <= 6f;
            if (x < world.courtyard.minX + .2f || x > world.courtyard.maxX - .2f ||
                z > world.courtyard.maxZ - .2f || z < world.courtyard.minZ + .2f) return false;
            if (DeadAirVanPresentation.TryFloorHeight(world, x, z, out float vanFloor) && Mathf.Abs(y - vanFloor) <= .28f) return true;
            if (world.stairs != null)
                foreach (var stair in world.stairs)
                    if (stair.Contains(x, z, .12f) && Mathf.Abs(y - stair.HeightAt(z)) <= .55f) return true;
            if (Mathf.Abs(y) <= .25f) return true;
            foreach (var room in world.rooms)
                if (Mathf.Abs(y - room.floorY) <= .45f && x >= room.minX && x <= room.maxX &&
                    z >= room.minZ && z <= room.maxZ) return true;
            return false;
        }

        bool SegmentBlocked(float ax, float ay, float az, float bx, float by, float bz)
        {
            // The host sees the same imported collision geometry as its local controller.
            // A torso-height ray catches walls even if a packet skips across their thin collider.
            if (PhysicalSegmentBlocked(new Vector3(-ax, ay + .75f, az), new Vector3(-bx, by + .75f, bz), true)) return true;
            foreach (var door in world.doors)
            {
                if (State.Door(door.id)?.open == true || Mathf.Abs((ay + by) * .5f + 1.4f - door.y) > 1) continue;
                var normal = new Vector2(Mathf.Cos(door.rotation), -Mathf.Sin(door.rotation));
                var tangent = new Vector2(-normal.y, normal.x);
                var a = new Vector2(ax - door.x, az - door.z);
                var b = new Vector2(bx - door.x, bz - door.z);
                float da = Vector2.Dot(a, normal), db = Vector2.Dot(b, normal);
                if (da * db > 0 || Mathf.Abs(da - db) < .0001f) continue;
                var crossing = Vector2.Lerp(a, b, da / (da - db));
                if (Mathf.Abs(Vector2.Dot(crossing, tangent)) < door.w * .5f + .2f) return true;
            }
            return false;
        }

        static readonly RaycastHit[] movementHits = new RaycastHit[128];
        static bool PhysicalSegmentBlocked(Vector3 origin, Vector3 target, bool ignoreDoors=false)
        {
            Vector3 offset=target-origin;
            if(offset.sqrMagnitude<.000001f)return false;
            int count=Physics.RaycastNonAlloc(origin,offset.normalized,movementHits,offset.magnitude,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var collider=movementHits[i].collider;
                // The locally predicted capsule can be at the end of this segment.
                // It is an actor, never an office wall or a movement blocker for itself.
                if(collider is CharacterController || collider.GetComponentInParent<DeadAirPlayer>()!=null ||
                    collider.GetComponentInParent<DeadAirCharacterMotion>()!=null)continue;
                if(collider.name.StartsWith("Stair tread") || collider.name.StartsWith("Stair riser") || collider.name.StartsWith("Stair side wall") || collider.name.StartsWith("Stair handrail"))continue;
                if(ignoreDoors)
                {
                    bool door=false;
                    for(var part=collider.transform;part!=null;part=part.parent)
                        if(part.name.StartsWith("door:") || part.name.StartsWith("Door frame") || part.name == "Door leaf")
                        {
                            door=true;break;
                        }
                    if(door)continue;
                }
                return true;
            }
            return count==movementHits.Length;
        }

        static float Dist(float ax, float az, float bx, float bz) => Vector2.Distance(new Vector2(ax, az), new Vector2(bx, bz));
        static bool Near(PlayerState player, float x, float y, float z, float range, float targetEyeOffset = 0, string targetId = null) =>
            Vector3.Distance(new Vector3(player.x, player.y + 1, player.z), new Vector3(x, y + targetEyeOffset, z)) <= range &&
            HasInteractionSightline(new Vector3(-player.x, player.y + 1.4f, player.z), new Vector3(-x, y + targetEyeOffset, z), targetId);
        static bool SameFloor(float a, float b) => Mathf.Abs(a - b) < 1;
        static readonly RaycastHit[] sightHits = new RaycastHit[32];

        public static bool HasInteractionSightline(Vector3 origin, Vector3 target, string targetId)
        {
            if (targetId == "ladder-climb" || targetId == "ladder-spot" || targetId == "drawer-director" || targetId == "ladder" || targetId == "key-main") return true;
            var offset = target - origin;
            if (offset.sqrMagnitude < .0001f) return true;
            int count = Physics.RaycastNonAlloc(origin, offset.normalized, sightHits, offset.magnitude,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                bool barrier = false, isTarget = false;
                for (var part = sightHits[i].collider.transform; part != null; part = part.parent)
                {
                    if (part.name == "shell" || part.name == "Van stationary collision shell" || part.name.StartsWith("door:") || part.name.StartsWith("Door frame ")) barrier = true;
                    if (!string.IsNullOrEmpty(targetId) && (part.name == "door:" + targetId || part.name == "Door frame " + targetId)) isTarget = true;
                }
                if (barrier && !isTarget) return false;
            }
            return true;
        }

        public string Interact(string id, string objectId, float frequency = 0)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || State.paused || p == null || p.down) return "Ефір не активний.";
            if (p.ladderClimbing) return "Дочекайтеся завершення підйому драбиною.";
            if (string.IsNullOrEmpty(objectId)) return "Невідомий предмет.";
            if (objectId == "van-map")
            {
                if (!CanReadVanMap(p)) return "Карта висить у фургоні. Підійдіть до неї.";
                EquipMap(id, true);
                return null;
            }
            if (objectId == "exit-locker")
            {
                if (!string.IsNullOrEmpty(p.hiding)) ExitLocker(p);
                return null;
            }
            if (!string.IsNullOrEmpty(p.hiding)) return "Спочатку вийдіть із шафи.";
            if (objectId.StartsWith("drawer", StringComparison.Ordinal)) return InteractDrawer(p, objectId);
            if (objectId.StartsWith("van-battery-", StringComparison.Ordinal)) return CollectVanBattery(p, objectId);
            if (objectId.StartsWith("pill-", StringComparison.Ordinal)) return TakePill(p, objectId);
            if (objectId.StartsWith("mushroom-", StringComparison.Ordinal))
                return EatMushroom(p, objectId);
            if (objectId.StartsWith("dropped:"))
            {
                string droppedId = objectId.Substring(8);
                var dropped = State.droppedItems.Find(d => d.id == droppedId);
                if (dropped == null) return "Предмет уже забрали.";
                if (!Near(p, dropped.x, dropped.y, dropped.z, 2.5f, 1)) return "Підійдіть ближче.";
                string actualItem = string.IsNullOrEmpty(dropped.itemId) ? dropped.id : dropped.itemId;
                if (actualItem == "flashlight" && p.HasItem("flashlight")) return "Ви вже маєте ліхтар.";
                State.droppedItems.Remove(dropped);
                if (actualItem == DeadAirSupplies.BatteryItem) AddBattery(p);
                else if (!p.inventory.Contains(actualItem)) p.inventory.Add(actualItem);
                if (actualItem == "flashlight") { p.flashlightCharge = dropped.flashlightCharge; p.flashlight = DeadAirSupplies.HasCharge(p); }
                if (actualItem.StartsWith("fuse-") && !State.fuses.Contains(actualItem)) State.fuses.Add(actualItem);
                if (actualItem.StartsWith("tape-") && !State.tapes.Contains(actualItem)) State.tapes.Add(actualItem);
                if (actualItem.StartsWith("keycard-") && !State.keycards.Contains(actualItem)) State.keycards.Add(actualItem);
                if (actualItem.StartsWith("fuel-canister") && !State.canisters.Contains(actualItem))
                {
                    State.canisters.Add(actualItem);
                    State.canister = State.quest.fuelAdded = State.canisters.Count >= 3;
                }
                State.message = p.name + " підібрав: " + dropped.label;
                Dirty = true;
                return null;
            }
            if (objectId.StartsWith("revive:"))
            {
                var other = State.Player(objectId.Substring(7));
                if (other == null || !other.down || !Near(p, other.x, other.y, other.z, 2, 1)) return "Підійдіть ближче до оператора.";
                other.down = false; other.sanity = Mathf.Max(40, other.sanity); other.invulnerableUntil = State.elapsed + 4;
                State.message = other.name + " знову в ефірі."; Dirty = true; return null;
            }
            var locker = world.Locker(objectId);
            if (locker != null)
            {
                if (p.hiding == objectId) ExitLocker(p);
                else if (Near(p, locker.x, locker.y, locker.z, 2.2f, 1, locker.id))
                {
                    if (State.players.Any(other => other != p && other.hiding == objectId)) return "Шафа вже зайнята.";
                    p.hiding = objectId; p.x = locker.x; p.y = locker.y; p.z = locker.z;
                    p.yaw = locker.FacingYaw; p.pitch = 0;
                    p.flashlight = p.camera = p.sprinting = p.mapOpen = p.uniqueItemEquipped = false;
                    p.uniqueActionCancelled = true;
                    State.message = p.name + " сховався в шафу."; Dirty = true;
                    TryLockerSpider(p, objectId);
                    HearNoise(locker.exitX,locker.ExitFloorY,locker.exitZ,9,5);
                }
                else return "Підійдіть ближче до шафи.";
                return null;
            }
            var doorData = world.Door(objectId);
            if (doorData != null)
            {
                if (!Near(p, doorData.x, doorData.y, doorData.z, 2.5f, 0, doorData.id)) return "Підійдіть ближче.";
                var door = State.Door(objectId);
                if (door.locked)
                {
                    if (string.IsNullOrEmpty(door.requiredKey) || p.HasItem(door.requiredKey))
                    {
                        door.locked = false;
                    }
                    else
                    {
                        var reqObj = world.Objective(door.requiredKey);
                        return "Потрібен ключ або картка" + (reqObj != null ? " (" + reqObj.label + ")" : "") + ".";
                    }
                }
                door.open = !door.open;
                if(door.open)
                {
                    door.openedAt=State.elapsed;
                    door.openSign = DeadAirHingedDoor.OpeningSign(doorData, p);
                }
                HearNoise(doorData.x,doorData.y-1.4f,doorData.z,18,7);
                State.message = doorData.label + (door.open ? ": відчинено." : ": зачинено.");
                Dirty = true; return null;
            }
            if (objectId == "ladder-spot")
            {
                if (!Near(p, -9f, 0f, 7f, 3.5f, 0, "ladder-spot")) return "Підійдіть до вікна зовні.";
                if (!p.HasItem("ladder")) return "Потрібна драбина. Перевірте фургон знімальної групи.";
                p.inventory.Remove("ladder");
                State.ladderPlaced = State.quest.ladderPlaced = true;
                State.message = "Драбину встановлено під вікном директора!";
                Dirty = true; return null;
            }
            if (objectId == "ladder-climb")
            {
                if (!State.ladderPlaced) return "Спочатку встановіть драбину.";
                if (!Near(p, -9f, 0f, 7f, 3.5f, 0, "ladder-climb") && !Near(p, -9f, 4.2f, 5.5f, 3.5f, 0, "ladder-climb")) return "Підійдіть до драбини.";
                bool up = Mathf.Abs(p.y) < .75f && p.z >= 6.3f;
                bool down = Mathf.Abs(p.y - 4.2f) < .75f && p.z <= 5.8f && State.windowBroken;
                if (!up && !down) return "Підійдіть до нижнього або верхнього краю драбини.";
                DeadAirLadder.Begin(p, State, up);
                State.message = up ? "Підіймаєтеся до вікна директора." : "Спускаєтеся на передній двір.";
                Dirty = true; return null;
            }
            var obj = world.Objective(objectId);
            if (obj == null) return "Невідомий предмет.";
            float objRange = obj.id == "ladder" ? 3.5f : 2.5f;
            float objEyeOffset = obj.id == "ladder" ? 0.9f : 0f;
            if (!Near(p, obj.x, obj.y, obj.z, objRange, objEyeOffset, obj.id)) return "Підійдіть ближче.";
            if (State.Collected(objectId)) return null;
            if (objectId == DeadAirPrisonBook.Id)
            {
                p.inventory.Add(DeadAirPrisonBook.Id);
                State.collected.Add(objectId);
                State.message = "Книгу взято. Оберіть її в інвентарі та натисніть R, щоб прочитати.";
                Dirty = true; return null;
            }
            if (objectId == "ladder")
            {
                if (!p.inventory.Contains("ladder")) p.inventory.Add("ladder");
                State.collected.Add("ladder");
                State.message = "Складну драбину взято. Встановіть її під вікном кабінету директора.";
                Dirty = true; return null;
            }
            if (objectId == "key-main")
            {
                if (!State.directorDrawerOpen) return "Ящик столу зачинений. Спочатку відкрийте ящик.";
                if (!p.inventory.Contains("key-main")) p.inventory.Add("key-main");
                State.collected.Add("key-main");
                State.message = "Головну картку доступу станції отримано! Тепер можна відчинити головний та задній входи.";
                Dirty = true; return null;
            }
            if (objectId == "key-basement")
            {
                if (!p.inventory.Contains("key-basement")) p.inventory.Add("key-basement");
                State.collected.Add("key-basement");
                State.message = "Картку підвалу отримано!";
                Dirty = true; return null;
            }
            if (objectId == "breaker")
            {
                if (!State.power) return "Немає живлення. Спочатку заправте та увімкніть дизельний генератор у дворі.";
                if (State.fuses.Count < 2) return "У щитку відсутні запобіжники 380В! Знайдіть 2 запобіжники (" + State.fuses.Count + "/2), щоб увімкнути світло.";
                State.breakerOn = State.quest.breakerOn = !State.breakerOn;
                if (State.breakerOn) ProtectBreaker();
                HearNoise(obj.x, obj.y, obj.z, 22, 6);
                State.message = State.breakerOn ? "Запобіжники встановлено, рубильник увімкнено! Освітлення станції та підвалу працює." : "Рубильник вимкнено. Світло згасло.";
                Dirty = true; return null;
            }
            if (objectId.StartsWith("keycard-"))
            {
                State.keycards.Add(objectId);
                if (!p.inventory.Contains(objectId)) p.inventory.Add(objectId);
                State.message = "Картку доступу (" + obj.label + ") отримано."; State.scare++;
            }
            else if (objectId.StartsWith("tape-"))
            {
                State.tapes.Add(objectId);
                if (!p.inventory.Contains(objectId)) p.inventory.Add(objectId);
                State.scare++;
                if (!State.creative) { State.enemy.active = true; State.spawnCountdown = Mathf.Min(3, State.spawnCountdown); }
                HearNoise(obj.x,p.y,obj.z,50,15);
                State.message = "КАСЕТУ ЗНАЙДЕНО (" + State.tapes.Count + "/3). Диктор чує ваші голоси.";
            }
            else if (objectId.StartsWith("fuse-"))
            {
                State.fuses.Add(objectId);
                if (!p.inventory.Contains(objectId)) p.inventory.Add(objectId);
                State.scare++;
                State.message = "ЗАПОБІЖНИК 380В ЗНАЙДЕНО (" + State.fuses.Count + "/2).";
            }
            else if (objectId.StartsWith("fuel-canister"))
            {
                if (!State.canisters.Contains(objectId)) State.canisters.Add(objectId);
                if (!p.inventory.Contains(objectId)) p.inventory.Add(objectId);
                State.canister = State.quest.fuelAdded = State.canisters.Count >= 3;
                State.message = "Каністру з дизелем знайдено (" + State.canisters.Count + "/3).";
            }
            else if (objectId == "pressure-valve") { State.valveOpened = State.quest.valveOpened = true; State.message = "Вентиль тиску відкрито."; }
            else if (objectId == "power")
            {
                if (State.canisters.Count < 3) return "Для запуску генератора потрібно 3 каністри дизельного палива (" + State.canisters.Count + "/3).";
                State.power = State.quest.power = true;
                State.message = "Генератор заправлено дизелем та успішно запущено!";
            }
            else if (objectId == "broadcast")
            {
                if (State.tapes.Count < 3) return "Для передачі потрібні всі три касети.";
                if (!State.power) return "Спочатку відновіть живлення.";
                if (!State.calibrated) return "Налаштуйте частоту 96.4 МГц.";
                State.broadcast = State.quest.broadcast = true;
                State.enemy.active = !State.creative; State.enemy.bloody = true;
                State.enemy.behaviour="pursuit";State.enemy.doorId=State.enemy.watchingPlayerId=null;
                enemyRoute.Clear();enemyRouteCursor=0;
                State.enemy.x = 0; State.enemy.y = p.y; State.enemy.z = -22;
                HearNoise(obj.x,p.y,obj.z,60,20);
                p.invulnerableUntil = State.elapsed + 5;
                State.message = "ЗАПИС У ЕФІРІ. ПОВЕРТАЙТЕСЯ ДО ФУРГОНА!";
            }
            else if (objectId == "exit")
            {
                if (!State.broadcast) return "Передайте сигнал перед від’їздом.";
                foreach (var crew in State.players)
                    if (!crew.down && !DeadAirVanPresentation.IsInside(world, crew.x, crew.z, -2.2f))
                        return "Зберіть усіх живих учасників біля фургона перед від’їздом.";
                State.phase = "departing"; State.departureStartedAt = State.elapsed; State.enemy.active = false;
                for (int i = 0; i < State.players.Count; i++)
                {
                    var crew = State.players[i];
                    var seat = DeadAirVanPresentation.SpawnPoint(world, i);
                    crew.x = seat.x; crew.y = seat.y; crew.z = seat.z;
                    crew.sprinting = crew.crouching = crew.camera = crew.mapOpen = crew.flashlight = false;
                    if (!crew.down) RecordReturnedLoot(crew);
                }
                foreach(var player in State.players)ClearMushroomTrip(player);
                State.message = "Ви вийшли. На записі чути на один голос більше.";
            }
            // Rejected objective interactions must remain available for a later retry.
            if (!State.collected.Contains(objectId)) State.collected.Add(objectId);
            UpdateQuest(); Dirty = true; return null;
        }

        string EatMushroom(PlayerState player, string mushroomId)
        {
            var mushroom = world.Mushroom(mushroomId);
            if (mushroom == null) return "Невідомий гриб.";
            if (State.Collected(mushroomId)) return "Цей гриб уже з’їли.";
            if (!SameFloor(player.y, mushroom.y) ||
                !Near(player, mushroom.x, mushroom.y, mushroom.z, DeadAirMushrooms.EatRange, DeadAirMushrooms.EyeOffset))
                return "Підійдіть ближче до гриба.";
            bool ongoing = player.mushroomTripUntil > State.elapsed;
            if (!ongoing) player.mushroomTripStartedAt = State.elapsed;
            player.mushroomTripUntil = ongoing
                ? Mathf.Min(player.mushroomTripUntil + DeadAirMushrooms.ExtensionSeconds,
                    player.mushroomTripStartedAt + DeadAirMushrooms.MaximumTripSeconds)
                : State.elapsed + DeadAirMushrooms.TripSeconds;
            player.mushroomTripSerial++;
            State.collected.Add(mushroomId);
            if (!State.creative)
            {
                player.sanity = Mathf.Max(Mathf.Min(12, player.sanity), player.sanity - 6);
                HearNoise(player.x, player.y, player.z, 18, 6);
            }
            State.message = player.name + " з’їв " + mushroom.label + ". Станція попливла…";
            Dirty = true;
            return null;
        }

        static void ClearMushroomTrip(PlayerState player)
        { player.mushroomTripStartedAt = player.mushroomTripUntil = 0; }

        string TakePill(PlayerState player, string pillId)
        {
            if (State.Collected(pillId)) return "Ці пігулки вже забрали.";
            player.sanity = Mathf.Min(100f, player.sanity + 40f);
            State.collected.Add(pillId);
            Dirty = true;
            State.message = "Прийнято заспокійливе. Розсудок відновлено (+40%)!";
            DeadAirAudio.Play("pill-consume");
            return null;
        }

        public string Drop(string id, string itemId)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || State.paused || p == null || p.down || !string.IsNullOrEmpty(p.hiding)) return "Не можна викинути зараз.";
            if (string.IsNullOrEmpty(itemId) || !p.HasItem(itemId)) return "Предмет відсутній в інвентарі.";
            if(DeadAirRoleItems.IsRoleItem(itemId))return "Особистий предмет залишається з персонажем.";
            if (itemId == DeadAirSupplies.BatteryItem) return DropBattery(p);
            int removedSlot=p.inventory.IndexOf(itemId);
            p.inventory.Remove(itemId);
            if(removedSlot<p.selectedSlot)p.selectedSlot--;
            p.selectedSlot = Mathf.Clamp(p.selectedSlot, 0, Mathf.Max(0, p.inventory.Count - 1));
            p.uniqueItemEquipped=p.HasSelectedRoleItem&&!p.camera&&!p.mapOpen;
            if(!p.uniqueItemEquipped)p.uniqueActionCancelled=true;
            if (itemId == "flashlight") p.flashlight = false;
            if (itemId.StartsWith("keycard-")) State.keycards.Remove(itemId);
            if (itemId.StartsWith("fuel-canister")) { State.canisters.Remove(itemId); State.canister = State.quest.fuelAdded = State.canisters.Count >= 3; }
            string dropInstanceId = "drop_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
            string label = GetItemLabel(itemId);
            State.droppedItems.Add(new DroppedItem { id = dropInstanceId, itemId = itemId, label = label, x = p.x, y = p.y, z = p.z,
                flashlightCharge = itemId == "flashlight" ? p.flashlightCharge : 0 });
            State.message = p.name + " викинув: " + label;
            Dirty = true;
            return null;
        }

        public string GetItemLabel(string itemId)
        {
            if (itemId == "flashlight") return "Ліхтар";
            if (itemId == DeadAirSupplies.BatteryItem) return "Батарейка для ліхтаря";
            if (itemId == DeadAirPrisonBook.Id) return DeadAirPrisonBook.Title;
            if (itemId.StartsWith("fuel-canister")) return "Каністра з дизелем";
            if (itemId == "ladder") return "Складна драбина";
            if (itemId == "key-main") return "Головний ключ станції";
            if (itemId == "key-basement" || itemId == "keycard-basement") return "Картка Під.";
            if(DeadAirRoleItems.IsRoleItem(itemId))return DeadAirRoleItems.Label(itemId);
            var obj = world.Objective(itemId);
            if (obj != null) return obj.label;
            if (itemId.StartsWith("keycard-")) return "Картка доступу";
            if (itemId.StartsWith("tape-")) return "Касета";
            if (itemId.StartsWith("fuse-")) return "Запобіжник 380В";
            return itemId;
        }

        public void SelectSlot(string id, int slot)
        {
            var p = State.Player(id);
            if (State.phase!="playing" || State.paused || p == null || p.down || !string.IsNullOrEmpty(p.hiding)) return;
            p.selectedSlot = Mathf.Clamp(slot, 0, Mathf.Max(0, p.inventory.Count - 1));
            p.camera=p.mapOpen=false;
            if (p.SelectedItem != "flashlight") p.flashlight = false;
            p.uniqueItemEquipped=p.HasSelectedRoleItem;
            if(!p.uniqueItemEquipped)p.uniqueActionCancelled=true;
            Dirty = true;
        }

        public string Calibrate(string id, float frequency)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || p == null || p.down) return "Ефір не активний.";
            var console = world.Objective("broadcast");
            // The solo calibration panel pauses movement while still accepting its tuning controls.
            if (console == null || !Near(p, console.x, console.y, console.z, 2.5f, 0, console.id)) return "Підійдіть до пульта.";
            if (float.IsNaN(frequency) || float.IsInfinity(frequency) || Mathf.Abs(frequency - 96.4f) >= .2f) return "Потрібна несуча 96.4 МГц.";
            State.calibrated = State.quest.calibrated = true;
            State.message = "ЧАСТОТУ НАЛАШТОВАНО (96.4 МГц).";
            Dirty = true; return null;
        }

        public void Radio(string id, int phrase)
        {
            var p = State.Player(id);
            if (State.phase!="playing"||State.paused||p == null || p.down || !string.IsNullOrEmpty(p.hiding)) return;
            var phrases = new[] { "Я тут! До мене!", "ОБЕРЕЖНО! ДИКТОР ПОРУЧ!", "Касету знайдено!", "ПОТРІБНА ДОПОМОГА!" };
            State.radioSequence++;
            State.radioSenderId = id;
            State.message = "РАЦІЯ [" + p.name + "]: " + phrases[Mathf.Clamp(phrase, 0, 3)];
            if (HearNoise(p.x,p.y,p.z,32,9))
            {
                State.enemy.radioX = p.x; State.enemy.radioY = p.y; State.enemy.radioZ = p.z; State.enemy.radioUntil = State.elapsed + 9;
                State.scare++; State.message += " — ДИКТОР ПОЧУВ РАЦІЮ!";
            }
            Dirty = true;
        }

        void ExitLocker(PlayerState p)
        {
            var locker = world.Locker(p.hiding);
            p.hiding = null;
            if (locker != null) { p.x = locker.exitX; p.y = locker.ExitFloorY; p.z = locker.exitZ; p.yaw = locker.FacingYaw; p.pitch = 0; HearNoise(p.x,p.y,p.z,9,5); }
            State.message = p.name + " вийшов із шафи."; Dirty = true;
        }

        void UpdateQuest()
        {
            State.quest.stage = State.broadcast || State.phase == "won" ? 4 : State.power ? 3 :
                State.tapes.Count > 0 || State.keycards.Count > 0 ? 2 : 1;
        }

        void CheckDefeat()
        {
            if (State.phase != "playing" || State.players.Count == 0) return;
            bool allDown = true;
            for (int i = 0; i < State.players.Count; i++)
            {
                if (!State.players[i].down) { allDown = false; break; }
            }
            if (allDown)
            {
                State.phase = "lost"; State.paused = false;
                foreach(var player in State.players)ClearMushroomTrip(player);
                State.message = "Сигнал утрачено. Ваші голоси належать станції.";
                // Tick stops immediately after defeat, so the final state cannot
                // wait for the next periodic publish tick.
                Dirty = true;
            }
        }

        void RecordReturnedLoot(PlayerState actor)
        {
            foreach (var item in actor.inventory)
                if (DeadAirLootSummary.IsRecoverable(world, item) && !State.securedItems.Contains(item)) State.securedItems.Add(item);
        }

        public void Tick(float dt)
        {
            if (State.phase == "departing")
            {
                State.elapsed += Mathf.Min(dt, .25f);
                if (State.elapsed - State.departureStartedAt >= DeadAirVanPresentation.DepartureDuration) State.phase = "won";
                Dirty = true;
                return;
            }
            if (State.phase != "playing")
            {
                if (DeadAirLadder.Advance(State)) Dirty = true;
                return;
            }
            if (State.phase != "playing" || State.paused) return;
            dt = Mathf.Min(dt, .25f);
            State.elapsed += dt;
            bool paneWasBroken = State.windowBroken;
            if (DeadAirLadder.Advance(State)) Dirty = true;
            if (!paneWasBroken && State.windowBroken) HearNoise(-9, 4.2f, 6, 35, 10);
            if (!State.creative && State.spawnCountdown > 0)
            {
                State.spawnCountdown = Mathf.Max(0, State.spawnCountdown - dt);
                if (State.spawnCountdown == 0 && !State.enemy.active)
                { State.enemy.active = true; State.scare++; State.message = "СИГНАЛ ПРОРВАНО. Диктор у коридорі."; }
            }
            if (State.elapsed >= nextScare)
            {
                nextScare += 60; State.scare++;
                State.message = "У рації чути ваше дихання. І ще чиєсь.";
            }
            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                if (!p.down && State.elapsed >= DeadAirVanPresentation.ArrivalDuration &&
                    DeadAirVanPresentation.IsInside(world, p.x, p.z) && Mathf.Abs(p.y - DeadAirVanPresentation.FloorHeight) < .3f)
                    RecordReturnedLoot(p);
                p.stamina = State.creative ? 100 : Mathf.Clamp(p.stamina +
                    (p.sprinting ? -DeadAirLocomotion.RunDrain : DeadAirLocomotion.Recovery * world.GetClass(p.characterClass).bonuses.staminaRecovery) * dt, 0, 100);
                if (p.stamina <= 2) p.sprinting = false;
                DrainFlashlight(p, dt);
                if (p.down) { ClearMushroomTrip(p); continue; }
                if (State.creative) { p.sanity = 100; continue; }
                if (!string.IsNullOrEmpty(p.hiding)) { p.sanity = Mathf.Min(100, p.sanity + 2.5f * dt); continue; }
                float drain = p.flashlight ? 0 : 1.4f;
                float d = Dist(p.x, p.z, State.enemy.x, State.enemy.z);
                if (State.enemy.active && State.enemy.behaviour!="vanished" && SameFloor(p.y, State.enemy.y)) drain += d < 5 ? 5 : d < 11 ? 2 : 0;
                if (p.sprinting) drain += .9f;
                if (p.camera && State.enemy.stunned) drain += 2.5f;
                var recover = p.flashlight && !p.sprinting ? .6f : 0;
                bool hasCompanion = false;
                for (int j = 0; j < State.players.Count; j++)
                {
                    var q = State.players[j];
                    if (q != p && !q.down && SameFloor(p.y, q.y) && Dist(p.x, p.z, q.x, q.z) < 4) { hasCompanion = true; break; }
                }
                if (hasCompanion) recover += 2.2f;
                p.sanity = Mathf.Clamp(p.sanity + (recover - drain * world.GetClass(p.characterClass).bonuses.sanityResistance) * dt, 0, 100);
            }
            if (State.enemy.active) UpdateEnemy(dt);
            if (DeadAirLadder.Advance(State)) Dirty = true;
            nextPublish += dt;
            if (nextPublish >= .1f) { Dirty = true; nextPublish = 0; }
        }

        void ProtectBreaker()
        {
            nextBreakerTrip = State.elapsed + BreakerProtectionSeconds;
        }

        // Sabotage requires the monster to reach the physical switch during patrol.
        bool TrySabotageBreaker(int roll)
        {
            if (State.elapsed < nextBreakerTrip) return false;
            nextBreakerTrip = State.elapsed + BreakerCheckSeconds;
            var enemy = State.enemy;
            var breaker = world.Objective("breaker");
            if (State.creative || !State.power || !State.breakerOn || State.broadcast ||
                breakerTrips >= MaximumBreakerTrips || breaker == null || !enemy.active ||
                State.elapsed < enemy.stunnedUntil || State.elapsed < enemy.heardUntil ||
                enemy.behaviour == "peek" || enemy.behaviour == "vanished" ||
                !SameFloor(enemy.y, breaker.y - 1.1f) || Dist(enemy.x, enemy.z, breaker.x, breaker.z) > 3 ||
                roll >= 10) return false;
            State.breakerOn = State.quest.breakerOn = false;
            breakerTrips++;
            State.scare++;
            State.message = "ДИКТОР ДІСТАВСЯ РУБИЛЬНИКА У ПІДВАЛІ! Світло згасло.";
            HearNoise(breaker.x, enemy.y, breaker.z, 30, 8);
            Dirty = true;
            return true;
        }

        void UpdateEnemy(float dt)
        {
            var enemy = State.enemy;
            if (State.elapsed < lastPerceptionTick) { attackRecoveryUntil = 0; ClearEnemyAttack(); }
            lastPerceptionTick = State.elapsed;
            if(UpdateEncounter())return;
            bool hasLiving = false;
            PlayerState encounterPlayer = null;

            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                if (p.down) continue;
                hasLiving = true;

                if(string.IsNullOrEmpty(p.hiding)&&SameFloor(p.y,enemy.y)&&encounterPlayer==null)encounterPlayer=p;
            }

            if (!hasLiving) { CheckDefeat(); return; }
            enemy.stunned = State.elapsed<enemy.stunnedUntil;
            if (enemy.stunned) { ClearEnemyAttack(); return; }
            if (!string.IsNullOrEmpty(enemy.attackPlayerId)) { AttackNearbyPlayers(1.25f); return; }
            if (State.elapsed >= nextBreakerTrip) TrySabotageBreaker(random.Next(100));
            bool investigating=State.elapsed<enemy.heardUntil;
            Vector3 heard=new Vector3(enemy.heardX,enemy.heardY,enemy.heardZ);
            bool searching = State.elapsed < enemy.searchUntil;
            if(investigating&&Vector3.Distance(new Vector3(enemy.x,enemy.y,enemy.z),heard)<.7f)
            {
                enemy.heardUntil = State.elapsed;
                enemy.searchUntil = State.elapsed + 5;
                enemy.behaviour="search";enemy.doorId=null;
                enemy.yaw+=dt*.4f;
                AttackNearbyPlayers(.7f);
                return;
            }
            if (searching && !investigating)
            {
                enemy.behaviour = "search"; enemy.doorId = null;
                enemy.yaw += dt * .55f;
                AttackNearbyPlayers(.8f); return;
            }
            if(!State.creative&&!State.broadcast&&!investigating&&State.elapsed>=nextPeek&&encounterPlayer!=null&&TryBeginPeek(encounterPlayer,false))return;
            if(!investigating&&(State.elapsed>=nextPatrol||Vector3.Distance(new Vector3(enemy.x,enemy.y,enemy.z),patrolDestination)<.7f))
                SelectPatrolDestination();
            Vector3 destinationPoint=investigating?heard:patrolDestination;
            float targetX=destinationPoint.x,targetY=destinationPoint.y,targetZ=destinationPoint.z;
            var destination = EnemyWaypoint(enemy, targetX, targetY, targetZ);
            var current = new Vector2(enemy.x, enemy.z);
            if(WaitForDoor(enemy,destination))return;
            enemy.behaviour=investigating?"investigate":"patrol";enemy.doorId=null;
            var step = Vector2.MoveTowards(current, destination, (investigating ? (State.broadcast ? 4.15f : 2.5f) : .85f) * dt);
            float stepY = EnemyFloorHeight(step.x, step.y, enemy.y);
            if (Walkable(step.x, stepY, step.y) && !SegmentBlocked(enemy.x, enemy.y, enemy.z, step.x, stepY, step.y))
            {
                var movement=step-current;
                if(movement.sqrMagnitude>.00001f)enemy.yaw=Mathf.Atan2(-movement.x,movement.y);
                enemy.x = step.x; enemy.y = stepY; enemy.z = step.y;
            }

            AttackNearbyPlayers(investigating ? 1.15f : .65f);
        }

        void AttackNearbyPlayers(float range)
        {
            var enemy=State.enemy;
            if (!string.IsNullOrEmpty(enemy.attackPlayerId))
            {
                var target = State.Player(enemy.attackPlayerId);
                enemy.behaviour = "attack";
                if (State.elapsed - enemy.attackStartedAt < AttackWindup) return;
                // The target has the entire wind-up to leave reach, close a door
                // or hide. No attack can follow an operator through an office wall.
                if (target != null && !target.down && string.IsNullOrEmpty(target.hiding) &&
                    State.elapsed >= target.invulnerableUntil && SameFloor(target.y, enemy.y) &&
                    Dist(target.x, target.z, enemy.x, enemy.z) <= 1.25f &&
                    !SegmentBlocked(enemy.x, enemy.y, enemy.z, target.x, target.y, target.z)) DownAttackedPlayer(target);
                ClearEnemyAttack(); attackRecoveryUntil = State.elapsed + AttackRecovery;
                enemy.behaviour = "search"; enemy.searchUntil = Mathf.Max(enemy.searchUntil, attackRecoveryUntil);
                Dirty = true; CheckDefeat(); return;
            }
            if (State.elapsed < attackRecoveryUntil) return;
            PlayerState nearest = null; float nearestDistance = range;
            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                if (p.down || !string.IsNullOrEmpty(p.hiding) || State.elapsed < p.invulnerableUntil) continue;
                float distance = Dist(p.x, p.z, enemy.x, enemy.z);
                if (SameFloor(p.y, enemy.y) && distance < nearestDistance &&
                    !SegmentBlocked(enemy.x,enemy.y,enemy.z,p.x,p.y,p.z))
                {
                    nearest = p; nearestDistance = distance;
                }
            }
            if (nearest == null) return;
            if (DeadAirChapters.IsChapter2(State.chapterId))
            {
                if (nearest.sanity > 40f)
                {
                    // High sanity: manifests, spooks, and vanishes without killing!
                    nearest.sanity = Mathf.Max(0, nearest.sanity - 10f);
                    enemy.behaviour = "vanished";
                    enemy.heardUntil = State.elapsed + 7f;
                    State.message = "Тек-Тек промайнула в тінях і розчинилася!";
                    Dirty = true;
                    return;
                }
                enemy.attackPlayerId = nearest.id; enemy.attackStartedAt = State.elapsed;
                enemy.behaviour = "attack";
                enemy.yaw = Mathf.Atan2(-(nearest.x - enemy.x), nearest.z - enemy.z);
                State.message = "ТЕК-ТЕК НАЗДОГНАЛА ВАС!"; Dirty = true;
                return;
            }
            enemy.attackPlayerId = nearest.id; enemy.attackStartedAt = State.elapsed;
            enemy.behaviour = "attack";
            enemy.yaw = Mathf.Atan2(-(nearest.x - enemy.x), nearest.z - enemy.z);
            State.message = "ДИКТОР ЗАМАХУЄТЬСЯ — ВІДСТУПАЙТЕ!"; Dirty = true;
        }

        // A blind enemy remembers noise positions, never an operator's live position.
        bool HearNoise(float x,float y,float z,float radius,float memory)
        {
            var enemy=State.enemy;
            if(!enemy.active||State.phase!="playing"||State.paused)return false;
            Vector3 sound=new Vector3(x,y,z),origin=new Vector3(enemy.x,enemy.y,enemy.z);
            if(!SameFloor(y,enemy.y))radius*=.4f;
            if(PhysicalSegmentBlocked(new Vector3(-x,y+1.1f,z),new Vector3(-enemy.x,enemy.y+1.1f,enemy.z)))radius*=.72f;
            float distance = Vector3.Distance(origin,sound);
            if(distance>radius)return false;
            float strength = radius / Mathf.Max(1, distance);
            bool remembered = State.elapsed < enemy.heardUntil;
            float previousStrength = enemy.heardStrength * Mathf.Clamp01((enemy.heardUntil - State.elapsed) / 5);
            Vector3 previous = new Vector3(enemy.heardX, enemy.heardY, enemy.heardZ);
            if (remembered && (sound - previous).sqrMagnitude > 4 && strength * 1.25f < previousStrength) return false;
            bool movedTarget = !remembered || (sound - previous).sqrMagnitude > 1;
            enemy.heardX=x;enemy.heardY=y;enemy.heardZ=z;
            enemy.heardUntil=State.elapsed+Mathf.Max(memory, distance / 2.5f + 3);
            enemy.heardStrength = strength; enemy.searchUntil = 0;
            // Standing still, looking or switching light never calls this path.
            // Small footsteps update the remembered location without rebuilding
            // the complete navigation route every accepted movement packet.
            if (movedTarget) { enemyRoute.Clear();enemyRouteCursor=0;nextRouteAttempt=0; }
            if(enemy.behaviour!="peek"&&enemy.behaviour!="vanished")enemy.behaviour="investigate";
            Dirty=true;return true;
        }

        static readonly Collider[] enemyOverlaps=new Collider[48];
        static bool EnemyPointClear(Vector3 source)
        {
            Vector3 position=new Vector3(-source.x,source.y,source.z);
            int count=Physics.OverlapCapsuleNonAlloc(position+Vector3.up*.54f,position+Vector3.up*1.48f,.22f,
                enemyOverlaps,~0,QueryTriggerInteraction.Ignore);
            for(int index=0;index<count;index++)
            {
                var collider=enemyOverlaps[index];
                if(collider is CharacterController||collider.GetComponentInParent<DeadAirPlayer>()!=null||
                    collider.GetComponentInParent<DeadAirCharacterMotion>()!=null)continue;
                return false;
            }
            return count<enemyOverlaps.Length;
        }

        void SelectPatrolDestination()
        {
            var enemy=State.enemy;var origin=new Vector3(enemy.x,enemy.y,enemy.z);
            patrolDestination=origin;
            int chosen=-1,candidates=0;
            if(world.navigationNodes!=null)
                for(int index=random.Next(7);index<world.navigationNodes.Length;index+=7)
                {
                    Vector3 node=NavigationPosition(index);
                    float distance=(node-origin).sqrMagnitude;
                    if(!SameFloor(node.y,enemy.y)||distance<9||distance>100||!EnemyPointClear(node))continue;
                    if(random.Next(++candidates)==0)chosen=index;
                }
            if(chosen>=0)patrolDestination=NavigationPosition(chosen);
            nextPatrol=State.elapsed+random.Next(9,16);
            enemyRoute.Clear();enemyRouteCursor=0;nextRouteAttempt=0;
        }

        static bool EnemySightline(PlayerState actor,Vector3 source)
        {
            return !PhysicalSegmentBlocked(new Vector3(-actor.x,actor.y+1.4f,actor.z),
                new Vector3(-source.x,source.y+1.35f,source.z));
        }

        bool WaitForDoor(EnemyState enemy,Vector2 destination)
        {
            Vector3 origin=new Vector3(enemy.x,enemy.y,enemy.z);
            Vector3 target=new Vector3(destination.x,enemy.y,destination.y);
            foreach(var data in world.doors)
            {
                if(Mathf.Abs(enemy.y+1.4f-data.y)>1||Dist(enemy.x,enemy.z,data.x,data.z)>1.7f)continue;
                var door=State.Door(data.id);
                if(door==null||door.locked||!CrossesDoor(origin,target,data,.28f))continue;
                if(!door.open){door.open=true;door.openedAt=State.elapsed;Dirty=true;}
                if(State.elapsed-door.openedAt<DoorPassageDelay)
                {
                    enemy.behaviour="door";enemy.doorId=door.id;
                    var toward=new Vector2(data.x-enemy.x,data.z-enemy.z);
                    if(toward.sqrMagnitude>.0001f)enemy.yaw=Mathf.Atan2(-toward.x,toward.y);
                    return true;
                }
            }
            return false;
        }

        static bool CrossesDoor(Vector3 a,Vector3 b,DoorData door,float clearance)
        {
            var normal=new Vector2(Mathf.Cos(door.rotation),-Mathf.Sin(door.rotation));
            var tangent=new Vector2(-normal.y,normal.x);
            var av=new Vector2(a.x-door.x,a.z-door.z);var bv=new Vector2(b.x-door.x,b.z-door.z);
            float da=Vector2.Dot(av,normal),db=Vector2.Dot(bv,normal);
            if(da*db>0||Mathf.Abs(da-db)<.0001f)return false;
            return Mathf.Abs(Vector2.Dot(Vector2.Lerp(av,bv,da/(da-db)),tangent))<door.w*.5f+clearance;
        }

        void TryLockerSpider(PlayerState actor,string lockerId)
        {
            if(State.creative||!inspectedLockers.Add(actor.id+"\0"+lockerId)||
                spiderCooldowns.TryGetValue(actor.id,out float until)&&State.elapsed<until)return;
            double roll=verificationLockerRoll>=0?verificationLockerRoll:random.NextDouble();
            if(roll>=SpiderChance)return;
            actor.sanity=Mathf.Clamp(actor.sanity*.5f,0,100);
            actor.spiderScareSerial++;actor.spiderScareAt=Mathf.RoundToInt(State.elapsed*1000);
            spiderCooldowns[actor.id]=State.elapsed+SpiderCooldown;
            Dirty=true;
        }

        static bool HorrorVerificationAllowed => Application.isEditor||
            Environment.GetCommandLineArgs().Contains("-deadAirHorrorCapture");

        // There is no network command for either verification hook.
        public bool ConfigureLockerRollForVerification(float roll)
        {
            if(!HorrorVerificationAllowed||float.IsNaN(roll)||float.IsInfinity(roll))return false;
            verificationLockerRoll=Mathf.Clamp01(roll);return true;
        }

        public bool TryStartPeekForVerification(string playerId)
        {
            if(!HorrorVerificationAllowed||State.phase!="playing"||State.paused)return false;
            var actor=State.Player(playerId);
            return actor!=null&&!actor.down&&string.IsNullOrEmpty(actor.hiding)&&TryBeginPeek(actor,true);
        }

        bool UpdateEncounter()
        {
            var enemy=State.enemy;
            if(enemy.behaviour=="peek")
            {
                bool approached=State.players.Any(actor=>!actor.down&&string.IsNullOrEmpty(actor.hiding)&&
                    SameFloor(actor.y,enemy.y)&&Dist(actor.x,actor.z,enemy.x,enemy.z)<3.2f);
                if(approached||State.elapsed>=encounterEnds)
                {
                    enemy.behaviour="vanished";enemy.stunned=false;encounterEnds=State.elapsed+3;
                    Dirty=true;
                }
                return true;
            }
            if(enemy.behaviour!="vanished")return false;
            if(State.elapsed<encounterEnds)return true;
            enemy.x=encounterReturnPosition.x;enemy.y=encounterReturnPosition.y;enemy.z=encounterReturnPosition.z;
            enemy.behaviour=State.elapsed<enemy.heardUntil?"investigate":"patrol";
            enemy.watchingPlayerId=null;enemy.peekLean=0;
            enemyRoute.Clear();enemyRouteCursor=0;nextRouteAttempt=0;nextPatrol=0;
            Dirty=true;return false;
        }

        bool TryBeginPeek(PlayerState actor,bool verification)
        {
            var enemy=State.enemy;
            if(enemy.behaviour=="peek"||enemy.behaviour=="vanished")return false;
            var origin=new Vector3(enemy.x,enemy.y,enemy.z);
            if(!verification&&Dist(actor.x,actor.z,enemy.x,enemy.z)<12&&EnemySightline(actor,origin))
            {nextPeek=State.elapsed+4;return false;}
            Vector3 chosen=Vector3.zero,leanDirection=Vector3.zero;
            float best=float.PositiveInfinity;
            foreach(var door in world.doors)
            {
                if(!SameFloor(actor.y,door.y-1.4f)||State.Door(door.id)?.locked==true)continue;
                var normal=new Vector3(Mathf.Cos(door.rotation),0,-Mathf.Sin(door.rotation));
                var tangent=new Vector3(-normal.z,0,normal.x);
                var center=new Vector3(door.x,door.y-1.4f,door.z);
                float behind=Vector3.Dot(new Vector3(actor.x,actor.y,actor.z)-center,normal)>0?-1:1;
                for(int side=-1;side<=1;side+=2)
                {
                    var position=center+tangent*side*(door.w*.5f+.1f)+normal*behind*.52f;
                    ConsiderPeek(actor,position,-tangent*side,ref chosen,ref leanDirection,ref best);
                }
            }
            // Junctions beside the central archive and side rooms can be used even
            // when every nearby door is closed. Actual cover and clearance are required.
            foreach(var room in world.rooms)
            {
                if(!SameFloor(room.floorY,actor.y))continue;
                for(int cornerX=0;cornerX<2;cornerX++)for(int cornerZ=0;cornerZ<2;cornerZ++)
                for(int sideX=-1;sideX<=1;sideX+=2)for(int sideZ=-1;sideZ<=1;sideZ+=2)
                {
                    var corner=new Vector3(cornerX==0?room.minX:room.maxX,room.floorY,cornerZ==0?room.minZ:room.maxZ);
                    var position=corner+new Vector3(sideX*.52f,0,sideZ*.52f);
                    ConsiderPeek(actor,position,new Vector3(-sideX,0,0),ref chosen,ref leanDirection,ref best);
                    ConsiderPeek(actor,position,new Vector3(0,0,-sideZ),ref chosen,ref leanDirection,ref best);
                }
            }
            nextPeek=State.elapsed+(float)(38+random.NextDouble()*17);
            if(float.IsPositiveInfinity(best))return false;
            encounterReturnPosition=origin;
            enemy.x=chosen.x;enemy.y=chosen.y;enemy.z=chosen.z;
            enemy.yaw=Mathf.Atan2(-(actor.x-chosen.x),actor.z-chosen.z);
            Vector3 localRight=Quaternion.Euler(0,enemy.yaw*Mathf.Rad2Deg,0)*Vector3.right;
            enemy.peekLean=Vector3.Dot(new Vector3(-leanDirection.x,0,leanDirection.z),localRight)>=0?1:-1;
            enemy.behaviour="peek";enemy.watchingPlayerId=actor.id;enemy.doorId=null;enemy.active=true;enemy.stunned=false;
            enemy.encounterSerial++;enemy.encounterAt=Mathf.RoundToInt(State.elapsed*1000);
            encounterEnds=State.elapsed+8;
            enemyRoute.Clear();enemyRouteCursor=0;Dirty=true;return true;
        }

        void ConsiderPeek(PlayerState actor,Vector3 position,Vector3 leanDirection,ref Vector3 chosen,ref Vector3 lean,ref float best)
        {
            float distance=Dist(actor.x,actor.z,position.x,position.z);
            if(distance<6||distance>17||!Walkable(position.x,position.y,position.z)||!EnemyPointClear(position))return;
            Vector2 toward=new Vector2(position.x-actor.x,position.z-actor.z).normalized;
            Vector2 forward=new Vector2(-Mathf.Sin(actor.yaw),-Mathf.Cos(actor.yaw));
            if(Vector2.Dot(toward,forward)<.25f||EnemySightline(actor,position))return;
            float yaw=Mathf.Atan2(-(actor.x-position.x),actor.z-position.z);
            Vector3 right=Quaternion.Euler(0,yaw*Mathf.Rad2Deg,0)*Vector3.right;
            float side=Vector3.Dot(new Vector3(-leanDirection.x,0,leanDirection.z),right)>=0?1:-1;
            Vector3 exposed=new Vector3(-position.x,position.y+2.48f,position.z)+right*side*.41f;
            if(PhysicalSegmentBlocked(new Vector3(-actor.x,actor.y+1.4f,actor.z),exposed))return;
            float score=Mathf.Abs(distance-9);
            if(score>=best)return;
            best=score;chosen=position;lean=leanDirection;
        }

        RoomData SideRoom(float x, float y, float z)
        {
            foreach (var room in world.rooms)
                if ((room.minX >= 2.9f || room.maxX <= -2.9f) && SameFloor(room.floorY, y) &&
                    x >= room.minX && x <= room.maxX && z >= room.minZ && z <= room.maxZ) return room;
            return null;
        }

        DoorData RoomDoor(RoomData room)
        {
            DoorData result = null;
            float nearest = float.MaxValue;
            float side = room.minX >= 0 ? 1 : -1;
            foreach (var door in world.doors)
            {
                if (Mathf.Sign(door.x) != side || Mathf.Abs(door.y - 1.4f - room.floorY) > .8f ||
                    door.z < room.minZ || door.z > room.maxZ || Mathf.Abs(door.x) > 4) continue;
                float distance = Mathf.Abs(door.z - (room.minZ + room.maxZ) * .5f);
                if (distance < nearest) { result = door; nearest = distance; }
            }
            return result;
        }

        Vector2 EnemyWaypoint(EnemyState enemy, float targetX, float targetY, float targetZ)
        {
            if(world.navigationNodes!=null&&world.navigationNodes.Length>0&&world.navigationLinks!=null)
                return NavigationWaypoint(enemy,new Vector3(targetX,targetY,targetZ));
            var sourceRoom = SideRoom(enemy.x, enemy.y, enemy.z);
            var targetRoom = SideRoom(targetX, targetY, targetZ);
            if (sourceRoom != null && sourceRoom != targetRoom)
            {
                var door = RoomDoor(sourceRoom);
                if (door != null)
                {
                    float corridorX = 0;
                    if (world.stairs != null)
                        foreach (var stair in world.stairs)
                            if (door.z >= stair.minZ && door.z <= stair.maxZ)
                                corridorX = Mathf.Sign(enemy.x) * (stair.maxX + .6f);
                    return Mathf.Abs(enemy.z - door.z) > .4f ? new Vector2(Mathf.Sign(enemy.x) * 3.65f, door.z) : new Vector2(corridorX, door.z);
                }
            }
            if (world.stairs != null && world.stairs.Length > 0 &&
                (Mathf.Abs(enemy.y - targetY) > .5f || world.stairs[0].Contains(enemy.x, enemy.z)))
            {
                var stair = world.stairs[0];
                float centerX = (stair.minX + stair.maxX) * .5f;
                bool upward = targetY >= stair.upperY - .5f || targetY > enemy.y + .15f;
                if (stair.Contains(enemy.x, enemy.z))
                    return new Vector2(centerX, upward ? stair.minZ - .4f : stair.maxZ + .4f);
                var approach = new Vector2(centerX, upward ? stair.maxZ + .6f : stair.minZ - .6f);
                // Reach each flight entrance along a side passage first. This also
                // lets an enemy leave a rear office without cutting across the flight.
                if (((!upward && enemy.z > stair.minZ) || (upward && enemy.z < stair.maxZ)) && Mathf.Abs(enemy.x) <= 3)
                {
                    float bypassX = enemy.x < -.5f ? stair.minX - .6f : stair.maxX + .6f;
                    return Mathf.Abs(enemy.x - bypassX) > .2f ? new Vector2(bypassX, enemy.z) : new Vector2(bypassX, approach.y);
                }
                return Dist(enemy.x, enemy.z, approach.x, approach.y) > .75f ? approach :
                    new Vector2(centerX, upward ? stair.minZ - .4f : stair.maxZ + .4f);
            }
            Vector2? courtyardDoor = null;
            float nearestFacade = float.MaxValue;
            if (Mathf.Abs(enemy.y) < .5f && Mathf.Abs(targetY) < .5f)
                foreach (var door in world.doors)
                {
                    if (Mathf.Abs(door.x) > .5f || Mathf.Abs(door.y - 1.4f) > .8f || Mathf.Abs(Mathf.Sin(door.rotation)) < .9f) continue;
                    float distance = Mathf.Abs(enemy.z - door.z);
                    if ((enemy.z - door.z) * (targetZ - door.z) < 0 && distance > .6f && distance < nearestFacade)
                    { courtyardDoor = new Vector2(door.x, door.z + Mathf.Sign(targetZ - door.z) * .4f); nearestFacade = distance; }
                }
            if (sourceRoom != null && sourceRoom == targetRoom) return new Vector2(targetX, targetZ);
            Vector2 destination = courtyardDoor ?? new Vector2(targetX, targetZ);
            if (targetRoom != null && !courtyardDoor.HasValue)
            {
                var door = RoomDoor(targetRoom);
                if (door != null)
                    destination = Mathf.Abs(enemy.z - door.z) > .4f ? new Vector2(0, door.z) : new Vector2(targetX, door.z);
            }
            // Both levels retain a side passage beside the open stairwell.
            if (world.stairs != null && SameFloor(enemy.y, targetY))
                foreach (var stair in world.stairs)
                    if (Mathf.Min(enemy.z, destination.y) < stair.maxZ && Mathf.Max(enemy.z, destination.y) > stair.minZ &&
                        Mathf.Abs(enemy.x) <= 3 && Mathf.Abs(destination.x) <= 3)
                    {
                        float bypassX = enemy.x < -.5f ? stair.minX - .6f : stair.maxX + .6f;
                        float bypassZ = Mathf.Clamp(destination.y, stair.minZ - .4f, stair.maxZ + .4f);
                        return Mathf.Abs(enemy.x - bypassX) > .2f ? new Vector2(bypassX, enemy.z) : new Vector2(bypassX, bypassZ);
                    }
            return destination;
        }

        Vector3 NavigationPosition(int index)
        {
            var node=world.navigationNodes[index];return new Vector3(node.x,node.y,node.z);
        }

        Vector2 NavigationWaypoint(EnemyState enemy,Vector3 target)
        {
            if(navigation==null)
            {
                navigation=new System.Collections.Generic.List<int>[world.navigationNodes.Length];
                for(int i=0;i<navigation.Length;i++)navigation[i]=new System.Collections.Generic.List<int>(4);
                foreach(var link in world.navigationLinks)
                {
                    if(link.a<0||link.b<0||link.a>=navigation.Length||link.b>=navigation.Length)continue;
                    navigation[link.a].Add(link.b);navigation[link.b].Add(link.a);
                }
            }
            var origin=new Vector3(enemy.x,enemy.y,enemy.z);
            bool targetMoved=(target-routeTarget).sqrMagnitude>1.5f*1.5f;
            if(State.elapsed>=nextRouteAttempt&&(enemyRoute.Count==0||targetMoved))
            {
                PlanEnemyRoute(origin,target);routeTarget=target;nextRouteAttempt=State.elapsed+.5f;
            }
            while(enemyRouteCursor<enemyRoute.Count&&(NavigationPosition(enemyRoute[enemyRouteCursor])-origin).sqrMagnitude<.13f)
                enemyRouteCursor++;
            if(enemyRouteCursor>=enemyRoute.Count)
            {
                enemyRoute.Clear();
                // The target's exact position may lie between graph nodes.
                if(Mathf.Abs(origin.y-target.y)<.6f&&!NavigationSegmentBlocked(origin,target))return new Vector2(target.x,target.z);
                return new Vector2(enemy.x,enemy.z);
            }
            Vector3 waypoint=NavigationPosition(enemyRoute[enemyRouteCursor]);
            if(NavigationSegmentBlocked(origin,waypoint))
            {
                // An unlocked door opens when approached; a newly locked route needs replanning.
                enemyRoute.Clear();return new Vector2(enemy.x,enemy.z);
            }
            return new Vector2(waypoint.x,waypoint.z);
        }

        int NearestNavigationNode(Vector3 position)
        {
            int nearest=-1;float best=16;
            for(int index=0;index<world.navigationNodes.Length;index++)
            {
                Vector3 node=NavigationPosition(index);
                if(Mathf.Abs(node.y-position.y)>.65f)continue;
                float distance=(node-position).sqrMagnitude;
                if(distance>=best||NavigationSegmentBlocked(position,node))continue;
                nearest=index;best=distance;
            }
            return nearest;
        }

        bool NavigationSegmentBlocked(Vector3 a,Vector3 b)
        {
            if(PhysicalSegmentBlocked(new Vector3(-a.x,a.y+.75f,a.z),new Vector3(-b.x,b.y+.75f,b.z),true))return true;
            foreach(var door in world.doors)
            {
                if(State.Door(door.id)?.locked!=true||Mathf.Abs((a.y+b.y)*.5f+1.4f-door.y)>1)continue;
                var normal=new Vector2(Mathf.Cos(door.rotation),-Mathf.Sin(door.rotation));
                var tangent=new Vector2(-normal.y,normal.x);
                var av=new Vector2(a.x-door.x,a.z-door.z);var bv=new Vector2(b.x-door.x,b.z-door.z);
                float da=Vector2.Dot(av,normal),db=Vector2.Dot(bv,normal);
                if(da*db>0||Mathf.Abs(da-db)<.0001f)continue;
                var crossing=Vector2.Lerp(av,bv,da/(da-db));
                if(Mathf.Abs(Vector2.Dot(crossing,tangent))<door.w*.5f+.15f)return true;
            }
            return false;
        }

        // A binary heap keeps the full courtyard/office grid cheap to search.
        sealed class NavigationHeap
        {
            readonly System.Collections.Generic.List<int> nodes=new System.Collections.Generic.List<int>();
            readonly System.Collections.Generic.List<float> scores=new System.Collections.Generic.List<float>();
            public int Count=>nodes.Count;
            public void Push(int node,float score)
            {
                int index=nodes.Count;nodes.Add(node);scores.Add(score);
                while(index>0)
                {
                    int parent=(index-1)/2;if(scores[parent]<=score)break;
                    nodes[index]=nodes[parent];scores[index]=scores[parent];index=parent;
                }
                nodes[index]=node;scores[index]=score;
            }
            public int Pop()
            {
                int result=nodes[0],last=nodes.Count-1,node=nodes[last];float score=scores[last];
                nodes.RemoveAt(last);scores.RemoveAt(last);if(last==0)return result;
                int index=0;
                while(index*2+1<nodes.Count)
                {
                    int child=index*2+1;if(child+1<nodes.Count&&scores[child+1]<scores[child])child++;
                    if(scores[child]>=score)break;
                    nodes[index]=nodes[child];scores[index]=scores[child];index=child;
                }
                nodes[index]=node;scores[index]=score;return result;
            }
        }

        void PlanEnemyRoute(Vector3 origin,Vector3 target)
        {
            enemyRoute.Clear();enemyRouteCursor=0;
            int source=NearestNavigationNode(origin),goal=NearestNavigationNode(target);
            if(source<0||goal<0)return;
            var costs=new float[navigation.Length];var previous=new int[navigation.Length];var visited=new bool[navigation.Length];
            for(int i=0;i<costs.Length;i++){costs[i]=float.PositiveInfinity;previous[i]=-1;}
            var heap=new NavigationHeap();costs[source]=0;heap.Push(source,0);
            while(heap.Count>0)
            {
                int current=heap.Pop();if(visited[current])continue;visited[current]=true;if(current==goal)break;
                var from=NavigationPosition(current);
                foreach(int next in navigation[current])
                {
                    if(visited[next])continue;
                    var to=NavigationPosition(next);
                    // Trust static clearance only when the saved graph matches the
                    // authored world. Raw or edited layouts retain the physical fallback.
                    if(world.navigationBaked?NavigationDoorBlocked(from,to):NavigationSegmentBlocked(from,to))continue;
                    float cost=costs[current]+Vector3.Distance(from,to);
                    if(cost>=costs[next])continue;
                    costs[next]=cost;previous[next]=current;
                    heap.Push(next,cost+Vector3.Distance(to,NavigationPosition(goal)));
                }
            }
            if(!visited[goal])return;
            for(int node=goal;node>=0;node=previous[node]){enemyRoute.Add(node);if(node==source)break;}
            enemyRoute.Reverse();
            if(enemyRoute.Count>1&&!NavigationSegmentBlocked(origin,NavigationPosition(enemyRoute[1])))enemyRouteCursor=1;
        }

        bool NavigationDoorBlocked(Vector3 a,Vector3 b)
        {
            foreach(var door in world.doors)
            {
                if(State.Door(door.id)?.locked!=true||Mathf.Abs((a.y+b.y)*.5f+1.4f-door.y)>1)continue;
                var normal=new Vector2(Mathf.Cos(door.rotation),-Mathf.Sin(door.rotation));
                var tangent=new Vector2(-normal.y,normal.x);
                var av=new Vector2(a.x-door.x,a.z-door.z);var bv=new Vector2(b.x-door.x,b.z-door.z);
                float da=Vector2.Dot(av,normal),db=Vector2.Dot(bv,normal);
                if(da*db>0||Mathf.Abs(da-db)<.0001f)continue;
                var crossing=Vector2.Lerp(av,bv,da/(da-db));
                if(Mathf.Abs(Vector2.Dot(crossing,tangent))<door.w*.5f+.15f)return true;
            }
            return false;
        }

        float EnemyFloorHeight(float x, float z, float currentY)
        {
            if (world.stairs != null)
            {
                float height=currentY,nearest=2.5f;
                bool onFlight=false;
                foreach (var stair in world.stairs)
                {
                    if(!stair.Contains(x,z,.12f))continue;
                    float candidate=stair.HeightAt(z),distance=Mathf.Abs(currentY-candidate);
                    if(distance>=nearest)continue;
                    height=candidate;nearest=distance;onFlight=true;
                }
                // Both flights occupy the same X/Z. Resolve the nearby flight before
                // accepting a landing on a different flight at the actor's old height.
                if(onFlight)return height;
                foreach (var stair in world.stairs)
                {
                    if (Mathf.Abs(currentY - stair.lowerY) < .4f) return stair.lowerY;
                    if (Mathf.Abs(currentY - stair.upperY) < .4f) return stair.upperY;
                }
            }
            return currentY;
        }

        public void ClearDirty() { Dirty = false; }
    }
}
