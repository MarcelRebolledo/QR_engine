using UnityEngine;
using UnityEngine.UI; // para Canvas.ForceUpdateCanvases

public class UIFlowManager : MonoBehaviour
{
  [SerializeField] GameObject panelMainMenu;
  [SerializeField] GameObject panelPlacement;
  [SerializeField] GameObject panelVisualization;

  void Awake() => ShowOnly(panelMainMenu);

  public void GoToMainMenu()      => ShowOnly(panelMainMenu);
  public void GoToPlacement()     => ShowOnly(panelPlacement);
  public void GoToVisualization() => ShowOnly(panelVisualization);

  void ShowOnly(GameObject target)
  {
    if (panelMainMenu)    panelMainMenu.SetActive(false);
    if (panelPlacement)   panelPlacement.SetActive(false);
    if (panelVisualization) panelVisualization.SetActive(false);

    if (target) target.SetActive(true);

    Canvas.ForceUpdateCanvases(); // fuerza redibujado inmediato
    Debug.Log($"[UIFlow] States => Main:{panelMainMenu?.activeSelf} | Placement:{panelPlacement?.activeSelf} | Viz:{panelVisualization?.activeSelf}");
  }
}
