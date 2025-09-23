using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.XR.CoreUtils;         // XROrigin
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ArucoUnity.Plugin;
using Cv   = ArucoUnity.Plugin.Cv;
using Aruco= ArucoUnity.Plugin.Aruco;
using Std  = ArucoUnity.Plugin.Std;
using TMPro;
using System.Text;
using System.Linq;
using System.Collections;

public sealed class MultiArucoTracker : MonoBehaviour
{
    const int MAX_CONTENT_NODES = 5; // ⟵ máximo de prefabs simultáneos
    const float DEBUG_FPS = 4f;

    // ────────────────── Inspector ──────────────────
    [Header("Managers")]
    [SerializeField] ARCameraManager camManager;

    [Header("Prefabs")]
    public GameObject defaultPrefab;

    [System.Serializable]
    public struct IdPrefabPair { public int id; public GameObject prefab; }
    [Tooltip("Asignar un prefab distinto para IDs concretos")]
    public List<IdPrefabPair> customPrefabs = new();

    [Header("Detection Settings")]
    public float markerSideMeters = 0.025f;
    [SerializeField, Range(0.001f, 0.01f)]
    float stepSize = 0.01f;
    public Aruco.PredefinedDictionaryName dictionaryName = Aruco.PredefinedDictionaryName.Dict4x4_50;

    [Header("Debug UI")]
    public TMP_Text debugText;

    // ────────────────── Internos ──────────────────
    Aruco.Dictionary dictionary;
    Aruco.DetectorParameters detectorParams;

    readonly Dictionary<int, Transform> markerNodes = new();
    readonly Dictionary<int, GameObject> prefabLookup = new();

    Transform arucoRoot;

    Cv.Mat cameraMatrix, distCoeffs;
    bool intrinsicsInit;
    readonly StringBuilder logBuffer = new();
    float nextDebugUpdate;
    bool warnedNoSize, warnedNoPrefab;

    [Header("ARF Integration")]
    [SerializeField] Transform trackablesParent;
    [SerializeField] XROrigin xrOrigin;

