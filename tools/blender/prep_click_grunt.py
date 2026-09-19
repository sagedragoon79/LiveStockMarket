"""Prepare the hand-made click grunt for the pig bundle.

Run headless:
  blender -b --python tools/blender/prep_click_grunt.py -- art/Pig/audio/UIShortPigGrunt.wav art/Pig/audio/PigClick.wav

The source is left untouched. The prepared copy starts 20 ms before the grunt's onset (a click
sound must answer at once; the source has about 230 ms of room noise in front), gets a 10 ms
fade-in and a 30 ms fade-out, and is written mono, 22.05 kHz, 16-bit with its peak at 0.8, like
the other clips. The onset is the first 10 ms window within 30 dB of the loudest window.
"""
import aud, numpy as np, os, sys

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
PRE, FADE_IN, FADE_OUT, PEAK = 0.02, 0.01, 0.03, 0.8

s = aud.Sound(src)
rate = int(s.specs[0])
mono = s.rechannel(1).data().reshape(-1)
win = max(1, rate // 100)
n = len(mono) // win
rms = np.sqrt((mono[:n * win].reshape(n, win) ** 2).mean(axis=1))
db = 20 * np.log10(rms + 1e-9)
onset_win = int(np.argmax(db > db.max() - 30.0))
start = max(0.0, onset_win * win / rate - PRE)
end = len(mono) / rate
length = end - start
peak = float(np.abs(mono[int(start * rate):]).max())
gain = PEAK / peak if peak > 1e-6 else 1.0

clip = s.limit(start, end).rechannel(1).fadein(0, FADE_IN).fadeout(length - FADE_OUT, FADE_OUT).resample(22050).volume(gain)
os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
clip.write(out, aud.RATE_22050, aud.CHANNELS_MONO, aud.FORMAT_S16, aud.CONTAINER_WAV, aud.CODEC_PCM)
print(f"PREP source {end:.3f}s at {rate} Hz; onset at {onset_win * win / rate:.3f}s (noise floor {np.median(db[:max(1, onset_win)]):.0f} dB, peak window {db.max():.1f} dB)")
print(f"WROTE {os.path.basename(out)} {length:.3f}s from {start:.3f}s, peak {peak:.3f} -> gain {gain:.2f}, {os.path.getsize(out) // 1024} KB")
print("PREP_DONE")
