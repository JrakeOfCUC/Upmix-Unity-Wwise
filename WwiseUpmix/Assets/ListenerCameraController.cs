using UnityEngine;

[DisallowMultipleComponent]
public sealed class ListenerCameraController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 3f;
    [SerializeField, Min(0f)] private float mouseSensitivity = 2f;

    private SpatialStemPlayer stemPlayer;
    private float yaw;
    private float pitch;

    private void OnEnable()
    {
        stemPlayer = FindObjectOfType<SpatialStemPlayer>();
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = Mathf.DeltaAngle(0f, angles.x);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (stemPlayer != null)
            {
                stemPlayer.MixerOpen = !stemPlayer.MixerOpen;
                Cursor.lockState = stemPlayer.MixerOpen ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = stemPlayer.MixerOpen;
            }
            else ReleaseCursor();
            return;
        }

        if (stemPlayer != null && stemPlayer.MixerOpen) return;

        if (!Application.isFocused) return;

        if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        float sideways = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
        float forward = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
        Vector3 direction = Vector3.ClampMagnitude(new Vector3(sideways, 0f, forward), 1f);
        transform.position += Quaternion.Euler(0f, yaw, 0f) * direction * (moveSpeed * Time.deltaTime);
    }

    private void OnDisable() => ReleaseCursor();

    private static void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
