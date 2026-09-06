using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>Unit XZ patch, with one index-buffer variant for each combination of stitched edges.</summary>
    public static class OceanPatchMesh
    {
        public static Mesh Create(int resolution, OceanSeam seams)
        {
            resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 2, 128);
            int width = resolution + 1;
            var positions = new Vector3[width * width];
            var normals = new Vector3[positions.Length];
            var tangents = new Vector4[positions.Length];
            var uv = new Vector2[positions.Length];
            for (int z = 0; z <= resolution; z++)
                for (int x = 0; x <= resolution; x++)
                {
                    int i = z * width + x;
                    uv[i] = new Vector2((float)x / resolution, (float)z / resolution);
                    positions[i] = new Vector3(uv[i].x - 0.5f, 0, uv[i].y - 0.5f);
                    normals[i] = Vector3.up;
                    tangents[i] = new Vector4(1, 0, 0, -1);
                }
            var indices = new List<int>(resolution * resolution * 6);
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    int a = Remap(x, z), b = Remap(x, z + 1), c = Remap(x + 1, z), d = Remap(x + 1, z + 1);
                    // Two collapsed edges at the top-right corner would leave a collinear interior
                    // T-junction on the usual diagonal. Flip this cell so the center remains shared.
                    if (x == resolution - 1 && z == resolution - 1 &&
                        (seams & (OceanSeam.Right | OceanSeam.Top)) == (OceanSeam.Right | OceanSeam.Top))
                    {
                        AddTriangle(a, b, d);
                        AddTriangle(a, d, c);
                    }
                    else
                    {
                        AddTriangle(a, b, c);
                        AddTriangle(c, b, d);
                    }
                }
            var mesh = new Mesh { name = $"Ocean Patch {resolution} [{seams}]", hideFlags = HideFlags.HideAndDontSave };
            mesh.indexFormat = positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uv;
            // The ocean shader uses TEXCOORD1 as an explicit geometry-kind flag.
            // Do not rely on a missing vertex stream reading as zero on every graphics backend.
            mesh.uv2 = new Vector2[positions.Length];
            mesh.SetTriangles(indices, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1, 0.1f, 1));
            return mesh;

            int Remap(int x, int z)
            {
                // A fine edge must use exactly the coarse neighbour's sample positions.
                if ((x == 0 && (seams & OceanSeam.Left) != 0) || (x == resolution && (seams & OceanSeam.Right) != 0)) z &= ~1;
                if ((z == 0 && (seams & OceanSeam.Bottom) != 0) || (z == resolution && (seams & OceanSeam.Top) != 0)) x &= ~1;
                return z * width + x;
            }
            void AddTriangle(int a, int b, int c)
            {
                if (a == b || a == c || b == c) return;
                // Corner stitching can also produce three different but collinear vertices.
                if (Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).y <= 0) return;
                indices.Add(a); indices.Add(b); indices.Add(c);
            }
        }
    }
}
