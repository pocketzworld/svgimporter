

//#define DEBUG_MATERIALS

using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

using System.Collections;
using System.Collections.Generic;

namespace SVGImporter
{
    using Utils;

    [ExecuteInEditMode]
    [AddComponentMenu("UI/SVG Image", 21)]
    public class SVGImage : MaskableGraphic, ILayoutElement, ICanvasRaycastFilter, ISVGRenderer, ISVGReference
    {
        public enum Type
        {
            Simple,
            Sliced
        }

        [FormerlySerializedAs("vectorGraphics")]
        [SerializeField]
        protected SVGAsset _vectorGraphics = default;
        protected SVGAsset _lastVectorGraphics;
        public SVGAsset vectorGraphics
        {
            get {
                return _vectorGraphics;
            }
            set {
                if(SVGPropertyUtility.SetClass(ref _vectorGraphics, value))
                {
                    Clear();
                    UpdateMaterial();
                    SetAllDirty();
                }
            }
        }

        /// How the Image is drawn.
        [SerializeField] private Type m_Type = Type.Simple;
        public Type type { get { return m_Type; } set { if (SVGPropertyUtility.SetStruct(ref m_Type, value)) SetVerticesDirty(); } }

        [SerializeField] private bool m_PreserveAspect = false;
        public bool preserveAspect { get { return m_PreserveAspect; } set { if (SVGPropertyUtility.SetStruct(ref m_PreserveAspect, value)) SetVerticesDirty(); } }

        [SerializeField] private bool m_UsePivot = false;
        public bool usePivot { get { return m_UsePivot; } set { if (SVGPropertyUtility.SetStruct(ref m_UsePivot, value)) SetVerticesDirty(); } }

        // Not serialized until we support read-enabled sprites better.
        private float m_EventAlphaThreshold = 1;
        public float eventAlphaThreshold { get { return m_EventAlphaThreshold; } set { m_EventAlphaThreshold = value; } }

        protected Material _defaultMaterial;

        protected List<ISVGModify> _modifiers = new List<ISVGModify>();
        public List<ISVGModify> modifiers
        {
            get {
                return _modifiers;
            }
        }

        // Tracking of mesh change
        protected int _lastFrameChanged;
        public int lastFrameChanged
        {
            get {
                return _lastFrameChanged;
            }
        }
        
        public void AddModifier(ISVGModify modifier)
        {
            if(_modifiers.Contains(modifier)) return;
            _modifiers.Add(modifier);
        }
        
        public void RemoveModifier(ISVGModify modifier)
        {
            if(!_modifiers.Contains(modifier)) return;
            _modifiers.Remove(modifier);
        }

        public void UpdateRenderer()
        {
            this.SetAllDirty();
        }

        protected System.Action<SVGLayer[], SVGAsset, bool> _OnPrepareForRendering;
        /// <summary>
        /// delegate which indicates that the mesh has changed.
        /// You can use it as your custom mesh postprocessor.
        /// </summary>
        public virtual System.Action<SVGLayer[], SVGAsset, bool> OnPrepareForRendering
        {
            get {
                return _OnPrepareForRendering;
            }
            set {
                _OnPrepareForRendering = value;
            }
        }

        bool useLayers
        {
            get {
                return _vectorGraphics.useLayers;
            }
        }

        protected override void Awake()
        {            
            Clear();
            UpdateMaterial();
            if(_vectorGraphics != null)
            {
                _vectorGraphics.AddReference(this);
            }
            base.Awake();
        }

        protected override void OnDestroy()
        {
            if (_vectorGraphics != null)
            {
                _vectorGraphics.RemoveReference(this);
            }
            base.OnDestroy();
        }

        #if UNITY_EDITOR
        protected override void Reset()
        #else
        private void OnAwake()
        #endif
        {
            Clear();
            _vectorGraphics = null;
            UpdateMaterial();
#if UNITY_EDITOR
            base.Reset();
#endif
        }

        protected void Refresh()
        {
            Clear();
            UpdateMaterial();
        }
       
        /// <summary>
        /// Whether the Image has a border to work with.
        /// </summary>        
        public bool hasBorder
        {
            get
            {
                if (_vectorGraphics != null)
                {
                    return _vectorGraphics.border.sqrMagnitude > 0f;
                }
                return false;
            }
        }

