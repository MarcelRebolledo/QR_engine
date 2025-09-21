using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace App.UI
{
  public class DebugConsole : MonoBehaviour
  {
    [SerializeField] private TMP_Text output;
    [SerializeField, Range(10, 500)] private int maxLines = 80;

    private readonly Queue<string> _lines = new Queue<string>();
    private readonly StringBuilder _sb = new StringBuilder();

    void OnEnable()
    {
      Application.logMessageReceivedThreaded += OnLog;
      Redraw();
    }

    void OnDisable()
    {
      Application.logMessageReceivedThreaded -= OnLog;
    }

    public void AddLine(string line)
    {
      lock (_lines)
      {
        _lines.Enqueue(line);
        while (_lines.Count > maxLines) _lines.Dequeue();
      }
      Redraw();
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
      string tag = type switch
      {
        LogType.Warning => "[WARN] ",
        LogType.Error or LogType.Exception => "[ERR ] ",
        _ => "[INFO] "
      };
      AddLine($"{tag}{condition}");
    }

    private void Redraw()
    {
      if (output == null) return;
      lock (_lines)
      {
        _sb.Clear();
        foreach (var l in _lines) _sb.AppendLine(l);
      }
      // Actualiza en el hilo principal
      UnityMainThreadDispatcher.RunOnMainThread(() =>
      {
        if (output != null) output.text = _sb.ToString();
      });
    }
  }

  // Utilidad mínima para encolar acciones al hilo principal.
  // Puedes reemplazarla por tu sistema preferido si ya tienes uno.
  public class UnityMainThreadDispatcher : MonoBehaviour
  {
    private static readonly Queue<System.Action> _queue = new Queue<System.Action>();
    private static UnityMainThreadDispatcher _inst;

    [RuntimeInitializeOnLoadMethod]
    static void Bootstrap()
    {
      var go = new GameObject("UnityMainThreadDispatcher");
      _inst = go.AddComponent<UnityMainThreadDispatcher>();
      DontDestroyOnLoad(go);
    }

    public static void RunOnMainThread(System.Action a)
    {
      lock (_queue) _queue.Enqueue(a);
    }

    void Update()
    {
      lock (_queue)
      {
        while (_queue.Count > 0) _queue.Dequeue()?.Invoke();
      }
    }
  }
}
