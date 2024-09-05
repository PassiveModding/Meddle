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
            color_map = None
            if "g_SamplerColorMap1" in mat:
                color_map = mat["g_SamplerColorMap1"]
                print(f"Found custom property 'g_SamplerColorMap1' in material '{mat.name}' with value {color_map}.")
            else:
                print(f"Material '{mat.name}' does not have the custom property 'g_SamplerColorMap1'.")

            base_color = None
            for node in mat.node_tree.nodes:
                if node.label == "BASE COLOR":
                    base_color = node
                    break

            normal_map = None
            if "g_SamplerNormalMap1" in mat:
                normal_map = mat["g_SamplerNormalMap1"]
                print(f"Found custom property 'g_SamplerNormalMap1' in material '{mat.name}' with value {normal_map}.")
            else:
                print(f"Material '{mat.name}' does not have the custom property 'g_SamplerNormalMap1'.")

            base_normal = None
            for node in mat.node_tree.nodes:
                if node.label == "NORMAL MAP":
                    base_normal = node
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
                if color_map is not None and base_color is not None:
                    mix_color = mat.node_tree.nodes.new('ShaderNodeMixRGB')
                    if "dummy_" in color_map:
                        mix_color.blend_type = 'MULTIPLY'
                    else:
                        mix_color.blend_type = 'MIX'
                    mix_color.inputs['Fac'].default_value = 1.0
                    mat.node_tree.links.new(mix_color.outputs['Color'], principled_bsdf.inputs['Base Color'])
                    mat.node_tree.links.new(vertex_color_node.outputs['Alpha'], mix_color.inputs['Fac'])

                    # load color texture using the selected folder + color_map + ".png"
                    color_texture = mat.node_tree.nodes.new('ShaderNodeTexImage')
                    color_texture.image = bpy.data.images.load(self.directory + color_map + ".png")
                    mat.node_tree.links.new(color_texture.outputs['Color'], mix_color.inputs['Color2'])

                    # use base_color
                    mat.node_tree.links.new(base_color.outputs['Color'], mix_color.inputs['Color1'])

                    # organize nodes
                    color_texture.location = (base_color.location.x, base_color.location.y - 150)
                    mix_color.location = (base_color.location.x + 300, base_color.location.y)

                if normal_map is not None and base_normal is not None and normal_tangent is not None:
                    mix_normal = mat.node_tree.nodes.new('ShaderNodeMixRGB')
                    if "dummy_" in normal_map:
                        mix_normal.blend_type = 'MULTIPLY'
                    else:
                        mix_normal.blend_type = 'MIX'
                    mix_normal.inputs['Fac'].default_value = 1.0
                    mat.node_tree.links.new(mix_normal.outputs['Color'], normal_tangent.inputs['Color'])
                    mat.node_tree.links.new(vertex_color_node.outputs['Alpha'], mix_normal.inputs['Fac'])

                    # load normal texture using the selected folder + normal_map + ".png"
                    normal_texture = mat.node_tree.nodes.new('ShaderNodeTexImage')
                    normal_texture.image = bpy.data.images.load(self.directory + normal_map + ".png")
                    mat.node_tree.links.new(normal_texture.outputs['Color'], mix_normal.inputs['Color2'])

                    # use base_normal
                    mat.node_tree.links.new(base_normal.outputs['Color'], mix_normal.inputs['Color1'])

                    # organize nodes
                    normal_texture.location = (base_normal.location.x, base_normal.location.y - 150)
                    mix_normal.location = (base_normal.location.x + 300, base_normal.location.y)
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