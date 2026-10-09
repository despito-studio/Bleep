using System;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirAudio
    {
        readonly AudioSource[] roomVoices = new AudioSource[2];
        string roomKey, activeRoomKey;
        int roomTarget;

        void LoadImportedCues()
        {
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Cues"))
            {
                if (clips.ContainsKey(clip.name)) throw new InvalidOperationException("Duplicate audio cue: " + clip.name);
                clips.Add(clip.name, clip);
            }
            foreach (string key in new[] { "hum", "air", "static", "dread", "tinnitus", "generator-bed",
                "fluorescent-bed", "mushroom-bed", "screamer", "spider", "scare", "role-operator",
                "role-engineer", "role-journalist", "role-dispatcher", "fall-operator", "fall-engineer",
                "fall-journalist", "fall-dispatcher" })
                if (!clips.ContainsKey(key)) throw new InvalidOperationException("Imported audio cue missing: " + key);
        }

        void InitializeRooms()
        {
            for (int i = 0; i < roomVoices.Length; i++)
            {
                roomVoices[i] = Source("Room ambience crossfade " + i, true);
                roomVoices[i].priority = 185;
            }
        }

        public void SetRoomContext(string roomName, float floorY)
        {
            if (floorY < -1) roomKey = "room-basement";
            else if (roomName != null && roomName.Contains("АРХІВ")) roomKey = "room-archive";
            else if (roomName != null && roomName.Contains("СТУДІЯ 04")) roomKey = "room-studio04";
            else if (roomName != null && roomName.Contains("ВЕНТВУЗОЛ")) roomKey = "room-ventilation";
            else roomKey = outside ? "room-backyard" : "room-corridor";
        }

        void TickRooms(bool paused, float gain)
        {
            if (!paused && roomKey != activeRoomKey && clips.TryGetValue(roomKey ?? "", out var clip))
            {
                roomTarget = roomVoices[0].volume <= roomVoices[1].volume ? 0 : 1;
                var next = roomVoices[roomTarget];
                next.Stop(); next.volume = 0; next.clip = clip;
                next.Play(); activeRoomKey = roomKey;
            }
            for (int i = 0; i < roomVoices.Length; i++)
            {
                var source = roomVoices[i];
                Fade(source, !paused && i == roomTarget && source.clip != null ? gain * .28f : 0, .9f);
                if ((paused || i != roomTarget) && source.volume < .0001f)
                {
                    source.volume = 0;
                    if (source.isPlaying) source.Stop();
                }
                else if (!paused && i == roomTarget && source.clip != null && !source.isPlaying) source.Play();
            }
        }

        void DuckRooms(float factor)
        {
            foreach (var source in roomVoices) if (source != null) source.volume *= factor;
        }

        public void PlayCharacterFall(string characterClass, Vector3? position)
        {
            string key = "fall-" + characterClass;
            Emit(clips.ContainsKey(key) ? key : "down", 1, 0, position);
        }

        public DeadAirMusicDiagnostics CaptureMusicDiagnostics() => adaptiveScore.CaptureDiagnostics();
    }
}
