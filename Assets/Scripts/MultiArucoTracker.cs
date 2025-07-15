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

        var conv = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
            outputFormat = TextureFormat.RGB24,        // 3 canales uint8
            transformation = XRCpuImage.Transformation.MirrorY
        };

        int byteCount = cpuImage.GetConvertedDataSize(conv);
        using var buffer = new NativeArray<byte>(byteCount, Allocator.Temp);
        cpuImage.Convert(conv, buffer);
        cpuImage.Dispose();

        // ② Native → OpenCV ---------------------------------------------------
        byte[] managed = buffer.ToArray();
        Cv.Mat frame = new Cv.Mat(conv.outputDimensions.y, conv.outputDimensions.x, Cv.Type.CV_8UC3, managed);
        Std.VectorVectorPoint2f corners;
        Std.VectorInt ids;

        Aruco.DetectMarkers(frame, dictionary, out corners, out ids, detectorParams);
        Log($"Detected {ids.Size()} markers");


        // ③ Crear/actualizar anclas ------------------------------------------
        if (ids.Size() == 0) return;

        for (int i = 0; i < ids.Size(); ++i)
        {
            int id = ids.At((uint)i);
            if (anchors.ContainsKey(id)) continue;        // Ya existe

            // (Ejemplo) coloca el objeto 30 cm frente a la cámara.
            Pose pose = new(
                camManager.transform.position + camManager.transform.forward * 0.30f,
                camManager.transform.rotation);


            // ─── Crear ancla ───────────────────────────────────────
            ARAnchor anchor = null;

            // Si hay AnchorManager y su subsistema admite alta sincrónica
            if (anchorManager &&
                anchorManager.subsystem != null &&
                anchorManager.subsystem.TryAddAnchor(pose, out XRAnchor xrAnchor))
            {
                // ARAnchorManager generará el ARAnchor la siguiente frame;
                // lo consultamos de inmediato (puede ser null si aún está pending).
                anchor = anchorManager.GetAnchor(xrAnchor.trackableId);
            }

            // Fallback: editor, simulación o proveedor sin add sincrónico
            if (!anchor)
            {
                var go = new GameObject($"aruco_{id}");
                go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                anchor = go.AddComponent<ARAnchor>();
            }

            if (!anchor)
            {
                // Fallback sin AnchorManager (p.ej. editor)
                var go = new GameObject($"aruco_{id}");
                go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                anchor = go.AddComponent<ARAnchor>();
            }

            anchors[id] = anchor;
            Instantiate(contentPrefab, anchor.transform, false);
        }
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
