/*
 * In C#, everybody screams.
 * 
 * This code connects to the NatNet server, and creates another server.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.IO;

using NatNetML; // This is local to the code's directory, see the .csproj file. You need to copy NatNetLib.dll too.


public class OpitrackMotiveDataStreamer
{
    /*
     * Shared variables.
     */

    // Absolute maximum stuff, for memory allocation
    public static UInt16 max_no_of_rigid_bodies_to_share = 1024;
    public static UInt16 max_no_of_rigid_bodies_to_stream = 1024;
    
    public static int no_of_streaming_destinations = 0; // We will update this.
    public static int no_of_rigid_bodies_in_natnet = 0; // We will update this once we have this interfaced with NatNet.
    public static long framecounter = 0; // NatNet's framecounter.
    public static int framerate = 0; // NatNet's frame rate.

    public static RigidBodyRecord[] shared_rigid_bodies = new RigidBodyRecord[max_no_of_rigid_bodies_to_share]; // NatNet will fill this.

    public static DataToSendRegister[] rigid_body_info_to_send = new DataToSendRegister[max_no_of_rigid_bodies_to_stream]; // Local thread will fill this using UDP control packets.

    // If we muck around with these variables, we better Mutex them.
    private static Mutex mutex = new Mutex();

    public static bool keep_thread_alive = false;

    /*
     * Local network stuff. Control port is hard-coded to 64923. See docs why.
     */

    public static UdpClient udp_client = new UdpClient(64923); // UDP object
    public static IPEndPoint remote_end_point = new IPEndPoint(IPAddress.Any, 64923); // IP endpoint object.


    /*
     * NatNet-specific stuff.
     * All IPs are hard-coded to 127.0.0.1. See docs why.
     */

    // The actual NatNet thingy, this is being used inside the functions.
    private static NatNetML.NatNetClientML natnet_client;

    // Connection details
    private static string my_ip_address = "127.0.0.1"; // This is the computer's IP address this thing is running on
    private static string server_ip_address = "127.0.0.1"; // Where Motive runs
    private static NatNetML.ConnectionType connection_type = ConnectionType.Multicast;

    // Data descriptor. Not sure yet what this does.
    private static List<NatNetML.DataDescriptor> data_dscriptor = new List<NatNetML.DataDescriptor>();

    // I will just concentrate on rigid bodies. We don't use skeletons or lone markers.
    private static Hashtable rigid_body_hashtable = new Hashtable();
    private static List<RigidBody> rigid_bodies = new List<RigidBody>();

    // In this case, 'asset' can be literally anything, so I am keeping it for now.
    private static bool assets_changed = false;


    /*
     * Main function.
     */
    static void Main(string[] input_arguments)
    {
        /*
         * Let's do some sanity check on the input arguments.
         */

        // Do we even have an input argument?
        if (input_arguments.Length < 1)
        {
            Console.WriteLine("Please specify at least 1 input argument, which should be the IP address Motive is running on. Defaulting to 127.0.0.1. Maybe it will work?");
            
        }

        // Is it an IPv4 thing?
        string[] butchered_input_argument = input_arguments[0].Split('.');
        if(butchered_input_argument.Length != 4)
        {
            Console.WriteLine("Error: The input argument ({0}) does not seem to be a valid IPv4 address.", input_arguments[0]);
        }

        // ok, if we got this far, then we can use this.
        string motive_computer_s_ip = input_arguments[0];
        Console.WriteLine("Motive is allegedly on {0}", motive_computer_s_ip);

        /*
         * Check if we have a local IPv4 address in the same subnet as NatNet's.
         */
        
        string this_computer_s_host_name = Dns.GetHostName();
        IPAddress[] this_computer_s_ip_addresses = Dns.GetHostAddresses(this_computer_s_host_name);

        if (this_computer_s_ip_addresses.Length == 0)
        {
            Console.WriteLine("Error: This computer should have some sort of a network connection. At least a loopback interface?");
            return;
        }
        Console.WriteLine("Local IP addresses are:");

        for (int i = 0; i<this_computer_s_ip_addresses.Length; i++)
        {
            Console.WriteLine("{0}: {1}", i, this_computer_s_ip_addresses[i]);
        }

        if(input_arguments.Length == 1 && this_computer_s_ip_addresses.Length > 1)
        {
            Console.WriteLine("\nError - It seems that you have more than 1 IP address on your computer.");
            Console.WriteLine("\tSpecify which one (0, 1, 2 ....) you want to use as your second input argument.");
            Console.WriteLine("\tMotive and your computer should ideally be in the same subnet.");
            return;
        }

        // Check if the second argument makes kind of sense.
        var which_ip_address_to_use_if_we_have_many = uint.Parse(input_arguments[1]); // Rely on Microsoft.

        if (which_ip_address_to_use_if_we_have_many >= this_computer_s_ip_addresses.Length)
        {
            Console.WriteLine("Error: You seem to have selected number {0} out of the {1} IP addresses you have on your computer. This doesn't make sense.",
                                which_ip_address_to_use_if_we_have_many, this_computer_s_ip_addresses.Length);
            return;
        }


        // If we survived this far, then:
        string this_computer_s_ip = this_computer_s_ip_addresses[which_ip_address_to_use_if_we_have_many].ToString();


        long current_time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Current unix time.
        long past_time = 0; // Previous unix time.

        long old_framecounter = 0; // To measure sampling rate.

        /*
         * It seems that initialising an array with a fixed number of places does not allocate memory.
         * I checked some NatNet code too, and they are filling their arrays in for loops too.
         * So, I am doing it here, manually. Must be a Microsoft thing.
         */

        //Pre-allocate stuff with non-null values, for limiting memory leaks
        for (int i = 0; i < max_no_of_rigid_bodies_to_share; i++)
        {
            shared_rigid_bodies[i] = new RigidBodyRecord { RigidBodyID = 0, RigidBodyName = "Blank placeholder rigid body", RigidBodyX = 0, RigidBodyY = 0, RigidBodyZ = 0, RigidBodyQX = 0, RigidBodyQY = 0, RigidBodyQZ = 0, RigidBodyQW = 1 };
        }

        for (int i = 0; i < max_no_of_rigid_bodies_to_stream; i++)
        {
            rigid_body_info_to_send[i] = new DataToSendRegister { IDToSend = 0, DestinationIP = "127.0.0.1", DestinationPort = 0, Decimation = 0};
        }


        
        



        
        

        NatNetML.ConnectionType connection_type_to_motive = ConnectionType.Multicast;

        Console.WriteLine("Connecting to server...");
        connectToServer(motive_computer_s_ip, this_computer_s_ip, connection_type_to_motive);

        bool connectionConfirmed = fetchServerDescriptor();

        if (connectionConfirmed)                         // Once the connection is confirmed.
        {

            fetchDataDescriptor();                  //Fetch and parse data descriptor

            /*  [NatNet] Assigning a event handler function for fetching frame data each time a frame is received   */
            natnet_client.OnFrameReady += new NatNetML.FrameReadyEventHandler(ObtainFrameData);

            Console.WriteLine("Success: Data Port Connected. Press ESC in this window to kill this application. \n");

        }
        else
        {
            // If we got here, throw an error message and exit
            Console.WriteLine("ERROR: Could not connect to the NatNet server. Exiting.");
            return;
        }

        /*
         * Multi-threaded goodness.
         */
        keep_thread_alive = true;
        Thread instruction_receiver_thread = new Thread(new ThreadStart(WaitForInstructions));
        Thread rigid_body_sender_thread = new Thread(new ThreadStart(SendRigidBodyDataToClients));

        // Start UDP threads.
        instruction_receiver_thread.Start();
        rigid_body_sender_thread.Start();


        while (true)
        {

            // Exception handler for updated assets list.
            if (assets_changed == true)
            {
                    
                /*  Clear out existing lists */
                data_dscriptor.Clear();

                rigid_bodies.Clear();

                /* [NatNet] Re-fetch the updated list of descriptors  */
                fetchDataDescriptor();
                assets_changed = false;
            }
            
            // Look for the escape key.
            while ((Console.KeyAvailable && Console.ReadKey().Key == ConsoleKey.Escape))
            {
                /*
                 * Notice for my future self.
                 * If you kill the framecounter by disconnecting NatNet,
                 * then the sender thread will hand with a mutex attached to it.
                 */


                // If we got here, we gracefully quit.
                Console.Clear();
                Console.WriteLine("EExiting gracefully."); // Weird artifact in console. The first letter is chopped off. Don't know why, don't care why.
                keep_thread_alive = false; // This makes the threads break out from their while loops
                
                // Close UDP threads.
                
                udp_client.Close(); // kills the udp client


                Console.Write("Terminating sender thread...");
                rigid_body_sender_thread.Join(); // Allow this thread to die first
                Console.WriteLine("done!");

                
                Console.Write("Terminating receiver thread...");
                instruction_receiver_thread.Join(); // ...allowing the instruction receiver thread to die.
                Console.WriteLine("done!");

                Console.Write("Disconnecting...."); 
                natnet_client.OnFrameReady -= ObtainFrameData; // Remove the timed execution.
                /*  Clearing Saved Descriptions */
                data_dscriptor.Clear();
                rigid_bodies.Clear();

                natnet_client.Disconnect();
                Console.WriteLine("NatNet client disconnected.");


                Console.WriteLine("NatNet disconnected, threads closed. See you later!");

                return;

            }




            // Update the screen once a second.
            while (past_time == current_time)
            {
                // Wait in this loop and allow time to advance.
                current_time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            
            // If we got here, we can hijack the console.
            if (framecounter > 0)
            {

                Console.Clear();
                Console.WriteLine("{0} - NatNet has {1} rigid bodies, streaming at {2} frames per second:", framecounter, no_of_rigid_bodies_in_natnet, framecounter - old_framecounter);
                old_framecounter = framecounter;

                for (int i = 0; i < no_of_rigid_bodies_in_natnet; i++)
                {
                    Console.WriteLine("\t ID:{0}\t({1}):\tXYZ: {2}, {3}, {4}", shared_rigid_bodies[i].RigidBodyID, shared_rigid_bodies[i].RigidBodyName,
                                                                             shared_rigid_bodies[i].RigidBodyX, shared_rigid_bodies[i].RigidBodyY, shared_rigid_bodies[i].RigidBodyZ);
                }
                Console.WriteLine("---------------------------------------");



                if (no_of_streaming_destinations > 0)
                {
                    Console.WriteLine("\nUDP client: Sending data to {0} streaming destinations:", no_of_streaming_destinations);
                    for (int i = 0; i < no_of_streaming_destinations; i++)
                    {
                        Console.WriteLine("\t To: {0}:{1}, ID: {2}, decimation: {3}", rigid_body_info_to_send[i].DestinationIP, rigid_body_info_to_send[i].DestinationPort,
                                                                                      rigid_body_info_to_send[i].IDToSend, rigid_body_info_to_send[i].Decimation);
                    }
                }
                else
                {
                    Console.WriteLine("UDP client: No streaming destinations registered, yet.");
                }


                Console.WriteLine("---------------------------------------");
                Console.WriteLine("Press Esc in this window to gracefully exit, or Ctrl+C in this window to rudely terminate.");


                past_time = current_time;
            }
           
        }
       
    }


    // This function came straight from the example.
    static void connectToServer(string serverIPAddress, string localIPAddress, NatNetML.ConnectionType connectionType)
    {
        /*  [NatNet] Instantiate the client object  */
        natnet_client = new NatNetML.NatNetClientML();

        /*  [NatNet] Checking verions of the NatNet SDK library  */
        int[] verNatNet = new int[4];           // Saving NatNet SDK version number
        verNatNet = natnet_client.NatNetVersion();
        Console.WriteLine("NatNet SDK Version: {0}.{1}.{2}.{3}", verNatNet[0], verNatNet[1], verNatNet[2], verNatNet[3]);

        /*  [NatNet] Connecting to the Server    */

        NatNetClientML.ConnectParams connectParams = new NatNetClientML.ConnectParams();
        connectParams.ConnectionType = connectionType;
        connectParams.ServerAddress = serverIPAddress;
        connectParams.LocalAddress = localIPAddress;

        Console.WriteLine("\nConnecting...");
        Console.WriteLine("\tServer IP Address: {0}", serverIPAddress);
        Console.WriteLine("\tLocal IP address : {0}", localIPAddress);
        Console.WriteLine("\tConnection Type  : {0}", connectionType);
        Console.WriteLine("\n");

        natnet_client.Connect(connectParams);
    }

    // This is called immediately after connection. Provides error messages if times are sad.
    static bool fetchServerDescriptor()
    {
        NatNetML.ServerDescription m_ServerDescriptor = new NatNetML.ServerDescription();
        int errorCode = natnet_client.GetServerDescription(m_ServerDescriptor);

        if (errorCode == 0)
        {
            Console.WriteLine("Success: Connected to the server\n");
            parseSeverDescriptor(m_ServerDescriptor);
            return true;
        }
        else
        {
            Console.WriteLine("Error: Failed to connect. Check the connection settings.");
            Console.WriteLine("Program terminated (Enter ESC to exit)");
            return false;
        }
    }
    // This function just displays some diagnostic data.
    static void parseSeverDescriptor(NatNetML.ServerDescription server)
    {
        Console.WriteLine("Server Info:");
        Console.WriteLine("\tHost               : {0}", server.HostComputerName);
        Console.WriteLine("\tApplication Name   : {0}", server.HostApp);
        Console.WriteLine("\tApplication Version: {0}.{1}.{2}.{3}", server.HostAppVersion[0], server.HostAppVersion[1], server.HostAppVersion[2], server.HostAppVersion[3]);
        Console.WriteLine("\tNatNet Version     : {0}.{1}.{2}.{3}\n", server.NatNetVersion[0], server.NatNetVersion[1], server.NatNetVersion[2], server.NatNetVersion[3]);
    }

    // This one gets the descriptors from the server, and calls parseDataDescriptor()
    static void fetchDataDescriptor()
    {
        /*  [NatNet] Fetch Data Descriptions. Instantiate objects for saving data descriptions and frame data    */
        bool result = natnet_client.GetDataDescriptions(out data_dscriptor);
        if (result)
        {
            Console.WriteLine("Success: Data Descriptions obtained from the server.");
            parseDataDescriptor(data_dscriptor);
        }
        else
        {
            Console.WriteLine("Error: Could not get the Data Descriptions");
        }
        Console.WriteLine("\n");
    }

    // This has been reduced to look at rigid bodies only.
    static void parseDataDescriptor(List<NatNetML.DataDescriptor> description)
    {
        //  [NatNet] Request a description of the Active Model List from the server. 
        //  This sample will list only names of the data sets, but you can access 
        int numDataSet = description.Count;
        Console.WriteLine("Total {0} data sets in the capture:", numDataSet);


        Console.WriteLine("Rigid bodies:");
        for (int i = 0; i < numDataSet; ++i)
        {
            // We go through everything that is being streamed, and display the rigid body names and IDs.
            int dataSetType = description[i].type;

            // Filter for rigid bodies.
            if (dataSetType == (int)NatNetML.DataDescriptorType.eRigidbodyData)
            {
                // Apparently, this is going to be 5.
                NatNetML.RigidBody rb = (NatNetML.RigidBody)description[i];
                Console.WriteLine("\t{0}, id:{1}", rb.Name, rb.ID);
                // Saving Rigid Body Descriptions
                rigid_bodies.Add(rb);


            }
        }
    }

    // This function processes the network thread in the background to the NatNet server
    static void ObtainFrameData(NatNetML.FrameOfMocapData data, NatNetML.NatNetClientML client)
    {

        /*  Exception handler for cases where assets are added or removed.
            Data description is re-obtained in the main function so that contents
            in the frame handler is kept minimal. */
        if ((data.bTrackingModelsChanged == true || data.nRigidBodies != rigid_bodies.Count))
        {
            assets_changed = true;
        }

        /*
         * These variables are being read by other threads.
         * We Mutex them, to make sure data is not being corrupted.
         */
        
        mutex.WaitOne();
        //Console.WriteLine("After mutex.");
        // Update the framecounter
        framecounter = data.iFrame;

        // Update the number of rigid bodies in the system
        no_of_rigid_bodies_in_natnet = data.nRigidBodies;

        // Loop through the number of rigid bodies, and place the stuff where they belong.
        for(int i = 0; i < no_of_rigid_bodies_in_natnet; i++)
        {
            shared_rigid_bodies[i].RigidBodyID = (UInt16)data.RigidBodies[i].ID;
            shared_rigid_bodies[i].RigidBodyName = rigid_bodies[i].Name;
            // Translation
            shared_rigid_bodies[i].RigidBodyX = data.RigidBodies[i].x;
            shared_rigid_bodies[i].RigidBodyY = data.RigidBodies[i].y;
            shared_rigid_bodies[i].RigidBodyZ = data.RigidBodies[i].z;
            // Rotation
            shared_rigid_bodies[i].RigidBodyQX = data.RigidBodies[i].qx;
            shared_rigid_bodies[i].RigidBodyQY = data.RigidBodies[i].qy;
            shared_rigid_bodies[i].RigidBodyQZ = data.RigidBodies[i].qz;
            shared_rigid_bodies[i].RigidBodyQW = data.RigidBodies[i].qw;
        }

        mutex.ReleaseMutex();

        
    }

    static void proceFrameData(NatNetML.FrameOfMocapData data)
    {
        /*  Parsing Rigid Body Frame Data   */
        for (int i = 0; i < rigid_bodies.Count; i++)
        {
            int rbID = rigid_bodies[i].ID;              // Fetching rigid body IDs from the saved descriptions

            for (int j = 0; j < data.nRigidBodies; j++)
            {
                if (rbID == data.RigidBodies[j].ID)      // When rigid body ID of the descriptions matches rigid body ID of the frame data.
                {
                    NatNetML.RigidBody rb = rigid_bodies[i];                // Saved rigid body descriptions
                    NatNetML.RigidBodyData rbData = data.RigidBodies[j];    // Received rigid body descriptions

                    if (rbData.Tracked == true)
                    {
                        Console.WriteLine("\tRigidBody ({0}):", rb.Name);
                        Console.WriteLine("\t\tpos ({0:N3}, {1:N3}, {2:N3})", rbData.x, rbData.y, rbData.z);

                        // Rigid Body Euler Orientation
                        float[] quat = new float[4] { rbData.qx, rbData.qy, rbData.qz, rbData.qw };
                        float[] eulers = new float[3];

                        eulers = NatNetClientML.QuatToEuler(quat, NATEulerOrder.NAT_XYZr); //Converting quat orientation into XYZ Euler representation.
                        double xrot = RadiansToDegrees(eulers[0]);
                        double yrot = RadiansToDegrees(eulers[1]);
                        double zrot = RadiansToDegrees(eulers[2]);

                        Console.WriteLine("\t\tori ({0:N3}, {1:N3}, {2:N3})", xrot, yrot, zrot);
                    }
                    else
                    {
                        Console.WriteLine("\t{0} is not tracked in current frame", rb.Name);
                    }
                }
            }
        }


        // This function throws the rigid body data to their repectove location.



        /* Optional Precision Timestamp (NatNet 4.1 or later) */
        if (data.PrecisionTimestampSeconds != 0)
        {
            int hours = (int)(data.PrecisionTimestampSeconds / 3600);
            int minutes = (int)(data.PrecisionTimestampSeconds / 60) % 60;
            int seconds = (int)(data.PrecisionTimestampSeconds) % 60;

            Console.WriteLine("Precision Timestamp HH:MM:SS : {0:00}:{1:00}:{2:00}", hours, minutes, seconds);
            Console.WriteLine("Precision Timestamp Seconds : {0}", data.PrecisionTimestampSeconds);
            Console.WriteLine("Precision Timestamp Fractional Seconds : {0}", data.PrecisionTimestampFractionalSeconds);
        }

        Console.WriteLine("\n");
    }


    // Some trigonometry. I think there is an integrated thing that does this, but hey, what do I know, right?
    static double RadiansToDegrees(double dRads)
    {
        return dRads * (180.0f / Math.PI);
    }

    /*
     * This was shamelessly stolen from here:
     * http://www.java2s.com/Code/CSharp/Network/GetSubnetMask.htm
     * I modified the return path, I don't want exceptions here.
     */
    public static IPAddress? GetSubnetMask(IPAddress address)
    {
        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (UnicastIPAddressInformation unicastIPAddressInformation in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicastIPAddressInformation.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (address.Equals(unicastIPAddressInformation.Address))
                    {
                        return unicastIPAddressInformation.IPv4Mask;
                    }
                }
            }
        }
        return null;
    }


        /*
        * This function waits for the instructions on the UDP port, and then parses the received information
        * If everything went well, it updates the rigid_body_info_to_send array.
        * This writes into the where to send data array. That needs mutex'd.
        */
    public static void WaitForInstructions()
    {
        while (keep_thread_alive == true)
        {
            bool received_request = false; // We check if we received a request

            byte[] received_data = null; // This is the buffer udp client will write to
            string sender_ip_address = null;

            // Stay here until we get something
            try
            {
                received_request = true;
                received_data = udp_client.Receive(ref remote_end_point);
                
            }
            catch
            {
                // If we got here, the UDP client failed, so by principle cannot be a received request.
                received_request = false; 
            }
            sender_ip_address = remote_end_point.Address.ToString(); // We will need this.
            


            if (received_request == true)
            {
                // If we got here, we have some data in the buffer, and we can work on this.

                // Try making the sense out of this.
                string packet_contents = Encoding.ASCII.GetString(received_data);
                string[] packet_contents_per_field = packet_contents.Split(';'); // CHUNK THAT THANG!!!!

                // Did we receive the correct number of arguments?
                if (packet_contents_per_field.Length != 3)
                {
                    // If we got here, we received an incorrect number of arguments.
                    Console.WriteLine("WaitForInstructions(): Host {0} sent something strange: {1}, and must be ignored. Expected format is <RigidBodyID>;<SendToWhichPort>;<Decimation>", sender_ip_address, packet_contents);
                    received_request = false;
                    return;
                }

                // Parse the separated string into 16-bit integers.
                var received_rigid_body_id = UInt16.Parse(packet_contents_per_field[0]);
                var received_destination_port = UInt16.Parse(packet_contents_per_field[1]);
                var received_decimation = UInt16.Parse(packet_contents_per_field[2]);

                Console.WriteLine("WaitForInstructions(): Parsed message from {0}:\n\t{1};{2};{3}", sender_ip_address, received_rigid_body_id, received_destination_port, received_decimation);

                /* Decimation = 0 -> Stop streaming.
                if(received_decimation < 1)
                {
                    received_decimation = 1;
                    Console.WriteLine("WaitForInstructions(): Decimation cannot be smaller than 1. Sending a packet every frame.");
                }
                */

                // Check if we have a correct rigid body ID in the shared rigid bodies.
                int index_in_shared_rigid_bodies = -1; // We will update this.

                // Since we have a class array, we need to go c-esque. I am ok with this, and I have the time to do this.
                for (int index = 0; index < max_no_of_rigid_bodies_to_stream; index++)
                {

                    //Console.Write("Index {0}: ", index);
                    //Console.WriteLine("and received_rigid_body_id is {0}", received_rigid_body_id);
                    /*
                     * This statement fails with nullpointer exception, even if I want to print its value.
                     */

                    //Console.WriteLine("Number of rigid bodies detected in shared array: {0}", shared_rigid_bodies.Length);


                    //Console.Write("shared_rigid_bodies[{0}].RigidBodyID is {1}}, ", index, shared_rigid_bodies[index].RigidBodyID);


                    if (shared_rigid_bodies[index].RigidBodyID == received_rigid_body_id)
                    {
                        // If we got here, we have a match.
                        index_in_shared_rigid_bodies = index;
                    }
                }

                // Do we have a match?
                if (index_in_shared_rigid_bodies < 0)
                {
                    Console.WriteLine("WaitForInstructions(): Request by host {0}, message {1}: Rigid body ID {2} was not found in currently streamed rigid bodies. Ignoring request.", sender_ip_address, packet_contents, received_rigid_body_id);
                    received_request = false;
                    return;
                }
                else
                {
                    /*
                     * If we have a match, we need to check if the same rigid body ID by the same host has been addressed before.
                     * If so, then that record needs to be updated (change of decimation, port(0 means stop))
                     * Otherwise, we can continue adding this.
                     */

                    // We go through everything that we need to stream, and check if we are updating a value.
                    bool need_to_add_record = true;
                    for (int k = 0; k < no_of_streaming_destinations; k++)
                    {
                        //If the rigid body ID, the sender's IP address and the port match, then just update the decimation.
                        if (rigid_body_info_to_send[k].IDToSend == (UInt16)shared_rigid_bodies[index_in_shared_rigid_bodies].RigidBodyID && // Rigid body ID
                            rigid_body_info_to_send[k].DestinationIP.Equals(sender_ip_address) && // IP address is a string.
                            rigid_body_info_to_send[k].DestinationPort == received_destination_port) // The destination port matches too
                        {
                            // If we got here, we just update the existing record.
                            mutex.WaitOne();
                            rigid_body_info_to_send[k].Decimation = received_decimation;
                            mutex.ReleaseMutex();
                            need_to_add_record = false;
                        }
                    }

                    if (need_to_add_record)
                    {
                        mutex.WaitOne();
                        // If we have a match, then add this to rigid_body_info_to_send

                        rigid_body_info_to_send[no_of_streaming_destinations].IDToSend = (UInt16)shared_rigid_bodies[index_in_shared_rigid_bodies].RigidBodyID; // rigid body ID in the shared array

                        // If port to send is 0, then disable streaming. This is done elsewhere.
                        rigid_body_info_to_send[no_of_streaming_destinations].DestinationIP = sender_ip_address; // IP address as string

                        rigid_body_info_to_send[no_of_streaming_destinations].DestinationPort = received_destination_port; // Port. 16-bit number.

                        rigid_body_info_to_send[no_of_streaming_destinations].Decimation = received_decimation; // every N frames.

                        no_of_streaming_destinations++; // Increase this, so we can update it later-on.
                        received_request = false; // Everything is in the array, so we can do something else now.
                        mutex.ReleaseMutex();

                        // Give a warning if required.
                        if (no_of_streaming_destinations >= max_no_of_rigid_bodies_to_stream * 0.9)
                        {
                            Console.WriteLine("WaitForInstructions(): You are rapidly approaching {0} rigid bodies to stream over your network. If you are OK with this, ignore this message.", max_no_of_rigid_bodies_to_stream);
                        }
                    }

                    received_request = false;
                    //Console.WriteLine("WaitForInstructions(): Information put in place:\n\tRigid body ID: {0}(index {1}), send to {2}:{3}", received_rigid_body_id, index_in_shared_rigid_bodies, sender_ip_address, received_destination_port);
                }
            }
        }
        //Console.WriteLine("WaitForInstructions(): No need to run any more, returning.");
        return;
    }

    /*
     * This function does not write into the shared variables, but it does occupy the network interface.
     */

    public static void SendRigidBodyDataToClients()
    {
        long past_framecounter = 0;
        while (keep_thread_alive == true)
        {

            /*
             * As this loop goes faster than the framecounter, we need to make sure we send the packet once per decimation criterion.
             * We hang in the code until the framecounter advances.
             */
            //Console.WriteLine("Framecounters: {0}{1}", framecounter, past_framecounter);
            while (framecounter == past_framecounter)
            {
                // Just hang in this loop until the framecounter advances

                // NatNet will upgrade this, but right now we have to do it this way.
                //framecounter = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            
            past_framecounter = framecounter;
            

            for (int i = 0; i < no_of_streaming_destinations; i++)
            {
                // We go through this array, and send the required data. We need to find the index in the main array, as it can change during runtime.
                // To speed this up, we won't go through the entire pre-allocated array, only for the number of rigid bodies.

                if (rigid_body_info_to_send[i].Decimation != 0)
                {
                    // If the decimation is nonzero, then send the stuff. Otherwise, don't send.
                    for (int j = 0; j < no_of_rigid_bodies_in_natnet; j++)
                    {
                        // As we created both of these, there must be a match.

                        if ((shared_rigid_bodies[j].RigidBodyID == rigid_body_info_to_send[i].IDToSend) && // If we have a match
                            (framecounter % rigid_body_info_to_send[i].Decimation == 0) && // and it is time to send
                            rigid_body_info_to_send[i].DestinationPort != 0) // and it is enabled to be sent
                        {
                            // If we got here, we have the correct index for both arrays, so we can send the packet.

                            /*
                            * For simplicity, every value will be human-readable string. So one can literally read these on the network.
                            * Format is:
                            * <RigidBodyID>;<X,Y,Z>;<Qx,Qy,Qz,Qw>;<Name><0x0A>
                            * Note that the separator is a semicolon, so I can toss vectors easier. Decimal separator is a dot.
                            */

                            // my regional settings is not US, and we don't use a point to mark decimals. Here is a workaround for that.
                            string string_to_send = string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-US"), "{0};{1},{2},{3};{4},{5},{6},{7};{8}\n",
                                                                    shared_rigid_bodies[j].RigidBodyID,
                                                                    shared_rigid_bodies[j].RigidBodyX, shared_rigid_bodies[j].RigidBodyY, shared_rigid_bodies[j].RigidBodyZ,
                                                                    shared_rigid_bodies[j].RigidBodyQX, shared_rigid_bodies[j].RigidBodyQY, shared_rigid_bodies[j].RigidBodyQZ, shared_rigid_bodies[j].RigidBodyQW,
                                                                    shared_rigid_bodies[j].RigidBodyName);

                            byte[] datagram_to_send = Encoding.ASCII.GetBytes(string_to_send);

                            //Console.WriteLine("SendRigidBodyDataToClients(): Sending packet to: {0}:{1}", rigid_body_info_to_send[i].DestinationIP, rigid_body_info_to_send[i].DestinationPort);
                            //Console.WriteLine("\tSendRigidBodyDataToClients(): Packet contents are: {0}", string_to_send);

                            //...and we use the socket interface to send this thing.
                            try
                            {
                                mutex.WaitOne();
                                // We exploit the fact that the indices are now pointing to the same data
                                udp_client.Send(datagram_to_send, datagram_to_send.Length, rigid_body_info_to_send[i].DestinationIP, rigid_body_info_to_send[i].DestinationPort);
                                mutex.ReleaseMutex();
                            }
                            catch (Exception e)
                            {

                                Console.WriteLine("SendRigidBodyDataToClients(): Error while sending the packet.");
                                Console.WriteLine("Settings are:\n\t{0}:{1} | Payload: {2}", rigid_body_info_to_send[i].DestinationIP, rigid_body_info_to_send[i].DestinationPort, datagram_to_send);
                                Console.WriteLine("See system's error message below:");
                                Console.WriteLine("SendRigidBodyDataToClients(): {0}", e.ToString());
                            }
                        }
                    }
                }
            }
        }
        //Console.WriteLine("SendRigidBodyDataToClients(): No need to run any more, returning.");
        return;
    }


    /* Class definitions for the inter-thread variables */

    public class RigidBodyRecord
    {
        // This is some epic levels of Microsoftness in here. In Matlab this could be a single row of a cell array.
        public UInt16 RigidBodyID { set; get; }
        public required string RigidBodyName { set; get; }
        // Translation coords
        public float RigidBodyX { set; get; }
        public float RigidBodyY { set; get; }
        public float RigidBodyZ { set; get; }
        // Rotation in quaternion format.
        public float RigidBodyQX { set; get; }
        public float RigidBodyQY { set; get; }
        public float RigidBodyQZ { set; get; }
        public float RigidBodyQW { set; get; }
    }


    /*
     * This is the definition of where to send the data, and which data to send.
     */

    public class DataToSendRegister
    {
        public required UInt16 IDToSend { get; set; } // Rigid body ID to send

        public required String DestinationIP { get; set; } // Which host to send this data to

        public UInt16 DestinationPort { get; set; } // Which port to send this data to

        public UInt16 Decimation { get; set; } // Send this data every N frames

    }

}