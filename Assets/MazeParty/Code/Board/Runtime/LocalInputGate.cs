using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Local-only gate between screen UI and gameplay input. While the common
    /// menu is open the local player sends neutral movement and no actions, and
    /// every cursor policy keeps the pointer free. Other players and the server
    /// simulation are unaffected.
    /// </summary>
    public static class LocalInputGate
    {
        private static int _menuClosedFrame = -1;

        public static bool IsMenuOpen { get; private set; }
        public static bool IsPausePointerRequested { get; private set; }

        /// <summary>
        /// True while local gameplay input must be ignored, including the frame
        /// the menu closed so the closing key or click is not replayed as an action.
        /// </summary>
        public static bool BlocksGameplayInput =>
            IsMenuOpen || _menuClosedFrame == Time.frameCount;

        /// <summary>True while a local UI needs a visible, unlocked pointer.</summary>
        public static bool RequestsPointer => IsMenuOpen || IsPausePointerRequested;

        public static event Action Changed;

        public static void SetMenuOpen(bool open)
        {
            if (IsMenuOpen == open)
            {
                return;
            }

            IsMenuOpen = open;
            if (!open)
            {
                _menuClosedFrame = Time.frameCount;
            }

            Changed?.Invoke();
        }

        public static void SetPausePointerRequested(bool requested)
        {
            if (IsPausePointerRequested == requested)
            {
                return;
            }

            IsPausePointerRequested = requested;
            Changed?.Invoke();
        }

        /// <summary>
        /// Applies the pointer override after a gameplay view chose its own cursor
        /// policy. Returns true when the override replaced that policy.
        /// </summary>
        public static bool ApplyPointerOverride()
        {
            if (!RequestsPointer)
            {
                return false;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            IsMenuOpen = false;
            IsPausePointerRequested = false;
            _menuClosedFrame = -1;
            Changed = null;
        }
    }
}
