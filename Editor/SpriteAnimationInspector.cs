using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpriteAnimation))]
[CanEditMultipleObjects]
public class SpriteAnimationInspector : Editor {
    private const float CheckerTileSize = 64f;
    private const float PreviewPadding = 1f;

    private static Texture2D s_CheckerTex;
    private static Material s_TransparentPreviewMaterial;

    private SerializedProperty _frames;
    private SerializedProperty _fps;
    private SerializedProperty _looping;
    private SerializedProperty _pingPong;
    private SerializedProperty _showBlankFrameAtTheEnd;

    private bool _isPlaying;
    private int _previewFrame;
    private bool _previewReverse;
    private int _remainingNonLoopPingPongFlips;
    private bool _showBlank;

    private double _lastTime;
    private float _accumulator;

    private Vector2 _framesScroll;

    private void OnEnable() {
        _frames = serializedObject.FindProperty("frames");
        _fps = serializedObject.FindProperty("fps");
        _looping = serializedObject.FindProperty("looping");
        _pingPong = serializedObject.FindProperty("pingPong");
        _showBlankFrameAtTheEnd = serializedObject.FindProperty("showBlankFrameAtTheEnd");

        ResetPreviewState();

        EditorApplication.update += OnEditorUpdate;
        _lastTime = EditorApplication.timeSinceStartup;
    }

    private void OnDisable() {
        EditorApplication.update -= OnEditorUpdate;
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_frames, includeChildren: true);
        EditorGUILayout.PropertyField(_fps);
        EditorGUILayout.PropertyField(_looping);
        EditorGUILayout.PropertyField(_pingPong);
        EditorGUILayout.PropertyField(_showBlankFrameAtTheEnd);

        serializedObject.ApplyModifiedProperties();

        if (serializedObject.isEditingMultipleObjects) {
            EditorGUILayout.HelpBox("Preview is disabled when editing multiple SpriteAnimations.", MessageType.Info);
            return;
        }

