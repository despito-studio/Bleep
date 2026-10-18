"""Bake original Ukrainian Blackpine dialogue; the game has no TTS/network dependency.

Build-time requirements: edge-tts, imageio-ffmpeg. No user recordings or voice cloning.
"""
import argparse
import asyncio
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument("--tools", type=Path)
args = parser.parse_args()
if args.tools:
    sys.path.insert(0, str(args.tools.resolve()))
import edge_tts
import imageio_ffmpeg

OUTPUT = ROOT / "Assets/Resources/Audio/Blackpine"
SOURCE = ROOT / "Art/BlackpineAudio"
OUTPUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
PHRASES = ["До мене.", "Він поруч.", "Касета тут.", "Допоможіть!"]
LINES = {
    "tape-1": "Ми вимкнули мікрофони. Голос залишився. Воно сліпе. Знаходить нас по кроках і рації. Якщо почуєте своє ім'я, спершу перевірте, хто кличе.",
    "tape-2": "Антена більше нічого не передає. Вона приймає відповідь із підвалу. Щоб розірвати петлю, роз'єднайте вузол у задньому дворі, потім вимкніть Резонатор унизу.",
}
for key, frequency in [(948,"дев'яносто чотири і вісім"), (964,"дев'яносто шість і чотири"), (1012,"сто один і два")]:
    LINES[f"tape-3-{key}"] = f"Аварійна несуча. {frequency} мегагерца. Захопіть її приймачем в апаратній. Поки не побачите джерело на приладі, вимикати його марно. Не відповідайте голосу."
for sex in ["m", "f"]:
    for index, phrase in enumerate(PHRASES):
        LINES[f"radio-{sex}-{index}"] = phrase

async def bake(key, text, semaphore):
    target = OUTPUT / f"{key}.wav"
    if target.exists() and target.stat().st_size > 1000:
        return
    async with semaphore:
        mp3 = SOURCE / f"{key}.mp3"
        voice = "uk-UA-PolinaNeural" if key.startswith("radio-f") else "uk-UA-OstapNeural"
        await asyncio.wait_for(edge_tts.Communicate(text, voice, rate="-8%" if key.startswith("tape") else "+0%").save(str(mp3)), 35)
        subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error", "-i", str(mp3),
                        "-af", "highpass=f=240,lowpass=f=3300,acompressor=threshold=0.1:ratio=3,alimiter=limit=0.85",
                        "-ar", "24000", "-ac", "1", str(target)], check=True)
        print(f"Baked {key}: {target.stat().st_size} bytes", flush=True)

async def main():
    semaphore = asyncio.Semaphore(3)
    results = await asyncio.gather(*(bake(key, text, semaphore) for key, text in LINES.items()), return_exceptions=True)
    errors = [str(item) for item in results if isinstance(item, BaseException)]
    (SOURCE / "dialogue.json").write_text(json.dumps({"language":"uk-UA", "syntheticVoices":["uk-UA-OstapNeural","uk-UA-PolinaNeural"],"lines":LINES}, ensure_ascii=False, indent=2), encoding="utf-8")
    if errors:
        raise RuntimeError("; ".join(errors))

asyncio.run(main())
