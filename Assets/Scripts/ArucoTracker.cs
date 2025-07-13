using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;
using OpenCvSharp;
using OpenCvSharp.Aruco;

[RequireComponent(typeof(ARCameraManager))]
[RequireComponent(typeof(ARSessionOrigin))]
[RequireComponent(typeof(ARAnchorManager))]
public class ArucoTracker : MonoBehaviour
{
    private ARCameraManager cameraManager;
    private ARAnchorManager anchorManager;
    private ARSessionOrigin origin;

    private Dictionary dictionary;
    private DetectorParameters parameters;

    private readonly Dictionary<int, ARAnchor> anchors = new();

    void Awake()
    {
        cameraManager = GetComponent<ARCameraManager>();
        anchorManager = GetComponent<ARAnchorManager>();
        origin = GetComponent<ARSessionOrigin>();
        dictionary = CvAruco.GetPredefinedDictionary(PredefinedDictionaryName.Dict4X4_50);
        parameters = DetectorParameters.Create();
    }

    void OnEnable()
    {
        cameraManager.frameReceived += OnCameraFrame;
    }

    void OnDisable()
    {
        cameraManager.frameReceived -= OnCameraFrame;
    }

    void OnCameraFrame(ARCameraFrameEventArgs args)
    {
        if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            return;

        using (image)
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(image.width, image.height),
                outputFormat = TextureFormat.RGB24,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            var size = image.GetConvertedDataSize(conversionParams);
            using var buffer = new NativeArray<byte>(size, Allocator.Temp);
            image.Convert(conversionParams, buffer);

            using var mat = new Mat(image.height, image.width, MatType.CV_8UC3, buffer.ToArray());
            Point2f[][] corners;
            int[] ids;
            CvAruco.DetectMarkers(mat, dictionary, out corners, out ids, parameters);

            if (ids != null && ids.Length > 0 && cameraManager.TryGetIntrinsics(out var intr))
            {
                var cameraMatrix = new Mat(3, 3, MatType.CV_64FC1);
                cameraMatrix.Set(0, 0, intr.focalLength.x);
                cameraMatrix.Set(1, 1, intr.focalLength.y);
                cameraMatrix.Set(0, 2, intr.principalPoint.x);
                cameraMatrix.Set(1, 2, intr.principalPoint.y);
                cameraMatrix.Set(2, 2, 1.0);

                foreach (var anchor in anchors.Values)
                    anchor.gameObject.SetActive(false);

                for (int i = 0; i < ids.Length; i++)
                {
                    Vec3d rvec, tvec;
                    CvAruco.EstimatePoseSingleMarkers(new[] { corners[i] }, 0.1f, cameraMatrix, new Mat(), out rvec, out tvec);
                    var pose = new Pose(new Vector3((float)tvec.Item0, (float)tvec.Item1, (float)tvec.Item2),
                        Quaternion.Euler((float)rvec.Item0, (float)rvec.Item1, (float)rvec.Item2));

                    if (!anchors.TryGetValue(ids[i], out var anchor))
                    {
                        anchor = anchorManager.AddAnchor(pose);
                        if (anchor != null)
                            anchors.Add(ids[i], anchor);
                    }
                    else
                    {
                        anchor.transform.SetPositionAndRotation(pose.position, pose.rotation);
                        anchor.gameObject.SetActive(true);
                    }
                }
            }
        }
    }
}