        SpriteAnimation anim = (SpriteAnimation)target;
        DrawFrameThumbnails(anim);
        DrawPlaybackControls(anim);
    }

    public override bool HasPreviewGUI() {
        return target is SpriteAnimation;
    }

    public override void OnPreviewGUI(Rect rect, GUIStyle background) {
        if (serializedObject.isEditingMultipleObjects) {
            return;
        }

        SpriteAnimation anim = (SpriteAnimation)target;
        if (anim == null) {
            return;
        }

        EnsureCheckerTexture();
        EnsureTransparentPreviewMaterial();

        Rect uv = new Rect(0, 0, rect.width / CheckerTileSize, rect.height / CheckerTileSize);
        GUI.DrawTextureWithTexCoords(rect, s_CheckerTex, uv, true);

        Rect inner = new Rect(rect.x + PreviewPadding, rect.y + PreviewPadding, rect.width - PreviewPadding * 2f, rect.height - PreviewPadding * 2f);

        Sprite sprite = GetPreviewSprite(anim);
        if (sprite == null) {
            EditorGUI.DropShadowLabel(inner, "(no frame)");
            return;
        }

        DrawSpritePreview(inner, sprite);
    }

    private void DrawFrameThumbnails(SpriteAnimation anim) {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Frames", EditorStyles.boldLabel);

        int count = anim.frames?.Length ?? 0;
        if (count == 0) {
            EditorGUILayout.HelpBox("No frames assigned.", MessageType.Info);
            return;
        }

        const float thumbWidth = 56f;
        const float pad = 6f;

        // Determine thumbnail height from the first valid sprite's aspect ratio.
        float thumbHeight = thumbWidth;
        for (int j = 0; j < count; j++) {
            if (anim.frames != null) {
                Sprite sprite = anim.frames[j];
                if (sprite != null && sprite.rect.width > 0f && sprite.rect.height > 0f) {
                    float aspect = sprite.rect.width / sprite.rect.height;
                    thumbHeight = thumbWidth / aspect;
                    break;
                }
            }

        }

        float viewWidth = EditorGUIUtility.currentViewWidth - 40f;
        int cols = Mathf.Max(1, Mathf.FloorToInt(viewWidth / (thumbWidth + pad)));
        int rows = Mathf.CeilToInt(count / (float)cols);

        float totalHeight = rows * (thumbHeight + pad) + pad;
        _framesScroll = EditorGUILayout.BeginScrollView(_framesScroll, GUILayout.Height(Mathf.Min(totalHeight, 200f)));

        EnsureCheckerTexture();
        EnsureTransparentPreviewMaterial();

        int i = 0;
        for (int y = 0; y < rows; y++) {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < cols && i < count; x++, i++) {
                Rect rect = GUILayoutUtility.GetRect(thumbWidth, thumbHeight, GUILayout.Width(thumbWidth), GUILayout.Height(thumbHeight));

                if (anim.frames != null) {
                    DrawSpriteThumb(rect, anim.frames[i]);
                }

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
                    _previewFrame = i;
                    _isPlaying = false;
                    _showBlank = false;
                    Repaint();
                    Event.current.Use();
                }

                if (i == _previewFrame && !_showBlank) {
                    Handles.DrawSolidRectangleWithOutline(rect, new Color(0, 0, 0, 0), new Color(0.2f, 0.6f, 1f, 1f));
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawPlaybackControls(SpriteAnimation anim) {
        EditorGUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope()) {
            bool hasFrames = anim.frames != null && anim.frames.Length > 0;
            using (new EditorGUI.DisabledScope(!hasFrames)) {
                string playLabel = _isPlaying ? "Pause" : "Play";
                if (GUILayout.Button(playLabel, GUILayout.Width(80))) {
                    TogglePlay(anim);
                }

                if (GUILayout.Button("Stop", GUILayout.Width(80))) {
                    Stop();
                }

                GUILayout.FlexibleSpace();

                if (hasFrames) {
                    int newFrame = EditorGUILayout.IntSlider(_previewFrame, 0, Mathf.Max(0, anim.frames.Length - 1));
                    if (newFrame != _previewFrame) {
                        _previewFrame = newFrame;
                        _isPlaying = false;
                        _showBlank = false;
                        Repaint();
                    }
                }
            }
        }

        int framesCount = anim.frames?.Length ?? 0;
        string mode = anim.pingPong ? "PingPong" : (anim.looping ? "Loop" : "Once");
        EditorGUILayout.LabelField($"Preview: {(framesCount == 0 ? 0 : _previewFrame + 1)}/{Mathf.Max(1, framesCount)} • {Mathf.Max(0, anim.fps)} fps • {mode}" + (_showBlank ? " • (blank)" : ""));
    }

    private void TogglePlay(SpriteAnimation anim) {
        if (_isPlaying) {
            _isPlaying = false;
            return;
        }

        int count = anim.frames?.Length ?? 0;
        if (count == 0) {
            ResetPreviewState();
            return;
        }

        _isPlaying = true;
        _showBlank = false;
        _accumulator = 0f;
        _lastTime = EditorApplication.timeSinceStartup;

        _previewFrame = Mathf.Clamp(_previewFrame, 0, count - 1);
        _previewReverse = false;
        _remainingNonLoopPingPongFlips = (!anim.looping && anim.pingPong) ? 1 : 0;
    }

    private void Stop() {
        _isPlaying = false;
        _accumulator = 0f;
        _lastTime = EditorApplication.timeSinceStartup;

        _previewReverse = false;
        _showBlank = false;
        _remainingNonLoopPingPongFlips = 0;

        _previewFrame = 0;

        Repaint();
    }

    private void ResetPreviewState() {
        _isPlaying = false;
        _previewFrame = 0;
        _previewReverse = false;
        _remainingNonLoopPingPongFlips = 0;
        _showBlank = false;
        _accumulator = 0f;
    }

    private void OnEditorUpdate() {
        if (!_isPlaying || target == null) {
            return;
        }

        SpriteAnimation anim = (SpriteAnimation)target;
        int count = anim.frames?.Length ?? 0;
        if (count == 0) {
            _isPlaying = false;
            return;
        }

        int fps = Mathf.Max(0, anim.fps);
        if (fps == 0) {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTime);
        _lastTime = now;

        _accumulator += dt;
        float step = 1f / fps;

        bool repaint = false;
        while (_accumulator >= step) {
            _accumulator -= step;
            repaint |= AdvanceFrame(anim);
            if (!_isPlaying) {
                break;
            }
        }

        if (repaint) {
            Repaint();
        }
    }

    private bool AdvanceFrame(SpriteAnimation anim) {
        int count = anim.frames?.Length ?? 0;
        if (count == 0) {
            return false;
        }

        if (_showBlank) {
            // When showing blank at the end, runtime animator would already be stopped.
            _isPlaying = false;
            return true;
        }

        bool reachedEnd = (!_previewReverse && _previewFrame == count - 1) || (_previewReverse && _previewFrame == 0);

        if (reachedEnd) {
            if (!anim.looping) {
                if (anim.showBlankFrameAtTheEnd) {
                    _showBlank = true;
                    // Let the blank be visible for at least one repaint.
                    _isPlaying = false;
                    return true;
                }

                if (_remainingNonLoopPingPongFlips > 0) {
                    _previewReverse = !_previewReverse;
                    _remainingNonLoopPingPongFlips--;
                }
                else {
                    _isPlaying = false;
                    return true;
                }
            }
            else if (anim.pingPong) {
                _previewReverse = !_previewReverse;
            }
            else {
                // Looping forward wrap.
                _previewFrame = 0;
                return true;
            }
        }

        int next = _previewFrame + (_previewReverse ? -1 : 1);
        next = Mathf.Clamp(next, 0, count - 1);

        bool changed = next != _previewFrame;
        _previewFrame = next;
        return changed;
    }

    private Sprite GetPreviewSprite(SpriteAnimation anim) {
        if (_showBlank) {
            return null;
        }

        if (anim.frames == null || anim.frames.Length == 0) {
            return null;
        }

        int i = Mathf.Clamp(_previewFrame, 0, anim.frames.Length - 1);
        return anim.frames[i];
    }

    private static void DrawSpriteThumb(Rect rect, Sprite sprite) {
        Rect uv = new Rect(0, 0, rect.width / CheckerTileSize, rect.height / CheckerTileSize);
        GUI.DrawTextureWithTexCoords(rect, s_CheckerTex, uv, true);

        if (sprite == null) {
            EditorGUI.DropShadowLabel(rect, "(null)");
            return;
        }

        Rect inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
        DrawSpritePreview(inner, sprite);
    }

    private static void DrawSpritePreview(Rect targetRect, Sprite sprite) {
        EnsureTransparentPreviewMaterial();

        Texture2D tex = sprite.texture;
        if (tex == null) {
            EditorGUI.DropShadowLabel(targetRect, "(no texture)");
            return;
        }

        // Use sprite.rect for the source rectangle - it contains the sprite's
        // position and size in texture pixel coordinates. sprite.textureRect
        // can return incorrect values depending on import settings.
        Rect spriteRect = sprite.rect;
        if (spriteRect.width <= 0f || spriteRect.height <= 0f) {
            EditorGUI.DropShadowLabel(targetRect, "(empty)");
            return;
        }

        // Convert to UVs (0..1) for GUI.DrawTextureWithTexCoords.
        Rect uv = new Rect(
            spriteRect.x / tex.width,
            spriteRect.y / tex.height,
            spriteRect.width / tex.width,
            spriteRect.height / tex.height
        );

        float aspect = spriteRect.width / spriteRect.height;
        Rect fitted = GetAspectFitRect(targetRect, aspect);

        GUI.DrawTextureWithTexCoords(fitted, tex, uv, true);
    }

    private static Rect GetAspectFitRect(Rect outer, float aspect) {
        if (outer.width <= 0f || outer.height <= 0f) {
            return outer;
        }

        float outerAspect = outer.width / outer.height;
        if (outerAspect > aspect) {
            // Fit height.
            float h = outer.height;
            float w = h * aspect;
            float x = outer.x + (outer.width - w) * 0.5f;
            return new Rect(x, outer.y, w, h);
        }
        else {
            // Fit width.
            float w = outer.width;
            float h = w / aspect;
            float y = outer.y + (outer.height - h) * 0.5f;
            return new Rect(outer.x, y, w, h);
        }
    }

    private static void EnsureCheckerTexture() {
        if (s_CheckerTex != null) {
            return;
        }

        const int size = 16;
        s_CheckerTex = new Texture2D(size, size, TextureFormat.RGBA32, false) {
            name = "InspectorPreview_Checker",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32 c0 = new Color32(255, 255, 255, 255);
        Color32 c1 = new Color32(205, 205, 205, 255);
        const int block = 4;

        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                bool odd = (x / block + y / block) % 2 == 1;
                s_CheckerTex.SetPixel(x, y, odd ? c1 : c0);
            }
        }

        s_CheckerTex.Apply(false, true);
    }

    private static void EnsureTransparentPreviewMaterial() {
        if (s_TransparentPreviewMaterial != null) {
            return;
        }

        try {
            Type matUtil = Type.GetType("UnityEditor.MaterialUtility, UnityEditor", false);
            if (matUtil != null) {
                MethodInfo mi = matUtil.GetMethod("GetDefaultMaterial",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null) {
                    Material m = mi.Invoke(null, null) as Material;
                    if (m != null) {
                        s_TransparentPreviewMaterial = m;
                        return;
                    }
                }
            }

            MethodInfo extraRes = typeof(EditorGUIUtility).GetMethod("GetBuiltinExtraResource",
                BindingFlags.Static | BindingFlags.Public);
            if (extraRes != null && extraRes.IsGenericMethodDefinition) {
                Material m = extraRes.MakeGenericMethod(typeof(Material))
                    .Invoke(null, new object[] { "Default-Material.mat" }) as Material;
                if (m != null) {
                    s_TransparentPreviewMaterial = m;
                    return;
                }
            }
        }
        catch {
            // ignore
        }

        Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("UI/Unlit/Transparent") ?? Shader.Find("Sprites/Default");
        if (shader != null) {
            s_TransparentPreviewMaterial = new Material(shader) {
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }
}
