using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay
{
    public sealed class GameplayItemDefinition
    {
        public GameplayItemDefinition(string id, string displayName, string description, int initialCharges = 1)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("An item id is required.", nameof(id));
            if (initialCharges < 1)
                throw new ArgumentOutOfRangeException(nameof(initialCharges));

            Id = id;
            DisplayName = displayName ?? id;
            Description = description ?? string.Empty;
            InitialCharges = initialCharges;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int InitialCharges { get; }
    }

    public sealed class GameplayItemSlot
    {
        internal GameplayItemSlot(GameplayItemDefinition definition)
        {
            Definition = definition;
            Charges = definition.InitialCharges;
        }

        public GameplayItemDefinition Definition { get; }
        public int Charges { get; internal set; }
        public bool IsEmpty => Definition == null;
    }

    /// <summary>
    /// Fixed three-slot inventory. Items never stack and there is no player discard action.
    /// </summary>
    public sealed class GameplayInventory
    {
        public const int Capacity = 3;

        private readonly GameplayItemSlot[] _slots = new GameplayItemSlot[Capacity];

        public event Action Changed;

        public IReadOnlyList<GameplayItemSlot> Slots => _slots;
        public int SelectedSlotIndex { get; private set; } = -1;
        public GameplayItemSlot SelectedSlot =>
            SelectedSlotIndex >= 0 && SelectedSlotIndex < Capacity ? _slots[SelectedSlotIndex] : null;
        public bool IsFull => FindFirstEmptySlot() < 0;

        public bool TryAdd(GameplayItemDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var index = FindFirstEmptySlot();
            if (index < 0)
                return false;

            _slots[index] = new GameplayItemSlot(definition);
            Changed?.Invoke();
            return true;
        }

        public bool TrySelect(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Capacity || _slots[slotIndex] == null)
                return false;

            SelectedSlotIndex = slotIndex;
            Changed?.Invoke();
            return true;
        }

        public void ClearSelection()
        {
            if (SelectedSlotIndex < 0)
                return;

            SelectedSlotIndex = -1;
            Changed?.Invoke();
        }

        public bool TryConsumeSelectedCharge()
        {
            var slot = SelectedSlot;
            if (slot == null)
                return false;

            slot.Charges--;
            if (slot.Charges <= 0)
            {
                _slots[SelectedSlotIndex] = null;
                SelectedSlotIndex = -1;
            }

            Changed?.Invoke();
            return true;
        }

        public bool EndSelectedItemUse()
        {
            if (SelectedSlot == null)
                return false;

            _slots[SelectedSlotIndex] = null;
            SelectedSlotIndex = -1;
            Changed?.Invoke();
            return true;
        }

        public void Reset()
        {
            for (var i = 0; i < _slots.Length; i++)
                _slots[i] = null;

            SelectedSlotIndex = -1;
            Changed?.Invoke();
        }

        private int FindFirstEmptySlot()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null)
                    return i;
            }

            return -1;
        }
    }
}
