using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using TMPro;
using System.Text;
using Unity.Netcode.Transports.UTP;

public class EnvironmentManager : NetworkBehaviour
{
    [Header("Spawning Related")]
    public GameObject spawnTransformsParent;
    private List<Transform> spawnTransforms;
    public GameObject playerPrefab;

    public TextMeshProUGUI scoreUi;
    public TextMeshProUGUI hostUi;
    public TextMeshProUGUI playerCountUi;
    public TextMeshProUGUI pingUi;

    public static EnvironmentManager Instance { get; private set; }

    public GameObject playerUi;

    private UnityTransport unityTransport;

    private Dictionary<ulong, string> playerSessionIds = new Dictionary<ulong, string>();

    private Dictionary<ulong, float> clientHeartbeats = new Dictionary<ulong, float>();

    private float heartbeatInterval = 5f; //check on clients every x seconds
    private float clientTimeout = 30f; //disconnect client after x seconds of inactivity

    private float serverTimeout = 300f; //kill server after x minutes of inactivity
    private float lastActivityTime;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
        }
    }

    void Start()
    {
        spawnTransforms = new List<Transform>(spawnTransformsParent.GetComponentsInChildren<Transform>().Skip(1));
        unityTransport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (IsServer)
        {
            Debug.Log("Is a server - registered client connection callbacks.");
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

            lastActivityTime = Time.time;

            //using this instead of OnClientDisconnected https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/issues/2064
            StartCoroutine(Heartbeat());
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton && IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    void Update()
    {
        if (!IsServer)
        {
            //check if we are connected to a server
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                var serverIp = unityTransport?.ConnectionData.Address;
                RefreshHostUi($"Host: {serverIp}");
                RefreshPingUi($"Ping: {NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(OwnerClientId)}ms");
            }
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        lastActivityTime = Time.time;
        clientHeartbeats.Add(clientId, Time.time);

        Debug.Log("Client connected: " + clientId);
        GameObject player = Instantiate(playerPrefab);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

        UpdateScoreboard();
        UpdatePlayers();

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { clientId }
            }
        };

        RequestPlayerSessionIdClientRpc(clientRpcParams);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        UpdateScoreboard();
        UpdatePlayers();
    }

    public Vector3 GetRandomSpawnPosition()
    {
        Vector3 position = spawnTransforms[Random.Range(0, spawnTransforms.Count)].position;
        position.y += playerPrefab.transform.localScale.y / 2;

        return position;
    }

    public Quaternion GetRandomSpawnRotation()
    {
        return Quaternion.Euler(0, Random.Range(0, 360), 0);
    }

    public void ReportDeath(ulong sourcePlayerId, ulong deadPlayerId)
    {
        if (!IsServer) return;

        //add kill/death count
        var sourcePlayer = NetworkManager.Singleton.ConnectedClients[sourcePlayerId].PlayerObject;
        var sourcePlayerFpsPlayer = sourcePlayer.GetComponent<FPSPlayer>();
        sourcePlayerFpsPlayer.kills.Value++;

        var deadPlayer = NetworkManager.Singleton.ConnectedClients[deadPlayerId].PlayerObject;
        var deadPlayerFpsPlayer = deadPlayer.GetComponent<FPSPlayer>();
        deadPlayerFpsPlayer.deaths.Value++;

        UpdateScoreboard();
    }

    public void UpdateScoreboard()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Scoreboard");

        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            var player = client.Value.PlayerObject.GetComponent<FPSPlayer>();
            sb.AppendLine($"Player {client.Key}: Kills: {player.kills.Value} Deaths: {player.deaths.Value}");
        }

        RefreshScoreboardClientRpc(sb.ToString());
    }

    private void UpdatePlayers()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Players: " + NetworkManager.Singleton.ConnectedClients.Count.ToString());

        RefreshPlayersClientRpc(sb.ToString());
    }

    [ClientRpc]
    public void RefreshScoreboardClientRpc(string text)
    {
        scoreUi.text = text;
    }

    [ClientRpc]
    public void RefreshPlayersClientRpc(string text)
    {
        playerCountUi.text = text;
    }

    public void RefreshHostUi(string text)
    {
        hostUi.text = text;
    }

    public void RefreshPingUi(string text)
    {
        pingUi.text = text;
    }

    [ClientRpc]
    public void RequestPlayerSessionIdClientRpc(ClientRpcParams rpcParams)
    {
        Debug.Log($"Sending GL session ID {GameLiftClient.GetPlayerSessionId()}.");
        SendPlayerSessionIdServerRpc(GameLiftClient.GetPlayerSessionId());
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendPlayerSessionIdServerRpc(string sessionId, ServerRpcParams rpcParams = default)
    {
        var clientId = rpcParams.Receive.SenderClientId;

        if (!string.IsNullOrEmpty(sessionId))
        {
            playerSessionIds.Add(clientId, sessionId);
            Debug.Log($"Received session ID {sessionId} from client {clientId}");

            GameLiftServer.ActivatePlayerSession(sessionId);
        }
    }

    [ClientRpc]
    private void HeartbeatClientRpc()
    {
        if(!IsServer) {
            HeartbeatServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void HeartbeatServerRpc(ServerRpcParams rpcParams = default)
    {
        var clientId = rpcParams.Receive.SenderClientId;

        if(IsServer && clientHeartbeats.ContainsKey(clientId)){
            Debug.Log("Received heartbeat from client: " + clientId + " at time: " + Time.time);
            clientHeartbeats[clientId] = Time.time;
            lastActivityTime = Time.time;
        }
    }

    private IEnumerator Heartbeat()
    {
        while (true)
        {
            yield return new WaitForSeconds(heartbeatInterval);

            var disconnectedClients = new List<ulong>();

            HeartbeatClientRpc();

            foreach (var client in clientHeartbeats)
            {
                if (Time.time - client.Value > clientTimeout)
                {
                    disconnectedClients.Add(client.Key);
                }
            }

            foreach (var clientId in disconnectedClients)
            {
                Debug.Log($"Client {clientId} has disconnected due to inactivity.");
                clientHeartbeats.Remove(clientId);

                Debug.Log("Client disconnected: " + clientId);
                if (playerSessionIds.ContainsKey(clientId))
                {
                    GameLiftServer.TerminatePlayerSession(playerSessionIds[clientId]);
                    playerSessionIds.Remove(clientId);
                }

                NetworkManager.Singleton.DisconnectClient(clientId);
            }

            if (Time.time - lastActivityTime > serverTimeout)
            {
                Debug.Log("Server has been inactive for too long. Shutting down.");
                GameLiftServer.TerminateGameSession();

                //i don't think this will be reached as Application.Quit() is called in TerminateGameSession() but just in case
                Debug.Log("Forcibly shutting down server.");
                Application.Quit();
            }
        }
    }
}
