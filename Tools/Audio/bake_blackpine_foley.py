"""Original deterministic snow foley; no external audio or runtime synthesis needed."""
from pathlib import Path
import math
import random
import struct
import wave

output = Path(__file__).resolve().parents[2] / "Assets/Resources/Audio/Cues"
output.mkdir(parents=True, exist_ok=True)
rate = 24000
for key, length, gain in [("step-snow", .48, .36), ("run-step-snow", .38, .47)]:
    rng = random.Random(964)
    samples = []
    smooth = 0
    for index in range(int(rate*length)):
        t = index/rate
        envelope = min(1,t/.012)*math.exp(-t*9)*(1+.30*math.sin(t*180))
        noise = rng.uniform(-1,1)
        smooth = smooth*.70 + noise*.30
        crunch = .5*smooth + .25*noise + .13*math.sin(t*2*math.pi*83)*math.exp(-t*18)
        crackle = rng.uniform(-1,1)*.14 if rng.random() < .018 else 0
        samples.append(int(max(-1,min(1,(crunch+crackle)*envelope*gain))*32767))
    with wave.open(str(output/f"{key}.wav"),"wb") as audio:
        audio.setnchannels(1); audio.setsampwidth(2); audio.setframerate(rate)
        audio.writeframes(struct.pack("<"+"h"*len(samples),*samples))
    print(key, len(samples), "samples")
