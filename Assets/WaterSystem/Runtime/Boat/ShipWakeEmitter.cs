using System.Collections.Generic;
using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>Provides hull motion samples for the persistent water-surface wake.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipSectionBuoyancy), typeof(ShipMotor), typeof(Rigidbody))]
    [AddComponentMenu("Water System/Ship Wake Emitter")]
    public sealed class ShipWakeEmitter : MonoBehaviour
    {
        internal static readonly List<ShipWakeEmitter> Active = new List<ShipWakeEmitter>();

        [Header("Foam")]
        [Min(0)] public float SideFoamScale = 0.72f;
        [Min(0)] public float BowFoamScale = 1.1f;
        [Min(0)] public float SternFoamScale = 1.2f;
        [Min(0.1f)] public float ForwardSpeedReference = 4.8f;
        [Min(0.1f)] public float SideRadius = 0.7f;
        [Min(0.1f)] public float BowRadius = 1.15f;
        [Min(0.1f)] public float SternRadius = 1.1f;
        [Min(0)] public float MinimumSubmergedVolume = 0.1f;

        ShipSectionBuoyancy buoyancy;
        ShipMotor motor;
        Rigidbody body;

        void Awake()
        {
            buoyancy = GetComponent<ShipSectionBuoyancy>();
            motor = GetComponent<ShipMotor>();
            body = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            if (buoyancy == null) buoyancy = GetComponent<ShipSectionBuoyancy>();
            if (motor == null) motor = GetComponent<ShipMotor>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (!Active.Contains(this)) Active.Add(this);
        }

        void OnDisable() => Active.Remove(this);

        internal void AppendWakeSplats(List<WakeSplat> output)
        {
            if (buoyancy == null || motor == null || body == null ||
                buoyancy.CurrentSubmergedVolume < MinimumSubmergedVolume) return;

            float speed = body.linearVelocity.magnitude;
            float speed01 = Mathf.Clamp01(speed / Mathf.Max(ForwardSpeedReference, 0.1f));
            float throttle = Mathf.Abs(motor.CurrentThrottle);
            float energy = Mathf.Max(speed01 * speed01, throttle * 0.18f);
            if (energy < 0.012f) return;

            int sections = buoyancy.WakeSectionCount;
            for (int i = 0; i < sections; i++)
            {
                float beam = buoyancy.GetWakeHalfBeam(i);
                if (beam <= 0) continue;
                Vector3 port = buoyancy.GetWakeSectionPoint(i, -1);
                Vector3 starboard = buoyancy.GetWakeSectionPoint(i, 1);
                float portFlow = body.GetPointVelocity(port).magnitude / Mathf.Max(ForwardSpeedReference, 0.1f);
                float starboardFlow = body.GetPointVelocity(starboard).magnitude / Mathf.Max(ForwardSpeedReference, 0.1f);
                float baseStrength = SideFoamScale * energy;
                output.Add(new WakeSplat(port, SideRadius, baseStrength * Mathf.Clamp(0.4f + portFlow, 0.4f, 1.5f)));
                output.Add(new WakeSplat(starboard, SideRadius, baseStrength * Mathf.Clamp(0.4f + starboardFlow, 0.4f, 1.5f)));
            }

            Vector3 bow = transform.TransformPoint(new Vector3(0, 0, buoyancy.WakeBowZ));
            Vector3 stern = transform.TransformPoint(new Vector3(0, 0, buoyancy.WakeSternZ));
            output.Add(new WakeSplat(bow, BowRadius, BowFoamScale * energy));
            float propWash = Mathf.Max(energy, throttle * 0.8f);
            output.Add(new WakeSplat(stern, SternRadius, SternFoamScale * propWash));
        }
    }
}
