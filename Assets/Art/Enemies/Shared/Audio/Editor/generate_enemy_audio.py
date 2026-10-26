"""Original layered enemy foley/energy mini-library; no downloaded recordings.

Run with Python 3. Produces deterministic mono PCM WAVs, an audition sequence and
signal audit. Audition/audit are outside Assets, so they are not shipped in builds.
All synthesis is offline: runtime enemies only share imported, preloaded clips.
"""
from pathlib import Path
import argparse
import array
import json
import math
import random
import tempfile
import wave

RATE = 22050
ROOT = Path(__file__).resolve().parents[1] / "Resources" / "EnemyAudio"


class Sound:
    def __init__(self, length, seed):
        self.data = [0.0] * round(length * RATE)
        self.rng = random.Random(seed)

    def tone(self, start, length, low, high=None, gain=.3, attack=.008, decay=2.0, metallic=0):
        phase = 0.0
        for k in range(round(length * RATE)):
            i = round(start * RATE) + k
            if i >= len(self.data):
                break
            t = k / RATE
            u = t / length
            freq = low + ((high if high is not None else low) - low) * u
            phase += math.tau * freq / RATE
            env = min(1, t / attack) * (1 - u) ** decay
            value = math.sin(phase) + metallic * math.sin(phase * 2.73 + .3)
            self.data[i] += gain * value * env
        return self

    def noise(self, start, length, gain, cutoff, attack=.005, decay=2, highpass=False, rough=0):
        value = 0.0
        alpha = 1 - math.exp(-math.tau * cutoff / RATE)
        for k in range(round(length * RATE)):
            i = round(start * RATE) + k
            if i >= len(self.data):
                break
            t = k / RATE
            u = t / length
            white = self.rng.uniform(-1, 1)
            value += alpha * (white - value)
            signal = white - value if highpass else value
            env = min(1, t / attack) * (1 - u) ** decay
            texture = 1 - rough + rough * (.5 + .5 * math.sin(math.tau * 43 * t))
            self.data[i] += gain * signal * env * texture
        return self

    def stone(self, start, strength=1, length=.26):
        self.noise(start, length, .8 * strength, 1900, decay=3, rough=.35)
        for hz, gain in ((137, .48), (311, .25), (697, .12), (1241, .06)):
            self.tone(start, length, hz, hz * .93, gain * strength, decay=4, metallic=.1)
        return self

    def ceramic(self, start, strength=1):
        self.noise(start, .09, .65 * strength, 2300, highpass=True, decay=4)
        for hz, gain in ((713, .25), (1327, .15), (2581, .06)):
            self.tone(start, .19, hz, hz * .98, gain * strength, decay=5)
        return self

    def finish(self):
        # DC-free, click-free edges; standardised perceived level without clipping.
        mean = sum(self.data) / len(self.data)
        data = [v - mean for v in self.data]
        fade = round(.006 * RATE)
        for i in range(fade):
            data[i] *= i / fade
            data[-1-i] *= i / fade
        rms = math.sqrt(sum(v*v for v in data) / len(data))
        peak = max(abs(v) for v in data)
        gain = min(.14 / max(rms, .00001), .8 / max(peak, .00001))
        data = [v * gain for v in data]
        return data


