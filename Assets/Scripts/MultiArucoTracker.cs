using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ArucoUnity.Plugin;          // Binding C# ➜ C++ (ArucoUnity)
using Cv = ArucoUnity.Plugin.Cv;  // Alias para abreviar las clases de OpenCV
using Aruco = ArucoUnity.Plugin.Aruco;   // Alias para funciones ArUco
using Std = ArucoUnity.Plugin.Std;       // Alias para contenedores std::vector
using TMPro;
using System.Text;
using UnityEngine.UI;
/// <summary>
/// Detecta múltiples marcadores ArUco con la librería ArucoUnity (OpenCV 4) y coloca un <see cref="contentPrefab"/> sobre cada id nuevo.
/// Compatible con <b>Unity 6 LTS</b> + <b>AR Foundation 6</b> (AR Core / AR Kit).
/// <para>
/// » El código sólo usa APIs públicas incluidas en el paquete <i>ArucoUnityOA</i>, sin dependencias de pago.
/// » Se ha depurado para que compile sin errores de espacio de nombres ni tipos faltantes.
/// </para>
/// </summary>
[RequireComponent(typeof(ARCameraManager))]
public sealed class MultiArucoTracker : MonoBehaviour
{
    //──────────────────────────────────────── Managers & Prefab
    [Header("Managers")]
    [SerializeField] ARCameraManager camManager;
    [SerializeField] ARAnchorManager anchorManager;   // Puede inyectarse desde la escena

    [Header("Content")]
    [Tooltip("Prefab que se instancia sobre cada marcador detectado")]
    public GameObject contentPrefab;

    //──────────────────────────────────────── Detección
    [Header("Detection Settings")]
    [Tooltip("Tamaño físico del lado del marcador en metros (‑1 ➜ sin estimar pose)")]
    public float markerSideMeters = 0.025f;
    [Tooltip("Diccionario predefinido OpenCV: Dict4x4_50, Dict4x4_100 …")]
    public Aruco.PredefinedDictionaryName dictionaryName = Aruco.PredefinedDictionaryName.Dict4x4_50;

    Aruco.Dictionary dictionary;                     // Tabla binaria de marcadores
    Aruco.DetectorParameters detectorParams;            // Parámetros de umbrales, etc.
    readonly Dictionary<int, ARAnchor> anchors = new();

    Cv.Mat cameraMatrix;
    Cv.Mat distCoeffs;

    bool intrinsicsInit;



    // ─────────────────────────────── NEW: UI debug
    [Header("Debug UI")]
    public TMP_Text debugText;
    public ScrollRect scrollRect;   // 👈 referencia al Scroll View
    public int maxLines = 200;

    readonly StringBuilder logBuffer = new();// guarda el histórico





    //──────────────────────────────────────── Lifecycle
    void Awake()
    {
        if (!camManager) camManager = GetComponent<ARCameraManager>();
        if (!anchorManager) anchorManager = FindAnyObjectByType<ARAnchorManager>();

        dictionary = Aruco.GetPredefinedDictionary(dictionaryName);
        detectorParams = new Aruco.DetectorParameters();

        cameraMatrix = new Cv.Mat();
        distCoeffs = new Cv.Mat(1, 5, Cv.Type.CV_64F, new double[5]);

        intrinsicsInit = false;


        Log($"Aruco dict: {dictionaryName}");
    }

    void OnEnable() => camManager.frameReceived += OnFrame;
    void OnDisable() => camManager.frameReceived -= OnFrame;

    //──────────────────────────────────────── Frame loop
    void OnFrame(ARCameraFrameEventArgs _)
    {
        // ① CPU image ---------------------------------------------------------
     
        bool gotImage = camManager.TryAcquireLatestCpuImage(out var cpuImage);
        Log($"cpu ok? {gotImage}");

        if (!gotImage) return;


        // Obtener intrínsecos solo una vez
        if (!intrinsicsInit)
        {
            if (!camManager.TryGetIntrinsics(out var intr))
            {
                cpuImage.Dispose();
                return;
            }

            double[] camMatrixArr =
            {
                intr.focalLength.x, 0, intr.principalPoint.x,
                0, intr.focalLength.y, intr.principalPoint.y,
                0, 0, 1
            };
            cameraMatrix = new Cv.Mat(3, 3, Cv.Type.CV_64F, camMatrixArr);
            intrinsicsInit = true;
        }


        var conv = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
            outputFormat = TextureFormat.R8,        // escala de grises
            transformation = XRCpuImage.Transformation.MirrorY
        };

