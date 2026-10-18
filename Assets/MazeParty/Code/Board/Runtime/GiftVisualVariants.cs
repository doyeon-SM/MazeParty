using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Authored gift models selected consistently from the shared event identity.</summary>
    [DisallowMultipleComponent]
    public sealed class GiftVisualVariants : MonoBehaviour
    {
        [SerializeField] private GameObject[] variants = new GameObject[3];
        private int _selected = -1;
        public GameObject[] Variants => variants;
        public bool HasRequiredReferences => variants != null && variants.Length == 3 &&
            System.Array.TrueForAll(variants, variant => variant != null);

        public void Present(ulong seed, int eventIndex, int giftIndex)
        {
            var selected = SelectIndex(seed, eventIndex, giftIndex);
            if (_selected == selected || !HasRequiredReferences) return;
            _selected = selected;
            for (var index = 0; index < variants.Length; index++)
                variants[index].SetActive(index == selected);
        }

        public static int SelectIndex(ulong seed, int eventIndex, int giftIndex)
        {
            // SplitMix64: no dependence on client-local UnityEngine.Random state.
            unchecked
            {
                var value = seed ^ ((ulong)(uint)eventIndex << 32) ^ (uint)giftIndex;
                value += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return (int)((value ^ (value >> 31)) % 3UL);
            }
        }
    }
}
