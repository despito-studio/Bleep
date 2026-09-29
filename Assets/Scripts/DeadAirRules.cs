using System;
using System.Linq;
using UnityEngine;

namespace DeadAir
{
    // One instance runs only on the host (also for a solo game). Clients receive snapshots.
    public sealed class DeadAirRules
    {
        readonly WorldData world;
        readonly System.Random random = new System.Random();
        public readonly RoomState State = new RoomState();
        float nextScare = 35, nextPublish;
        public bool Dirty { get; private set; }

        public DeadAirRules(WorldData world, string hostId, string name, string classId, bool solo, bool creative)
        {
            this.world = world;
            State.code = new string(Enumerable.Range(0, 5).Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[random.Next(32)]).ToArray());
            State.hostId = hostId;
            State.solo = solo;
            State.creative = creative;
            State.spawnCountdown = creative ? 999999 : 35;
            State.message = "Сигнал установлено. Очікуємо на операторів.";
            foreach (var d in world.doors)
                State.doors.Add(new DoorState { id = d.id, locked = !string.IsNullOrEmpty(d.requires), requiredKey = d.requires });
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
            var pState = new PlayerState
            {
                id = id, name = string.IsNullOrWhiteSpace(name) ? "Оператор" : name.Trim().Substring(0, Mathf.Min(20, name.Trim().Length)),
                characterClass = cls.id,
                x = world.spawn.x + (index % 2 == 0 ? -.42f : .42f),
                z = world.spawn.z + (index >= 2 ? .42f : 0),
                flashlight = true
            };
            pState.inventory.Add("flashlight");
            State.players.Add(pState);
            State.message = State.players[index].name + " приєднався до ефіру.";
            Dirty = true;
            return true;
        }

        public void RemovePlayer(string id)
        {
            State.players.RemoveAll(p => p.id == id);
            if (State.hostId == id && State.players.Count > 0) State.hostId = State.players[0].id;
            CheckDefeat();
            Dirty = true;
        }

        public void SetClass(string id, string classId)
        {
            var p = State.Player(id);
            if (p == null || !world.classes.Any(c => c.id == classId)) return;
            p.characterClass = classId;
            Dirty = true;
        }

        public void Start(string id)
        {
            if (id != State.hostId || State.phase != "lobby") return;
            Reset();
        }

        public void Restart(string id)
        {
            if (id == State.hostId && (State.phase == "won" || State.phase == "lost")) Reset();
        }

