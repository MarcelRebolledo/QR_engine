using UnityEngine;

public class PortraitEnforcer : MonoBehaviour
{
  void Awake()
  {
    Screen.autorotateToLandscapeLeft = false;
    Screen.autorotateToLandscapeRight = false;
    Screen.autorotateToPortraitUpsideDown = false;
    Screen.autorotateToPortrait = true;
    Screen.orientation = ScreenOrientation.Portrait;
  }
}
