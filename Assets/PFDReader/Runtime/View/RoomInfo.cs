using UnityEngine;

namespace PFDReader.View
{
    /// <summary>Beschreibt einen 3D-Raum (eigene Szene): Titel, Sitzplaetze (Augenpunkt + Blickrichtung) und Himmel.</summary>
    public sealed class RoomInfo : MonoBehaviour
    {
        public string id;
        public string title;
        [Tooltip("Standard-Sitzplatz (Augenpunkt des sitzenden Lesers, +Z = Blickrichtung)")]
        public Transform seat;
        [Tooltip("Weitere Sitzplaetze; Name des Transforms = Anzeigename")]
        public Transform[] seats;
        public Material skybox;
        [Tooltip("Himmel fuer die Abendstimmung")]
        public Material skyEvening;
        [Tooltip("Gebackene Lightmaps je Tageszeit (gleiche Reihenfolge/Namen)")]
        public Texture2D[] lightmapsDay, lightmapsEvening;
        [Tooltip("Bildschirm eines Fernsehers: Position/Drehung = Bildmitte (Blick auf -Z), localScale x/y = Breite/Hoehe in m")]
        public Transform tv;
    }
}
