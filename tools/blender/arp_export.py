import bpy, sys, os
infile = sys.argv[sys.argv.index("--") + 1]; out = sys.argv[sys.argv.index("--") + 2]
bpy.ops.wm.open_mainfile(filepath=infile)
sc = bpy.context.scene
rig = bpy.data.objects['rig']; mesh = bpy.data.objects['tripo_node_12a89ca9']
if bpy.context.object and bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT'); mesh.select_set(True); bpy.context.view_layer.objects.active = mesh
dec = mesh.modifiers.new("lsm_decimate", 'DECIMATE'); dec.ratio = 0.19; dec.use_collapse_triangulate = True
while mesh.modifiers.find("lsm_decimate") > 0: bpy.ops.object.modifier_move_up(modifier="lsm_decimate")
print("MODIFIERS:", [m.name for m in mesh.modifiers])
settings = dict(arp_engine_type='UNITY', arp_export_rig_type='UNIVERSAL', arp_bake_anim=True, arp_bake_type='ACTIONS',
    arp_bake_only_active=False, arp_export_use_actlist=False, arp_full_facial=False, arp_keep_bend_bones=False, arp_push_bend=True,
    arp_export_twist=False, arp_units_x100=True, arp_mesh_smooth_type='FACE', arp_export_tex=False, arp_export_triangulate=True,
    arp_apply_mods=True, arp_ge_force_rest_pose_export=True, arp_ge_sel_only=False, arp_export_noparent=False, arp_global_scale=1.0,
    arp_frame_range_type='FULL', arp_export_rig_name='root', arp_export_renaming=False, arp_fix_fbx_matrix=True, arp_fix_fbx_rot=False,
    arp_export_bake_axis_convert=False, arp_ge_add_dummy_mesh=False, arp_simplify_fac=0.01)
for k, v in settings.items():
    try: setattr(sc, k, v)
    except Exception as e: print("PROP", k, "->", e)
bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); mesh.select_set(True); bpy.context.view_layer.objects.active = rig
os.makedirs(os.path.dirname(out), exist_ok=True)
try:
    res = bpy.ops.arp.arp_export_fbx_panel(filepath=out, quick_export=True)
    print("EXPORT_RESULT", res)
except Exception as e:
    print("EXPORT_ERROR", e)
print("EXPORT_FILE", out, os.path.exists(out), os.path.getsize(out) if os.path.exists(out) else 0)
print("ARP_EXPORT_DONE")
