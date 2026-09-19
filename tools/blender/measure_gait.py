import bpy, sys, mathutils, statistics, math
# Gait measurement in ARMATURE space (object transforms ignored): cadence, stride and natural speed in body lengths.
args = sys.argv[sys.argv.index("--") + 1:]
path = args[0]
action_name = args[1] if len(args) > 1 else "Walk"
keys = tuple(args[2].lower().split(",")) if len(args) > 2 else ("foot", "toes", "hand", "finger")
if path.lower().endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=path)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
scene = bpy.context.scene
fps = scene.render.fps / scene.render.fps_base
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
act = bpy.data.actions.get(action_name)
if act is None:
    act = next((a for a in bpy.data.actions if a.name.endswith("|" + action_name)), None)
if act is None:
    print("ACTIONS", [a.name for a in bpy.data.actions]); print("NO ACTION", action_name); sys.exit(0)
if not arms:
    print("NO ARMATURE"); sys.exit(0)
arm = arms[0]
ad = arm.animation_data or arm.animation_data_create()
for t in ad.nla_tracks: t.mute = True
ad.action = act
try:
    if act.slots: ad.action_slot = act.slots[0]
except Exception as e: print("slot", e)
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
cycle = (f1 - f0) / fps
scene.frame_set(f0); bpy.context.view_layer.update()
names = [b.name for b in arm.pose.bones if any(k in b.name.lower() for k in keys)]
print("CANDIDATES", names)
if not names:
    print("BONES", [b.name for b in arm.pose.bones]); sys.exit(0)
pbs = arm.pose.bones
# rest-pose geometry in armature space: up = axis where the root sits farthest above the feet
allpts = [pb.head for pb in pbs] + [pb.tail for pb in pbs]
ext = [max(p[i] for p in allpts) - min(p[i] for p in allpts) for i in range(3)]
root0 = pbs[0].head.copy()
feet0 = [pbs[n].tail.copy() for n in names]
mean_feet = sum(feet0, mathutils.Vector()) / len(feet0)
up = max(range(3), key=lambda i: root0[i] - mean_feet[i])
horiz = [i for i in range(3) if i != up]
fwd_body = max(horiz, key=lambda i: ext[i])
body = ext[fwd_body]
print(f"ACTION {act.name} on '{arm.name}': frames {f0}-{f1} = {cycle:.3f} s at {fps:g} fps → {1 / cycle:.3f} cycles/s; bone extents {[round(e, 4) for e in ext]}; up {'XYZ'[up]}, body axis {'XYZ'[fwd_body]}, body length {body:.4f} (bone span)")
pts = {n: [] for n in names}
root = []
for f in range(f0, f1 + 1):
    scene.frame_set(f); bpy.context.view_layer.update()
    root.append(pbs[0].head.copy())
    for n in names:
        pts[n].append(pbs[n].tail.copy())
rr = [max(p[i] for p in root) - min(p[i] for p in root) for i in range(3)]
print(f"ROOT bone '{pbs[0].name}' travel {[round(r, 4) for r in rr]} (in place if ~0)")
strides = []
for n in names:
    p = pts[n]
    ranges = [max(q[i] for q in p) - min(q[i] for q in p) for i in range(3)]
    fwd = max(horiz, key=lambda i: ranges[i])
    hmin = min(q[up] for q in p)
    tol = ranges[up] * 0.15 + 1e-6
    vel = [(p[i + 1][fwd] - p[i][fwd]) * fps for i in range(len(p) - 1)]
    stance = [vel[i] for i in range(len(vel)) if p[i][up] - hmin < tol and p[i + 1][up] - hmin < tol]
    if not stance or body <= 0:
        print(f"BONE {n}: no stance frames (lift {ranges[up]:.4f})"); continue
    med = statistics.median(stance)
    stride_bl = abs(med) * cycle / body
    strides.append(stride_bl)
    print(f"BONE {n}: fwd={'XYZ'[fwd]} travel={ranges[fwd] / body:.3f} BL lift={ranges[up] / body:.3f} BL stance={len(stance)}/{len(vel)} → stride {stride_bl:.3f} BL/cycle, natural {stride_bl / cycle:.3f} BL/s")
if strides:
    s = statistics.median(strides)
    print(f"RESULT {act.name}: cadence {1 / cycle:.3f} cycles/s, stride {s:.3f} BL/cycle, natural speed {s / cycle:.3f} BL/s")
