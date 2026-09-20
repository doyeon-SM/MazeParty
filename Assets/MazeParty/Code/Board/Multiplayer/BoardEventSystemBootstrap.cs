using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Creates a uGUI input event system only when the bootstrap scene did not provide one.</summary>
    public sealed class BoardEventSystemBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("Board EventSystem (Runtime)");
            eventSystemObject.AddComponent<EventSystem>();
            var inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }
    }
}
