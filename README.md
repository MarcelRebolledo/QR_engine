# QR Engine

Este repositorio contiene un ejemplo sencillo de como detectar y seguir marcadores ArUco 4x4 (diccionario 50) en Unity 2022.3.20f1 LTS.

La deteccion se realiza mediante **OpenCV** y se integra con **AR Foundation** para generar anclas que siguen el marcador en tiempo real. La camara ocupa toda la pantalla y la escena `TEST1` incluye las sesiones AR necesarias.

## Estructura

```
Assets/
  Scripts/
    ArucoTracker.cs
    ARSetup.cs
```

`ArucoTracker.cs` procesa los fotogramas de la camara mediante `ARCameraManager` y usa OpenCV para detectar marcadores ArUco. Cada marcador genera un `ARAnchor` que sigue su pose en tiempo real. `ARSetup.cs` se encarga de crear las sesiones AR necesarias al iniciar la aplicación.

## Uso

1. Crear un proyecto vacío en Unity (versión 2022.3.20f1 o superior).
2. Copiar la carpeta `Assets/Scripts` de este repositorio dentro de la carpeta `Assets` de tu proyecto.
3. Abrir la escena `TEST1` y ejecutar. El script `ARSetup` generará un `AR Session` y un `AR Session Origin` con una cámara configurada para realidad aumentada.
4. `ArucoTracker` detectará marcadores ArUco 4x4 (diccionario 50) y creará anclas que los sigan en tiempo real.

La deteccion se realiza continuamente por lo que los codigos pueden aparecer y desaparecer en pantalla mientras la camara se mantiene activa.

## Notas

- Este ejemplo usa OpenCV para la deteccion de marcadores ArUco.
- Se recomienda probar en un dispositivo con soporte ARCore o ARKit para mejores resultados.
- La camara se ajusta automaticamente para mostrar la imagen en pantalla completa.

