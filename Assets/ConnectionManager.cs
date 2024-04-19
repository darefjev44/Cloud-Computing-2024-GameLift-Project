using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Aws.GameLift.Server.Model;
using Aws.GameLift.Server;
using Aws.GameLift;
using System;
using Unity.Netcode.Transports.UTP;

#if UNITY_EDITOR //parrelsync is only available/required in the editor
using ParrelSync;
#endif

public class ConnectionManager : MonoBehaviour
{
    [SerializeField]
    private bool runEditorServer = false;

    void Start()
    {
#if UNITY_EDITOR
        if (ClonesManager.IsClone())
        {
            bool isClient = ClonesManager.GetArgument().Equals("client");
            if (isClient)
            {
                UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData("127.0.0.1", 7777);
                    NetworkManager.Singleton.StartClient();
                }
            }
        } else
        {
            if (runEditorServer)
            {
                NetworkManager.Singleton.StartServer();
            }
        }
#elif SERVER
        NetworkManager.Singleton.StartServer();
        GameLiftServer.Initialize();
#endif
    }
}