        /// <summary>
        /// Conversion ratio for UI Interpretation
        /// </summary>
        public float pixelsPerUnit
        {
            get
            {
                float spritePixelsPerUnit = 100;
                return spritePixelsPerUnit;
            }
        }

        protected Mesh sharedMesh
        {
            get {
                if(_vectorGraphics == null)
                    return null;
                return _vectorGraphics.sharedMesh;
            }
        }

        /// Image's dimensions used for drawing. X = left, Y = bottom, Z = right, W = top.
        private Vector4 GetDrawingDimensions(bool shouldPreserveAspect)
        {
            Vector2 size = sharedMesh == null ? Vector2.zero : (Vector2)sharedMesh.bounds.size;
            
            Rect r = GetPixelAdjustedRect();
            // Debug.Log(string.Format("r:{2}, size:{0}, padding:{1}", size, padding, r));

            if (shouldPreserveAspect && size.sqrMagnitude > 0.0f)
            {
                var spriteRatio = size.x / size.y;
                var rectRatio = r.width / r.height;
                
                if (spriteRatio > rectRatio)
                {
                    var oldHeight = r.height;
                    r.height = r.width * (1.0f / spriteRatio);
                    r.y += (oldHeight - r.height) * rectTransform.pivot.y;
                }
                else
                {
                    var oldWidth = r.width;
                    r.width = r.height * spriteRatio;
                    r.x += (oldWidth - r.width) * rectTransform.pivot.x;
                }
            }

            return new Vector4(
                r.x,
                r.y,
                r.width,
                r.height
                );
        }

        public override void SetNativeSize()
        {
            if (sharedMesh != null)
            {
                Bounds bounds = sharedMesh.bounds;
                Vector2 size = bounds.size * 1000f;
                float w = size.x / pixelsPerUnit;
                float h = size.y / pixelsPerUnit;
                rectTransform.anchorMax = rectTransform.anchorMin;
                rectTransform.sizeDelta = new Vector2(w, h);
                SetAllDirty();
            }
        }

        public override Material defaultMaterial
        {
            get
            {
                GetDefaultMaterial();
                return _defaultMaterial;
            }
        }

        protected float InverseLerp(float from, float to, float value)
        {
            if (from < to)
            {               
                value -= from;
                value /= to - from;
                return value;
            }
            else
            {
                return 1f - (value - to) / (from - to);
            }
        }

        protected float Lerp(float from, float to, float value)
        {
            return from + value * (to-from);
        }

        public virtual void CalculateLayoutInputHorizontal() { }
        public virtual void CalculateLayoutInputVertical() { }
        
        public virtual float minWidth { get { return 0; } }
        
        public virtual float preferredWidth
        {
            get
            {
                if (sharedMesh == null)
                    return 0;
                Bounds bounds = sharedMesh.bounds;
                return bounds.size.x / pixelsPerUnit;
            }
        }
        
        public virtual float flexibleWidth { get { return -1; } }
        
        public virtual float minHeight { get { return 0; } }
        
        public virtual float preferredHeight
        {
            get
            {
                if (sharedMesh == null)
                    return 0;
                Bounds bounds = sharedMesh.bounds;
                return bounds.size.y / pixelsPerUnit;
            }
        }
        
        public virtual float flexibleHeight { get { return -1; } }
        
        public virtual int layoutPriority { get { return 0; } }
        
        public virtual bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (m_EventAlphaThreshold >= 1)
                return true;

            if (sharedMesh == null)
                return true;

            return true;
        }
        
        private Vector2 MapCoordinate(Vector2 local, Rect rect)
        {
            Bounds bounds = sharedMesh.bounds;
            return new Vector2(local.x * bounds.size.x / rect.width, local.y * bounds.size.y / rect.height);
        }

        public override void SetMaterialDirty()
        {
            if (this.IsActive())
            {
                SVGAtlas.Instance.UpdateMaterialProperties(m_Material);
            }

            base.SetMaterialDirty();
        }  

        protected float SafeDivide(float a, float b)
        {
            if(b == 0) return 0f;
            return a / b;
        }

