using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class HeadPoseStreamer : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private Transform centerEyeAnchor;
    [SerializeField] private float frequencySeconds = 1f / 30f;

    [Header("Logging")]
    [SerializeField] private bool logToHUD = true;
    [SerializeField] private string hudLogSource = "Right";

    private UdpClient _udpClient;
    private TcpClient _tcpClient;
    private NetworkStream _tcpStream;
    private IPEndPoint _remoteEndPoint;
    private bool _isInitialized;
    private int _currentProtocol = -1;
    private float _timer;

    private readonly StringBuilder _sbPacket = new StringBuilder(256);
    private readonly StringBuilder _sbLog = new StringBuilder(256);
    private uint _frameId;

    private const long UnixEpochTicks = 621355968000000000L;

    private void Update()
    {
        if (AppManager.Instance == null || !AppManager.Instance.isStreaming || !AppManager.Instance.TrackHeadPose)
        {
            if (_isInitialized)
            {
                Disconnect();
            }
            return;
        }

        if (!_isInitialized)
        {
            InitializeNetwork();
        }

        Transform source = ResolveHeadSource();
        if (source == null)
        {
            return;
        }

        _timer += Time.deltaTime;
        if (_timer < frequencySeconds)
        {
            return;
        }
        _timer = 0f;

        // Capture timestamp immediately before sampling the pose so `t` represents pose capture time.
        ulong poseCaptureTimestampNs = GetUnixTimestampNs();
        Vector3 position = source.position;
        Quaternion rotation = source.rotation;

        BuildAndSendPacket(position, rotation, poseCaptureTimestampNs);
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private Transform ResolveHeadSource()
    {
        if (centerEyeAnchor != null)
        {
            return centerEyeAnchor;
        }
        if (Camera.main != null)
        {
            return Camera.main.transform;
        }
        return null;
    }

    private void BuildAndSendPacket(Vector3 position, Quaternion rotation, ulong poseCaptureTimestampNs)
    {
        _sbPacket.Clear();
        _sbLog.Clear();

        bool addDebugHeaderMeta = AppManager.Instance != null && AppManager.Instance.ShowDebugInfo;
        if (addDebugHeaderMeta)
        {
            _frameId++;
            AppendHeaderWithMeta(_sbPacket, "pose", _frameId, poseCaptureTimestampNs);
            _sbPacket.Append(", ");
        }
        else
        {
            _sbPacket.Append("Head pose:, ");
        }

        AppendVector3(_sbPacket, position);
        _sbPacket.Append(", ");
        AppendQuaternion(_sbPacket, rotation);

        if (logToHUD)
        {
            _sbLog.AppendLine("=== [Head] Pose ===");
            _sbLog.Append("Pos: ").AppendLine(FormatVector3Tuple(position));
            _sbLog.Append("Rot: ").AppendLine(FormatQuaternionTuple(rotation));
            LogHUD(_sbLog.ToString());
        }

        SendData(_sbPacket.ToString());
    }

    private void InitializeNetwork()
    {
        if (AppManager.Instance == null)
        {
            return;
        }

        string ip = AppManager.Instance.ServerIP;
        int port = AppManager.Instance.ServerPort;
        _currentProtocol = AppManager.Instance.SelectedProtocol;

        try
        {
            if (_currentProtocol == 0)
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SendBufferSize = 0;
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                LogHUD($"Head UDP Ready: {ip}:{port}");
            }
            else
            {
                _tcpClient = new TcpClient(AddressFamily.InterNetwork);
                _tcpClient.NoDelay = true;
                _tcpClient.SendTimeout = 1000;
                _tcpClient.ReceiveTimeout = 1000;
                _tcpClient.Connect(ip, port);
                _tcpStream = _tcpClient.GetStream();
                string type = _currentProtocol == 1 ? "Wired" : "WiFi";
                LogHUD($"Head TCP({type}) Connected: {ip}:{port}");
            }
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            LogHUD($"Head Conn Error: {ex.Message}");
            if (AppManager.Instance != null)
            {
                AppManager.Instance.StopStreaming();
            }
        }
    }

    private void SendData(string message)
    {
        if (AppManager.Instance != null && !AppManager.Instance.isStreaming)
        {
            return;
        }

        try
        {
            if (_currentProtocol == 0 && _udpClient != null)
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                _udpClient.Send(data, data.Length, _remoteEndPoint);
            }
            else if ((_currentProtocol == 1 || _currentProtocol == 2) && _tcpStream != null && _tcpStream.CanWrite)
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                _tcpStream.Write(data, 0, data.Length);
            }
        }
        catch (Exception ex)
        {
            Disconnect();
            if (AppManager.Instance != null)
            {
                AppManager.Instance.HandleDisconnection("Head pose send failed: " + ex.Message);
            }
        }
    }

    private void Disconnect()
    {
        try
        {
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
            if (_tcpStream != null)
            {
                _tcpStream.Close();
                _tcpStream = null;
            }
            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient = null;
            }
        }
        catch
        {
            // ignored
        }
        _isInitialized = false;
    }

    private static ulong GetUnixTimestampNs()
    {
        return (ulong)((DateTime.UtcNow.Ticks - UnixEpochTicks) * 100L);
    }

    private static void AppendHeaderWithMeta(
        StringBuilder sb,
        string section,
        uint frameId,
        ulong poseCaptureTimestampNs
    )
    {
        sb.Append("Head ")
          .Append(section)
          .Append(" | f = ")
          .Append(frameId)
          .Append(" | t = ")
          .Append(poseCaptureTimestampNs)
          .Append(":");
    }

    private static void AppendVector3(StringBuilder sb, Vector3 vec)
    {
        sb.Append(vec.x.ToString("F4")).Append(", ")
          .Append(vec.y.ToString("F4")).Append(", ")
          .Append(vec.z.ToString("F4"));
    }

    private static void AppendQuaternion(StringBuilder sb, Quaternion q)
    {
        sb.Append(q.x.ToString("F3")).Append(", ")
          .Append(q.y.ToString("F3")).Append(", ")
          .Append(q.z.ToString("F3")).Append(", ")
          .Append(q.w.ToString("F3"));
    }

    private static string FormatVector3Tuple(Vector3 vec)
    {
        return $"({vec.x:F3}, {vec.y:F3}, {vec.z:F3})";
    }

    private static string FormatQuaternionTuple(Quaternion q)
    {
        return $"({q.x:F3}, {q.y:F3}, {q.z:F3}, {q.w:F3})";
    }

    private void LogHUD(string msg)
    {
        if (logToHUD && LogManager.Instance != null)
        {
            LogManager.Instance.Log(hudLogSource, msg);
        }
    }
}
