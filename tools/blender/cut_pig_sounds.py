"""Cut the two source recordings into the mono 22.05 kHz WAV clips the pig bundle ships.

Run headless:
  blender -b --python tools/blender/cut_pig_sounds.py -- <grunts.mp3> <breathing.mp3> art/Pig/audio

Segment times were found with a 20 ms RMS envelope over the grunt recording (threshold 12 % of
the loudest window): 17 short grunts (0.15-0.8 s, spectral centroid 1.0-1.8 kHz) and 6 long
squeals (1-4.3 s, 2.3-2.9 kHz). The breathing loop is a steady 16 s stretch without the snorts
near the end, with its tail crossfaded into its head so it loops without a click.
"""
import aud, numpy as np, os, sys

args = sys.argv[sys.argv.index("--") + 1:]
grunt_file, breath_file, outdir = args[0], args[1], args[2]
os.makedirs(outdir, exist_ok=True)
RATE = 22050

GRUNTS = [(12.10, 12.28), (12.64, 12.84), (13.22, 13.56), (13.98, 14.38), (14.78, 14.96), (15.92, 16.06),
          (16.32, 16.58), (16.98, 17.24), (18.42, 19.00), (19.28, 19.98), (20.74, 21.50), (23.10, 23.46),
          (24.44, 24.70), (25.44, 25.88), (26.38, 27.02), (27.68, 28.02), (28.66, 28.94)]
SQUEALS = [(2.82, 7.08), (7.68, 8.68), (33.28, 33.48), (34.00, 36.40), (36.94, 38.34), (38.96, 40.76)]
BREATH = (18.5, 34.5)
PAD, FADE = 0.04, 0.02


def export(sound, path, peak_target):
    mono = sound.rechannel(1)
    d = mono.data()
    peak = float(np.abs(d).max()) if d.size else 0.0
    gain = peak_target / peak if peak > 1e-6 else 1.0
    mono.resample(RATE).volume(gain).write(path, aud.RATE_22050, aud.CHANNELS_MONO, aud.FORMAT_S16,
                                           aud.CONTAINER_WAV, aud.CODEC_PCM)
    print(f"WROTE {os.path.basename(path)} {d.shape[0] / sound.specs[0]:.2f}s peak {peak:.3f} gain {gain:.2f} -> {os.path.getsize(path) // 1024} KB")


def cut(src, a, b, pre=PAD, post=PAD, fade=FADE):
    a = max(0.0, a - pre)
    b = b + post
    return src.limit(a, b).fadein(0, fade).fadeout((b - a) - fade, fade)


g = aud.Sound(grunt_file)
for i, (a, b) in enumerate(GRUNTS, 1):
    export(cut(g, a, b), os.path.join(outdir, f"PigGrunt{i:02d}.wav"), 0.8)
for i, (a, b) in enumerate(SQUEALS, 1):
    export(cut(g, a, b, post=0.15, fade=0.05), os.path.join(outdir, f"PigSqueal{i:02d}.wav"), 0.85)

# breathing: steady stretch, seam crossfaded, quiet
br = aud.Sound(breath_file)
seg = br.limit(BREATH[0], BREATH[1]).rechannel(1).resample(RATE)
d = seg.data().astype(np.float32).reshape(-1)
x = int(RATE * 0.75)
body = d[:-x].copy()
tail = d[-x:]
ramp = np.linspace(0.0, 1.0, x, dtype=np.float32)
body[:x] = body[:x] * ramp + tail * (1.0 - ramp)
peak = float(np.abs(body).max())
if peak > 1e-6:
    body *= 0.5 / peak
loop = aud.Sound.buffer(np.ascontiguousarray(body.reshape(-1, 1)), RATE)
path = os.path.join(outdir, "PigBreathing.wav")
loop.write(path, aud.RATE_22050, aud.CHANNELS_MONO, aud.FORMAT_S16, aud.CONTAINER_WAV, aud.CODEC_PCM)
print(f"WROTE PigBreathing.wav {len(body) / RATE:.2f}s loop, seam crossfade 0.75 s, peak 0.50 -> {os.path.getsize(path) // 1024} KB")
print("CUT_DONE")
