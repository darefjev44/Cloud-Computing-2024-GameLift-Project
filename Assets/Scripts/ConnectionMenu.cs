using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ConnectionMenu : MonoBehaviour
{
    public TMP_InputField hostField;
    public Button hostButton;
    public Button quickButton;

    public static ConnectionMenu Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); //make sure it persists between scenes so connection data is not lost
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        hostButton.onClick.AddListener(OnHostClick);
        quickButton.onClick.AddListener(OnQuickClick);
    }

    void OnHostClick()
    {
        string host = string.IsNullOrEmpty(hostField.text) ? "127.0.0.1" : hostField.text;
        StartCoroutine(LoadGameSceneAndConnect(host));
    }

    IEnumerator LoadGameSceneAndConnect(string host)
    {
        hostButton.onClick.RemoveListener(OnHostClick);
        quickButton.onClick.RemoveListener(OnQuickClick);

        //load asynchronously
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync("Main");

        //wait until scene is loaded
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        Debug.Log("Scene loaded successfully.");

        //make sure NetworkManager is available
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("NetworkManager is not available in the loaded scene.");
            yield break;
        }

        //once scene is loaded, set connection data and start as a client
        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(host, 7777);
            NetworkManager.Singleton.StartClient(); 
        }
        else
        {
            Debug.LogError("UnityTransport component not found on NetworkManager.");
        }
    }


    void OnQuickClick()
    {
        GameLiftClient.Initialize();
        
        string ipAddress = "";
        ushort port = 0;

        GameLiftClient.GetConnectionInfo(ref ipAddress, ref port);

        if (!string.IsNullOrEmpty(ipAddress) && port != 0)
        {
            StartCoroutine(LoadGameSceneAndConnect(ipAddress));
        }
    }
}
