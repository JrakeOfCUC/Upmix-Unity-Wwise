using UnityEngine;

[DisallowMultipleComponent]
public sealed class StereoWwiseSpatializer : MonoBehaviour
{
    [SerializeField] private Transform listener;
    [SerializeField] private GameObject leftEmitter;
    [SerializeField] private GameObject rightEmitter;
    [SerializeField] private string leftEvent = "Play_Stereo_Left";
    [SerializeField] private string rightEvent = "Play_Stereo_Right";
    [SerializeField] private Vector3 leftOffset = new Vector3(-0.5f, 0f, 1f);
    [SerializeField] private Vector3 rightOffset = new Vector3(0.5f, 0f, 1f);

    private void LateUpdate()
    {
        if (listener == null)
        {
            return;
        }

        if (leftEmitter != null)
        {
            leftEmitter.transform.position = listener.TransformPoint(leftOffset);
        }

        if (rightEmitter != null)
        {
            rightEmitter.transform.position = listener.TransformPoint(rightOffset);
        }
    }

    public void Play()
    {
        if (listener == null || leftEmitter == null || rightEmitter == null ||
            leftEmitter == rightEmitter ||
            string.IsNullOrWhiteSpace(leftEvent) || string.IsNullOrWhiteSpace(rightEvent))
        {
            Debug.LogError("StereoWwiseSpatializer requires a listener, two distinct emitters, and two Wwise event names.", this);
            return;
        }

        AkUnitySoundEngine.PostEvent(leftEvent, leftEmitter);
        AkUnitySoundEngine.PostEvent(rightEvent, rightEmitter);
    }
}
