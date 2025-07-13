using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.InputSystem.XR;

public class ARSetup : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        if (FindObjectOfType<ARSession>() == null)
        {
            var sessionGO = new GameObject("AR Session");
            sessionGO.AddComponent<ARSession>();
        }

        if (FindObjectOfType<ARSessionOrigin>() == null)
        {
            var originGO = new GameObject("AR Session Origin");
            var origin = originGO.AddComponent<ARSessionOrigin>();
            originGO.AddComponent<ARAnchorManager>();

            var cameraGO = new GameObject("AR Camera");
            cameraGO.transform.parent = originGO.transform;
            var cam = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<ARCameraManager>();
            cameraGO.AddComponent<ArucoTracker>();
            cameraGO.AddComponent<TrackedPoseDriver>();
            origin.camera = cam;
        }
    }
}
