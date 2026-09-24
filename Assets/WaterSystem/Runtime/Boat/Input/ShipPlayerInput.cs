using UnityEngine;
using UnityEngine.InputSystem;

namespace WaterSystem.Ocean
{
    /// <summary>Demo controls; replace or disable this when supplying SetControls from your own input layer.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipMotor))]
    [AddComponentMenu("Water System/Ship Player Input")]
    public sealed class ShipPlayerInput : MonoBehaviour
    {
        [SerializeField] bool useKeyboard = true;
        [SerializeField] bool useGamepad = true;
        [SerializeField, Range(0f, 0.5f)] float gamepadDeadZone = 0.15f;

        ShipMotor motor;

        void Awake() => motor = GetComponent<ShipMotor>();

        void Update()
        {
            if (motor == null) return;
            if (!Application.isFocused)
            {
                motor.SetControls(0, 0);
                return;
            }

            float throttle = 0;
            float rudder = 0;
            if (useKeyboard && Keyboard.current != null)
            {
                Keyboard keys = Keyboard.current;
                throttle = (keys.wKey.isPressed || keys.upArrowKey.isPressed ? 1 : 0) -
                    (keys.sKey.isPressed || keys.downArrowKey.isPressed ? 1 : 0);
                rudder = (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1 : 0) -
                    (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1 : 0);
            }
            if (useGamepad && Gamepad.current != null && throttle == 0 && rudder == 0)
            {
                Gamepad pad = Gamepad.current;
                Vector2 stick = pad.leftStick.ReadValue();
                throttle = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                if (Mathf.Abs(throttle) < gamepadDeadZone) throttle = stick.y;
                rudder = stick.x;
                if (Mathf.Abs(throttle) < gamepadDeadZone) throttle = 0;
                if (Mathf.Abs(rudder) < gamepadDeadZone) rudder = 0;
            }
            motor.SetControls(throttle, rudder);
        }

        void OnDisable()
        {
            if (motor != null) motor.SetControls(0, 0);
        }
    }
}
