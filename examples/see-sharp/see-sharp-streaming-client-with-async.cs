/*
 * C# example for interacting with the streaming server, using truly asynchronous IO.
 * This code then:
 * 1., Sends the request to the server
 * 2., Receives and parses the packet asynchronously as they come in
 * 3., Displays the parsed packet contents asynchronously as per a timer
 */

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;
using System.Timers;
using System.Net;
using System.Globalization;




public class StreamExampleWithAsync
{

    /*
     * Test scenario:
     * The server is running on 192.168.42.79, and is configured to have a rigid body (id: 2501, 'Rotator'), and operates at 200 samples per second.
     * The decimation is with respect to the framerate, so a decimation of 2 would mean (200 Hz / 2) = 100 packets per second.
     */

    static string server_ip = "192.168.42.79";
    static int server_port = 64923;

    static uint rigid_body_id_we_want = 2501;
    static uint decimation_we_want = 500; // Decimation to be a bit lower, but nothing too fancy
    static int listening_on_port = 6548; // This should be randomised


    static UdpClient udp_client = new UdpClient(listening_on_port);

    // Timer stuff
    private static System.Timers.Timer event_timer;


    // This will be written to using receive_message()
    static string received_payload_as_string;

    static void Main()
    {

        // Configure the timer
        event_timer = new System.Timers.Timer(1000);
        event_timer.Elapsed += display_function;
        // Enable the timer.
        event_timer.Start();

        send_request();

        // Create the asynchronous call to receive_message()
        udp_client.BeginReceive(new AsyncCallback(receive_message), udp_client);

        Console.WriteLine("Packet sent, now waiting for the response... Press Ctrl+c in this window to terminate.");


        // Infinite loop in the main, so everything is event-driven.
        while (true);
    }


    static void send_request()
    {
        string request_to_send = string.Format("{0};{1};{2}", rigid_body_id_we_want, listening_on_port, decimation_we_want);

        Console.WriteLine("Sending request to {0}:{1} payload: {2}", server_ip, server_port, request_to_send);

        Byte[] payload_to_send = Encoding.ASCII.GetBytes(request_to_send);

        try
        {
            udp_client.Send(payload_to_send, payload_to_send.Length, server_ip, server_port);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: Could not send the packet. See what the system says below.");
            Console.WriteLine(e);
            return;
        }


    }


    // This function is executed every time the timer is finished.
    private static void display_function(Object source, ElapsedEventArgs e)
    {
        string[] separated_string = received_payload_as_string.Split(";");

        if (separated_string.Length != 5)
        {
            Console.WriteLine("The number of fields when parsing the payload is not 5. Cannot continue.");
            return;
        }

        // The name contains a new line, let's remove that
        string rigid_body_name_string = separated_string[4].Replace("\n", string.Empty);

        ulong unix_time_in_milliseconds = ulong.Parse(separated_string[0]);

        uint rigid_body_id_extracted_from_separated_string = uint.Parse(separated_string[1]);

        if (rigid_body_id_extracted_from_separated_string == rigid_body_id_we_want)
        {
            // If we have the correct one, then:

            // Extract the translation coordinates
            string[] translation_as_string = separated_string[2].Split(",");
            if (translation_as_string.Length != 3)
            {
                Console.WriteLine("Something is wrong with the formatting of the translation coordinates, could not split it into numbers.");
                return;
            }

            if (!float.TryParse(translation_as_string[0], new CultureInfo("en-US"), out float translation_x))
            {
                translation_x = float.NaN;
            }

            if (!float.TryParse(translation_as_string[1], new CultureInfo("en-US"), out float translation_y))
            {
                translation_y = float.NaN;
            }

            if (!float.TryParse(translation_as_string[2], new CultureInfo("en-US"), out float translation_z))
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


            if (!float.TryParse(quaternion_as_string[0], new CultureInfo("en-US"), out float quaternion_qx))
            {
                quaternion_qx = float.NaN;
            }

            if (!float.TryParse(quaternion_as_string[1], new CultureInfo("en-US"), out float quaternion_qy))
            {
                quaternion_qy = float.NaN;
            }

            if (!float.TryParse(quaternion_as_string[2], new CultureInfo("en-US"), out float quaternion_qz))
            {
                quaternion_qz = float.NaN;
            }

            if (!float.TryParse(quaternion_as_string[3], new CultureInfo("en-US"), out float quaternion_qw))
            {
                quaternion_qz = float.NaN;
            }



            // Print the output:
            Console.WriteLine("Processed rigid body {0} at timestamp {1}. ID: {2}\n\tXYZ: {3}, {4}, {5} - QxQyQzQw: {6},{7},{8},{9}\n\n\n", rigid_body_name_string, unix_time_in_milliseconds, rigid_body_id_extracted_from_separated_string,
                        translation_x, translation_y, translation_z,
                        quaternion_qx, quaternion_qy, quaternion_qz, quaternion_qw);

        }
    }

    // This function gets executed when there is a packet in the buffer.
    // Original code: https://yal.cc/cs-dotnet-asynchronous-udp-example/
    static void receive_message(IAsyncResult result)
    {
        UdpClient socket = result.AsyncState as UdpClient; // set the client to be asynchronous maybe?

        IPEndPoint ip_endpoint = new IPEndPoint(IPAddress.Parse(server_ip), listening_on_port); // Accept packets only from the server.

        // Apparently there is a bug in Unity, see, from 2010. Hopefully fixed. :)
        // https://discussions.unity.com/t/udp-receive-problems-v2-beginreceive-and-endreceive/410064


        byte[] received_payload = socket.EndReceive(result, ref ip_endpoint);

        received_payload_as_string = Encoding.ASCII.GetString(received_payload);

        // Once the transfer is done, restart the receive process again.
        socket.BeginReceive(new AsyncCallback(receive_message), socket);





    }


}