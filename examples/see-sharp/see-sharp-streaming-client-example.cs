/*
 * C# example for the streaming client.
 * Simple stuff:
 * 1., Send a request UDP packet to the server
 * 2., Time the loop slowly so packets can pile up
 * 3., Parse said packet
 * 4., Display parsed data
 * 5., Go back to step 2
 * This can be used to set a decimation when packet overflow occurs, and maybe change the decimation accordingly.
 *
 * (all global code, no objects, no overheads, no nothing.)
 */

using System.Net.Sockets;
using System.Net;
using System;
using System.Text;
using System.Net.Cache;
using System.Globalization;
using System.Threading;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Timers;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;


/*
 * Test scenario:
 * The server is running on 192.168.42.79, and is configured to have a rigid body (id: 2501, 'Rotator'), and operates at 200 samples per second.
 * The decimation is with respect to the framerate, so a decimation of 2 would mean (200 Hz / 2) = 100 packets per second.
 */

string server_ip = "192.168.42.79";
int server_port = 64923;

uint rigid_body_id_we_want = 2501;
uint decimation_we_want = 10; // Decimation to be a bit lower, but nothing too fancy
int listening_on_port = 6548; // This should be randomised


UdpClient udp_client = new UdpClient(listening_on_port);

/*
 * Sending request bit
 */


// <rigid_body_id_in_motive>;<udp_port_to_stream_to>;<decimation>
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

Console.WriteLine("Packet sent, now waiting for the response... Press Ctrl+c in this window to terminate.");

/*
 * Parse the received data bit. You probably won't read the documentation, so here is the received packet format:
 * Note the separators. Semicolons for fields, commas for the vectors, and a newline at the end.
 * <unix time in milliseconds>;<rigid body ID>;<X-coordinate>,<Y-coordinate>,<Z-coordinate>;<QX>,<QY>,<QZ>,<QW>;<name of rigid body><0x0A>
 */

IPEndPoint ip_endpoint = new IPEndPoint(IPAddress.Parse(server_ip), listening_on_port); // Accept packets only from the server.


// This is in a timed infinite loop simulating a thread, and we use non-blocking ReceiveAsync method.
// So, if there is nothing to receive, it will just throw a message that nothing was received.


while(true)
{
    // This blocks execution until we received something
    Byte[] received_payload = udp_client.Receive(ref ip_endpoint);

    // If we have something, then convert it to string
    string received_payload_as_string = Encoding.ASCII.GetString(received_payload);
    Console.Clear();
    Console.WriteLine("Received {0} bytes, payload is:\n\t{1}", received_payload.Length, received_payload_as_string);

    // It is possible to have the same computer, on the same port requesting different rigid bodies.

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

    if(rigid_body_id_extracted_from_separated_string == rigid_body_id_we_want)
    {
        // If we have the correct one, then:

        // Extract the translation coordinates
        string[] translation_as_string = separated_string[2].Split(",");
        if(translation_as_string.Length != 3)
        {
            Console.WriteLine("Something is wrong with the formatting of the translation coordinates, could not split it into numbers.");
            return;
        }

        if(!float.TryParse(translation_as_string[0], new CultureInfo("en-US"), out float translation_x))
        {
            translation_x = float.NaN;
        }

        if(!float.TryParse(translation_as_string[1], new CultureInfo("en-US"), out float translation_y))
        {
            translation_y = float.NaN;
        }

        if(!float.TryParse(translation_as_string[2], new CultureInfo("en-US"), out float translation_z))
        {
            translation_z = float.NaN;
        }


        // Extract the orientation
        string[] quaternion_as_string = separated_string[3].Split(",");
        if(quaternion_as_string.Length != 4)
        {
            Console.WriteLine("Something is wrong with the formatting of the quaternion, could not split it into numbers.");
            return;
        }


        if(!float.TryParse(quaternion_as_string[0], new CultureInfo("en-US"), out float quaternion_qx))
        {
            quaternion_qx = float.NaN;
        }

        if(!float.TryParse(quaternion_as_string[1], new CultureInfo("en-US"), out float quaternion_qy))
        {
            quaternion_qy = float.NaN;
        }

        if(!float.TryParse(quaternion_as_string[2], new CultureInfo("en-US"), out float quaternion_qz))
        {
            quaternion_qz = float.NaN;
        }

        if(!float.TryParse(quaternion_as_string[3], new CultureInfo("en-US"), out float quaternion_qw))
        {
            quaternion_qz = float.NaN;
        }



        // Print the output:
        Console.WriteLine("Processed rigid body {0} at timestamp {1}. ID: {2}\n\tXYZ: {3}, {4}, {5} - QxQyQzQw: {6},{7},{8},{9}\n\n\n", rigid_body_name_string, unix_time_in_milliseconds, rigid_body_id_extracted_from_separated_string,
                    translation_x, translation_y, translation_z,
                    quaternion_qx, quaternion_qy, quaternion_qz, quaternion_qw);

        Thread.Sleep(1000);
    }


}




