using UnityEngine;

namespace LocalVrcw
{
    public sealed class ProbeCamera : MonoBehaviour
    {
        float pitch;
        float yaw;
        public void ResetLook() { yaw = transform.eulerAngles.y; pitch = transform.eulerAngles.x; }

        void Start()
        {
            ResetLook();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (Input.GetMouseButtonDown(1))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                yaw += Input.GetAxisRaw("Mouse X") * 2;
                pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2, -89, 89);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            }
            Vector3 movement = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) movement += transform.forward;
            if (Input.GetKey(KeyCode.S)) movement -= transform.forward;
            if (Input.GetKey(KeyCode.A)) movement -= transform.right;
            if (Input.GetKey(KeyCode.D)) movement += transform.right;
            if (Udon.LocalPlayer.Instance?.immobilized == true) return;
            transform.position += Vector3.ClampMagnitude(movement, 1) *
                (Input.GetKey(KeyCode.LeftShift) ? 12 : 4) * Time.unscaledDeltaTime;
        }
    }
}