        int byteCount = cpuImage.GetConvertedDataSize(conv);
        using var buffer = new NativeArray<byte>(byteCount, Allocator.Temp);
        cpuImage.Convert(conv, buffer);
        cpuImage.Dispose();

        bool portrait =
            Screen.orientation == ScreenOrientation.Portrait ||
            Screen.orientation == ScreenOrientation.PortraitUpsideDown;

        // ② Native → OpenCV ---------------------------------------------------
        byte[] managed = buffer.ToArray();
        Cv.Mat frame = new Cv.Mat(conv.outputDimensions.y, conv.outputDimensions.x, Cv.Type.CV_8UC1, managed);
        Std.VectorVectorPoint2f corners;
        Std.VectorInt ids;

        Aruco.DetectMarkers(frame, dictionary, out corners, out ids, detectorParams);
        Log($"Detected {ids.Size()} markers");


        if (ids.Size() == 0)
        {
            frame.Dispose();
            return;
        }

        Std.VectorVec3d rvecs = null;
        Std.VectorVec3d tvecs = null;
        if (markerSideMeters > 0)
        {
            Aruco.EstimatePoseSingleMarkers(corners, markerSideMeters, cameraMatrix, distCoeffs, out rvecs, out tvecs);
        }

        for (int i = 0; i < ids.Size(); ++i)
        {
            int id = ids.At((uint)i);


            Vector3 localPos  = tvecs != null ? Cv2UnityPos(tvecs.At((uint)i), portrait) : Vector3.zero;
            Quaternion localRot = rvecs != null ? Cv2UnityRot(rvecs.At((uint)i), portrait) : Quaternion.identity;


            Pose worldPose = new Pose(
                camManager.transform.TransformPoint(localPos),
                camManager.transform.rotation * localRot);

            if (!anchors.TryGetValue(id, out var anchor) || anchor == null)
            {
                // ─── Crear ancla ───────────────────────────────────────
                ARAnchor newAnchor = null;
                if (anchorManager && anchorManager.subsystem != null &&
                    anchorManager.subsystem.TryAddAnchor(worldPose, out XRAnchor xrAnchor))
                {
                    newAnchor = anchorManager.GetAnchor(xrAnchor.trackableId);
                }

                if (!newAnchor)
                {
                    var go = new GameObject($"aruco_{id}");
                    go.transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
                    newAnchor = go.AddComponent<ARAnchor>();
                }

                anchors[id] = newAnchor;
                Instantiate(contentPrefab, newAnchor.transform, false);
            }
            else
            {
                anchor.transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            }
        }

        frame.Dispose();
    }

    // ---------- Funciones a completar ----------
    static Vector3 Cv2UnityPos(Cv.Vec3d t, bool portrait)
    {
        double x = t.Get(0);
        double y = t.Get(1);
        double z = t.Get(2);

        if (portrait)
        {
            double tmp = x;
            x = y;
            y = tmp;
        }

        return new Vector3((float)x, -(float)y, (float)z);
    }

    static Quaternion Cv2UnityRot(Cv.Vec3d r, bool portrait)
    {
        double x = r.Get(0);
        double y = r.Get(1);
        double z = r.Get(2);

        if (portrait)
        {
            double tmp = x;
            x = y;
            y = tmp;
        }

        y = -y;

        double angleRad = System.Math.Sqrt(x * x + y * y + z * z);
        if (angleRad < 1e-12)
            return Quaternion.identity;

        Vector3 axis = new Vector3((float)(x / angleRad), (float)(y / angleRad), (float)(z / angleRad));
        float angleDeg = (float)(angleRad * Mathf.Rad2Deg);
        return Quaternion.AngleAxis(angleDeg, axis);
    }


    // ─────────────────────────────── helpers ───────────────────────────────
    void Log(string msg)
    {
        Debug.Log(msg);

        if (!debugText) return;

        logBuffer.AppendLine(msg);

        // recorta si excede el límite
        int excess = logBuffer.ToString().Split('\n').Length - maxLines;
        if (excess > 0)
        {
            // descarta las primeras 'excess' líneas
            string[] lines = logBuffer.ToString().Split('\n');
            logBuffer.Clear();
            for (int i = excess; i < lines.Length; i++)
                logBuffer.AppendLine(lines[i]);
        }

        debugText.text = logBuffer.ToString();

        // ─── fuerza actualización de layout y baja el scroll ───
        Canvas.ForceUpdateCanvases();
        if (scrollRect)
            scrollRect.verticalNormalizedPosition = 0f; // 0 = abajo
    }
}
