using UnityEngine;
using UnityEngine.InputSystem;

namespace ZombieCoop
{
    /// Ввод одного игрока: геймпад (если есть) плюс, у игрока 1, клавиатура и мышь.
    /// Оси приведены к тем же знакам, что в Godot-версии: стик вниз — плюс по Y.
    public class PlayerInput
    {
        public Gamepad pad;
        public bool keyboard;

        public PlayerInput(Gamepad pad, bool keyboard)
        {
            this.pad = pad;
            this.keyboard = keyboard;
        }

        static bool Key(Key k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[k].isPressed;
        }

        public static bool MouseLocked => Cursor.lockState == CursorLockMode.Locked;

        /// Ходьба: x — вправо, y — вперёд.
        public Vector2 Move(float deadzone)
        {
            var v = Vector2.zero;
            if (keyboard)
            {
                v.x = (Key(UnityEngine.InputSystem.Key.D) ? 1f : 0f) - (Key(UnityEngine.InputSystem.Key.A) ? 1f : 0f);
                v.y = (Key(UnityEngine.InputSystem.Key.W) ? 1f : 0f) - (Key(UnityEngine.InputSystem.Key.S) ? 1f : 0f);
            }
            if (pad != null)
            {
                var stick = pad.leftStick.ReadValue();
                if (stick.magnitude > deadzone) v += stick;
            }
            return Vector2.ClampMagnitude(v, 1f);
        }

        /// Правый стик: x — вправо, y — вверх.
        public Vector2 Look => pad != null ? pad.rightStick.ReadValue() : Vector2.zero;

        public Vector2 MouseDelta
        {
            get
            {
                if (!keyboard || !MouseLocked || Mouse.current == null) return Vector2.zero;
                return Mouse.current.delta.ReadValue();
            }
        }

        public float Scroll => keyboard && MouseLocked && Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;

        public bool Fire =>
            (pad != null && pad.rightTrigger.ReadValue() > 0.5f) ||
            (keyboard && MouseLocked && Mouse.current != null && Mouse.current.leftButton.isPressed);

        public bool Reload => (pad != null && pad.buttonWest.isPressed) || (keyboard && Key(UnityEngine.InputSystem.Key.R));
        public bool Next => (pad != null && pad.buttonNorth.isPressed) || (keyboard && Key(UnityEngine.InputSystem.Key.Q));
        public bool Share => (pad != null && pad.buttonEast.isPressed) || (keyboard && Key(UnityEngine.InputSystem.Key.G));

        public bool Kick =>
            (pad != null && pad.rightShoulder.isPressed) ||
            (keyboard && Key(UnityEngine.InputSystem.Key.F)) ||
            (keyboard && MouseLocked && Mouse.current != null && Mouse.current.rightButton.isPressed);

        /// Номер оружия по клавишам 1–4 или -1.
        public int NumberKey
        {
            get
            {
                if (!keyboard) return -1;
                if (Key(UnityEngine.InputSystem.Key.Digit1)) return 0;
                if (Key(UnityEngine.InputSystem.Key.Digit2)) return 1;
                if (Key(UnityEngine.InputSystem.Key.Digit3)) return 2;
                if (Key(UnityEngine.InputSystem.Key.Digit4)) return 3;
                return -1;
            }
        }
    }
}
