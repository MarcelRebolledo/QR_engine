import cv2
from cv2 import aruco
from PIL import Image

# Parámetros del tablero
squaresX = 22
squaresY = 14
squareLength = 0.025  # metros
markerLength = 0.018  # metros

# Crear diccionario y tablero ChArUco
dictionary = aruco.getPredefinedDictionary(aruco.DICT_5X5_1000)
board = aruco.CharucoBoard_create(
    squaresX, squaresY,
    squareLength, markerLength,
    dictionary
)

# Dimensiones físicas del tablero (mm)
width_mm = 560
height_mm = 360
dpi = 300

# Convertir a pixeles
mm_to_inches = 1 / 25.4
width_px = int(width_mm * mm_to_inches * dpi)    # ≈ 6614 px
height_px = int(height_mm * mm_to_inches * dpi)  # ≈ 4252 px

# Dibujar imagen del tablero
img = board.draw((width_px, height_px))

# Guardar imagen con DPI incrustado
Image.fromarray(img).save("charuco_board_36x56cm.png", dpi=(dpi, dpi))