using UnityEngine;
using TMPro;

public class fichaController : MonoBehaviour
{
    const int   kRequiredId    = 6;
    const float kRayDistance   = 10f;
    const float kSurfaceOffset = 0.015f; // separación para evitar z-fighting
    const int   kIgnoreRaycastLayer = 2; // capa especial de Unity: "Ignore Raycast"

    [Header("Prefabs / UI")]
    public GameObject pjPrefab;
    public TMP_Text   debugText;

    [Header("Debug en dispositivo")]
    [SerializeField] RayVisualizer rayViz;

    [Header("Colocación")]
    [Tooltip("Tag del collider de la superficie/ARPlane que quieres golpear")]
    [SerializeField] string surfaceTag = "capa1";
    [Tooltip("Tag del GameObject contenedor para colgar el PJ")]
    [SerializeField] string contentParentTag = "capa2";

    [Tooltip("Opcional: limita por capas qué puede golpear el raycast (excluye tu PJ). Si queda vacío, usa 'Everything' menos IgnoreRaycast.")]
    [SerializeField] LayerMask placementMask;

    [Tooltip("Altura desde la cual lanzamos el rayo (en mundo), encima del marcador")]
    [SerializeField] float originHeight = 0.30f;

    [Tooltip("Si está activado, el PJ seguirá al ArUco cada frame. Si está desactivado, se coloca una vez y no se mueve.")]
    [SerializeField] bool followContinuously = false;

    GameObject _spawned;
    bool       _placedOnce;

    void Update()
    {
        var aruco = GameObject.FindWithTag("aruco");
        if (aruco == null) return;
        if (!HasRequiredId(aruco.name)) return;

        // Origen y dirección en coordenadas de MUNDO (¡no uses la cámara!)
        Vector3 origin = aruco.transform.position + Vector3.up * originHeight;
        Vector3 dir    = Vector3.down;

        if (rayViz != null) rayViz.ShowRay(origin, dir, Mathf.Min(kRayDistance, originHeight * 2f));

        // Cálculo de máscara efectiva: si no asignaste nada en el inspector, usa "Everything" EXCEPTO IgnoreRaycast
        int effectiveMask = (placementMask.value == 0) ? (~(1 << kIgnoreRaycastLayer)) : placementMask.value;

        // Dos rayos (abajo y arriba) por si el marcador está por debajo/encima de la superficie
        bool hitDown = Physics.Raycast(origin, Vector3.down, out var hitD, kRayDistance, effectiveMask, QueryTriggerInteraction.Ignore);
        bool hitUp   = Physics.Raycast(origin, Vector3.up,   out var hitU, kRayDistance, effectiveMask, QueryTriggerInteraction.Ignore);

        // Validación por tag (si la usas) — opcional si confías solo en capas
        bool okD = hitDown && (string.IsNullOrEmpty(surfaceTag) || hitD.collider.CompareTag(surfaceTag));
        bool okU = hitUp   && (string.IsNullOrEmpty(surfaceTag) || hitU.collider.CompareTag(surfaceTag));

        if (!okD && !okU) { Log("sin hit válido"); return; }

        // Quedarse con el impacto más cercano válido
        var hit = (!okD) ? hitU : (!okU) ? hitD : (hitU.distance <= hitD.distance ? hitU : hitD);

        // Posición final con pequeño offset según la normal del plano
        Vector3 targetPos = hit.point + hit.normal * kSurfaceOffset;

        // Si ya coloqué y no debo seguir moviendo, salgo
        if (_placedOnce && !followContinuously)
            return;

        if (_spawned == null)
        {
            if (pjPrefab == null) { Log("sin prefab"); return; }

            _spawned = Instantiate(pjPrefab, targetPos, Quaternion.identity);

            // Parent a tu contenedor (escala 1,1,1 por favor)
            var parentGO = GameObject.FindWithTag(contentParentTag);
            if (parentGO != null) _spawned.transform.SetParent(parentGO.transform, true);

            _spawned.name = "pj_instanced";
            if (!_spawned.activeSelf) _spawned.SetActive(true);

            // MUY IMPORTANTE: tu PJ no debe interferir con el raycast
            SetLayerRecursive(_spawned, kIgnoreRaycastLayer); // lo saca de los Physics.Raycast por defecto

            Log($"spawn en {targetPos}  |  hit={hit.collider.name}  layer={LayerMask.LayerToName(hit.collider.gameObject.layer)}");
            _placedOnce = true;
        }
        else
        {
            // Si se quiere seguimiento continuo, actualiza posición
            if (followContinuously)
            {
                _spawned.transform.position = targetPos;
                // Rotación opcional para alinear con el plano:
                // _spawned.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(_spawned.transform.forward, hit.normal), hit.normal);
                Log($"move → {targetPos}  |  hit={hit.collider.name}");
            }
        }
    }

    static bool HasRequiredId(string goName)
    {
        int us = goName.LastIndexOf('_');
        if (us < 0 || us + 1 >= goName.Length) return false;
        return int.TryParse(goName[(us + 1)..], out int id) && id == kRequiredId;
    }

    void Log(string m) { Debug.Log(m); if (debugText) debugText.text = m; }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }
}
