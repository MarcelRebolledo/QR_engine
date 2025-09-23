using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;
using TMPro;
using Google.XR.ARCoreExtensions.Samples.PersistentCloudAnchors;

public class AnchorSceneController : MonoBehaviour
{
  [Header("AR Foundation / Extensions")]
  [SerializeField] Camera arCamera;
  [SerializeField] ARCoreExtensions arCoreExtensions;
  [SerializeField] ARAnchorManager anchorManager;
  [SerializeField] ARPlaneManager planeManager;
  [SerializeField] ARRaycastManager raycastManager;
  [SerializeField] ARPointCloudManager pointCloudManager;

  [Header("UI / Visual")]
  [SerializeField] GameObject anchorVisualPrefab;
  [SerializeField] GameObject mapQualityIndicatorPrefab;
  [SerializeField] TMP_Text debugText;

  [Header("Hosting")]
  [SerializeField, Range(1, 365)] int cloudTtlDays = 365;
  [SerializeField] FeatureMapQuality minQualityToHost = FeatureMapQuality.Sufficient;
  [SerializeField] bool requireIndicatorCoverage = false;
  [SerializeField] bool logVerbose = true;

  // límites similares al sample
  const float kStartPrepareTime = 3f;
  const float kMinDistFactor = 1.5f;
  const float kMaxDistMeters = 10f;

  [Header("Placement Controls")]
  [SerializeField] float rotationSpeedDeg = 90f;
  [SerializeField] bool rotateAroundAnchorUp = true;
  [SerializeField] GameObject botonguardar;

  //net
  [SerializeField] LanDiscovery lan;

  // Estados internos
  ARAnchor _localAnchor;
  ARCloudAnchor _cloudAnchor;
  GameObject _visual;
  Transform _visualRoot;
  MapQualityIndicator _qualityIndicator;
  string _cloudId;

  HostCloudAnchorPromise _hostPromise;
  ResolveCloudAnchorPromise _resolvePromise;
  Coroutine _resolveLoop;

  // Captura/mapeo
  bool _mappingActive = false;   // <- ahora controlado por botones
  float _sinceMapStart = 0f;

  // Rotación “hold”
  bool _rotLeftHeld, _rotRightHeld;

  // ---------- BOTONES BÁSICOS ----------
  public void BtnCrear() { TogglePlanes(true); TogglePointCloud(true); Log("Escaneo ON."); }

  public void BtnPlaceOrReplace() { StartCoroutine(PlaceAnchorOnly()); }
  void Start()
  {

    Screen.sleepTimeout = SleepTimeout.NeverSleep;

    if (lan != null)
    {
      lan.OnAnchorShared += OnAnchorShared;
      lan.OnStatus += (dev, st) => Debug.Log($"[LAN] {dev} -> {st}");
    }
  }

  public void reiniciar()
  {
    ClearAll();
    TogglePlanes(true);
    TogglePointCloud(true);
    Log("Reinicio.");
  }

  public void BtnIniciar()
  {
    TogglePlanes(false);
    TogglePointCloud(false);
    if (_resolveLoop != null) StopCoroutine(_resolveLoop);
    _resolveLoop = StartCoroutine(KeepResolvingLoop());
    Log("Iniciar: UI OFF, escaneo OFF, auto-resolve ON.");
  }

  // ---------- NUEVOS BOTONES DE CAPTURA ----------
  // 1) Iniciar captura: crea/activa el indicador y empieza a evaluar calidad (sin hostear aún)
  public void BtnStartCapture()
  {
    if (_localAnchor == null && _cloudAnchor == null)
    { Log("Coloca primero el anchor (botón central)."); return; }

    EnsureQualityIndicator();
    TogglePlanes(false);        // como el sample: enfócate en el objeto
    TogglePointCloud(true);     // feedback de nube ON
    _mappingActive = true;
    _sinceMapStart = 0f;

    if (botonguardar) botonguardar.SetActive(false);

    Log("Captura iniciada: camina alrededor para mapear. (warm-up)");
  }