        protected string BorderToString(Vector4 border)
        {
            return string.Format("left: {0}, bottom: {1}, right: {2}, top: {3}", border.x, border.y, border.z, border.w);
        }

        const float epsilon = 0.0000001f;

        int tempVBOLength;
        static List<UIVertex> vertexStream = new List<UIVertex>();
        static List<Vector3> vertices = new List<Vector3>();
        static List<int> triangles = new List<int>();
        static List<Vector2> uv = new List<Vector2>();
        static List<Vector2> uv2 = new List<Vector2>();
        static List<Color32> colors = new List<Color32>();
        static List<Vector3> normals = new List<Vector3>();
        
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if(sharedMesh == null)
            {
                base.OnPopulateMesh(vh); 
                return;
            }

            if(vh == null || canvas==null || _vectorGraphics==null)
            {
                return;
            }

            vh.Clear();

            Mesh mesh = sharedMesh;
            tempVBOLength = mesh.vertexCount;

            mesh.GetVertices(vertices);
            mesh.GetTriangles(triangles, 0);
            mesh.GetUVs(0, uv);
            mesh.GetUVs(1, uv2);
            mesh.GetColors(colors);
            mesh.GetNormals(normals);

            vertexStream.Clear();
            if(vertexStream.Capacity < tempVBOLength)
            {
                vertexStream.Capacity = tempVBOLength;
            }
            for(int i = 0; i < tempVBOLength; i++)
            {
                vertexStream.Add(new UIVertex());
            }

            if (_vectorGraphics.antialiasing || _vectorGraphics.generateNormals)
            {
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.Normal;
            }
            else
            {
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            }

            Bounds bounds = sharedMesh.bounds;
            if(m_UsePivot)
            {
                bounds.center += new Vector3((-0.5f + _vectorGraphics.pivotPoint.x) * bounds.size.x, (0.5f - _vectorGraphics.pivotPoint.y) * bounds.size.y, 0f);
            }

            if(m_Type == Type.Simple)
            {
                Vector4 v = GetDrawingDimensions(preserveAspect);
                
                for(int i = 0; i < tempVBOLength; i++)
                {
                    UIVertex vertex = vertexStream[i];
                    vertex.position.x = v.x + InverseLerp(bounds.min.x, bounds.max.x, vertices[i].x) * v.z;
                    vertex.position.y = v.y + InverseLerp(bounds.min.y, bounds.max.y, vertices[i].y) * v.w;
                    vertex.color = colors[i] * color;
                    vertexStream[i] = vertex;
                }
            } else
            {
                Vector4 v = GetDrawingDimensions(false);
                
                // LEFT = X, BOTTOM = Y, RIGHT = Z, TOP = W
                Vector4 border = _vectorGraphics.border;                
                Vector4 borderCalc = new Vector4(border.x + epsilon, border.y + epsilon, 1f - border.z - epsilon, 1f - border.w - epsilon);
                
                Vector2 normalizedPosition;
                
                float rectSize = canvas.referencePixelsPerUnit * vectorGraphics.scale * 100f;
                Vector2 size = new Vector2(bounds.size.x * rectSize, bounds.size.y * rectSize);
                Vector4 transformRect = new Vector4(v.x, v.y, v.x + v.z, v.y + v.w);
                Vector4 borderRect = new Vector4(size.x * border.x,
                                                 size.y * border.y,
                                                 size.x * border.z,
                                                 size.y * border.w);
                
                Vector2 scale = new Vector2(SafeDivide(1f, (1f - (border.x + border.z))) * (v.z - (borderRect.x + borderRect.z)),
                                            SafeDivide(1f, (1f - (border.y + border.w))) * (v.w - (borderRect.w + borderRect.y)));
                
                float minWidth = borderRect.x + borderRect.z;
                if(minWidth != 0f)
                {
                    minWidth = Mathf.Clamp01(v.z / minWidth);
                    if(minWidth != 1f)
                    {
                        scale.x = 0f;
                        size.x *= minWidth;
                        borderRect.x *= minWidth;
                        borderRect.z *= minWidth;
                    }
                }
                
                float minHeight = borderRect.w + borderRect.y;
                if(minHeight != 0f)
                {
                    minHeight = Mathf.Clamp01(v.w / minHeight);
                    if(minHeight != 1f)
                    {
                        scale.y = 0f;
                        size.y *= minHeight;
                        borderRect.w *= minHeight;
                        borderRect.y *= minHeight;
                    }                    
                }
                
                float borderTop = transformRect.w - borderRect.w;
                float borderLeft = transformRect.x + borderRect.x;
                
                for(int i = 0; i < tempVBOLength; i++)
                {
                    UIVertex vertex = vertexStream[i];
                    vertex.color = colors[i] * color;
                    
                    normalizedPosition.x = InverseLerp(bounds.min.x, bounds.max.x, vertices[i].x);
                    normalizedPosition.y = InverseLerp(bounds.min.y, bounds.max.y, vertices[i].y);
                    
                    if(border.x != 0f && normalizedPosition.x <= borderCalc.x)
                    {
                        vertex.position.x = transformRect.x + normalizedPosition.x * size.x;
                    } else if(border.z != 0f && normalizedPosition.x >= borderCalc.z)
                    {
                        vertex.position.x = transformRect.z - (1f - normalizedPosition.x) * size.x;
                    } else {
                        vertex.position.x = borderLeft + (normalizedPosition.x - border.x) * scale.x;
                    }
                    
                    if(border.w != 0f && normalizedPosition.y >= borderCalc.w)
                    {
                        vertex.position.y = transformRect.w - (1f - normalizedPosition.y) * size.y;
                    } else if(border.y != 0f && normalizedPosition.y <= borderCalc.y)
                    {
                        vertex.position.y = transformRect.y + normalizedPosition.y * size.y;
                    } else {
                        vertex.position.y = borderTop - (((1f - normalizedPosition.y) - border.w) * scale.y);
                    }
                    vertexStream[i] = vertex;
                }
            }
            
