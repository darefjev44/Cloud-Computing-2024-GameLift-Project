using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FPSPlayer : NetworkBehaviour
{
    private CharacterController characterController;

    public static Action<GameObject> OnDeath;

    
    private float lookSensitivity
    {
        get
        {
            return MenuController.Instance.Sensitivity;
        }
    }

    [Header("View Settings")]
    [SerializeField]
    [Tooltip("Child GameObject representing the player's view.")]
    public GameObject playerView;

    [Header("Movement Settings")]
    [SerializeField]
    [Tooltip("Movement speed of the player.")]
    public float moveSpeed = 7.5f;

    [SerializeField]
    [Tooltip("Jump speed of the player.")]
    public float jumpSpeed = 8.0f;

    [SerializeField]
    [Tooltip("Gravity effect on the player.")]
    public float gravity = 20.0f;

    private Vector3 moveDirection = Vector3.zero;

    [Header("Health")]
    [SerializeField]
    [Tooltip("The player's maximum health.")]
    public int maxHealth = 5;

    private NetworkVariable<int> health = new NetworkVariable<int>(5);

    public PlayerState playerState;

    [Header("Weapon Settings")]
    [SerializeField]
    [Tooltip("Weapon Damage")]
    public int weaponDamage = 1;

    private NetworkTransform networkTransform;
    public Camera playerCamera;

    public NetworkVariable<int> kills = new NetworkVariable<int>(0);
    public NetworkVariable<int> deaths = new NetworkVariable<int>(0);

    public override async void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        enabled = IsClient;
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        playerCamera.enabled = true;

        GameObject playerUi = EnvironmentManager.Instance.playerUi;
        playerUi.SetActive(true);

        characterController = GetComponent<CharacterController>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        StartCoroutine(SetNewPosition());
    }

    void Update()
    {
        if (IsOwner && playerState == PlayerState.Alive)
        {
            Move(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"), Input.GetButtonDown("Jump"));
            if (Input.GetButtonDown("Fire1"))
            {
                Shoot();
            }
            Pitch(Input.GetAxis("Mouse Y"));
            Yaw(Input.GetAxis("Mouse X"));
        }
    }

    public void Shoot()
    {
        if (!IsOwner) return;

        Vector3 rayOrigin = playerView.transform.position;
        RaycastHit hit;

        if (Physics.Raycast(rayOrigin, playerView.transform.forward, out hit, 50))
        {
            if (hit.collider.TryGetComponent(out FPSPlayer target))
            {
                ReportHitServerRpc(OwnerClientId, target.OwnerClientId, weaponDamage);
            }
        }
    }


    public void Move(float horizontal, float vertical, bool jump)
    {
        if (!characterController.isGrounded)
        {
            moveDirection.y -= gravity * Time.deltaTime; // apply gravity
        }

        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);

        float curSpeedH = moveSpeed * horizontal;
        float curSpeedV = moveSpeed * vertical;

        Vector3 combinedDirection = (forward * curSpeedV) + (right * curSpeedH);

        if (horizontal != 0 || vertical != 0)
        {
            combinedDirection = combinedDirection.normalized * moveSpeed;
        }

        moveDirection.x = combinedDirection.x;
        moveDirection.z = combinedDirection.z;

        if (jump && characterController.isGrounded)
        {
            moveDirection.y = jumpSpeed;
        }

        characterController.Move(moveDirection * Time.deltaTime);
    }

    public void Pitch(float amount)
    {
        if (!IsOwner) return;

        float pitch = playerView.transform.localEulerAngles.x - amount * lookSensitivity;
        if (pitch > 180) pitch -= 360;
        pitch = Mathf.Clamp(pitch, -89.0f, 89.0f);

        playerView.transform.localEulerAngles = new Vector3(pitch, 0, 0);
    }

    public void Yaw(float amount)
    {
        if (!IsOwner) return;

        transform.rotation *= Quaternion.Euler(0, amount * lookSensitivity, 0);
    }

    public void TakeDamage(ulong sourcePlayerId, int damage)
    {
        if (!IsServer) return;

        health.Value -= damage;
        Debug.Log(damage + " damage taken. Health: " + health.Value);

        if (health.Value <= 0)
        {
            health.Value = 0;

            HandleDeath(sourcePlayerId);
        }
    }

    IEnumerator SetNewPosition()
    {
        yield return null;

        this.transform.position = EnvironmentManager.Instance.GetRandomSpawnPosition();
        playerView.transform.rotation = EnvironmentManager.Instance.GetRandomSpawnRotation();

        Physics.SyncTransforms();
    }

    private void HandleDeath(ulong sourcePlayerId)
    {
        EnvironmentManager.Instance.ReportDeath(sourcePlayerId, OwnerClientId);

        health.Value = maxHealth;

        ResetPositionClientRpc();
    }

    [ClientRpc]
    public void ResetPositionClientRpc()
    {
        StartCoroutine(SetNewPosition());
    }

    [ServerRpc]
    public void ReportHitServerRpc(ulong sourcePlayerId, ulong targetPlayerId, int damage, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        var targetPlayerObject = NetworkManager.Singleton.ConnectedClients[targetPlayerId].PlayerObject;
        var targetPlayer = targetPlayerObject.GetComponent<FPSPlayer>();
        targetPlayer.TakeDamage(sourcePlayerId, damage);
    }
}

public enum PlayerState
{
    Alive,
    Dead
}

