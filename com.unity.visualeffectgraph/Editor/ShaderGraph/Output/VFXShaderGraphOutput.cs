#if VFX_HAS_SG
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Text;
using UnityEditor.VFX;
using UnityEngine.Rendering;
using System.Reflection;
using UnityEngine.VFX;

using UnityObject = UnityEngine.Object;

namespace UnityEditor.VFX.SG
{
    class VFXShaderGraphPostProcessor : AssetPostprocessor
    {

        static MethodInfo s_GetResourceAtPath = System.Type.GetType("UnityEditor.VFX.VisualEffectResource, UnityEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null").GetMethod("GetResourceAtPath",System.Reflection.BindingFlags.Public| System.Reflection.BindingFlags.Static);
        static PropertyInfo s_GetOrCreateGraph = System.Type.GetType("UnityEditor.VFX.VisualEffectResource, UnityEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null").GetProperty("graph", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            string[] guids = AssetDatabase.FindAssets("t:VisualEffectAsset");

            var assetsToReimport = new HashSet<VFXGraph>();

            var modifiedShaderGraphs = new HashSet<Shader>();

            foreach( var asset in importedAssets.Concat(deletedAssets))
            {
                if (asset.EndsWith(".shadergraph", StringComparison.InvariantCultureIgnoreCase))
                    modifiedShaderGraphs.Add(AssetDatabase.LoadAssetAtPath<Shader>(asset));
            }

            foreach( var vfxPath in guids.Select(t => AssetDatabase.GUIDToAssetPath(t)))
            {

                UnityObject resource = (UnityObject)s_GetResourceAtPath.Invoke(null, new object[] { vfxPath });
                if (resource != null)
                {
                    VFXGraph graph = (VFXGraph)s_GetOrCreateGraph.GetValue(resource, new object[] { });

                    if (graph != null)
                    {
                        if (graph.children.OfType<VFXShaderGraphOutput>().Any(t => modifiedShaderGraphs.Contains(t.shaderGraph)))
                            assetsToReimport.Add(graph);
                    }
                }
            }

            foreach( var graph in assetsToReimport)
            {
                foreach (var sgOutput in graph.children.OfType<VFXShaderGraphOutput>().Where(t => modifiedShaderGraphs.Contains(t.shaderGraph)))
                    sgOutput.ResyncSlots(true);

                graph.SetExpressionGraphDirty();
                graph.RecompileIfNeeded();
            }
        }
    }


