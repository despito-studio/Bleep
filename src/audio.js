/**
 * Procedural sound for the station. Everything is synthesized locally: no
 * audio downloads, microphone access, or prerecorded voices are involved.
 * Call start() from a user gesture; update() may then run every animation frame.
 */
export class HorrorAudio {
  constructor() {
    this.context = null;
    this.volume = 0.64;
    this.muted = false;
    this.paused = true;
    this.started = false;
    this.disposed = false;
    this._sources = new Set();
    this._nodes = new Set();
    this._transients = new Set();
    this._lastUpdate = -Infinity;
    this._nextStep = 0;
    this._nextHeartbeat = 0;
    this._nextKnock = Infinity;
    this._nextMonsterSound = 0;
    this._nextBreath = 0;
    this._nextWhisper = 0;
    this._nextAlarm = 0;
    this._moving = false;
    this._stepSide = 1;
    this._threat = 0;
  }

  async start() {
    if (this.disposed) return false;
    try {
      if (!this.context) {
        const AudioContext = globalThis.AudioContext || globalThis.webkitAudioContext;
        if (!AudioContext) return false;
        this.context = new AudioContext({ latencyHint: 'interactive' });
        this._build();
      }
      if (this.context.state === 'suspended') await this.context.resume();
      this.started = this.context.state === 'running';
      return this.started;
    } catch {
      this.started = false;
      return false;
    }
  }

  setVolume(value) {
    const number = Number(value);
    if (!Number.isFinite(number)) return;
    this.volume = Math.min(1, Math.max(0, number));
    this._setMaster();
  }

  setMuted(value) {
    this.muted = Boolean(value);
    this._setMaster();
  }

  _setMaster() {
    if (!this.context || !this.master) return;
    this.master.gain.setTargetAtTime(this.muted ? 0 : this.volume, this.context.currentTime, 0.04);
  }

  _node(node) {
    this._nodes.add(node);
    return node;
  }

  _gain(value = 1) {
    const node = this._node(this.context.createGain());
    node.gain.value = value;
    return node;
  }

  _filter(type, frequency, q = 0.7) {
    const node = this._node(this.context.createBiquadFilter());
    node.type = type;
    node.frequency.value = frequency;
    node.Q.value = q;
    return node;
  }

  _build() {
    const ctx = this.context;
    this.master = this._gain(this.muted ? 0 : this.volume);
    const compressor = this._node(ctx.createDynamicsCompressor());
    compressor.threshold.value = -19;
    compressor.knee.value = 20;
    compressor.ratio.value = 4;
    compressor.attack.value = 0.008;
    compressor.release.value = 0.3;
    this.master.connect(compressor);
    compressor.connect(ctx.destination);

    this.ambience = this._gain(0);
    this.effects = this._gain(0.85);
    this.ambience.connect(this.master);
    this.effects.connect(this.master);

    const reverb = this._node(ctx.createConvolver());
    const impulse = ctx.createBuffer(2, Math.floor(ctx.sampleRate * 1.4), ctx.sampleRate);
    for (let channel = 0; channel < 2; channel += 1) {
      const data = impulse.getChannelData(channel);
      let smooth = 0;
      for (let i = 0; i < data.length; i += 1) {
        smooth = smooth * 0.55 + (Math.random() * 2 - 1) * 0.45;
        data[i] = smooth * Math.pow(1 - i / data.length, 3.5);
      }
    }
    reverb.buffer = impulse;
    const reverbSend = this._gain(0.14);
    const reverbFilter = this._filter('lowpass', 2200);
    this.effects.connect(reverbSend);
    reverbSend.connect(reverb);
    reverb.connect(reverbFilter);
    reverbFilter.connect(this.master);

    this._whiteNoise = this._noiseBuffer(false);
    this._pinkNoise = this._noiseBuffer(true);

    this.hum = this._gain(1);
    this.hum.connect(this.ambience);
    for (const [frequency, level, waveform] of [[48, 0.036, 'sine'], [96.2, 0.012, 'triangle'], [143.4, 0.006, 'sine']]) {
      const oscillator = this._node(ctx.createOscillator());
      oscillator.type = waveform;
      oscillator.frequency.value = frequency;
      const gain = this._gain(level);
      oscillator.connect(gain);
      gain.connect(this.hum);
      this._startLoop(oscillator);
      const drift = this._node(ctx.createOscillator());
      drift.frequency.value = 0.11 + Math.random() * 0.06;
      const driftGain = this._gain(frequency * 0.002);
      drift.connect(driftGain);
      driftGain.connect(oscillator.frequency);
      this._startLoop(drift);
    }

    const air = this._loopNoise(this._pinkNoise);
    const airHigh = this._filter('highpass', 105);
    const airLow = this._filter('lowpass', 1250);
    this.airGain = this._gain(0.055);
    air.connect(airHigh);
    airHigh.connect(airLow);
    airLow.connect(this.airGain);
    this.airGain.connect(this.ambience);

    const radio = this._loopNoise(this._whiteNoise);
    this.radioFilter = this._filter('bandpass', 1800, 0.65);
    this.radioGain = this._gain(0.003);
    radio.connect(this.radioFilter);
    this.radioFilter.connect(this.radioGain);
    this.radioGain.connect(this.ambience);

    this.dreadOscillator = this._node(ctx.createOscillator());
    this.dreadOscillator.type = 'sine';
    this.dreadOscillator.frequency.value = 71;
    this.dreadGain = this._gain(0);
    this.dreadOscillator.connect(this.dreadGain);
    this.dreadGain.connect(this.ambience);
    this._startLoop(this.dreadOscillator);

    // Insanity tinnitus drone
    this.tinnitus = this._node(ctx.createOscillator());
    this.tinnitus.type = 'sine';
    this.tinnitus.frequency.value = 5200;
    this.tinnitusGain = this._gain(0);
    this.tinnitus.connect(this.tinnitusGain);
    this.tinnitusGain.connect(this.ambience);
    this._startLoop(this.tinnitus);

    this._nextKnock = ctx.currentTime + 14 + Math.random() * 12;
  }

