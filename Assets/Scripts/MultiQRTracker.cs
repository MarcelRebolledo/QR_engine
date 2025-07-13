using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ZXing;
using ZXing.Common;

/// <summary>
/// Detects and tracks multiple QR codes from a camera feed.
/// Requires the ZXing.Net library.
/// Attach this script to a GameObject in the scene.
/// The boundingBoxPrefab should be a UI element that contains an Image
/// for the border and an optional Text component for displaying the
/// decoded string.
/// </summary>
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

        // Keep aspect ratio
        if (fitter != null)
            fitter.aspectRatio = (float)_webCam.width / _webCam.height;

        ProcessFrame();
    }

    void ProcessFrame()
    {
        var pixels = _webCam.GetPixels32();
        var results = _reader.DecodeMultiple(pixels, _webCam.width, _webCam.height);

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

