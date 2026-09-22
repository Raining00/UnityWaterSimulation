using UnityEngine;
using UnityEngine.Rendering;

namespace WaterSystem.Ocean
{
    /// <summary>
    /// Immutable view of the GPU resources needed to query the current ocean surface.
    /// The provider owns these resources; consumers may use them while recording commands,
    /// but must not release them or retain them across provider reinitialization.
    /// </summary>
    public readonly struct OceanSurfaceQueryResources
    {
        public readonly RenderTexture Displacement;
        public readonly RenderTexture Normal;
        public readonly Vector4 DomainSizes;
        public readonly int CascadeCount;
        public readonly int Resolution;
        public readonly float SeaLevel;
        public readonly double SimulationTime;

        public bool IsValid =>
            Displacement != null && Displacement.IsCreated() &&
            Normal != null && Normal.IsCreated() &&
            CascadeCount > 0 && Resolution > 0;

        public OceanSurfaceQueryResources(
            RenderTexture displacement,
            RenderTexture normal,
            Vector4 domainSizes,
            int cascadeCount,
            int resolution,
            float seaLevel,
            double simulationTime)
        {
            Displacement = displacement;
            Normal = normal;
            DomainSizes = domainSizes;
            CascadeCount = cascadeCount;
            Resolution = resolution;
            SeaLevel = seaLevel;
            SimulationTime = simulationTime;
        }
    }

    public readonly struct OceanSimulationContext
    {
        public readonly OceanRenderer Ocean;
        public OceanSimulationContext(OceanRenderer ocean) { Ocean = ocean; }
        public float SeaLevel => Ocean.transform.position.y;
    }

    public readonly struct OceanSimulationFrame
    {
        public readonly double Time;
        public readonly float DeltaTime;
        public readonly int FrameIndex;
        public OceanSimulationFrame(double time, float deltaTime, int frameIndex)
        { Time = time; DeltaTime = deltaTime; FrameIndex = frameIndex; }
    }

    /// <summary>
    /// Optional FFT adapter. One provider instance belongs to one ocean.
    /// Initialize allocates spectra/textures; RecordSimulation records GPU work once per rendered frame;
    /// BindResources supplies textures/parameters per camera; Release frees all owned resources.
    /// Implementations must tolerate Release after partial initialization. No FFT is required by the geometry.
    /// </summary>
    public abstract class OceanSimulationProvider : MonoBehaviour
    {
        [Tooltip("Explicitly opt in only when the provider supports edit-mode resource allocation and updates.")]
        public bool RunInEditMode;

        // X = maximum horizontal displacement in either X/Z; Y = maximum absolute vertical displacement.
        public virtual Vector2 MaximumDisplacement => Vector2.zero;
        public abstract void Initialize(in OceanSimulationContext context);
        public abstract void RecordSimulation(CommandBuffer commands, in OceanSimulationFrame frame);
        /// <summary>
        /// Exposes only the current simulation resources required by surface queries. Their producer
        /// commands may still be pending in the same command buffer when this method is called.
        /// Returning false means this provider does not support GPU surface queries yet.
        /// </summary>
        public virtual bool TryGetSurfaceQueryResources(out OceanSurfaceQueryResources resources)
        {
            resources = default;
            return false;
        }
        public abstract void BindResources(MaterialPropertyBlock properties, Camera camera);
        public abstract void Release();
    }
}