def build():
    sounds = {}
    def new(name, seconds):
        s = Sound(seconds, len(sounds) * 31 + 8462)
        sounds[name] = s
        return s

    # Broad gritty movement / dry weight, distinct from the generic punch clip.
    new("StoneScrape", .48).noise(0, .45, .95, 1750, attack=.08, decay=.45, rough=.7).tone(0, .43, 175, 310, .2, attack=.06, decay=.5, metallic=.24)
    new("StonePunch", .22).noise(0, .19, .7, 1650, attack=.03, decay=1).tone(.025, .18, 250, 110, .22)
    new("StoneHit", .29).stone(0).ceramic(.006, .27).stone(.08, .16, .15)
    new("StoneFall", .72).stone(0, 1.1, .44).stone(.16, .38, .29).stone(.32, .17, .26).noise(.11, .5, .24, 2600, decay=3)
    # Lock rises in the midrange so it remains audible on laptop speakers.
    new("CoreLock", .36).tone(0, .33, 580, 1140, .26, attack=.018, decay=.35).tone(.1, .23, 1180, 1490, .1, attack=.02, decay=.8)
    new("DartFire", .20).tone(0, .17, 1850, 470, .38, decay=1.5).noise(0, .16, .48, 2900, decay=3).tone(.025, .14, 330, 180, .15)
    new("ShellHit", .25).ceramic(0).tone(0, .22, 280, 235, .15, metallic=.3)
    new("CorePowerDown", .55).tone(0, .48, 940, 130, .28, decay=1).tone(0, .48, 350, 65, .16, metallic=.13).ceramic(.3, .15)
    # Prism charge is a short bass/mid-frequency growl, NOT a perpetual hum.
    new("PrismCharge", .67).tone(0, .62, 135, 320, .45, attack=.08, decay=.3, metallic=.35).tone(.09, .5, 410, 730, .13, attack=.06, decay=.35).noise(.02, .58, .22, 800, attack=.05, decay=.6, rough=.3)
    new("PlateOpen", .24).ceramic(0, .4).noise(.035, .17, .5, 2000, attack=.02, decay=1, rough=.55).tone(.025, .18, 370, 610, .13, metallic=.5)
    new("Pulse", .44).tone(0, .4, 360, 80, .52, decay=2, metallic=.14).tone(0, .28, 780, 220, .17, decay=2).noise(0, .34, .6, 1700, attack=.013, decay=2)
    # Charger: escalating mechanical tension, air burst, heavier wall-only crash.
    new("BeetleCharge", .73).tone(0, .66, 180, 660, .3, attack=.06, decay=.32, metallic=.5).noise(.02, .63, .28, 2100, attack=.1, decay=.4, rough=.7).ceramic(.06, .12)
    new("BeetleRush", .36).noise(0, .32, .9, 2500, attack=.025, decay=.7, rough=.2).tone(0, .3, 510, 150, .29, decay=1, metallic=.32)
    new("WallCrash", .62).stone(0, 1.1, .5).ceramic(.01, .55).stone(.09, .32, .3).noise(.05, .5, .28, 3000, decay=3)
    new("Stun", .39).tone(0, .32, 470, 80, .3, decay=.9, metallic=.38).tone(.14, .22, 240, 80, .12, metallic=.3)
    # Soft paired notes communicate preparation / connection / success / failure.
    new("RepairPrepare", .33).tone(0, .2, 660, 880, .3, decay=1).tone(.13, .18, 990, 1170, .2, decay=1.5)
    new("RepairLink", .23).tone(0, .2, 1175, 1397, .27, attack=.02, decay=1.5).noise(0, .18, .14, 2500, attack=.02, decay=2)
    new("RepairSuccess", .44).tone(0, .24, 880, 880, .22).tone(.11, .26, 1175, 1175, .2).tone(.22, .21, 1568, 1568, .18)
    new("RepairBreak", .30).tone(0, .23, 1130, 390, .22, metallic=.15).noise(.015, .19, .4, 2800, highpass=True, decay=2)
    new("RepairEmpty", .43).tone(0, .19, 440, 415, .24).tone(.2, .2, 311, 220, .21, metallic=.12)
    # Four compact wet/elastic cues; existing deterministic seeds remain unchanged.
    new("GelCharge", .58).tone(0, .53, 230, 590, .28, attack=.05, decay=.35).noise(.04, .48, .24, 550, attack=.04, decay=.6, rough=.45)
    new("GelJump", .19).tone(0, .17, 260, 970, .37, attack=.01, decay=1.7).noise(0, .15, .22, 1400, decay=2)
    new("GelSplat", .32).noise(0, .28, .75, 900, decay=2.5).tone(0, .23, 290, 75, .38, decay=2).tone(.065, .19, 720, 220, .15, decay=3)
    new("GelHit", .22).tone(0, .19, 520, 150, .4, decay=2.4).noise(0, .16, .45, 1100, decay=3).tone(.025, .17, 850, 340, .14, decay=3)
    return {name: s.finish() for name, s in sounds.items()}


def write_wav(path, samples):
    pcm = array.array("h", (round(max(-1, min(1, s)) * 32767) for s in samples))
    import sys
    if sys.byteorder != "little":
        pcm.byteswap()
    with wave.open(str(path), "wb") as file:
        file.setnchannels(1)
        file.setsampwidth(2)
        file.setframerate(RATE)
        file.writeframes(pcm.tobytes())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="Validate deterministic export without modifying assets")
    args = parser.parse_args()
    sounds = build()
    ROOT.mkdir(parents=True, exist_ok=True)
    audit = []
    audition = []
    for name, samples in sounds.items():
        peak = max(abs(v) for v in samples)
        rms = math.sqrt(sum(v*v for v in samples) / len(samples))
        assert peak <= .801 and .04 < rms <= .141 and abs(samples[0]) < .0001 and abs(samples[-1]) < .007, name
        path = ROOT / (name + ".wav")
        if args.check:
            with wave.open(str(path), "rb") as file:
                assert file.getnchannels() == 1 and file.getsampwidth() == 2 and file.getframerate() == RATE
                expected = array.array("h", (round(v * 32767) for v in samples))
                assert file.readframes(file.getnframes()) == expected.tobytes(), name + " differs from source"
        else:
            write_wav(path, samples)
        audit.append(dict(cue=name, seconds=round(len(samples)/RATE, 3), peak=round(peak, 4), rms=round(rms, 4), bytes=path.stat().st_size))
        # Pair comparison repeats each twice, with a clear gap. Not imported by Unity.
        audition.extend(samples + [0.] * round(RATE * .32) + samples + [0.] * round(RATE * .75))
    output = Path(tempfile.gettempdir()) / "anca-enemy-audio"
    output.mkdir(exist_ok=True)
    write_wav(output / "enemy-cues-audition.wav", audition)
    (output / "signal-audit.json").write_text(json.dumps(audit, indent=2), encoding="utf-8")
    total = sum(item["bytes"] for item in audit)
    assert total < 500_000
    print(f"PASS {len(audit)} original mono clips, {total} bytes; no clipping/DC/end clicks; audition: {output}")


if __name__ == "__main__":
    main()
