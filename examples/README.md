# `optitrack-motive-data-streamer` examples

Every example here revolves around the following:

* Send `<rigid_body_id_in_motive>;<udp_port_to_stream_to>;<decimation>` to the server (in the lab, it is `192.168.42.5:64923`, yours will be different)
* Receive a packet on the host, at port `<udp_port_to_stream_to>` (hopefully at a decimation that is healthy enough for this thing to work without crashing)
* Parse the contents of the received packet, if necessary.

See the directories here. Note that the examples are not EXACTLY the same. Some have more, some have less error management. The idea is, that it allows you to develop your own code with it.

## So what is what?

Each platform has its own directory with examples in them. As the need arises for more platforms, the number of directories here will increase accordingly.

* `Matlab`:
This implementation was initially a test, but then it was developed into a replacement of the [NatNet Matlab wrapper](https://docs.optitrack.com/developer-tools/natnet-sdk/natnet-matlab-wrapper) due to a bug in Motive 3.x where the rigid body IDs and their coordinates are mixed up. The `volciclab_optitrack_streamer.m` is to be part of [volciclab-utilities](https://github.com/ha5dzs/volciclab-utilities/tree/main/OptiTrack) until the NatNet wrapper gets fixed.

* `see-sharp`
This is a 'pure' C# implementation, to see if the back-end functionalities are working. I only tested it on Windows, but it may well work with mono and on other platforms. It can be used in other custom applications.

* `Unity`
Scripts written for use with the Unity game engine. Effectively, create a GameObject, attach this script to it, set it up, and it will adjust the position and rotation of the GameObject and all its children. Use the `streaming_client_async.cs` example, unless you are 100% sure your packets are reliably coming in.