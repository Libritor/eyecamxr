using UnityEngine;

/// <summary>
/// Visualization for Muse Band Powers in 3D (MR).
/// </summary>
public class BandPowerVisualizer : MonoBehaviour
{
    [System.Serializable]
    public class Bar
    {
        public string Name;
        public Transform Transform;
        public float TargetValue;
        public Color BarColor;
    }

    public Bar[] bars = new Bar[5];
    public float scaleFactor = 2.0f;
    public float smoothSpeed = 5.0f;

    private void Start()
    {
        // Auto-setup if empty
        if (bars[0].Transform == null)
            CreateBars();
    }

    private void CreateBars()
    {
        string[] names = { "Delta", "Theta", "Alpha", "Beta", "Gamma" };
        Color[] colors = { Color.blue, Color.cyan, Color.green, Color.yellow, Color.red };

        for (int i = 0; i < 5; i++)
        {
            GameObject pivot = new GameObject(names[i]);
            pivot.transform.SetParent(this.transform, false);
            pivot.transform.localPosition = new Vector3((i - 2) * 0.15f, 0, 0);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(pivot.transform, false);
            cube.transform.localPosition = new Vector3(0, 0.5f, 0); // Origin at bottom
            cube.transform.localScale = new Vector3(0.1f, 1f, 0.1f);
            
            // Material
            Renderer r = cube.GetComponent<Renderer>();
            r.material = new Material(Shader.Find("Standard"));
            r.material.color = colors[i];

            bars[i] = new Bar { Name = names[i], Transform = pivot.transform, BarColor = colors[i] };
        }
    }

    private void Update()
    {
        if (MuseOSCManager.Instance == null) return;

        // Fetch Data
        bars[0].TargetValue = MuseOSCManager.Instance.Delta;
        bars[1].TargetValue = MuseOSCManager.Instance.Theta;
        bars[2].TargetValue = MuseOSCManager.Instance.Alpha;
        bars[3].TargetValue = MuseOSCManager.Instance.Beta;
        bars[4].TargetValue = MuseOSCManager.Instance.Gamma;

        // Animate
        foreach (var bar in bars)
        {
            if (bar.Transform != null)
            {
                // Clamp and Scale
                float h = Mathf.Clamp(bar.TargetValue, 0.01f, 1.0f) * scaleFactor;
                Vector3 s = bar.Transform.localScale;
                s.y = Mathf.Lerp(s.y, h, Time.deltaTime * smoothSpeed);
                bar.Transform.localScale = s;
            }
        }
    }
}
