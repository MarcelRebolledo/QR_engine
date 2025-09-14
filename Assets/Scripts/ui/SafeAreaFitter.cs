// Assets/Scripts/UI/SafeAreaFitter.cs
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
  RectTransform rt;
  ScreenOrientation lastOrientation;
  Rect lastSafe;

  void Awake()
  {
    rt = GetComponent<RectTransform>();
    Apply();
  }

  void OnRectTransformDimensionsChange() => Apply();
  void Update()
  {
    if (lastOrientation != Screen.orientation || lastSafe != Screen.safeArea)
      Apply();
  }

  void Apply()
  {
    var sa = Screen.safeArea;
    var canvas = rt.GetComponentInParent<Canvas>();
    if (canvas == null) return;

    var size = canvas.pixelRect.size;
    Vector2 anchorMin = sa.position;
    Vector2 anchorMax = sa.position + sa.size;
    anchorMin.x /= size.x; anchorMin.y /= size.y;
    anchorMax.x /= size.x; anchorMax.y /= size.y;

    rt.anchorMin = anchorMin;
    rt.anchorMax = anchorMax;
    rt.offsetMin = Vector2.zero;
    rt.offsetMax = Vector2.zero;

    lastOrientation = Screen.orientation;
    lastSafe = sa;
  }
}
