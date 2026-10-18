using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirUI
    {
        UnityEngine.UI.Text blackpineMission, blackpineProgress, blackpineCaption, blackpineSubtitle, frequencyHint, signalText;
        GameObject blackpineSubtitlePanel;
        int seenEvidence, seenEcho;
        string observedBlackpineRun, manualEvidence;
        float subtitleUntil;
        string lastBlackpineMission;

        public void SetBlackpineCaptureHud(bool shown)
        {
            interfaceGroup.alpha=shown?1:0;
            interfaceGroup.blocksRaycasts=shown;
        }

        void BuildBlackpineHud()
        {
            blackpineMission = Text(hudPanel.transform, "Blackpine current objective", "", 18, 58, 46, 690, 92);
            blackpineMission.color = Ink;
            blackpineMission.raycastTarget = false;
            blackpineProgress = Text(hudPanel.transform, "Blackpine operation progress", "", 19, 580, 732, 760, 42, TextAnchor.MiddleCenter);
            blackpineProgress.color = new Color(.78f,.91f,.88f);
            blackpineProgress.raycastTarget = false;
            blackpineSubtitlePanel = Fill(hudPanel.transform, "Recovered recording subtitle", 350, 603, 1220, 127, new Color(.018f,.028f,.035f,.89f)).gameObject;
            blackpineSubtitlePanel.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            blackpineCaption = Text(blackpineSubtitlePanel.transform, "Recording source", "", 15, 25, 12, 1170, 25);
            blackpineCaption.color = new Color(.69f,.83f,.84f);
            blackpineCaption.raycastTarget = false;
            blackpineSubtitle = Text(blackpineSubtitlePanel.transform, "Recording transcript", "", 21, 25, 42, 1170, 77);
            blackpineSubtitle.color = Ink;
            blackpineSubtitle.raycastTarget = false;
            blackpineSubtitlePanel.SetActive(false);
        }

        public void ShowBlackpineEvidence(string id)
        {
            var state = session.State;
            if (state == null || DeadAirChapters.IsChapter2(state.chapterId) || !state.tapes.Contains(id)) return;
            manualEvidence = id;
            ShowBlackpineSubtitle(DeadAirBlackpine.EvidenceTitle(id), DeadAirBlackpine.EvidenceText(id,state), 22);
            DeadAirBlackpinePresentation.PlayEvidence(id, state);
        }

        void ShowBlackpineSubtitle(string caption, string text, float seconds)
        {
            blackpineCaption.text = caption;
            blackpineSubtitle.text = text;
            subtitleUntil = Time.unscaledTime + seconds;
            blackpineSubtitlePanel.SetActive(true);
        }

        void RefreshBlackpineHud(RoomState state, PlayerState actor)
        {
            bool enabled = !DeadAirChapters.IsChapter2(state.chapterId) && state.blackpine != null;
            blackpineMission.gameObject.SetActive(enabled);
            blackpineProgress.gameObject.SetActive(enabled);
            if (!enabled) { blackpineSubtitlePanel.SetActive(false); return; }
            var progress = state.blackpine;
            string run = state.code + "/" + state.chapterId;
            if (observedBlackpineRun != run || state.elapsed < .3f)
            {
                observedBlackpineRun = run;
                seenEvidence = seenEcho = 0;
                manualEvidence = null;
                subtitleUntil = 0;
            }
            string objective = DeadAirBlackpine.ObjectiveText(state);
            if (lastBlackpineMission != objective)
            {
                lastBlackpineMission = objective;
                blackpineMission.text = "BLACKPINE / ЧУЖИЙ ЕФІР\n" + objective;
            }
            if (progress.evidenceSerial != seenEvidence)
            {
                seenEvidence = progress.evidenceSerial;
                if (state.elapsed - progress.evidenceAt < 24)
                    ShowBlackpineSubtitle(DeadAirBlackpine.EvidenceTitle(progress.evidenceId), DeadAirBlackpine.EvidenceText(progress.evidenceId,state), 22);
            }
            if (progress.echoSerial != seenEcho)
            {
                seenEcho = progress.echoSerial;
                if (state.elapsed - progress.echoAt < 6)
                {
                    var speaker = state.Player(progress.echoPlayerId);
                    string[] phrases = { "До мене", "Він поруч", "Касета тут", "Допоможіть" };
                    ShowBlackpineSubtitle("РАЦІЯ / " + (speaker != null ? speaker.name : "НЕВІДОМИЙ ГОЛОС"),
                        "«" + phrases[Mathf.Clamp(progress.echoPhrase,0,3)] + "»", 5.5f);
                }
            }
            if (blackpineSubtitlePanel.activeSelf && Time.unscaledTime >= subtitleUntil)
                blackpineSubtitlePanel.SetActive(false);
            if (!string.IsNullOrEmpty(progress.operationId))
            {
                float value = progress.operationId == DeadAirBlackpine.AntennaId ? progress.antennaProgress :
                    progress.operationId == DeadAirBlackpine.ResonatorId ? progress.resonatorProgress : progress.interceptProgress;
                string operation = progress.operationId == DeadAirBlackpine.AntennaId ? "ВІД’ЄДНАННЯ АНТЕНИ" :
                    progress.operationId == DeadAirBlackpine.ResonatorId ? "ЗУПИНКА РЕЗОНАТОРА" : "ПЕРЕХОПЛЕННЯ";
                blackpineProgress.text = operation + "   " + Mathf.RoundToInt(Mathf.Clamp01(value / DeadAirBlackpine.OperationSeconds(progress.operationId)) * 100) + "%  ·  залишайтеся біля пульта";
            }
            else blackpineProgress.text = DeadAirFarmyard.IsCarrying(state,actor) ? "КУРКА В РУКАХ  ·  G — поставити  ·  друг може забрати через E" : "";
        }

        void UpdateBlackpineTuning(float value)
        {
            if (signalText == null || session.State == null) return;
            float distance = Mathf.Abs(value-DeadAirBlackpine.TargetFrequency(session.State));
            int strength = Mathf.RoundToInt(Mathf.Clamp01(1-distance/2.4f)*100);
            signalText.text = distance < .18f ? "НЕСУЧУ ЗНАЙДЕНО / ГОЛОС У ШУМІ" : "ЯКІСТЬ СИГНАЛУ   " + strength + "%";
            signalText.color = distance < .18f ? new Color(.65f,.86f,.71f) : Muted;
        }
    }
}
