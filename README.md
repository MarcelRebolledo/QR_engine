# QR Engine

Este repositorio contiene un ejemplo sencillo de como detectar y seguir multiples codigos QR en Unity 2022.3.20f1 LTS.

El codigo se basa en la libreria [ZXing.Net](https://github.com/micjahn/ZXing.Net) para la decodificacion de los codigos y utiliza la camara del dispositivo para capturar los frames.

## Estructura

```
Assets/
  Scripts/
    MultiQRTracker.cs
```

`MultiQRTracker.cs` obtiene la imagen de la camara mediante `WebCamTexture`, decodifica los QR encontrados en cada frame y genera cajas delimitadoras para cada codigo detectado.

## Uso

1. Crear un proyecto vacio en Unity (version 2022.3.20f1 o superior).
2. Importar la libreria **ZXing.Net** (desde NuGet o desde su repositorio como dll para Unity).
3. Copiar la carpeta `Assets/Scripts` de este repositorio dentro de la carpeta `Assets` de tu proyecto.
4. Crear un Canvas y un `RawImage` para mostrar la imagen de la camara.
5. Asignar el prefab que servira como bounding box (puede ser un `Image` con un borde y un componente `Text` para mostrar el contenido del codigo) al campo `boundingBoxPrefab` del componente `MultiQRTracker`.
6. Ejecutar la escena. La camara se activara y se ajustara automaticamente su orientacion. Se mostraran en pantalla todas las cajas detectadas junto con el texto decodificado.

La deteccion se realiza continuamente por lo que los codigos pueden aparecer y desaparecer en pantalla mientras la camara se mantiene activa.

## Notas

- La libreria ZXing.Net soporta la deteccion de multiples codigos en un mismo frame usando `DecodeMultiple`.
- Para un rendimiento optimo en dispositivos moviles se recomienda reducir la resolucion de la `WebCamTexture`.
- El script ajusta cada fotograma la orientacion de la camara para mostrar correctamente la imagen.

