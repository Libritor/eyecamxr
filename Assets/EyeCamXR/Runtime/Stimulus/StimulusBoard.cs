using UnityEngine;

namespace EyeCamXR
{
    /// <summary>
    /// The visual stimulus rig: a dim reference quad showing the whole target
    /// (context for the subject), and the flashing cursor cell — a quad whose
    /// color alternates yellow/blue (shaded by cell luminance) with a white
    /// fixation dot, at 0.55 m in front of the camera rig. Region-serial by
    /// design: only the cursor cell ever flickers.
    /// Calibration mode shows a single large central patch instead.
    /// Built-in RP, Unlit/Color materials, MaterialPropertyBlock updates.
    /// </summary>
    public sealed class StimulusBoard
    {
        const float Depth = 0.55f;
        const float BoardW = 0.44f;   // ~44 deg at 0.55 m

        readonly Transform _root;
        readonly Renderer _cursor;
        readonly Renderer _patch;
        readonly Transform _dot;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        static readonly int ColorId = Shader.PropertyToID("_Color");

        double[,] _target;
        int _gw, _gh;
        float _boardH;

        public StimulusBoard(Transform cameraRig)
        {
            _root = new GameObject("EyeCamStimulus").transform;
            _root.SetParent(cameraRig, false);
            _root.localPosition = new Vector3(0, 0, Depth);

            _cursor = MakeQuad("Cursor", _root, new Color(0, 0, 0, 1));
            _patch = MakeQuad("CalibPatch", _root, new Color(0, 0, 0, 1));
            _patch.transform.localScale = new Vector3(0.30f, 0.30f, 1);
            _patch.gameObject.SetActive(false);

            var dotGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dotGo.name = "FixationDot";
            Object.Destroy(dotGo.GetComponent<Collider>());
            dotGo.transform.SetParent(_root, false);
            dotGo.transform.localScale = new Vector3(0.004f, 0.004f, 1);
            dotGo.transform.localPosition = new Vector3(0, 0, -0.001f);
            var dotMat = new Material(EyeCamRender.Solid);
            dotMat.color = Color.white;
            dotGo.GetComponent<Renderer>().sharedMaterial = dotMat;
            _dot = dotGo.transform;

            SetVisible(false);
        }

        static Renderer MakeQuad(string name, Transform parent, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var mat = new Material(EyeCamRender.Solid);
            mat.color = c;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.GetComponent<Renderer>();
        }

        public void SetTarget(double[,] target)
        {
            _target = target;
            _gh = target.GetLength(0);
            _gw = target.GetLength(1);
            _boardH = BoardW * _gh / _gw;
            float cw = BoardW / _gw, ch = _boardH / _gh;
            _cursor.transform.localScale = new Vector3(cw, ch, 1);
        }

        public void SetVisible(bool on)
        {
            _root.gameObject.SetActive(on);
        }

        public void ShowCalibrationPatch(bool flickerOn, bool stimOn)
        {
            _patch.gameObject.SetActive(true);
            _cursor.gameObject.SetActive(false);
            _dot.localPosition = new Vector3(0, 0, -0.001f);
            Color c = !stimOn ? Color.black
                : (flickerOn ? new Color(1f, 1f, 0f) : new Color(0f, 0f, 1f));
            _mpb.SetColor(ColorId, c);
            _patch.SetPropertyBlock(_mpb);
        }

        /// <summary>Show the cursor over cell (gx, gy); luminance shades the
        /// flicker colors exactly like stimulus.py (0.3 + 0.7*lum).</summary>
        public void ShowScanCell(int gx, int gy, bool flickerOn)
        {
            _patch.gameObject.SetActive(false);
            _cursor.gameObject.SetActive(true);
            double lum = _target[gy, gx];
            float cw = BoardW / _gw, ch = _boardH / _gh;
            var pos = new Vector3(
                -BoardW / 2 + cw * (gx + 0.5f),
                _boardH / 2 - ch * (gy + 0.5f),
                0);
            _cursor.transform.localPosition = pos;
            _dot.localPosition = pos + new Vector3(0, 0, -0.001f);

            Color c;
            if (lum <= 0.05) c = Color.black;
            else
            {
                float shade = (float)(0.3 + 0.7 * lum);
                c = flickerOn ? new Color(shade, shade, 0f)
                              : new Color(0f, 0f, shade);
            }
            _mpb.SetColor(ColorId, c);
            _cursor.SetPropertyBlock(_mpb);
        }

        public double Luminance(int gx, int gy) => _target[gy, gx];
    }
}
