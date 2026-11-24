using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class TcpClientExample : MonoBehaviour
{
    [Header("連線設定")]
    public string host = "127.0.0.1"; // 預設 Node.js Server IP
    public int port = 5000;           // 預設 Node.js Server Port

    [Header("設備設定")]
    [Tooltip("這台 Quest 對應的 device_num（字串），例如 \"49\"")]
    public string deviceNum;

    [Header("顯示設定")]
    [Tooltip("用來在右手控制器上顯示 IP / 訊息 的腳本 (ControllerTextDisplay)")]
    public ControllerTextDisplay rightControllerIpDisplay;

    [Header("Exhibition 設定")]
    [Tooltip("要被 position / rotation 更新的根物件")]
    public Transform exhibitionRoot;

    private TcpClient _client;
    private NetworkStream _stream;
    private Thread _receiveThread;
    private bool _isConnected = false;

    // 在背景執行緒收到的訊息，丟到主執行緒處理
    private string _lastReceivedMessage = "";

    // ===== 用來實作「每次都是絕對值」的狀態 =====
    private bool _hasOriginalTransform = false;
    private Vector3 _originalPosition;
    private Quaternion _originalRotation;

    // 記錄上一筆 offset（debug 用）
    private Vector3 _lastPosOffset = Vector3.zero;
    private Vector3 _lastRotOffset = Vector3.zero;

    private void Start()
    {
        Debug.LogWarning("TcpClientExample Start");

        // 1. 直接在 C# 從 Android Intent 讀 host / port
        LoadHostAndPortFromIntent_CSharpOnly();

        // 2. 連線
        ConnectToServer();

        // 3. 顯示 IP 在右手手把 + 傳 JSON 給 server
        ShowIpAndSendHelloJson();
    }

    /// <summary>
    /// 純 C# 版本：在 Android 上直接用 AndroidJavaObject 讀 Activity.getIntent() 的 extras
    /// 不需要 Java 插件、不需要 AAR。
    /// </summary>
    private void LoadHostAndPortFromIntent_CSharpOnly()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            // 取得 UnityPlayer.currentActivity
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null)
                {
                    Debug.LogWarning("[TcpClientExample] currentActivity 為 null，無法從 Intent 讀取參數，改用預設 host/port。");
                    return;
                }

                // 取得 activity.getIntent()
                AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
                if (intent == null)
                {
                    Debug.LogWarning("[TcpClientExample] getIntent() 為 null，改用預設 host/port。");
                    return;
                }

                // ---- 讀 String Extra: qqquest_server_ip ----
                string ipFromAdb = intent.Call<string>("getStringExtra", "qqquest_server_ip");

                // ---- 讀 int Extra: qqquest_server_port ----
                int portFromAdb = intent.Call<int>("getIntExtra", "qqquest_server_port", -1);

                if (!string.IsNullOrEmpty(ipFromAdb))
                {
                    host = ipFromAdb;
                }

                if (portFromAdb > 0)
                {
                    port = portFromAdb;
                }

                Debug.Log($"[Intent C#] host = {host}, port = {port}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TcpClientExample] 從 Intent 讀取 host/port 失敗，改用 Inspector 預設值。錯誤：" + e.Message);
        }
#else
        Debug.Log($"[Intent C#] 非 Android 實機（或在 Editor），使用 Inspector 預設 host/port：{host}:{port}");
