using UnityEngine;

/// Pon este script en el root del mapa (ej.: MAPA14x20) dentro del prefab.
/// Opcionalmente ajusta el nombre del nodo de terreno (por defecto "capa0").
public class MapRootTag : MonoBehaviour
{
    [Tooltip("Nombre del hijo que contiene los colliders del terreno (capa0)")]
    public string terrainNodeName = "capa0";
}
