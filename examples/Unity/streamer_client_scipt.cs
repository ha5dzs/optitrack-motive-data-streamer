using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.Net;
using System;
using System.Text;
using System.Net.Cache;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Numerics;
using JetBrains.Annotations;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;

public class streamer_client_scipt : MonoBehaviour
{

    /*
     * Public variables.
     * These can be adjusted from the Unity GUI as well.
     */
    public string streamerServerAddress = "192.168.42.5";
    public int streamerServerPort = 64923;
    public int listeningPort = 54622;
    public bool useRandomListeningPortForEachInstanceOfThisScript = true;
    public int decimation = 3;
    public bool swapYandZCoordinates = false;
    public int rigidBodyIDYouWantToTrack = 2510;
    public bool useRigidBodyNameAsGameObjectName = false;

    // Not sure what is the difference between Unity's random number generator and the Microsoft-provided random number generator.
    private System.Random rng = new System.Random();
    private bool game_object_name_updated = false; //so we don't have to rename the game object every frame.

    UdpClient udp_client = new UdpClient();

    // Start is called before the first frame update
    void Start()
    {

        // set culture info. This is required for the decimal formatting.
        Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
        Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

        if (useRandomListeningPortForEachInstanceOfThisScript)
        {
            // If we got here, assign a new random number for the listening port.
            listeningPort = rng.Next(listeningPort, listeningPort+200);
        }

        udp_client = new UdpClient(listeningPort); // Change where we listen
        //udp_client.Client.ReceiveTimeout = 5000;


        /*
         * Send the request to the server
         */

        // <rigid_body_id_in_motive>;<udp_port_to_stream_to>;<decimation>
        string request_to_send = string.Format("{0};{1};{2}", rigidBodyIDYouWantToTrack, listeningPort, decimation);
        //Debug.Log("Sending request to {0}:{1} payload: {2}", streamerServerAddress, streamerServerPort, request_to_send);

        Byte[] payload_to_send = Encoding.ASCII.GetBytes(request_to_send);

        // No try-catch here, I want this to fail
        udp_client.Send(payload_to_send, payload_to_send.Length, streamerServerAddress, streamerServerPort);

    }

    // Update is called once per frame
    void Update()
    {
        /*
         * Receive data
         */

        // I am not sure if I have a choice here, not sure how much will this hit performance.
        IPEndPoint ip_endpoint = new IPEndPoint(IPAddress.Parse(streamerServerAddress), listeningPort);

        // I should add a timeout exception handling here, because it can totally stall the render loop.
        Byte[] received_payload = udp_client.Receive(ref ip_endpoint);

        string received_payload_as_string = Encoding.ASCII.GetString(received_payload);

        string[] separated_string = received_payload_as_string.Split(";");

        if (separated_string.Length != 5)
        {
            Debug.LogError("The number of fields when parsing the payload is not 5. Cannot continue.");
            return;
        }

        string rigid_body_name_string = separated_string[4].Replace("\n", string.Empty);

        uint rigid_body_id_extracted_from_separated_string = uint.Parse(separated_string[1]);

        if (rigid_body_id_extracted_from_separated_string == rigidBodyIDYouWantToTrack)
        {
            // If we have the correct one, then:

            // Extract the translation coordinates
            string[] translation_as_string = separated_string[2].Split(",");
            if (translation_as_string.Length != 3)
            {
                Console.WriteLine("Something is wrong with the formatting of the translation coordinates, could not split it into numbers.");
                return;
            }

            if (!float.TryParse(translation_as_string[0], out float translation_x))
            {
                translation_x = float.NaN;
            }

            if (!float.TryParse(translation_as_string[1], out float translation_y))
            {
                translation_y = float.NaN;
            }

            if (!float.TryParse(translation_as_string[2], out float translation_z))
            {
                translation_z = float.NaN;
            }


            // Extract the orientation
            string[] quaternion_as_string = separated_string[3].Split(",");
            if (quaternion_as_string.Length != 4)
            {
                Console.WriteLine("Something is wrong with the formatting of the quaternion, could not split it into numbers.");
                return;
            }


            if (!float.TryParse(quaternion_as_string[0], out float quaternion_qx))
            {
                quaternion_qx = float.NaN;
            }

            if (!float.TryParse(quaternion_as_string[1], out float quaternion_qy))
            {
                quaternion_qy = float.NaN;
            }

            if (!float.TryParse(quaternion_as_string[2], out float quaternion_qz))
            {
                quaternion_qz = float.NaN;
            }

            if (!float.TryParse(quaternion_as_string[3], out float quaternion_qw))
            {
                quaternion_qz = float.NaN;
            }

            if (!game_object_name_updated)
            {
                game_object_name_updated = true; // so we only get to this path here only once.

                // If required, update the game object name
                if (useRigidBodyNameAsGameObjectName)
                {
                    if (separated_string[4].Length != 0)
                    {
                        // If we got here, we have a rigid body name. Remove the \n bit
                        transform.name = separated_string[3].Replace("\n", String.Empty);

                    }
                    else
                    {
                        // If we got here, the name was not valid.
                        transform.name = "Unknown rigid body!";
                    }
                }
            }

            // Update the parent object's coordinates.
            if(swapYandZCoordinates)
            {
                // If we got here, we are swapping Y and Z.
                transform.position = new UnityEngine.Vector3(translation_x, translation_z, translation_y);
            }
            else
            {
                transform.position = new UnityEngine.Vector3(translation_x, translation_y, translation_z);
            }
            
            transform.rotation = new UnityEngine.Quaternion(quaternion_qx, quaternion_qy, quaternion_qz, quaternion_qw);

        }



    }

    /*
     * This function closes the streaming, when the game object is being destroyed.
     */
    void OnDestroy()
    {

        // Effectively, this is the same as when we start the stream, but we set the decimation to 0.

        // <rigid_body_id_in_motive>;<udp_port_to_stream_to>;<decimation>
        string request_to_send = string.Format("{0};{1};0", rigidBodyIDYouWantToTrack, listeningPort);
        //Debug.Log("Sending request to {0}:{1} payload: {2}", streamerServerAddress, streamerServerPort, request_to_send);

        Byte[] payload_to_send = Encoding.ASCII.GetBytes(request_to_send);

        // No try-catch here, I want this to fail
        udp_client.Send(payload_to_send, payload_to_send.Length, streamerServerAddress, streamerServerPort);


    }

    /*
     * This function calls OnDestroy(), when the application quits, to tell the server to stop streaming data.
     */

    void OnApplicationQuit()
    {
        // See above what this does.
        OnDestroy();
        
    }

}
