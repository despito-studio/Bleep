using System;
using UnityEngine;

namespace DeadAir
{
    public sealed partial class DeadAirAudio
    {
        void AddProceduralScoreAndFoley()
        {
            Add("music-menu", b => b.Music(0), 32);
            Add("music-exploration", b => b.Music(1), 32);
            Add("music-tension", b => b.Music(2), 32);
            Add("music-pursuit", b => b.Music(3), 32);
            Add("generator-bed", b => b.MachineBed(false), 8);
            Add("fluorescent-bed", b => b.MachineBed(true), 8);
            Add("mushroom-bed", b => b.MushroomBed(), 8);
            Add("flashlight", b => { b.Knock(0, .55f); b.Noise(.015f, .055f, .06f, 2700, .002f); }, .22f);
            Add("camera-equip", b => { b.Noise(0, .16f, .065f, 1700, .015f); b.Tone(.03f, 1080, 690, .09f, .045f, Wave.Triangle); b.Knock(.16f, .3f); }, .4f);
            Add("equip", b => { b.Noise(0, .16f, .045f, 1200, .018f); b.Knock(.11f, .25f); }, .32f);
            Add("paper", b => { b.Noise(0, .26f, .07f, 2400, .025f); b.Noise(.16f, .15f, .045f, 3900, .02f); }, .4f);
            Add("cassette", b => { b.Knock(0, .45f); b.Knock(.12f, .6f); b.Tone(.19f, 860, 850, .24f, .06f, Wave.Sine, .015f); }, .65f);
            Add("fuse", b => { b.Tone(0, 1350, 1340, .22f, .065f); b.Tone(.05f, 1930, 1910, .17f, .045f); b.Knock(.14f, .55f); }, .5f);
            Add("keycard", b => { b.Noise(0, .12f, .08f, 2000, .007f); b.Tone(.12f, 780, 780, .1f, .055f); b.Tone(.26f, 1040, 1040, .12f, .045f); }, .55f);
            Add("key", b => { b.Tone(0, 2400, 2380, .15f, .05f); b.Tone(.04f, 3200, 3180, .12f, .04f); b.Knock(.08f, .4f); }, .35f);
            Add("ladder", b => { b.Knock(0, .9f); b.Tone(.03f, 320, 280, .25f, .06f, Wave.Triangle); b.Knock(.12f, .7f); }, .4f);
            Add("breaker-toggle", b => { b.Knock(0, 1.4f); b.Tone(.02f, 180, 80, .35f, .08f, Wave.Triangle); b.Noise(.02f, .12f, .14f, 1800, .005f); b.Knock(.08f, .8f); }, .5f);
            Add("drawer-slide", b => { b.Noise(0, .28f, .08f, 750, .03f); b.Knock(.22f, .6f); }, .45f);
            Add("window-shatter", b => { b.Noise(0, .45f, .22f, 4500, .002f); b.Noise(.05f, .6f, .18f, 3200, .01f); b.Tone(0, 850, 420, .3f, .08f, Wave.Saw); b.Knock(0, 1.6f); b.Knock(.15f, .9f); }, .9f);
            Add("fuel-pickup", b => { b.Knock(0, 1.2f); b.Tone(.05f, 230, 125, .38f, .06f, Wave.Triangle); b.Noise(.15f, .35f, .085f, 480, .04f); }, .8f);
            Add("drop", b => { b.Noise(0, .14f, .08f, 1500, .02f); b.Knock(.08f, 1.2f); b.Knock(.24f, .5f); }, .6f);
            Add("valve", b => { for (int i = 0; i < 6; i++) { b.Knock(i * .14f, .5f); b.Tone(i * .14f, 630, 390, .14f, .055f, Wave.Triangle); } b.Noise(.25f, 1.05f, .10f, 900, .06f); }, 1.6f);
            Add("calibrate", b => { b.Noise(0, .42f, .08f, 1600, .03f); b.Tone(.1f, 1850, 910, .38f, .065f); b.Tone(.5f, 660, 660, .22f, .065f); b.Tone(.73f, 880, 880, .3f, .05f); }, 1.2f);
            Add("broadcast-start", b => { b.Knock(0, 1); b.Noise(.06f, .55f, .14f, 1900, .015f); b.Tone(.05f, 55, 46, 1.25f, .16f, Wave.Triangle, .02f); b.Tone(.14f, 311, 293, .85f, .085f, Wave.Saw, .015f); }, 1.7f);
            Add("locker-enter", b => { b.Noise(0, .25f, .1f, 950, .01f); b.Tone(.03f, 210, 110, .3f, .065f, Wave.Triangle); b.Knock(.32f, 1.25f); }, .8f);
            Add("locker-exit", b => { b.Knock(0, 1); b.Tone(.08f, 120, 240, .3f, .06f, Wave.Triangle); b.Noise(.08f, .38f, .08f, 1250, .018f); }, .75f);
            Add("down", b => { b.Tone(0, 87, 25, .8f, .2f, Wave.Triangle, .004f); b.Noise(0, .35f, .16f, 650, .003f); b.Noise(.28f, .6f, .075f, 420, .08f); }, 1.2f);
            Add("revive", b => { b.Noise(0, .4f, .08f, 590, .08f); b.Tone(.16f, 98, 146.83f, .65f, .065f, Wave.Sine, .08f); b.Tone(.4f, 196, 220, .65f, .05f, Wave.Sine, .08f); }, 1.3f);
            Add("monster-stun", b => { b.Noise(0, .4f, .16f, 2300, .005f); b.Tone(0, 520, 70, .65f, .15f, Wave.Saw, .003f); b.Tone(.08f, 81, 32, .65f, .12f, Wave.Triangle); }, 1.05f);
            Add("pursuit", b => { b.Tone(0, 71, 44, .7f, .11f, Wave.Triangle, .018f); b.Tone(.04f, 294, 277, .55f, .06f, Wave.Saw, .025f); }, .9f);
            Add("teke-scream", b => { b.Noise(0, 1.2f, .25f, 3200, .002f); b.Tone(0, 880, 1400, 1.2f, .28f, Wave.Saw, .01f); b.Tone(.05f, 1760, 920, 1.0f, .18f, Wave.Triangle, .02f); b.Noise(.2f, .8f, .15f, 4500, .005f); }, 1.4f);
            Add("teke-groan", b => { b.Tone(0, 220, 140, 1.8f, .22f, Wave.Sine, .04f); b.Tone(.1f, 110, 85, 1.6f, .28f, Wave.Triangle, .03f); b.Noise(.2f, 1.2f, .08f, 950, .08f); }, 1.8f);
            Add("teke-crawl", b => { b.Knock(0, .9f); b.Noise(0, .14f, .12f, 1800, .005f); b.Knock(.16f, .75f); b.Noise(.16f, .12f, .10f, 2200, .005f); }, .35f);
            Add("pill-consume", b => { b.Knock(0, .4f); b.Noise(.02f, .08f, .06f, 3200, .003f); b.Tone(.08f, 540, 720, .18f, .08f, Wave.Sine); b.Knock(.22f, .5f); }, .4f);
            Add("role-operator", b => {
                b.Noise(0, .35f, .075f, 2800, .03f); b.Knock(.58f, .42f);
                b.Noise(.62f, .32f, .11f, 2900, .004f); b.Tone(.62f, 440, 180, .12f, .055f, Wave.Triangle);
                b.Noise(1.4f, .8f, .045f, 620, .1f); b.Noise(3.7f, 1.1f, .055f, 480, .15f);
                b.Noise(5.7f, .7f, .045f, 540, .1f); b.Knock(6.6f, .3f);
            }, 7);
            Add("role-engineer", b => { b.Knock(0, .7f); for (int i = 0; i < 7; i++) { b.Knock(.3f + i * .22f, .6f); b.Noise(.3f + i * .22f, .065f, .07f, 2100, .002f); } b.Tone(1.9f, 1230, 810, .3f, .055f); }, 2.5f);
            Add("role-journalist", b => { b.Knock(0, .65f); b.Tone(.05f, 950, 950, .12f, .065f); b.Noise(.24f, 1.65f, .055f, 1100, .1f); b.Tone(.4f, 120, 112, 1.4f, .045f, Wave.Triangle, .1f); b.Knock(2.1f, .5f); }, 2.5f);
            Add("role-dispatcher", b => { for (int i = 0; i < 5; i++) { b.Tone(i * .35f, 1760, 1750, .28f, .035f); b.Tone(i * .35f, 2349, 2320, .22f, .022f); } b.Tone(.2f, 130.81f, 138.59f, 1.65f, .055f, Wave.Sine, .08f); }, 2.5f);
            for (int s = 0; s < 2; s++)
            {
                float gain = s == 1 ? 1.1f : .8f;
                Add(s == 1 ? "run-step-gravel" : "step-gravel", b => { b.Tone(0, 78, 28, .13f, .075f * gain); b.Noise(0, .16f, .12f * gain, 2100, .003f); b.Noise(.065f, .18f, .07f * gain, 3700, .005f); }, .38f);
                Add(s == 1 ? "run-step-metal" : "step-metal", b => { b.Tone(0, 122, 94, .2f, .085f * gain); b.Tone(.005f, 392, 385, .23f, .04f * gain); b.Noise(0, .11f, .09f * gain, 1250, .002f); }, .4f);
            }
        }

