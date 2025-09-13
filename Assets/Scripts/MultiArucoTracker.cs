using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ArucoUnity.Plugin;
using Cv   = ArucoUnity.Plugin.Cv;
using Aruco= ArucoUnity.Plugin.Aruco;
using Std  = ArucoUnity.Plugin.Std;
using TMPro;
using System.Text;
using UnityEngine.UI;
using System.Linq;

[RequireComponent(typeof(ARCameraManager))]
public sealed class MultiArucoTracker : MonoBehaviour
{
    const int   MAX_CONTENT_NODES = 15; // ⟵ máximo de prefabs simultáneos
    const float DEBUG_FPS         = 4f;

    // ────────────────── Inspector ──────────────────
    [Header("Managers")]
    [SerializeField] ARCameraManager camManager;


    [Header("Prefabs")]
    public GameObject defaultPrefab;                // antes: contentPrefab

    // NEW: lista editable en Inspector
    [System.Serializable] public struct IdPrefabPair
    {
        public int id;
        public GameObject prefab;
    }
    [Tooltip("Asignar un prefab distinto para IDs concretos")]
    public List<IdPrefabPair> customPrefabs = new();  // ← se ve como tabla

    [Header("Detection Settings")]
    public float markerSideMeters = -1f;
    public Aruco.PredefinedDictionaryName dictionaryName =
        Aruco.PredefinedDictionaryName.Dict4x4_50;

    [Header("Debug UI")]
    public TMP_Text  debugText;
    public ScrollRect scrollRect;
    public int maxLines = 200;

    // ────────────────── Internos ──────────────────
    Aruco.Dictionary        dictionary;
    Aruco.DetectorParameters detectorParams;

    readonly Dictionary<int, Transform> markerNodes = new();
    readonly Dictionary<int, GameObject> prefabLookup = new();   // NEW

    ARAnchor rootAnchor;                   // único anchor raíz

    Cv.Mat cameraMatrix, distCoeffs;
    bool   intrinsicsInit;
    readonly StringBuilder logBuffer = new();
    float  nextDebugUpdate;
    bool warnedNoSize, warnedNoPrefab;

    // ────────────────── Init ──────────────────

     void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    void OnApplicationQuit()
    {
        // Restaurar el comportamiento por defecto (opcional)
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }
    
    void Awake()
    {
        camManager    ??= GetComponent<ARCameraManager>();


        rootAnchor = new GameObject("RootAnchor").AddComponent<ARAnchor>();

        // NEW: pasa lista a diccionario en O(1)
        foreach (var pair in customPrefabs)
            prefabLookup[pair.id] = pair.prefab ? pair.prefab : defaultPrefab;

        dictionary = Aruco.GetPredefinedDictionary(dictionaryName);
        detectorParams = new Aruco.DetectorParameters
        {
            AdaptiveThreshWinSizeMin     = 3,
            AdaptiveThreshWinSizeMax     = 23,
            AdaptiveThreshWinSizeStep    = 10,
            MinMarkerPerimeterRate       = 0.02f,
            MaxMarkerPerimeterRate       = 4.0f,
            PolygonalApproxAccuracyRate  = 0.05f,
            MinCornerDistanceRate        = 0.03f,
            MaxErroneousBitsInBorderRate = 0.5f,
            ErrorCorrectionRate          = 0.8f,
            CornerRefinementMethod       = Aruco.CornerRefineMethod.Subpix
        };

        cameraMatrix = new Cv.Mat();
        distCoeffs   = new Cv.Mat(1, 5, Cv.Type.CV_64F, new double[5]);
    }

    void OnEnable()  => camManager.frameReceived += OnFrame;
    void OnDisable() => camManager.frameReceived -= OnFrame;

    void OnValidate()
    {
    #if UNITY_EDITOR
        if (markerSideMeters <= 0f)
            Debug.LogWarning("[MultiArucoTracker] Marker Side Meters debe ser > 0 para estimar pose.");
        if (!defaultPrefab && (customPrefabs == null || customPrefabs.Count == 0))
            Debug.LogWarning("[MultiArucoTracker] Asigna Default Prefab o al menos un Custom Prefab.");
    #endif
    }

    void WarnOnce(ref bool flag, string msg)
    {
        if (!flag) { Debug.LogWarning(msg); flag = true; }
    }