#endif
    }

    /// <summary>
    /// 顯示 IP 並同時向 server 傳送一個 JSON:
    /// {
    ///   "type": "hello",
    ///   "exhibitionWorldPos": { "x": ..., "y": ..., "z": ... }
    /// }
    /// </summary>
    private void ShowIpAndSendHelloJson()
    {
        // 顯示 IP 在右手手把
        if (rightControllerIpDisplay != null)
        {
            string textToShow = $"IP:\n{host}\nDeviceNum:\n{deviceNum}";
            rightControllerIpDisplay.SetText(textToShow);
        }
        else
        {
            Debug.LogWarning("[TcpClientExample] 尚未指定 rightControllerIpDisplay，無法在控制器上顯示 IP。");
        }

        // 組 JSON 並傳送
        string json = BuildHelloExhibitionJson();
        SendMessageToServer(json);
    }

    /// <summary>
    /// 依照目前 exhibitionRoot 的世界座標組出 JSON 字串
    /// </summary>
    private string BuildHelloExhibitionJson()
    {
        if (exhibitionRoot == null)
        {
            Debug.LogWarning("[TcpClientExample] exhibitionRoot 未指定，將傳送 null 座標。");
            return "{\"type\":\"hello\",\"exhibitionWorldPos\":null}";
        }

        Vector3 pos = exhibitionRoot.position;
        string json = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{{\"type\":\"hello\",\"exhibitionWorldPos\":{{\"x\":{0},\"y\":{1},\"z\":{2}}}}}",
            pos.x,
            pos.y,
            pos.z
        );
        return json;
    }

    private void ConnectToServer()
    {
        try
        {
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();
            _isConnected = true;

            Debug.Log("成功連線到 Server: " + host + ":" + port);

            // 開啟一條執行緒專門接收資料，避免堵住 Unity 主執行緒
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("連線 Server 失敗: " + e.Message);
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (_isConnected)
            {
                if (_stream == null || !_stream.CanRead) break;

                int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    // 對方關閉連線
                    Debug.Log("Server 已關閉連線");
                    _isConnected = false;
                    break;
                }

                string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                _lastReceivedMessage = msg;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("ReceiveLoop 錯誤: " + e.Message);
            _isConnected = false;
        }
    }

    private void Update()
    {
        if (!string.IsNullOrEmpty(_lastReceivedMessage))
        {
            string msg = _lastReceivedMessage;
            _lastReceivedMessage = "";

            Debug.Log("[RAW FROM SERVER]\n" + msg);

            TryHandleJsonMessage(msg);
        }
    }

    /// <summary>
    /// 根據 type 分派處理
    /// </summary>
    private void TryHandleJsonMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg) || !msg.Contains("{") || !msg.Contains("type"))
            return;

        try
        {
            MessageHeader header = JsonUtility.FromJson<MessageHeader>(msg);
            if (header == null || string.IsNullOrEmpty(header.type))
                return;

            if (header.type == "welcome")
            {
                WelcomeMessage welcome = JsonUtility.FromJson<WelcomeMessage>(msg);
                if (welcome != null)
                    ShowWelcomeOnController(welcome);
            }
            else if (header.type == "params_update")
            {
                // 針對新 JSON 結構解析 device_values 陣列
                ParseParamsUpdateByString(msg);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TcpClientExample] TryHandleJsonMessage 解析 JSON 失敗：" + e.Message);
        }
    }

    private void ShowWelcomeOnController(WelcomeMessage data)
    {
        if (rightControllerIpDisplay == null)
        {
            Debug.LogWarning("[TcpClientExample] rightControllerIpDisplay 未指定，無法顯示 welcome 訊息。");
            return;
        }

        string roomLine = string.IsNullOrEmpty(data.room_name)
            ? $"Room ID: {data.room_id}"
            : $"Room {data.room_name}";

        string messageLine = string.IsNullOrEmpty(data.message)
            ? "Welcome!"
            : data.message;

        string text = $"{roomLine}\n{messageLine}";
        rightControllerIpDisplay.SetText(text);
    }

    /// <summary>
    /// 從 params_update 的 JSON 中，找到 device_values 陣列裡
    /// device_num == this.deviceNum 的那一筆，取出 value / rotation_value，
    /// 然後更新 exhibitionRoot 的 position / rotation（絕對 offset）。
    /// </summary>
    private void ParseParamsUpdateByString(string json)
    {
        if (string.IsNullOrEmpty(deviceNum))
        {
            Debug.LogWarning("[TcpClientExample] deviceNum 尚未在 Inspector 設定，略過 params_update。");
            return;
        }

        try
        {
            // 找到 "device_values": [
            const string deviceValuesKey = "\"device_values\"";
            int dvIndex = json.IndexOf(deviceValuesKey, StringComparison.Ordinal);
            if (dvIndex < 0)
            {
                Debug.LogWarning("[TcpClientExample] 找不到 device_values 區塊。");
                return;
            }

            int bracketStart = json.IndexOf('[', dvIndex);
            int bracketEnd = json.IndexOf(']', bracketStart + 1);
            if (bracketStart < 0 || bracketEnd < 0 || bracketEnd <= bracketStart)
            {
                Debug.LogWarning("[TcpClientExample] device_values 陣列方括號不完整。");
                return;
            }

            // device_values 陣列內容字串（不含兩端 []）
            string deviceValuesArray = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);

            // 在這段字串裡尋找我們的 device_num
            string targetDeviceNumKey = "\"device_num\":\"" + deviceNum + "\"";
            int myIndex = deviceValuesArray.IndexOf(targetDeviceNumKey, StringComparison.Ordinal);
            if (myIndex < 0)
            {
                Debug.Log($"[TcpClientExample] params_update 中沒有匹配 device_num={deviceNum} 的項目。");
                return;
            }

            // 向前找此物件的 '{'，向後找對應的 '}'
            int objStart = deviceValuesArray.LastIndexOf('{', myIndex);
            int objEnd = deviceValuesArray.IndexOf('}', myIndex);
            if (objStart < 0 || objEnd < 0 || objEnd <= objStart)
            {
                Debug.LogWarning("[TcpClientExample] 找到 device_num 但對應物件的大括號不完整。");
                return;
            }

            string myDeviceObj = deviceValuesArray.Substring(objStart, objEnd - objStart + 1);
            Debug.Log("[TcpClientExample] Matched device object: " + myDeviceObj);

            // 解析這個物件中的 device_id / value / rotation_value
            string deviceId = ExtractJsonStringField(myDeviceObj, "device_id");
            string value = ExtractJsonStringField(myDeviceObj, "value");
            string rotationValue = ExtractJsonStringField(myDeviceObj, "rotation_value");

            // 顯示在手把上
            if (rightControllerIpDisplay != null)
            {
                string text =
                    $"device_num: {deviceNum}\n" +
                    $"device_id: {deviceId}\n\n" +
                    $"Pos Offset (absolute):\n{value}\n\n" +
                    $"Rot Offset (absolute):\n{rotationValue}";
                rightControllerIpDisplay.SetText(text);
            }

            Debug.Log($"[Parsed params_update] device_num={deviceNum}, device_id={deviceId}, value={value}, rotation_value={rotationValue}");

            // 第一次收到時，記住原始 transform
            EnsureOriginalTransformCached();

            // value -> Vector3 -> position 絕對 offset
            if (!string.IsNullOrEmpty(value) &&
                TryParseVector3FromCommaString(value, out Vector3 posOffset))
            {
                _lastPosOffset = posOffset;
                ApplyWorldTranslationByAbsoluteOffset(posOffset);
            }

            // rotation_value -> Vector3 -> rotation 絕對 offset
            if (!string.IsNullOrEmpty(rotationValue) &&
                TryParseVector3FromCommaString(rotationValue, out Vector3 rotOffset))
            {
                _lastRotOffset = rotOffset;
                ApplyWorldRotationByAbsoluteOffset(rotOffset);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TcpClientExample] ParseParamsUpdateByString 失敗：" + e.Message);
        }
    }

    /// <summary>
    /// 從一個 JSON 物件字串中，用最簡單的字串方法取某個欄位的字串值。
    /// 例如 content: {"device_num":"49","value":"0,0,0"}
    /// ExtractJsonStringField(content, "value") -> "0,0,0"
    /// 若欄位不存在或為 null，回傳 null。
    /// </summary>
    private string ExtractJsonStringField(string content, string fieldName)
    {
        string key = "\"" + fieldName + "\"";
        int idx = content.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return null;

        int colon = content.IndexOf(':', idx + key.Length);
        if (colon < 0) return null;

        // 判斷是不是 null
        int nonSpace = colon + 1;
        while (nonSpace < content.Length && char.IsWhiteSpace(content[nonSpace]))
            nonSpace++;

        if (nonSpace + 3 < content.Length &&
            string.Compare(content, nonSpace, "null", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return null;
        }

        // 否則應該是 "xxx"
        int firstQuote = content.IndexOf('"', nonSpace);
        if (firstQuote < 0) return null;
        int secondQuote = content.IndexOf('"', firstQuote + 1);
        if (secondQuote < 0 || secondQuote <= firstQuote) return null;

        return content.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }

    /// <summary>
    /// 第一次被呼叫時，把當前 exhibitionRoot 的 position / rotation 記下來，
    /// 當作「世界的原始 transform」。
    /// </summary>
    private void EnsureOriginalTransformCached()
    {
        if (_hasOriginalTransform)
            return;

        if (exhibitionRoot == null)
        {
            Debug.LogWarning("[TcpClientExample] exhibitionRoot 未指定，無法快取原始 transform。");
            return;
        }

        _originalPosition = exhibitionRoot.position;
        _originalRotation = exhibitionRoot.rotation;
        _hasOriginalTransform = true;

        Debug.Log($"[EnsureOriginalTransformCached] originalPos={_originalPosition}, originalRot={_originalRotation.eulerAngles}");
    }

    /// <summary>
    /// 把 "0.4,0,0" 這樣的字串解析為 Vector3。
    /// （注意這裡不再支援中括號，純逗號分隔即可）
    /// </summary>
    private bool TryParseVector3FromCommaString(string value, out Vector3 result)
    {
        result = Vector3.zero;

        if (string.IsNullOrEmpty(value))
            return false;

        string trimmed = value.Trim();
        string[] parts = trimmed.Split(',');
        if (parts.Length != 3)
            return false;

        if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float z))
        {
            result = new Vector3(x, y, z);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 每次收到一個新的 position offset（絕對值）：
    /// 直接用 originalPosition + offset。
    /// </summary>
    private void ApplyWorldTranslationByAbsoluteOffset(Vector3 offset)
    {
        if (exhibitionRoot == null)
        {
            Debug.LogWarning("[TcpClientExample] exhibitionRoot 未指定，無法套用世界座標位移。");
            return;
        }

        EnsureOriginalTransformCached();
        if (!_hasOriginalTransform)
            return;

        Vector3 newPos = _originalPosition + offset;
        exhibitionRoot.position = newPos;

        Debug.Log($"[ApplyWorldTranslationByAbsoluteOffset] originalPos={_originalPosition}, offset={offset}, newPos={newPos}");
    }

    /// <summary>
    /// 每次收到一個新的 rotation offset（Euler, 絕對值）：
    /// 直接用 originalRotation * Quaternion.Euler(offset)。
    /// </summary>
    private void ApplyWorldRotationByAbsoluteOffset(Vector3 eulerOffset)
    {
        if (exhibitionRoot == null)
        {
            Debug.LogWarning("[TcpClientExample] exhibitionRoot 未指定，無法套用世界旋轉。");
            return;
        }

        EnsureOriginalTransformCached();
        if (!_hasOriginalTransform)
            return;

        Quaternion newRot = _originalRotation * Quaternion.Euler(eulerOffset);
        exhibitionRoot.rotation = newRot;

        Debug.Log($"[ApplyWorldRotationByAbsoluteOffset] originalRot={_originalRotation.eulerAngles}, offset={eulerOffset}, newRot={newRot.eulerAngles}");
    }

    // ===== JSON 對應資料結構（只給 welcome & type 用） =====

    [Serializable]
    private class MessageHeader
    {
        public string type;
    }

    [Serializable]
    private class WelcomeMessage
    {
        public string type;
        public string room_id;
        public string room_name;
        public string message;
    }

    // ===== 發送 / 關閉連線 =====

    public void SendMessageToServer(string msg)
    {
        if (!_isConnected || _stream == null || !_stream.CanWrite)
        {
            Debug.LogWarning("尚未連線或無法寫入，無法傳送訊息");
            return;
        }

        try
        {
            string messageWithNewLine = msg + "\n";
            byte[] data = Encoding.UTF8.GetBytes(messageWithNewLine);
            _stream.Write(data, 0, data.Length);
            Debug.Log("已發送訊息到 Server: " + msg);
        }
        catch (Exception e)
        {
            Debug.LogError("傳送訊息失敗: " + e.Message);
        }
    }

    private void OnApplicationQuit()
    {
        CloseConnection();
    }

    private void OnDestroy()
    {
        CloseConnection();
    }

    private void CloseConnection()
    {
        _isConnected = false;

        try
        {
            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }

            if (_client != null)
            {
                _client.Close();
                _client = null;
            }

            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Abort();
                _receiveThread = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("關閉連線時發生錯誤: " + e.Message);
        }
    }
}