  _noiseBuffer(pink) {
    const ctx = this.context;
    const buffer = ctx.createBuffer(1, ctx.sampleRate * 4, ctx.sampleRate);
    const data = buffer.getChannelData(0);
    let b0 = 0, b1 = 0, b2 = 0;
    for (let i = 0; i < data.length; i += 1) {
      const white = Math.random() * 2 - 1;
      if (pink) {
        b0 = 0.99765 * b0 + white * 0.099046;
        b1 = 0.963 * b1 + white * 0.2965164;
        b2 = 0.57 * b2 + white * 1.0526913;
        data[i] = (b0 + b1 + b2 + white * 0.1848) * 0.11;
      } else data[i] = white;
    }
    return buffer;
  }

  _startLoop(source) {
    this._sources.add(source);
    source.start();
  }

  _loopNoise(buffer) {
    const source = this._node(this.context.createBufferSource());
    source.buffer = buffer;
    source.loop = true;
    this._startLoop(source);
    return source;
  }

  /**
   * Main audio tick update
   */
  update({
    moving = false, sprinting = false, threat = 0, camera = false, power = false,
    broadcast = false, time, paused = false, stamina = 100, sanity = 100,
    monsterSpatial = null, hiding = false,
  } = {}) {
    if (!this.started || !this.context || this.disposed) return;
    const now = this.context.currentTime;
    const pauseChanged = this.paused !== Boolean(paused);
    this.paused = Boolean(paused);
    if (!pauseChanged && now - this._lastUpdate < 1 / 30) return;
    this._lastUpdate = now;
    const gameTime = Number.isFinite(time) ? time : now;
    const danger = Math.max(0, Math.min(1, Number(threat) || 0));
    const san = Math.max(0, Math.min(100, Number(sanity) || 100));
    const ins = 1 - san / 100;
    this._threat = danger;

    // Ambient levels (muffled in locker)
    const baseAmbience = hiding ? 0.35 : 1;
    this.ambience.gain.setTargetAtTime(this.paused ? 0 : baseAmbience, now, 0.24);
    this.hum.gain.setTargetAtTime(power ? 1.45 : 0.7, now, 0.7);
    this.airGain.gain.setTargetAtTime(0.04 + danger * 0.042, now, 0.5);
    this.radioGain.gain.setTargetAtTime(camera ? 0.02 + danger * 0.018 : 0.002, now, 0.18);
    this.radioFilter.frequency.setTargetAtTime(1500 + Math.sin(gameTime * 0.57) * 280 + danger * 400, now, 0.16);
    this.dreadGain.gain.setTargetAtTime(danger * 0.05 + ins * 0.02, now, 0.3);
    this.dreadOscillator.frequency.setTargetAtTime(71 + Math.sin(gameTime * 0.24) * 1.5 + danger * 10, now, 0.4);

    // Insanity high-frequency tinnitus
    const tinnitusLevel = ins > 0.45 ? (ins - 0.45) * 0.03 : 0;
    this.tinnitusGain.gain.setTargetAtTime(tinnitusLevel, now, 0.5);

    if (this.paused) {
      this._moving = false;
      this._nextStep = now + 0.1;
      this._nextHeartbeat = now + 0.4;
      return;
    }

    // Footsteps
    if (moving && !hiding) {
      if (!this._moving) this._nextStep = now + 0.06;
      if (now >= this._nextStep) {
        this._footstep(now, Boolean(sprinting));
        this._nextStep = now + (sprinting ? 0.31 : 0.52) + Math.random() * 0.035;
      }
    }
    this._moving = Boolean(moving);

    // Heavy breathing when stamina is depleted
    if (stamina < 30 && now >= this._nextBreath) {
      const breathIntensity = (1 - stamina / 30);
      this._breath(now, breathIntensity);
      this._nextBreath = now + 0.85 + Math.random() * 0.2;
    }

    // Heartbeat on threat or panic
    if ((danger > 0.15 || hiding && danger > 0.05 || ins > 0.6) && now >= this._nextHeartbeat) {
      const heartLevel = 0.35 + danger * 0.65;
      this._heartbeat(now, heartLevel);
      this._nextHeartbeat = now + 1.2 - danger * 0.72;
    }

    // 3D Spatial monster cue (footsteps / bone clicks / radio static from direction)
    if (monsterSpatial && monsterSpatial.dist < 16 && now >= this._nextMonsterSound) {
      const pan = Math.max(-1, Math.min(1, monsterSpatial.pan || 0));
      const distVol = Math.max(0.08, 1 - monsterSpatial.dist / 16);
      this._monsterCue(now, pan, distVol);
      this._nextMonsterSound = now + 1.6 + Math.random() * 2.2 - (broadcast ? 0.8 : 0);
    }

    // Insanity phantom whispers
    if (ins > 0.45 && now >= this._nextWhisper) {
      const pan = (Math.random() - 0.5) * 1.8;
      this._whisper(now, pan, ins * 0.05);
      this._nextWhisper = now + 6 + Math.random() * 10;
    }

    // Broadcast escape alarm klaxon
    if (broadcast && now >= this._nextAlarm) {
      this._alarm(now);
      this._nextAlarm = now + 2.4;
    }

    // Ambient knocks
    if (now >= this._nextKnock) {
      this._knock(now, 0.42, Math.random() * 1.6 - 0.8);
      if (Math.random() > 0.55) this._knock(now + 0.28, 0.24, -0.35);
      this._nextKnock = now + 16 + Math.random() * 20;
    }
  }

