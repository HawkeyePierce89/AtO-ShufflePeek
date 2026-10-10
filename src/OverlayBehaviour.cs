using UnityEngine;

namespace AtOShufflePeek
{
    public class OverlayBehaviour : MonoBehaviour
    {
        public bool Visible { get; private set; }

        public void Hide()
        {
            Visible = false;
        }
    }
}
