using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Local, presentation-only stack for dismissible screen popups. Popup
    /// owners keep their own state; the stack only decides which owner receives
    /// the next Escape/close request.
    /// </summary>
    internal static class UiPopupStack
    {
        private readonly struct Entry
        {
            public Entry(GameObject root, Action close)
            {
                Root = root;
                Close = close;
            }

            public GameObject Root { get; }
            public Action Close { get; }
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        internal static int Count
        {
            get
            {
                Refresh();
                return Entries.Count;
            }
        }

        internal static void Push(GameObject root, Action close)
        {
            if (root == null || close == null)
            {
                return;
            }

            RemoveInternal(root);
            Entries.Add(new Entry(root, close));
            root.transform.SetAsLastSibling();
            RefreshInputGate();
        }

        internal static void Remove(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            RemoveInternal(root);
            Refresh();
        }

        internal static bool IsTop(GameObject root)
        {
            Refresh();
            return root != null && Entries.Count > 0 &&
                   Entries[Entries.Count - 1].Root == root;
        }

        internal static bool TryCloseTop()
        {
            Refresh();
            if (Entries.Count == 0)
            {
                return false;
            }

            var index = Entries.Count - 1;
            var entry = Entries[index];
            Entries.RemoveAt(index);
            RefreshInputGate();
            entry.Close?.Invoke();
            Refresh();
            return true;
        }

        /// <summary>
        /// Removes roots hidden or destroyed by their owning gameplay state.
        /// Called by the persistent common menu every frame as a safety net.
        /// </summary>
        internal static void Refresh()
        {
            for (var index = Entries.Count - 1; index >= 0; index--)
            {
                var root = Entries[index].Root;
                if (root == null || !root.activeInHierarchy)
                {
                    Entries.RemoveAt(index);
                }
            }

            RefreshInputGate();
        }

        internal static void ClearForTests()
        {
            Entries.Clear();
            RefreshInputGate();
        }

        private static void RemoveInternal(GameObject root)
        {
            for (var index = Entries.Count - 1; index >= 0; index--)
            {
                if (Entries[index].Root == root)
                {
                    Entries.RemoveAt(index);
                }
            }
        }

        private static void RefreshInputGate()
        {
            LocalInputGate.SetMenuOpen(Entries.Count > 0);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Entries.Clear();
            RefreshInputGate();
        }
    }
}