  play(kind, options = {}) {
    if (!this.started || !this.context || this.disposed || this.muted) return;
    const now = this.context.currentTime;
    switch (kind) {
      case 'click':
        this._tone({ at: now, frequency: 810, endFrequency: 320, duration: 0.035, gain: 0.045, type: 'triangle' });
        break;
      case 'step':
        if (!this.paused) this._footstep(now, false);
        break;
      case 'pickup':
        this._knock(now, 0.45, 0.1);
        this._tone({ at: now + 0.055, frequency: 520, endFrequency: 390, duration: 0.16, gain: 0.045, type: 'triangle' });
        this._noise({ at: now + 0.1, duration: 0.17, gain: 0.038, frequency: 2400 });
        break;
      case 'power':
        this._knock(now, 0.95, 0);
        this._knock(now + 0.15, 0.55, -0.1);
        this._tone({ at: now + 0.15, frequency: 31, endFrequency: 96, duration: 1.25, gain: 0.14, attack: 0.14, type: 'triangle' });
        this._noise({ at: now + 0.05, duration: 0.65, gain: 0.11, frequency: 900, attack: 0.015 });
        break;
      case 'error':
        for (const offset of [0, 0.14]) this._tone({ at: now + offset, frequency: 132, endFrequency: 113, duration: 0.1, gain: 0.048, type: 'triangle' });
        break;
      case 'scare':
        this._noise({ at: now, duration: 0.9, gain: 0.22, frequency: 1100, attack: 0.045 });
        this._tone({ at: now, frequency: 115, endFrequency: 36, duration: 1.4, gain: 0.19, attack: 0.04, type: 'triangle' });
        this._tone({ at: now + 0.07, frequency: 340, endFrequency: 140, duration: 0.75, gain: 0.075, attack: 0.04, pan: -0.55 });
        this._tone({ at: now + 0.12, frequency: 360, endFrequency: 150, duration: 0.85, gain: 0.062, attack: 0.04, pan: 0.55 });
        break;
      case 'screamer':
        this._noise({ at: now, duration: 0.35, gain: 0.3, frequency: 2800, attack: 0.005 });
        this._tone({ at: now, frequency: 720, endFrequency: 85, duration: 0.45, gain: 0.25, attack: 0.005, type: 'sawtooth' });
        break;
      case 'radio':
        this._radioChirp(now);
        break;
      case 'locker':
        this._knock(now, 0.7, options.pan || 0);
        this._tone({ at: now + 0.03, frequency: 180, endFrequency: 75, duration: 0.15, gain: 0.06, type: 'triangle', pan: options.pan || 0 });
        break;
      case 'alarm':
        this._alarm(now);
        break;
      case 'win':
        for (const [index, frequency] of [164.81, 246.94, 329.63, 369.99].entries()) {
          this._tone({ at: now + index * 0.43, frequency, endFrequency: frequency * 0.998, duration: 2.1, gain: 0.078, attack: 0.07 });
        }
        break;
      case 'lose':
        this._tone({ at: now, frequency: 86, endFrequency: 23, duration: 2.2, gain: 0.16, attack: 0.035, type: 'triangle' });
        this._noise({ at: now, duration: 1.25, gain: 0.14, frequency: 750, attack: 0.05 });
        break;
      default:
        break;
    }
  }