    // ────────────────── Frame loop ──────────────────
    void OnFrame(ARCameraFrameEventArgs _){

        if (!camManager || !camManager.TryAcquireLatestCpuImage(out var cpuImage))
            return;

        if (!intrinsicsInit && camManager.TryGetIntrinsics(out var intr))
        {
            cameraMatrix = new Cv.Mat(3, 3, Cv.Type.CV_64F, new double[] {
                intr.focalLength.x, 0, intr.principalPoint.x,
                0, intr.focalLength.y, intr.principalPoint.y,
                0, 0, 1 });
            intrinsicsInit = true;
        }

        var conv = new XRCpuImage.ConversionParams
        {
            inputRect        = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
            outputFormat     = TextureFormat.R8,
            transformation   = XRCpuImage.Transformation.None
        };

        int byteCount = cpuImage.GetConvertedDataSize(conv);
        using var buffer = new NativeArray<byte>(byteCount, Allocator.Temp);
        cpuImage.Convert(conv, buffer);
        cpuImage.Dispose();

        bool portrait = Screen.orientation is ScreenOrientation.Portrait or ScreenOrientation.PortraitUpsideDown;
        var frame = new Cv.Mat(conv.outputDimensions.y, conv.outputDimensions.x, Cv.Type.CV_8UC1, buffer.ToArray());

        Std.VectorVectorPoint2f corners;
        Std.VectorInt ids;
        Aruco.DetectMarkers(frame, dictionary, out corners, out ids, detectorParams);

        // Nada detectado → limpia perdidos y debug opcional
        if (ids.Size() == 0)
        {
            // elimina todo lo que estaba y ya no está
            if (markerNodes.Count > 0)
            {
                foreach (var kv in markerNodes.ToList())
                {
                    Destroy(kv.Value.gameObject);
                    markerNodes.Remove(kv.Key);
                }
            }
            if (ShouldUpdateDebugUI()) Log("Markers: 0");
            return;
        }

        // Sin tamaño válido → no podemos estimar pose de ArUco
        if (markerSideMeters <= 0f)
        {
            WarnOnce(ref warnedNoSize, "[MultiArucoTracker] Estimación de pose deshabilitada: asigna Marker Side Meters > 0 (por ej. 0.025f para 25 mm).");
            if (ShouldUpdateDebugUI())
                Log($"Markers: {ids.Size()} (sin pose; MarkerSideMeters<=0)");
            return;
        }

        Std.VectorVec3d rvecs = null, tvecs = null;
        Aruco.EstimatePoseSingleMarkers(corners, markerSideMeters, cameraMatrix, distCoeffs, out rvecs, out tvecs);

        // Seguridad extra
        if (rvecs == null || tvecs == null || rvecs.Size() != ids.Size() || tvecs.Size() != ids.Size())
        {
            if (ShouldUpdateDebugUI())
                Log("Detectados sin rvecs/tvecs válidos");
            return;
        }

        var detected = new HashSet<int>();

        for (int i = 0; i < ids.Size(); ++i)
        {
            int id = ids.At((uint)i);
            detected.Add(id);

            // Selección de prefab
            GameObject prefab = prefabLookup.TryGetValue(id, out var custom) ? custom : defaultPrefab;
            if (!prefab)
            {
                WarnOnce(ref warnedNoPrefab, $"[MultiArucoTracker] No hay prefab asignado (ID {id}). Asigna Default Prefab o uno en Custom Prefabs.");
                continue;
            }

            Vector3    localPos = Cv2UnityPos(tvecs.At((uint)i), portrait);
            Quaternion localRot = Cv2UnityRot(rvecs.At((uint)i), portrait);

            if (!markerNodes.TryGetValue(id, out var node) || !node)
            {
                if (markerNodes.Count >= MAX_CONTENT_NODES) continue;

                var go = Instantiate(prefab);
                go.name = $"aruco_{id}";
                node = go.transform;
                node.SetParent(rootAnchor.transform, worldPositionStays: true);
                markerNodes[id] = node;
            }

            // AR pose → mundo → raíz
            Vector3 worldPos = camManager.transform.TransformPoint(localPos);
            Quaternion worldRot = camManager.transform.rotation * localRot;

            node.localPosition = rootAnchor.transform.InverseTransformPoint(worldPos);
            node.localRotation = Quaternion.Inverse(rootAnchor.transform.rotation) * worldRot;
        }

        // eliminar perdidos
        foreach (var kv in markerNodes.Where(k => !detected.Contains(k.Key)).ToList())
        {
            Destroy(kv.Value.gameObject);
            markerNodes.Remove(kv.Key);
        }

        if (ShouldUpdateDebugUI())
            Log($"Markers: {ids.Size()} | IDs: {string.Join(",", detected.Take(20))}{(ids.Size() > 20 ? "…" : "")}");
    }

    // ───────── conversion helpers ─────────
    static Vector3 Cv2UnityPos(Cv.Vec3d t,bool portrait)
    {
        double x=t.Get(0), y=t.Get(1), z=t.Get(2);
        if (portrait){ (x,y)=(y,x); }
        x=-x; y=-y;
        return new((float)x,(float)y,(float)z);
    }
    static Quaternion Cv2UnityRot(Cv.Vec3d r,bool portrait)
    {
        double x=r.Get(0), y=r.Get(1), z=r.Get(2);
        if (portrait){ (x,y)=(y,x); }
        x=-x; y=-y; z=-z;
        double ang=Mathf.Sqrt((float)(x*x+y*y+z*z));
        if (ang<1e-12) return Quaternion.identity;
        Vector3 axis=new((float)(x/ang),(float)(y/ang),(float)(z/ang));
        return Quaternion.AngleAxis((float)(ang*Mathf.Rad2Deg), axis);
    }

    // ───────── debug helpers ─────────
    bool ShouldUpdateDebugUI()
    {
        if (!debugText) return false;
        if (Time.unscaledTime < nextDebugUpdate) return false;
        nextDebugUpdate = Time.unscaledTime + 1f/DEBUG_FPS;
        return true;
    }
    void Log(string msg)
    {
        logBuffer.AppendLine(msg);
        var lines = logBuffer.ToString().Split('\n');
        int extra = lines.Length - maxLines;
        if (extra>0) { logBuffer.Clear(); for(int i=extra;i<lines.Length;i++) logBuffer.AppendLine(lines[i]); }
        debugText.text = logBuffer.ToString();
        Canvas.ForceUpdateCanvases();
        if (scrollRect) scrollRect.verticalNormalizedPosition=0f;
    }
}