        sealed partial class Instrument
        {
            static float Note(int midi) => 440 * Mathf.Pow(2, (midi - 69) / 12f);

            // The tail wraps into the next bar, preserving release and reverb at the loop seam.
            void Pad(float at, float frequency, float duration, float gain, float beat = 0, bool bowed = false)
            {
                int offset = Mathf.RoundToInt(at * Rate), count = Mathf.CeilToInt(duration * Rate);
                double phase = 0, detuned = 0;
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)Rate;
                    double attack = Math.Min(.6, duration * .18), release = Math.Min(1.8, duration * .35);
                    double envelope = Math.Min(1, t / attack) * Math.Min(1, (duration - t) / release);
                    double flutter = 1 + Math.Sin(t * 3.1) * .0012;
                    phase += frequency * flutter / Rate; detuned += frequency * 1.0033 * flutter / Rate;
                    double signal = Math.Sin(phase * Math.PI * 2) * .65 + Math.Sin(detuned * Math.PI * 2) * .25;
                    if (bowed) signal += Math.Sin(phase * Math.PI * 4) * .13 + Math.Sin(phase * Math.PI * 6) * .06;
                    double pulse = beat <= 0 ? 1 : .45 + .55 * (.5 + .5 * Math.Sin(t * Math.PI * 2 * beat));
                    samples[(offset + i) % samples.Length] += (float)(signal * envelope * pulse * gain);
                }
            }

