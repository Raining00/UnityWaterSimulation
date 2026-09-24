using UnityEngine;

namespace WaterSystem.Ocean
{
    /// <summary>
    /// Horizontal boat maneuvering. ShipSectionBuoyancy owns heave and hydrostatic
    /// restoring forces; this component only applies propulsion and in-plane drag.
    /// Local +Z is the bow, +X is starboard.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(ShipSectionBuoyancy))]
    [AddComponentMenu("Water System/Ship Motor")]
    public sealed class ShipMotor : MonoBehaviour
    {
        [Header("Engine")]
        [SerializeField, Min(0)] float forwardThrust = 24000f;
        [SerializeField, Min(0)] float reverseThrust = 10500f;
        [SerializeField, Min(0)] float forwardThrottleRate = 0.5f;
        [SerializeField, Min(0)] float reverseThrottleRate = 0.35f;
        [SerializeField, Min(0)] float neutralThrottleRate = 0.9f;
        [SerializeField] Vector3 propellerLocalPosition = new(0, -0.55f, -3.35f);

        [Header("Hull resistance")]
        [SerializeField, Min(0)] float surgeLinearDrag = 900f;
        [SerializeField, Min(0)] float surgeQuadraticDrag = 850f;
        [SerializeField, Min(0)] float swayLinearDrag = 14000f;
        [SerializeField, Min(0)] float swayQuadraticDrag = 5000f;
        [SerializeField] Vector3 bowDragLocalPosition = new(0, -0.2f, 2.65f);
        [SerializeField] Vector3 sternDragLocalPosition = new(0, -0.2f, -2.65f);

        [Header("Rudder")]
        [SerializeField, Range(0, 45)] float maximumRudderAngle = 30f;
        [SerializeField, Min(0)] float rudderRateDegrees = 65f;
        [SerializeField, Min(0)] float rudderLift = 300f;
        [SerializeField, Min(0)] float propWashSpeed = 3f;
        [SerializeField] Vector3 rudderLocalPosition = new(0, -0.55f, -3.1f);
        [SerializeField, Min(0)] float minimumSubmergedVolume = 0.1f;

        Rigidbody body;
        ShipSectionBuoyancy buoyancy;
        float requestedThrottle;
        float requestedRudder;
        float engineThrottle;
        float rudderAngle;

        public float CurrentThrottle => engineThrottle;
        public float CurrentRudderAngle => rudderAngle;
        public float ForwardSpeed { get; private set; }

        /// <summary>Supply normalized controls from any input device or AI pilot.</summary>
        public void SetControls(float throttle, float rudder)
        {
            requestedThrottle = Mathf.Clamp(throttle, -1f, 1f);
            requestedRudder = Mathf.Clamp(rudder, -1f, 1f);
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            buoyancy = GetComponent<ShipSectionBuoyancy>();
        }

        void OnDisable()
        {
            SetControls(0, 0);
            engineThrottle = 0;
            rudderAngle = 0;
        }

        void OnValidate()
        {
            forwardThrust = Mathf.Max(0, forwardThrust);
            reverseThrust = Mathf.Max(0, reverseThrust);
            forwardThrottleRate = Mathf.Max(0, forwardThrottleRate);
            reverseThrottleRate = Mathf.Max(0, reverseThrottleRate);
            neutralThrottleRate = Mathf.Max(0, neutralThrottleRate);
            surgeLinearDrag = Mathf.Max(0, surgeLinearDrag);
            surgeQuadraticDrag = Mathf.Max(0, surgeQuadraticDrag);
            swayLinearDrag = Mathf.Max(0, swayLinearDrag);
            swayQuadraticDrag = Mathf.Max(0, swayQuadraticDrag);
            rudderRateDegrees = Mathf.Max(0, rudderRateDegrees);
            rudderLift = Mathf.Max(0, rudderLift);
            propWashSpeed = Mathf.Max(0, propWashSpeed);
            minimumSubmergedVolume = Mathf.Max(0, minimumSubmergedVolume);
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic || buoyancy == null) return;
            float dt = Time.fixedDeltaTime;
            float throttleRate = requestedThrottle == 0 ||
                (engineThrottle != 0 && Mathf.Sign(requestedThrottle) != Mathf.Sign(engineThrottle))
                ? neutralThrottleRate
                : requestedThrottle > 0 ? forwardThrottleRate : reverseThrottleRate;
            engineThrottle = Mathf.MoveTowards(engineThrottle, requestedThrottle, throttleRate * dt);
            rudderAngle = Mathf.MoveTowards(rudderAngle,
                requestedRudder * maximumRudderAngle, rudderRateDegrees * dt);

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) return;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            ForwardSpeed = Vector3.Dot(body.linearVelocity, forward);

            if (!buoyancy.isActiveAndEnabled ||
                buoyancy.CurrentSubmergedVolume < minimumSubmergedVolume) return;

            float thrust = engineThrottle >= 0
                ? engineThrottle * forwardThrust : engineThrottle * reverseThrust;
            if (Mathf.Abs(thrust) > 0.01f)
                body.AddForceAtPosition(forward * thrust,
                    transform.TransformPoint(propellerLocalPosition), ForceMode.Force);

            // The equilibrium speed emerges from thrust balancing water resistance.
            float surgeForce = -(surgeLinearDrag * ForwardSpeed +
                surgeQuadraticDrag * ForwardSpeed * Mathf.Abs(ForwardSpeed));
            body.AddForce(forward * surgeForce, ForceMode.Force);

            // Separate bow/stern side forces damp both sideways translation and yaw.
            ApplySwayDrag(bowDragLocalPosition, right);
            ApplySwayDrag(sternDragLocalPosition, right);

            // Rudder lift changes sign when reversing. Prop wash allows some steering
            // before the hull has gained speed; no artificial yaw torque is imposed.
            Vector3 rudderPoint = transform.TransformPoint(rudderLocalPosition);
            float flow = Vector3.Dot(body.GetPointVelocity(rudderPoint), forward) +
                engineThrottle * propWashSpeed;
            float rudderForce = -Mathf.Sin(rudderAngle * Mathf.Deg2Rad) *
                rudderLift * flow * Mathf.Abs(flow);
            body.AddForceAtPosition(right * rudderForce, rudderPoint, ForceMode.Force);
        }

        void ApplySwayDrag(Vector3 localPosition, Vector3 right)
        {
            Vector3 point = transform.TransformPoint(localPosition);
            float speed = Vector3.Dot(body.GetPointVelocity(point), right);
            float force = -0.5f * (swayLinearDrag * speed +
                swayQuadraticDrag * speed * Mathf.Abs(speed));
            body.AddForceAtPosition(right * force, point, ForceMode.Force);
        }
    }
}
