using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ARLocation
{
    public class WebMapLoader : MonoBehaviour
    {
        public TextAsset XmlDataFile;
        public bool DebugMode;

        public class DataEntry
        {
            public int id;
            public double lat;
            public double lng;
            public double altitude;
            public string altitudeMode;
            public string name;
            public string meshId;
            public float movementSmoothing;
            public int maxNumberOfLocationUpdates;
            public bool useMovingAverage;
            public bool hideObjectUtilItIsPlaced;

            public AltitudeMode getAltitudeMode()
            {
                return altitudeMode switch
                {
                    "GroundRelative" => AltitudeMode.GroundRelative,
                    "DeviceRelative" => AltitudeMode.DeviceRelative,
                    "Absolute" => AltitudeMode.Absolute,
                    _ => AltitudeMode.Ignore,
                };
            }
        }

        private List<DataEntry> _dataEntries = new List<DataEntry>();
        private List<PlaceAtLocation> _placeAtComponents = new List<PlaceAtLocation>();

        public List<PlaceAtLocation> Instances => _placeAtComponents;

        void Start()
        {
            LoadXmlFile();
            BuildGameObjectsAsync();
        }

        void LoadXmlFile()
        {
            XmlDocument xmlDoc = new XmlDocument();

            try { xmlDoc.LoadXml(XmlDataFile.text); }
            catch (XmlException e)
            {
                Debug.LogError("[WebMapLoader]: Failed to parse XML file: " + e.Message);
                return;
            }

            var nodes = xmlDoc.FirstChild?.ChildNodes;
            if (nodes == null) return;

            foreach (XmlNode node in nodes)
            {
                DataEntry entry = new DataEntry
                {
                    id = int.Parse(node["id"].InnerText),
                    lat = double.Parse(node["lat"].InnerText, CultureInfo.InvariantCulture),
                    lng = double.Parse(node["lng"].InnerText, CultureInfo.InvariantCulture),
                    altitude = double.Parse(node["altitude"].InnerText, CultureInfo.InvariantCulture),
                    altitudeMode = node["altitudeMode"].InnerText,
                    name = node["name"].InnerText,
                    meshId = node["meshId"].InnerText,
                    movementSmoothing = float.Parse(node["movementSmoothing"].InnerText, CultureInfo.InvariantCulture),
                    maxNumberOfLocationUpdates = int.Parse(node["maxNumberOfLocationUpdates"].InnerText),
                    useMovingAverage = bool.Parse(node["useMovingAverage"].InnerText),
                    hideObjectUtilItIsPlaced = bool.Parse(node["hideObjectUtilItIsPlaced"].InnerText)
                };

                _dataEntries.Add(entry);
            }
        }

        async void BuildGameObjectsAsync()
        {
            foreach (var entry in _dataEntries)
            {
                AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>(entry.meshId);
                await handle.Task;

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    GameObject prefab = handle.Result;

                    var options = new PlaceAtLocation.PlaceAtOptions
                    {
                        MovementSmoothing = entry.movementSmoothing,
                        MaxNumberOfLocationUpdates = entry.maxNumberOfLocationUpdates,
                        UseMovingAverage = entry.useMovingAverage,
                        HideObjectUntilItIsPlaced = entry.hideObjectUtilItIsPlaced
                    };

                    var location = new Location
                    {
                        Latitude = entry.lat,
                        Longitude = entry.lng,
                        Altitude = entry.altitude,
                        AltitudeMode = entry.getAltitudeMode(),
                        Label = entry.name
                    };

                    var instance = PlaceAtLocation.CreatePlacedInstance(prefab, location, options, DebugMode);
                    _placeAtComponents.Add(instance.GetComponent<PlaceAtLocation>());
                }
                else
                {
                    Debug.LogWarning($"[WebMapLoader]: Failed to load prefab: {entry.meshId}");
                }
            }
        }

        public void SetActiveGameObjects(bool value)
        {
            foreach (var obj in _placeAtComponents)
            {
                obj.gameObject.SetActive(value);
            }
        }

        public void HideMeshes()
        {
            foreach (var obj in _placeAtComponents)
            {
                Utils.Misc.HideGameObject(obj.gameObject);
            }
        }

        public void ShowMeshes()
        {
            foreach (var obj in _placeAtComponents)
            {
                Utils.Misc.ShowGameObject(obj.gameObject);
            }
        }
    }
}