            void MusicBell(float at, int midi, float gain)
            {
                Pad(at, Note(midi), 3.5f, gain);
                Pad(at + .08f, Note(midi) * 2.006f, 2.5f, gain * .25f);
                Pad(at + .31f, Note(midi), 3.1f, gain * .19f);
            }

            public void Music(int kind)
            {
                loop = true; stereo = true;
                int[] roots = { 40, 39, 36, 42 };
                int[] motif = { 64, 63, 59, 66, 64, 56, 59, 63 };
                for (int bar = 0; bar < 4; bar++)
                {
                    int root = roots[bar]; float at = bar * 8;
                    if (kind == 0)
                    {
                        Pad(at, Note(root), 10, .11f, 0, true);
                        Pad(at + .5f, Note(root + 7), 9.5f, .045f);
                        Pad(at + 2, Note(root + 13), 7.5f, .027f);
                        MusicBell(at + .7f, motif[bar * 2], .13f);
                        MusicBell(at + 4.6f, motif[bar * 2 + 1], .09f);
                    }
                    else if (kind == 1)
                    {
                        Pad(at, Note(root - 12), 11, .075f);
                        Pad(at + 1, Note(root + 12), 9, .035f, 0, true);
                        Pad(at + 3.5f, Note(root + 18), 5, .018f);
                        MusicBell(at + 6.3f, root + 31, .048f);
                    }
                    else if (kind == 2)
                    {
                        Pad(at, Note(root), 9, .09f, 3.5f, true);
                        Pad(at + .15f, Note(root + 13), 8.5f, .065f, 4, true);
                        Pad(at + 2.3f, Note(root + 6), 6.5f, .055f, 2.5f, true);
                        MusicBell(at + 4.2f, root + 25, .055f);
                    }
                    else
                    {
                        Pad(at, Note(root + 12), 8.5f, .045f, 7, true);
                        Pad(at + .2f, Note(root + 13), 8.1f, .055f, 6.5f, true);
                        for (int beat = 0; beat < 16; beat++)
                        {
                            float onset = at + beat * .5f;
                            Tone(onset, Note(root - 12), Note(root - 19), .22f, beat % 4 == 0 ? .22f : .13f, Wave.Triangle, .002f);
                            if (beat % 4 == 2) Noise(onset, .14f, .085f, 880, .002f);
                        }
                    }
                }
            }

            public void MachineBed(bool lamp)
            {
                loop = true;
                for (int i = 0; i < samples.Length; i++)
                {
                    double t = i / (double)Rate;
                    double value = lamp ?
                        Math.Sin(t * Math.PI * 2 * 100) * .14 + Math.Sin(t * Math.PI * 2 * 200) * .028 :
                        (Math.Sin(t * Math.PI * 2 * 40) * .13 + Math.Sin(t * Math.PI * 2 * 80) * .06 + Math.Sin(t * Math.PI * 2 * 121) * .025) * (1 + Math.Sin(t * Math.PI * 2 * 3) * .15);
                    samples[i] = (float)value;
                }
            }

