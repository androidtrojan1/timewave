using TimeWave.Shared;
using UnityEngine;

namespace TimeWave.Client
{
    /// <summary>Angled top-down RTS camera: arrow keys or middle-drag to pan, wheel to zoom.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        const float Pitch = 55f;
        Camera _camera;
        Vector3 _focus = new Vector3(24f, 0f, 16f);
        Vector2 _mapSize = new Vector2(48f, 32f);
        float _distance = 40f;
        Vector2 _lastMouse;

        public Camera Camera => _camera;

        void Awake()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = go.AddComponent<Camera>();
            }
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.06f, 0.07f, 0.1f);
            _camera.fieldOfView = 45f;
            Apply();
        }

        public void FocusOnMap(Welcome welcome)
        {
            _mapSize = new Vector2(welcome.MapWidth, welcome.MapHeight);
            // Aim below the map centre so the whole map sits above the timeline panel.
            _focus = new Vector3(welcome.MapWidth / 2f, 0f, welcome.MapHeight * 0.28f);
            _distance = Mathf.Max(welcome.MapWidth, welcome.MapHeight) * 1.05f;
            GetComponent<TimeWaveGame>().BuildMap(welcome);
            Apply();
        }

        void LateUpdate()
        {
            float pan = _distance * 0.9f * Time.unscaledDeltaTime;
            if (InputShim.KeyHeld(KeyCode.LeftArrow)) _focus.x -= pan;
            if (InputShim.KeyHeld(KeyCode.RightArrow)) _focus.x += pan;
            if (InputShim.KeyHeld(KeyCode.UpArrow)) _focus.z += pan;
            if (InputShim.KeyHeld(KeyCode.DownArrow)) _focus.z -= pan;

            var mouse = InputShim.MousePosition;
            if (InputShim.MouseHeld(2))
            {
                var delta = mouse - _lastMouse;
                float scale = _distance / Screen.height * 1.2f;
                _focus.x -= delta.x * scale;
                _focus.z -= delta.y * scale;
            }
            _lastMouse = mouse;

            float scroll = InputShim.Scroll;
            if (Mathf.Abs(scroll) > 0.01f && !GetComponent<TimelinePanel>().ContainsScreenPoint(mouse))
                _distance = Mathf.Clamp(_distance * (1f - scroll * 0.12f), 8f, 80f);

            _focus.x = Mathf.Clamp(_focus.x, 0f, _mapSize.x);
            _focus.z = Mathf.Clamp(_focus.z, -_mapSize.y * 0.5f, _mapSize.y);
            Apply();
        }

        void Apply()
        {
            var rotation = Quaternion.Euler(Pitch, 0f, 0f);
            _camera.transform.SetPositionAndRotation(_focus + rotation * new Vector3(0f, 0f, -_distance), rotation);
        }
    }
}
