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

        #row = layout.row()
        #row.operator("meddle.fix_ior", text="Fix ior")

        row = layout.row()
        row.operator("meddle.fix_bg", text="Fix bg.shpk")

        row = layout.row()
        row.operator("meddle.stain_housing", text="Fix bgcolorchange.shpk")

        row = layout.row()
        row.operator("meddle.connect_volume", text="Fix skin.shpk/iris.shpk")

class MEDDLE_OT_connect_volume(Operator):
    """Connects the volume output to the material output for skin.shpk and iris.shpk"""
    bl_idname = "meddle.connect_volume"
    bl_label = "Connect Volume"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        # Iterate all materials in the scene
        for mat in bpy.data.materials:
            # Check if the material uses nodes
            if not mat.use_nodes:
                continue

            if "ShaderPackage" not in mat:
                continue

            if mat["ShaderPackage"] != "skin.shpk" and mat["ShaderPackage"] != "iris.shpk":
                continue
            
            # Look for the Principled BSDF node
            principled_bsdf = None
            for node in mat.node_tree.nodes:
                if node.type == 'BSDF_PRINCIPLED':
                    principled_bsdf = node
                    break
            
            if not principled_bsdf:
                print(f"Material '{mat.name}' does not have a Principled BSDF node.")
                continue
            
            material_output = None
            for node in mat.node_tree.nodes:
                if node.type == 'OUTPUT_MATERIAL':
                    material_output = node
                    break

            if not material_output:
                print(f"Material '{mat.name}' does not have a Material Output node.")
                continue

            mat.node_tree.links.new(principled_bsdf.outputs['BSDF'], material_output.inputs['Volume'])

        return {'FINISHED'}

class MEDDLE_OT_fix_ior(Operator):
    """Sets the IOR value in the Principled BSDF node of all materials to the value of the custom property g_GlassIOR or 1.0 if not found"""
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
                print(f"Material '{mat.name}' does not have the custom property 'g_GlassIOR'. Setting IOR to 1.0.")
                # Set the IOR value in the Principled BSDF node to 1.0
                principled_bsdf.inputs['IOR'].default_value = 1.0

        return {'FINISHED'}
    
