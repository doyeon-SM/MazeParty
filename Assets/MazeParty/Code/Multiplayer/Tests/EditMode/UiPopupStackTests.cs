using System.Collections.Generic;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class UiPopupStackTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            UiPopupStack.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            UiPopupStack.ClearForTests();
            foreach (var root in _roots)
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            _roots.Clear();
        }

        [Test]
        public void Popups_CloseInLastOpenedFirstClosedOrder()
        {
            var closed = new List<string>();
            var first = CreateRoot("First Popup");
            var second = CreateRoot("Second Popup");

            UiPopupStack.Push(first, () =>
            {
                closed.Add("first");
                first.SetActive(false);
            });
            UiPopupStack.Push(second, () =>
            {
                closed.Add("second");
                second.SetActive(false);
            });

            Assert.That(UiPopupStack.Count, Is.EqualTo(2));
            Assert.That(LocalInputGate.IsMenuOpen, Is.True);
            Assert.That(UiPopupStack.TryCloseTop(), Is.True);
            Assert.That(closed, Is.EqualTo(new[] { "second" }));
            Assert.That(UiPopupStack.Count, Is.EqualTo(1));
            Assert.That(LocalInputGate.IsMenuOpen, Is.True);

            Assert.That(UiPopupStack.TryCloseTop(), Is.True);
            Assert.That(closed, Is.EqualTo(new[] { "second", "first" }));
            Assert.That(UiPopupStack.Count, Is.Zero);
            Assert.That(LocalInputGate.IsMenuOpen, Is.False);
            Assert.That(LocalInputGate.BlocksGameplayInput, Is.True,
                "The closing input must remain consumed for the current frame.");
        }

        [Test]
        public void ReopeningPopup_MovesItToTopWithoutDuplicatingEntry()
        {
            var closed = string.Empty;
            var first = CreateRoot("First Popup");
            var second = CreateRoot("Second Popup");

            UiPopupStack.Push(first, () => closed = "old first");
            UiPopupStack.Push(second, () => closed = "second");
            UiPopupStack.Push(first, () => closed = "new first");

            Assert.That(UiPopupStack.Count, Is.EqualTo(2));
            Assert.That(UiPopupStack.IsTop(first), Is.True);
            Assert.That(UiPopupStack.TryCloseTop(), Is.True);
            Assert.That(closed, Is.EqualTo("new first"));
            Assert.That(UiPopupStack.IsTop(second), Is.True);
        }

        [Test]
        public void OwnerStateClose_RemovesEntryWithoutCallingItAgain()
        {
            var closeCalls = 0;
            var popup = CreateRoot("Popup");
            UiPopupStack.Push(popup, () => closeCalls++);

            popup.SetActive(false);
            UiPopupStack.Remove(popup);

            Assert.That(UiPopupStack.Count, Is.Zero);
            Assert.That(UiPopupStack.TryCloseTop(), Is.False);
            Assert.That(closeCalls, Is.Zero);
            Assert.That(LocalInputGate.IsMenuOpen, Is.False);
        }

        [Test]
        public void Refresh_PrunesInactiveAndDestroyedRootsWithoutInvokingOwners()
        {
            var closeCalls = 0;
            var inactive = CreateRoot("Inactive Popup");
            var destroyed = CreateRoot("Destroyed Popup");
            UiPopupStack.Push(inactive, () => closeCalls++);
            UiPopupStack.Push(destroyed, () => closeCalls++);

            inactive.SetActive(false);
            Object.DestroyImmediate(destroyed);
            UiPopupStack.Refresh();

            Assert.That(UiPopupStack.Count, Is.Zero);
            Assert.That(closeCalls, Is.Zero);
            Assert.That(LocalInputGate.IsMenuOpen, Is.False);
        }

        private GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            _roots.Add(root);
            return root;
        }
    }
}
