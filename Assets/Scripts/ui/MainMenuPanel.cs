using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
  public class MainMenuPanel : UIPanel
  {
    [SerializeField] private Button btnCrear;
    [SerializeField] private Button btnUnirse;
    [SerializeField] private UIFlowManager flow;
    bool wired;

    void OnEnable()  => Wire();
    void OnDisable() => Unwire();

    protected override void OnShow() { Wire(); }
    protected override void OnHide() { Unwire(); }

    void Wire()
    {
      if (wired) return;

      if (!btnCrear || !btnUnirse || !flow)
      {
        Debug.LogError("[MainMenuPanel] Faltan referencias: " +
                       $"{(btnCrear ? "" : "btnCrear ")}" +
                       $"{(btnUnirse ? "" : "btnUnirse ")}" +
                       $"{(flow ? "" : "flow")}");
        return;
      }

      btnCrear.onClick.AddListener(OnCrear);
      btnUnirse.onClick.AddListener(OnUnirse);
      wired = true;
    }

    void Unwire()
    {
      if (!wired) return;
      btnCrear.onClick.RemoveListener(OnCrear);
      btnUnirse.onClick.RemoveListener(OnUnirse);
      wired = false;
    }

    void OnCrear()
    {
      Debug.Log("[MainMenu] Crear → Placement");
      flow.GoToPlacement();
    }

    void OnUnirse()
    {
      Debug.Log("[MainMenu] Unirse (pendiente)");
      // TODO: flujo de unión
    }
  }
}