class MEDDLE_OT_stain_housing(Operator):
    """Applies stain colors to housing materials"""
    bl_idname = "meddle.stain_housing"
    bl_label = "Stain Housing"
    bl_options = {'REGISTER', 'UNDO'}

    def discardNormalBlueChannel(self, mat):
        principled_bsdf = self.getBsdfPrincipled(mat)

        # get the node connected to the bsdf "Normal" input
        normal_map = None
        for node in mat.node_tree.nodes:
            if node.label == 'NORMAL MAP':
                normal_map = node
                break

        if normal_map is None:
            return

        # create node to remove the blue channel
        separate_rgb = None
        for node in mat.node_tree.nodes:
            if node.type == 'SEPARATE_COLOR' and node.name == "Separate Normal Map":
                separate_rgb = node
                break

        if separate_rgb is None:
            separate_rgb = mat.node_tree.nodes.new('ShaderNodeSeparateColor')
            separate_rgb.location = (normal_map.location.x + 300, normal_map.location.y)
            separate_rgb.name = "Separate Normal Map"

        mat.node_tree.links.new(normal_map.outputs['Color'], separate_rgb.inputs['Color'])

        # create node to add the blue channel
        combine_rgb = None
        for node in mat.node_tree.nodes:
            if node.type == 'COMBINE_COLOR' and node.name == "Combine Normal Map":
                combine_rgb = node
                break

        if combine_rgb is None:
            combine_rgb = mat.node_tree.nodes.new('ShaderNodeCombineColor')
            combine_rgb.location = (separate_rgb.location.x + 300, separate_rgb.location.y)
            combine_rgb.name = "Combine Normal Map"
            
        combine_rgb.inputs['Blue'].default_value = 1.0
        mat.node_tree.links.new(separate_rgb.outputs['Red'], combine_rgb.inputs['Red'])
        mat.node_tree.links.new(separate_rgb.outputs['Green'], combine_rgb.inputs['Green'])
        mat.node_tree.links.new(combine_rgb.outputs['Color'], principled_bsdf.inputs['Normal'])

        normal_map_node = None
        for node in mat.node_tree.nodes:
            if node.type == 'NORMAL_MAP':
                normal_map_node = node
                break

        if normal_map_node is None:
            return

        mat.node_tree.links.new(combine_rgb.outputs['Color'], normal_map_node.inputs['Color'])
        mat.node_tree.links.new(normal_map_node.outputs['Normal'], principled_bsdf.inputs['Normal'])

        # organize nodes
        combine_rgb.location = (normal_map.location.x, normal_map.location.y - 150)
        separate_rgb.location = (normal_map.location.x + 300, normal_map.location.y)
        normal_map_node.location = (normal_map.location.x + 600, normal_map.location.y)



    def getBsdfPrincipled(self, mat):
        # Look for the Principled BSDF node
        principled_bsdf = None
        for node in mat.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED':
                principled_bsdf = node
                break
        
        return principled_bsdf

    def mixColor(self, mat):
        principled_bsdf = self.getBsdfPrincipled(mat)
        
        # if DiffuseColor is not found, then skip
        if "DiffuseColor" not in mat:
            return

        diffuse_color = mat["DiffuseColor"]
        print(f"Found custom property 'DiffuseColor' in material '{mat.name}' with value {diffuse_color}.")

        # create a new RGB node
        rgb_node = None
        for node in mat.node_tree.nodes:
            if node.type == 'RGB':
                rgb_node = node
                break

        if rgb_node is None:
            rgb_node = mat.node_tree.nodes.new('ShaderNodeRGB')
            rgb_node.location = (-300, 0)

        rgb_node.outputs['Color'].default_value = diffuse_color

        power_node = None
        for node in mat.node_tree.nodes:
            if node.label == "POWER":
                power_node = node
                break

        if power_node is None:
            power_node = mat.node_tree.nodes.new('ShaderNodeMath')
            power_node.operation = 'POWER'
            power_node.label = "POWER"

        mat.node_tree.links.new(rgb_node.outputs['Color'], power_node.inputs['Value'])
        power_node.inputs[1].default_value = 2.0

        # mix the color with "BASE COLOR" node
        base_color = None
        for node in mat.node_tree.nodes:
            if node.label == "BASE COLOR":
                base_color = node
                break

        if base_color is None:
            print(f"Material '{mat.name}' does not have a 'BASE COLOR' node.")
            return
        
        mix_color = None
        for node in mat.node_tree.nodes:
            if node.label == "MULTIPLY COLOR":
                mix_color = node
                break

        if mix_color is None:
            mix_color = mat.node_tree.nodes.new('ShaderNodeMixRGB')
            mix_color.label = "MULTIPLY COLOR"

        mix_color.blend_type = 'MULTIPLY'
        mix_color.inputs['Fac'].default_value = 1.0
        mat.node_tree.links.new(mix_color.outputs['Color'], principled_bsdf.inputs['Base Color'])
        mat.node_tree.links.new(base_color.outputs['Color'], mix_color.inputs['Color1'])
        mat.node_tree.links.new(power_node.outputs['Value'], mix_color.inputs['Color2'])
        mat.node_tree.links.new(base_color.outputs['Alpha'], mix_color.inputs['Fac'])

        # organize nodes
        rgb_node.location = (base_color.location.x - 300, base_color.location.y)
        mix_color.location = (base_color.location.x, base_color.location.y - 150)
        power_node.location = (base_color.location.x + 300, base_color.location.y)

    def handleMaterial(self, mat):
        # Check if the material uses nodes
        if not mat.use_nodes:
            return

        if "ShaderPackage" not in mat:
            return

        if mat["ShaderPackage"] != "bgcolorchange.shpk":
            return
        
        self.mixColor(mat)
        self.discardNormalBlueChannel(mat)


    def execute(self, context):
        # Iterate all materials in the scene
        # if ShaderPackage is bgcolorchange.shpk, then read DiffuseColor property

        for mat in bpy.data.materials:
            self.handleMaterial(mat)

        return {'FINISHED'}

