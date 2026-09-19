import bpy, sys, os, math, mathutils
argv = sys.argv; blend = argv[argv.index("--") + 1]; outdir = argv[argv.index("--") + 2]
bpy.ops.wm.open_mainfile(filepath=blend)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE'); mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
# texture for the viewport render
img = bpy.data.images.load(os.path.join(os.path.dirname(os.path.abspath(blend)), "PigBase.png"))
mat = mesh.material_slots[0].material if mesh.material_slots else bpy.data.materials.new("PigMat")
if not mesh.material_slots: mesh.data.materials.append(mat)
mat.use_nodes = True; nt = mat.node_tree
tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img
bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
if bsdf: nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'; scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = 900; scene.render.resolution_y = 500; scene.render.resolution_percentage = 100
cam_data = bpy.data.cameras.new("chk"); cam = bpy.data.objects.new("chk", cam_data); scene.collection.objects.link(cam); scene.camera = cam
bpy.context.view_layer.update()
bb = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
center = sum(bb, mathutils.Vector()) / 8; size = max((max(v[i] for v in bb) - min(v[i] for v in bb)) for i in range(3))
cam_data.type = 'ORTHO'; cam_data.ortho_scale = size * 1.5; cam_data.clip_start = 0.001; cam_data.clip_end = 100
def shot(action, frame, name, iso=False):
    if arm.animation_data is None: arm.animation_data_create()
    act = bpy.data.actions[action]; arm.animation_data.action = act
    try:
        if act.slots: arm.animation_data.action_slot = act.slots[0]
    except Exception: pass
    scene.frame_set(frame); bpy.context.view_layer.update()
    if iso: cam.location = center + mathutils.Vector((size*2, -size*2, size*1.6)); cam.rotation_euler = (math.radians(58), 0, math.radians(45))
    else: cam.location = center + mathutils.Vector((size*3, 0, size*0.5)); cam.rotation_euler = (math.radians(84), 0, math.radians(90))
    scene.render.filepath = os.path.join(outdir, name); bpy.ops.render.render(write_still=True); print("rendered", name)
shot("Walk", 16, "pig_walk_side.png"); shot("GrazeLoop", 30, "pig_graze_iso.png", iso=True); shot("Death", 90, "pig_death_iso.png", iso=True)
print("RENDER_DONE")
