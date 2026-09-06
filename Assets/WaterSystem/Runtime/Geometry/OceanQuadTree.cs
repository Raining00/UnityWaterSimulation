using System;
using System.Collections.Generic;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [Flags]
    public enum OceanSeam { None = 0, Left = 1, Right = 2, Bottom = 4, Top = 8 }

    // Coordinates and spans are integers in the finest-level grid, avoiding float neighbour tests.
    public struct OceanTile
    {
        public int X, Z, Span, Depth;
        public OceanSeam Seams;
        public OceanTile(int x, int z, int span, int depth)
        { X = x; Z = z; Span = span; Depth = depth; Seams = OceanSeam.None; }
    }

    /// <summary>Camera-adaptive, complete, 2:1 balanced square quadtree. No rendering or FFT dependencies.</summary>
    public sealed class OceanQuadTree
    {
        readonly List<OceanTile> leaves = new List<OceanTile>(512);
        readonly List<OceanTile> scratch = new List<OceanTile>(512);
        int[] owners = Array.Empty<int>();
        bool[] split = Array.Empty<bool>();
        Vector3 cameraPosition;
        float splitDistance;
        public IReadOnlyList<OceanTile> Leaves => leaves;
        public int GridSize { get; private set; }
        public float RootSize { get; private set; }
        public float CellSize => RootSize / GridSize;
        public Vector3 Center { get; private set; }

        public void Build(Vector3 center, float rootSize, int maxDepth, float lodDistance, Vector3 camera)
        {
            Center = center;
            RootSize = Mathf.Max(1, rootSize);
            GridSize = 1 << Mathf.Clamp(maxDepth, 0, 8);
            splitDistance = Mathf.Max(0.25f, lodDistance);
            cameraPosition = camera;
            leaves.Clear();
            Subdivide(new OceanTile(0, 0, GridSize, 0));
            if (owners.Length != GridSize * GridSize) owners = new int[GridSize * GridSize];
            Balance();
            for (int i = 0; i < leaves.Count; i++)
            {
                var tile = leaves[i];
                if (HasLargerNeighbour(tile, OceanSeam.Left)) tile.Seams |= OceanSeam.Left;
                if (HasLargerNeighbour(tile, OceanSeam.Right)) tile.Seams |= OceanSeam.Right;
                if (HasLargerNeighbour(tile, OceanSeam.Bottom)) tile.Seams |= OceanSeam.Bottom;
                if (HasLargerNeighbour(tile, OceanSeam.Top)) tile.Seams |= OceanSeam.Top;
                leaves[i] = tile;
            }
        }

        void Subdivide(OceanTile tile)
        {
            var bounds = GetBounds(tile, 0, 0);
            if (tile.Span > 1 && bounds.SqrDistance(cameraPosition) < Mathf.Pow(tile.Span * CellSize * splitDistance, 2))
            {
                int half = tile.Span / 2;
                for (int z = 0; z < 2; z++)
                    for (int x = 0; x < 2; x++)
                        Subdivide(new OceanTile(tile.X + x * half, tile.Z + z * half, half, tile.Depth + 1));
            }
            else leaves.Add(tile);
        }

        void FillOwners()
        {
            for (int i = 0; i < leaves.Count; i++)
            {
                var t = leaves[i];
                for (int z = t.Z; z < t.Z + t.Span; z++)
                    for (int x = t.X; x < t.X + t.Span; x++) owners[z * GridSize + x] = i;
            }
        }

        void Balance()
        {
            // Balance the whole tree before culling; off-screen neighbours still determine seams.
            while (true)
            {
                FillOwners();
                if (split.Length < leaves.Count) split = new bool[Mathf.NextPowerOfTwo(leaves.Count)];
                Array.Clear(split, 0, leaves.Count);
                bool changed = false;
                for (int i = 0; i < leaves.Count; i++)
                {
                    var t = leaves[i];
                    for (int k = 0; k < t.Span; k++)
                    {
                        changed |= MarkLarge(t.X - 1, t.Z + k, t.Span);
                        changed |= MarkLarge(t.X + t.Span, t.Z + k, t.Span);
                        changed |= MarkLarge(t.X + k, t.Z - 1, t.Span);
                        changed |= MarkLarge(t.X + k, t.Z + t.Span, t.Span);
                    }
                }
                if (!changed) return;
                scratch.Clear();
                for (int i = 0; i < leaves.Count; i++)
                {
                    var t = leaves[i];
                    if (!split[i]) { scratch.Add(t); continue; }
                    int half = t.Span / 2;
                    for (int z = 0; z < 2; z++)
                        for (int x = 0; x < 2; x++)
                            scratch.Add(new OceanTile(t.X + x * half, t.Z + z * half, half, t.Depth + 1));
                }
                leaves.Clear();
                leaves.AddRange(scratch);
            }
        }

        bool MarkLarge(int x, int z, int neighbourSpan)
        {
            int id = GetOwner(x, z);
            if (id < 0 || leaves[id].Span <= neighbourSpan * 2) return false;
            split[id] = true;
            return true;
        }

        bool HasLargerNeighbour(OceanTile tile, OceanSeam edge)
        {
            int x = tile.X, z = tile.Z;
            switch (edge)
            {
                case OceanSeam.Left: x--; break;
                case OceanSeam.Right: x += tile.Span; break;
                case OceanSeam.Bottom: z--; break;
                case OceanSeam.Top: z += tile.Span; break;
            }
            int id = GetOwner(x, z);
            return id >= 0 && leaves[id].Span > tile.Span;
        }

        public int GetOwner(int x, int z) => x < 0 || z < 0 || x >= GridSize || z >= GridSize ? -1 : owners[z * GridSize + x];

        public Vector3 GetTileCenter(OceanTile tile)
        {
            return Center + new Vector3((tile.X + tile.Span * 0.5f) * CellSize - RootSize * 0.5f,
                0, (tile.Z + tile.Span * 0.5f) * CellSize - RootSize * 0.5f);
        }

        public Bounds GetBounds(OceanTile tile, float horizontalDisplacement, float verticalDisplacement)
        {
            float size = tile.Span * CellSize + 2 * Mathf.Max(0, horizontalDisplacement);
            return new Bounds(GetTileCenter(tile), new Vector3(size, Mathf.Max(0.1f, 2 * verticalDisplacement), size));
        }
    }
}
