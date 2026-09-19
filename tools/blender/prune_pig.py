import bpy, sys, os, re
argv = sys.argv; infile = argv[argv.index("--") + 1]; out = argv[argv.index("--") + 2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=infile)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and o.vertex_groups)
print("imported", arm.name, len(arm.data.bones), "bones;", mesh.name, len(mesh.data.polygons), "polys")
# target bone per deleted bone
def target_for(n):
    if n.startswith("c_eye_target"): return None
    if re.match(r"c_(chin|lips_bot|teeth_bot|lips_corner_mini|lips_smile)|tong_", n): return "c_jawbone.x"
    if re.match(r"c_(lips_top|teeth_top)", n): return "c_skull_01.x"
    if re.match(r"c_(cheek|eye|eyelid|eyebrow|nose)", n): return "c_skull_02.x"
    return "KEEP"
delete = {b.name: target_for(b.name) for b in arm.data.bones if target_for(b.name) != "KEEP"}
print("deleting", len(delete), "bones")
# merge vertex weights
vg = mesh.vertex_groups
for name, tgt in delete.items():
    g = vg.get(name)
    if g is None: continue
    if tgt is not None:
        t = vg.get(tgt) or vg.new(name=tgt)
        for v in mesh.data.vertices:
            w = next((gw.weight for gw in v.groups if gw.group == g.index), 0.0)
            if w > 0.0:
                cur = next((gw.weight for gw in v.groups if gw.group == t.index), 0.0)
                t.add([v.index], cur + w, 'REPLACE')
    vg.remove(g)
# delete bones
bpy.context.view_layer.objects.active = arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
for name in delete:
    b = eb.get(name)
    if b: eb.remove(b)
bpy.ops.object.mode_set(mode='OBJECT')
print("bones now", len(arm.data.bones), ":", [b.name for b in arm.data.bones])
# normalize weights and cap at 4
for v in mesh.data.vertices:
    ws = sorted([(gw.group, gw.weight) for gw in v.groups if gw.weight > 1e-4], key=lambda t: -t[1])
    keep = ws[:4]; tot = sum(w for _, w in keep) or 1.0
    for gw in list(v.groups):
        if gw.group not in [k for k, _ in keep]: vg[gw.group].remove([v.index])
    for k, w in keep: vg[k].add([v.index], w / tot, 'REPLACE')
# clean action names: root|root|Walk -> Walk
for a in bpy.data.actions:
    a.name = a.name.split("|")[-1]
print("actions:", [a.name for a in bpy.data.actions])
# one NLA strip per action: the FBX exporter bakes strips as takes reliably (all-actions skips unassigned slotted actions)
if arm.animation_data is None: arm.animation_data_create()
ad = arm.animation_data
ad.action = None
for t in list(ad.nla_tracks): ad.nla_tracks.remove(t)
for a in bpy.data.actions:
    track = ad.nla_tracks.new(); track.name = a.name
    strip = track.strips.new(a.name, int(a.frame_range[0]), a)
    strip.name = a.name
    track.mute = False
print("nla tracks:", [(t.name, [s.name for s in t.strips]) for t in ad.nla_tracks])
# smooth shading
for p in mesh.data.polygons: p.use_smooth = True
# export
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); mesh.select_set(True); bpy.context.view_layer.objects.active = arm
os.makedirs(os.path.dirname(out), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
    bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0,
    use_mesh_modifiers=True, mesh_smooth_type='FACE', use_armature_deform_only=True, armature_nodetype='NULL',
    path_mode='AUTO', embed_textures=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
    axis_forward='-Z', axis_up='Y')
print("exported", out, os.path.getsize(out) // 1024, "KB")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(out), "pig_game_rig.blend"), compress=True)
print("PRUNE_DONE")