class MEDDLE_OT_fix_bg(Operator):
    """Looks up the g_SamplerXXXMap1 values on bg materials and creates the relevant texture nodes, select the 'cache' directory from your meddle export folder"""
    bl_idname = "meddle.fix_bg"
    bl_label = "Fix bg.shpk"
    bl_options = {'REGISTER', 'UNDO'}

    directory: StringProperty(subtype='DIR_PATH')

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}


    def getPrincipalBsdf(self, mat):
        principled_bsdf = None
        for node in mat.node_tree.nodes:
            if node.type == 'BSDF_PRINCIPLED':
                principled_bsdf = node
                break
        return principled_bsdf

    def handleNormalChannels(self, mat, vertex_color_node, principled_bsdf):
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

        if g_SamplerNormalMap1 is not None and g_SamplerNormalMap0Node is not None and normal_tangent is not None and vertex_color_node is not None:
            mix_normal = None
            for node in mat.node_tree.nodes:
                if node.label == "MIX NORMAL":
                    mix_normal = node
                    break
            if mix_normal is None:
                mix_normal = mat.node_tree.nodes.new('ShaderNodeMixRGB')
                mix_normal.label = "MIX NORMAL"

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

            mat.node_tree.links.new(g_SamplerNormalMap0Node.outputs['Color'], mix_normal.inputs['Color1'])
            mat.node_tree.links.new(gSamplerNormalMap1Node.outputs['Color'], mix_normal.inputs['Color2'])

            # organize nodes
            gSamplerNormalMap1Node.location = (g_SamplerNormalMap0Node.location.x, g_SamplerNormalMap0Node.location.y - 150)
            mix_normal.location = (g_SamplerNormalMap0Node.location.x + 300, g_SamplerNormalMap0Node.location.y)
            normal_tangent.location = (g_SamplerNormalMap0Node.location.x + 600, g_SamplerNormalMap0Node.location.y)
            self.discardNormalBlueChannel(mat, mix_normal, normal_tangent, principled_bsdf)
        else:
            self.discardNormalBlueChannel(mat, g_SamplerNormalMap0Node, normal_tangent, principled_bsdf)

    def discardNormalBlueChannel(self, mat, normal_source_node, normal_map_node, principled_bsdf):
        # create node to remove the blue channel
        separate_rgb = None
        for node in mat.node_tree.nodes:
            if node.type == 'SEPARATE_COLOR' and node.name == "Separate Normal Map":
                separate_rgb = node
                break

        if separate_rgb is None:
            separate_rgb = mat.node_tree.nodes.new('ShaderNodeSeparateColor')
            separate_rgb.location = (normal_source_node.location.x + 300, normal_source_node.location.y)
            separate_rgb.name = "Separate Normal Map"

        mat.node_tree.links.new(normal_source_node.outputs['Color'], separate_rgb.inputs['Color'])

        # create node to add the blue channel
        combine_rgb = None
        for node in mat.node_tree.nodes:
            if node.type == 'COMBINE_COLOR' and node.name == "Combine Normal Map":
                combine_rgb = node
                break

        if combine_rgb is None:
            combine_rgb = mat.node_tree.nodes.new('ShaderNodeCombineColor')
            combine_rgb.location = (separate_rgb.location.x + 300, separate_rgb.location.y)
            combine_rgb.name = "Combine Normal Map"
            
        combine_rgb.inputs['Blue'].default_value = 1.0
        mat.node_tree.links.new(separate_rgb.outputs['Red'], combine_rgb.inputs['Red'])
        mat.node_tree.links.new(separate_rgb.outputs['Green'], combine_rgb.inputs['Green'])
        mat.node_tree.links.new(combine_rgb.outputs['Color'], principled_bsdf.inputs['Normal'])

        mat.node_tree.links.new(combine_rgb.outputs['Color'], normal_map_node.inputs['Color'])
        mat.node_tree.links.new(normal_map_node.outputs['Normal'], principled_bsdf.inputs['Normal'])

    def handleColorChannels(self, mat, vertex_color_node, principled_bsdf):
        g_SamplerColorMap0Node = None
        for node in mat.node_tree.nodes:
            if node.label == "BASE COLOR":
                g_SamplerColorMap0Node = node
                break

        g_SamplerColorMap1 = None
        if "g_SamplerColorMap1" in mat:
            g_SamplerColorMap1 = mat["g_SamplerColorMap1"]
            print(f"Found custom property 'g_SamplerColorMap1' in material '{mat.name}' with value {g_SamplerColorMap1}.")
        else:
            print(f"Material '{mat.name}' does not have the custom property 'g_SamplerColorMap1'.")

        if g_SamplerColorMap1 is not None and g_SamplerColorMap0Node is not None and vertex_color_node is not None:
            mix_color = None
            for node in mat.node_tree.nodes:
                if node.label == "MIX COLOR":
                    mix_color = node
                    break
            if mix_color is None:
                mix_color = mat.node_tree.nodes.new('ShaderNodeMixRGB')
                mix_color.label = "MIX COLOR"

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


            mat.node_tree.links.new(g_SamplerColorMap0Node.outputs['Color'], mix_color.inputs['Color1'])
            mat.node_tree.links.new(g_SamplerColorMap1Node.outputs['Color'], mix_color.inputs['Color2'])

            # organize nodes
            g_SamplerColorMap1Node.location = (g_SamplerColorMap0Node.location.x, g_SamplerColorMap0Node.location.y - 150)
            mix_color.location = (g_SamplerColorMap0Node.location.x + 300, g_SamplerColorMap0Node.location.y)
        
           
    def handleSpecularChannels(self, mat, vertex_color_node, principled_bsdf):
        g_SamplerSpecularMap1 = None
        if "g_SamplerSpecularMap1" in mat:
            g_SamplerSpecularMap1 = mat["g_SamplerSpecularMap1"]
            print(f"Found custom property 'g_SamplerSpecularMap1' in material '{mat.name}' with value {g_SamplerSpecularMap1}.")
        else:
            print(f"Material '{mat.name}' does not have the custom property 'g_SamplerSpecularMap1'.")

        g_SamplerSpecularMap0Node = None
        for node in mat.node_tree.nodes:
            if node.label == "METALLIC ROUGHNESS":
                g_SamplerSpecularMap0Node = node
                break

        if g_SamplerSpecularMap1 is not None and g_SamplerSpecularMap0Node is not None and vertex_color_node is not None:
            mix_specular = None
            for node in mat.node_tree.nodes:
                if node.label == "MIX SPECULAR":
                    mix_specular = node
                    break
            if mix_specular is None:
                mix_specular = mat.node_tree.nodes.new('ShaderNodeMixRGB')
                mix_specular.label = "MIX SPECULAR"

            mix_specular.blend_type = 'MIX'
            mix_specular.inputs['Fac'].default_value = 1.0
            mat.node_tree.links.new(mix_specular.outputs['Color'], principled_bsdf.inputs['Metallic'])
            mat.node_tree.links.new(vertex_color_node.outputs['Alpha'], mix_specular.inputs['Fac'])

            # load specular texture using the selected folder + specular_map + ".png"
            g_SamplerSpecularMap1Node = None
            for node in mat.node_tree.nodes:
                if node.label == "METALLIC ROUGHNESS 1":
                    g_SamplerSpecularMap1Node = node
                    break

            if g_SamplerSpecularMap1Node is None:
                g_SamplerSpecularMap1Node = mat.node_tree.nodes.new('ShaderNodeTexImage')
                g_SamplerSpecularMap1Node.label = "METALLIC ROUGHNESS 1"
            g_SamplerSpecularMap1Node.image = bpy.data.images.load(self.directory + g_SamplerSpecularMap1 + ".png")

            metallic_factor_node = None
            for node in mat.node_tree.nodes:
                if node.label == "Metallic Factor":
                    metallic_factor_node = node
                    break

            if metallic_factor_node is None:
                metallic_factor_node = mat.node_tree.nodes.new('ShaderNodeMath')
                metallic_factor_node.operation = 'MULTIPLY'
                metallic_factor_node.label = "Metallic Factor"
                metallic_factor_node.inputs['Value'].default_value = 0.0

            # Specular -> Mix/Multiply -> Separate Color -> [Green -> Roughness] [Blue -> Metallic Factor -> Metallic]
            separate_color = None
            for node in mat.node_tree.nodes:
                if node.type == 'SEPARATE_COLOR' and node.name == "Separate Metallic Roughness":
                    separate_color = node
                    break

            if separate_color is None:
                separate_color = mat.node_tree.nodes.new('ShaderNodeSeparateColor')
                separate_color.location = (g_SamplerSpecularMap0Node.location.x + 300, g_SamplerSpecularMap0Node.location.y)
                separate_color.name = "Separate Metallic Roughness"

            mat.node_tree.links.new(g_SamplerSpecularMap0Node.outputs['Color'], mix_specular.inputs['Color1'])
            mat.node_tree.links.new(g_SamplerSpecularMap1Node.outputs['Color'], mix_specular.inputs['Color2'])

            mat.node_tree.links.new(mix_specular.outputs['Color'], separate_color.inputs['Color'])
            mat.node_tree.links.new(separate_color.outputs['Green'], principled_bsdf.inputs['Roughness'])
            mat.node_tree.links.new(separate_color.outputs['Blue'], metallic_factor_node.inputs['Value'])
            mat.node_tree.links.new(metallic_factor_node.outputs['Value'], principled_bsdf.inputs['Metallic'])

            # organize nodes
            g_SamplerSpecularMap1Node.location = (g_SamplerSpecularMap0Node.location.x, g_SamplerSpecularMap0Node.location.y - 150)
            mix_specular.location = (g_SamplerSpecularMap0Node.location.x + 300, g_SamplerSpecularMap0Node.location.y)
            separate_color.location = (g_SamplerSpecularMap0Node.location.x + 600, g_SamplerSpecularMap0Node.location.y)
            metallic_factor_node.location = (g_SamplerSpecularMap0Node.location.x + 900, g_SamplerSpecularMap0Node.location.y)

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

            # get vertex color node
            vertex_color_node = None
            for node in mat.node_tree.nodes:
                if node.type == 'VERTEX_COLOR':
                    vertex_color_node = node
                    break

            try:
                self.handleColorChannels(mat, vertex_color_node, principled_bsdf)
            except Exception as e:
                print(f"Error: {e}")

            try:
                self.handleNormalChannels(mat, vertex_color_node, principled_bsdf)
            except Exception as e:
                print(f"Error: {e}")

            try:
                self.handleSpecularChannels(mat, vertex_color_node, principled_bsdf)
            except Exception as e:
                print(f"Error: {e}")

        return {'FINISHED'}
    
def register():
    bpy.utils.register_class(VIEW3D_PT_update_meddle_shaders)
    bpy.utils.register_class(MEDDLE_OT_fix_ior)
    bpy.utils.register_class(MEDDLE_OT_fix_bg)
    bpy.utils.register_class(MEDDLE_OT_connect_volume)
    bpy.utils.register_class(MEDDLE_OT_stain_housing)
    bpy.types.Scene.selected_folder = StringProperty(name="Selected Folder", description="Path to the selected folder")

def unregister():
    bpy.utils.unregister_class(VIEW3D_PT_update_meddle_shaders)
    bpy.utils.unregister_class(MEDDLE_OT_fix_ior)
    bpy.utils.unregister_class(MEDDLE_OT_fix_bg)
    bpy.utils.unregister_class(MEDDLE_OT_connect_volume)
    bpy.utils.unregister_class(MEDDLE_OT_stain_housing)

if __name__ == "__main__":
    register()
