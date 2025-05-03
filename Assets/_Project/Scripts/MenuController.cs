using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ARLocation.MapboxRoutes.SampleProject
{
    public class MenuController : MonoBehaviour
    {
        public enum LineType { Route, NextTarget }
        

        public Texture2D magnifyingGlassIcon;
        public Texture2D microphoneIcon;  
        public Texture2D backgroundTexture;
        public string MapboxToken = "your-mapbox-token";
        public GameObject ARSession;
        public GameObject ARSessionOrigin;
        public GameObject RouteContainer;
        public Camera Camera;
        public Camera MapboxMapCamera;
        public MapboxRoute MapboxRoute;
        public AbstractRouteRenderer RoutePathRenderer;
        public AbstractRouteRenderer NextTargetPathRenderer;
        public Texture RenderTexture;
        public Mapbox.Unity.Map.AbstractMap Map;
        [Range(100, 800)]
        public int MapSize = 400;
        public DirectionsFactory DirectionsFactory;
        public int MinimapLayer;
        public Material MinimapLineMaterial;
        public float BaseLineWidth = 2;
        public float MinimapStepSize = 0.5f;

        private AbstractRouteRenderer currentPathRenderer => s.LineType == LineType.Route ? RoutePathRenderer : NextTargetPathRenderer;

        public LineType PathRendererType
        {
            get => s.LineType;
            set
            {
                if (value != s.LineType)
                {
                    currentPathRenderer.enabled = false;
                    s.LineType = value;
                    currentPathRenderer.enabled = true;

                    if (s.View == View.Route)
                    {
                        MapboxRoute.RoutePathRenderer = currentPathRenderer;
                    }
                }
            }
        }

        enum View { SearchMenu, Route }

        [System.Serializable]
        private class State
        {
            public string QueryText = "";
            public List<GeocodingFeature> Results = new List<GeocodingFeature>();
            public View View = View.SearchMenu;
            public Location destination;
            public LineType LineType = LineType.NextTarget;
            public string ErrorMessage;
        }

        private State s = new State();

        // --- Style Methods ---
        private GUIStyle _textStyle;
        GUIStyle textStyle()
        {
            if (_textStyle == null)
            {
                _textStyle = new GUIStyle(GUI.skin.label);
                _textStyle.fontSize = 48;
                _textStyle.fontStyle = FontStyle.Bold;
                _textStyle.normal.textColor = Color.white;
                _textStyle.alignment = TextAnchor.MiddleLeft;
                _textStyle.padding = new RectOffset(10, 10, 10, 10);
            }
            return _textStyle;
        }

        private GUIStyle _textFieldStyle;
        GUIStyle textFieldStyle()
        {
            if (_textFieldStyle == null)
            {
                _textFieldStyle = new GUIStyle(GUI.skin.textField);
                _textFieldStyle.fontSize = 48;
                _textFieldStyle.padding = new RectOffset(20, 20, 10, 10);
                _textFieldStyle.normal.textColor = Color.black;
                _textFieldStyle.alignment = TextAnchor.MiddleLeft;
            }
            return _textFieldStyle;
        }

        private GUIStyle _errorLabelStyle;
        GUIStyle errorLabelSytle()
        {
            if (_errorLabelStyle == null)
            {
                _errorLabelStyle = new GUIStyle(GUI.skin.label);
                _errorLabelStyle.fontSize = 24;
                _errorLabelStyle.fontStyle = FontStyle.Bold;
                _errorLabelStyle.normal.textColor = Color.red;
                _errorLabelStyle.padding = new RectOffset(10, 10, 5, 5);
            }
            return _errorLabelStyle;
        }

        private GUIStyle _buttonStyle;
        GUIStyle buttonStyle()
        {
            if (_buttonStyle == null)
            {
                _buttonStyle = new GUIStyle(GUI.skin.button);
                _buttonStyle.fontSize = 48;
                _buttonStyle.fontStyle = FontStyle.Bold;
                _buttonStyle.alignment = TextAnchor.MiddleCenter;
                _buttonStyle.normal.textColor = Color.white;
                _buttonStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.5f, 0.9f));
                _buttonStyle.hover.background = MakeTex(2, 2, new Color(0.3f, 0.6f, 1f));
                _buttonStyle.active.background = MakeTex(2, 2, new Color(0.1f, 0.4f, 0.8f));
                _buttonStyle.padding = new RectOffset(20, 20, 10, 10);
                _buttonStyle.border = new RectOffset(12, 12, 12, 12);
            }
            return _buttonStyle;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        // --- Lifecycle ---
        void Awake() { }

        void Start()
        {
            NextTargetPathRenderer.enabled = false;
            RoutePathRenderer.enabled = false;
            ARLocationProvider.Instance.OnEnabled.AddListener(onLocationEnabled);
            Map.OnUpdated += OnMapRedrawn;

            magnifyingGlassIcon= Resources.Load<Texture2D>("search2");
            microphoneIcon= Resources.Load<Texture2D>("mic2");
            backgroundTexture= Resources.Load<Texture2D>("back2");
        }

        private void OnMapRedrawn()
        {
            if (currentResponse != null)
            {
                buildMinimapRoute(currentResponse);
            }
        }

        private void onLocationEnabled(Location location)
        {
            Map.SetCenterLatitudeLongitude(new Mapbox.Utils.Vector2d(location.Latitude, location.Longitude));
            Map.UpdateMap();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { }

        void drawMap()
        {
            var tw = RenderTexture.width;
            var th = RenderTexture.height;
            var scale = MapSize / th;
            var newWidth = scale * tw;
            var x = Screen.width / 2 - newWidth / 2;
            float border = (x < 0) ? -x : 0;

            GUI.DrawTexture(new Rect(x, Screen.height - MapSize, newWidth, MapSize), RenderTexture, ScaleMode.ScaleAndCrop);
            GUI.DrawTexture(new Rect(0, Screen.height - MapSize - 20, Screen.width, 20), separatorTexture, ScaleMode.StretchToFill, false);

            var newZoom = GUI.HorizontalSlider(new Rect(0, Screen.height - 60, Screen.width, 60), Map.Zoom, 10, 22);
            if (newZoom != Map.Zoom)
            {
                Map.SetZoom(newZoom);
                Map.UpdateMap();
            }
        }

        void OnGUI()
        {
            // 1. Draw background (98% transparent)
            if (backgroundTexture != null)
            {
                float backgroundY = 80;
                Color originalColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.1f); // 98% transparent
                GUI.DrawTexture(
                    new Rect(0, backgroundY, Screen.width, Screen.height - backgroundY),
                    backgroundTexture,
                    ScaleMode.StretchToFill
                );
                GUI.color = originalColor;
            }

            // 2. Skip if in Route view
            if (s.View == View.Route)
            {
                drawMap();
                return;
            }

            float h = Screen.height - MapSize;
            
            // Main UI container
            GUILayout.BeginVertical(GUILayout.Height(h));
            
            // --- Gray Top Bar ---
            GUILayout.BeginHorizontal(new GUIStyle() 
            { 
                normal = { background = MakeTex(1, 1, new Color(0.3f, 0.3f, 0.3f)) }, // Gray color
                fixedHeight = 80,
                padding = new RectOffset(40, 0, 10, 0) // Added bottom padding to push text down
            }, 
            GUILayout.ExpandWidth(true));

            GUILayout.Space(20);
            
            // Search label (now aligned to LowerLeft and with top padding)
            GUILayout.Label("SEARCH", new GUIStyle(GUI.skin.label) 
            { 
                fontSize = 40,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft, // Changed from MiddleLeft to LowerLeft
                normal = { textColor = Color.white },
                padding = new RectOffset(0, 0, 5, 0) // Added 5px top padding
            }, 
            GUILayout.Width(200));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // --- Rest of the UI ---
            GUILayout.BeginVertical(new GUIStyle() 
            { 
                padding = new RectOffset(20, 20, 20, 20)
            });

            // Search bar (unchanged)
            GUILayout.BeginHorizontal(GUILayout.Height(100));
            s.QueryText = GUILayout.TextField(s.QueryText, textFieldStyle(), GUILayout.MinWidth(0.75f * Screen.width), GUILayout.Height(75));
            if (GUILayout.Button(magnifyingGlassIcon, buttonStyle(), GUILayout.Width(0.1f * Screen.width), GUILayout.Height(75)))
            {
                if (!string.IsNullOrEmpty(s.QueryText))
                    StartCoroutine(search());
            }
            if (GUILayout.Button(microphoneIcon, buttonStyle(), GUILayout.Width(0.1f * Screen.width), GUILayout.Height(75)))
            {
                Debug.Log("Microphone pressed");
            }
            GUILayout.EndHorizontal();

            // Results list (unchanged)
            if (s.ErrorMessage != null)
                GUILayout.Label(s.ErrorMessage, errorLabelSytle());
            foreach (var r in s.Results)
            {
                if (GUILayout.Button(r.place_name, new GUIStyle(buttonStyle()) 
                    { alignment = TextAnchor.MiddleLeft, fontSize = 32, fixedHeight = 80 }))
                    StartRoute(r.geometry.coordinates[0]);
            }

            GUILayout.EndVertical();
            GUILayout.EndVertical();

            drawMap(); // Map at bottom
        }

        private Texture2D _separatorTexture;
        private Texture2D separatorTexture
        {
            get
            {
                if (_separatorTexture == null)
                {
                    _separatorTexture = new Texture2D(1, 1);
                    _separatorTexture.SetPixel(0, 0, new Color(0.15f, 0.15f, 0.15f));
                    _separatorTexture.Apply();
                }
                return _separatorTexture;
            }
        }

        public void StartRoute(Location dest)
        {
            s.destination = dest;
            if (ARLocationProvider.Instance.IsEnabled)
            {
                loadRoute(ARLocationProvider.Instance.CurrentLocation.ToLocation());
            }
            else
            {
                ARLocationProvider.Instance.OnEnabled.AddListener(loadRoute);
            }
        }

        public void EndRoute()
        {
            ARLocationProvider.Instance.OnEnabled.RemoveListener(loadRoute);
            ARSession.SetActive(false);
            ARSessionOrigin.SetActive(false);
            RouteContainer.SetActive(false);
            Camera.gameObject.SetActive(true);
            s.View = View.SearchMenu;
        }

        private void loadRoute(Location _)
        {
            if (s.destination != null)
            {
                var api = new MapboxApi(MapboxToken);
                var loader = new RouteLoader(api);
                StartCoroutine(loader.LoadRoute(
                    new RouteWaypoint { Type = RouteWaypointType.UserLocation },
                    new RouteWaypoint { Type = RouteWaypointType.Location, Location = s.destination },
                    (err, res) =>
                    {
                        if (err != null)
                        {
                            s.ErrorMessage = err;
                            s.Results = new List<GeocodingFeature>();
                            return;
                        }

                        ARSession.SetActive(true);
                        ARSessionOrigin.SetActive(true);
                        RouteContainer.SetActive(true);
                        Camera.gameObject.SetActive(false);
                        s.View = View.Route;

                        currentPathRenderer.enabled = true;
                        MapboxRoute.RoutePathRenderer = currentPathRenderer;
                        MapboxRoute.BuildRoute(res);
                        currentResponse = res;
                        buildMinimapRoute(res);
                    }));
            }
        }

        private GameObject minimapRouteGo;
        private RouteResponse currentResponse;

        private void buildMinimapRoute(RouteResponse res)
        {
            var geo = res.routes[0].geometry;
            var worldPositions = new List<Vector2>();

            foreach (var p in geo.coordinates)
            {
                var pos = Map.GeoToWorldPosition(new Mapbox.Utils.Vector2d(p.Latitude, p.Longitude), true);
                worldPositions.Add(new Vector2(pos.x, pos.z));
            }

            if (minimapRouteGo != null) minimapRouteGo.Destroy();

            minimapRouteGo = new GameObject("minimap route game object");
            minimapRouteGo.layer = MinimapLayer;
            var mesh = minimapRouteGo.AddComponent<MeshFilter>().mesh;
            var lineWidth = BaseLineWidth * Mathf.Pow(2.0f, Map.Zoom - 18);
            LineBuilder.BuildLineMesh(worldPositions, mesh, lineWidth);
            minimapRouteGo.AddComponent<MeshRenderer>().sharedMaterial = MinimapLineMaterial;
        }

        IEnumerator search()
        {
            var api = new MapboxApi(MapboxToken);
            yield return api.QueryLocal(s.QueryText, true);

            if (api.ErrorMessage != null)
            {
                s.ErrorMessage = api.ErrorMessage;
                s.Results = new List<GeocodingFeature>();
            }
            else
            {
                s.Results = api.QueryLocalResult.features;
            }
        }

        Vector3 lastCameraPos;
        void Update()
        {
            if (s.View == View.Route)
            {
                var cameraPos = Camera.main.transform.position;
                var arLocationRootAngle = ARLocationManager.Instance.gameObject.transform.localEulerAngles.y;
                var cameraAngle = Camera.main.transform.localEulerAngles.y;
                var mapAngle = cameraAngle - arLocationRootAngle;
                MapboxMapCamera.transform.eulerAngles = new Vector3(90, mapAngle, 0);

                if ((cameraPos - lastCameraPos).magnitude < MinimapStepSize) return;
                lastCameraPos = cameraPos;

                var location = ARLocationManager.Instance.GetLocationForWorldPosition(Camera.main.transform.position);
                Map.SetCenterLatitudeLongitude(new Mapbox.Utils.Vector2d(location.Latitude, location.Longitude));
                Map.UpdateMap();
            }
            else
            {
                MapboxMapCamera.transform.eulerAngles = new Vector3(90, 0, 0);
            }
        }
    }
}