    abstract class VFXShaderGraphOutput : VFXAbstractSortedOutput, ISpecificGenerationOutput
    {
        protected VFXShaderGraphOutput() :base(VFXDataType.Particle) { }
        public override string codeGeneratorTemplate { get { return ""; } }

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InGraph), SerializeField]
        protected Shader m_ShaderGraph = null;

        public Shader shaderGraph
        {
            get { return m_ShaderGraph; }
        }

        public override IEnumerable<VFXAttributeInfo> attributes
        {
            get
            {
                yield return new VFXAttributeInfo(VFXAttribute.Position, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Color, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Alpha, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.Alive, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AxisX, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AxisY, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AxisZ, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AngleX, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AngleY, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.AngleZ, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.PivotX, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.PivotY, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.PivotZ, VFXAttributeMode.Read);

                yield return new VFXAttributeInfo(VFXAttribute.Size, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.ScaleX, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.ScaleY, VFXAttributeMode.Read);
                yield return new VFXAttributeInfo(VFXAttribute.ScaleZ, VFXAttributeMode.Read);
            }
        }

        static readonly Dictionary<string, Type> s_shaderTypeToType = new Dictionary<string, Type>
        {
            { "Vector1" , typeof(float) },
            { "Vector2", typeof(Vector2) },
            { "Vector3", typeof(Vector3) },
            { "Vector4", typeof(Vector4) },
            { "Color" , typeof(Color) },
            { "Texture2D" , typeof(Texture2D) },
            { "Texture2DArray" , typeof(Texture2DArray) },
            { "Texture3D" , typeof(Texture3D) },
            { "Cubemap" , typeof(Cubemap) },
            { "Bool" , typeof(bool) },
            { "Matrix4" , typeof(Matrix4x4) },
            { "Gradient" , typeof(Gradient) },
        };

        public string GenerateShader(ref VFXInfos infos)
        {
            if (shaderGraph == null)
                return null;
            string result = VFXSGShaderGenerator.GenerateShader(shaderGraph, ref infos);

            return result;
        }

        protected override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get {
                if( shaderGraph != null)
                {
                    var graph = VFXSGShaderGenerator.LoadShaderGraph(shaderGraph);
                    if( graph != null)
                    {
                        List<string> sgDeclarations = VFXSGShaderGenerator.GetPropertiesExcept(graph,attributes.Select(t => t.attrib.name).ToList());

                        foreach (var decl in sgDeclarations)
                        {
                            int lastSpace = decl.LastIndexOfAny(new char[] { '\t', ' ' });
                            string variable = decl.Substring(lastSpace + 1);
                            string typeName = decl.Substring(0, lastSpace).Trim();
                            Type type;
                            if(s_shaderTypeToType.TryGetValue(typeName,out type))
                                yield return new VFXPropertyWithValue(new VFXProperty(type, variable));
                        }
                    }
                }

                foreach ( var prop in PropertiesFromType(GetInputPropertiesTypeName()))
                {
                    yield return prop;
                }
            }
        }

        public virtual IEnumerable<string> GetUsedSlotNames()
        {
            foreach( var input in inputSlots)
                yield return input.name;
        }
        protected override IEnumerable<VFXNamedExpression> CollectGPUExpressions(IEnumerable<VFXNamedExpression> slotExpressions)
        {
            foreach (var exp in base.CollectGPUExpressions(slotExpressions))
                yield return exp;
            if (shaderGraph != null)
            {
                var graph = VFXSGShaderGenerator.LoadShaderGraph(shaderGraph);
                if (graph != null)
                {
                    List<string> sgDeclarations = VFXSGShaderGenerator.GetPropertiesExcept(graph, attributes.Select(t => t.attrib.name).ToList());

                    foreach (var decl in sgDeclarations)
                    {
                        int lastSpace = decl.LastIndexOfAny(new char[] { '\t', ' ' });
                        string variable = decl.Substring(lastSpace + 1);
                        string typeName = decl.Substring(0, lastSpace).Trim();
                        Type type;
                        if (s_shaderTypeToType.TryGetValue(typeName, out type))
                        {
                            VFXNamedExpression expression = slotExpressions.FirstOrDefault(o => o.name == variable);
                            if( expression.exp != null)
                                yield return expression;
                        }
                    }
                }

            }

        }

        public override VFXExpressionMapper GetExpressionMapper(VFXDeviceTarget target)
        {
            var mapper = base.GetExpressionMapper(target);

            switch (target)
            {
                case VFXDeviceTarget.CPU:


                    break;
                default:
                    var graph = VFXSGShaderGenerator.LoadShaderGraph(shaderGraph);
                    if (graph != null)
                    {
                        Dictionary<string,Texture> textures = VFXSGShaderGenerator.GetUsedTextures(graph);
                        foreach( var tex in textures.Where(t=>t.Value != null).OrderBy(t=>t.Key))
                        {
                            var renderTex = tex.Value as RenderTexture;
                            switch( tex.Value.dimension)
                            {
                                case TextureDimension.Tex2D:
                                    mapper.AddExpression(new VFXTexture2DValue(tex.Value, VFXValue.Mode.Variable), tex.Key, -1);
                                    break;
                                case TextureDimension.Tex3D:
                                    mapper.AddExpression(new VFXTexture3DValue(tex.Value, VFXValue.Mode.Variable), tex.Key, -1);
                                    break;
                                case TextureDimension.Cube:
                                    mapper.AddExpression(new VFXTextureCubeValue(tex.Value, VFXValue.Mode.Variable), tex.Key, -1);
                                    break;
                                case TextureDimension.Tex2DArray:
                                    mapper.AddExpression(new VFXTexture2DArrayValue(tex.Value, VFXValue.Mode.Variable), tex.Key, -1);
                                    break;
                                case TextureDimension.CubeArray:
                                    mapper.AddExpression(new VFXTextureCubeArrayValue(tex.Value, VFXValue.Mode.Variable), tex.Key, -1);
                                    break;
                            }
                        }
                    }
                    break;
            }

            return mapper;
        }
    }
}

#endif
