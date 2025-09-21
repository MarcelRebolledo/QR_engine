// Assets/Scripts/AR/SimpleCloudAnchorUI.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;

namespace Google.XR.ARCoreExtensions.Samples.PersistentCloudAnchors
{
  public class SimpleCloudAnchorUI : MonoBehaviour
  {
    [Header("Controller del sample")]
    [SerializeField] private PersistentCloudAnchorsController Controller;

    [Header("Managers extra")]
    [SerializeField] private ARPointCloudManager PointCloudManager;

    [Header("Visual y UI")]
    [SerializeField] private GameObject AnchorVisualPrefab;
    [SerializeField] private GameObject UIRoot;

    [Header("Opciones")]
    [SerializeField, Range(1,365)] private int CloudTtlDays = 7;

    // Estado
    private ARAnchor _localAnchor;            // anchor local (adjunto a plano)
    private ARCloudAnchor _cloudAnchor;       // anchor resuelto desde la nube
    private GameObject _visual;
    private string _cloudId;
    private ResolveCloudAnchorPromise _resolvePromise;
    private HostCloudAnchorPromise _hostPromise;
    private Coroutine _resolveLoop;

    // --- Botones ---
    public void OnCrear()
    {
      TogglePlanes(true);
      TogglePointCloud(true);
      Debug.Log("[UI] Escaneo ON (planos + nube).");
    }

    public void OnPlaceOrReplace()
    {
      StartCoroutine(PlaceAndHostAtCenter());
    }

    public void OnIniciar()
    {
      if (UIRoot) UIRoot.SetActive(false);
      TogglePlanes(false);
      TogglePointCloud(false);

      if (_resolveLoop != null) StopCoroutine(_resolveLoop);
      _resolveLoop = StartCoroutine(KeepResolvingLoop());
      Debug.Log("[UI] Iniciar: UI OFF, escaneo OFF, anchor visible con auto-resolve.");
    }

    // --- Lógica ---
    private IEnumerator PlaceAndHostAtCenter()
    {
      CancelPromises();
      ClearAnchors();

      // Raycast SOLO a planos para evitar el AddAnchor geoespacial
      var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
      var hits = new List<ARRaycastHit>();
      var rayTypes = TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated;

      if (!Controller.RaycastManager.Raycast(center, hits, rayTypes))
      {
        Debug.LogWarning("[Anchor] No se detectó plano en el centro. Muévete o apunta a una superficie con textura.");
        yield break;
      }

      var hit = hits[0];
      var plane = Controller.PlaneManager.GetPlane(hit.trackableId);
      if (plane == null)
      {
        Debug.LogWarning("[Anchor] El Trackable no es un ARPlane válido.");
        yield break;
      }

      // Adjuntar SIEMPRE a un plano (evita AddAnchor(Pose) geoespacial)
      _localAnchor = Controller.AnchorManager.AttachAnchor(plane, hit.pose);
      if (_localAnchor == null)
      {
        Debug.LogError("[Anchor] Falló AttachAnchor sobre el plano.");
        yield break;
      }

      if (AnchorVisualPrefab)
        _visual = Instantiate(AnchorVisualPrefab, _localAnchor.transform);

      Debug.Log("[Anchor] Anchor local creado. Hosteando en la nube…");
      _hostPromise = Controller.AnchorManager.HostCloudAnchorAsync(_localAnchor, Mathf.Clamp(CloudTtlDays, 1, 365));
      yield return _hostPromise;

      var hostResult = _hostPromise.Result;
      _hostPromise = null;

      if (hostResult.CloudAnchorState == CloudAnchorState.Success)
      {
        _cloudId = hostResult.CloudAnchorId;
        GUIUtility.systemCopyBuffer = _cloudId; // copiar al portapapeles
        Debug.Log($"[Anchor] Host OK. Cloud ID = {_cloudId} (copiado al portapapeles).");
      }
      else
      {
        Debug.LogError($"[Anchor] Host FAILED: {hostResult.CloudAnchorState}");
      }
    }

    private IEnumerator KeepResolvingLoop()
    {
      while (true)
      {
        if (string.IsNullOrEmpty(_cloudId)) { yield return null; continue; }

        // Si ya hay tracking en alguno, no resolvemos
        if ((_cloudAnchor && _cloudAnchor.trackingState == TrackingState.Tracking) ||
            (_localAnchor && _localAnchor.trackingState == TrackingState.Tracking))
        { yield return null; continue; }

        if (_resolvePromise != null) { yield return null; continue; }

        Debug.Log("[Resolve] Intentando resolver Cloud Anchor…");
        _resolvePromise = Controller.AnchorManager.ResolveCloudAnchorAsync(_cloudId);
        yield return _resolvePromise;

        var res = _resolvePromise.Result;
        _resolvePromise = null;

        if (res.CloudAnchorState == CloudAnchorState.Success)
        {
          Debug.Log("[Resolve] OK. Adoptando anchor resuelto.");
          if (_localAnchor) { Destroy(_localAnchor.gameObject); _localAnchor = null; }
          if (_cloudAnchor) { Destroy(_cloudAnchor.gameObject); _cloudAnchor = null; }
          _cloudAnchor = res.Anchor;

          if (_visual != null) _visual.transform.SetParent(_cloudAnchor.transform, false);
          else if (AnchorVisualPrefab) _visual = Instantiate(AnchorVisualPrefab, _cloudAnchor.transform);
        }
        else
        {
          Debug.LogWarning($"[Resolve] FAILED: {res.CloudAnchorState}. Reintento en 1.5s.");
          yield return new WaitForSeconds(1.5f);
        }

        yield return null;
      }
    }

    // --- Helpers ---
    private void TogglePlanes(bool visible)
    {
      if (!Controller || !Controller.PlaneManager) return;
      Controller.PlaneManager.enabled = visible;
      foreach (var p in Controller.PlaneManager.trackables)
        if (p) p.gameObject.SetActive(visible);
    }

    private void TogglePointCloud(bool visible)
    {
      if (!PointCloudManager) return;
      PointCloudManager.enabled = visible;
      foreach (var pc in PointCloudManager.trackables)
        if (pc) pc.gameObject.SetActive(visible);
    }

    private void ClearAnchors()
    {
      if (_visual) { Destroy(_visual); _visual = null; }
      if (_localAnchor) { Destroy(_localAnchor.gameObject); _localAnchor = null; }
      if (_cloudAnchor) { Destroy(_cloudAnchor.gameObject); _cloudAnchor = null; }
      // _cloudId se reemplaza al hostear uno nuevo
    }

    private void CancelPromises()
    {
      if (_hostPromise != null) { _hostPromise.Cancel(); _hostPromise = null; }
      if (_resolvePromise != null) { _resolvePromise.Cancel(); _resolvePromise = null; }
    }

    private void OnDisable()
    {
      CancelPromises();
      if (_resolveLoop != null) StopCoroutine(_resolveLoop);
    }
  }
}
