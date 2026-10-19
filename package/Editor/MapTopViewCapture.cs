using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LogKit.EditorTools
{
    /// <summary>
    /// 습격 씬을 위에서 수직으로 내려다본 이미지(PNG)와 그 이미지가 덮는 월드 좌표 범위(JSON)를 만드는 에디터 창.
    /// LogKit 대시보드는 이 범위로 이미지를 위치 히트맵·습격 리플레이의 배경에 맞춰 깐다(이미지 위쪽 = +Z, 오른쪽 = +X).
    /// 직교 카메라를 절단 높이에 두고 아래를 보게 해 그보다 높은 지붕·천장은 잘라 내고, 렌더러 경계로 정사각형 범위를 정한다.
    /// </summary>
    public sealed class MapTopViewCapture : EditorWindow
    {
        private const string Prefix = "LogKit.MapCapture.";

        private int _resolution = 2048;
        private float _cutHeight = 3f;
        private float _maxRendererSize = 400f;
        private float _padding = 4f;
        private bool _brighten = true;
        private bool _manualBounds;
        private float _minX = -50, _maxX = 50, _minZ = -50, _maxZ = 50;
        private string _outputFolder = "LogKitMaps";
        private string _serverUrl = "http://localhost:5000";
        private string _apiKey = "dev-log-key";
        private bool _upload;
        private Vector2 _scroll;
        private string _log = string.Empty;

        [MenuItem("Tools/LogKit/맵 윗면 캡처")]
        private static void Open() => GetWindow<MapTopViewCapture>("맵 윗면 캡처");

        private void OnEnable()
        {
            _resolution = EditorPrefs.GetInt(Prefix + "resolution", _resolution);
            _cutHeight = EditorPrefs.GetFloat(Prefix + "cutHeight", _cutHeight);
            _maxRendererSize = EditorPrefs.GetFloat(Prefix + "maxRendererSize", _maxRendererSize);
            _padding = EditorPrefs.GetFloat(Prefix + "padding", _padding);
            _brighten = EditorPrefs.GetBool(Prefix + "brighten", _brighten);
            _outputFolder = EditorPrefs.GetString(Prefix + "outputFolder", _outputFolder);
            _serverUrl = EditorPrefs.GetString(Prefix + "serverUrl", _serverUrl);
            _apiKey = EditorPrefs.GetString(Prefix + "apiKey", _apiKey);
            _upload = EditorPrefs.GetBool(Prefix + "upload", _upload);
        }

        private void OnDisable()
        {
            EditorPrefs.SetInt(Prefix + "resolution", _resolution);
            EditorPrefs.SetFloat(Prefix + "cutHeight", _cutHeight);
            EditorPrefs.SetFloat(Prefix + "maxRendererSize", _maxRendererSize);
            EditorPrefs.SetFloat(Prefix + "padding", _padding);
            EditorPrefs.SetBool(Prefix + "brighten", _brighten);
            EditorPrefs.SetString(Prefix + "outputFolder", _outputFolder);
            EditorPrefs.SetString(Prefix + "serverUrl", _serverUrl);
            EditorPrefs.SetString(Prefix + "apiKey", _apiKey);
            EditorPrefs.SetBool(Prefix + "upload", _upload);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("열린 씬 또는 프로젝트 창에서 고른 씬들을 위에서 내려다본 이미지로 찍습니다. " +
                "결과는 <씬>.png와 <씬>.json(minX·maxX·minZ·maxZ)이며 LogKit 대시보드의 리플레이 페이지 '맵 배경 이미지 관리'에 그대로 올리거나 " +
                "아래 '서버에 바로 올리기'로 보냅니다.", MessageType.Info);

            _resolution = EditorGUILayout.IntPopup("해상도", _resolution, new[] { "1024", "2048", "4096" }, new[] { 1024, 2048, 4096 });
            _cutHeight = EditorGUILayout.FloatField(new GUIContent("절단 높이(Y)", "이 높이에서 아래를 찍습니다. 이보다 높은 지붕·천장은 보이지 않습니다."), _cutHeight);
            _brighten = EditorGUILayout.Toggle(new GUIContent("밝게 찍기", "밤 조명 대신 평면 주변광과 위에서 비추는 임시 조명으로 찍고 안개를 끕니다."), _brighten);

            _manualBounds = EditorGUILayout.Toggle(new GUIContent("범위 직접 지정", "끄면 렌더러 경계로 자동 계산합니다."), _manualBounds);
            if (_manualBounds)
            {
                _minX = EditorGUILayout.FloatField("minX", _minX);
                _maxX = EditorGUILayout.FloatField("maxX", _maxX);
                _minZ = EditorGUILayout.FloatField("minZ", _minZ);
                _maxZ = EditorGUILayout.FloatField("maxZ", _maxZ);
            }
            else
            {
                _maxRendererSize = EditorGUILayout.FloatField(new GUIContent("렌더러 최대 크기", "이보다 큰 렌더러(하늘·바닥 판 등)는 범위 계산에서 뺍니다."), _maxRendererSize);
                _padding = EditorGUILayout.FloatField("여백", _padding);
            }

            _outputFolder = EditorGUILayout.TextField(new GUIContent("저장 폴더", "프로젝트 루트 기준(Assets 밖)."), _outputFolder);
            _upload = EditorGUILayout.Toggle("서버에 바로 올리기", _upload);
            if (_upload)
            {
                _serverUrl = EditorGUILayout.TextField("서버 주소", _serverUrl);
                _apiKey = EditorGUILayout.TextField("API 키", _apiKey);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("열린 씬 찍기"))
            {
                CaptureOpenScene();
            }

            var selected = Selection.objects.OfType<SceneAsset>().ToList();
            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                if (GUILayout.Button("고른 씬 " + selected.Count + "개 찍기"))
                {
                    CaptureScenes(selected.Select(AssetDatabase.GetAssetPath).ToList());
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_log.Length > 0 ? _log : "아직 찍은 씬이 없습니다.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void CaptureOpenScene()
        {
            _log = string.Empty;
            Capture(SceneManager.GetActiveScene());
        }

        private void CaptureScenes(List<string> paths)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string restore = SceneManager.GetActiveScene().path;
            _log = string.Empty;
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("맵 윗면 캡처", paths[i], (float)i / paths.Count);
                    Capture(EditorSceneManager.OpenScene(paths[i], OpenSceneMode.Single));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (!string.IsNullOrEmpty(restore))
                {
                    EditorSceneManager.OpenScene(restore, OpenSceneMode.Single);
                }
            }
        }

        /// <summary>씬 하나를 찍어 PNG·JSON으로 저장하고, 설정하면 서버에 올린다.</summary>
        private void Capture(Scene scene)
        {
            if (!TryBounds(scene, out float minX, out float maxX, out float minZ, out float maxZ))
            {
                _log += scene.name + ": 범위를 정할 렌더러가 없습니다.\n";
                return;
            }

            byte[] png = Render(minX, maxX, minZ, maxZ);
            string folder = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, _outputFolder);
            Directory.CreateDirectory(folder);
            string pngPath = Path.Combine(folder, scene.name + ".png");
            string jsonPath = Path.Combine(folder, scene.name + ".json");
            File.WriteAllBytes(pngPath, png);
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"scene\":\"{0}\",\"minX\":{1},\"maxX\":{2},\"minZ\":{3},\"maxZ\":{4},\"cutHeight\":{5},\"resolution\":{6},\"source\":\"unity-capture\"}}",
                scene.name, minX, maxX, minZ, maxZ, _cutHeight, _resolution);
            File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
            _log += string.Format(CultureInfo.InvariantCulture, "{0}: X {1:0.#}~{2:0.#}, Z {3:0.#}~{4:0.#} → {5}\n", scene.name, minX, maxX, minZ, maxZ, pngPath);

            if (_upload)
            {
                _log += scene.name + ": " + Upload(scene.name, png, json) + "\n";
            }

            Repaint();
        }

        /// <summary>찍을 정사각형 범위. 직접 지정이 아니면 너무 큰 렌더러를 뺀 렌더러 경계의 X·Z 범위에 여백을 더해 긴 변으로 맞춘다.</summary>
        private bool TryBounds(Scene scene, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0;
            Bounds? total = null;
            if (_manualBounds)
            {
                total = new Bounds(new Vector3((_minX + _maxX) / 2, 0, (_minZ + _maxZ) / 2), new Vector3(_maxX - _minX, 0, _maxZ - _minZ));
            }
            else
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                        {
                            continue;
                        }

                        Bounds b = r.bounds;
                        if (b.size.x > _maxRendererSize || b.size.z > _maxRendererSize)
                        {
                            continue;
                        }

                        if (total is Bounds t)
                        {
                            t.Encapsulate(b);
                            total = t;
                        }
                        else
                        {
                            total = b;
                        }
                    }
                }

                if (total is Bounds padded)
                {
                    padded.Expand(new Vector3(_padding * 2, 0, _padding * 2));
                    total = padded;
                }
            }

            if (total is not Bounds area || area.size.x <= 0 || area.size.z <= 0)
            {
                return false;
            }

            float half = Mathf.Max(area.size.x, area.size.z) / 2;
            minX = area.center.x - half;
            maxX = area.center.x + half;
            minZ = area.center.z - half;
            maxZ = area.center.z + half;
            return true;
        }

        /// <summary>
        /// 절단 높이에 둔 직교 카메라로 아래를 찍는다. 회전 (90, 0, 0)이면 화면 위쪽 = +Z, 오른쪽 = +X라 대시보드 좌표와 같다.
        /// 밝게 찍기면 주변광·안개를 잠시 바꾸고 위에서 비추는 조명을 더했다가 되돌린다.
        /// </summary>
        private byte[] Render(float minX, float maxX, float minZ, float maxZ)
        {
            var cameraObject = new GameObject("LogKit Top View Camera") { hideFlags = HideFlags.HideAndDontSave };
            var lightObject = _brighten ? new GameObject("LogKit Top View Light") { hideFlags = HideFlags.HideAndDontSave } : null;
            var rt = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientLight = RenderSettings.ambientLight;
            bool fog = RenderSettings.fog;
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = (maxZ - minZ) / 2;
                camera.aspect = 1;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 1000;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.05f, 0.07f, 0.09f, 1);
                camera.transform.SetPositionAndRotation(new Vector3((minX + maxX) / 2, _cutHeight, (minZ + maxZ) / 2), Quaternion.Euler(90, 0, 0));
                camera.targetTexture = rt;

                if (lightObject != null)
                {
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
                    RenderSettings.fog = false;
                    Light light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = 1.1f;
                    light.shadows = LightShadows.None;
                    lightObject.transform.rotation = Quaternion.Euler(80, 30, 0);
                }

                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.Render();
                }

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var texture = new Texture2D(_resolution, _resolution, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, _resolution, _resolution), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;
                byte[] png = texture.EncodeToPNG();
                DestroyImmediate(texture);
                return png;
            }
            finally
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.fog = fog;
                DestroyImmediate(cameraObject);
                if (lightObject != null)
                {
                    DestroyImmediate(lightObject);
                }

                rt.Release();
                DestroyImmediate(rt);
            }
        }

        /// <summary>서버의 POST /api/maps/{scene}에 이미지와 범위 JSON을 올린다.</summary>
        private string Upload(string scene, byte[] png, string json)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("image", png, scene + ".png", "image/png"),
                new MultipartFormFileSection("bounds", Encoding.UTF8.GetBytes(json), scene + ".json", "application/json"),
            };
            using UnityWebRequest request = UnityWebRequest.Post(_serverUrl.TrimEnd('/') + "/api/maps/" + UnityWebRequest.EscapeURL(scene), form);
            request.SetRequestHeader("X-Api-Key", _apiKey);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!operation.isDone && DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Sleep(20);
            }

            if (!operation.isDone)
            {
                request.Abort();
                return "업로드 시간 초과";
            }

            return request.result == UnityWebRequest.Result.Success ? "서버에 올림" : "업로드 실패 " + request.responseCode + " " + request.downloadHandler.text;
        }
    }
}
