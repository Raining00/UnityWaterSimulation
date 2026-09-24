using System;
using System.Collections.Generic;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Buoyancy for a longitudinally sectioned, closed hull proxy.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Water System/Ship Section Buoyancy")]
    public sealed class ShipSectionBuoyancy : MonoBehaviour, IOceanSurfaceQueryClient
    {
        [Serializable] public sealed class SectionFile { public HullStation[] stations; }
        [Serializable] public sealed class HullStation
        {
            public float z;
            // Local boat X is beam, Y is up, Z points toward the bow.
            public Vector2[] outline;
        }

        [SerializeField] OceanRenderer ocean;
        [SerializeField] TextAsset hullSections;
        [SerializeField, Min(0)] float waterDensity = 1000f;
        [SerializeField, Min(0)] float verticalDamping = 0.3f;
        [SerializeField] bool drawSections = true;

        HullStation[] stations = Array.Empty<HullStation>();
        float[] halfBeams = Array.Empty<float>();
        float[] integrationWidths = Array.Empty<float>();
        OceanSurfaceQueryResult[] latestResults = Array.Empty<OceanSurfaceQueryResult>();
        readonly List<Vector2> clipped = new(32);
        readonly List<Vector2> scratch = new(32);
        Rigidbody body;
        int version;
        long latestRequestId = -1;

        public int SamplePointCount => stations.Length * 2;
        public float CurrentSubmergedVolume { get; private set; }
        MonoBehaviour IOceanSurfaceQueryClient.QueryBehaviour => this;
        OceanRenderer IOceanSurfaceQueryClient.Ocean => ocean;
        int IOceanSurfaceQueryClient.QueryVersion => version;

        public void Configure(OceanRenderer owningOcean, TextAsset sectionFile)
        {
            ocean = owningOcean;
            hullSections = sectionFile;
            LoadSections();
        }

        void Awake() => body = GetComponent<Rigidbody>();

        void OnEnable()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            LoadSections();
            if (!Application.isPlaying) return;
            if (ocean == null) ocean = FindAnyObjectByType<OceanRenderer>();
            ocean?.SurfaceQueries?.Register(this);
        }

        void OnDisable()
        {
            ocean?.SurfaceQueries?.Unregister(this);
            ClearSurfaceResults();
        }

        void OnValidate()
        {
            waterDensity = Mathf.Max(0, waterDensity);
            verticalDamping = Mathf.Max(0, verticalDamping);
            if (!Application.isPlaying) LoadSections();
        }

        void LoadSections()
        {
            ClearSurfaceResults();
            stations = Array.Empty<HullStation>();
            halfBeams = integrationWidths = Array.Empty<float>();
            if (hullSections == null) return;
            SectionFile parsed;
            try { parsed = JsonUtility.FromJson<SectionFile>(hullSections.text); }
            catch (Exception error)
            {
                Debug.LogError($"Invalid hull section JSON: {error.Message}", this);
                return;
            }
            if (parsed?.stations == null || parsed.stations.Length < 2) return;
            for (int i = 0; i < parsed.stations.Length; i++)
            {
                var station = parsed.stations[i];
                if (station?.outline == null || station.outline.Length < 3 ||
                    (i > 0 && station.z <= parsed.stations[i - 1].z))
                {
                    Debug.LogError("Hull stations must have closed outlines with at least 3 points and strictly increasing Z.", this);
                    return;
                }
            }
            stations = parsed.stations;
            halfBeams = new float[stations.Length];
            integrationWidths = new float[stations.Length];
            for (int i = 0; i < stations.Length; i++)
            {
                foreach (var point in stations[i].outline)
                    halfBeams[i] = Mathf.Max(halfBeams[i], Mathf.Abs(point.x));
                float before = i == 0 ? 0 : stations[i].z - stations[i - 1].z;
                float after = i == stations.Length - 1 ? 0 : stations[i + 1].z - stations[i].z;
                integrationWidths[i] = 0.5f * (before + after);
            }
        }

        public Vector3 GetWorldSamplePoint(int index)
        {
            int station = index / 2;
            if ((uint)station >= (uint)stations.Length) throw new ArgumentOutOfRangeException(nameof(index));
            float x = (index & 1) == 0 ? -halfBeams[station] : halfBeams[station];
            return transform.TransformPoint(new Vector3(x, 0, stations[station].z));
        }

        void IOceanSurfaceQueryClient.AcceptSurfaceResults(OceanSurfaceQueryResult[] source,
            int start, int count, long requestId, double simulationTime, int expectedVersion)
        {
            if (expectedVersion != version || count != SamplePointCount || requestId <= latestRequestId) return;
            if (latestResults.Length != count) latestResults = new OceanSurfaceQueryResult[count];
            Array.Copy(source, start, latestResults, 0, count);
            latestRequestId = requestId;
        }

        void IOceanSurfaceQueryClient.ClearSurfaceResults() => ClearSurfaceResults();

        void ClearSurfaceResults()
        {
            unchecked { version++; }
            latestRequestId = -1;
            latestResults = Array.Empty<OceanSurfaceQueryResult>();
            CurrentSubmergedVolume = 0;
        }

        void FixedUpdate()
        {
            CurrentSubmergedVolume = 0;
            if (body == null || body.isKinematic || latestRequestId < 0 ||
                latestResults.Length != SamplePointCount || waterDensity <= 0) return;
            Matrix4x4 toWorld = transform.localToWorldMatrix;
            float scale = Mathf.Abs(toWorld.determinant);
            if (scale <= 0 || Physics.gravity.sqrMagnitude <= 0) return;
            for (int i = 0; i < stations.Length; i++)
            {
                float beam = halfBeams[i];
                if (beam <= 0 || integrationWidths[i] <= 0) continue;
                float portHeight = latestResults[2 * i].Position.y;
                float starboardHeight = latestResults[2 * i + 1].Position.y;
                if (!TrySubmergedSection(stations[i], beam, portHeight, starboardHeight,
                    toWorld, out float localArea, out Vector2 center)) continue;
                float volume = localArea * integrationWidths[i] * scale;
                CurrentSubmergedVolume += volume;
                Vector3 forcePoint = toWorld.MultiplyPoint3x4(
                    new Vector3(center.x, center.y, stations[i].z));
                Vector3 buoyancy = -Physics.gravity * (waterDensity * volume);
                // Only heave damping here. Forward/side resistance belongs to a separate hull-drag model.
                float heaveSpeed = Vector3.Dot(body.GetPointVelocity(forcePoint), -Physics.gravity.normalized);
                Vector3 damping = Physics.gravity.normalized *
                    (heaveSpeed * waterDensity * volume * verticalDamping);
                body.AddForceAtPosition(buoyancy + damping, forcePoint, ForceMode.Force);
            }
        }

        bool TrySubmergedSection(HullStation station, float halfBeam,
            float portHeight, float starboardHeight, Matrix4x4 toWorld,
            out float area, out Vector2 centroid)
        {
            area = 0;
            centroid = default;
            clipped.Clear();
            scratch.Clear();
            scratch.AddRange(station.outline);
            for (int i = 0; i < scratch.Count; i++)
            {
                Vector2 a = scratch[i];
                Vector2 b = scratch[(i + 1) % scratch.Count];
                float da = SignedDistance(a);
                float db = SignedDistance(b);
                bool insideA = da <= 0;
                bool insideB = db <= 0;
                if (insideA) clipped.Add(a);
                if (insideA != insideB)
                    clipped.Add(Vector2.Lerp(a, b, Mathf.Clamp01(da / (da - db))));
            }
            if (clipped.Count < 3) return false;
            float twiceArea = 0;
            Vector2 moment = Vector2.zero;
            for (int i = 0; i < clipped.Count; i++)
            {
                Vector2 a = clipped[i];
                Vector2 b = clipped[(i + 1) % clipped.Count];
                float cross = a.x * b.y - b.x * a.y;
                twiceArea += cross;
                moment += (a + b) * cross;
            }
            if (Mathf.Abs(twiceArea) < 0.000001f) return false;
            centroid = moment / (3f * twiceArea);
            area = 0.5f * Mathf.Abs(twiceArea);
            return true;

            float SignedDistance(Vector2 point)
            {
                float waterY = Mathf.Lerp(portHeight, starboardHeight,
                    Mathf.Clamp01((point.x + halfBeam) / (2f * halfBeam)));
                return toWorld.MultiplyPoint3x4(new Vector3(point.x, point.y, station.z)).y - waterY;
            }
        }

        void OnDrawGizmosSelected()
        {
            if (!drawSections) return;
            if (stations.Length == 0) LoadSections();
            Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.8f);
            foreach (var station in stations)
            {
                var outline = station.outline;
                for (int i = 0; i < outline.Length; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Length];
                    Gizmos.DrawLine(transform.TransformPoint(new Vector3(a.x, a.y, station.z)),
                        transform.TransformPoint(new Vector3(b.x, b.y, station.z)));
                }
            }
        }
    }
}
