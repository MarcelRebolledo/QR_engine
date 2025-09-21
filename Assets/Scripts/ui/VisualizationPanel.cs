using UnityEngine;

namespace App.UI
{
  public class VisualizationPanel : UIPanel
  {
    protected override void OnShow()
    {
      Debug.Log("[Visualization] Panel activo (sin controles).");      
    }
  }
}