            if (_vectorGraphics.hasGradients || _vectorGraphics.useGradients == SVGUseGradients.Always)
            {
                if(uv != null && uv2 != null && tempVBOLength == uv.Count && tempVBOLength == uv2.Count)
                {
                    for(int i = 0; i < tempVBOLength; i++)
                    {
                        UIVertex vertex = vertexStream[i];
                        vertex.uv0 = uv[i];
                        vertex.uv1 = uv2[i];
                        vertexStream[i] = vertex;
                    }
                }                
            }
            
            if(_vectorGraphics.antialiasing)
            {
                if(_vectorGraphics.antialiasing)
                {
                    if(normals != null && tempVBOLength == normals.Count)
                    {
                        for(int i = 0; i < tempVBOLength; i++)
                        {
                            UIVertex vertex = vertexStream[i];
                            vertex.normal.x = normals[i].x;
                            vertex.normal.y = normals[i].y;
                            vertexStream[i] = vertex;
                        }
                    } 
                }
            } else {
                if(_vectorGraphics.generateNormals)
                {
                    if(normals != null && normals.Count == tempVBOLength)
                    {
                        for(int i = 0; i < tempVBOLength; i++)
                        {
                            UIVertex vertex = vertexStream[i];
                            vertex.normal = normals[i];
                            vertexStream[i] = vertex;
                        }
                    }
                }
            }
            
            vh.AddUIVertexStream(vertexStream, triangles);

            _lastFrameChanged = Time.frameCount;
        }

        protected void GetDefaultMaterial()
        {
            if(_lastVectorGraphics != _vectorGraphics)
            {
                if(_lastVectorGraphics != null)
                {
                    _lastVectorGraphics.RemoveReference(this);
                }
                if(_vectorGraphics != null)
                {
                    _vectorGraphics.AddReference(this);
                }
                _lastVectorGraphics = _vectorGraphics;
                Clear();
            }
            
            if(_vectorGraphics != null)
            {
                if(_defaultMaterial == null)
                {
                    _defaultMaterial = _vectorGraphics.sharedUIMaterial;
                }                
            }
        }
        
        protected void Clear()
        {            
            _defaultMaterial = null;
        }

        protected override void UpdateMaterial()
        {
            GetDefaultMaterial();
			base.UpdateMaterial();
        }                    
    }
}
