using UnityEngine;
using UnityEngine.InputSystem;

namespace WaterSystem.Ocean
{
    /// <summary>
    /// Third-person ship camera. The focus follows the hull position, while the view
    /// uses world up and the hull's horizontal heading so waves never roll the camera.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Water System/Ship Third Person Camera")]
    public sealed class ShipThirdPersonCamera : MonoBehaviour
    {
        [Header("Target and framing")]
        [SerializeField] Transform target;
        [SerializeField, Min(0f)] float focusHeight = 1.5f;
        [SerializeField, Min(0.1f)] float distance = 10f;
        [SerializeField, Min(0.1f)] float minimumDistance = 4f;
        [SerializeField, Min(0.1f)] float maximumDistance = 20f;
        [SerializeField] float pitch = 15f;
        [SerializeField] float minimumPitch = 5f;
        [SerializeField] float maximumPitch = 65f;

        [Header("Follow smoothing")]
        [SerializeField, Min(0.01f)] float horizontalSmoothTime = 0.12f;
        [SerializeField, Min(0.01f)] float verticalSmoothTime = 0.35f;
        [SerializeField, Min(0.01f)] float headingSmoothTime = 0.25f;
        [SerializeField, Min(0.01f)] float pitchSmoothTime = 0.12f;
        [SerializeField, Min(0.01f)] float zoomSmoothTime = 0.15f;
        [SerializeField, Min(0.1f)] float teleportDistance = 30f;

        [Header("Controls")]
        [SerializeField, Min(0f)] float mouseDegreesPerPixel = 0.16f;
        [SerializeField, Min(0f)] float gamepadDegreesPerSecond = 100f;
        [SerializeField, Min(0f)] float scrollDistancePerUnit = 0.015f;
        [SerializeField] bool autoRecenter = true;
        [SerializeField, Min(0f)] float recenterDelay = 2f;
        [SerializeField, Min(0f)] float recenterDegreesPerSecond = 55f;

        Vector3 smoothedFocus;
        Vector2 horizontalVelocity;
        float verticalVelocity;
        float smoothedYaw;
        float yawVelocity;
        float smoothedPitch;
        float pitchVelocity;
        float smoothedDistance;
        float zoomVelocity;
        float heading;
        float yawOffset;
        float idleTime;
        bool initialized;

        public Transform Target => target;

        /// <summary>Switch boats at runtime and place the view behind the new target.</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            yawOffset = 0f;
            if (target != null) SnapToTarget();
            else initialized = false;
        }

        void Awake()
        {
            // Existing scenes may have the camera parented to the boat. Keep that
            // transform as the target, then detach so pitch and roll cannot propagate.
            if (target == null) target = transform.parent;
            if (transform.parent != null) transform.SetParent(null, true);
            if (target != null) SnapToTarget();
            else Debug.LogWarning("Ship Third Person Camera needs a target boat.", this);
        }

        void OnValidate()
        {
            minimumDistance = Mathf.Max(0.1f, minimumDistance);
            maximumDistance = Mathf.Max(minimumDistance, maximumDistance);
            distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
            maximumPitch = Mathf.Max(minimumPitch, maximumPitch);
            pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
            horizontalSmoothTime = Mathf.Max(0.01f, horizontalSmoothTime);
            verticalSmoothTime = Mathf.Max(0.01f, verticalSmoothTime);
            headingSmoothTime = Mathf.Max(0.01f, headingSmoothTime);
            pitchSmoothTime = Mathf.Max(0.01f, pitchSmoothTime);
            zoomSmoothTime = Mathf.Max(0.01f, zoomSmoothTime);
            teleportDistance = Mathf.Max(0.1f, teleportDistance);
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (!initialized) SnapToTarget();

            float dt = Time.deltaTime;
            ReadControls(dt);
            Vector3 desiredFocus = target.position + Vector3.up * focusHeight;
            if ((desiredFocus - smoothedFocus).sqrMagnitude > teleportDistance * teleportDistance)
            {
                smoothedFocus = desiredFocus;
                horizontalVelocity = Vector2.zero;
                verticalVelocity = 0f;
            }

            Vector2 horizontal = Vector2.SmoothDamp(
                new Vector2(smoothedFocus.x, smoothedFocus.z),
                new Vector2(desiredFocus.x, desiredFocus.z),
                ref horizontalVelocity, horizontalSmoothTime, Mathf.Infinity, dt);
            smoothedFocus.x = horizontal.x;
            smoothedFocus.z = horizontal.y;
            smoothedFocus.y = Mathf.SmoothDamp(smoothedFocus.y, desiredFocus.y,
                ref verticalVelocity, verticalSmoothTime, Mathf.Infinity, dt);

            smoothedYaw = Mathf.SmoothDampAngle(smoothedYaw,
                GetHeading() + yawOffset, ref yawVelocity, headingSmoothTime, Mathf.Infinity, dt);
            smoothedPitch = Mathf.SmoothDamp(smoothedPitch, pitch,
                ref pitchVelocity, pitchSmoothTime, Mathf.Infinity, dt);
            smoothedDistance = Mathf.SmoothDamp(smoothedDistance, distance,
                ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);
            PlaceCamera();
        }

        void ReadControls(float dt)
        {
            if (!Application.isFocused) return;
            bool orbiting = false;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yawOffset += delta.x * mouseDegreesPerPixel;
                    pitch -= delta.y * mouseDegreesPerPixel;
                    orbiting = true;
                }
                distance -= mouse.scroll.ReadValue().y * scrollDistancePerUnit;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.sqrMagnitude > 0.01f)
                {
                    yawOffset += stick.x * gamepadDegreesPerSecond * dt;
                    pitch -= stick.y * gamepadDegreesPerSecond * dt;
                    orbiting = true;
                }
            }

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                yawOffset = 0f;
                orbiting = true;
            }

            yawOffset = Mathf.DeltaAngle(0f, yawOffset);
            pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
            distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);
            idleTime = orbiting ? 0f : idleTime + dt;
            if (autoRecenter && !orbiting && idleTime >= recenterDelay)
                yawOffset = Mathf.MoveTowards(yawOffset, 0f, recenterDegreesPerSecond * dt);
        }

        float GetHeading()
        {
            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.0001f)
                heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return heading;
        }

        void SnapToTarget()
        {
            smoothedFocus = target.position + Vector3.up * focusHeight;
            horizontalVelocity = Vector2.zero;
            verticalVelocity = yawVelocity = pitchVelocity = zoomVelocity = 0f;
            smoothedYaw = GetHeading() + yawOffset;
            smoothedPitch = pitch;
            smoothedDistance = distance;
            initialized = true;
            PlaceCamera();
        }

        void PlaceCamera()
        {
            Quaternion orbit = Quaternion.Euler(smoothedPitch, smoothedYaw, 0f);
            Vector3 cameraPosition = smoothedFocus + orbit * (Vector3.back * smoothedDistance);
            transform.SetPositionAndRotation(cameraPosition,
                Quaternion.LookRotation(smoothedFocus - cameraPosition, Vector3.up));
        }
    }
}
