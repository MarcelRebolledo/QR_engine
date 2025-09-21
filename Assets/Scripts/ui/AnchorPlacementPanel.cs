using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
  public class AnchorPlacementPanel : UIPanel
  {
    [Header("Botones")]
    [SerializeField] private Button btnPlaceToggle;   // central-abajo (colocar / reemplazar)
    [SerializeField] private Button btnRotateLeft;    // izquierda
    [SerializeField] private Button btnRotateRight;   // derecha
    [SerializeField] private Button btnIniciar;       // iniciar
    [Header("Extras")]
    [SerializeField] private DebugConsole console;    // recuadro debug
    [SerializeField] private UIFlowManager flow;

    // Estado simulado del "modelo posicionado" (aún sin AR):
    private GameObject _dummyModel;
    private float _yRotation;

    protected override void OnShow()
    {
      btnPlaceToggle.onClick.AddListener(OnPlaceToggle);
      btnRotateLeft.onClick.AddListener(OnRotateLeft);
      btnRotateRight.onClick.AddListener(OnRotateRight);
      btnIniciar.onClick.AddListener(OnIniciar);

      Debug.Log("[Placement] Panel activo.");
    }

    protected override void OnHide()
    {
      btnPlaceToggle.onClick.RemoveListener(OnPlaceToggle);
      btnRotateLeft.onClick.RemoveListener(OnRotateLeft);
      btnRotateRight.onClick.RemoveListener(OnRotateRight);
      btnIniciar.onClick.RemoveListener(OnIniciar);
    }

    private void OnPlaceToggle()
    {
      if (_dummyModel == null)
      {
        _dummyModel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _dummyModel.name = "DummyAnchorModel";
        _dummyModel.transform.position = Vector3.zero; // por ahora centro del mundo
        _dummyModel.transform.localScale = Vector3.one * 0.2f;
        _yRotation = 0f;
        Debug.Log("[Placement] Modelo colocado (dummy).");
      }
      else
      {
        Destroy(_dummyModel);
        _dummyModel = null;
        Debug.Log("[Placement] Modelo eliminado. Pulsa nuevamente para colocar.");
      }
    }

    private void OnRotateLeft()  => RotateDummy(-15f);
    private void OnRotateRight() => RotateDummy(+15f);

    private void RotateDummy(float delta)
    {
      if (_dummyModel == null)
      {
        Debug.LogWarning("[Placement] No hay modelo para rotar.");
        return;
      }
      _yRotation += delta;
      _dummyModel.transform.rotation = Quaternion.Euler(0f, _yRotation, 0f);
      Debug.Log($"[Placement] Rotación Y = {_yRotation:0}°");
    }

    private void OnIniciar()
    {
      // Más adelante: persistir pose/rotación para la siguiente pantalla y
      // activar detección ArUco + render del modelo real sobre un anchor real.
      Debug.Log("[Placement] Iniciar → Ir a Visualización.");
      flow.GoToVisualization();
    }
  }
}
