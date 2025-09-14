using UnityEngine;

namespace App.UI
{
  public class UIFlowManager : MonoBehaviour
  {
    [Header("Panels")]
    [SerializeField] private UIPanel mainMenuPanel;
    [SerializeField] private UIPanel anchorPlacementPanel;
    [SerializeField] private UIPanel visualizationPanel;

    private UIPanel _current;

    void Awake()
    {
      // Arranca en el menú principal
      GoToMainMenu();
    }

    public void GoToMainMenu()        => SwitchTo(mainMenuPanel);
    public void GoToPlacement()       => SwitchTo(anchorPlacementPanel);
    public void GoToVisualization()   => SwitchTo(visualizationPanel);

    private void SwitchTo(UIPanel panel)
    {
      if (_current != null) _current.Hide();
      _current = panel;
      if (_current != null) _current.Show();
    }
  }
}