  _footstep(at, sprinting) {
    this._stepSide *= -1;
    const pan = this._stepSide * 0.12;
    const level = sprinting ? 1.15 : 0.8;
    this._tone({ at, frequency: 75 + Math.random() * 16, endFrequency: 34, duration: 0.16, gain: 0.094 * level, pan });
    this._noise({ at, duration: 0.105, gain: 0.09 * level, frequency: 380 + Math.random() * 140, pan, attack: 0.003 });
    this._noise({ at: at + 0.075, duration: 0.11, gain: 0.025 * level, frequency: 1500, pan });
  }

  _breath(at, level = 1) {
    const pan = (Math.random() - 0.5) * 0.2;
    this._noise({ at, duration: 0.38, gain: 0.05 * level, frequency: 540, attack: 0.08, pan });
    this._noise({ at: at + 0.45, duration: 0.42, gain: 0.04 * level, frequency: 460, attack: 0.09, pan });
  }

  _heartbeat(at, level) {
    this._tone({ at, frequency: 62, endFrequency: 36, duration: 0.14, gain: 0.1 * level, attack: 0.012 });
    this._tone({ at: at + 0.19, frequency: 56, endFrequency: 32, duration: 0.17, gain: 0.075 * level, attack: 0.01 });
  }

  _knock(at, level = 1, pan = 0) {
    for (const [frequency, amplitude, duration] of [[183, 0.065, 0.24], [411, 0.023, 0.15], [687, 0.012, 0.08]]) {
      this._tone({ at, frequency, duration, gain: amplitude * level, pan, attack: 0.002 });
    }
    this._noise({ at, duration: 0.04, gain: 0.04 * level, frequency: 900, pan, attack: 0.002 });
  }

  _monsterCue(at, pan, level = 1) {
    // 3D spatial bone crack / heavy shuffle
    this._knock(at, level * 0.7, pan);
    this._noise({ at: at + 0.05, duration: 0.22, gain: level * 0.06, frequency: 1200 + Math.random() * 400, pan, attack: 0.01 });
    this._tone({ at: at + 0.08, frequency: 45, endFrequency: 22, duration: 0.35, gain: level * 0.08, pan, type: 'triangle' });
  }

