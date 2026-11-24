using UnityEngine;

public class AdbIntentTest : MonoBehaviour
{
    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var extrasClass = new AndroidJavaClass("com.yourcompany.androidplugin.MyIntentExtras"))
        {
            // 讀 adb -es qqquest_server_ip 192.168.0.10
            string serverIp = extrasClass.CallStatic<string>(
                "getStringExtra",
                "qqquest_server_ip"
            );

            // 讀 adb -ei qqquest_server_port 5555
            int serverPort = extrasClass.CallStatic<int>(
                "getIntExtra",
                "qqquest_server_port",
                0  // defaultValue，取不到就會是 0
            );

            Debug.Log($"ADB Extra qqquest_server_ip = {serverIp}");
            Debug.Log($"ADB Extra qqquest_server_port = {serverPort}");

            // 之後你要直接用來連線就可以這樣：
            // MyTcpClient.Connect(serverIp, serverPort);
        }
#else
        Debug.Log("在 Editor 裡讀不到 ADB 的 Intent Extra，只有 Android 裝置實機才有喔");
#endif
    }
}