  // ---------- FLUJO DE PLACEMENT (sin iniciar captura) ----------
  IEnumerator PlaceAnchorOnly()
  {
    CancelOps();
    ClearAll();

    // Raycast a plano bajo el centro de pantalla
    var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    var hits = new List<ARRaycastHit>();
    if (!raycastManager.Raycast(center, hits, TrackableType.PlaneWithinPolygon))
    { Log("No hay plano bajo el centro. Muévete y reintenta."); yield break; }

    var hit = hits[0];
    var plane = planeManager.GetPlane(hit.trackableId);
    if (plane == null) { Log("El trackable del raycast no es un ARPlane."); yield break; }

    _localAnchor = anchorManager.AttachAnchor(plane, hit.pose);
    if (_localAnchor == null) { Log("AttachAnchor falló."); yield break; }

    EnsureVisualRootUnder(_localAnchor.transform);

    // NO creamos indicador ni empezamos a mapear todavía
    Log("Anchor colocado. Ahora puedes rotarlo y luego pulsar 'Iniciar captura'.");
  }

  // ---------- CICLO DE ACTUALIZACIÓN ----------
  void Update()
  {
    if (!_mappingActive) return;

    // warm-up antes de evaluar
    _sinceMapStart += Time.deltaTime;
    if (_sinceMapStart < kStartPrepareTime)
    {
      Log($"Preparando ARCore... {(_sinceMapStart / kStartPrepareTime * 100f):0}%");
      return;
    }

    // Solo actualizar feedback de calidad (NO hostear aquí)
    UpdateMappingFeedback();
  }

  // ---------- EVALUACIÓN DE CALIDAD (feedback) ----------
  void UpdateMappingFeedback()
  {
    if (ARSession.state != ARSessionState.SessionTracking)
    { Log($"No tracking ({ARSession.notTrackingReason})."); if (botonguardar) botonguardar.SetActive(false); return; }

    var tAnchor = GetActiveAnchorTransform();
    if (tAnchor == null)
    { Log("No hay anchor activo."); if (botonguardar) botonguardar.SetActive(false); return; }

    Pose camPose = new Pose(arCamera.transform.position, arCamera.transform.rotation);
    FeatureMapQuality q = anchorManager.EstimateFeatureMapQualityForHosting(camPose);
    if (_qualityIndicator) _qualityIndicator.UpdateQualityState((int)q);

    Vector3 refPos = (_qualityIndicator ? _qualityIndicator.transform.position : tAnchor.position);
    float dist = Vector3.Distance(refPos, arCamera.transform.position);

    string why;
    bool canSave = IsQualityAcceptable(out why);
    if (canSave)
    {
      _mappingActive = false;
      TryHostNow();
    }

    if (logVerbose)
      Log($"Mapping quality: {anchorManager.EstimateFeatureMapQualityForHosting(new Pose(arCamera.transform.position, arCamera.transform.rotation))} | saveReady={canSave} ({why})");

  }

  // ---------- INTENTO ÚNICO DE HOSTEO (al pulsar “terminar”) ----------
  void TryHostNow()
  {
    if (_localAnchor == null || _hostPromise != null || !string.IsNullOrEmpty(_cloudId))
    { return; }

    if (ARSession.state != ARSessionState.SessionTracking)
    { return; }

    // Evalúa las mismas reglas del sample
    Pose camPose = new Pose(arCamera.transform.position, arCamera.transform.rotation);
    FeatureMapQuality q = anchorManager.EstimateFeatureMapQualityForHosting(camPose);
    var tAnchor = GetActiveAnchorTransform();
    Vector3 refPos = (_qualityIndicator ? _qualityIndicator.transform.position : tAnchor.position);
    float dist = Vector3.Distance(refPos, arCamera.transform.position);

    if (_qualityIndicator && dist < _qualityIndicator.Radius * kMinDistFactor)
    { return; }
    if (dist > kMaxDistMeters)
    { return; }
    if (_qualityIndicator && _qualityIndicator.ReachTopviewAngle)
    { return; }

    bool ok = (q >= minQualityToHost);
    if (requireIndicatorCoverage && _qualityIndicator)
      ok &= _qualityIndicator.ReachQualityThreshold;

    if (!ok)
    {
      return;
    }

    Log("Calidad suficiente. Hosteando Cloud Anchor…");
    _hostPromise = anchorManager.HostCloudAnchorAsync(_localAnchor, Mathf.Clamp(cloudTtlDays, 1, 365));
    StartCoroutine(WaitHostResult());
  }


