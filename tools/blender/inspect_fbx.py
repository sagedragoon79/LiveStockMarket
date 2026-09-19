import bpy, sys, mathutils
argv = sys.argv; infile = argv[argv.index("--") + 1]
print("INSPECT_INPUT:", infile)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=infile)
objs = list(bpy.context.scene.objects)
print("OBJECT_COUNT:", len(objs))
for o in objs:
    parent = o.parent.name if o.parent else None
    print(f"OBJ name='{o.name}' type={o.type} parent={parent} loc={tuple(round(v,4) for v in o.location)} rotE_deg={tuple(round(v*57.2958,2) for v in o.rotation_euler)} scale={tuple(round(v,4) for v in o.scale)}")
    if o.type == 'ARMATURE':
        bones = o.data.bones
        print(f"   ARMATURE bones={len(bones)}")
        def walk(b, depth):
            print(f"   {'  '*depth}- {b.name}  head={tuple(round(v,3) for v in b.head_local)} len={b.length:.3f}")
            for c in b.children: walk(c, depth+1)
        for b in bones:
            if b.parent is None: walk(b, 0)
    if o.type == 'MESH':
        me = o.data
        bb = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
        xs = [v.x for v in bb]; ys = [v.y for v in bb]; zs = [v.z for v in bb]
        print(f"   MESH verts={len(me.vertices)} polys={len(me.polygons)} world_dims X={max(xs)-min(xs):.3f} Y={max(ys)-min(ys):.3f} Z={max(zs)-min(zs):.3f} zmin={min(zs):.3f} uv_layers={len(me.uv_layers)}")
        print(f"   vertex_groups={len(o.vertex_groups)} modifiers={[(m.type, getattr(m,'object',None).name if getattr(m,'object',None) else None) for m in o.modifiers]}")
        print(f"   vgroups: {[g.name for g in o.vertex_groups]}")
        for s in o.material_slots:
            mm = s.material
            print(f"   MATERIAL '{mm.name if mm else None}'")
print("ACTIONS:", len(bpy.data.actions))
for a in bpy.data.actions: print(f"   ACTION '{a.name}' frames={tuple(round(v,1) for v in a.frame_range)}")
print("INSPECT_DONE")
