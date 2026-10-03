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
                y = world.spawn.y,
                z = world.spawn.z + (index >= 2 ? .42f : 0),
                flashlight = true
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
        }

        bool CanUseRoleItem(PlayerState player)
        {
            return State.phase == "playing" && !State.paused && player != null &&
                !player.down && string.IsNullOrEmpty(player.hiding);
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
            if (State.phase != "playing" || player == null || player.down || !string.IsNullOrEmpty(player.hiding)) return;
            player.mapOpen = open;
            if (open)
            {
                player.camera = player.uniqueItemEquipped = player.flashlight = false;
                player.uniqueActionCancelled = true;
            }
            else player.uniqueItemEquipped = player.HasSelectedRoleItem && !player.camera;
            Dirty = true;
        }

        public string UseUnique(string id)
        {
            var player = State.Player(id);
            if (!CanUseRoleItem(player)) return "Особистий предмет зараз недоступний.";
            if (!player.uniqueItemEquipped || !player.HasSelectedRoleItem || player.camera || player.mapOpen)
                return "Оберіть особистий предмет в інвентарі.";
            bool cigarettes = player.characterClass == "operator";
            if (cigarettes && player.cigarettesRemaining <= 0) return "Пачка сигарет порожня.";
            float cooldown = cigarettes ? 7f : 2.5f;
            if (State.elapsed - player.uniqueActionAt < cooldown) return "Дочекайтеся завершення дії.";
            if (cigarettes) player.cigarettesRemaining--;
            player.uniqueActionAt = State.elapsed;
            player.uniqueActionSequence++;
            player.uniqueActionCancelled = false;
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
            State.tapes.Clear(); State.fuses.Clear(); State.keycards.Clear();
            State.collected.Clear();
            State.droppedItems.Clear();
            State.canister = State.valveOpened = State.calibrated = State.power = State.broadcast = false;
            State.quest = new QuestState();
            State.enemy = new EnemyState { x = 0, y = 0, z = -34 };
            enemyRoute.Clear();enemyRouteCursor=0;nextRouteAttempt=0;
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
                p.y = world.spawn.y;
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
                ResetRoleItem(p);
                p.moveSequence = 0;
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

        public void Move(string id, NetPacket input)
        {
            if (State.phase != "playing" || State.paused) return;
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
            if (float.IsNaN(input.x) || float.IsInfinity(input.x) ||
                float.IsNaN(input.y) || float.IsInfinity(input.y) ||
                float.IsNaN(input.z) || float.IsInfinity(input.z) ||
                float.IsNaN(input.yaw) || float.IsInfinity(input.yaw) ||
                float.IsNaN(input.pitch) || float.IsInfinity(input.pitch)) return;
            if (Mathf.Abs(input.x) > 100 || Mathf.Abs(input.z) > 100 || input.y < -.5f || input.y > 20) return;
            if (string.IsNullOrEmpty(p.hiding))
            {
                var delta = Vector3.Distance(new Vector3(p.x, p.y, p.z), new Vector3(input.x, input.y, input.z));
                if (!MovementWithinBudget(p, delta) || !Walkable(input.x, input.y, input.z) ||
                    SegmentBlocked(p.x, p.y, p.z, input.x, input.y, input.z)) return;
                movementBudgets[id].distance=Mathf.Max(0,movementBudgets[id].distance-delta);
                p.x = input.x; p.y = input.y; p.z = input.z;
                p.yaw = input.yaw; p.pitch = Mathf.Clamp(input.pitch, -1.35f, 1.35f);
            }
            else
            {
                var locker = world.Locker(p.hiding);
                float centerYaw = locker != null ? locker.FacingYaw : p.yaw;
                p.yaw = centerYaw + Mathf.Clamp(Mathf.DeltaAngle(centerYaw * Mathf.Rad2Deg, input.yaw * Mathf.Rad2Deg), -18, 18) * Mathf.Deg2Rad;
                p.pitch = Mathf.Clamp(input.pitch, -14 * Mathf.Deg2Rad, 14 * Mathf.Deg2Rad);
            }
            p.flashlight = input.flashlight && string.IsNullOrEmpty(p.hiding) && p.HasItem("flashlight");
            p.camera = input.camera && string.IsNullOrEmpty(p.hiding);
            p.mapOpen = input.mapOpen && string.IsNullOrEmpty(p.hiding);
            if (p.mapOpen) p.camera = p.flashlight = false;
            if (p.mapOpen || p.camera || !string.IsNullOrEmpty(p.hiding))
            {
                p.uniqueItemEquipped = false;
                p.uniqueActionCancelled = true;
            }
            else p.uniqueItemEquipped = p.HasSelectedRoleItem;
            p.sprinting = input.sprinting && string.IsNullOrEmpty(p.hiding);
        }

        float MaximumMovementSpeed(PlayerState actor) => Mathf.Max(2.6f, 4.5f * world.GetClass(actor.characterClass).bonuses.speedMultiplier) * 1.12f;

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
            if (x < world.courtyard.minX + .2f || x > world.courtyard.maxX - .2f ||
                z > world.courtyard.maxZ - .2f || z < world.courtyard.minZ + .2f) return false;
            if (world.stairs != null)
                foreach (var stair in world.stairs)
                    if (stair.Contains(x, z, .12f)) return Mathf.Abs(y - stair.HeightAt(z)) <= .4f;
            if (Mathf.Abs(y) <= .25f) return true;
            foreach (var room in world.rooms)
                if (Mathf.Abs(y - room.floorY) <= .25f && x >= room.minX && x <= room.maxX &&
                    z >= room.minZ && z <= room.maxZ) return true;
            return false;
        }

        bool SegmentBlocked(float ax, float ay, float az, float bx, float by, float bz)
        {
            // The host sees the same imported collision geometry as its local controller.
            // A torso-height ray catches walls even if a packet skips across their thin collider.
            if (PhysicalSegmentBlocked(new Vector3(-ax, ay + .75f, az), new Vector3(-bx, by + .75f, bz))) return true;
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
                if(ignoreDoors)
                {
                    bool door=false;
                    for(var part=collider.transform;part!=null;part=part.parent)if(part.name.StartsWith("door:")){door=true;break;}
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
            var offset = target - origin;
            if (offset.sqrMagnitude < .0001f) return true;
            int count = Physics.RaycastNonAlloc(origin, offset.normalized, sightHits, offset.magnitude,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                bool barrier = false, isTarget = false;
                for (var part = sightHits[i].collider.transform; part != null; part = part.parent)
                {
                    if (part.name == "shell" || part.name.StartsWith("door:") || part.name.StartsWith("Door frame ")) barrier = true;
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
            if (string.IsNullOrEmpty(objectId)) return "Невідомий предмет.";
            if (objectId == "exit-locker")
            {
                if (!string.IsNullOrEmpty(p.hiding)) ExitLocker(p);
                return null;
            }
            if (!string.IsNullOrEmpty(p.hiding)) return "Спочатку вийдіть із шафи.";
            if (objectId.StartsWith("dropped:"))
            {
                string droppedId = objectId.Substring(8);
                var dropped = State.droppedItems.Find(d => d.id == droppedId);
                if (dropped == null) return "Предмет уже забрали.";
                if (!Near(p, dropped.x, dropped.y, dropped.z, 2.5f, 1)) return "Підійдіть ближче.";
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
                        return "Потрібна картка доступу" + (reqObj != null ? " (" + reqObj.label + ")" : "") + ".";
                    }
                }
                door.open = !door.open;
                State.message = doorData.label + (door.open ? ": відчинено." : ": зачинено.");
                Dirty = true; return null;
            }
            var obj = world.Objective(objectId);
            if (obj == null) return "Невідомий предмет.";
            if (!Near(p, obj.x, obj.y, obj.z, 2.5f, 0, obj.id)) return "Підійдіть ближче.";
            if (State.Collected(objectId)) return null;
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
                State.enemy.x = 0; State.enemy.y = p.y; State.enemy.z = -22;
                p.invulnerableUntil = State.elapsed + 5;
                State.message = "ЗАПИС У ЕФІРІ. ПОВЕРТАЙТЕСЯ ДО ФУРГОНА!";
            }
            else if (objectId == "exit")
            {
                if (!State.broadcast) return "Передайте сигнал перед від’їздом.";
                State.phase = "won"; State.enemy.active = false;
                State.message = "Ви вийшли. На записі чути на один голос більше.";
            }
            // Rejected objective interactions must remain available for a later retry.
            if (!State.collected.Contains(objectId)) State.collected.Add(objectId);
            UpdateQuest(); Dirty = true; return null;
        }

        public string Drop(string id, string itemId)
        {
            var p = State.Player(id);
            if (State.phase != "playing" || State.paused || p == null || p.down || !string.IsNullOrEmpty(p.hiding)) return "Не можна викинути зараз.";
            if (string.IsNullOrEmpty(itemId) || !p.HasItem(itemId)) return "Предмет відсутній в інвентарі.";
            if(DeadAirRoleItems.IsRoleItem(itemId))return "Особистий предмет залишається з персонажем.";
            int removedSlot=p.inventory.IndexOf(itemId);
            p.inventory.Remove(itemId);
            if(removedSlot<p.selectedSlot)p.selectedSlot--;
            p.selectedSlot = Mathf.Clamp(p.selectedSlot, 0, Mathf.Max(0, p.inventory.Count - 1));
            p.uniqueItemEquipped=p.HasSelectedRoleItem&&!p.camera&&!p.mapOpen;
            if(!p.uniqueItemEquipped)p.uniqueActionCancelled=true;
            if (itemId == "flashlight") p.flashlight = false;
            if (itemId.StartsWith("keycard-")) State.keycards.Remove(itemId);
            string dropInstanceId = "drop_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
            string label = GetItemLabel(itemId);
            State.droppedItems.Add(new DroppedItem { id = dropInstanceId, itemId = itemId, label = label, x = p.x, y = p.y, z = p.z });
            State.message = p.name + " викинув: " + label;
            Dirty = true;
            return null;
        }

        public string GetItemLabel(string itemId)
        {
            if (itemId == "flashlight") return "Ліхтар";
            if (itemId == "fuel-canister") return "Каністра";
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
            if (p == null || p.down || !string.IsNullOrEmpty(p.hiding)) return;
            var phrases = new[] { "Я тут! До мене!", "ОБЕРЕЖНО! ДИКТОР ПОРУЧ!", "Касету знайдено!", "ПОТРІБНА ДОПОМОГА!" };
            State.radioSequence++;
            State.radioSenderId = id;
            State.message = "РАЦІЯ [" + p.name + "]: " + phrases[Mathf.Clamp(phrase, 0, 3)];
            if (State.enemy.active && Dist(p.x, p.z, State.enemy.x, State.enemy.z) < 18)
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
            if (locker != null) { p.x = locker.exitX; p.y = locker.ExitFloorY; p.z = locker.exitZ; p.yaw = locker.FacingYaw; p.pitch = 0; }
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
                if (State.enemy.active && SameFloor(p.y, State.enemy.y)) drain += d < 5 ? 5 : d < 11 ? 2 : 0;
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
                    if (SameFloor(p.y, enemy.y) && d <= 9 && d >= .02f)
                    {
                        var forward = new Vector2(-Mathf.Sin(p.yaw), -Mathf.Cos(p.yaw));
                        var toward = new Vector2(enemy.x - p.x, enemy.z - p.z).normalized;
                        if (Vector2.Dot(forward, toward) > .9f) isStunned = true;
                    }
                }

                if (string.IsNullOrEmpty(p.hiding))
                {
                    float d = Vector3.Distance(new Vector3(p.x, p.y, p.z), new Vector3(enemy.x, enemy.y, enemy.z));
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
            float targetY = State.elapsed < enemy.radioUntil ? enemy.radioY : target.y;
            float targetZ = State.elapsed < enemy.radioUntil ? enemy.radioZ : target.z;
            var destination = EnemyWaypoint(enemy, targetX, targetY, targetZ);
            var current = new Vector2(enemy.x, enemy.z);
            foreach (var doorData in world.doors)
            {
                var door = State.Door(doorData.id);
                if (door != null && !door.locked && !door.open && Mathf.Abs(enemy.y + 1.4f - doorData.y) < 1 &&
                    Dist(enemy.x, enemy.z, doorData.x, doorData.z) < 1.5f) { door.open = true; Dirty = true; }
            }
            var step = Vector2.MoveTowards(current, destination, (State.broadcast ? 4.15f : 2.5f) * dt);
            float stepY = EnemyFloorHeight(step.x, step.y, enemy.y);
            if (Walkable(step.x, stepY, step.y) && !SegmentBlocked(enemy.x, enemy.y, enemy.z, step.x, stepY, step.y))
            { enemy.x = step.x; enemy.y = stepY; enemy.z = step.y; }

            for (int i = 0; i < State.players.Count; i++)
            {
                var p = State.players[i];
                if (p.down || !string.IsNullOrEmpty(p.hiding) || State.elapsed < p.invulnerableUntil) continue;
                if (SameFloor(p.y, enemy.y) && Dist(p.x, p.z, enemy.x, enemy.z) < 1.15f)
                {
                    p.down = true; p.sanity = 0; State.scare++;
                    p.camera = p.flashlight = p.sprinting = p.mapOpen = p.uniqueItemEquipped = false;
                    p.uniqueActionCancelled = true;
                    State.message = p.name + " втратив зв’язок. Підніміть напарника!";
                }
            }
            CheckDefeat();
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
                    // Wall and furniture clearance are baked into the graph. Door state
                    // remains dynamic, so a key-locked portal cannot be a valid edge.
                    if(NavigationDoorBlocked(from,to))continue;
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
                foreach (var stair in world.stairs)
                {
                    if (stair.Contains(x, z, .12f)) return stair.HeightAt(z);
                    if (Mathf.Abs(currentY - stair.lowerY) < .4f) return stair.lowerY;
                    if (Mathf.Abs(currentY - stair.upperY) < .4f) return stair.upperY;
                }
            return currentY;
        }

        public void ClearDirty() { Dirty = false; }
    }
}
