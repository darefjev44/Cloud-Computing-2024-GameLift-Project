using System;
using System.Collections;
using System.Collections.Generic;
using AmazonGameLift.Runtime;
using Aws.GameLift;
using Aws.GameLift.Server;
using Aws.GameLift.Server.Model;
using Unity.Netcode;
using UnityEngine;

public static class GameLiftServer
{
    public static void Initialize()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            CreateServer();
        } else
        {
            Debug.Log("Did not initialize server. Not running as server or NetworkManager instance not found.");
        }
    }

    private static void CreateServer()
    {
        //init sdk
        var initSDKOutcome = GameLiftServerAPI.InitSDK();
        int port = UnityEngine.Random.Range(7000, 8000);

        //once sdk is initialized, inform gamelift that the server is ready to host game sessions
        if (initSDKOutcome.Success)
        {
            ProcessParameters processParameters = new ProcessParameters(
               OnGameSession,
               OnGameSessionUpdate,
               OnProcessTerminate,
               OnHealthCheck,
               port,
               new LogParameters(new List<string>() { "/local/game/server.log" }
            ));

            var processReadyOutcome = GameLiftServerAPI.ProcessReady(processParameters);
            if (processReadyOutcome.Success)
            {
                Debug.Log("Server is ready to host game sessions.");
            }
            else
            {
                Debug.Log("Failed to prepare the server:  " + processReadyOutcome.Error.ToString());
            }
        }
        else
        {
            Debug.Log("Failed to initialize GameLift SDK: " + initSDKOutcome.Error.ToString());
        }
    }

    static void OnGameSession(GameSession gameSession)
    {
        // when a game session is requested, check if server is ready and activate the game session

        if (NetworkManager.Singleton.IsServer)
        {
            GameLiftServerAPI.ActivateGameSession();
        } else
        {
            Debug.Log("Failed to activate game session. Server is not running.");
        }
        
    }

    static void OnProcessTerminate()
    {
        Debug.Log("Recieved termination request from GameLift.");

        var outcome = GameLiftServerAPI.ProcessEnding();
        if (outcome.Success)
        {
            Debug.Log("Termination successful.");
            Application.Quit();
        }
        else
        {
            Debug.Log("Termination failed: " + outcome.Error.ToString());
        }
    }

    static void OnGameSessionUpdate(UpdateGameSession updateGameSession)
    {
        // called when a game session is updated, not utilized here
    }

    static bool OnHealthCheck()
    {
        // GameLift calls this regularly to check if the server process is healthy
        // don't really have any metrics to use so simply checking if server is running

        if(NetworkManager.Singleton.IsServer)
        {
            return true;
        } 
        else
        {
            return false;
        }
    }

    static public void ActivatePlayerSession(string playerSessionId)
    {
        var outcome = GameLiftServerAPI.AcceptPlayerSession(playerSessionId);
        if (outcome.Success)
        {
            Debug.Log("Player session activated: " + playerSessionId);
        }
        else
        {
            Debug.Log("Failed to activate player session: " + outcome.Error.ToString());
        }
    }

    static public void TerminatePlayerSession(string playerSessionId)
    {
        var outcome = GameLiftServerAPI.RemovePlayerSession(playerSessionId);
        if (outcome.Success)
        {
            Debug.Log("Player session terminated: " + playerSessionId);
        }
        else
        {
            Debug.Log("Failed to terminate player session: " + outcome.Error.ToString());
        }
    }

    static public void TerminateGameSession()
    {
        var outcome = GameLiftServerAPI.ProcessEnding();
        if (outcome.Success)
        {
            Debug.Log("Succesfully terminated the game session. Shutting down the server.");
            Application.Quit();
        }
        else
        {
            Debug.Log("Failed to terminate the game session: " + outcome.Error.ToString());

            //if termination fails, try to end the process anyway
            Debug.Log("Forcibly shutting down server.");
            Application.Quit();
        }
    }
}