    // ─── WATCHDOG / RESILIENCIA ───
    [Header("Resiliencia")]
    [Tooltip("Segundos sin frames para considerar 'detención' y reiniciar la detección.")]
    public float stallSeconds = 2.0f;
    [Tooltip("Backoff entre intentos de re-suscripción.")]
    public float restartBackoffSeconds = 0.5f;
    float _lastHeartbeat;               // Time.realtimeSinceStartup del último frame recibido
    Coroutine _watchdogCo;
    bool _subscribed;
    bool _processingFrame;              // evita reentradas si llega otro frame durante procesamiento
    int _restartCount;

    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    void OnApplicationQuit()
    {
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    void Awake()
    {
        camManager ??= GetComponent<ARCameraManager>();

        if (xrOrigin == null) xrOrigin = FindAnyObjectByType<XROrigin>();
        if (trackablesParent == null && xrOrigin != null) trackablesParent = xrOrigin.TrackablesParent;
        if (trackablesParent == null && xrOrigin != null)
        {
            var t = xrOrigin.transform.Find("Trackables");
            if (t) trackablesParent = t;
        }
        if (trackablesParent == null)
            Debug.LogError("[MultiArucoTracker] No se encontró TrackablesParent. Asigna XR Origin/Trackables.");

        arucoRoot = new GameObject("ArucoRoot").transform;
        if (trackablesParent) arucoRoot.SetParent(trackablesParent, false);

        foreach (var pair in customPrefabs)
            prefabLookup[pair.id] = pair.prefab ? pair.prefab : defaultPrefab;

        dictionary = Aruco.GetPredefinedDictionary(dictionaryName);
        detectorParams = new Aruco.DetectorParameters
        {
            AdaptiveThreshWinSizeMin = 3,
            AdaptiveThreshWinSizeMax = 23,
            AdaptiveThreshWinSizeStep = 10,
            MinMarkerPerimeterRate = 0.02f,
            MaxMarkerPerimeterRate = 4.0f,
            PolygonalApproxAccuracyRate = 0.05f,
            MinCornerDistanceRate = 0.03f,
            MaxErroneousBitsInBorderRate = 0.5f,
            ErrorCorrectionRate = 0.8f,
            CornerRefinementMethod = Aruco.CornerRefineMethod.Subpix
        };

        cameraMatrix = new Cv.Mat();
        distCoeffs = new Cv.Mat(1, 5, Cv.Type.CV_64F, new double[5]);
    }

    void OnEnable()
    {
        SafeSubscribe();
        _watchdogCo = StartCoroutine(WatchdogLoop());
    }

    void OnDisable()
    {
        if (_watchdogCo != null) { StopCoroutine(_watchdogCo); _watchdogCo = null; }
        SafeUnsubscribe();
    }

    void OnApplicationPause(bool paused)
    {
        // Al volver de pausa, espera un frame y re-suscribe para recuperar el evento si se perdió
        if (!paused) StartCoroutine(ResumeAfterPause());
    }

    IEnumerator ResumeAfterPause()
    {
        yield return null; // esperar 1 frame
        ForceRestartDetection();
    }

    void SafeSubscribe()
    {
        if (camManager == null || _subscribed) return;
        camManager.frameReceived += OnFrame;
        _subscribed = true;
        _lastHeartbeat = Time.realtimeSinceStartup;
        // Opcional: Log($"Suscrito a frameReceived. (reinicios: {_restartCount})");
    }

    void SafeUnsubscribe()
    {
        if (camManager == null || !_subscribed) return;
        camManager.frameReceived -= OnFrame;
        _subscribed = false;
    }

    IEnumerator WatchdogLoop()
    {
        var wait = new WaitForSeconds(restartBackoffSeconds);
        while (true)
        {
            yield return wait;

            // Si AR no está tracking, no reiniciar agresivamente
            if (ARSession.state == ARSessionState.None ||
                ARSession.state == ARSessionState.CheckingAvailability ||
                ARSession.state == ARSessionState.NeedsInstall ||
                ARSession.state == ARSessionState.Installing ||
                ARSession.state == ARSessionState.Ready ||
                ARSession.state == ARSessionState.SessionInitializing)
            {
                // Aún no hay tracking estable; reinicia el latido para no disparar falsos positivos
                _lastHeartbeat = Time.realtimeSinceStartup;
                continue;
            }

            // Si estamos en Tracking pero sin latidos por 'stallSeconds' → reintentar
            if (Time.realtimeSinceStartup - _lastHeartbeat > stallSeconds)
            {
                ForceRestartDetection();
            }
        }
    }

    /// <summary>Reinicia la suscripción al evento y re-inicializa buffers livianos.</summary>
    public void ForceRestartDetection()
    {
        _restartCount++;
        SafeUnsubscribe();

        // Limpia estados que podrían haber quedado inconsistentes
        _processingFrame = false;

        // (OPCIONAL) Limpia nodos si quieres forzar re-poblado al volver a detectar:
        // foreach (var kv in markerNodes.ToList()) { if (kv.Value) Destroy(kv.Value.gameObject); }
        // markerNodes.Clear();

        // Re-suscribe
        SafeSubscribe();
        Log($"[Aruco] Watchdog: reinicio #{_restartCount} (ARState={ARSession.state})");
    }

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
    void OnFrame(ARCameraFrameEventArgs _)
    {
        // Latido al inicio: si el evento llega, hay “vida”
        _lastHeartbeat = Time.realtimeSinceStartup;

        if (_processingFrame) return; // evita reentradas si el dispositivo dispara frames muy rápido
        _processingFrame = true;

        try
        {
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
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
                outputFormat = TextureFormat.R8,
                transformation = XRCpuImage.Transformation.None
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

            if (ids.Size() == 0)
            {
                if (markerNodes.Count > 0)
                {
                    foreach (var kv in markerNodes.ToList())
                    {
                        if (kv.Value) Destroy(kv.Value.gameObject);
                        markerNodes.Remove(kv.Key);
                    }
                }
                return;
            }

            if (markerSideMeters <= 0f)
            {
                WarnOnce(ref warnedNoSize, "[MultiArucoTracker] Estimación de pose deshabilitada: asigna Marker Side Meters > 0.");
                Log($"Markers: {ids.Size()} (sin pose; MarkerSideMeters<=0)");
                return;
            }

            Std.VectorVec3d rvecs = null, tvecs = null;
            Aruco.EstimatePoseSingleMarkers(corners, markerSideMeters, cameraMatrix, distCoeffs, out rvecs, out tvecs);

            if (rvecs == null || tvecs == null || rvecs.Size() != ids.Size() || tvecs.Size() != ids.Size())
            {
                Log("Detectados sin rvecs/tvecs válidos");
                return;
            }

            var detected = new HashSet<int>();

            for (int i = 0; i < ids.Size(); ++i)
            {
                int id = ids.At((uint)i);
                detected.Add(id);

                GameObject prefab = prefabLookup.TryGetValue(id, out var custom) ? custom : defaultPrefab;
                if (!prefab)
                {
                    WarnOnce(ref warnedNoPrefab, $"[MultiArucoTracker] No hay prefab asignado (ID {id}).");
                    continue;
                }

                Vector3 localPos = Cv2UnityPos(tvecs.At((uint)i), portrait);
                Quaternion localRot = Cv2UnityRot(rvecs.At((uint)i), portrait);

                if (!markerNodes.TryGetValue(id, out var node) || !node)
                {
                    if (markerNodes.Count >= MAX_CONTENT_NODES) continue;
                    var go = Instantiate(prefab);
                    go.name = $"aruco_{id}";
                    node = go.transform;
                    node.SetParent(arucoRoot, worldPositionStays: true);
                    markerNodes[id] = node;
                }

                Vector3 worldPos = camManager.transform.TransformPoint(localPos);
                Quaternion worldRot = camManager.transform.rotation * localRot;

                node.position = worldPos;
                node.rotation = worldRot;
            }

            foreach (var kv in markerNodes.Where(k => !detected.Contains(k.Key)).ToList())
            {
                if (kv.Value) Destroy(kv.Value.gameObject);
                markerNodes.Remove(kv.Key);
            }
        }
        catch (System.Exception ex)
        {
            // Cualquier excepción NO mata el loop. Loguea y deja que el watchdog re-suscriba si el evento se corta.
            Debug.LogError($"[Aruco] Excepción en OnFrame: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            _processingFrame = false;
        }
    }

    // ───────── conversion helpers ─────────
    static Vector3 Cv2UnityPos(Cv.Vec3d t, bool portrait)
    {
        double x = t.Get(0), y = t.Get(1), z = t.Get(2);
        if (portrait) { (x, y) = (y, x); }
        x = -x; y = -y;
        return new((float)x, (float)y, (float)z);
    }
    static Quaternion Cv2UnityRot(Cv.Vec3d r, bool portrait)
    {
        double x = r.Get(0), y = r.Get(1), z = r.Get(2);
        if (portrait) { (x, y) = (y, x); }
        x = -x; y = -y; z = -z;
        double ang = Mathf.Sqrt((float)(x * x + y * y + z * z));
        if (ang < 1e-12) return Quaternion.identity;
        Vector3 axis = new((float)(x / ang), (float)(y / ang), (float)(z / ang));
        return Quaternion.AngleAxis((float)(ang * Mathf.Rad2Deg), axis);
    }

    // ───────── debug helpers ─────────
    void Log(string m) { Debug.Log(m); if (debugText) debugText.text = m; }

    public bool TryGetMarkerPose(int id, out Vector3 worldPos, out Quaternion worldRot)
    {
        if (markerNodes.TryGetValue(id, out var node) && node)
        {
            worldPos = node.position;
            worldRot = node.rotation;
            return true;
        }
        worldPos = default;
        worldRot = default;
        return false;
    }

    public void IncreaseMarkerSize()
    {
        markerSideMeters = Mathf.Clamp(markerSideMeters + stepSize, 0.01f, 1f);
        Log($"Marker size → {markerSideMeters:F3} m");
    }

    public void DecreaseMarkerSize()
    {
        markerSideMeters = Mathf.Clamp(markerSideMeters - stepSize, 0.01f, 1f);
        Log($"Marker size → {markerSideMeters:F3} m");
    }
}