  _whisper(at, pan, level = 0.05) {
    // Formant sweep simulating phantom whisper
    this._noise({ at, duration: 0.7, gain: level, frequency: 950 + Math.random() * 300, pan, attack: 0.1 });
    this._tone({ at: at + 0.05, frequency: 220, endFrequency: 180, duration: 0.6, gain: level * 0.4, pan, type: 'sine' });
  }

  _alarm(at) {
    // Emergency broadcast siren tone
    this._tone({ at, frequency: 440, endFrequency: 330, duration: 0.9, gain: 0.09, attack: 0.08, type: 'sawtooth' });
    this._tone({ at: at + 0.95, frequency: 330, endFrequency: 440, duration: 0.9, gain: 0.08, attack: 0.08, type: 'sawtooth' });
  }

  _radioChirp(at) {
    // Squelch click + radio burst
    this._noise({ at, duration: 0.04, gain: 0.08, frequency: 3200 });
    this._tone({ at: at + 0.03, frequency: 1240, endFrequency: 1100, duration: 0.08, gain: 0.05, type: 'sine' });
    this._noise({ at: at + 0.09, duration: 0.25, gain: 0.07, frequency: 1800, attack: 0.02 });
  }

  _voice(source, { at, duration, gain, attack = 0.007, pan = 0 }, filter = null) {
    const ctx = this.context;
    const envelope = ctx.createGain();
    const nodes = [source, envelope];
    if (filter) {
      nodes.push(filter);
      source.connect(filter);
      filter.connect(envelope);
    } else source.connect(envelope);

    if (typeof ctx.createStereoPanner === 'function') {
      const panner = ctx.createStereoPanner();
      panner.pan.value = Math.min(1, Math.max(-1, pan));
      envelope.connect(panner);
      panner.connect(this.effects);
      nodes.push(panner);
    } else envelope.connect(this.effects);

    const safeAttack = Math.min(attack, duration * 0.45);
    envelope.gain.setValueAtTime(0, at);
    envelope.gain.linearRampToValueAtTime(gain, at + safeAttack);
    envelope.gain.exponentialRampToValueAtTime(0.0001, at + duration);
    envelope.gain.linearRampToValueAtTime(0, at + duration + 0.025);
    const voice = { source, nodes };
    this._transients.add(voice);
    source.onended = () => {
      for (const node of nodes) node.disconnect();
      this._transients.delete(voice);
    };
    source.start(at);
    source.stop(at + duration + 0.04);
  }

  _tone({ at, frequency, endFrequency = frequency, duration = 0.2, gain = 0.05, type = 'sine', attack, pan }) {
    const oscillator = this.context.createOscillator();
    oscillator.type = type;
    oscillator.frequency.setValueAtTime(frequency, at);
    oscillator.frequency.exponentialRampToValueAtTime(Math.max(10, endFrequency), at + duration);
    this._voice(oscillator, { at, duration, gain, attack, pan });
  }

  _noise({ at, duration = 0.2, gain = 0.06, frequency = 1000, attack, pan }) {
    const source = this.context.createBufferSource();
    source.buffer = this._whiteNoise;
    const filter = this.context.createBiquadFilter();
    filter.type = 'bandpass';
    filter.frequency.value = frequency;
    filter.Q.value = 0.65;
    this._voice(source, { at, duration, gain, attack, pan }, filter);
  }

  dispose() {
    if (this.disposed) return;
    this.disposed = true;
    this.started = false;
    for (const voice of this._transients) {
      voice.source.onended = null;
      try { voice.source.stop(); } catch {}
      for (const node of voice.nodes) node.disconnect();
    }
    for (const source of this._sources) {
      try { source.stop(); } catch {}
    }
    for (const node of this._nodes) node.disconnect();
    this._sources.clear();
    this._nodes.clear();
    this._transients.clear();
    if (this.context && this.context.state !== 'closed') this.context.close().catch(() => {});
    this.context = null;
    this.master = null;
    this._whiteNoise = null;
    this._pinkNoise = null;
  }
}
