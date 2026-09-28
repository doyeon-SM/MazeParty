using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Translates a static world-space TextMesh label (for example the key shop
    /// sign). The authored English copy is the string-table key. Runtime code
    /// that writes the same TextMesh keeps ownership of it.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    [DisallowMultipleComponent]
    public sealed class LocalizedTextMesh : MonoBehaviour
    {
        [SerializeField, TextArea] private string sourceText = string.Empty;

        private TextMesh _textMesh;
        private string _lastApplied;

        public string SourceText => sourceText;

        public void Configure(string source)
        {
            sourceText = source ?? string.Empty;
            if (Application.isPlaying)
            {
                Apply();
            }
        }

        private void Reset()
        {
            var textMesh = GetComponent<TextMesh>();
            sourceText = textMesh != null ? textMesh.text : string.Empty;
        }

        private void Awake()
        {
            _textMesh = GetComponent<TextMesh>();
            if (string.IsNullOrEmpty(sourceText) && _textMesh != null)
            {
                sourceText = _textMesh.text;
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
            if (_textMesh == null)
            {
                _textMesh = GetComponent<TextMesh>();
            }

            if (_textMesh == null)
            {
                return;
            }

            GameFonts.Apply(_textMesh);
            if (string.IsNullOrEmpty(sourceText))
            {
                return;
            }

            var current = _textMesh.text;
            if (current != sourceText && current != _lastApplied)
            {
                return;
            }

            _lastApplied = GameText.T(sourceText);
            _textMesh.text = _lastApplied;
        }
    }
}
