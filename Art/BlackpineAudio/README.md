# Original Blackpine dialogue

All Ukrainian dialogue was written for this project. Thirteen clips were baked using stock synthetic voices `uk-UA-OstapNeural` and `uk-UA-PolinaNeural`, with radio band filtering. No human recordings or voice cloning were used. Actual player microphone audio is never captured.

Source scripts: `Tools/Audio/generate_blackpine_voice.py`; exact text: `dialogue.json` in this directory. The game consumes bundled mono 24 kHz WAV files from `Assets/Resources/Audio/Blackpine`; no network/TTS dependency is required at runtime. The same actor radio clips are used for ordinary phrases and the Dictor's filtered echoes.

Build tools: [edge-tts](https://github.com/rany2/edge-tts), [Microsoft voice language reference](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-support). Snow footstep recordings are original deterministic synthesis from `Tools/Audio/bake_blackpine_foley.py`.
