using UnityEngine;
using UnityEngine.Android;
using System;
using System.Collections.Generic;

#if UNITY_ANDROID
public class MuseHandler : MonoBehaviour
{
    public static MuseHandler Instance { get; private set; }

    // Events for data
    public event Action<float> OnAlphaChanged;
    public event Action<float> OnBetaChanged;
    public event Action<float> OnThetaChanged;
    public event Action<float> OnDeltaChanged;
    public event Action<float> OnGammaChanged;
    
    // Properties to access latest values
    public float Alpha { get; private set; }
    public float Beta { get; private set; }
    public float Theta { get; private set; }
    public float Delta { get; private set; }
    public float Gamma { get; private set; }
    
    public string MuseName { get; private set; } = "None";
    public string ConnectionStatus { get; set; } = "Disconnected";
    public bool IsConnected => ConnectionStatus == "CONNECTED";
    public bool IsScanning { get; set; }

    private AndroidJavaObject _museManager;
    private AndroidJavaObject _currentMuse;
    private MuseListenerProxy _museListener;
    private ConnectionListenerProxy _connectionListener;
    private DataListenerProxy _dataListener;

    private List<AndroidJavaObject> _foundMuses = new List<AndroidJavaObject>();
    public event Action OnMuseListUpdated;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        InitializeMuse();
    }

    private void InitializeMuse()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var managerClass = new AndroidJavaClass("com.choosemuse.libmuse.MuseManagerAndroid"))
            {
                _museManager = managerClass.CallStatic<AndroidJavaObject>("getInstance");
                _museManager.Call("setContext", activity);
                
                _museListener = new MuseListenerProxy(this);
                _museManager.Call("setMuseListener", _museListener);
                
                _connectionListener = new ConnectionListenerProxy(this);
                _dataListener = new DataListenerProxy(this);
            }
            Debug.Log("[MuseHandler] Initialized.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[MuseHandler] Failed to initialize: {e.Message}");
        }
    }

    public void StartScanning()
    {
        if (!Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN") ||
            !Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_CONNECT") ||
            !Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Debug.Log("[MuseHandler] Requesting Permissions...");
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionDenied += PermissionDenied;
            callbacks.PermissionGranted += PermissionGranted;
            
            string[] permissions = {
                "android.permission.BLUETOOTH_SCAN",
                "android.permission.BLUETOOTH_CONNECT",
                Permission.FineLocation,
                Permission.CoarseLocation
            };
            Permission.RequestUserPermissions(permissions, callbacks);
        }
        else
        {
            StartScanningInternal();
        }
    }

    private void PermissionDenied(string permissionName)
    {
        Debug.LogError($"[MuseHandler] Permission {permissionName} Denied!");
    }

    private void PermissionGranted(string permissionName)
    {
        Debug.Log($"[MuseHandler] Permission {permissionName} Granted.");
        if (permissionName.Contains("BLUETOOTH_CONNECT")) StartScanningInternal();
    }

    private void StartScanningInternal()
    {
        Debug.Log("[MuseHandler] Permission OK. Start Scanning.");
        IsScanning = true;
        _museManager?.Call("stopListening");
        _museManager?.Call("startListening");
    }

    public void Connect(AndroidJavaObject muse)
    {
        if (_currentMuse != null) Disconnect();

        _currentMuse = muse;
        _currentMuse.Call("unregisterAllListeners");
        _currentMuse.Call("registerConnectionListener", _connectionListener);
        
        // Register for EEG and Relative Band Powers
        RegisterListener("EEG");
        RegisterListener("ALPHA_RELATIVE");
        RegisterListener("BETA_RELATIVE");
        RegisterListener("THETA_RELATIVE");
        RegisterListener("DELTA_RELATIVE");
        RegisterListener("GAMMA_RELATIVE");
        
        _currentMuse.Call("runAsynchronously");
    }

    private void RegisterListener(string typeName)
    {
        using (var typeClass = new AndroidJavaClass("com.choosemuse.libmuse.MuseDataPacketType"))
        using (var typeEnum = typeClass.GetStatic<AndroidJavaObject>(typeName))
        {
            _currentMuse.Call("registerDataListener", _dataListener, typeEnum);
        }
    }

    public void Disconnect()
    {
        if (_currentMuse != null)
        {
            _currentMuse.Call("disconnect");
            _currentMuse = null;
        }
    }

    public List<string> GetAvailableMuseNames()
    {
        var names = new List<string>();
        foreach (var m in _foundMuses)
        {
            names.Add(m.Call<string>("getName"));
        }
        return names;
    }

    public void ConnectToMuse(int index)
    {
        if (index >= 0 && index < _foundMuses.Count)
        {
            Connect(_foundMuses[index]);
        }
        else
        {
            Debug.LogError("[MuseHandler] Invalid Muse Index");
        }
    }
    
    public void UpdateMuseList()
    {
        _foundMuses.Clear();
        var muses = _museManager.Call<AndroidJavaObject>("getMuses");
        int size = muses.Call<int>("size");
        for (int i = 0; i < size; i++)
        {
            var muse = muses.Call<AndroidJavaObject>("get", i);
            _foundMuses.Add(muse);
            string name = muse.Call<string>("getName");
            Debug.Log($"[MuseHandler] Found Muse: {name}");
        }
        
        OnMuseListUpdated?.Invoke();
    }

    // --- PROXIES ---

    class MuseListenerProxy : AndroidJavaProxy
    {
        private MuseHandler _handler;
        public MuseListenerProxy(MuseHandler handler) : base("com.choosemuse.libmuse.MuseListener") { _handler = handler; }

        public void museListChanged()
        {
            Debug.Log("[MuseHandler] Muse List Changed");
            _handler.UpdateMuseList();
        }
    }

    class ConnectionListenerProxy : AndroidJavaProxy
    {
        private MuseHandler _handler;
        public ConnectionListenerProxy(MuseHandler handler) : base("com.choosemuse.libmuse.MuseConnectionListener") { _handler = handler; }

        public void receiveMuseConnectionPacket(AndroidJavaObject packet, AndroidJavaObject muse)
        {
            var state = packet.Call<AndroidJavaObject>("getCurrentConnectionState"); // Enum
            string stateStr = state.Call<string>("toString");
            _handler.ConnectionStatus = stateStr;
            // _handler.MuseName = muse.Call<string>("getName"); // Might be called on thread, careful
            if (stateStr == "CONNECTED") _handler.IsScanning = false;
            Debug.Log($"[MuseHandler] Connection State: {stateStr}");
        }
    }

    class DataListenerProxy : AndroidJavaProxy
    {
        private MuseHandler _handler;
        public DataListenerProxy(MuseHandler handler) : base("com.choosemuse.libmuse.MuseDataListener") { _handler = handler; }

        public void receiveMuseDataPacket(AndroidJavaObject packet, AndroidJavaObject muse)
        {
            var typeObj = packet.Call<AndroidJavaObject>("packetType");
            string type = typeObj.Call<string>("toString");

            if (type.Contains("RELATIVE"))
            {
                // Simple average logic for example
                double total = 0;
                int count = 0;
                for (int i = 0; i < 4; i++)
                {
                    double v = packet.Call<double>("getEegChannelValue", GetEegEnum(i));
                    if (!double.IsNaN(v)) { total += v; count++; }
                }
                float val = count > 0 ? (float)(total / count) : 0f;

                // Dispatch on Main Thread if UI depends on it?
                // For now, invocation is direct. Unity callbacks usually fine from Proxy if attached.
                if (type == "ALPHA_RELATIVE") _handler.OnAlphaChanged?.Invoke(val);
                else if (type == "BETA_RELATIVE") _handler.OnBetaChanged?.Invoke(val);
                else if (type == "THETA_RELATIVE") _handler.OnThetaChanged?.Invoke(val);
                else if (type == "DELTA_RELATIVE") _handler.OnDeltaChanged?.Invoke(val);
                else if (type == "GAMMA_RELATIVE") _handler.OnGammaChanged?.Invoke(val);
            }
        }

        public void receiveMuseArtifactPacket(AndroidJavaObject packet, AndroidJavaObject muse) { }

        private AndroidJavaObject GetEegEnum(int index)
        {
            string[] names = { "EEG1", "EEG2", "EEG3", "EEG4" };
            using (var cls = new AndroidJavaClass("com.choosemuse.libmuse.Eeg"))
                return cls.GetStatic<AndroidJavaObject>(names[index]);
        }
    }
}
#else
// Mock for Editor
public class MuseHandler : MonoBehaviour
{
    public static MuseHandler Instance { get; private set; }
    public event Action<float> OnAlphaChanged;
    public event Action<float> OnBetaChanged;
    public event Action<float> OnThetaChanged;
    public event Action<float> OnDeltaChanged;
    public event Action<float> OnGammaChanged;
    public event Action OnMuseListUpdated; 

    public float Alpha => 0f;
    public float Beta => 0f;
    public float Theta => 0f;
    public float Delta => 0f;
    public float Gamma => 0f;
    
    public string MuseName => "Editor Mock";
    public string ConnectionStatus => "Simulated";
    public bool IsConnected => true;
    public bool IsScanning { get; private set; }

    public void StartScanning() 
    { 
        IsScanning = true; 
        Debug.Log("Editor: Start Scanning");
        // Simulate finding a device after 1s
        Invoke(nameof(SimulateFoundDevice), 1.0f);
    }
    
    private void SimulateFoundDevice()
    {
        Debug.Log("Editor: Found Muse-01");
        OnMuseListUpdated?.Invoke();
    }
    
    public List<string> GetAvailableMuseNames() { return new List<string> { "Muse-01 (Simulated)" }; }
    public void ConnectToMuse(int index) { Debug.Log($"Editor: Connected to Muse {index}"); }

    private void Awake() { Instance = this; }
}
#endif
