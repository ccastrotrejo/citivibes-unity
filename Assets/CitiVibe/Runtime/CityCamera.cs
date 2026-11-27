using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CitiVibe
{
    /// <summary>
    /// Orthographic diorama camera. Drag to pan, right-drag (or Ctrl/Cmd-drag) to orbit and tilt,
    /// scroll to zoom; WASD pans, Q/E rotates, R resets.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CityCamera : MonoBehaviour
    {
        public Vector3 target = new(0f, 6f, 0f);
        public float yaw = 135f, pitch = 34f, size = 82f;
        public float minSize = 10f, maxSize = 110f, minPitch = 14f, maxPitch = 80f, distance = 320f;
        public Vector2 bounds = new(80f, 70f);

        Camera cam;
        Vector3 homeTarget;
        float homeYaw, homePitch, homeSize, smoothSize;

        void Start()
        {
            cam = GetComponent<Camera>();
            homeTarget = target;
            homeYaw = yaw;
            homePitch = pitch;
            homeSize = smoothSize = size;
            Apply();
        }

        void LateUpdate()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            float worldPerPixel = 2f * cam.orthographicSize / Mathf.Max(1, Screen.height);
            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, Vector3.up);
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                bool modifier = kb != null && (kb.leftCtrlKey.isPressed || kb.leftCommandKey.isPressed);
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed || (mouse.leftButton.isPressed && modifier))
                {
                    yaw += delta.x * 0.25f;
                    pitch = Mathf.Clamp(pitch - delta.y * 0.2f, minPitch, maxPitch);
                }
                else if (mouse.leftButton.isPressed)
                {
                    target -= (right * delta.x + forward * delta.y / Mathf.Sin(pitch * Mathf.Deg2Rad)) * worldPerPixel;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) size = Mathf.Clamp(size * Mathf.Exp(-Mathf.Sign(scroll) * Mathf.Min(Mathf.Abs(scroll), 120f) * 0.0018f), minSize, maxSize);
            }
            if (kb != null)
            {
                float pan = size * dt;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) target += forward * pan;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) target -= forward * pan;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) target += right * pan;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) target -= right * pan;
                if (kb.qKey.isPressed) yaw -= 60f * dt;
                if (kb.eKey.isPressed) yaw += 60f * dt;
                if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) size = Mathf.Max(minSize, size * (1f - dt));
                if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) size = Mathf.Min(maxSize, size * (1f + dt));
                if (kb.rKey.wasPressedThisFrame)
                {
                    target = homeTarget;
                    yaw = homeYaw;
                    pitch = homePitch;
                    size = homeSize;
                }
            }
#endif
            target.x = Mathf.Clamp(target.x, -bounds.x, bounds.x);
            target.z = Mathf.Clamp(target.z, -bounds.y, bounds.y);
            Apply();
        }

        void Apply()
        {
            smoothSize = Mathf.Lerp(smoothSize, size, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            if (!Application.isPlaying) smoothSize = size;
            cam.orthographicSize = smoothSize;
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            transform.position = target + new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p)) * distance;
            transform.LookAt(target);
        }
    }
}
