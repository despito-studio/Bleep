using System;
using System.Linq;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirRules
    {
        float nextBlackpineEcho, nextOperationNoise;
        bool blackpineFinalEchoPending;
        int nextDropSerial;
        bool IsBlackpine => DeadAirBlackpine.Active(State);
        int RequiredFuel => IsBlackpine ? DeadAirBlackpine.RequiredFuel : 3;
        bool CarryingChicken(PlayerState actor) => IsBlackpine && actor != null && State.blackpine.chickenCarrierId == actor.id;

        void ResetBlackpine()
        {
            State.blackpine = new BlackpineState();
            nextBlackpineEcho = float.PositiveInfinity;
            blackpineFinalEchoPending = false;
            nextOperationNoise = 0;
            if (IsBlackpine) nextScare = float.PositiveInfinity;
        }

        void BeginBlackpineBuildUp()
        {
            if (IsBlackpine && float.IsPositiveInfinity(nextScare)) nextScare = State.elapsed + 28;
        }

        void RevealBlackpineEvidence(string id)
        {
            var chapter = State.blackpine;
            chapter.evidenceSerial++;
            chapter.evidenceId = id;
            chapter.evidenceAt = State.elapsed;
            BeginBlackpineBuildUp();
            State.message = "ЗАПИС «" + DeadAirBlackpine.EvidenceTitle(id) + "»: " + DeadAirBlackpine.EvidenceText(id, State);
        }

        string BeginBlackpineOperation(PlayerState actor, string operation)
        {
            var chapter = State.blackpine;
            var objective = operation == "broadcast" ? world.Objective("broadcast") : DeadAirBlackpine.Objective(world, operation);
            if (objective == null) return "Невідомий прилад.";
            if (!Near(actor, objective.x, objective.y, objective.z, 2.5f, 0, objective.id)) return "Підійдіть ближче до приладу.";
            if (CarryingChicken(actor)) return "Перед роботою з приладом передайте курку другу або поставте її на землю (G).";
            if (operation == "broadcast")
            {
                if (chapter.intercepted) return "Сигнал уже перехоплено. Вимкніть антенний вузол і Резонатор.";
                if (!State.power || State.fuses.Count < 2 || !State.breakerOn) return "Приймачу потрібні генератор, два запобіжники та увімкнений рубильник у підвалі.";
                if (!DeadAirBlackpine.HasAllEvidence(State)) return "Спершу дослідіть три записи останньої зміни — вони пояснять, яку несучу треба перехопити.";
                if (!State.calibrated) return "Налаштуйте приймач за частотою із запису «Останній ефір».";
            }
            else
            {
                if (!chapter.intercepted) return "Перехопіть сигнал в апаратній, щоб визначити джерело.";
                if (operation == DeadAirBlackpine.AntennaId && chapter.antennaDisconnected) return "Антенний вузол уже роз'єднано.";
                if (operation == DeadAirBlackpine.ResonatorId)
                {
                    if (chapter.resonatorStopped) return "Резонатор уже зупинено. Повертайтеся до фургона.";
                    if (!chapter.antennaDisconnected) return "Спочатку роз'єднайте антенний вузол у задньому дворі: він знову запускає петлю.";
                }
            }
            if (!string.IsNullOrEmpty(chapter.operationPlayerId) && chapter.operationPlayerId != actor.id)
            {
                var operatorPlayer = State.Player(chapter.operationPlayerId);
                if (ValidBlackpineOperator(operatorPlayer, chapter.operationId)) return "З приладом уже працює " + operatorPlayer.name + ". Прикрийте напарника.";
            }
            chapter.operationId = operation;
            chapter.operationPlayerId = actor.id;
            actor.sprinting = actor.camera = actor.mapOpen = actor.uniqueItemEquipped = false;
            actor.uniqueActionCancelled = true;
            nextOperationNoise = State.elapsed;
            State.message = operation == "broadcast" ? "ПРИЙОМ: захоплення чужої несучої. Залишайтеся біля пульта; можна відійти й продовжити пізніше." :
                operation == DeadAirBlackpine.AntennaId ? "АНТЕНА: роз'єднуєте кабель. Тріск видає ваше місце — прикрийте оператора!" :
                "РЕЗОНАТОР: розриваєте петлю живлення. Залишайтеся біля приладу; прогрес зберігається.";
            Dirty = true;
            return null;
        }

        bool ValidBlackpineOperator(PlayerState actor, string operation)
        {
            if (actor == null || actor.down || actor.ladderClimbing || !string.IsNullOrEmpty(actor.hiding) || CarryingChicken(actor)) return false;
            var objective = operation == "broadcast" ? world.Objective("broadcast") : DeadAirBlackpine.Objective(world, operation);
            return objective != null && Near(actor, objective.x, objective.y, objective.z, 2.5f, 0, objective.id) &&
                (operation != "broadcast" || State.power && State.breakerOn && State.fuses.Count >= 2);
        }

        void ClearBlackpineOperation()
        {
            State.blackpine.operationId = State.blackpine.operationPlayerId = null;
            Dirty = true;
        }

        void TickBlackpine(float dt)
        {
            TickFarmyard();
            var chapter = State.blackpine;
            if (!string.IsNullOrEmpty(chapter.operationPlayerId))
            {
                var actor = State.Player(chapter.operationPlayerId);
                string operation = chapter.operationId;
                if (!ValidBlackpineOperator(actor, operation))
                {
                    ClearBlackpineOperation();
                    State.message = "Роботу призупинено. Прогрес збережено; поверніться до приладу й натисніть E.";
                }
                else
                {
                    if (operation == "broadcast") chapter.interceptProgress = Mathf.Min(DeadAirBlackpine.CaptureSeconds, chapter.interceptProgress + dt);
                    else if (operation == DeadAirBlackpine.AntennaId) chapter.antennaProgress = Mathf.Min(DeadAirBlackpine.AntennaSeconds, chapter.antennaProgress + dt);
                    else chapter.resonatorProgress = Mathf.Min(DeadAirBlackpine.ResonatorSeconds, chapter.resonatorProgress + dt);
                    if (State.elapsed >= nextOperationNoise)
                    {
                        nextOperationNoise = State.elapsed + 2;
                        HearNoise(actor.x, actor.y, actor.z, operation == DeadAirBlackpine.AntennaId ? 58 : 38, 12);
                    }
                    if (operation == "broadcast" && chapter.interceptProgress >= DeadAirBlackpine.CaptureSeconds) CompleteBlackpineInterception();
                    else if (operation == DeadAirBlackpine.AntennaId && chapter.antennaProgress >= DeadAirBlackpine.AntennaSeconds)
                    {
                        chapter.antennaDisconnected = true; ClearBlackpineOperation();
                        State.scare++;
                        State.message = "АНТЕННИЙ ВУЗОЛ РОЗ'ЄДНАНО. Голос стих у дворі й відповів знизу. Зупиніть Резонатор у підвалі.";
                    }
                    else if (operation == DeadAirBlackpine.ResonatorId && chapter.resonatorProgress >= DeadAirBlackpine.ResonatorSeconds)
                    {
                        chapter.resonatorStopped = true; ClearBlackpineOperation();
                        State.broadcast = State.quest.broadcast = true; // Legacy compatibility: source stopped, no outbound transmission.
                        State.enemy.bloody = true;
                        State.scare++;
                        blackpineFinalEchoPending = true;
                        nextBlackpineEcho = State.elapsed + 3;
                        State.message = "РЕЗОНАТОР ЗУПИНЕНО. Станція замовкла… але голос перейшов у рацію. Збирайте друзів і повертайтеся до фургона!";
                    }
                    UpdateQuest(); Dirty = true;
                }
            }
            if (chapter.intercepted && (blackpineFinalEchoPending || !State.creative) && State.elapsed >= nextBlackpineEcho)
            {
                PublishBlackpineEcho();
                blackpineFinalEchoPending = false;
                nextBlackpineEcho = State.elapsed + random.Next(24, 39);
            }
        }

        void CompleteBlackpineInterception()
        {
            var chapter = State.blackpine;
            chapter.intercepted = true; ClearBlackpineOperation();
            State.spawnCountdown = 0;
            State.enemy.active = !State.creative;
            // The enemy wakes at its existing physical location. It never teleports to the operator.
            State.enemy.behaviour = "investigate";
            var console = world.Objective("broadcast");
            if (console != null) HearNoise(console.x, console.y - 1.2f, console.z, 80, 20);
            foreach (var actor in State.players) actor.invulnerableUntil = Mathf.Max(actor.invulnerableUntil, State.elapsed + 4);
            nextPeek = State.elapsed + 25;
            nextBlackpineEcho = State.elapsed + 22;
            State.scare++;
            PublishBlackpineEcho();
            State.message = "СИГНАЛ ПЕРЕХОПЛЕНО. Станція відповіла вашим голосом! Джерело — у підвалі. Роз'єднайте антенний вузол у дворі, потім зупиніть Резонатор.";
        }

        void PublishBlackpineEcho()
        {
            var chapter = State.blackpine;
            var speaker = State.Player(chapter.lastRadioPlayerId) ?? State.players.FirstOrDefault(actor => !actor.down);
            chapter.echoPlayerId = speaker != null ? speaker.id : null;
            chapter.echoPhrase = chapter.lastRadioPlayerId != null ? chapter.lastRadioPhrase : random.Next(4);
            chapter.echoAt = State.elapsed; chapter.echoSerial++;
            State.scare++;
            Dirty = true;
        }

        string InteractFarmyard(PlayerState actor, string id)
        {
            var chapter = State.blackpine;
            if (id == "farm-chicken-drop")
            {
                if (!CarryingChicken(actor)) return "Ви не тримаєте курку.";
                ReleaseChicken(actor); State.message = actor.name + " поставив курку на землю."; return null;
            }
            var point = DeadAirFarmyard.Position(world, State, id);
            if (!Near(actor, point.x, point.y, point.z, 2.3f, id == "farm-goat" ? .8f : .45f)) return "Підійдіть ближче до тварини.";
            if (id == "farm-goat")
            {
                if (State.elapsed - chapter.goatPetAt < 2 && chapter.goatPetSerial > 0) return "Коза ще доїдає частування.";
                chapter.goatPetAt = State.elapsed; chapter.goatFriendlyUntil = State.elapsed + 45; chapter.goatPetSerial++;
                actor.sanity = Mathf.Min(100, actor.sanity + 3);
                State.message = actor.name + " пригостив і погладив козу. Вона перестала жувати й дивиться на офіс.";
            }
            else
            {
                if (chapter.chickenRescued) return "Курка вже у фургоні.";
                if (CarryingChicken(actor)) { ReleaseChicken(actor); State.message = actor.name + " поставив курку на землю."; return null; }
                var holder = State.Player(chapter.chickenCarrierId);
                if (holder != null && (holder.down || holder.ladderClimbing || !string.IsNullOrEmpty(holder.hiding) || !Near(actor, holder.x, holder.y, holder.z, 2.3f, 1)))
                    return "Підійдіть до напарника, щоб забрати курку.";
                if (holder != null) RestoreChickenEquipment(holder);
                chapter.chickenCarrierId = actor.id; chapter.chickenDropped = false;
                chapter.chickenSquawkAt = State.elapsed; chapter.chickenSquawkSerial++;
                actor.sprinting = actor.camera = actor.flashlight = actor.mapOpen = actor.uniqueItemEquipped = false;
                actor.selectedSlot = -1;
                actor.uniqueActionCancelled = true;
                State.message = holder == null ? actor.name + " узяв курку на руки. G — поставити; друг може забрати її клавішею E." : actor.name + " забрав курку у " + holder.name + ".";
                HearNoise(actor.x, actor.y, actor.z, 20, 7);
            }
            Dirty = true; return null;
        }

        void ReleaseChicken(PlayerState actor)
        {
            var chapter = State.blackpine;
            chapter.chickenCarrierId = null; chapter.chickenDropped = true; chapter.chickenDroppedAt = State.elapsed;
            chapter.chickenX = actor.ladderClimbing ? -9 : actor.x;
            chapter.chickenY = actor.ladderClimbing ? 0 : actor.y;
            chapter.chickenZ = actor.ladderClimbing ? 7 : actor.z;
            RestoreChickenEquipment(actor);
            Dirty = true;
        }

        static void RestoreChickenEquipment(PlayerState actor)
        {
            actor.selectedSlot = Mathf.Clamp(actor.selectedSlot, 0, Mathf.Max(0, actor.inventory.Count - 1));
        }

        void TickFarmyard()
        {
            var chapter = State.blackpine;
            var carrier = State.Player(chapter.chickenCarrierId);
            if (carrier != null)
            {
                if (carrier.down || !string.IsNullOrEmpty(carrier.hiding)) ReleaseChicken(carrier);
                else
                {
                    chapter.chickenX = carrier.x; chapter.chickenY = carrier.y; chapter.chickenZ = carrier.z;
                    carrier.selectedSlot = -1;
                    carrier.sprinting = carrier.camera = carrier.flashlight = carrier.mapOpen = carrier.uniqueItemEquipped = false;
                    if (State.elapsed - chapter.chickenSquawkAt >= 15) SquawkChicken(carrier.x, carrier.y, carrier.z);
                }
            }
            else if (!chapter.chickenRescued)
            {
                var chicken = DeadAirFarmyard.Position(world, State, "farm-chicken");
                foreach (var actor in State.players)
                    if (!actor.down && actor.sprinting && SameFloor(actor.y, chicken.y) && Dist(actor.x, actor.z, chicken.x, chicken.z) < 4 && State.elapsed - chapter.chickenSquawkAt >= 8)
                    { SquawkChicken(chicken.x, chicken.y, chicken.z); break; }
            }
        }

        void SquawkChicken(float x, float y, float z)
        {
            State.blackpine.chickenSquawkAt = State.elapsed; State.blackpine.chickenSquawkSerial++;
            HearNoise(x, y, z, 30, 10); Dirty = true;
        }

        void RecoverBlackpineDisconnect(PlayerState actor)
        {
            if (!IsBlackpine || actor == null) return;
            if (CarryingChicken(actor)) ReleaseChicken(actor);
            if (State.blackpine.operationPlayerId == actor.id) ClearBlackpineOperation();
            // Keys and the ladder remain obtainable if their carrier loses connection.
            foreach (string item in actor.inventory)
                if (item == "ladder" || item.StartsWith("key-", StringComparison.Ordinal) || item.StartsWith("keycard-", StringComparison.Ordinal))
                    State.droppedItems.Add(new DroppedItem { id = "disconnect_" + (++nextDropSerial), itemId = item, label = GetItemLabel(item),
                        x = actor.ladderClimbing ? -9 : actor.x, y = actor.ladderClimbing ? 0 : actor.y, z = actor.ladderClimbing ? 7 : actor.z });
        }

        void RescueChicken()
        {
            if (!IsBlackpine) return;
            var chapter = State.blackpine;
            var holder = State.Player(chapter.chickenCarrierId);
            var point = DeadAirFarmyard.Position(world, State, "farm-chicken");
            if (holder != null && !holder.down && DeadAirVanPresentation.IsInside(world, holder.x, holder.z, -2.2f) ||
                chapter.chickenDropped && DeadAirVanPresentation.IsInside(world, point.x, point.z, -1))
            {
                if (holder != null) RestoreChickenEquipment(holder);
                chapter.chickenRescued = true; chapter.chickenCarrierId = null; Dirty = true;
            }
        }
    }
}
