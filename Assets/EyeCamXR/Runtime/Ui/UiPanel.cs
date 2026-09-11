using UnityEngine;

namespace EyeCamXR
{
    /// <summary>
    /// Minimal world-space text panel (TextMesh — zero package dependencies,
    /// same approach as MuseLogKit's monitor panel). One title line + a body
    /// block, floating at 1.1 m in front of the rig.
    /// </summary>
    public sealed class UiPanel
    {
        readonly TextMesh _title;
        readonly TextMesh _body;
        readonly Transform _root;

        public UiPanel(Transform cameraRig)
        {
            _root = new GameObject("EyeCamUi").transform;
            _root.SetParent(cameraRig, false);
            _root.localPosition = new Vector3(0, 0.05f, 1.1f);

            _title = MakeText("Title", new Vector3(0, 0.28f, 0), 0.05f,
                              new Color(1f, 1f, 1f));
            _body = MakeText("Body", new Vector3(0, 0.20f, 0), 0.032f,
                             new Color(0.85f, 0.85f, 0.85f));
        }

        TextMesh MakeText(string name, Vector3 pos, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.anchor = TextAnchor.UpperCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = size;
            tm.fontSize = 64;
            tm.color = color;
            return tm;
        }

        public void Show(string title, string body, Color? titleColor = null)
        {
            _root.gameObject.SetActive(true);
            _title.text = title;
            _title.color = titleColor ?? Color.white;
            _body.text = body;
        }

        public void Hide() => _root.gameObject.SetActive(false);
    }
}
