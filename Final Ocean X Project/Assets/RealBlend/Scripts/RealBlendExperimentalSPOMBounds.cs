using System.Collections.Generic;
using UnityEngine;

namespace RealBlend
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/RealBlend Experimental SPOM Bounds")]
    public class RealBlendExperimentalSPOMBounds : MonoBehaviour
    {
        private static readonly int ClipBoundsId = Shader.PropertyToID("_SPOM_Clip_Bounds");
        private static readonly int ClipSoftnessId = Shader.PropertyToID("_SPOM_Clip_Softness");
        private static readonly int ClipGrazingFadeId = Shader.PropertyToID("_SPOM_Clip_Grazing_Fade");
        private static readonly int ClipDitherId = Shader.PropertyToID("_SPOM_Clip_Dither");
        private static readonly Vector4 NoClipBounds = new Vector4(-500000f, -500000f, 500000f, 500000f);
        private const float DefaultClipSoftness = 0.35f;
        private const float DefaultGrazingFade = 0.25f;
        private const float DefaultClipDither = 0.06f;

        [SerializeField] private bool autoUpdate = true;
        [SerializeField, Range(0f, 2f)] private float clipSoftness = DefaultClipSoftness;
        [SerializeField, Range(0f, 1f)] private float grazingFade = DefaultGrazingFade;
        [SerializeField, Range(0f, 1f)] private float clipDither = DefaultClipDither;

        private Mesh _lastMesh;
        private int _lastVertexCount = -1;
        private Bounds _lastBounds;

        private void OnEnable()
        {
            Refresh();
        }

        private void OnValidate()
        {
            clipSoftness = Mathf.Max(0f, clipSoftness);
            grazingFade = Mathf.Max(0f, grazingFade);
            clipDither = Mathf.Max(0f, clipDither);
            Refresh();
        }

        private void LateUpdate()
        {
            if (!autoUpdate)
                return;

            Mesh mesh = GetSharedMesh();
            if (mesh == null)
                return;

            Bounds bounds = mesh.bounds;
            if (mesh == _lastMesh && mesh.vertexCount == _lastVertexCount && bounds == _lastBounds)
                return;

            Refresh();
        }

        public void Refresh()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            Renderer meshRenderer = GetComponent<Renderer>();
            ApplyToRenderer(meshFilter, meshRenderer, clipSoftness, grazingFade, clipDither);

            _lastMesh = meshFilter != null ? meshFilter.sharedMesh : null;
            _lastVertexCount = _lastMesh != null ? _lastMesh.vertexCount : -1;
            _lastBounds = _lastMesh != null ? _lastMesh.bounds : default;
        }

        public static void ApplyToRenderer(GameObject target)
        {
            if (target == null)
                return;

            RealBlendExperimentalSPOMBounds settings = target.GetComponent<RealBlendExperimentalSPOMBounds>();
            if (settings != null)
            {
                ApplyToRenderer(
                    target.GetComponent<MeshFilter>(),
                    target.GetComponent<Renderer>(),
                    settings.clipSoftness,
                    settings.grazingFade,
                    settings.clipDither);
                return;
            }

            ApplyToRenderer(target.GetComponent<MeshFilter>(), target.GetComponent<Renderer>());
        }

        public static void ApplyToRenderer(MeshFilter meshFilter, Renderer meshRenderer)
        {
            ApplyToRenderer(meshFilter, meshRenderer, DefaultClipSoftness, DefaultGrazingFade, DefaultClipDither);
        }

        private static void ApplyToRenderer(
            MeshFilter meshFilter,
            Renderer meshRenderer,
            float clipSoftness,
            float grazingFade,
            float clipDither)
        {
            if (meshRenderer == null)
                return;

            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            Vector4 bounds = CalculateUVBounds(mesh);

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(block);
            block.SetVector(ClipBoundsId, bounds);
            block.SetFloat(ClipSoftnessId, Mathf.Max(0f, clipSoftness));
            block.SetFloat(ClipGrazingFadeId, Mathf.Max(0f, grazingFade));
            block.SetFloat(ClipDitherId, Mathf.Max(0f, clipDither));
            meshRenderer.SetPropertyBlock(block);
        }

        public static Vector4 CalculateUVBounds(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0)
                return NoClipBounds;

            List<Vector2> uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            if (uvs.Count == 0)
                return NoClipBounds;

            Vector2 min = uvs[0];
            Vector2 max = uvs[0];
            for (int i = 1; i < uvs.Count; i++)
            {
                Vector2 uv = uvs[i];
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }

            if (max.x - min.x <= 0.0001f)
            {
                min.x -= 0.0001f;
                max.x += 0.0001f;
            }

            if (max.y - min.y <= 0.0001f)
            {
                min.y -= 0.0001f;
                max.y += 0.0001f;
            }

            return new Vector4(min.x, min.y, max.x, max.y);
        }

        private Mesh GetSharedMesh()
        {
            MeshFilter meshFilter = GetComponent<MeshFilter>();
            return meshFilter != null ? meshFilter.sharedMesh : null;
        }
    }
}
