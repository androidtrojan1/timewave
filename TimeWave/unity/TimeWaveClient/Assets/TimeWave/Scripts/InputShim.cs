using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TimeWave.Client
{
    /// <summary>Mouse and keyboard access that compiles with either the legacy Input Manager or the Input System package.</summary>
    public static class InputShim
    {
        /// <summary>False when the project enables only the Input System backend but the package is not installed.</summary>
        public static bool Available
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER || (ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM)
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Screen position with the origin at the bottom-left, like Input.mousePosition.</summary>
        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>Wheel notches this frame (positive = away from the user).</summary>
        public static float Scroll
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mouseScrollDelta.y;
#elif ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
                return 0f;
#endif
            }
        }

        public static bool MouseDown(int button)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(button);
#elif ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
            var b = MouseButton(button);
            return b != null && b.wasPressedThisFrame;
#else
            return false;
#endif
        }

        public static bool MouseHeld(int button)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#elif ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
            var b = MouseButton(button);
            return b != null && b.isPressed;
#else
            return false;
#endif
        }

        public static bool MouseUp(int button)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(button);
#elif ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
            var b = MouseButton(button);
            return b != null && b.wasReleasedThisFrame;
#else
            return false;
#endif
        }

        public static bool KeyHeld(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(key);
#elif ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
            var k = Map(key);
            return k != null && k.isPressed;
#else
            return false;
#endif
        }

        public static bool KeyDown(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#elif ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
            var k = Map(key);
            return k != null && k.wasPressedThisFrame;
#else
            return false;
#endif
        }

#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM && TIMEWAVE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.ButtonControl MouseButton(int button)
        {
            var m = Mouse.current;
            if (m == null) return null;
            return button == 0 ? m.leftButton : button == 1 ? m.rightButton : m.middleButton;
        }

        static UnityEngine.InputSystem.Controls.KeyControl Map(KeyCode key)
        {
            var k = Keyboard.current;
            if (k == null) return null;
            switch (key)
            {
                case KeyCode.W: return k.wKey;
                case KeyCode.A: return k.aKey;
                case KeyCode.S: return k.sKey;
                case KeyCode.D: return k.dKey;
                case KeyCode.Q: return k.qKey;
                case KeyCode.E: return k.eKey;
                case KeyCode.UpArrow: return k.upArrowKey;
                case KeyCode.DownArrow: return k.downArrowKey;
                case KeyCode.LeftArrow: return k.leftArrowKey;
                case KeyCode.RightArrow: return k.rightArrowKey;
                case KeyCode.Space: return k.spaceKey;
                case KeyCode.LeftShift: return k.leftShiftKey;
                case KeyCode.Alpha1: return k.digit1Key;
                case KeyCode.Alpha2: return k.digit2Key;
                case KeyCode.Alpha3: return k.digit3Key;
                case KeyCode.Alpha4: return k.digit4Key;
                case KeyCode.Escape: return k.escapeKey;
                default: return null;
            }
        }
#endif
    }
}
