using UnityEngine;

namespace EyeCamXR
{
    /// <summary>
    /// Shader/font access with build-safe fallbacks. Runtime-created materials
    /// depend on Shader.Find, which only works for shaders actually shipped in
    /// the build — Unlit/Color+Texture are now in Always Included Shaders,
    /// and these fallbacks make a stripped build visibly wrong (grey, not the
    /// magenta missing-shader screen) instead of unreadable.
    /// </summary>
    public static class EyeCamRender
    {
        public static Shader Solid =>
            Shader.Find("Unlit/Color")
            ?? Shader.Find("Hidden/Internal-Colored")
            ?? Shader.Find("Sprites/Default");

        public static Shader Textured =>
            Shader.Find("Unlit/Texture")
            ?? Shader.Find("Sprites/Default");

        public static Font DefaultFont
        {
            get
            {
                var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f == null)
                    f = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return f;
            }
        }
    }
}
