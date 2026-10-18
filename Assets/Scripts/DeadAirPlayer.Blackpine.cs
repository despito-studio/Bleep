using UnityEngine;
using System.Collections;
using System.IO;

namespace DeadAir
{
    public sealed partial class DeadAirPlayer
    {
        bool takingBlackpinePhoto;
        IEnumerator TakeBlackpinePhoto()
        {
            if (takingBlackpinePhoto || DeadAirFarmyard.IsCarrying(session.State,session.State?.Player(session.LocalId))) yield break;
            takingBlackpinePhoto = true;
            string folder=Path.Combine(Application.persistentDataPath,"Photos");
            Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"Blackpine-"+System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".png");
            ui.SetBlackpineCaptureHud(false);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            ui.SetBlackpineCaptureHud(true);
            DeadAirAudio.Play("camera-equip");
            ui.Toast("Фото збережено · "+folder);
            takingBlackpinePhoto = false;
        }

        void FindBlackpineInteractions(PlayerState actor, RoomState state, Vector2 origin, Vector2 forward, ref float nearest)
        {
            if (DeadAirChapters.IsChapter2(state.chapterId) || state.blackpine == null) return;
            var progress = state.blackpine;
            if (progress.intercepted)
            {
                foreach (var objective in DeadAirBlackpine.Objectives(world))
                {
                    bool done = objective.id == DeadAirBlackpine.AntennaId ? progress.antennaDisconnected : progress.resonatorStopped;
                    if (done) continue;
                    string label = objective.id == DeadAirBlackpine.AntennaId ? "Роз’єднати антенний вузол" : "Зупинити Резонатор-4";
                    if (progress.operationId == objective.id)
                        label += " · робота триває";
                    ConsiderInteraction(objective.id, label, objective.x, objective.y, objective.z, 2.5f, origin, forward, ref nearest);
                }
            }
            if (!DeadAirFarmyard.IsCarrying(state, actor))
            {
                var goat = DeadAirFarmyard.Position(world, state, DeadAirFarmyard.GoatId);
                ConsiderInteraction(DeadAirFarmyard.GoatId, "Погладити й пригостити козу", goat.x, goat.y + .8f, goat.z, 2.3f, origin, forward, ref nearest);
            }
            if (!progress.chickenRescued && progress.chickenCarrierId != actor.id)
            {
                var chicken = DeadAirFarmyard.Position(world, state, DeadAirFarmyard.ChickenId);
                string label = string.IsNullOrEmpty(progress.chickenCarrierId) ? "Взяти курку на руки" : "Перейняти курку в друга";
                ConsiderInteraction(DeadAirFarmyard.ChickenId, label, chicken.x, chicken.y + .45f, chicken.z, 2.1f, origin, forward, ref nearest);
            }
        }
    }
}
