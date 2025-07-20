import cv2
import numpy as np
from cv2 import aruco

# 1) Configuración
aruco_dict     = aruco.getPredefinedDictionary(aruco.DICT_4X4_50)
marker_size_px = 300   # tamaño de cada marcador en píxeles
sep_px         = 50    # separación/margen entre marcadores en píxeles
cols, rows     = 10, 5 # 10 columnas × 5 filas = 50 marcadores

# 2) Crear canvas blanco
width  = cols*marker_size_px + (cols+1)*sep_px
height = rows*marker_size_px + (rows+1)*sep_px
canvas = np.full((height, width), 255, dtype=np.uint8)

# 3) Generar y colocar cada marcador
for r in range(rows):
    for c in range(cols):
        id_ = r*cols + c
        marker = aruco.generateImageMarker(aruco_dict, id_, marker_size_px)
        x = sep_px + c*(marker_size_px + sep_px)
        y = sep_px + r*(marker_size_px + sep_px)
        canvas[y:y+marker_size_px, x:x+marker_size_px] = marker

# 4) Guardar imagen final
cv2.imwrite("board_custom_4x4_50.png", canvas)
print("Board generado: board_custom_4x4_50.png")

# 5) (Opcional) Verificar detección en Python
params   = aruco.DetectorParameters()
detector = aruco.ArucoDetector(aruco_dict, params)
corners, ids, _ = detector.detectMarkers(canvas)
print("IDs detectados en Python:", ids.flatten())