using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Translates a static prefab label. The English copy authored on the Text
    /// (or <see cref="sourceText"/> when set) is the string-table key; the label
    /// is refreshed whenever the language changes. Runtime code that writes the
    /// same Text keeps ownership: once the label shows anything other than the
    /// source or the last translation, this component stops touching it.
    /// </summary>
    [RequireComponent(typeof(Text))]
    [DisallowMultipleComponent]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField, TextArea] private string sourceText = string.Empty;

        private Text _text;
        private string _lastApplied;

        public string SourceText => sourceText;

        public void Configure(string source)
        {
            sourceText = source ?? string.Empty;
            // Edit-time setup must keep the authored English copy in the prefab.
            if (Application.isPlaying)
            {
                Apply();
            }
        }

        private void Reset()
        {
            var text = GetComponent<Text>();
            sourceText = text != null ? text.text : string.Empty;
        }

        private void Awake()
        {
            _text = GetComponent<Text>();
            if (string.IsNullOrEmpty(sourceText) && _text != null)
            {
                sourceText = _text.text;
            }
        }

        private void OnEnable()
        {
            GameText.LanguageChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GameText.LanguageChanged -= Apply;
        }

        private void Apply()
        {
            if (_text == null)
            {
                _text = GetComponent<Text>();
            }

            if (_text == null)
            {
                return;
            }

            _text.font = GameFonts.Current;
            if (string.IsNullOrEmpty(sourceText))
            {
                return;
            }

            var current = _text.text;
            if (current != sourceText && current != _lastApplied)
            {
                return;
            }

            _lastApplied = GameText.T(sourceText);
            _text.text = _lastApplied;
        }
    }
}
