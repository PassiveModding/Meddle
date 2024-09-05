bl_info = {
    "name": "Meddle Utils",
    "blender": (4, 0, 0),
    "category": "3D View",
}

import bpy
from bpy.types import Operator, Panel, ShaderNodeBsdfPrincipled
from bpy.props import StringProperty
import os

class VIEW3D_PT_update_meddle_shaders(Panel):
    bl_label = "Meddle Utils"
    bl_idname = "VIEW3D_PT_update_meddle_shaders"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Meddle'

    def draw(self, context):
        layout = self.layout

        row = layout.row()
        row.operator("meddle.fix_ior", text="Fix IOR")

        row = layout.row()
        row.operator("meddle.fix_terrain", text="Fix Terrain")



class MEDDLE_OT_fix_ior(Operator):
    """Sets the IOR value in the Principled BSDF node of all materials"""
    bl_idname = "meddle.fix_ior"
    bl_label = "Update Shaders"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        # Iterate all materials in the scene
        for mat in bpy.data.materials:
            # Check if the material uses nodes
            if not mat.use_nodes:
                continue
            
            # Look for the Principled BSDF node
            principled_bsdf = None
            for node in mat.node_tree.nodes:
                if node.type == 'BSDF_PRINCIPLED':
                    principled_bsdf = node
                    break
            
            if not principled_bsdf:
                continue
            
            # Check if the material has the custom property g_GlassIOR
            if "g_GlassIOR" in mat:
                ior_value = mat["g_GlassIOR"]
                # get first value of the IOR
                print(f"Found custom property 'g_GlassIOR' in material '{mat.name}' with value {ior_value[0]}.")
                # Set the IOR value in the Principled BSDF node
                principled_bsdf.inputs['IOR'].default_value = ior_value[0]
            else:
                print(f"Material '{mat.name}' does not have the custom property 'g_GlassIOR'.")

        return {'FINISHED'}
    
