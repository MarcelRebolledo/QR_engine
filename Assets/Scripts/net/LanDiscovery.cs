using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class LanDiscovery : MonoBehaviour
{
  [Header("LAN")]
  public string multicastAddress = "239.1.2.3";
  public int port = 40404;
  public float announceInterval = 1.0f; // cada 1s
  public string sessionTag = "ORBIS-SESSION-1"; // misma “sala” en la LAN

  UdpClient _udp;
  IPEndPoint _remoteEp;
  Thread _rxThread;
  volatile bool _running;
  string _deviceId;

#if UNITY_ANDROID && !UNITY_EDITOR
  AndroidJavaObject _wifiLock;
#endif

  public event Action<string,string> OnAnchorShared; // (cloudId, yawStr)
  public event Action<string,string> OnStatus;       // (deviceId, status)

  void Start()
  {
    _deviceId = SystemInfo.deviceUniqueIdentifier;
    _remoteEp = new IPEndPoint(IPAddress.Parse(multicastAddress), port);

    // RX socket
    _udp = new UdpClient();
    _udp.ExclusiveAddressUse = false;
    _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    _udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
    _udp.JoinMulticastGroup(IPAddress.Parse(multicastAddress));

#if UNITY_ANDROID && !UNITY_EDITOR
    AcquireMulticastLock();
#endif

    _running = true;
    _rxThread = new Thread(RxLoop){IsBackground = true};
    _rxThread.Start();

    InvokeRepeating(nameof(Announce), 0.2f, announceInterval);
  }

  void OnDestroy()
  {
    _running = false;
    try { _rxThread?.Join(200); } catch {}
    try { _udp?.DropMulticastGroup(IPAddress.Parse(multicastAddress)); } catch {}
    try { _udp?.Close(); } catch {}
#if UNITY_ANDROID && !UNITY_EDITOR
    ReleaseMulticastLock();
#endif
  }

  void RxLoop()
  {
    var any = new IPEndPoint(IPAddress.Any, 0);
    while (_running)
    {
      try
      {
        var data = _udp.Receive(ref any);
        var s = Encoding.UTF8.GetString(data);
        if (!s.Contains(sessionTag)) continue;

        // Mensajes tipo: ORBIS|ANNOUNCE|<deviceId>
        //                ORBIS|STATUS|<deviceId>|<status>
        //                ORBIS|ANCHOR|<cloudId>|y=<yaw>
        var p = s.Split('|');
        if (p.Length < 3) continue;
        if (p[0] != "ORBIS") continue;

        switch (p[1])
        {
          case "STATUS":
            if (p.Length >= 4) OnStatus?.Invoke(p[2], p[3]);
            break;
          case "ANCHOR":
            if (p.Length >= 4)
            {
              string cloudId = p[2];
              string yawStr  = p[3]; // "y=37.5"
              OnAnchorShared?.Invoke(cloudId, yawStr);
            }
            break;
          default: break; // ANNOUNCE no hace nada por ahora
        }
      }
      catch { /* ignorar en cierre */ }
    }
  }

  void Announce()
  {
    SendRaw($"ORBIS|ANNOUNCE|{_deviceId}|{sessionTag}");
  }

  public void SendStatus(string status)
  {
    SendRaw($"ORBIS|STATUS|{_deviceId}|{status}|{sessionTag}");
  }

  public void SendAnchor(string cloudId, float yaw)
  {
    var yawS = yaw.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    SendRaw($"ORBIS|ANCHOR|{cloudId}|y={yawS}|{sessionTag}");
  }

  void SendRaw(string msg)
  {
    var bytes = Encoding.UTF8.GetBytes(msg);
    _udp.Send(bytes, bytes.Length, _remoteEp);
  }

#if UNITY_ANDROID && !UNITY_EDITOR
  void AcquireMulticastLock()
  {
    try {
      var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
      var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
      var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
      _wifiLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "orbis-lock");
      _wifiLock.Call("setReferenceCounted", false);
      _wifiLock.Call("acquire");
    } catch (Exception e) { Debug.LogWarning("MulticastLock fail: " + e.Message); }
  }
  void ReleaseMulticastLock() { try { _wifiLock?.Call("release"); } catch {} }
#endif
}
