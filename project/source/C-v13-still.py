from pathlib import Path
import sys
import math

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[2] / 'tmp-b-packaging-runtime'))
sys.path.insert(0, str(ROOT.parents[2] / 'tmp/b-packaging-runtime'))
import bpy
from mathutils import Vector

OUT = ROOT / 'C'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'C-master.blend'))
scene = bpy.context.scene
base = bpy.data.objects['Removable paper cassette']
trays = [bpy.data.objects[f'Colour palette {i}'] for i in range(1, 4)]
for obj in bpy.data.objects:
    if obj.animation_data:
        obj.animation_data_clear()

base.location = (0, -350, 4)
trays[0].location = (-10, 4, 3)
trays[1].location = (-260, 4, -4)
trays[2].location = (240, 4, -4)

# Only the main cassette side walls open. The two smaller moved-out trays
# keep both of their own side walls upright.
for obj in bpy.data.objects:
    if obj.name.startswith('Cassette full drop hinge'):
        sign = -1 if obj.name.endswith(' L') else 1
        obj.rotation_euler = (0, sign * math.pi / 2, 0)
    elif 'full side hinge' in obj.name.lower():
        obj.rotation_euler = (0, 0, 0)

# Fixed upper/rear baffles and the printed lower baffle remain attached.
for name, loc in [('Cassette front art', (0, 0, 0)), ('Cassette end1', (0, 1, 37)), ('Cassette end319', (0, 319, 37))]:
    obj = bpy.data.objects.get(name)
    if obj:
        obj.parent = base
        obj.location = loc
        obj.rotation_euler = (0, 0, 0)
        obj.hide_render = False
divider = bpy.data.objects.get('Blender bay divider')
if divider:
    divider.hide_render = True
for name in ['Cassette locking tongue-75', 'Cassette locking tongue75']:
    obj = bpy.data.objects.get(name)
    if obj:
        obj.hide_render = True

scene.camera = bpy.data.objects.get('Concept camera')
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x = 1536
scene.render.resolution_y = 1024
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.frame_set(1)
scene.render.filepath = str(OUT / 'comparison-open-v13.png')
bpy.ops.render.render(write_still=True)

bpy.ops.object.camera_add()
cam = bpy.context.object
cam.name = 'C v13 centre-right still camera'
cam.data.type = 'ORTHO'
cam.data.ortho_scale = 430
cam.location = (-340, -820, 420)
cam.rotation_euler = (Vector((-10, -420, 8)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
scene.camera = cam
scene.render.filepath = str(OUT / 'mechanism-clearance-v13.png')
bpy.ops.render.render(write_still=True)

scene.camera = bpy.data.objects.get('Concept camera')
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'C-master-v13-still.blend'))
print(OUT / 'comparison-open-v13.png')
print(OUT / 'mechanism-clearance-v13.png')
