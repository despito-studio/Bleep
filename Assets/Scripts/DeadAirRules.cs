using System;
using System.Collections.Generic;
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
            State.spawnCountdown = creative ? 999999 : DeadAirChapters.IsChapter2(chapterId) ? 35 : -1;
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
            RecoverBlackpineDisconnect(State.Player(id));
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
            player.clothingMask = -1;
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
                !player.down && !player.ladderClimbing && !CarryingChicken(player) && string.IsNullOrEmpty(player.hiding);
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

        bool CanReadVanMap(PlayerState player)
        {
            var board = world.Objective("van-map");
            return board != null && player != null && !player.down && !player.ladderClimbing && !CarryingChicken(player) &&
                string.IsNullOrEmpty(player.hiding) && DeadAirVanPresentation.IsInside(world, player.x, player.z) &&
                Near(player, board.x, board.y, board.z, 2.5f, 0, "van-map");
        }


		public string UseUnique(string id)
		{
			PlayerState playerState = State.Player(id);
			if (!CanUseRoleItem(playerState))
			{
				return "Особистий предмет зараз недоступний.";
			}
			if (DeadAirChapters.IsChapter2(State.chapterId))
			{
				string selectedItem = playerState.SelectedItem;
				if (selectedItem == "pills")
				{
					if (playerState.sanity >= 100f)
					{
						return "Розсудок уже відновлено.";
					}
					float num = Mathf.Min(50f, 100f - playerState.sanity);
					playerState.sanity += num;
					playerState.inventory.RemoveAt(playerState.selectedSlot);
					playerState.selectedSlot = Mathf.Clamp(playerState.selectedSlot, 0, Mathf.Max(0, playerState.inventory.Count - 1));
					playerState.uniqueItemEquipped = playerState.HasSelectedRoleItem && !playerState.camera && !playerState.mapOpen;
					if (!playerState.uniqueItemEquipped)
					{
						playerState.uniqueActionCancelled = true;
					}
					DeadAirAudio.Play("pill-consume");
					State.message = playerState.name + " прийняв заспокійливе (+" + Mathf.CeilToInt(num) + "% розсудку).";
					Dirty = true;
					return null;
				}
				if (selectedItem == "camera" || playerState.camera)
				{
					DeadAirAudio.Play("camera-snap");
					EnemyState enemy = State.enemy;
					if (enemy.active && enemy.behaviour != "vanished" && Dist(playerState.x, playerState.z, enemy.x, enemy.z) < 16f && SameFloor(playerState.y, enemy.y))
					{
						enemy.stunnedUntil = State.elapsed + 4f;
						enemy.attackPlayerId = null;
						State.message = "СПАЛАХ КАМЕРИ! Тек-Тек приголомшено фотоспалахом!";
						Dirty = true;
						return null;
					}
					State.message = "Зроблено фотознімок аномальної зони.";
					Dirty = true;
					return null;
				}
				if (selectedItem == "recorder")
				{
					UseRecorder(playerState);
					Dirty = true;
					return null;
				}
				if (selectedItem == "emf-detector")
				{
					EnemyState enemy2 = State.enemy;
					if (enemy2.active && enemy2.behaviour != "vanished")
					{
						float num2 = Dist(playerState.x, playerState.z, enemy2.x, enemy2.z);
						if (num2 < 8f)
						{
							State.message = "ДЕТЕКТОР ЕРС: КРИТИЧНИЙ РІВЕНЬ 5! ТЕК-ТЕК ДУЖЕ БЛИЗЬКО!";
						}
						else if (num2 < 18f)
						{
							State.message = "ДЕТЕКТОР ЕРС: Сильні електромагнітні сплески (Рівень 3).";
						}
						else
						{
							State.message = "ДЕТЕКТОР ЕРС: Слабкі коливання вдалині (Рівень 1).";
						}
					}
					else
					{
						State.message = "ДЕТЕКТОР ЕРС: Спокійний фон (Рівень 0).";
					}
					Dirty = true;
					return null;
				}
			}
			if (!playerState.uniqueItemEquipped || !playerState.HasSelectedRoleItem || playerState.camera || playerState.mapOpen)
			{
				return "Оберіть особистий предмет в інвентарі.";
			}
			bool flag = playerState.characterClass == "operator";
			if (flag && playerState.cigarettesRemaining <= 0)
			{
				return "Пачка сигарет порожня.";
			}
			float num3 = DeadAirRoleAbilities.Cooldown(playerState.characterClass);
			if (State.elapsed < playerState.roleReadyAt || State.elapsed - playerState.uniqueActionAt < num3)
			{
				return "Особистий предмет буде готовий через " + Mathf.CeilToInt(Mathf.Max(playerState.roleReadyAt - State.elapsed, num3 - (State.elapsed - playerState.uniqueActionAt))) + " с.";
			}
			if (flag)
			{
				playerState.cigarettesRemaining--;
			}
			playerState.uniqueActionAt = State.elapsed;
			playerState.roleReadyAt = State.elapsed + num3;
			playerState.uniqueActionSequence++;
			playerState.uniqueActionCancelled = false;
			if (playerState.characterClass == "engineer")
			{
				bool flag2 = UseEngineerTool(playerState);
				HearNoise(playerState.x, playerState.y, playerState.z, flag2 ? 14 : 4, flag2 ? 8 : 3);
			}
			else if (playerState.characterClass == "journalist")
			{
				UseRecorder(playerState);
			}
			else if (playerState.characterClass == "dispatcher")
			{
				UseCompass(playerState);
			}
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
            State.spawnCountdown = State.creative ? 999999 : isCh2 ? 10 : -1;
            State.message = State.creative ? "РЕЖИМ ДОСЛІДЖЕННЯ: загроза пасивна." :
                isCh2 ? "ЯПОНСЬКА ШКОЛА: Ви — журналісти-розслідувачі. Знайдіть 5 касет-доказів про Тек-Тек на трьох поверхах школи! Зберігайте розсудок пігулками." :
                "BLACKPINE: перевірте аварійний сигнал із закритої гірської станції. Візьміть драбину у фургоні та проникніть через вікно директора.";
            if (isCh2)
            {
                State.enemy = new EnemyState { x = 0, y = 0.6f, z = -10f };
                State.quest.stage = 1;
            }
            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                if (isCh2)
                {
                    p.x = 0; p.y = 0.5f; p.z = 40f;
                    p.yaw = Mathf.PI; p.pitch = 0;
                    p.characterClass = "journalist";
                    p.sanity = p.stamina = 100;
                    p.down = p.sprinting = p.crouching = p.camera = p.ladderClimbing = false;
                    p.inventory.Clear();
                    p.inventory.Add("flashlight");
                    p.inventory.Add("camera");
                    p.inventory.Add("recorder");
                    p.inventory.Add("emf-detector");
                    p.inventory.Add("pills");
                    p.selectedSlot = 0;
                    p.flashlight = true;
                    p.flashlightCharge = DeadAirSupplies.FlashlightSeconds;
                    p.spareBatteries = 2; p.coins = p.flashlightReloadSerial = 0;
                    p.hiding = null;
                    p.invulnerableUntil = 8;
                    p.uniqueItemEquipped = p.mapOpen = false;
                }
                else
                {
                    var spawn = DeadAirVanPresentation.SpawnPoint(world, i);
                    p.x = spawn.x; p.y = spawn.y; p.z = spawn.z;
                    p.yaw = DeadAirVanPresentation.SpawnYaw; p.pitch = 0;
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
                }
                p.moveSequence = 0;
                p.spiderScareSerial = p.spiderScareAt = 0;
                p.mushroomTripStartedAt = p.mushroomTripUntil = 0;
                p.mushroomTripSerial = 0;
                ResetMovementBudget(p);
            }
            nextScare = 35;
            ResetBlackpine();
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
            p.sprinting = input.sprinting && !p.crouching && !CarryingChicken(p) && (State.creative || p.stamina > 2) && string.IsNullOrEmpty(p.hiding);
            if (string.IsNullOrEmpty(p.hiding))
            {
                var delta = Vector3.Distance(new Vector3(p.x, p.y, p.z), new Vector3(input.x, input.y, input.z));
                float moveSeconds=movementBudgets.TryGetValue(id,out var lastBudget)?Mathf.Max(.04f,(float)(movementTime()-lastBudget.at)):.1f;
                if (!p.noclip && (!MovementWithinBudget(p, delta) || !Walkable(input.x, input.y, input.z) ||
                    SegmentBlocked(p.x, p.y, p.z, input.x, input.y, input.z) ||
                    DeadAirChapters.IsChapter2(State.chapterId) && SchoolMovementSweepBlocked(
                        new Vector3(-p.x, p.y, p.z), new Vector3(-input.x, input.y, input.z), p.crouching))) return;
                if (movementBudgets.TryGetValue(id, out var budget))
                    budget.distance = Mathf.Max(0, budget.distance - delta);
                p.x = input.x; p.y = input.y; p.z = input.z;
                p.yaw = input.yaw; p.pitch = Mathf.Clamp(input.pitch, DeadAirLocomotion.MaximumDownwardPitch, 1.35f);
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
            p.flashlight = input.flashlight && !CarryingChicken(p) && string.IsNullOrEmpty(p.hiding) && DeadAirSupplies.HasCharge(p) &&
                (p.SelectedItem == "flashlight" || input.camera);
            p.camera = input.camera && !CarryingChicken(p) && string.IsNullOrEmpty(p.hiding);
            p.mapOpen = input.mapOpen && CanReadVanMap(p);
            if (p.mapOpen) p.camera = p.flashlight = false;
            if (p.mapOpen || p.camera || CarryingChicken(p) || !string.IsNullOrEmpty(p.hiding))
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
                return x >= -60f && x <= 60f && z >= -80f && z <= 115f && y >= -1f && y <= 8f;
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
            if (DeadAirChapters.IsChapter2(State.chapterId)) return false;
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

        static bool SchoolMovementSweepBlocked(Vector3 origin, Vector3 target, bool crouching)
        {
            Vector3 travel = target - origin;
            float distance = travel.magnitude;
            if (distance < .0001f) return false;
            // Match the local controller after its skin and step allowance. The raised foot
            // segment lets the controller perform its .28 m step while walls and furniture
            // still intercept the swept torso and head, including below the old torso ray.
            const float radius = .185f;
            float height = crouching ? DeadAirLocomotion.CrouchingHeight : DeadAirLocomotion.StandingHeight;
            float stepAllowance = Mathf.Min(.28f, height * .25f);
            Vector3 lower = origin + Vector3.up * (stepAllowance + radius);
            Vector3 upper = origin + Vector3.up * (height - radius);
            int count = Physics.CapsuleCastNonAlloc(lower, upper, radius, travel / distance,
                movementHits, distance, -1, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = movementHits[i].collider;
                if (collider is CharacterController || collider.GetComponentInParent<DeadAirPlayer>() != null ||
                    collider.GetComponentInParent<DeadAirCharacterMotion>() != null) continue;
                string name = collider.name;
                if (name.StartsWith("Step F1", StringComparison.Ordinal) || name.StartsWith("Step F2", StringComparison.Ordinal) ||
                    name.StartsWith("Flight 1 Smooth Ramp", StringComparison.Ordinal) || name.StartsWith("Flight 2 Smooth Ramp", StringComparison.Ordinal) ||
                    name == "Third Floor Stair Tread" || name == "Third Floor Smooth Stair Collider") continue;
                if (movementHits[i].normal.y >= .67f) continue;
                return true;
            }
            return count == movementHits.Length;
        }


		private static bool PhysicalSegmentBlocked(Vector3 origin, Vector3 target, bool ignoreDoors = false)
		{
			Vector3 vector = target - origin;
			if (vector.sqrMagnitude < 1E-06f)
			{
				return false;
			}
			int num = Physics.RaycastNonAlloc(origin, vector.normalized, movementHits, vector.magnitude, -1, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < num; i++)
			{
				Collider collider = movementHits[i].collider;
				if (collider is CharacterController || collider.GetComponentInParent<DeadAirPlayer>() != null || collider.GetComponentInParent<DeadAirCharacterMotion>() != null || collider.name.StartsWith("Stair tread") || collider.name.StartsWith("Stair riser") || collider.name.StartsWith("Stair side wall") || collider.name.StartsWith("Stair handrail") || collider.name.StartsWith("Step F1") || collider.name.StartsWith("Step F2") || collider.name.StartsWith("Flight 1 Smooth Ramp") || collider.name.StartsWith("Flight 2 Smooth Ramp") || collider.name == "Third Floor Stair Tread" || collider.name == "Third Floor Smooth Stair Collider")
				{
					continue;
				}
				if (ignoreDoors)
				{
					bool flag = false;
					Transform transform = collider.transform;
					while (transform != null)
					{
						if (transform.name.StartsWith("door:") || transform.name.StartsWith("Door frame") || transform.name == "Door leaf")
						{
							flag = true;
							break;
						}
						transform = transform.parent;
					}
					if (flag)
					{
						continue;
					}
				}
				return true;
			}
			return num == movementHits.Length;
		}


        static float Dist(float ax, float az, float bx, float bz) => Vector2.Distance(new Vector2(ax, az), new Vector2(bx, bz));
        static bool Near(PlayerState player, float x, float y, float z, float range, float targetEyeOffset = 0, string targetId = null) =>
            Vector3.Distance(new Vector3(player.x, player.y + 1, player.z), new Vector3(x, y + targetEyeOffset, z)) <= range &&
            HasInteractionSightline(new Vector3(-player.x, player.y + 1.4f, player.z), new Vector3(-x, y + targetEyeOffset, z), targetId);
        static bool SameFloor(float a, float b) => Mathf.Abs(a - b) < 1;
        static readonly RaycastHit[] sightHits = new RaycastHit[32];


		public static bool HasInteractionSightline(Vector3 origin, Vector3 target, string targetId)
		{
			switch (targetId)
			{
			case "ladder-climb":
			case "ladder-spot":
			case "drawer-director":
			case "ladder":
			case "key-main":
				return true;
			default:
			{
				Vector3 vector = target - origin;
				if (vector.sqrMagnitude < 0.0001f)
				{
					return true;
				}
				bool flag = !string.IsNullOrEmpty(targetId) && (targetId.StartsWith("pill-", StringComparison.Ordinal) || targetId.StartsWith("tape-", StringComparison.Ordinal));
				int num = Physics.RaycastNonAlloc(origin, vector.normalized, sightHits, vector.magnitude, -1, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < num; i++)
				{
					bool flag2 = false;
					bool flag3 = false;
					Transform transform = sightHits[i].collider.transform;
					while (transform != null)
					{
						if (transform.name == "shell" || transform.name == "Van stationary collision shell" || transform.name.StartsWith("door:") || transform.name.StartsWith("Door frame "))
						{
							flag2 = true;
						}
						if (!string.IsNullOrEmpty(targetId) && (transform.name == "door:" + targetId || transform.name == "Door frame " + targetId))
						{
							flag3 = true;
						}
						transform = transform.parent;
					}
					if (flag && IsSchoolPickupBarrier(sightHits[i].collider))
					{
						flag2 = true;
					}
					if (flag2 && !flag3)
					{
						return false;
					}
				}
				return num < sightHits.Length;
			}
			}
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
            if (IsBlackpine && (objectId == DeadAirBlackpine.AntennaId || objectId == DeadAirBlackpine.ResonatorId || objectId == "broadcast"))
                return BeginBlackpineOperation(p, objectId);
            if (IsBlackpine && (objectId == "farm-goat" || objectId == "farm-chicken" || objectId == "farm-chicken-drop"))
                return InteractFarmyard(p, objectId);
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
                    State.canister = State.quest.fuelAdded = State.canisters.Count >= RequiredFuel;
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
                if (CarryingChicken(p)) return "Поставте курку на землю (G) або передайте другу, перш ніж ховатися.";
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
            if (DeadAirChapters.IsChapter2(State.chapterId))
            {
                if (objectId == "school-generator")
                {
                    if (!Near(p, -8.0f, 0.6f, 2.5f, 3.5f, 0f, objectId)) return "Підійдіть ближче до генератора.";
                    if (State.power) return "Генератор уже працює та подає напругу до електрощитка школи.";
                    State.power = State.quest.power = true;
                    DeadAirAudio.Play("power");
                    State.message = "ГЕНЕРАТОР ЗАПУЩЕНО! Тепер увімкніть рубильник у щитку всередині школи (праворуч біля входу).";
                    Dirty = true;
                    return null;
                }
                if (objectId == "school-breaker")
                {
                    if (!Near(p, 4.75f, 1.6f, -1.8f, 3.2f, 0f, objectId)) return "Підійдіть ближче до електрощитка.";
                    if (!State.power) return "Немає напруги! Спочатку запустіть дизельний генератор на подвір'ї школи.";
                    State.breakerOn = State.quest.breakerOn = !State.breakerOn;
                    DeadAirAudio.Play(State.breakerOn ? "power" : "click");
                    State.message = State.breakerOn 
                        ? "ОСВІТЛЕННЯ ВІДНОВЛЕНО! Світло на всіх поверхах школи увімкнено." 
                        : "Рубильник вимкнено. Школа знову поринула у темряву.";
                    Dirty = true;
                    return null;
                }
                if (objectId == "van-exit")
                {
                    if (!Near(p, 0f, .5f, 43f, 4.5f, 0f, objectId)) return "Підійдіть до фургона журналістів.";
                    if (State.tapes.Count < 5)
                        return "Потрібно зібрати всі 5 касет з доказами для репортажу! (Зібрано " + State.tapes.Count + "/5).";
                    State.phase = "won";
                    State.message = "РЕПОРТАЖ ЗАВЕРШЕНО! Ви зібрали всі 5 доказів про легенду Тек-Тек та успішно виїхали на фургоні!";
                    Dirty = true;
                    return null;
                }
                if (objectId.StartsWith("tape-", StringComparison.Ordinal))
                {
                    if (!DeadAirSchoolWorld.TryGetTapePosition(objectId, out var position)) return "Невідома касета.";
                    if (!Near(p, position.x, position.y, position.z, 2.5f, 0f, objectId)) return "Підійдіть до касети.";
                    if (State.Collected(objectId)) return "Цю касету вже забрано.";
                    State.tapes.Add(objectId);
                    State.collected.Add(objectId);
                    if (!p.inventory.Contains(objectId)) p.inventory.Add(objectId);
                    State.scare++;
                    if (!State.creative) State.enemy.active = true;
                    HearNoise(p.x, p.y, p.z, 30, 10);
                    DeadAirAudio.Play("cassette");
                    if (State.tapes.Count >= 5)
                    {
                        State.quest.stage = 4;
                        State.message = "ВСІ 5 КАСЕТ ЗІБРАНО! Біжіть до фургона журналістів на дорозі для евакуації!";
                    }
                    else
                    {
                        State.quest.stage = 2;
                        State.message = "КАСЕТУ-ДОКАЗ ЗНАЙДЕНО (" + State.tapes.Count + "/5). Тек-Тек чує ваші кроки!";
                    }
                    Dirty = true;
                    return null;
                }
            }
            var obj = world.Objective(objectId);
            if (IsBlackpine) obj = DeadAirBlackpine.EvidenceObjective(world, State, obj);
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
                if (State.fuses.Count < 2) return "У щитку відсутні запобіжники 380В! Знайдіть 2 запобіжники (" + State.fuses.Count + "/2), щоб живити станцію та приймач.";
                State.breakerOn = State.quest.breakerOn = !State.breakerOn;
                if (State.breakerOn) ProtectBreaker();
                HearNoise(obj.x, obj.y, obj.z, 22, 6);
                State.message = State.breakerOn ? "Запобіжники встановлено, рубильник увімкнено! Приймач, освітлення станції та підвалу працюють." : "Рубильник вимкнено. Світло та приймач згасли.";
                if (IsBlackpine && State.breakerOn) BeginBlackpineBuildUp();
                UpdateQuest();
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
                if (!State.creative && !IsBlackpine) { State.enemy.active = true; State.spawnCountdown = Mathf.Min(3, State.spawnCountdown); }
                HearNoise(obj.x,p.y,obj.z,50,15);
                State.message = "КАСЕТУ ЗНАЙДЕНО (" + State.tapes.Count + "/3). Диктор чує ваші голоси.";
                if (IsBlackpine) RevealBlackpineEvidence(objectId);
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
                State.canister = State.quest.fuelAdded = State.canisters.Count >= RequiredFuel;
                State.message = IsBlackpine ? "Дизель знайдено. Однієї каністри достатньо для генератора в задньому дворі." : "Каністру з дизелем знайдено (" + State.canisters.Count + "/3).";
            }
            else if (objectId == "pressure-valve") { State.valveOpened = State.quest.valveOpened = true; State.message = "Вентиль тиску відкрито."; }
            else if (objectId == "power")
            {
                if (State.canisters.Count < RequiredFuel) return IsBlackpine ? "Візьміть одну каністру дизелю біля генератора." : "Для запуску генератора потрібно 3 каністри дизельного палива (" + State.canisters.Count + "/3).";
                State.power = State.quest.power = true;
                State.message = "Генератор заправлено дизелем та успішно запущено!";
                if (IsBlackpine) BeginBlackpineBuildUp();
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
                if (IsBlackpine && (!State.blackpine.intercepted || !State.blackpine.antennaDisconnected || !State.blackpine.resonatorStopped)) return "Спершу перехопіть сигнал, роз'єднайте антену та зупиніть Резонатор.";
                if (!State.broadcast) return "Завершіть роботу на станції перед від’їздом.";
                foreach (var crew in State.players)
                    if (!crew.down && !DeadAirVanPresentation.IsInside(world, crew.x, crew.z, -2.2f))
                        return "Зберіть усіх живих учасників біля фургона перед від’їздом.";
                RescueChicken();
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
                State.message = IsBlackpine && State.blackpine.chickenRescued ? "Ви вирвалися зі станції та врятували курку. У рації чути на один голос більше." : "Ви вийшли. На записі чути на один голос більше.";
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


		private string TakePill(PlayerState player, string pillId)
		{
			if (!DeadAirChapters.IsChapter2(State.chapterId))
			{
				return "Тут немає заспокійливого.";
			}
			if (!DeadAirSchoolWorld.TryGetPillPosition(pillId, out var position))
			{
				return "Невідоме заспокійливе.";
			}
			if (!Near(player, position.x, position.y, position.z, 2.5f, 0f, pillId))
			{
				return "Підійдіть до заспокійливого.";
			}
			if (State.Collected(pillId))
			{
				return "Ці пігулки вже забрали.";
			}
			if (player.sanity >= 100f)
			{
				return "Розсудок уже відновлено.";
			}
			float num = Mathf.Min(40f, 100f - player.sanity);
			player.sanity += num;
			State.collected.Add(pillId);
			Dirty = true;
			State.message = "Прийнято заспокійливе. Розсудок відновлено (+" + Mathf.CeilToInt(num) + "%)!";
			DeadAirAudio.Play("pill-consume");
			return null;
		}


        public string Drop(string id, string itemId)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || State.paused || p == null || p.down || !string.IsNullOrEmpty(p.hiding)) return "Не можна викинути зараз.";
            if (CarryingChicken(p)) return "Спочатку поставте курку на землю (G) або передайте другу.";
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
            if (itemId.StartsWith("fuel-canister")) { State.canisters.Remove(itemId); State.canister = State.quest.fuelAdded = State.canisters.Count >= RequiredFuel; }
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
            if (itemId == "camera") return "Репортерська камера";
            if (itemId == "emf-detector") return "Детектор аномалій";
            if (itemId == "pills" || itemId.StartsWith("pill-")) return "Заспокійливі пігулки";
            if (itemId.StartsWith("tape-")) return "Касета-доказ";
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
            if (State.phase!="playing" || State.paused || p == null || p.down || CarryingChicken(p) || !string.IsNullOrEmpty(p.hiding)) return;
            p.selectedSlot = Mathf.Clamp(slot, 0, Mathf.Max(0, p.inventory.Count - 1));
            p.camera=p.mapOpen=false;
            if (p.SelectedItem != "flashlight") p.flashlight = false;
            p.uniqueItemEquipped=p.HasSelectedRoleItem&&!CarryingChicken(p);
            if(!p.uniqueItemEquipped)p.uniqueActionCancelled=true;
            Dirty = true;
        }

        public string Calibrate(string id, float frequency)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || p == null || p.down || p.ladderClimbing || !string.IsNullOrEmpty(p.hiding)) return "Ефір не активний.";
            var console = world.Objective("broadcast");
            // The solo calibration panel pauses movement while still accepting its tuning controls.
            if (console == null || !Near(p, console.x, console.y, console.z, 2.5f, 0, console.id)) return "Підійдіть до пульта.";
            if (IsBlackpine)
            {
                if (CarryingChicken(p)) return "Перед роботою з приймачем поставте курку на землю (G) або передайте другу.";
                if (!State.power || !State.breakerOn || State.fuses.Count < 2) return "Відновіть живлення приймача: генератор, два запобіжники та рубильник у підвалі.";
                if (!DeadAirBlackpine.HasAllEvidence(State)) return "Знайдіть усі три записи останньої зміни. Несуча вказана на касеті «Останній ефір».";
                if (float.IsNaN(frequency) || float.IsInfinity(frequency) || Mathf.Abs(frequency - DeadAirBlackpine.TargetFrequency(State)) >= .15f) return "На цій частоті лише шум. Звіртеся із записом «Останній ефір» у журналі.";
                State.calibrated = State.quest.calibrated = true;
                return BeginBlackpineOperation(p, "broadcast");
            }
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
            if (IsBlackpine) { State.blackpine.lastRadioPlayerId = id; State.blackpine.lastRadioPhrase = Mathf.Clamp(phrase, 0, 3); }
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
            if (DeadAirChapters.IsChapter2(State.chapterId))
            {
                State.quest.stage = State.phase == "won" ? 4 : State.tapes.Count >= 5 ? 3 : State.tapes.Count > 0 ? 2 : 1;
            }
            else
            {
                State.quest.stage = State.broadcast || State.phase == "won" ? 4 : State.blackpine.intercepted ? 3 :
                    State.tapes.Count > 0 || State.keycards.Count > 0 ? 2 : 1;
            }
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
            if (DeadAirChapters.IsChapter2(State.chapterId)) BuildSchoolNavigation();
            bool paneWasBroken = State.windowBroken;
            if (DeadAirLadder.Advance(State)) Dirty = true;
            if (!paneWasBroken && State.windowBroken) HearNoise(-9, 4.2f, 6, 35, 10);
            if (IsBlackpine) TickBlackpine(dt);
            if (!IsBlackpine && !State.creative && State.spawnCountdown > 0)
            {
                State.spawnCountdown = Mathf.Max(0, State.spawnCountdown - dt);
                if (State.spawnCountdown == 0 && !State.enemy.active)
                { State.enemy.active = true; State.scare++; State.message = DeadAirChapters.IsChapter2(State.chapterId) ? "У ШКОЛІ ЧУТИ КРОКИ ТЕК-ТЕК." : "СИГНАЛ ПРОРВАНО. Диктор у коридорі."; }
            }
            if (State.elapsed >= nextScare)
            {
                nextScare += 60; State.scare++;
                State.message = DeadAirChapters.IsChapter2(State.chapterId) ? "У порожньому коридорі знову чути кроки Тек-Тек." : "У рації чути ваше дихання. І ще чиєсь.";
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


		private bool TrySabotageBreaker(int roll)
		{
			if (DeadAirChapters.IsChapter2(State.chapterId))
			{
				return false;
			}
			if (State.elapsed < nextBreakerTrip)
			{
				return false;
			}
			nextBreakerTrip = State.elapsed + 60f;
			EnemyState enemy = State.enemy;
			ObjectiveData objectiveData = world.Objective("breaker");
			if (State.creative || !State.power || !State.breakerOn || State.broadcast || breakerTrips >= 2 || objectiveData == null || !enemy.active || State.elapsed < enemy.stunnedUntil || State.elapsed < enemy.heardUntil || enemy.behaviour == "peek" || enemy.behaviour == "vanished" || !SameFloor(enemy.y, objectiveData.y - 1.1f) || Dist(enemy.x, enemy.z, objectiveData.x, objectiveData.z) > 3f || roll >= 10)
			{
				return false;
			}
			State.breakerOn = (State.quest.breakerOn = false);
			breakerTrips++;
			State.scare++;
			State.message = "ДИКТОР ДІСТАВСЯ РУБИЛЬНИКА У ПІДВАЛІ! Світло згасло.";
			HearNoise(objectiveData.x, enemy.y, objectiveData.z, 30f, 8f);
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


		private void SelectPatrolDestination()
		{
			if (DeadAirChapters.IsChapter2(State.chapterId))
			{
				SelectSchoolPatrolDestination();
				return;
			}
			EnemyState enemy = State.enemy;
			Vector3 vector = (patrolDestination = new Vector3(enemy.x, enemy.y, enemy.z));
			int num = -1;
			int num2 = 0;
			if (world.navigationNodes != null)
			{
				for (int i = random.Next(7); i < world.navigationNodes.Length; i += 7)
				{
					Vector3 vector2 = NavigationPosition(i);
					float sqrMagnitude = (vector2 - vector).sqrMagnitude;
					if (SameFloor(vector2.y, enemy.y) && !(sqrMagnitude < 9f) && !(sqrMagnitude > 100f) && EnemyPointClear(vector2) && random.Next(++num2) == 0)
					{
						num = i;
					}
				}
			}
			if (num >= 0)
			{
				patrolDestination = NavigationPosition(num);
			}
			nextPatrol = State.elapsed + (float)random.Next(9, 16);
			enemyRoute.Clear();
			enemyRouteCursor = 0;
			nextRouteAttempt = 0f;
		}


        static bool EnemySightline(PlayerState actor,Vector3 source)
        {
            return !PhysicalSegmentBlocked(new Vector3(-actor.x,actor.y+1.4f,actor.z),
                new Vector3(-source.x,source.y+1.35f,source.z));
        }


		private bool WaitForDoor(EnemyState enemy, Vector2 destination)
		{
			if (DeadAirChapters.IsChapter2(State.chapterId))
			{
				return false;
			}
			Vector3 a = new Vector3(enemy.x, enemy.y, enemy.z);
			Vector3 b = new Vector3(destination.x, enemy.y, destination.y);
			DoorData[] doors = world.doors;
			foreach (DoorData doorData in doors)
			{
				if (Mathf.Abs(enemy.y + 1.4f - doorData.y) > 1f || Dist(enemy.x, enemy.z, doorData.x, doorData.z) > 1.7f)
				{
					continue;
				}
				DoorState doorState = State.Door(doorData.id);
				if (doorState == null || doorState.locked || !CrossesDoor(a, b, doorData, 0.28f))
				{
					continue;
				}
				if (!doorState.open)
				{
					doorState.open = true;
					doorState.openedAt = State.elapsed;
					Dirty = true;
				}
				if (State.elapsed - doorState.openedAt < 0.6f)
				{
					enemy.behaviour = "door";
					enemy.doorId = doorState.id;
					Vector2 vector = new Vector2(doorData.x - enemy.x, doorData.z - enemy.z);
					if (vector.sqrMagnitude > 0.0001f)
					{
						enemy.yaw = Mathf.Atan2(0f - vector.x, vector.y);
					}
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


		private bool TryBeginPeek(PlayerState actor, bool verification)
		{
			if (DeadAirChapters.IsChapter2(State.chapterId))
			{
				return false;
			}
			EnemyState enemy = State.enemy;
			if (enemy.behaviour == "peek" || enemy.behaviour == "vanished")
			{
				return false;
			}
			Vector3 source = new Vector3(enemy.x, enemy.y, enemy.z);
			if (!verification && Dist(actor.x, actor.z, enemy.x, enemy.z) < 12f && EnemySightline(actor, source))
			{
				nextPeek = State.elapsed + 4f;
				return false;
			}
			Vector3 chosen = Vector3.zero;
			Vector3 lean = Vector3.zero;
			float best = float.PositiveInfinity;
			DoorData[] doors = world.doors;
			foreach (DoorData doorData in doors)
			{
				if (!SameFloor(actor.y, doorData.y - 1.4f))
				{
					continue;
				}
				DoorState doorState = State.Door(doorData.id);
				if (doorState == null || !doorState.locked)
				{
					Vector3 vector = new Vector3(Mathf.Cos(doorData.rotation), 0f, 0f - Mathf.Sin(doorData.rotation));
					Vector3 vector2 = new Vector3(0f - vector.z, 0f, vector.x);
					Vector3 vector3 = new Vector3(doorData.x, doorData.y - 1.4f, doorData.z);
					float num = ((!(Vector3.Dot(new Vector3(actor.x, actor.y, actor.z) - vector3, vector) > 0f)) ? 1 : (-1));
					for (int j = -1; j <= 1; j += 2)
					{
						Vector3 position = vector3 + vector2 * j * (doorData.w * 0.5f + 0.1f) + vector * num * 0.52f;
						ConsiderPeek(actor, position, -vector2 * j, ref chosen, ref lean, ref best);
					}
				}
			}
			RoomData[] rooms = world.rooms;
			foreach (RoomData roomData in rooms)
			{
				if (!SameFloor(roomData.floorY, actor.y))
				{
					continue;
				}
				for (int k = 0; k < 2; k++)
				{
					for (int l = 0; l < 2; l++)
					{
						for (int m = -1; m <= 1; m += 2)
						{
							for (int n = -1; n <= 1; n += 2)
							{
								Vector3 position2 = new Vector3((k == 0) ? roomData.minX : roomData.maxX, roomData.floorY, (l == 0) ? roomData.minZ : roomData.maxZ) + new Vector3((float)m * 0.52f, 0f, (float)n * 0.52f);
								ConsiderPeek(actor, position2, new Vector3(-m, 0f, 0f), ref chosen, ref lean, ref best);
								ConsiderPeek(actor, position2, new Vector3(0f, 0f, -n), ref chosen, ref lean, ref best);
							}
						}
					}
				}
			}
			nextPeek = State.elapsed + (float)(38.0 + random.NextDouble() * 17.0);
			if (float.IsPositiveInfinity(best))
			{
				return false;
			}
			encounterReturnPosition = source;
			enemy.x = chosen.x;
			enemy.y = chosen.y;
			enemy.z = chosen.z;
			enemy.yaw = Mathf.Atan2(0f - (actor.x - chosen.x), actor.z - chosen.z);
			Vector3 rhs = Quaternion.Euler(0f, enemy.yaw * 57.29578f, 0f) * Vector3.right;
			enemy.peekLean = ((Vector3.Dot(new Vector3(0f - lean.x, 0f, lean.z), rhs) >= 0f) ? 1 : (-1));
			enemy.behaviour = "peek";
			enemy.watchingPlayerId = actor.id;
			enemy.doorId = null;
			enemy.active = true;
			enemy.stunned = false;
			enemy.encounterSerial++;
			enemy.encounterAt = Mathf.RoundToInt(State.elapsed * 1000f);
			encounterEnds = State.elapsed + 8f;
			enemyRoute.Clear();
			enemyRouteCursor = 0;
			Dirty = true;
			return true;
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


		private Vector2 EnemyWaypoint(EnemyState enemy, float targetX, float targetY, float targetZ)
		{
			if (DeadAirChapters.IsChapter2(State.chapterId))
			{
				return SchoolWaypoint(enemy, new Vector3(targetX, targetY, targetZ));
			}
			if (world.navigationNodes != null && world.navigationNodes.Length != 0 && world.navigationLinks != null)
			{
				return NavigationWaypoint(enemy, new Vector3(targetX, targetY, targetZ));
			}
			RoomData roomData = SideRoom(enemy.x, enemy.y, enemy.z);
			RoomData roomData2 = SideRoom(targetX, targetY, targetZ);
			if (roomData != null && roomData != roomData2)
			{
				DoorData doorData = RoomDoor(roomData);
				if (doorData != null)
				{
					float x = 0f;
					if (world.stairs != null)
					{
						StairData[] stairs = world.stairs;
						foreach (StairData stairData in stairs)
						{
							if (doorData.z >= stairData.minZ && doorData.z <= stairData.maxZ)
							{
								x = Mathf.Sign(enemy.x) * (stairData.maxX + 0.6f);
							}
						}
					}
					if (!(Mathf.Abs(enemy.z - doorData.z) > 0.4f))
					{
						return new Vector2(x, doorData.z);
					}
					return new Vector2(Mathf.Sign(enemy.x) * 3.65f, doorData.z);
				}
			}
			if (world.stairs != null && world.stairs.Length != 0 && (Mathf.Abs(enemy.y - targetY) > 0.5f || world.stairs[0].Contains(enemy.x, enemy.z)))
			{
				StairData stairData2 = world.stairs[0];
				float x2 = (stairData2.minX + stairData2.maxX) * 0.5f;
				bool flag = targetY >= stairData2.upperY - 0.5f || targetY > enemy.y + 0.15f;
				if (stairData2.Contains(enemy.x, enemy.z))
				{
					return new Vector2(x2, flag ? (stairData2.minZ - 0.4f) : (stairData2.maxZ + 0.4f));
				}
				Vector2 result = new Vector2(x2, flag ? (stairData2.maxZ + 0.6f) : (stairData2.minZ - 0.6f));
				if (((!flag && enemy.z > stairData2.minZ) || (flag && enemy.z < stairData2.maxZ)) && Mathf.Abs(enemy.x) <= 3f)
				{
					float num = ((enemy.x < -0.5f) ? (stairData2.minX - 0.6f) : (stairData2.maxX + 0.6f));
					if (!(Mathf.Abs(enemy.x - num) > 0.2f))
					{
						return new Vector2(num, result.y);
					}
					return new Vector2(num, enemy.z);
				}
				if (!(Dist(enemy.x, enemy.z, result.x, result.y) > 0.75f))
				{
					return new Vector2(x2, flag ? (stairData2.minZ - 0.4f) : (stairData2.maxZ + 0.4f));
				}
				return result;
			}
			Vector2? vector = null;
			float num2 = float.MaxValue;
			if (Mathf.Abs(enemy.y) < 0.5f && Mathf.Abs(targetY) < 0.5f)
			{
				DoorData[] doors = world.doors;
				foreach (DoorData doorData2 in doors)
				{
					if (!(Mathf.Abs(doorData2.x) > 0.5f) && !(Mathf.Abs(doorData2.y - 1.4f) > 0.8f) && !(Mathf.Abs(Mathf.Sin(doorData2.rotation)) < 0.9f))
					{
						float num3 = Mathf.Abs(enemy.z - doorData2.z);
						if ((enemy.z - doorData2.z) * (targetZ - doorData2.z) < 0f && num3 > 0.6f && num3 < num2)
						{
							vector = new Vector2(doorData2.x, doorData2.z + Mathf.Sign(targetZ - doorData2.z) * 0.4f);
							num2 = num3;
						}
					}
				}
			}
			if (roomData != null && roomData == roomData2)
			{
				return new Vector2(targetX, targetZ);
			}
			Vector2 result2 = vector ?? new Vector2(targetX, targetZ);
			if (roomData2 != null && !vector.HasValue)
			{
				DoorData doorData3 = RoomDoor(roomData2);
				if (doorData3 != null)
				{
					result2 = ((Mathf.Abs(enemy.z - doorData3.z) > 0.4f) ? new Vector2(0f, doorData3.z) : new Vector2(targetX, doorData3.z));
				}
			}
			if (world.stairs != null && SameFloor(enemy.y, targetY))
			{
				StairData[] stairs = world.stairs;
				foreach (StairData stairData3 in stairs)
				{
					if (Mathf.Min(enemy.z, result2.y) < stairData3.maxZ && Mathf.Max(enemy.z, result2.y) > stairData3.minZ && Mathf.Abs(enemy.x) <= 3f && Mathf.Abs(result2.x) <= 3f)
					{
						float num4 = ((enemy.x < -0.5f) ? (stairData3.minX - 0.6f) : (stairData3.maxX + 0.6f));
						float y = Mathf.Clamp(result2.y, stairData3.minZ - 0.4f, stairData3.maxZ + 0.4f);
						if (!(Mathf.Abs(enemy.x - num4) > 0.2f))
						{
							return new Vector2(num4, y);
						}
						return new Vector2(num4, enemy.z);
					}
				}
			}
			return result2;
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


		private float EnemyFloorHeight(float x, float z, float currentY)
		{
			if (DeadAirChapters.IsChapter2(State.chapterId))
			{
				return SchoolFloorHeight(x, z, currentY);
			}
			if (world.stairs != null)
			{
				float result = currentY;
				float num = 2.5f;
				bool flag = false;
				StairData[] stairs = world.stairs;
				foreach (StairData stairData in stairs)
				{
					if (stairData.Contains(x, z, 0.12f))
					{
						float num2 = stairData.HeightAt(z);
						float num3 = Mathf.Abs(currentY - num2);
						if (!(num3 >= num))
						{
							result = num2;
							num = num3;
							flag = true;
						}
					}
				}
				if (flag)
				{
					return result;
				}
				stairs = world.stairs;
				foreach (StairData stairData2 in stairs)
				{
					if (Mathf.Abs(currentY - stairData2.lowerY) < 0.4f)
					{
						return stairData2.lowerY;
					}
					if (Mathf.Abs(currentY - stairData2.upperY) < 0.4f)
					{
						return stairData2.upperY;
					}
				}
			}
			return currentY;
		}


        public void ClearDirty() { Dirty = false; }
    

		private readonly List<int> schoolPatrolNodes = new List<int>(150);



		private static bool IsSchoolPickupBarrier(Collider collider)
		{
			bool flag = false;
			Transform transform = collider.transform;
			while (transform != null)
			{
				if (transform.name == "CHAPTER 2 - JAPANESE SCHOOL & NEIGHBORHOOD")
				{
					flag = true;
					break;
				}
				transform = transform.parent;
			}
			if (!flag)
			{
				return false;
			}
			Vector3 size = collider.bounds.size;
			bool num = size.y > 1.6f && Mathf.Min(size.x, size.z) < 0.65f;
			bool flag2 = size.y < 0.8f && size.x > 3f && size.z > 3f;
			bool flag3 = size.y > 1.6f && size.x > 1f && size.z > 1f;
			return num || flag2 || flag3;
		}



		private readonly List<int> schoolRoute = new List<int>(80);



		private bool schoolGraphReady;



		private void BuildSchoolNavigation()
		{
			if (schoolGraphReady)
			{
				return;
			}
			if (!schoolGraphInitialized)
			{
				if (GameObject.Find("CHAPTER 2 - JAPANESE SCHOOL & NEIGHBORHOOD") == null)
				{
					return;
				}
				Physics.SyncTransforms();
				float[] array = new float[3] { 0.6f, 3.8f, 7.4f };
				float[] array2 = new float[19]
				{
					-32f, -28.5f, -24f, -20f, -16f, -13f, -10f, -6f, -3f, 0f,
					3f, 6f, 10f, 13f, 16f, 20f, 24f, 28.5f, 32f
				};
				float[] array3 = new float[4] { -28.5f, -13f, 13f, 28.5f };
				float[] array4 = array;
				foreach (float y in array4)
				{
					float[] array5 = array2;
					foreach (float x in array5)
					{
						AddSchoolNode(x, y, -11f, patrol: true);
					}
					array5 = array3;
					foreach (float x2 in array5)
					{
						AddSchoolNode(x2, y, -9.5f);
						AddSchoolNode(x2, y, -7.5f);
						AddSchoolNode(x2, y, -5.5f);
						AddSchoolNode(x2, y, -3.2f);
						AddSchoolNode(x2, y, -12.5f);
						AddSchoolNode(x2, y, -14.5f);
						AddSchoolNode(x2, y, -17f);
						AddSchoolNode(x2, y, -19.5f);
					}
					array5 = new float[2] { -13f, 13f };
					foreach (float x3 in array5)
					{
						AddSchoolNode(x3, y, -21f);
						AddSchoolNode(x3, y, -22.9f);
						AddSchoolNode(x3, y, -24.5f);
						AddSchoolNode(x3, y, -26.5f);
					}
					array5 = new float[2] { -26.5f, -36.5f };
					foreach (float z in array5)
					{
						int[] array6 = new int[2] { -1, 1 };
						foreach (int num in array6)
						{
							AddSchoolNode((float)num * 28.5f, y, z);
							AddSchoolNode((float)num * 24.5f, y, z);
							AddSchoolNode((float)num * 20.5f, y, z);
							AddSchoolNode((float)num * 16.5f, y, z);
							AddSchoolNode((float)num * 13f, y, z);
							AddSchoolNode((float)num * 10f, y, z);
							AddSchoolNode((float)num * 7f, y, z);
							AddSchoolNode((float)num * 4f, y, z);
						}
					}
					array5 = new float[7] { -23.5f, -26.5f, -29.5f, -32.5f, -36.5f, -39.5f, -41f };
					foreach (float z2 in array5)
					{
						AddSchoolNode(-4f, y, z2, patrol: true);
						AddSchoolNode(0f, y, z2, patrol: true);
						AddSchoolNode(4f, y, z2, patrol: true);
					}
				}
				for (int l = 0; l <= 6; l++)
				{
					float t = (float)l / 6f;
					AddSchoolNode(3f, Mathf.Lerp(0.6f, 2.2f, t), Mathf.Lerp(-13.8f, -18.2f, t));
					AddSchoolNode(-3f, Mathf.Lerp(3.8f, 2.2f, t), Mathf.Lerp(-13.8f, -18.2f, t));
				}
				array4 = new float[5] { -3f, -1.5f, 0f, 1.5f, 3f };
				foreach (float x4 in array4)
				{
					AddSchoolNode(x4, 2.2f, -20f);
				}
				for (int m = 0; m <= 12; m++)
				{
					float t2 = (float)m / 12f;
					AddSchoolNode(0f, Mathf.Lerp(3.8f, 7.4f, t2), Mathf.Lerp(-25f, -37f, t2));
				}
				array4 = new float[18]
				{
					-2f, 1f, 4f, 8f, 12f, 18f, 24f, 30f, 36f, 43f,
					49f, 55f, 62f, 69f, 76f, 83f, 90f, 97f
				};
				foreach (float num2 in array4)
				{
					AddSchoolNode(0f, (num2 < 1f) ? 0.55f : 0.1f, num2, patrol: true);
				}
				array4 = new float[5] { -7.5f, -5.5f, -3.2f, -2f, -1f };
				foreach (float z3 in array4)
				{
					AddSchoolNode(1f, 0.6f, z3, patrol: true);
				}
				array4 = new float[4] { 0f, 1f, 3f, 4f };
				foreach (float z4 in array4)
				{
					AddSchoolNode(1f, 0.45f, z4, patrol: true);
				}
				AddSchoolNode(1f, 0.35f, 4.5f, patrol: true);
				array4 = new float[11]
				{
					5f, 6f, 8f, 12f, 16f, 20f, 24f, 28f, 32f, 36f,
					40f
				};
				foreach (float z5 in array4)
				{
					AddSchoolNode(1f, 0.1f, z5, patrol: true);
				}
				array4 = new float[4] { 40f, 43f, 46f, 49f };
				foreach (float z6 in array4)
				{
					AddSchoolNode(3f, 0.1f, z6, patrol: true);
				}
				array4 = new float[13]
				{
					49f, 53f, 57f, 61f, 65f, 69f, 73f, 77f, 81f, 85f,
					89f, 93f, 97f
				};
				foreach (float z7 in array4)
				{
					AddSchoolNode(1f, 0.1f, z7, patrol: true);
				}
				AddSchoolNode(0f, 0.1f, 95.5f, patrol: true);
				array4 = new float[7] { -42.7f, -45f, -49f, -53f, -58f, -63f, -67f };
				foreach (float z8 in array4)
				{
					float[] array5 = new float[5] { -6f, -3f, 0f, 3f, 6f };
					foreach (float x5 in array5)
					{
						AddSchoolNode(x5, 0.1f, z8, patrol: true);
					}
				}
				schoolGraphInitialized = true;
			}
			if (schoolBuildFrame == Time.frameCount)
			{
				return;
			}
			schoolBuildFrame = Time.frameCount;
			int num3 = Mathf.Min(schoolNodes.Count, schoolBuildCursor + 24);
			for (int n = schoolBuildCursor; n < num3; n++)
			{
				Vector3 vector = schoolNodes[n];
				for (int num4 = n + 1; num4 < schoolNodes.Count; num4++)
				{
					Vector3 to = schoolNodes[num4];
					float sqrMagnitude = new Vector2(vector.x - to.x, vector.z - to.z).sqrMagnitude;
					float num5 = Mathf.Abs(vector.y - to.y);
					if (!(sqrMagnitude > 27.1f) && !(num5 > 0.68f) && (!(num5 > 0.3f) || !(sqrMagnitude < 0.25f)) && SchoolSegmentClear(vector, to))
					{
						schoolLinks[n].Add(num4);
						schoolLinks[num4].Add(n);
					}
				}
			}
			schoolBuildCursor = num3;
			schoolGraphReady = num3 >= schoolNodes.Count;
		}



		private float schoolNextRouteAt;



		private void PlanSchoolRoute(Vector3 origin, Vector3 target)
		{
			schoolRoute.Clear();
			schoolRouteCursor = 0;
			int num = NearestSchoolNode(origin);
			int num2 = NearestSchoolNode(target);
			if (num < 0 || num2 < 0)
			{
				return;
			}
			float[] array = new float[schoolNodes.Count];
			int[] array2 = new int[schoolNodes.Count];
			bool[] array3 = new bool[schoolNodes.Count];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = float.PositiveInfinity;
				array2[i] = -1;
			}
			NavigationHeap navigationHeap = new NavigationHeap();
			array[num] = 0f;
			navigationHeap.Push(num, 0f);
			while (navigationHeap.Count > 0)
			{
				int num3 = navigationHeap.Pop();
				if (array3[num3])
				{
					continue;
				}
				array3[num3] = true;
				if (num3 == num2)
				{
					break;
				}
				foreach (int item in schoolLinks[num3])
				{
					if (!array3[item])
					{
						float num4 = array[num3] + Vector3.Distance(schoolNodes[num3], schoolNodes[item]);
						if (!(num4 >= array[item]))
						{
							array[item] = num4;
							array2[item] = num3;
							navigationHeap.Push(item, num4 + Vector3.Distance(schoolNodes[item], schoolNodes[num2]));
						}
					}
				}
			}
			if (!array3[num2])
			{
				return;
			}
			for (int num5 = num2; num5 >= 0; num5 = array2[num5])
			{
				schoolRoute.Add(num5);
				if (num5 == num)
				{
					break;
				}
			}
			schoolRoute.Reverse();
			if (schoolRoute.Count > 1 && SchoolSegmentClear(origin, schoolNodes[schoolRoute[1]]))
			{
				schoolRouteCursor = 1;
			}
		}



		private readonly List<Vector3> schoolNodes = new List<Vector3>(400);



		private static bool SchoolFloorSupported(Vector3 point)
		{
			int num = Physics.RaycastNonAlloc(new Vector3(0f - point.x, point.y + 0.65f, point.z), Vector3.down, schoolSupportHits, 1.45f, -1, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < num; i++)
			{
				RaycastHit raycastHit = schoolSupportHits[i];
				if (!(raycastHit.collider is CharacterController) && !(raycastHit.collider.GetComponentInParent<DeadAirPlayer>() != null) && !(raycastHit.collider.GetComponentInParent<DeadAirCharacterMotion>() != null) && Mathf.Abs(raycastHit.point.y - point.y) <= 0.42f)
				{
					return true;
				}
			}
			return false;
		}



		private static bool SchoolSegmentClear(Vector3 from, Vector3 to)
		{
			if (PhysicalSegmentBlocked(new Vector3(0f - from.x, from.y + 0.8f, from.z), new Vector3(0f - to.x, to.y + 0.8f, to.z), ignoreDoors: true))
			{
				return false;
			}
			int num = Mathf.CeilToInt(Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z)) / 1.4f);
			for (int i = 1; i < num; i++)
			{
				if (!SchoolFloorSupported(Vector3.Lerp(from, to, (float)i / (float)num)))
				{
					return false;
				}
			}
			return true;
		}



		private bool schoolGraphInitialized;



		private int schoolBuildCursor;



		private Vector2 SchoolWaypoint(EnemyState enemy, Vector3 target)
		{
			BuildSchoolNavigation();
			Vector3 vector = new Vector3(enemy.x, enemy.y, enemy.z);
			if (Mathf.Abs(vector.y - target.y) < 0.65f && SchoolSegmentClear(vector, target))
			{
				return new Vector2(target.x, target.z);
			}
			if (!schoolGraphReady)
			{
				return new Vector2(enemy.x, enemy.z);
			}
			if (State.elapsed >= schoolNextRouteAt && (schoolRoute.Count == 0 || (target - schoolRouteTarget).sqrMagnitude > 4f))
			{
				PlanSchoolRoute(vector, target);
				schoolRouteTarget = target;
				schoolNextRouteAt = State.elapsed + 0.8f;
			}
			while (schoolRouteCursor < schoolRoute.Count && (schoolNodes[schoolRoute[schoolRouteCursor]] - vector).sqrMagnitude < 0.36f)
			{
				schoolRouteCursor++;
			}
			if (schoolRouteCursor >= schoolRoute.Count)
			{
				schoolRoute.Clear();
				return new Vector2(enemy.x, enemy.z);
			}
			Vector3 to = schoolNodes[schoolRoute[schoolRouteCursor]];
			if (!SchoolSegmentClear(vector, to))
			{
				schoolRoute.Clear();
				return new Vector2(enemy.x, enemy.z);
			}
			return new Vector2(to.x, to.z);
		}



		private int schoolBuildFrame = -1;



		private int NearestSchoolNode(Vector3 point)
		{
			int result = -1;
			float num = 144f;
			for (int i = 0; i < schoolNodes.Count; i++)
			{
				Vector3 vector = schoolNodes[i];
				if (!(Mathf.Abs(vector.y - point.y) > 0.72f))
				{
					float sqrMagnitude = (vector - point).sqrMagnitude;
					if (!(sqrMagnitude >= num) && SchoolSegmentClear(point, vector))
					{
						num = sqrMagnitude;
						result = i;
					}
				}
			}
			return result;
		}



		private readonly List<List<int>> schoolLinks = new List<List<int>>(400);



		private void SelectSchoolPatrolDestination()
		{
			BuildSchoolNavigation();
			Vector3 vector = (patrolDestination = new Vector3(State.enemy.x, State.enemy.y, State.enemy.z));
			if (!schoolGraphReady)
			{
				nextPatrol = State.elapsed + 0.25f;
				return;
			}
			int num = -1;
			int num2 = 0;
			foreach (int schoolPatrolNode in schoolPatrolNodes)
			{
				Vector3 vector2 = schoolNodes[schoolPatrolNode];
				float sqrMagnitude = (vector2 - vector).sqrMagnitude;
				if (!(Mathf.Abs(vector2.y - vector.y) > 0.65f) && !(sqrMagnitude < 16f) && !(sqrMagnitude > 225f) && SchoolSegmentClear(vector, vector2) && random.Next(++num2) == 0)
				{
					num = schoolPatrolNode;
				}
			}
			if (num >= 0)
			{
				patrolDestination = schoolNodes[num];
			}
			nextPatrol = State.elapsed + (float)random.Next(7, 13);
			schoolRoute.Clear();
			schoolRouteCursor = 0;
			schoolNextRouteAt = 0f;
		}



		private static float SchoolFloorHeight(float x, float z, float currentY)
		{
			if (x > 0.7f && x < 5.3f && z <= -13.8f && z >= -18.2f && currentY < 2.6f)
			{
				return 0.6f + (-13.8f - z) * 0.36363637f;
			}
			if (x < -0.7f && x > -5.3f && z <= -13.8f && z >= -18.2f && currentY > 1.7f && currentY < 4.4f)
			{
				return 2.2f + (z + 18.2f) * 0.36363637f;
			}
			if (Mathf.Abs(x) < 5.5f && z < -18.2f && z > -21.8f && currentY > 1.6f && currentY < 3f)
			{
				return 2.2f;
			}
			if (Mathf.Abs(x) < 2.65f && z <= -25f && z >= -37f && currentY > 3.1f && currentY < 7.8f)
			{
				return 3.8f + (-25f - z) * 0.3f;
			}
			if (Mathf.Abs(x) < 36f && z < 0f && z > -42f)
			{
				if (currentY < 1.4f)
				{
					return 0.6f;
				}
				if (currentY > 3f && currentY < 4.5f)
				{
					return 3.8f;
				}
				if (currentY > 6.8f)
				{
					return 7.4f;
				}
			}
			if ((z > 0f || z < -42f) && currentY < 1.4f)
			{
				return 0.1f;
			}
			return currentY;
		}



		private void AddSchoolNode(float x, float y, float z, bool patrol = false)
		{
			Vector3 vector = new Vector3(x, y, z);
			if (EnemyPointClear(vector) && SchoolFloorSupported(vector))
			{
				int count = schoolNodes.Count;
				schoolNodes.Add(vector);
				schoolLinks.Add(new List<int>(6));
				if (patrol)
				{
					schoolPatrolNodes.Add(count);
				}
			}
		}



		private Vector3 schoolRouteTarget;



		private int schoolRouteCursor;



		private static readonly RaycastHit[] schoolSupportHits = new RaycastHit[16];

}
}
