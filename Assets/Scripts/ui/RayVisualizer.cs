using UnityEngine;

public class RayVisualizer : MonoBehaviour
{
    LineRenderer lr;

    void Awake()
    {
        lr = gameObject.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startWidth = 0.005f;
        lr.endWidth = 0.005f;
        lr.positionCount = 2;
        lr.startColor = Color.red;
        lr.endColor = Color.red;
    }

    public void ShowRay(Vector3 start, Vector3 dir, float length = 0.5f)
    {
        lr.SetPosition(0, start);
        lr.SetPosition(1, start + dir.normalized * length);
    }
}
