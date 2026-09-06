using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    internal sealed class OceanInstanceBatch
    {
        // Standard shaders carry both objectToWorld and worldToObject. 511 is the portable Unity limit.
        const int Capacity = 511;
        sealed class Page
        {
            public readonly Matrix4x4[] Matrices = new Matrix4x4[Capacity];
            public int Count;
            public Bounds Bounds;
        }
        readonly List<Page> pages = new List<Page>();
        int usedPages;
        public void Clear()
        {
            usedPages = 0;
            foreach (var page in pages) page.Count = 0;
        }
        public void Add(Matrix4x4 matrix, Bounds bounds)
        {
            if (usedPages == 0 || pages[usedPages - 1].Count == Capacity)
            {
                if (pages.Count == usedPages) pages.Add(new Page());
                usedPages++;
            }
            var page = pages[usedPages - 1];
            if (page.Count == 0) page.Bounds = bounds;
            else page.Bounds.Encapsulate(bounds);
            page.Matrices[page.Count++] = matrix;
        }
        public int Draw(Mesh mesh, Material material, MaterialPropertyBlock properties, Camera camera, int layer, bool receiveShadows)
        {
            for (int i = 0; i < usedPages; i++)
            {
                var page = pages[i];
                var rp = new RenderParams(material)
                {
                    camera = camera, layer = layer, matProps = properties, worldBounds = page.Bounds,
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = receiveShadows,
                    lightProbeUsage = LightProbeUsage.Off, motionVectorMode = MotionVectorGenerationMode.Camera
                };
                Graphics.RenderMeshInstanced(rp, mesh, 0, page.Matrices, page.Count);
            }
            return usedPages;
        }
    }
}
