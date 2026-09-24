using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

namespace WaterSystem.Ocean
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("Water System/Bouyant Body")]
    [ExecuteAlways]
    public sealed class BouyantBody : MonoBehaviour, IOceanSurfaceQueryClient
    {
        // For now directly resign which ocean this object belongs to in Unity Editor. This finally need to calculate which ocean this object belongs to by calculating.
        [SerializeField] OceanRenderer ocean;

        [Header("Buoyancy sample points")]
        [Tooltip("Number of coarse water-query voxels along the body's local X/Y/Z bounds.")]
        [SerializeField] Vector3Int samplesPerAxis = new Vector3Int(3, 8, 3);
        [Tooltip("Geometry subdivisions inside each coarse voxel. Only occupied voxels request GPU water samples.")]
        [SerializeField] Vector3Int geometrySubdivisions = new Vector3Int(4, 8, 4);
        [Tooltip("Occupancy tests along each axis of a geometry subcell. Higher values improve curved collider boundaries.")]
        [SerializeField, Range(1, 3)] int occupancySamplesPerAxis = 2;
        [Tooltip("Allow trigger colliders to contribute to the buoyancy volume.")]
        [SerializeField] bool includeTriggerColliders;
        [Tooltip("World-space tolerance used when checking whether a voxel center is inside a collider.")]
        [SerializeField, Min(0.000001f)] float insideTolerance = 0.0001f;
        [Header("Buoyancy forces")]
        [Tooltip("Mass per cubic meter of displaced water. A value of 1000 approximates fresh water.")]
        [SerializeField, Min(0f)] float waterDensity = 1000f;
        [Tooltip("Velocity damping per second, scaled by displaced water mass. Helps the body settle instead of bouncing at the surface.")]
        [SerializeField, Min(0f)] float waterDrag = 4f;
        [Tooltip("Fraction of one voxel volume assigned to the single fallback point when no voxel center lies inside a collider.")]
        [SerializeField, Range(0f, 1f)] float fallbackVolumeFraction = 0.25f;
        [SerializeField, HideInInspector] Vector3[] localSamplePoints = Array.Empty<Vector3>();
        [SerializeField, HideInInspector] Bounds localSamplingBounds;
        [SerializeField, HideInInspector] Vector3 localSampleCellSize;
        [ReadOnly(true), SerializeField] float localSampleVolume;
        [Tooltip("Estimated local-space volume occupied by the colliders; scales with the transform at runtime.")]
        [ReadOnly(true), SerializeField] float estimatedLocalVolume;

        readonly struct OccupiedSubcell
        {
            public readonly Vector3 LocalCenter;
            public readonly float LocalVolume;

            public OccupiedSubcell(Vector3 localCenter, float localVolume)
            {
                LocalCenter = localCenter;
                LocalVolume = localVolume;
            }
        }

        Rigidbody _rigidbody;
        Collider[] _colliders = Array.Empty<Collider>();
        OceanSurfaceQueryResult[] latestSurfaceResults = Array.Empty<OceanSurfaceQueryResult>();
        OccupiedSubcell[] occupiedSubcells = Array.Empty<OccupiedSubcell>();
        int[] subcellStarts = Array.Empty<int>();
        int[] subcellCounts = Array.Empty<int>();
        float[] localVoxelVolumes = Array.Empty<float>();
        Vector3[] localVoxelCenters = Array.Empty<Vector3>();
        Vector3 localSubcellSize;
        int queryVersion;

        public int SamplePointCount => localSamplePoints?.Length ?? 0;
        public IReadOnlyList<Vector3> LocalSamplePoints => localSamplePoints;
        public float EstimatedDisplacementVolume =>
            estimatedLocalVolume * Mathf.Abs(transform.localToWorldMatrix.determinant);
        internal OceanRenderer Ocean => ocean;
        internal int QueryVersion => queryVersion;
        MonoBehaviour IOceanSurfaceQueryClient.QueryBehaviour => this;
        OceanRenderer IOceanSurfaceQueryClient.Ocean => ocean;
        int IOceanSurfaceQueryClient.QueryVersion => queryVersion;
        public long LatestSurfaceRequestId { get; private set; } = -1;
        public double LatestSurfaceSimulationTime { get; private set; }

        /// <summary>Returns the latest completed GPU samples in local sample-point order.</summary>
        public bool TryGetSurfaceResults(out IReadOnlyList<OceanSurfaceQueryResult> results)
        {
            results = latestSurfaceResults;
            return LatestSurfaceRequestId >= 0 && latestSurfaceResults.Length == SamplePointCount;
        }

        internal void AcceptSurfaceResults(OceanSurfaceQueryResult[] source, int start, int count,
            long requestId, double simulationTime, int expectedVersion)
        {
            if (queryVersion != expectedVersion || count != SamplePointCount || requestId <= LatestSurfaceRequestId)
                return;
            if (latestSurfaceResults.Length != count)
                latestSurfaceResults = new OceanSurfaceQueryResult[count];
            Array.Copy(source, start, latestSurfaceResults, 0, count);
            LatestSurfaceRequestId = requestId;
            LatestSurfaceSimulationTime = simulationTime;
        }

        void IOceanSurfaceQueryClient.AcceptSurfaceResults(OceanSurfaceQueryResult[] source,
            int start, int count, long requestId, double simulationTime, int expectedVersion) =>
            AcceptSurfaceResults(source, start, count, requestId, simulationTime, expectedVersion);

        void InvalidateSurfaceResults()
        {
            unchecked { queryVersion++; }
            latestSurfaceResults = Array.Empty<OceanSurfaceQueryResult>();
            LatestSurfaceRequestId = -1;
            LatestSurfaceSimulationTime = 0;
        }

        internal void ClearSurfaceResults() => InvalidateSurfaceResults();
        void IOceanSurfaceQueryClient.ClearSurfaceResults() => ClearSurfaceResults();

        void Reset() => GenerateSamplePoints();

        void OnValidate()
        {
            samplesPerAxis.x = Mathf.Clamp(samplesPerAxis.x, 1, 16);
            samplesPerAxis.y = Mathf.Clamp(samplesPerAxis.y, 1, 16);
            samplesPerAxis.z = Mathf.Clamp(samplesPerAxis.z, 1, 16);
            geometrySubdivisions.x = Mathf.Clamp(geometrySubdivisions.x, 1, 8);
            geometrySubdivisions.y = Mathf.Clamp(geometrySubdivisions.y, 1, 12);
            geometrySubdivisions.z = Mathf.Clamp(geometrySubdivisions.z, 1, 8);
            occupancySamplesPerAxis = Mathf.Clamp(occupancySamplesPerAxis, 1, 3);
            insideTolerance = Mathf.Max(0.000001f, insideTolerance);
            waterDensity = Mathf.Max(0f, waterDensity);
            waterDrag = Mathf.Max(0f, waterDrag);
            fallbackVolumeFraction = Mathf.Clamp01(fallbackVolumeFraction);
            GenerateSamplePoints();
        }

        void CacheComponents()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

            var candidates = GetComponentsInChildren<Collider>(true);
            var owned = new List<Collider>(candidates.Length);
            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.enabled) continue;
                if (!includeTriggerColliders && candidate.isTrigger) continue;

                // A nested Rigidbody owns its own colliders and must generate its own sample set.
                var attachedBody = candidate.attachedRigidbody;
                if (attachedBody != null && attachedBody != _rigidbody) continue;
                owned.Add(candidate);
            }
            _colliders = owned.ToArray();
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying || _rigidbody == null || _rigidbody.isKinematic)
                return;
            if (!TryGetSurfaceResults(out var results) || results.Count == 0 ||
                subcellStarts.Length != results.Count)
                return;

            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            float volumeScale = Mathf.Abs(localToWorld.determinant);
            Vector3 gravity = Physics.gravity;
            if (volumeScale <= 0f || waterDensity <= 0f || gravity.sqrMagnitude <= 0f)
                return;

            Vector3 voxelEdgeX = localToWorld.MultiplyVector(new Vector3(localSampleCellSize.x, 0f, 0f));
            Vector3 voxelEdgeY = localToWorld.MultiplyVector(new Vector3(0f, localSampleCellSize.y, 0f));
            Vector3 voxelEdgeZ = localToWorld.MultiplyVector(new Vector3(0f, 0f, localSampleCellSize.z));
            Vector3 subcellEdgeX = localToWorld.MultiplyVector(new Vector3(localSubcellSize.x, 0f, 0f));
            Vector3 subcellEdgeY = localToWorld.MultiplyVector(new Vector3(0f, localSubcellSize.y, 0f));
            Vector3 subcellEdgeZ = localToWorld.MultiplyVector(new Vector3(0f, 0f, localSubcellSize.z));
            float maxDampingPerPoint = _rigidbody.mass /
                (Mathf.Max(Time.fixedDeltaTime, 0.000001f) * results.Count);

            for (int i = 0; i < results.Count; i++)
            {
                Vector3 normal = results[i].Normal;
                if (normal.y <= 0.15f || normal.sqrMagnitude < 0.25f)
                    normal = Vector3.up;
                else
                    normal.Normalize();

                Vector3 surface = results[i].Position;
                Vector3 voxelCenter = localToWorld.MultiplyPoint3x4(localSamplePoints[i]);
                Vector3 geometricCenter = localToWorld.MultiplyPoint3x4(localVoxelCenters[i]);
                float signedDistance = Vector3.Dot(geometricCenter - surface, normal);
                float voxelHalfThickness = 0.5f * (
                    Mathf.Abs(Vector3.Dot(normal, voxelEdgeX)) +
                    Mathf.Abs(Vector3.Dot(normal, voxelEdgeY)) +
                    Mathf.Abs(Vector3.Dot(normal, voxelEdgeZ)));

                if (signedDistance >= voxelHalfThickness)
                    continue;

                float submergedLocalVolume;
                Vector3 buoyancyCenter;
                if (signedDistance <= -voxelHalfThickness)
                {
                    // Entire coarse voxel is below the local water plane. Its occupied
                    // subcells are already summarized by volume and centroid.
                    submergedLocalVolume = localVoxelVolumes[i];
                    buoyancyCenter = voxelCenter;
                }
                else
                {
                    float subcellHalfThickness = 0.5f * (
                        Mathf.Abs(Vector3.Dot(normal, subcellEdgeX)) +
                        Mathf.Abs(Vector3.Dot(normal, subcellEdgeY)) +
                        Mathf.Abs(Vector3.Dot(normal, subcellEdgeZ)));
                    if (subcellHalfThickness <= 0.000001f)
                        continue;

                    submergedLocalVolume = 0f;
                    Vector3 weightedCenter = Vector3.zero;
                    int end = subcellStarts[i] + subcellCounts[i];
                    for (int j = subcellStarts[i]; j < end; j++)
                    {
                        OccupiedSubcell subcell = occupiedSubcells[j];
                        Vector3 center = localToWorld.MultiplyPoint3x4(subcell.LocalCenter);
                        float subcellDistance = Vector3.Dot(center - surface, normal);
                        float fraction = Mathf.Clamp01(0.5f -
                            subcellDistance / (2f * subcellHalfThickness));
                        float volume = subcell.LocalVolume * fraction;
                        submergedLocalVolume += volume;
                        weightedCenter += center * volume;
                    }
                    if (submergedLocalVolume <= 0f)
                        continue;
                    buoyancyCenter = weightedCenter / submergedLocalVolume;
                }

                float submergedWorldVolume = submergedLocalVolume * volumeScale;
                Vector3 buoyancy = -gravity * (waterDensity * submergedWorldVolume);
                float damping = Mathf.Min(
                    waterDensity * submergedWorldVolume * waterDrag,
                    maxDampingPerPoint);
                Vector3 waterResistance = -_rigidbody.GetPointVelocity(buoyancyCenter) * damping;
                _rigidbody.AddForceAtPosition(
                    buoyancy + waterResistance, buoyancyCenter, ForceMode.Force);
            }
        }

        /// <summary>
        /// Builds collider-backed voxel centers and stores them in this body's local space.
        /// This is an authoring/initialization operation, not something to call every physics step.
        /// </summary>
        [ContextMenu("Regenerate Buoyancy Sample Points")]
        public void GenerateSamplePoints()
        {
            InvalidateSurfaceResults();
            CacheComponents();
            if (_colliders.Length == 0)
            {
                localSamplingBounds = default;
                ClearGeneratedSamples();
                return;
            }

            if (!TryBuildLocalBounds(out localSamplingBounds))
            {
                ClearGeneratedSamples();
                return;
            }

            int countX = Mathf.Max(1, samplesPerAxis.x);
            int countY = Mathf.Max(1, samplesPerAxis.y);
            int countZ = Mathf.Max(1, samplesPerAxis.z);
            int subX = Mathf.Clamp(geometrySubdivisions.x, 1, 8);
            int subY = Mathf.Clamp(geometrySubdivisions.y, 1, 12);
            int subZ = Mathf.Clamp(geometrySubdivisions.z, 1, 8);
            int coverage = Mathf.Clamp(occupancySamplesPerAxis, 1, 3);
            long coverageTests = (long)countX * countY * countZ * subX * subY * subZ *
                coverage * coverage * coverage;
            if (coverageTests > 1000000)
            {
                Debug.LogWarning("Buoyancy voxel settings exceed one million collider samples; reduce the voxel or geometry resolution.", this);
                ClearGeneratedSamples();
                return;
            }

            Vector3 cellSize = new Vector3(
                localSamplingBounds.size.x / countX,
                localSamplingBounds.size.y / countY,
                localSamplingBounds.size.z / countZ);
            localSampleCellSize = cellSize;
            localSampleVolume = cellSize.x * cellSize.y * cellSize.z;
            localSubcellSize = new Vector3(cellSize.x / subX, cellSize.y / subY, cellSize.z / subZ);
            float subcellVolume = localSampleVolume / (subX * subY * subZ);
            float inverseCoverageCount = 1f / (coverage * coverage * coverage);

            var points = new List<Vector3>(countX * countY * countZ);
            var voxelCenters = new List<Vector3>(points.Capacity);
            var voxelVolumes = new List<float>(points.Capacity);
            var starts = new List<int>(points.Capacity);
            var counts = new List<int>(points.Capacity);
            var subcells = new List<OccupiedSubcell>();
            Vector3 minimum = localSamplingBounds.min;
            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            estimatedLocalVolume = 0f;
            for (int z = 0; z < countZ; z++)
            for (int y = 0; y < countY; y++)
            for (int x = 0; x < countX; x++)
            {
                Vector3 voxelMinimum = minimum + new Vector3(
                    x * cellSize.x, y * cellSize.y, z * cellSize.z);
                int start = subcells.Count;
                float occupiedVolume = 0f;
                Vector3 occupiedMoment = Vector3.zero;

                for (int sz = 0; sz < subZ; sz++)
                for (int sy = 0; sy < subY; sy++)
                for (int sx = 0; sx < subX; sx++)
                {
                    Vector3 subMinimum = voxelMinimum + new Vector3(
                        sx * localSubcellSize.x,
                        sy * localSubcellSize.y,
                        sz * localSubcellSize.z);
                    int insideCount = 0;
                    Vector3 insidePositionSum = Vector3.zero;
                    for (int oz = 0; oz < coverage; oz++)
                    for (int oy = 0; oy < coverage; oy++)
                    for (int ox = 0; ox < coverage; ox++)
                    {
                        Vector3 localPoint = subMinimum + new Vector3(
                            (ox + 0.5f) / coverage * localSubcellSize.x,
                            (oy + 0.5f) / coverage * localSubcellSize.y,
                            (oz + 0.5f) / coverage * localSubcellSize.z);
                        if (!IsInsideOwnedCollider(localToWorld.MultiplyPoint3x4(localPoint)))
                            continue;
                        insideCount++;
                        insidePositionSum += localPoint;
                    }

                    if (insideCount == 0) continue;
                    float volume = subcellVolume * insideCount * inverseCoverageCount;
                    Vector3 centroid = insidePositionSum / insideCount;
                    subcells.Add(new OccupiedSubcell(centroid, volume));
                    occupiedVolume += volume;
                    occupiedMoment += centroid * volume;
                }

                if (occupiedVolume <= 0f) continue;
                points.Add(occupiedMoment / occupiedVolume);
                voxelCenters.Add(voxelMinimum + cellSize * 0.5f);
                voxelVolumes.Add(occupiedVolume);
                starts.Add(start);
                counts.Add(subcells.Count - start);
                estimatedLocalVolume += occupiedVolume;
            }

            // Thin colliders can still miss every occupancy test. Retain one
            // conservative force point until the user increases the resolution.
            if (points.Count == 0)
            {
                Vector3 worldPoint = _colliders[0].ClosestPoint(_colliders[0].bounds.center);
                Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
                float volume = localSampleVolume * fallbackVolumeFraction;
                points.Add(localPoint);
                voxelCenters.Add(localPoint);
                voxelVolumes.Add(volume);
                starts.Add(subcells.Count);
                counts.Add(1);
                subcells.Add(new OccupiedSubcell(localPoint, volume));
                estimatedLocalVolume = volume;
            }

            localSamplePoints = points.ToArray();
            localVoxelCenters = voxelCenters.ToArray();
            localVoxelVolumes = voxelVolumes.ToArray();
            subcellStarts = starts.ToArray();
            subcellCounts = counts.ToArray();
            occupiedSubcells = subcells.ToArray();
        }

        void ClearGeneratedSamples()
        {
            localSamplePoints = Array.Empty<Vector3>();
            localVoxelCenters = Array.Empty<Vector3>();
            localVoxelVolumes = Array.Empty<float>();
            subcellStarts = Array.Empty<int>();
            subcellCounts = Array.Empty<int>();
            occupiedSubcells = Array.Empty<OccupiedSubcell>();
            localSampleCellSize = localSubcellSize = default;
            localSampleVolume = estimatedLocalVolume = 0f;
        }

        bool TryBuildLocalBounds(out Bounds bounds)
        {
            bounds = default;
            bool initialized = false;
            foreach (var collider in _colliders)
            {
                Bounds worldBounds = collider.bounds;
                Vector3 center = worldBounds.center;
                Vector3 extents = worldBounds.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldCorner = center + Vector3.Scale(extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1,
                        (corner & 4) == 0 ? -1 : 1));
                    Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                    if (!initialized)
                    {
                        bounds = new Bounds(localCorner, Vector3.zero);
                        initialized = true;
                    }
                    else bounds.Encapsulate(localCorner);
                }
            }
            return initialized;
        }

        bool IsInsideOwnedCollider(Vector3 worldPoint)
        {
            float toleranceSquared = insideTolerance * insideTolerance;
            foreach (var collider in _colliders)
            {
                Vector3 closest = collider.ClosestPoint(worldPoint);
                if ((closest - worldPoint).sqrMagnitude <= toleranceSquared) return true;
            }
            return false;
        }

        public Vector3 GetWorldSamplePoint(int index)
        {
            if ((uint)index >= (uint)SamplePointCount) throw new ArgumentOutOfRangeException(nameof(index));
            return transform.TransformPoint(localSamplePoints[index]);
        }

        private void OnEnable()
        {
            // Regenerate at activation: disabled scene objects can have stale or
            // zeroed serialized sample data after their colliders change in the Editor.
            GenerateSamplePoints();
            if (ocean == null)
                return;

            var queries = ocean.SurfaceQueries;
            if (queries != null)
                queries.Register(this);
            else
                Debug.LogWarning($"{name} could not find OceanSurfaceQuerySystem.", this);
        }

        private void OnDisable()
        {
            InvalidateSurfaceResults();
            if (ocean != null)
                ocean.SurfaceQueries?.Unregister(this);
        }

        void OnDrawGizmosSelected()
        {
            if (localSamplePoints == null || localSamplePoints.Length == 0) return;

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.85f);
            float radius = Mathf.Max(0.015f, localSamplingBounds.size.magnitude * 0.0125f);
            foreach (var point in localSamplePoints) Gizmos.DrawSphere(point, radius);
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.25f);
            Gizmos.DrawWireCube(localSamplingBounds.center, localSamplingBounds.size);
            Gizmos.matrix = previous;

            if (TryGetSurfaceResults(out var results))
            {
                foreach (var result in results)
                {
                    Vector3 surface = result.Position;
                    Vector3 sample = surface - Vector3.up * result.SignedDepth;

                    Gizmos.color = result.SignedDepth > 0f ? Color.cyan : Color.yellow;
                    Gizmos.DrawSphere(surface, radius * 0.6f);
                    Gizmos.DrawLine(sample, surface);

                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(surface, surface + result.Normal * radius * 2f);
                }
            }
        }
    }
}
