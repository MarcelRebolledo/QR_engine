using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZXing;
using ZXing.Common;

public class MultiQRTracker : MonoBehaviour
{
    [Header("Camera")] public RawImage cameraOutput;
    public AspectRatioFitter fitter;

    [Header("Bounding Box")]
    public RectTransform boundingBoxPrefab;

    private WebCamTexture _webCam;
    private IBarcodeReaderGeneric _reader;
    private readonly List<RectTransform> _boxes = new List<RectTransform>();

    void Start()
    {
        _webCam = new WebCamTexture();
        cameraOutput.texture = _webCam;
        _webCam.Play();
        UpdateOrientation();

        _reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
            }
        };
    }

    void Update()
    {
        if (_webCam == null || !_webCam.isPlaying || !_webCam.didUpdateThisFrame)
            return;

        UpdateOrientation();

        if (fitter != null)
            fitter.aspectRatio = (float)_webCam.width / _webCam.height;

        ProcessFrame();
    }

    void ProcessFrame()
    {
        // 1) Leemos los píxeles como Color32[]
        Color32[] pixels32 = _webCam.GetPixels32();

        // 2) Convertimos a byte[] RGB24 (r, g, b por píxel)
        int len = pixels32.Length;
        byte[] raw = new byte[len * 3];
        for (int i = 0; i < len; i++)
        {
            int j = i * 3;
            raw[j    ] = pixels32[i].r;   // R
            raw[j + 1] = pixels32[i].g;   // G
            raw[j + 2] = pixels32[i].b;   // B
        }

     
        var results = _reader.DecodeMultiple(
                 raw,                     // <- aquí va el byte[]
                 _webCam.width,
                 _webCam.height,
                 RGBLuminanceSource.BitmapFormat.RGB24); // usaste RGB24// o RGB24 según tu plataforma

        foreach (var box in _boxes) Destroy(box.gameObject);
        _boxes.Clear();

        if (results == null)
            return;

        foreach (var result in results)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            foreach (var p in result.ResultPoints)
            {
                Vector2 pos = new Vector2(p.X, _webCam.height - p.Y);
                min = Vector2.Min(min, pos);
                max = Vector2.Max(max, pos);
            }

            var box = Instantiate(boundingBoxPrefab, cameraOutput.transform.parent);
            box.anchoredPosition = min;
            box.sizeDelta = max - min;

            var text = box.GetComponentInChildren<Text>();
            if (text != null)
                text.text = result.Text;

            _boxes.Add(box);
        }
    }

    void UpdateOrientation()
    {
        if (cameraOutput == null || _webCam == null)
            return;

        cameraOutput.rectTransform.localEulerAngles = new Vector3(0, 0, -_webCam.videoRotationAngle);
        cameraOutput.rectTransform.localScale = new Vector3(1, _webCam.videoVerticallyMirrored ? -1 : 1, 1);
    }

    void OnDestroy()
    {
        if (_webCam != null)
        {
            _webCam.Stop();
            _webCam = null;
        }
    }
}