class MEDDLE_OT_fix_terrain(Operator):
    """Looks up the g_SamplerXXXMap1 values on bg materials and creates the relevant texture nodes"""
    bl_idname = "meddle.fix_terrain"
    bl_label = "Fix Terrain"
    bl_options = {'REGISTER', 'UNDO'}

    directory: StringProperty(subtype='DIR_PATH')

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}

    def execute(self, context):
        context.scene.selected_folder = self.directory
        print(f"Folder selected: {self.directory}")

        # Iterate all materials in the scene
        for mat in bpy.data.materials:
            # Check if the material uses nodes
            if not mat.use_nodes:
                continue

            if "ShaderPackage" not in mat:
                continue

            if mat["ShaderPackage"] != "bg.shpk":
                continue
            
            # Look for the Principled BSDF node
            principled_bsdf = None
            for node in mat.node_tree.nodes:
                if node.type == 'BSDF_PRINCIPLED':
                    principled_bsdf = node
                    break
            
            if not principled_bsdf:
                continue

            # set IOR to 1.0
            principled_bsdf.inputs['IOR'].default_value = 1.0
            
            # look for g_SamplerColorMap1, g_SamplerNormalMap1, g_SamplerSpecularMap1
            g_SamplerColorMap1 = None
            if "g_SamplerColorMap1" in mat:
                g_SamplerColorMap1 = mat["g_SamplerColorMap1"]
                print(f"Found custom property 'g_SamplerColorMap1' in material '{mat.name}' with value {g_SamplerColorMap1}.")
            else:
                print(f"Material '{mat.name}' does not have the custom property 'g_SamplerColorMap1'.")

            g_SamplerColorMap0Node = None
            for node in mat.node_tree.nodes:
                if node.label == "BASE COLOR":
                    g_SamplerColorMap0Node = node
                    break

            g_SamplerNormalMap1 = None
            if "g_SamplerNormalMap1" in mat:
                g_SamplerNormalMap1 = mat["g_SamplerNormalMap1"]
                print(f"Found custom property 'g_SamplerNormalMap1' in material '{mat.name}' with value {g_SamplerNormalMap1}.")
            else:
                print(f"Material '{mat.name}' does not have the custom property 'g_SamplerNormalMap1'.")

            g_SamplerNormalMap0Node = None
            for node in mat.node_tree.nodes:
                if node.label == "NORMAL MAP":
                    g_SamplerNormalMap0Node = node
                    break

            normal_tangent = None
            for node in mat.node_tree.nodes:
                if node.name == "Normal Map":
                    normal_tangent = node
                    break

            # specular_map = None
            #if "g_SamplerSpecularMap1" in mat:
            #    specular_map = mat["g_SamplerSpecularMap1"]
            #    print(f"Found custom property 'g_SamplerSpecularMap1' in material '{mat.name}' with value {specular_map}.")

            # get vertex color node
            vertex_color_node = None
            for node in mat.node_tree.nodes:
                if node.type == 'VERTEX_COLOR':
                    vertex_color_node = node
                    break

            if vertex_color_node is None:
                continue

            try:
                if g_SamplerColorMap1 is not None and g_SamplerColorMap0Node is not None:
                    mix_color = None
                    for node in mat.node_tree.nodes:
                        if node.label == "MIX COLOR":
                            mix_color = node
                            break
                    if mix_color is None:
                        mix_color = mat.node_tree.nodes.new('ShaderNodeMixRGB')
                        mix_color.label = "MIX COLOR"

                    if "dummy_" in g_SamplerColorMap1:
                        mix_color.blend_type = 'MULTIPLY'
                    else:
                        mix_color.blend_type = 'MIX'
                    mix_color.inputs['Fac'].default_value = 1.0
                    mat.node_tree.links.new(mix_color.outputs['Color'], principled_bsdf.inputs['Base Color'])
                    mat.node_tree.links.new(vertex_color_node.outputs['Alpha'], mix_color.inputs['Fac'])

                    # load color texture using the selected folder + color_map + ".png"
                    g_SamplerColorMap1Node = None
                    for node in mat.node_tree.nodes:
                        if node.label == "BASE COLOR 1":
                            g_SamplerColorMap1Node = node
                            break
                    
                    if g_SamplerColorMap1Node is None:
                        g_SamplerColorMap1Node = mat.node_tree.nodes.new('ShaderNodeTexImage')
                        g_SamplerColorMap1Node.label = "BASE COLOR 1"

                    g_SamplerColorMap1Node.image = bpy.data.images.load(self.directory + g_SamplerColorMap1 + ".png")
                    mat.node_tree.links.new(g_SamplerColorMap1Node.outputs['Color'], mix_color.inputs['Color1'])

                    # use base_color
                    mat.node_tree.links.new(g_SamplerColorMap0Node.outputs['Color'], mix_color.inputs['Color2'])

                    # organize nodes
                    g_SamplerColorMap1Node.location = (g_SamplerColorMap0Node.location.x, g_SamplerColorMap0Node.location.y - 150)
                    mix_color.location = (g_SamplerColorMap0Node.location.x + 300, g_SamplerColorMap0Node.location.y)

                if g_SamplerNormalMap1 is not None and g_SamplerNormalMap0Node is not None and normal_tangent is not None:
                    mix_normal = None
                    for node in mat.node_tree.nodes:
                        if node.label == "MIX NORMAL":
                            mix_normal = node
                            break
                    if mix_normal is None:
                        mix_normal = mat.node_tree.nodes.new('ShaderNodeMixRGB')
                        mix_normal.label = "MIX NORMAL"

                    if "dummy_" in g_SamplerNormalMap1:
                        mix_normal.blend_type = 'MULTIPLY'
                    else:
                        mix_normal.blend_type = 'MIX'
                    mix_normal.inputs['Fac'].default_value = 1.0
                    mat.node_tree.links.new(mix_normal.outputs['Color'], normal_tangent.inputs['Color'])
                    mat.node_tree.links.new(vertex_color_node.outputs['Alpha'], mix_normal.inputs['Fac'])

                    # load normal texture using the selected folder + normal_map + ".png"
                    gSamplerNormalMap1Node = None
                    for node in mat.node_tree.nodes:
                        if node.label == "NORMAL MAP 1":
                            gSamplerNormalMap1Node = node
                            break

                    if gSamplerNormalMap1Node is None:
                        gSamplerNormalMap1Node = mat.node_tree.nodes.new('ShaderNodeTexImage')
                        gSamplerNormalMap1Node.label = "NORMAL MAP 1"
                    gSamplerNormalMap1Node.image = bpy.data.images.load(self.directory + g_SamplerNormalMap1 + ".png")
                    mat.node_tree.links.new(gSamplerNormalMap1Node.outputs['Color'], mix_normal.inputs['Color1'])

                    # use base_normal
                    mat.node_tree.links.new(g_SamplerNormalMap0Node.outputs['Color'], mix_normal.inputs['Color2'])

                    # organize nodes
                    gSamplerNormalMap1Node.location = (g_SamplerNormalMap0Node.location.x, g_SamplerNormalMap0Node.location.y - 150)
                    mix_normal.location = (g_SamplerNormalMap0Node.location.x + 300, g_SamplerNormalMap0Node.location.y)
                    normal_tangent.location = (g_SamplerNormalMap0Node.location.x + 600, g_SamplerNormalMap0Node.location.y)
            except Exception as e:
                print(f"Error: {e}")
                continue           


        return {'FINISHED'}
    
def register():
    bpy.utils.register_class(VIEW3D_PT_update_meddle_shaders)
    bpy.utils.register_class(MEDDLE_OT_fix_ior)
    bpy.utils.register_class(MEDDLE_OT_fix_terrain)
    bpy.types.Scene.selected_folder = StringProperty(name="Selected Folder", description="Path to the selected folder")

def unregister():
    bpy.utils.unregister_class(VIEW3D_PT_update_meddle_shaders)
    bpy.utils.unregister_class(MEDDLE_OT_fix_ior)
    bpy.utils.unregister_class(MEDDLE_OT_fix_terrain)

if __name__ == "__main__":
    register()