            public void HorrorSting(int kind)
            {
                stereo = true;
                // Original formant cry, impact and falling bass; each creature has a
                // recognisable texture rather than only a louder version of one sting.
                Noise(0, kind == 2 ? .12f : .2f, .34f, kind == 2 ? 3600 : 1900, .002f);
                Noise(.013f, .65f, .18f, 820, .005f);
                Tone(0, kind == 2 ? 112 : 84, 29, .95f, .34f, Wave.Triangle, .003f);
                CreatureCry(.012f, kind == 0 ? 1.02f : .88f, kind == 0 ? .31f : .48f, kind);
                if (kind == 2)
                {
                    Tone(.009f, 1560, 460, .38f, .15f, Wave.Saw, .003f);
                    for (int i = 0; i < 8; i++)
                    {
                        float at = .22f + i * .072f;
                        Knock(at, .55f); Noise(at, .04f, .1f, 2900 + i * 90, .002f);
                    }
                }
                else
                {
                    Tone(.016f, 640, 180, .82f, .17f, Wave.Saw, .003f);
                    Tone(.033f, 679, 193, .88f, .11f, Wave.Saw, .005f);
                    Noise(.14f, .53f, .1f, 2800, .009f);
                }
            }

            void CreatureCry(float at, float duration, float gain, int kind)
            {
                int offset = Mathf.RoundToInt(at * Rate), count = Mathf.CeilToInt(duration * Rate);
                var rasp = new BandPass(kind == 2 ? 3100 : 1600);
                double phase = 0;
                for (int i = 0; i < count && offset + i < samples.Length; i++)
                {
                    double t = i / (double)Rate, u = t / duration;
                    double fundamental = kind == 2 ? 310 + 270 * Math.Sin(Math.PI * u) : 118 + 245 * Math.Sin(Math.PI * u);
                    fundamental *= 1 + .025 * Math.Sin(t * 2 * Math.PI * 13) + .008 * Math.Sin(t * 97);
                    phase += fundamental / Rate;
                    double voice = 0, weights = 0;
                    double formant = kind == 2 ? 970 : 620;
                    for (int k = 1; k <= 24 && fundamental * k < Rate * .44; k++)
                    {
                        double frequency = fundamental * k;
                        double a = (frequency - formant) / 290, b = (frequency - 1480) / 420, c = (frequency - 2680) / 590;
                        double weight = (.05 + 1.8 * Math.Exp(-a * a) + Math.Exp(-b * b) + .55 * Math.Exp(-c * c)) / Math.Sqrt(k);
                        voice += Math.Sin(phase * Math.PI * 2 * k) * weight;
                        weights += weight;
                    }
                    double envelope = Math.Min(1, t / .007) * Math.Pow(Math.Max(0, 1 - u), 1.65);
                    double tremor = .78 + .22 * Math.Sin(t * 2 * Math.PI * (kind == 2 ? 29 : 17));
                    double noise = rasp.Tick(random.NextDouble() * 2 - 1);
                    samples[offset + i] += (float)((voice / Math.Max(.1, weights) * 1.55 + noise * .32) * envelope * tremor * gain);
                }
            }

            public void MushroomBloom()
            {
                stereo = true;
                // Swelling glassy chord bends into an uneasy, fluttering trip onset.
                Tone(0, 82, 48, 1.4f, .14f, Wave.Sine, .18f);
                Noise(.06f, .85f, .085f, 1300, .21f);
                int count = Math.Min(samples.Length, Mathf.CeilToInt(3.05f * Rate));
                double a = 0, b = 0, c = 0;
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)Rate, u = t / 3.05;
                    double bend = 1 + .065 * Math.Sin(t * 5.5) + .019 * Math.Sin(t * 17);
                    a += (294 + u * 68) * bend / Rate;
                    b += (415 - u * 75) / Rate; c += (588 + u * 175) / Rate;
                    double envelope = Math.Min(1, t / .3) * Math.Pow(Math.Max(0, 1 - u), 1.5);
                    double flutter = .7 + .3 * Math.Sin(t * 2 * Math.PI * (2 + u * 5));
                    samples[i] += (float)((Math.Sin(a * Math.PI * 2) * .12 + Math.Sin(b * Math.PI * 2) * .09 + Math.Sin(c * Math.PI * 2) * .055) * envelope * flutter);
                }
                for (int i = 0; i < 5; i++)
                    Tone(.35f + i * .27f, 1040 + i * 73, 570 + i * 42, .45f, .055f, Wave.Sine, .025f);
            }

            public void MushroomBed()
            {
                loop = true; stereo = true;
                // Every carrier and modulation completes whole cycles in eight seconds.
                // The trip can sustain for any game duration without a seam or retrigger.
                for (int i = 0; i < samples.Length; i++)
                {
                    double t = i / (double)Rate, tau = Math.PI * 2;
                    double flutter = .72 + .28 * Math.Sin(tau * .5 * t);
                    double low = Math.Sin(tau * 55 * t) * .06;
                    double glass = Math.Sin(tau * 196.25 * t + 1.1 * Math.Sin(tau * .375 * t)) * .05;
                    double dissonance = Math.Sin(tau * 293.375 * t + .7 * Math.Sin(tau * .25 * t)) * .025;
                    samples[i] = (float)(low + (glass + dissonance) * flutter);
                }
            }
        }
    }
}
