using System;
using UnityEngine;
using UnityEditor.Rendering;



namespace UnityEditor.ShaderGraph
{

   
    class PBRMasterGUI : ShaderGUI
    {

        public bool m_FirstTimeApply = true;


        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
        {
            Material material = materialEditor.target as Material;

            EditorGUI.BeginChangeCheck();

            materialEditor.PropertiesDefaultGUI(props);
           
            foreach (MaterialProperty prop in props)
            {
                if (prop.name == "_EmissionColor")
                {
                    if (materialEditor.EmissionEnabledProperty())
                    {
                        materialEditor.LightmapEmissionFlagsProperty(MaterialEditor.kMiniTextureFieldLabelIndentLevel, true);
                    }
                    return;
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                SetMaterialKeywords(material);
            }
        }


        public static void SetMaterialKeywords(Material material, Action<Material> shadingModelFunc = null, Action<Material> shaderFunc = null)
        {
            StackUtilities.SetMaterialKeywords(material);
        }

    }
}
