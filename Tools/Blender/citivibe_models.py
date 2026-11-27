"""Original CitiVibe street-scale models, built procedurally and exported as FBX for Unity.

Run headless:
    Blender -b -P Tools/Blender/citivibe_models.py -- <output dir> [preview dir]

Every model is authored facing +Y (front) with +X to its right and Z up, then turned
180 degrees before export so it faces Unity's +Z. Dimensions are metres and follow
the NYC references in CitiVibe's research notes (sedans ~4.9 m, 40 ft buses, 5 ft
sidewalk flags, 7.5 ft benches, ~1.7 m people). Material names are remapped to URP
materials by the Unity builder, so only names matter here.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector, noise

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = args[0] if args else os.path.join(os.getcwd(), "Assets/CitiVibe/Models")
PREVIEW = args[1] if len(args) > 1 else None
os.makedirs(OUT, exist_ok=True)

ROT180 = Matrix.Rotation(math.pi, 4, "Z")


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name):
    m = bpy.data.materials.get(name)
    return m if m else bpy.data.materials.new(name)


class Part:
    """One exported object: geometry accumulated in a bmesh with per-face materials."""

    def __init__(self, name, origin=(0, 0, 0)):
        self.name = name
        self.origin = Vector(origin)
        self.bm = bmesh.new()
        self.mats = []
        self.bevel = None
        self.flat = False

    def mi(self, name):
        if name not in self.mats:
            self.mats.append(name)
        return self.mats.index(name)

    def _tag(self, verts, mat):
        index = self.mi(mat)
        faces = {f for v in verts for f in v.link_faces}
        for f in faces:
            f.material_index = index
        return faces

    @staticmethod
    def _matrix(center, size=(1, 1, 1), rot=(0, 0, 0)):
        r = Matrix.Rotation(rot[2], 4, "Z") @ Matrix.Rotation(rot[1], 4, "Y") @ Matrix.Rotation(rot[0], 4, "X")
        s = Matrix.Diagonal((size[0], size[1], size[2], 1))
        return Matrix.Translation(Vector(center)) @ r @ s

    def box(self, center, size, mat, rot=(0, 0, 0)):
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=self._matrix(center, size, rot), calc_uvs=True)
        return self._tag(res["verts"], mat)

    def cyl(self, center, r, depth, mat, segs=16, r2=None, rot=(0, 0, 0), caps=True):
        """Cylinder or cone centred on `center`, along local Z before `rot`."""
        res = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segs, radius1=r,
                                    radius2=r if r2 is None else r2, depth=depth,
                                    matrix=self._matrix(center, (1, 1, 1), rot), calc_uvs=True)
        return self._tag(res["verts"], mat)

    def sphere(self, center, radii, mat, subdiv=2, rot=(0, 0, 0), wobble=0.0, seed=0.0):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=1.0,
                                         matrix=Matrix.Identity(4), calc_uvs=True)
        m = self._matrix(center, radii, rot)
        for v in res["verts"]:
            d = v.co.normalized()
            if wobble:
                d *= 1.0 + wobble * noise.noise(d * 1.9 + Vector((seed, seed * 0.7, seed * 1.3)))
            v.co = m @ d
        return self._tag(res["verts"], mat)

    def uvsphere(self, center, radii, mat, u=16, v=10, rot=(0, 0, 0)):
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=1.0,
                                        matrix=self._matrix(center, radii, rot), calc_uvs=True)
        return self._tag(res["verts"], mat)

    def torus(self, center, R, r, mat, segs=24, tube=8, rot=(0, 0, 0)):
        m = self._matrix(center, (1, 1, 1), rot)
        rings = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            ring = []
            for j in range(tube):
                b = 2 * math.pi * j / tube
                p = Vector(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b)))
                ring.append(self.bm.verts.new(m @ p))
            rings.append(ring)
        verts = []
        for i in range(segs):
            for j in range(tube):
                a, b = rings[i], rings[(i + 1) % segs]
                self.bm.faces.new((a[j], b[j], b[(j + 1) % tube], a[(j + 1) % tube]))
            verts += rings[i]
        return self._tag(verts, mat)

    def profile(self, pts, x0, x1, mat, taper=0.0, z_base=0.0, z_top=1.0):
        """Extrude a side profile (y, z) across x0..x1; `taper` pulls the top edges inward."""
        def x_at(x, z):
            k = 0.0 if z_top == z_base else max(0.0, min(1.0, (z - z_base) / (z_top - z_base)))
            return x - math.copysign(taper * k, x)
        left = [self.bm.verts.new((x_at(x0, z), y, z)) for y, z in pts]
        right = [self.bm.verts.new((x_at(x1, z), y, z)) for y, z in pts]
        n = len(pts)
        faces = [self.bm.faces.new(left), self.bm.faces.new(list(reversed(right)))]
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.bm.faces.new((left[i], left[j], right[j], right[i])))
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        return self._tag(left + right, mat)

    def tube(self, points, r, mat, segs=8):
        """Swept tube through a polyline (rails, bike racks, branches)."""
        for a, b in zip(points[:-1], points[1:]):
            a, b = Vector(a), Vector(b)
            d = b - a
            rot = d.to_track_quat("Z", "Y").to_euler()
            self.cyl((a + b) / 2, r, d.length + r * 0.6, mat, segs=segs, rot=(rot.x, rot.y, rot.z))

    def finish(self):
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for m in self.mats:
            mesh.materials.append(material(m))
        for p in mesh.polygons:
            p.use_smooth = not self.flat
        ob = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(ob)
        ob.location = self.origin
        if self.bevel:
            mod = ob.modifiers.new("Bevel", "BEVEL")
            mod.width, mod.segments = self.bevel
            mod.limit_method = "ANGLE"
            mod.angle_limit = math.radians(35)
            mod.harden_normals = True
            mod.miter_outer = "MITER_ARC"
        return ob


def boolean_cut(target, cutters):
    for c in cutters:
        mod = target.modifiers.new("Cut", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.object = c
        bpy.context.view_layer.objects.active = target
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(c, do_unlink=True)


def cutter_cylinder(center, r, depth, rot):
    p = Part("cutter")
    p.cyl(center, r, depth, "cut", segs=28, rot=rot)
    return p.finish()


def export(name):
    for ob in bpy.context.scene.objects:
        ob.data.transform(ROT180)
        ob.location = ROT180 @ ob.location
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=False,
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             mesh_smooth_type="OFF", use_mesh_modifiers=True, use_tspace=False,
                             add_leaf_bones=False, bake_anim=False)
    if PREVIEW:
        colorize_preview(); render_preview(name)


def render_preview(name):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x = scene.render.resolution_y = 360
    scene.render.film_transparent = False
    mins = Vector((1e9, 1e9, 1e9))
    maxs = -mins
    for ob in scene.objects:
        for corner in ob.bound_box:
            w = ob.matrix_world @ Vector(corner)
            mins = Vector(map(min, mins, w))
            maxs = Vector(map(max, maxs, w))
    center = (mins + maxs) / 2
    size = max((maxs - mins).length, 0.5)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 1.05
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    direction = Vector((1.0, -1.25, 0.8)).normalized()
    cam.location = center + direction * size * 3
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.filepath = os.path.join(PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)


def colorize_preview():
    """Workbench previews use viewport colours; give the shared material names believable tints."""
    tints = {
        "Paint": (0.55, 0.6, 0.7), "Glass": (0.1, 0.13, 0.16), "Tire": (0.04, 0.04, 0.04), "Rim": (0.6, 0.6, 0.62),
        "Trim": (0.05, 0.05, 0.05), "Chrome": (0.8, 0.8, 0.8), "Headlight": (1, 1, 0.9), "Taillight": (0.7, 0.05, 0.04),
        "Plate": (0.9, 0.9, 0.85), "TaxiSign": (0.95, 0.9, 0.8), "Skin": (0.8, 0.6, 0.48), "Shirt": (0.3, 0.45, 0.6),
        "Pants": (0.15, 0.18, 0.25), "Shoes": (0.08, 0.06, 0.05), "Hair": (0.15, 0.1, 0.06), "Accent": (0.6, 0.2, 0.15),
        "Bark": (0.3, 0.24, 0.18), "LeafA": (0.25, 0.42, 0.18), "LeafB": (0.35, 0.5, 0.2), "LeafC": (0.42, 0.52, 0.22),
        "Iron": (0.06, 0.06, 0.06), "PoleSilver": (0.55, 0.57, 0.56), "LampLens": (1, 0.95, 0.8), "LampHead": (0.3, 0.32, 0.32),
        "SignalBody": (0.1, 0.12, 0.1), "LensRed": (0.8, 0.1, 0.1), "LensAmber": (0.9, 0.6, 0.1), "LensGreen": (0.1, 0.8, 0.5),
        "LensWalk": (0.9, 0.9, 0.9), "LensStop": (0.95, 0.45, 0.1), "HydrantBody": (0.6, 0.1, 0.07), "HydrantCap": (0.75, 0.75, 0.72),
        "MailBlue": (0.08, 0.2, 0.45), "Wood": (0.45, 0.32, 0.2), "BagBlack": (0.03, 0.03, 0.03), "BagWhite": (0.85, 0.85, 0.82),
        "BasketGreen": (0.1, 0.25, 0.15), "AcWhite": (0.85, 0.85, 0.82), "Galvanized": (0.6, 0.62, 0.62), "GlassPanel": (0.6, 0.7, 0.72),
        "KioskGreen": (0.08, 0.3, 0.2), "TankWood": (0.42, 0.3, 0.2), "Concrete": (0.65, 0.64, 0.6), "Interior": (0.15, 0.14, 0.13),
        "MapPanel": (0.85, 0.85, 0.8), "CargoBox": (0.9, 0.9, 0.88), "BusBody": (0.85, 0.87, 0.9), "BusStripe": (0.1, 0.35, 0.7),
    }
    for m in bpy.data.materials:
        m.diffuse_color = (*tints.get(m.name, (0.6, 0.6, 0.6)), 1)


# ---------------------------------------------------------------- vehicles

def wheels(body, track, axles, r=0.34, width=0.23, hub="Rim", cut=False):
    parts = []
    for y in axles:
        for sx in (-1, 1):
            x = sx * (track / 2)
            body.cyl((x, y, r), r, width, "Tire", segs=24, rot=(0, math.pi / 2, 0))
            body.cyl((x + sx * width * 0.5, y, r), r * 0.62, 0.02, hub, segs=20, rot=(0, math.pi / 2, 0))
            for k in range(5):
                a = k * 2 * math.pi / 5
                body.box((x + sx * width * 0.52, y + math.cos(a) * r * 0.32, r + math.sin(a) * r * 0.32),
                         (0.02, 0.05, r * 0.55), hub, rot=(a, 0, 0))
            body.cyl((x + sx * width * 0.53, y, r), r * 0.14, 0.03, "Chrome", segs=10, rot=(0, math.pi / 2, 0))
            if cut:
                parts.append(cutter_cylinder((x, y, r), r + 0.07, width + 0.6, (0, math.pi / 2, 0)))
    return parts


def arch_cutters(track, axles, r=0.34, width=0.23):
    return [cutter_cylinder((sx * track / 2, y, r), r + 0.07, width + 0.6, (0, math.pi / 2, 0)) for y in axles for sx in (-1, 1)]


def car(name, kind, taxi=False):
    reset()
    random.seed(name)
    # Side profiles (y forward, z up) for the lower body and the glazed cabin.
    if kind == "sedan":
        L, W, track, axles = 4.88, 1.84, 1.58, (1.42, -1.38)
        lower = [(-2.44, 0.28), (2.42, 0.28), (2.48, 0.52), (2.36, 0.8), (1.3, 0.97), (-1.6, 0.99), (-2.38, 0.92), (-2.48, 0.6)]
        cabin = [(-1.62, 0.96), (1.28, 0.95), (0.42, 1.44), (-0.72, 1.46)]
        roof_z, cab_taper = 1.46, 0.16
    elif kind == "suv":
        L, W, track, axles = 4.6, 1.88, 1.6, (1.36, -1.34)
        lower = [(-2.3, 0.36), (2.28, 0.36), (2.34, 0.62), (2.2, 0.95), (1.25, 1.1), (-2.18, 1.12), (-2.32, 0.9)]
        cabin = [(-2.16, 1.08), (1.28, 1.08), (0.58, 1.68), (-2.02, 1.7)]
        roof_z, cab_taper = 1.7, 0.12
    else:  # van
        L, W, track, axles = 5.9, 2.0, 1.72, (1.9, -1.95)
        lower = [(-2.95, 0.36), (2.92, 0.36), (2.98, 0.66), (2.82, 1.0), (2.2, 1.18), (-2.95, 1.18)]
        cabin = [(-2.95, 1.16), (2.22, 1.16), (1.55, 2.28), (-2.95, 2.36)]
        roof_z, cab_taper = 2.36, 0.06
    shell = Part("Shell")
    shell.bevel = (0.045, 3)
    shell.profile(lower, -W / 2, W / 2, "Paint", taper=0.05, z_base=0.3, z_top=1.0)
    shell_ob = shell.finish()
    boolean_cut(shell_ob, arch_cutters(track, axles))
    body = Part("Body")
    body.bevel = (0.03, 2)
    if kind == "van":
        # Cargo vans have a painted box and glass only in the cab.
        body.profile(cabin, -W / 2 + 0.02, W / 2 - 0.02, "Paint", taper=cab_taper, z_base=1.16, z_top=roof_z)
        body.profile([(1.0, 1.32), (2.18, 1.3), (1.56, 2.2), (1.0, 2.24)], -W / 2 + 0.005, W / 2 - 0.005, "Glass",
                     taper=cab_taper, z_base=1.16, z_top=roof_z)
        body.box((0, 2.0, 1.72), (W * 0.86, 0.02, 0.9), "Glass", rot=(-0.62, 0, 0))
    else:
        body.profile(cabin, -W / 2 + 0.08, W / 2 - 0.08, "Glass", taper=cab_taper, z_base=cabin[0][1], z_top=roof_z)
        y0, y1 = cabin[3][0] + 0.05, cabin[2][0] - 0.05
        body.box((0, (y0 + y1) / 2, roof_z + 0.01), (W - 0.16 - 2 * cab_taper, y1 - y0, 0.05), "Paint")
        # Pillars frame the glass.
        for sx in (-1, 1):
            xs = sx * (W / 2 - 0.08 - cab_taper * 0.55)
            mid = (cabin[1][0] + cabin[2][0]) / 2
            a_len = (Vector(cabin[1]) - Vector(cabin[2])).length
            a_ang = math.atan2(cabin[1][1] - cabin[2][1], cabin[1][0] - cabin[2][0])
            body.box((xs, mid, (cabin[1][1] + cabin[2][1]) / 2), (0.07, a_len, 0.07), "Paint", rot=(a_ang, 0, 0))
            body.box((xs, (cabin[0][0] + cabin[3][0]) / 2 + 0.2 if kind == "suv" else -0.15, (cabin[0][1] + roof_z) / 2),
                     (0.07, 0.11, roof_z - cabin[0][1]), "Paint")
            c_len = (Vector(cabin[0]) - Vector(cabin[3])).length
            c_ang = math.atan2(cabin[3][1] - cabin[0][1], cabin[3][0] - cabin[0][0])
            body.box((xs, (cabin[0][0] + cabin[3][0]) / 2, (cabin[0][1] + cabin[3][1]) / 2),
                     (0.07, c_len, 0.12 if kind == "suv" else 0.18), "Paint", rot=(c_ang, 0, 0))
    front, rear = lower[2][0], lower[-1][0]
    z_lamp = lower[3][1] - 0.12
    for sx in (-1, 1):
        body.box((sx * W * 0.34, front - 0.06, z_lamp), (0.4, 0.14, 0.11), "Headlight")
        body.box((sx * W * 0.38, rear + 0.05, z_lamp + 0.02), (0.34, 0.12, 0.13), "Taillight")
        body.box((sx * (W / 2 + 0.06), cabin[1][0] - 0.12, cabin[1][1] + 0.08), (0.1, 0.06, 0.12), "Paint")
        for y in (0.45, -0.75):
            body.box((sx * (W / 2 + 0.005), y, lower[3][1] - 0.06), (0.02, 0.16, 0.03), "Chrome")
        body.box((sx * (W / 2 - 0.02), 0, 0.42), (0.06, L * 0.6, 0.1), "Trim")
    body.box((0, front + 0.01, z_lamp - 0.08), (W * 0.42, 0.1, 0.16), "Trim")
    body.box((0, front + 0.06, 0.36), (W * 0.95, 0.12, 0.14), "Trim")
    body.box((0, rear - 0.06, 0.36), (W * 0.95, 0.12, 0.14), "Trim")
    body.box((0, front + 0.07, 0.5), (0.5, 0.02, 0.12), "Plate")
    body.box((0, rear - 0.07, 0.62), (0.5, 0.02, 0.12), "Plate")
    body.box((0, 0, 0.55), (W * 0.9, L * 0.8, 0.5), "Interior")
    if kind == "suv":
        for sx in (-1, 1):
            body.box((sx * 0.62, -0.5, roof_z + 0.08), (0.04, 2.2, 0.05), "Trim")
    if taxi:
        body.box((0, -0.2, roof_z + 0.16), (0.82, 0.3, 0.26), "TaxiSign")
        body.box((0, -0.2, roof_z + 0.04), (0.6, 0.2, 0.06), "Trim")
        for sx in (-1, 1):
            body.box((sx * (W / 2 + 0.002), 0.1, 0.72), (0.01, L * 0.55, 0.06), "Trim")
    body.finish()
    rims = Part("Wheels")
    rims.bevel = (0.03, 2)
    wheels(rims, track, axles)
    for y in axles:
        for sx in (-1, 1):
            rims.cyl((sx * (track / 2 - 0.02), y, 0.34), 0.42, 0.24, "Trim", segs=24, rot=(0, math.pi / 2, 0), caps=False)
    rims.finish()
    export(name)


def box_truck():
    reset()
    body = Part("Body")
    body.bevel = (0.04, 2)
    W = 2.3
    body.profile([(2.4, 0.5), (3.55, 0.5), (3.62, 0.85), (3.45, 1.4), (3.25, 1.58), (2.75, 2.5), (2.4, 2.55)],
                 -W / 2, W / 2, "Paint", taper=0.08, z_base=0.5, z_top=2.5)
    body.profile([(3.0, 1.62), (3.28, 1.6), (2.78, 2.42), (2.6, 2.44)], -W / 2 - 0.004, W / 2 + 0.004, "Glass",
                 taper=0.08, z_base=0.5, z_top=2.5)
    body.box((0, 3.34, 2.0), (W * 0.84, 0.02, 0.72), "Glass", rot=(-0.55, 0, 0))
    body.box((0, -1.25, 2.05), (2.44, 7.1, 2.75), "CargoBox")
    body.box((0, -1.25, 0.62), (2.0, 7.4, 0.24), "Trim")
    body.box((0, -4.82, 0.62), (2.3, 0.2, 0.16), "Trim")
    for sx in (-1, 1):
        body.box((sx * 0.8, 3.58, 1.0), (0.32, 0.1, 0.16), "Headlight")
        body.box((sx * 1.0, -4.81, 1.0), (0.18, 0.04, 0.32), "Taillight")
        body.box((sx * 1.28, 2.95, 2.15), (0.06, 0.12, 0.36), "Trim")
    body.box((0, -4.81, 2.05), (2.3, 0.03, 2.6), "Trim")
    body.box((0, 3.62, 0.62), (2.24, 0.14, 0.2), "Trim")
    wheels(body, 1.95, (2.9, -2.1, -3.15), r=0.47, width=0.28, hub="Rim")
    body.finish()
    export("BoxTruck")


def bus():
    reset()
    body = Part("Body")
    body.bevel = (0.06, 3)
    W, L, H = 2.55, 12.2, 3.1
    body.box((0, 0, 0.35 + H / 2), (W, L, H), "BusBody")
    for sx in (-1, 1):
        body.box((sx * (W / 2 + 0.004), 0.3, 2.15), (0.01, L - 1.6, 1.15), "Glass")
        body.box((sx * (W / 2 + 0.006), 0, 1.25), (0.01, L - 0.3, 0.18), "BusStripe")
        for y in [-4.2 + i * 1.45 for i in range(7)]:
            body.box((sx * (W / 2 + 0.008), y, 2.15), (0.012, 0.08, 1.15), "BusBody")
    body.box((0, L / 2 + 0.004, 2.05), (W - 0.2, 0.02, 1.6), "Glass")
    body.box((0, -L / 2 - 0.004, 2.4), (W - 0.6, 0.02, 0.7), "Glass")
    body.box((W / 2 + 0.008, 4.6, 1.5), (0.012, 1.2, 2.3), "Glass")
    body.box((W / 2 + 0.008, -0.6, 1.5), (0.012, 1.2, 2.3), "Glass")
    for sx in (-1, 1):
        body.box((sx * 0.95, L / 2 + 0.03, 0.85), (0.3, 0.04, 0.14), "Headlight")
        body.box((sx * 1.05, -L / 2 - 0.03, 0.95), (0.2, 0.04, 0.4), "Taillight")
    body.box((0, L / 2 + 0.03, 3.08), (1.6, 0.05, 0.22), "TaxiSign")
    body.box((0, 0.2, H + 0.5), (1.8, 3.2, 0.32), "BusBody")
    ob = body.finish()
    boolean_cut(ob, arch_cutters(2.1, (3.85, -2.45), r=0.5, width=0.3))
    w = Part("Wheels")
    wheels(w, 2.1, (3.85, -2.45), r=0.5, width=0.3)
    w.finish()
    export("Bus")


# ---------------------------------------------------------------- people

def limb(p, top, length, r0, r1, mat, segs=10):
    p.cyl((top[0], top[1], top[2] - length / 2), r0, length, mat, segs=segs, r2=r1)
    p.sphere((top[0], top[1], top[2] - length), (r1, r1, r1), mat, subdiv=1)


def person(name, variant):
    """Rigid-limb pedestrian: Body plus LegL/R and ArmL/R pivoting at hip and shoulder."""
    reset()
    female = variant in ("skirt", "dress")
    hip_z, shoulder_z = 0.93, 1.43
    sw = 0.17 if female else 0.2
    body = Part("Body")
    body.bevel = (0.05, 3)
    torso_top = Part("Torso")
    torso_top.flat = False
    # Torso: rounded, slightly tapered chest and waist.
    body.profile([(-0.11, hip_z + 0.05), (0.11, hip_z + 0.05), (0.13, 1.3), (0.1, shoulder_z + 0.05), (-0.1, shoulder_z + 0.05), (-0.12, 1.3)],
                 -sw, sw, "Shirt", taper=0.04, z_base=hip_z, z_top=shoulder_z)
    body.box((0, 0, hip_z + 0.02), (sw * 1.9, 0.22, 0.16), "Pants")
    body.cyl((0, 0.005, 1.53), 0.05, 0.12, "Skin", segs=10)
    body.sphere((0, 0.01, 1.64), (0.088, 0.1, 0.118), "Skin", subdiv=3)
    body.sphere((0, 0.098, 1.63), (0.018, 0.022, 0.03), "Skin", subdiv=1)
    for sx in (-1, 1):
        body.sphere((sx * 0.088, 0.0, 1.635), (0.014, 0.025, 0.03), "Skin", subdiv=1)
    if variant == "skirt":
        body.cyl((0, 0, hip_z - 0.2), 0.22, 0.4, "Pants", segs=18, r2=0.16)
        body.sphere((0, -0.02, 1.66), (0.1, 0.11, 0.12), "Hair", subdiv=2)
        body.sphere((0, -0.075, 1.53), (0.085, 0.05, 0.12), "Hair", subdiv=2)
    elif variant == "dress":
        body.cyl((0, 0, hip_z - 0.12), 0.24, 0.52, "Shirt", segs=18, r2=0.17)
        body.sphere((0, -0.02, 1.665), (0.1, 0.112, 0.12), "Hair", subdiv=2)
        body.sphere((0, -0.1, 1.68), (0.05, 0.05, 0.05), "Hair", subdiv=2)
        body.box((0.16, 0.02, 1.0), (0.05, 0.24, 0.2), "Accent")
        body.tube([(0.16, 0.0, 1.11), (0.14, 0.0, 1.4)], 0.008, "Accent", segs=6)
    elif variant == "coat":
        body.cyl((0, 0, hip_z - 0.08), 0.23, 0.42, "Shirt", segs=18, r2=0.205)
        body.sphere((0, -0.01, 1.68), (0.095, 0.105, 0.075), "Hair", subdiv=2)
        body.cyl((0, 0, 1.465), 0.085, 0.08, "Accent", segs=14)
    else:  # casual with backpack
        body.sphere((0, -0.015, 1.665), (0.096, 0.108, 0.095), "Hair", subdiv=2)
        body.box((0, -0.2, 1.2), (0.28, 0.14, 0.36), "Accent")
        for sx in (-1, 1):
            body.box((sx * 0.12, 0.0, 1.32), (0.04, 0.24, 0.025), "Accent", rot=(0.5, 0, 0))
    body.finish()

    leg_mat = "Skin" if variant == "skirt" else ("Pants" if variant != "dress" else "Skin")
    for sx, nm in ((-1, "LegL"), (1, "LegR")):
        leg = Part(nm, origin=(sx * 0.095, 0, hip_z))
        limb(leg, (0, 0, 0), 0.45, 0.075, 0.058, leg_mat)
        limb(leg, (0, 0, -0.45), 0.42, 0.056, 0.04, leg_mat)
        leg.box((0, 0.05, -0.885), (0.1, 0.26, 0.085), "Shoes")
        leg.sphere((0, 0.15, -0.89), (0.05, 0.05, 0.04), "Shoes", subdiv=1)
        leg.finish()
    sleeve = "Shirt"
    for sx, nm in ((-1, "ArmL"), (1, "ArmR")):
        arm = Part(nm, origin=(sx * (sw + 0.035), 0, shoulder_z))
        arm.sphere((0, 0, 0), (0.055, 0.055, 0.055), sleeve, subdiv=2)
        limb(arm, (0, 0, 0), 0.29, 0.048, 0.04, sleeve)
        limb(arm, (0, 0, -0.29), 0.25, 0.038, 0.03, "Skin" if variant in ("skirt", "dress") else sleeve)
        arm.sphere((0, 0.005, -0.6), (0.032, 0.045, 0.06), "Skin", subdiv=2)
        arm.finish()
    bpy.data.objects.remove(bpy.data.objects["Torso"], do_unlink=True) if "Torso" in bpy.data.objects else None
    export(name)


# ---------------------------------------------------------------- trees

def tree(name, style, seed):
    reset()
    rnd = random.Random(seed)
    p = Part("Tree")
    leaves = ["LeafA", "LeafB", "LeafC"]
    if style == "plane":  # London plane: mottled trunk, broad rounded crown
        h, crown_r, clusters, spread = 3.0, 3.4, 18, 2.6
    elif style == "locust":  # Honey locust: lighter, airier canopy
        h, crown_r, clusters, spread = 2.7, 2.8, 22, 2.4
    else:  # park oak: big and spreading
        h, crown_r, clusters, spread = 3.4, 4.6, 26, 3.6
    lean = Vector((rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), 1)).normalized()
    top = lean * h
    p.cyl(top / 2, 0.24 if style != "oak" else 0.36, h, "Bark", segs=12, r2=0.17 if style != "oak" else 0.25,
          rot=lean.to_track_quat("Z", "Y").to_euler()[:])
    p.cyl((0, 0, 0.12), 0.3 if style != "oak" else 0.42, 0.25, "Bark", segs=12, r2=0.2)
    tips = []
    branches = 4 if style != "oak" else 6
    for b in range(branches):
        a = b * 2 * math.pi / branches + rnd.uniform(-0.4, 0.4)
        up = rnd.uniform(0.7, 1.3)
        length = rnd.uniform(1.6, 2.3) * (1.4 if style == "oak" else 1.0)
        mid = top + Vector((math.cos(a) * length * 0.45, math.sin(a) * length * 0.45, up * 0.8))
        tip = top + Vector((math.cos(a) * length, math.sin(a) * length, up * 1.6))
        p.tube([top, mid, tip], 0.07 if style != "oak" else 0.1, "Bark", segs=7)
        tips.append(tip)
    center = top + Vector((0, 0, crown_r * 0.55))
    for i in range(clusters):
        if i < len(tips):
            c = tips[i] + Vector((0, 0, 0.3))
        else:
            d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.45, 0.85)))
            if d.length > 1:
                d.normalize()
            c = center + Vector((d.x * spread, d.y * spread, d.z * crown_r * 0.6))
        r = rnd.uniform(0.9, 1.45) * (1.25 if style == "oak" else 1.0) * (0.75 if style == "locust" else 1.0)
        p.sphere(c, (r, r, r * 0.8), leaves[rnd.randrange(3)], subdiv=2, wobble=0.35, seed=rnd.uniform(0, 100))
    p.finish()
    export(name)


def shrub():
    reset()
    p = Part("Shrub")
    rnd = random.Random(7)
    for i in range(5):
        a = i * 1.3
        p.sphere((math.cos(a) * 0.35, math.sin(a) * 0.35, 0.4 + rnd.uniform(0, 0.15)),
                 (0.5, 0.5, 0.42), ["LeafA", "LeafB"][i % 2], subdiv=2, wobble=0.3, seed=i * 3.1)
    p.finish()
    export("Shrub")


# ---------------------------------------------------------------- street furniture

def street_lamp():
    """NYC-style octagonal pole with a curved mast arm and cobra-head luminaire, reaching +Y."""
    reset()
    p = Part("Lamp")
    p.bevel = (0.015, 2)
    p.cyl((0, 0, 0.45), 0.24, 0.9, "PoleSilver", segs=8, r2=0.18)
    p.cyl((0, 0, 0.95), 0.2, 0.1, "PoleSilver", segs=8)
    p.cyl((0, 0, 4.9), 0.13, 7.9, "PoleSilver", segs=8, r2=0.075)
    pts = []
    for i in range(9):
        t = i / 8
        a = t * math.pi * 0.42
        pts.append((0, math.sin(a) * 1.3 + t * 0.9, 8.6 + (1 - math.cos(a)) * -0.1 + math.sin(t * math.pi) * 0.35))
    p.tube([(0, 0, 8.7)] + pts, 0.055, "PoleSilver", segs=8)
    head = Vector(pts[-1]) + Vector((0, 0.45, -0.05))
    p.sphere(head, (0.26, 0.6, 0.15), "LampHead", subdiv=3)
    p.sphere(head + Vector((0, 0.05, -0.08)), (0.2, 0.45, 0.06), "LampLens", subdiv=3)
    p.finish()
    export("StreetLamp")


def park_lamp():
    reset()
    p = Part("ParkLamp")
    p.bevel = (0.01, 2)
    p.cyl((0, 0, 0.3), 0.16, 0.6, "Iron", segs=12, r2=0.1)
    for k in range(8):
        a = k * math.pi / 4
        p.box((math.cos(a) * 0.075, math.sin(a) * 0.075, 2.0), (0.03, 0.03, 2.8), "Iron", rot=(0, 0, a))
    p.cyl((0, 0, 2.0), 0.07, 2.8, "Iron", segs=10)
    p.cyl((0, 0, 3.45), 0.12, 0.1, "Iron", segs=12)
    p.cyl((0, 0, 3.75), 0.2, 0.5, "LampLens", segs=12, r2=0.15)
    p.cyl((0, 0, 4.05), 0.26, 0.12, "Iron", segs=12, r2=0.08)
    p.cyl((0, 0, 4.2), 0.02, 0.2, "Iron", segs=6)
    p.finish()
    export("ParkLamp")


def signal_head():
    """Three-section vehicle signal with visors and a backplate; lenses face +Y."""
    reset()
    p = Part("Signal")
    p.bevel = (0.012, 2)
    p.box((0, 0, 0), (0.34, 0.26, 1.02), "SignalBody")
    p.box((0, -0.15, 0), (0.56, 0.02, 1.25), "SignalBody")
    for z, lens in ((0.33, "LensRed"), (0.0, "LensAmber"), (-0.33, "LensGreen")):
        p.cyl((0, 0.135, z), 0.105, 0.02, lens, segs=20, rot=(math.pi / 2, 0, 0))
        p.cyl((0, 0.22, z + 0.02), 0.125, 0.18, "SignalBody", segs=16, rot=(math.pi / 2, 0, 0), caps=False)
    p.finish()
    export("SignalHead")


def ped_head():
    reset()
    p = Part("PedSignal")
    p.bevel = (0.01, 2)
    p.box((0, 0, 0), (0.34, 0.22, 0.4), "SignalBody")
    p.box((0, 0.112, 0.0), (0.26, 0.01, 0.3), "LensStop")
    p.box((0, 0.116, 0.0), (0.12, 0.01, 0.16), "LensWalk")
    p.box((0, 0.2, 0.19), (0.34, 0.18, 0.02), "SignalBody")
    p.finish()
    export("PedSignalHead")


def signal_pole():
    reset()
    p = Part("SignalPole")
    p.bevel = (0.01, 2)
    p.cyl((0, 0, 0.3), 0.18, 0.6, "PoleSilver", segs=10, r2=0.13)
    p.cyl((0, 0, 2.6), 0.1, 4.6, "PoleSilver", segs=10, r2=0.08)
    p.cyl((0, 0, 4.95), 0.11, 0.1, "PoleSilver", segs=10)
    p.box((0, 0.0, 2.6), (0.2, 0.12, 0.3), "SignalBody")
    p.finish()
    export("SignalPole")


def hydrant():
    reset()
    p = Part("Hydrant")
    p.bevel = (0.008, 2)
    p.cyl((0, 0, 0.04), 0.17, 0.08, "HydrantBody", segs=16)
    p.cyl((0, 0, 0.32), 0.12, 0.48, "HydrantBody", segs=16, r2=0.11)
    p.cyl((0, 0, 0.58), 0.145, 0.05, "HydrantBody", segs=16)
    p.sphere((0, 0, 0.62), (0.125, 0.125, 0.09), "HydrantCap", subdiv=3)
    p.cyl((0, 0, 0.73), 0.035, 0.06, "HydrantCap", segs=5)
    for sx in (-1, 1):
        p.cyl((sx * 0.14, 0, 0.42), 0.045, 0.08, "HydrantBody", segs=12, rot=(0, math.pi / 2, 0))
        p.cyl((sx * 0.19, 0, 0.42), 0.05, 0.03, "HydrantCap", segs=6, rot=(0, math.pi / 2, 0))
    p.cyl((0, 0.15, 0.38), 0.065, 0.1, "HydrantBody", segs=12, rot=(math.pi / 2, 0, 0))
    p.cyl((0, 0.21, 0.38), 0.07, 0.035, "HydrantCap", segs=6, rot=(math.pi / 2, 0, 0))
    p.finish()
    export("Hydrant")


def mailbox():
    reset()
    p = Part("Mailbox")
    p.bevel = (0.015, 2)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * 0.2, sy * 0.2, 0.15), (0.05, 0.05, 0.3), "MailBlue")
    p.box((0, 0, 0.65), (0.5, 0.52, 0.72), "MailBlue")
    p.cyl((0, 0, 1.01), 0.26, 0.5, "MailBlue", segs=20, rot=(0, math.pi / 2, 0))
    p.box((0, 0.265, 1.02), (0.32, 0.05, 0.12), "Iron", rot=(-0.4, 0, 0))
    p.box((0, 0.262, 0.6), (0.32, 0.01, 0.18), "MapPanel")
    p.finish()
    export("Mailbox")


def bench():
    """7.5 ft park bench with slatted seat and back and cast-iron ends; the seat faces +Y."""
    reset()
    p = Part("Bench")
    p.bevel = (0.01, 2)
    L = 2.28
    for i in range(4):
        p.box((0, 0.18 - i * 0.12, 0.45), (L, 0.09, 0.035), "Wood")
    for i in range(3):
        p.box((0, -0.25 - i * 0.02, 0.6 + i * 0.13), (L, 0.035, 0.09), "Wood", rot=(-0.2, 0, 0))
    for sx in (-1, 0, 1):
        x = sx * (L / 2 - 0.12) if sx else 0
        p.box((x, 0.12, 0.22), (0.05, 0.05, 0.44), "Iron")
        p.box((x, -0.2, 0.4), (0.05, 0.05, 0.8), "Iron", rot=(-0.15, 0, 0))
        p.box((x, -0.03, 0.42), (0.05, 0.45, 0.05), "Iron")
        if sx:
            p.box((x, 0.0, 0.66), (0.05, 0.4, 0.04), "Iron", rot=(0.1, 0, 0))
    p.finish()
    export("Bench")


def litter_basket():
    reset()
    p = Part("Basket")
    for k in range(18):
        a = k * 2 * math.pi / 18
        p.box((math.cos(a) * 0.27, math.sin(a) * 0.27, 0.45), (0.012, 0.012, 0.86), "BasketGreen")
    for z in (0.04, 0.3, 0.6, 0.88):
        p.torus((0, 0, z), 0.27, 0.012, "BasketGreen", segs=24, tube=6)
    p.cyl((0, 0, 0.03), 0.26, 0.04, "BasketGreen", segs=18)
    p.cyl((0, 0, 0.5), 0.25, 0.75, "BagBlack", segs=14, r2=0.26)
    p.finish()
    export("LitterBasket")


def bike_rack():
    reset()
    p = Part("BikeRack")
    p.torus((0, 0, 0.47), 0.42, 0.028, "Iron", segs=28, tube=8, rot=(0, math.pi / 2, 0))
    p.box((0, 0, 0.03), (0.08, 0.6, 0.06), "Iron")
    p.finish()
    export("BikeRack")


def trash_bags():
    reset()
    p = Part("Bags")
    rnd = random.Random(11)
    for i in range(7):
        c = (rnd.uniform(-0.6, 0.6), rnd.uniform(-0.25, 0.25), 0.32 + (0.3 if i == 6 else 0))
        r = rnd.uniform(0.28, 0.38)
        p.sphere(c, (r, r * 0.9, r * 0.85), "BagBlack" if i % 3 else "BagWhite", subdiv=2, wobble=0.22, seed=i * 2.7)
        p.cyl((c[0], c[1], c[2] + r * 0.8), 0.04, 0.12, "BagBlack" if i % 3 else "BagWhite", segs=6, r2=0.015)
    p.finish()
    export("TrashBags")


def ac_unit():
    """Window air conditioner seen from the street; the grille faces +Y."""
    reset()
    p = Part("AC")
    p.bevel = (0.012, 2)
    p.box((0, 0.2, 0), (0.62, 0.42, 0.4), "AcWhite")
    for i in range(7):
        p.box((0, 0.415, -0.12 + i * 0.04), (0.5, 0.01, 0.015), "Iron")
    p.box((0, 0.2, -0.24), (0.66, 0.36, 0.04), "Iron")
    p.finish()
    export("ACUnit")


def hvac():
    reset()
    p = Part("HVAC")
    p.bevel = (0.02, 2)
    p.box((0, 0, 0.6), (2.2, 1.4, 1.2), "Galvanized")
    for x in (-0.55, 0.55):
        p.cyl((x, 0, 1.22), 0.42, 0.06, "Iron", segs=20)
        p.cyl((x, 0, 1.26), 0.38, 0.02, "Galvanized", segs=20)
    p.box((-1.3, 0.2, 0.3), (0.4, 0.35, 0.35), "Galvanized")
    p.box((0, 0.71, 0.5), (1.8, 0.02, 0.6), "Iron")
    p.finish()
    export("HVAC")


def water_tower():
    reset()
    p = Part("WaterTower")
    p.bevel = (0.01, 2)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * 1.15, sy * 1.15, 1.6), (0.14, 0.14, 3.2), "Iron")
    for z in (1.0, 2.3):
        for sx in (-1, 1):
            p.box((sx * 1.15, 0, z), (0.06, 2.3, 0.06), "Iron")
            p.box((0, sx * 1.15, z), (2.3, 0.06, 0.06), "Iron")
    p.box((0, 0, 3.25), (3.3, 3.3, 0.14), "Iron")
    staves = 32
    for k in range(staves):
        a = k * 2 * math.pi / staves
        p.box((math.cos(a) * 1.55, math.sin(a) * 1.55, 5.0), (0.07, 0.31, 3.4), "TankWood", rot=(0, 0, a))
    p.cyl((0, 0, 5.0), 1.52, 3.4, "TankWood", segs=32)
    for z in (3.6, 4.4, 5.2, 6.0, 6.6):
        p.torus((0, 0, z), 1.6, 0.03, "Iron", segs=36, tube=6)
    p.cyl((0, 0, 7.25), 1.72, 1.1, "TankWood", segs=32, r2=0.08)
    p.cyl((0, 0, 7.9), 0.1, 0.3, "Iron", segs=8)
    p.sphere((0, 0, 8.1), (0.12, 0.12, 0.14), "Iron", subdiv=2)
    for side in (-0.18, 0.18):
        p.box((1.62 + side * 0.1, side, 4.9), (0.03, 0.03, 3.4), "Iron")
    for z in [3.4 + i * 0.3 for i in range(11)]:
        p.box((1.63, 0, z), (0.03, 0.36, 0.025), "Iron")
    p.finish()
    export("WaterTower")


def bus_shelter():
    reset()
    p = Part("Shelter")
    p.bevel = (0.012, 2)
    L, D, H = 4.2, 1.5, 2.6
    for x in (-L / 2, L / 2):
        for y in (-D / 2, D / 2):
            p.box((x, y, H / 2), (0.07, 0.07, H), "PoleSilver")
    p.box((0, 0, H + 0.05), (L + 0.3, D + 0.3, 0.1), "PoleSilver")
    p.box((0, -D / 2, 1.3), (L, 0.02, 2.0), "GlassPanel")
    for x in (-L / 2, L / 2):
        p.box((x, 0, 1.3), (0.02, D, 2.0), "GlassPanel")
    p.box((L / 2 - 0.02, 0.1, 1.3), (0.04, 1.1, 1.8), "MapPanel")
    p.box((-0.5, -0.45, 0.48), (2.4, 0.35, 0.05), "PoleSilver")
    p.finish()
    export("BusShelter")


def newsstand():
    reset()
    p = Part("Newsstand")
    p.bevel = (0.02, 2)
    p.box((0, 0, 1.25), (3.0, 1.8, 2.5), "KioskGreen")
    p.box((0, 0.91, 1.25), (2.5, 0.02, 1.0), "GlassPanel")
    p.box((0, 1.25, 2.35), (3.1, 0.8, 0.06), "KioskGreen", rot=(0.25, 0, 0))
    p.box((0, 0.95, 0.65), (2.6, 0.3, 0.06), "Wood")
    p.box((0, 0, 2.6), (3.15, 1.95, 0.12), "Iron")
    p.finish()
    export("Newsstand")


def preview_sheet():
    pass


BUILDERS = [
    lambda: car("Sedan", "sedan"), lambda: car("SUV", "suv"), lambda: car("Van", "van"),
    lambda: car("TaxiSedan", "sedan", taxi=True), lambda: car("TaxiSUV", "suv", taxi=True),
    box_truck, bus,
    lambda: person("PersonCasual", "casual"), lambda: person("PersonSkirt", "skirt"),
    lambda: person("PersonCoat", "coat"), lambda: person("PersonDress", "dress"),
    lambda: tree("TreePlane", "plane", 3), lambda: tree("TreeLocust", "locust", 8), lambda: tree("TreeOak", "oak", 21),
    shrub, street_lamp, park_lamp, signal_head, ped_head, signal_pole, hydrant, mailbox, bench, litter_basket,
    bike_rack, trash_bags, ac_unit, hvac, water_tower, bus_shelter, newsstand,
]

if __name__ == "__main__":
    for build in BUILDERS:
        build()
        if PREVIEW:
            pass
    print("CITIVIBE_MODELS_DONE", len(BUILDERS))
