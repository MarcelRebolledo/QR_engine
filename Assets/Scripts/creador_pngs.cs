using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ArucoUnity.Plugin;          // Binding C# ➜ C++ (ArucoUnity)
using Cv = ArucoUnity.Plugin.Cv;  // Alias para abreviar las clases de OpenCV
using Aruco = ArucoUnity.Plugin.Aruco;   // Alias para funciones ArUco
using Std = ArucoUnity.Plugin.Std;       // Alias para contenedores std::vector
using TMPro;
using System.Text;
using UnityEngine.UI;

public class creador_pngs : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
void Start()
{
  GenerateArucoMarkerSet();   // ← NUEVA LÍNEA
}

/* ------------------------------------------------------------------ */
/* Genera aruco_0.png … aruco_49.png en Assets/aruco_ex/ si no existen */
void GenerateArucoMarkerSet()
{
    // 1. Carpeta destino absoluta (…/MiProyecto/Assets/aruco_ex)
    string dir = System.IO.Path.Combine(Application.dataPath, "aruco_ex");
    if (!System.IO.Directory.Exists(dir))
        System.IO.Directory.CreateDirectory(dir);

    // 2. Diccionario ArUco (4x4 con 50 IDs)
    var dict = Aruco.GetPredefinedDictionary(
                   Aruco.PredefinedDictionaryName.Dict4x4_50);

    const int sidePx    = 400; // resolución de salida
    const int borderBits = 1;  // grosor del borde blanco

    for (int id = 0; id < 50; ++id)
    {
        string pngPath = System.IO.Path.Combine(dir, $"aruco_{id}.png");
        if (System.IO.File.Exists(pngPath))
            continue;  // Ya estaba generado

        // 3. Dibujar el marcador en un Mat de OpenCV
        Cv.Mat marker = new Cv.Mat();
        Aruco.DrawMarker(dict, id, sidePx, marker, borderBits);

        // 4. Guardar como PNG (usa los bindings de OpenCV)
        Cv.Imgcodecs.Imwrite(pngPath, marker);
        marker.Dispose();
    }

#if UNITY_EDITOR
    // 5. Hacer visible la carpeta nueva en el Project view
    UnityEditor.AssetDatabase.Refresh();
#endif
}

    // Update is called once per frame
    void Update()
    {
        
    }
}
