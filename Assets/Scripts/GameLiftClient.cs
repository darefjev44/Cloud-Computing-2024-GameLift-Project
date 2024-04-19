using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.GameLift;
using Amazon.GameLift.Model;
using Aws.GameLift.Server.Model;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public static class GameLiftClient
{
    private static AmazonGameLiftClient _client;
    private static string _playerId = System.Guid.NewGuid().ToString();

    //these should be stored securely, for demonstration purposes they are hardcoded here
    //for games where player authentication doesn't matter (e.g for storing player data) a single IAM user can be used
    //for other purposes, can use AWS Cognito or other authentication services
    //the credentials used have been revoked and are not valid, but are removed to not get spammed with annoying github scraping bots
    private static string _accessKey = "access key for AWS IAM **player** user";
    private static string _secretKey = "secret key for AWS IAM **player** user";

    private static string _aliasId = "the fleet's GameLift alias ID";

    private static string _sessionId = null;

    private static int _maxRetries = 5;

    private static int _acceptableLatency = 100;
    private static string _bestRegion = "us-east-1"; //default region
    private static List<string> _acceptableRegions;

    public static void Initialize()
    {
        _client = new AmazonGameLiftClient(_accessKey, _secretKey, new AmazonGameLiftConfig { RegionEndpoint = Amazon.RegionEndpoint.USEast1 });
    }

    public static void GetConnectionInfo(ref string ipAddress, ref ushort port)
    {

        try
        {
            string bestRegion = GetBestRegion();
            if(bestRegion != null)
            {
                _bestRegion = bestRegion;
                Debug.Log("Best region determined: " + _bestRegion);
            } else
            {
                Debug.Log($"Failed to determine best region, using default region. ({_bestRegion})");
            }

            //try to find an existing game session with players
            foreach(string region in _acceptableRegions)
            {
                for (int retry = _maxRetries; retry > 0; retry--)
                {
                    Amazon.GameLift.Model.GameSession gameSession = FindGameSession(region, 1); //minimum 1 player
                    Debug.Log($"Searching for game session with players in {region}, retries remaining: {retry}");
                    if (gameSession != null)
                    {
                        Debug.Log($"Game session found in region {region}, game session ID: {gameSession.GameSessionId}");
                        Amazon.GameLift.Model.PlayerSession playerSession = CreatePlayerSession(gameSession);

                        if (playerSession != null)
                        {
                            ipAddress = playerSession.IpAddress;
                            port = (ushort)playerSession.Port;
                            _sessionId = playerSession.PlayerSessionId;

                            Debug.Log($"Connection info retrieved: {ipAddress}, {port}");
                            return;
                        }

                        // found game session but player session creation failed, restart search
                        retry = _maxRetries;
                    }
                }
            }
            


            //try to find an existing game session in best region
            for (int retry = _maxRetries; retry > 0; retry--)
            {
                Amazon.GameLift.Model.GameSession gameSession = FindGameSession();
                Debug.Log("Searching for game session, retries remaining: " + retry);
                if (gameSession != null)
                {
                    Debug.Log("Game session found, game session ID: " + gameSession.GameSessionId);
                    Amazon.GameLift.Model.PlayerSession playerSession = CreatePlayerSession(gameSession);

                    if (playerSession != null)
                    {
                        ipAddress = playerSession.IpAddress;
                        port = (ushort)playerSession.Port;
                        _sessionId = playerSession.PlayerSessionId;

                        Debug.Log($"Connection info retrieved: {ipAddress}, {port}");
                        return;
                    }

                    // found game session but player session creation failed, restart search
                    retry = _maxRetries;
                }
            }

            //if no game session found, create a new one in best region
            for (int retry = _maxRetries; retry > 0; retry--)
            {
                Amazon.GameLift.Model.GameSession gameSession = CreateGameSession();
                Debug.Log("Attempting to create a new game session, retries remaining: " + retry);
                if (gameSession != null)
                {
                    Debug.Log("Game session created, game session ID: " + gameSession.GameSessionId);
                    Amazon.GameLift.Model.PlayerSession playerSession = CreatePlayerSession(gameSession);

                    if (playerSession != null)
                    {
                        ipAddress = playerSession.IpAddress;
                        port = (ushort)playerSession.Port;
                        _sessionId = playerSession.PlayerSessionId;

                        Debug.Log($"Connection info retrieved: {ipAddress}, {port}");
                        return;
                    }

                    // got a game session but player session creation failed, restart search
                    retry = _maxRetries;
                }
            }
        }
        catch (Exception e)
        {
            Debug.Log("Error retrieving connection info: " + e.Message);
        }
    }

    private static Amazon.GameLift.Model.PlayerSession CreatePlayerSession(Amazon.GameLift.Model.GameSession gameSession)
    {
        var request = new CreatePlayerSessionRequest
        {
            GameSessionId = gameSession.GameSessionId,
            PlayerId = _playerId
        };
        var response = _client.CreatePlayerSession(request);

        return response.PlayerSession;
    }

    private static Amazon.GameLift.Model.GameSession FindGameSession()
    {
        var request = new SearchGameSessionsRequest
        {
            AliasId = _aliasId,
            FilterExpression = "hasAvailablePlayerSessions=true",
            Location = _bestRegion
        };
        var response = _client.SearchGameSessions(request);

        if (response.GameSessions.Count > 0)
        {
            return response.GameSessions[0];
        }

        return null;
    }

    private static Amazon.GameLift.Model.GameSession FindGameSession(string region, int minPlayers)
    {
        var request = new SearchGameSessionsRequest
        {
            AliasId = _aliasId,
            FilterExpression = $"hasAvailablePlayerSessions=true AND playerSessionCount>={minPlayers}",
            Location = region,
        };
        var response = _client.SearchGameSessions(request);

        if (response.GameSessions.Count > 0)
        {
            return response.GameSessions[0];
        }

        return null;
    }

    private static Amazon.GameLift.Model.GameSession CreateGameSession()
    {
        var request = new CreateGameSessionRequest
        {
            MaximumPlayerSessionCount = 10,
            AliasId = _aliasId,
            Location = _bestRegion
        };
        var response = _client.CreateGameSession(request);
        
        return response.GameSession;
    }

    public static string GetPlayerSessionId()
    {
        return _sessionId;
    }

    private static string GetBestRegion()
    {
        try
        {
            string fleetId = ResolveFleetAlias(_aliasId);

            if(!string.IsNullOrEmpty(fleetId))
            {
                List<string> regions = GetFleetRegions(fleetId);
                Dictionary<string, long> regionLatencies = new Dictionary<string, long>();

                if(regions.Count > 0)
                {
                    foreach (var region in regions)
                    {
                        regionLatencies.Add(region, GetLatencyToRegion(region));
                    }

                    _acceptableRegions = regionLatencies.Where(x => x.Value <= _acceptableLatency).OrderBy(x => x.Value).Select(x => x.Key).ToList();

                    return regionLatencies.OrderBy(x => x.Value).First().Key;
                } else
                {
                    Debug.Log("No regions found for fleet.");
                }
            } else
            {
                Debug.Log("Failed to resolve fleet alias.");
            }
        }
        catch (Exception e)
        {
            Debug.Log("Error retrieving connection info: " + e.Message);
        }

        return null;
    }

    //uses gamelift:ResolveAlias
    private static string ResolveFleetAlias(string aliasId)
    {
        var request = new ResolveAliasRequest
        {
            AliasId = aliasId
        };
        var response = _client.ResolveAlias(request);

        return response.FleetId;
    }

    //gamelift:ListCompute
    private static List<string> GetFleetRegions(string fleetId)
    {
        var request = new ListComputeRequest
        {
            FleetId = fleetId
        };

        var response = _client.ListCompute(request);

        List<string> regions = new List<string>();
        foreach (var compute in response.ComputeList)
        {
            if(!regions.Contains(compute.Location))
            {
                regions.Add(compute.Location);
            }
        }

        return regions;
    }

    public static long GetLatencyToRegion(string region)
    {
        string endpoint = $"gamelift.{region}.amazonaws.com";

        var ping = new System.Net.NetworkInformation.Ping();

        var pingReply = ping.Send(endpoint);

        Debug.Log("Ping to " + region + " endpoint: " + pingReply.RoundtripTime + "ms");

        return pingReply.RoundtripTime;
    }
}