  bool IsQualityAcceptable(out string reason)
  {
    reason = "";

    if (ARSession.state != ARSessionState.SessionTracking)
    { reason = $"No tracking ({ARSession.notTrackingReason})"; return false; }

    var tAnchor = GetActiveAnchorTransform();
    if (tAnchor == null)
    { reason = "No hay anchor activo."; return false; }

    Pose camPose = new Pose(arCamera.transform.position, arCamera.transform.rotation);
    FeatureMapQuality q = anchorManager.EstimateFeatureMapQualityForHosting(camPose);

    // distancia / top-view (mismas reglas del sample)
    Vector3 refPos = (_qualityIndicator ? _qualityIndicator.transform.position : tAnchor.position);
    float dist = Vector3.Distance(refPos, arCamera.transform.position);
    if (_qualityIndicator && dist < _qualityIndicator.Radius * kMinDistFactor)
    { reason = "Muy cerca: aléjate un poco."; return false; }
    if (dist > kMaxDistMeters)
    { reason = "Muy lejos: acércate."; return false; }
    if (_qualityIndicator && _qualityIndicator.ReachTopviewAngle)
    { reason = "Evita vista superior; rodea desde varios lados."; return false; }

    // umbral de calidad
    bool ok = (q >= minQualityToHost);
    if (requireIndicatorCoverage && _qualityIndicator)
      ok &= _qualityIndicator.ReachQualityThreshold;

    reason = ok ? "Calidad OK" : $"Calidad insuficiente ({q})";
    return ok;
  }

  IEnumerator WaitHostResult()
  {
    yield return _hostPromise;
    var host = _hostPromise.Result;
    _hostPromise = null;

    if (host.CloudAnchorState == CloudAnchorState.Success)
    {
      _cloudId = host.CloudAnchorId;
      GUIUtility.systemCopyBuffer = _cloudId;
      Log($"Host OK → Cloud ID: {_cloudId} (copiado).");
      if (_visualRoot)
      {
        float yaw = _visualRoot.localEulerAngles.y;
        lan?.SendAnchor(_cloudId, yaw);          // ← comparte id + yaw a la red
        lan?.SendStatus("hosted");
      }


      if (_qualityIndicator) { Destroy(_qualityIndicator.gameObject); _qualityIndicator = null; }

      botonguardar.SetActive(true);

    }
    else
    {
      Log($"Host FAILED: {host.CloudAnchorState}. Reinicia la app");
      // puedes volver a activar captura si quieres:
      //_mappingActive = true;
      _sinceMapStart = 0f;
    }
  }

  IEnumerator KeepResolvingLoop()
  {
    while (true)
    {
      if (string.IsNullOrEmpty(_cloudId)) { yield return null; continue; }

      if ((_cloudAnchor && _cloudAnchor.trackingState == TrackingState.Tracking) ||
          (_localAnchor && _localAnchor.trackingState == TrackingState.Tracking))
      { yield return null; continue; }

      if (_resolvePromise != null) { yield return null; continue; }

      Log("Resolviendo Cloud Anchor…");
      _resolvePromise = anchorManager.ResolveCloudAnchorAsync(_cloudId);
      yield return _resolvePromise;

      var res = _resolvePromise.Result;
      _resolvePromise = null;

      if (res.CloudAnchorState == CloudAnchorState.Success)
      {
        Log("Resolve OK.");
        if (_localAnchor) { Destroy(_localAnchor.gameObject); _localAnchor = null; }
        if (_cloudAnchor) { Destroy(_cloudAnchor.gameObject); _cloudAnchor = null; }
        _cloudAnchor = res.Anchor;

        EnsureVisualRootUnder(_cloudAnchor.transform);
      }
      else
      {
        Log($"Resolve FAILED: {res.CloudAnchorState}. Reintento en 10.5s.");
        yield return new WaitForSeconds(10.5f);
      }
      yield return null;
    }
  }

  // ---------- Helpers ----------
  void TogglePlanes(bool on)
  {
    if (!planeManager) return;
    planeManager.enabled = on;
    foreach (var p in planeManager.trackables) if (p) p.gameObject.SetActive(on);
  }
  void TogglePointCloud(bool on)
  {
    if (!pointCloudManager) return;
    pointCloudManager.enabled = on;
    foreach (var pc in pointCloudManager.trackables) if (pc) pc.gameObject.SetActive(on);
  }
  void ClearAll()
  {
    if (_qualityIndicator) { Destroy(_qualityIndicator.gameObject); _qualityIndicator = null; }
    if (_visual) { Destroy(_visual); _visual = null; }
    if (_localAnchor) { Destroy(_localAnchor.gameObject); _localAnchor = null; }
    if (_cloudAnchor) { Destroy(_cloudAnchor.gameObject); _cloudAnchor = null; }
    if (botonguardar) botonguardar.SetActive(false); // <-- por si acaso
  }
  void CancelOps()
  {
    if (_hostPromise != null) { _hostPromise.Cancel(); _hostPromise = null; }
    if (_resolvePromise != null) { _resolvePromise.Cancel(); _resolvePromise = null; }
  }
  void Log(string m) { Debug.Log(m); if (debugText) debugText.text = m; }