        void Reset()
        {
            State.phase = "playing";
            State.paused = false;
            State.tapes.Clear(); State.fuses.Clear(); State.keycards.Clear();
            State.collected.Clear();
            State.droppedItems.Clear();
            State.canister = State.valveOpened = State.calibrated = State.power = State.broadcast = false;
            State.quest = new QuestState();
            State.enemy = new EnemyState { x = 0, z = -48 };
            foreach (var door in State.doors) { door.open = false; door.locked = !string.IsNullOrEmpty(door.requiredKey); }
            State.elapsed = 0;
            State.scare = 0;
            State.radioSequence = 0;
            State.radioSenderId = null;
            State.spawnCountdown = State.creative ? 999999 : 35;
            State.message = State.creative ? "РЕЖИМ ДОСЛІДЖЕННЯ: загроза пасивна." :
                "Група прибула. До прориву сигналу 35 секунд.";
            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                p.x = world.spawn.x + (i % 2 == 0 ? -.42f : .42f);
                p.z = world.spawn.z + (i >= 2 ? .42f : 0);
                p.yaw = p.pitch = 0;
                p.sanity = p.stamina = 100;
                p.down = p.sprinting = p.camera = false;
                p.inventory.Clear();
                p.inventory.Add("flashlight");
                p.selectedSlot = 0;
                p.flashlight = true;
                p.hiding = null;
                p.invulnerableUntil = 8;
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

        public void Move(string id, NetPacket input)
        {
            if (State.phase != "playing" || State.paused) return;
            var p = State.Player(id);
            if (p == null || p.down) return;
            if (float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.z) || float.IsInfinity(input.z) ||
                float.IsNaN(input.yaw) || float.IsInfinity(input.yaw)) return;
            if (Mathf.Abs(input.x) > 100 || Mathf.Abs(input.z) > 100) return;
            var delta = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(input.x, input.z));
            if (delta > 1.8f || !Walkable(input.x, input.z)) return;
            if (string.IsNullOrEmpty(p.hiding)) { p.x = input.x; p.z = input.z; }
            p.yaw = input.yaw; p.pitch = Mathf.Clamp(input.pitch, -1.35f, 1.35f);
            p.flashlight = input.flashlight && string.IsNullOrEmpty(p.hiding) && p.HasItem("flashlight");
            p.camera = input.camera && string.IsNullOrEmpty(p.hiding);
            p.sprinting = input.sprinting && string.IsNullOrEmpty(p.hiding);
        }

        bool Walkable(float x, float z)
        {
            if (x < world.courtyard.minX + .2f || x > world.courtyard.maxX - .2f ||
                z > world.courtyard.maxZ - .2f || z < -53.8f) return false;
            if (z < 6 && Mathf.Abs(x) > 11.8f) return false;
            if (z < 6 && Mathf.Abs(x) > 2.35f)
            {
                foreach (var door in world.doors)
                {
                    if (Mathf.Abs(door.x - x) < .4f && Mathf.Abs(door.z - z) < 1.15f)
                        return State.Door(door.id)?.open == true;
                }
            }
            return true;
        }

        static float Dist(float ax, float az, float bx, float bz) => Vector2.Distance(new Vector2(ax, az), new Vector2(bx, bz));

        public string Interact(string id, string objectId, float frequency = 0)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || State.paused || p == null || p.down) return "Ефір не активний.";
            if (objectId == "exit-locker")
            {
                if (!string.IsNullOrEmpty(p.hiding)) ExitLocker(p);
                return null;
            }
            if (objectId.StartsWith("dropped:"))
            {
                string droppedId = objectId.Substring(8);
                var dropped = State.droppedItems.Find(d => d.id == droppedId);
                if (dropped == null) return "Предмет уже забрали.";
                if (Dist(p.x, p.z, dropped.x, dropped.z) > 2.5f) return "Підійдіть ближче.";
                State.droppedItems.Remove(dropped);
                string actualItem = string.IsNullOrEmpty(dropped.itemId) ? dropped.id : dropped.itemId;
                if (!p.inventory.Contains(actualItem)) p.inventory.Add(actualItem);
                if (actualItem == "flashlight") p.flashlight = true;
                if (actualItem.StartsWith("fuse-") && !State.fuses.Contains(actualItem)) State.fuses.Add(actualItem);
                if (actualItem.StartsWith("tape-") && !State.tapes.Contains(actualItem)) State.tapes.Add(actualItem);
                if (actualItem.StartsWith("keycard-") && !State.keycards.Contains(actualItem)) State.keycards.Add(actualItem);
                State.message = p.name + " підібрав: " + dropped.label;
                Dirty = true;
                return null;
            }
            if (objectId.StartsWith("revive:"))
            {
                var other = State.Player(objectId.Substring(7));
                if (other == null || !other.down || Dist(p.x, p.z, other.x, other.z) > 2) return "Підійдіть ближче до оператора.";
                other.down = false; other.sanity = Mathf.Max(40, other.sanity); other.invulnerableUntil = State.elapsed + 4;
                State.message = other.name + " знову в ефірі."; Dirty = true; return null;
            }
            var locker = world.Locker(objectId);
            if (locker != null)
            {
                if (p.hiding == objectId) ExitLocker(p);
                else if (Dist(p.x, p.z, locker.x, locker.z) <= 2.2f)
                {
                    p.hiding = objectId; p.x = locker.x; p.z = locker.z;
                    p.flashlight = p.camera = p.sprinting = false;
                    State.message = p.name + " сховався в шафу."; Dirty = true;
                }
                else return "Підійдіть ближче до шафи.";
                return null;
            }
            var doorData = world.Door(objectId);
            if (doorData != null)
            {
                if (Dist(p.x, p.z, doorData.x, doorData.z) > 2.5f) return "Підійдіть ближче.";
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
                        return "Потрібна картка доступу" + (reqObj != null ? " (" + reqObj.label + ")" : "") + ".";
                    }
                }
                door.open = !door.open;
                State.message = doorData.label + (door.open ? ": відчинено." : ": зачинено.");
                Dirty = true; return null;
            }
            var obj = world.Objective(objectId);
            if (obj == null) return "Невідомий предмет.";
            if (Dist(p.x, p.z, obj.x, obj.z) > 2.5f) return "Підійдіть ближче.";
            if (State.Collected(objectId)) return null;
            if (!State.collected.Contains(objectId)) State.collected.Add(objectId);
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
                State.message = "КАСЕТУ ЗНАЙДЕНО (" + State.tapes.Count + "/3). Диктор чує ваші голоси.";
            }
            else if (objectId.StartsWith("fuse-"))
            {
                State.fuses.Add(objectId);
                if (!p.inventory.Contains(objectId)) p.inventory.Add(objectId);
                State.scare++;
                State.message = "ЗАПОБІЖНИК 380В ЗНАЙДЕНО (" + State.fuses.Count + "/2).";
            }
            else if (objectId == "fuel-canister")
            {
                State.canister = State.quest.fuelAdded = true;
                if (!p.inventory.Contains(objectId)) p.inventory.Add(objectId);
                State.message = "Каністру знайдено.";
            }
            else if (objectId == "pressure-valve") { State.valveOpened = State.quest.valveOpened = true; State.message = "Вентиль тиску відкрито."; }
            else if (objectId == "power")
            {
                if (State.fuses.Count < 2) return "Потрібні 2 запобіжники 380В.";
                State.power = State.quest.power = true;
                State.message = "Живлення відновлено. Передавач готовий.";
            }
            else if (objectId == "broadcast")
            {
                if (State.tapes.Count < 3) return "Для передачі потрібні всі три касети.";
                if (!State.power) return "Спочатку відновіть живлення.";
                if (!State.calibrated) return "Налаштуйте частоту 96.4 МГц.";
                State.broadcast = State.quest.broadcast = true;
                State.enemy.active = !State.creative; State.enemy.bloody = true;
                State.enemy.x = 0; State.enemy.z = -22;
                p.invulnerableUntil = State.elapsed + 5;
                State.message = "ЗАПИС У ЕФІРІ. ПОВЕРТАЙТЕСЯ ДО ФУРГОНА!";
            }
            else if (objectId == "exit")
            {
                if (!State.broadcast) return "Передайте сигнал перед від’їздом.";
                State.phase = "won"; State.enemy.active = false;
                State.message = "Ви вийшли. На записі чути на один голос більше.";
            }
            UpdateQuest(); Dirty = true; return null;
        }

        public string Drop(string id, string itemId)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || State.paused || p == null || p.down) return "Не можна викинути зараз.";
            if (string.IsNullOrEmpty(itemId) || !p.HasItem(itemId)) return "Предмет відсутній в інвентарі.";
            p.inventory.Remove(itemId);
            if (itemId == "flashlight") p.flashlight = false;
            if (itemId.StartsWith("keycard-")) State.keycards.Remove(itemId);
            string dropInstanceId = "drop_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
            string label = GetItemLabel(itemId);
            State.droppedItems.Add(new DroppedItem { id = dropInstanceId, itemId = itemId, label = label, x = p.x, z = p.z });
            State.message = p.name + " викинув: " + label;
            Dirty = true;
            return null;
        }

        public string GetItemLabel(string itemId)
        {
            if (itemId == "flashlight") return "Ліхтар";
            if (itemId == "fuel-canister") return "Каністра";
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
            if (p == null) return;
            p.selectedSlot = Mathf.Clamp(slot, 0, 9);
            Dirty = true;
        }

        public string Calibrate(string id, float frequency)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || p == null || p.down) return "Ефір не активний.";
            if (p == null || Dist(p.x, p.z, 10, -2) > 2.5f) return "Підійдіть до пульта.";
            if (float.IsNaN(frequency) || float.IsInfinity(frequency) || Mathf.Abs(frequency - 96.4f) >= .2f) return "Потрібна несуча 96.4 МГц.";
            State.calibrated = State.quest.calibrated = true;
            State.message = "ЧАСТОТУ НАЛАШТОВАНО (96.4 МГц).";
            Dirty = true; return null;
        }

        public void Radio(string id, int phrase)
        {
            var p = State.Player(id);
            if (p == null || p.down || !string.IsNullOrEmpty(p.hiding)) return;
            var phrases = new[] { "Я тут! До мене!", "ОБЕРЕЖНО! ДИКТОР ПОРУЧ!", "Касету знайдено!", "ПОТРІБНА ДОПОМОГА!" };
            State.radioSequence++;
            State.radioSenderId = id;
            State.message = "РАЦІЯ [" + p.name + "]: " + phrases[Mathf.Clamp(phrase, 0, 3)];
            if (State.enemy.active && Dist(p.x, p.z, State.enemy.x, State.enemy.z) < 18)
            {
                State.enemy.radioX = p.x; State.enemy.radioZ = p.z; State.enemy.radioUntil = State.elapsed + 9;
                State.scare++; State.message += " — ДИКТОР ПОЧУВ РАЦІЮ!";
            }
            Dirty = true;
        }

        void ExitLocker(PlayerState p)
        {
            var locker = world.Locker(p.hiding);
            p.hiding = null;
            if (locker != null) { p.x = locker.exitX; p.z = locker.exitZ; }
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
                State.message = "Сигнал утрачено. Ваші голоси належать станції.";
                // Tick stops immediately after defeat, so the final state cannot
                // wait for the next periodic publish tick.
                Dirty = true;
            }
        }

        public void Tick(float dt)
        {
            if (State.phase != "playing" || State.paused) return;
            dt = Mathf.Min(dt, .25f);
            State.elapsed += dt;
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
                if (p.down) continue;
                if (State.creative) { p.sanity = 100; continue; }
                if (!string.IsNullOrEmpty(p.hiding)) { p.sanity = Mathf.Min(100, p.sanity + 2.5f * dt); continue; }
                float drain = p.flashlight ? 0 : 1.4f;
                float d = Dist(p.x, p.z, State.enemy.x, State.enemy.z);
                if (State.enemy.active) drain += d < 5 ? 5 : d < 11 ? 2 : 0;
                if (p.sprinting) drain += .9f;
                if (p.camera && State.enemy.stunned) drain += 2.5f;
                var recover = p.flashlight && !p.sprinting ? .6f : 0;
                bool hasCompanion = false;
                for (int j = 0; j < State.players.Count; j++)
                {
                    var q = State.players[j];
                    if (q != p && !q.down && Dist(p.x, p.z, q.x, q.z) < 4) { hasCompanion = true; break; }
                }
                if (hasCompanion) recover += 2.2f;
                p.sanity = Mathf.Clamp(p.sanity + (recover - drain * world.GetClass(p.characterClass).bonuses.sanityResistance) * dt, 0, 100);
            }
            if (State.enemy.active) UpdateEnemy(dt);
            nextPublish += dt;
            if (nextPublish >= .1f) { Dirty = true; nextPublish = 0; }
        }

        void UpdateEnemy(float dt)
        {
            var enemy = State.enemy;
            bool hasLiving = false;
            bool isStunned = false;
            PlayerState target = null;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                if (p.down) continue;
                hasLiving = true;

                if (!isStunned && p.flashlight && p.camera && string.IsNullOrEmpty(p.hiding))
                {
                    var d = Dist(p.x, p.z, enemy.x, enemy.z);
                    if (d <= 9 && d >= .02f)
                    {
                        var forward = new Vector2(-Mathf.Sin(p.yaw), -Mathf.Cos(p.yaw));
                        var toward = new Vector2(enemy.x - p.x, enemy.z - p.z).normalized;
                        if (Vector2.Dot(forward, toward) > .9f) isStunned = true;
                    }
                }

                if (string.IsNullOrEmpty(p.hiding))
                {
                    float d = Dist(p.x, p.z, enemy.x, enemy.z);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        target = p;
                    }
                }
            }

            if (!hasLiving) { CheckDefeat(); return; }
            enemy.stunned = isStunned;
            if (enemy.stunned) return;
            if (target == null && State.elapsed >= enemy.radioUntil) return;

            float targetX = State.elapsed < enemy.radioUntil ? enemy.radioX : target.x;
            float targetZ = State.elapsed < enemy.radioUntil ? enemy.radioZ : target.z;
            var destination = new Vector2(targetX, targetZ);
            var current = new Vector2(enemy.x, enemy.z);

            // Route through the central corridor and an open room door if a wall blocks sight.
            if (target != null && Mathf.Abs(target.x) > 2.5f && Mathf.Abs(enemy.x) < 2.5f)
            {
                float nearest = -1.5f;
                float minDiff = Mathf.Abs(-1.5f - target.z);
                float diff16 = Mathf.Abs(-16.5f - target.z);
                if (diff16 < minDiff) { minDiff = diff16; nearest = -16.5f; }
                float diff31 = Mathf.Abs(-31.5f - target.z);
                if (diff31 < minDiff) { minDiff = diff31; nearest = -31.5f; }
                float diff46 = Mathf.Abs(-46.5f - target.z);
                if (diff46 < minDiff) { nearest = -46.5f; }

                if (Mathf.Abs(enemy.z - nearest) > 1) destination = new Vector2(0, nearest);
                else destination = new Vector2(Mathf.Sign(target.x) * 2.9f, nearest);
            }
            var step = Vector2.MoveTowards(current, destination, (State.broadcast ? 4.15f : 2.5f) * dt);
            if (Walkable(step.x, step.y)) { enemy.x = step.x; enemy.z = step.y; }

            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                if (p.down || !string.IsNullOrEmpty(p.hiding) || State.elapsed < p.invulnerableUntil) continue;
                if (Dist(p.x, p.z, enemy.x, enemy.z) < 1.15f)
                {
                    p.down = true; p.sanity = 0; State.scare++;
                    State.message = p.name + " втратив зв’язок. Підніміть напарника!";
                }
            }
            CheckDefeat();
        }

        public void ClearDirty() { Dirty = false; }
    }
}
