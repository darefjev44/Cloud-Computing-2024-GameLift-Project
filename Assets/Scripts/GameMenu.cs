using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuController : MonoBehaviour
{
    public Slider sensSlider;
    public TMP_InputField sensField;

    public Button disconnectButton;

    public GameObject menu;

    public static MenuController Instance { get; private set; }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        menu.SetActive(false); //hide the menu by default
    }

    private void Update()
    {
        //listen for escape key to toggle the menu
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            menu.SetActive(!menu.activeSelf);

            if (menu.activeSelf)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    public float Sensitivity
    {
        get
        {
            return sensSlider.value;
        }
    }

    void Start()
    {
        sensSlider.onValueChanged.AddListener(OnSensSliderChange);
        sensField.onEndEdit.AddListener(OnSensFieldChange);
        disconnectButton.onClick.AddListener(OnDisconnectClick);
    }

    void OnSensSliderChange(float value)
    {
        sensField.text = value.ToString();
    }

    void OnSensFieldChange(string value)
    {
        if (float.TryParse(value, out float result))
        {
            sensSlider.value = result;
        }
    }

    void OnDisconnectClick()
    {
        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene("Menu");
    }
}