  Transform GetActiveAnchorTransform()
  {
    if (_cloudAnchor != null) return _cloudAnchor.transform;
    if (_localAnchor != null) return _localAnchor.transform;
    return null;
  }

  void EnsureQualityIndicator()
  {
    if (_qualityIndicator != null) return;

    var t = GetActiveAnchorTransform();
    if (t == null || mapQualityIndicatorPrefab == null) return;

    // Por defecto, asumimos plano horizontal (si necesitas, guarda el alignment del ARPlane al crear el anchor)
    var go = Instantiate(mapQualityIndicatorPrefab, t);
    _qualityIndicator = go.GetComponent<MapQualityIndicator>();
    _qualityIndicator.DrawIndicator(PlaneAlignment.HorizontalUp, arCamera);
  }

  // ---------- Rotación continua ----------
  void LateUpdate()
  {
    int dir = (_rotRightHeld ? 1 : 0) - (_rotLeftHeld ? 1 : 0);
    if (dir != 0 && _visualRoot != null)
    {
      // Eje de rotación: up del anchor si existe, si no Y global
      var anchorT = GetActiveAnchorTransform();
      Vector3 axis = rotateAroundAnchorUp
          ? (anchorT != null ? anchorT.up : Vector3.up)
          : Vector3.up;

      _visualRoot.Rotate(axis, dir * rotationSpeedDeg * Time.deltaTime, Space.World);
    }
  }
  public void OnRotateLeftDown() { _rotLeftHeld = true; }
  public void OnRotateLeftUp() { _rotLeftHeld = false; }
  public void OnRotateRightDown() { _rotRightHeld = true; }
  public void OnRotateRightUp() { _rotRightHeld = false; }

  void OnAnchorShared(string cloudId, string yawStr)
  {
    // Parsear "y=37.5"
    float yaw = 0f;
    if (yawStr.StartsWith("y="))
      float.TryParse(yawStr.Substring(2), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out yaw);

    // Ajusta tu flujo “Unirse”: setea _cloudId, arranca resolve y aplica yaw al VisualRoot
    _cloudId = cloudId;

    // si ya resolviste antes, re-aplica; si no, al Resolve OK:
    StartCoroutine(StartResolveAndApplyYaw(yaw));
  }

  IEnumerator StartResolveAndApplyYaw(float yaw)
  {
    if (string.IsNullOrEmpty(_cloudId)) yield break;

    // corta cualquier resolve previo
    if (_resolvePromise != null) { _resolvePromise.Cancel(); _resolvePromise = null; }

    // dispara resolve
    _resolvePromise = anchorManager.ResolveCloudAnchorAsync(_cloudId);
    yield return _resolvePromise;
    var res = _resolvePromise.Result;
    _resolvePromise = null;

    if (res.CloudAnchorState == CloudAnchorState.Success)
    {
      _cloudAnchor = res.Anchor;

      // asegurar VisualRoot bajo el cloud anchor
      if (_visualRoot == null) _visualRoot = new GameObject("VisualRoot").transform;
      _visualRoot.SetParent(_cloudAnchor.transform, false);

      if (_visual == null && anchorVisualPrefab) _visual = Instantiate(anchorVisualPrefab, _visualRoot);
      _visualRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);

      

      Debug.Log($"[LAN] Resolved & applied yaw {yaw:0.##}°");
      lan?.SendStatus("resolving_ok");
    }
    else
    {
      Debug.LogWarning($"[LAN] Resolve FAIL {res.CloudAnchorState}");
      lan?.SendStatus("resolving_fail");
    }
  }

  void EnsureVisualRootUnder(Transform parent)
  {
    if (_visualRoot == null)
      _visualRoot = new GameObject("VisualRoot").transform;

    _visualRoot.SetParent(parent, false);

    // Asegura que el visual exista como hijo de VisualRoot
    if (_visual == null && anchorVisualPrefab != null)
      _visual = Instantiate(anchorVisualPrefab, _visualRoot);
    else if (_visual != null && _visual.transform.parent != _visualRoot)
      _visual.transform.SetParent(_visualRoot, true);
     
  }

  void OnDestroy()
  {
    if (lan != null)
    {
      lan.OnAnchorShared -= OnAnchorShared;
    }
  }
  
}
