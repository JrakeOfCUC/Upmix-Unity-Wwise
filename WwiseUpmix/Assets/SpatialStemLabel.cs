using UnityEngine;

public sealed class SpatialStemLabel : MonoBehaviour
{
    [SerializeField] private string displayName;
    public string DisplayName => displayName;
    private Vector3 LabelPosition => transform.position + Vector3.up * 0.28f;

    private void OnGUI()
    {
        var camera = Camera.main;
        if (camera == null || Event.current.type != EventType.Repaint) return;
        var point = camera.WorldToScreenPoint(LabelPosition);
        if (point.z <= camera.nearClipPlane || !camera.pixelRect.Contains(point)) return;

        var content = new GUIContent(displayName);
        var size = GUI.skin.box.CalcSize(content);
        GUI.Box(new Rect(point.x - size.x / 2f, Screen.height - point.y - size.y,
            size.x, size.y), content);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var style = new GUIStyle(UnityEditor.EditorStyles.whiteBoldLabel)
        {
            alignment = TextAnchor.LowerCenter
        };
        UnityEditor.Handles.Label(LabelPosition, displayName, style);
    }
#endif
}
