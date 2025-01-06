# `optitrack-motive-data-streamer` examples

Every example here revolves around the following:

* Send `<rigid_body_id_in_motive>;<udp_port_to_stream_to>;<decimation>` to the server (in the lab, it is `192.168.42.5:64923`, yours will be different)
* Receive a packet on the host, at port `<udp_port_to_stream_to>` (hopefully at a decimation that is healthy enough for this thing to work without crashing)
* Parse the contents of the received packet, if necessary.

See the directories here. Note that the examples are not EXACTLY the same. Some have more, some have less error management. The idea is, that it allows you to develop your own code with it.

There are two implementations:

* `streamer_client_script.cs` is the simplest one, but it blocks execution. So if you have issues with lost packets or performance issues with the server, your application will freeze. If you are developing for VR, then this is not a good option.
* `streamer_client_script_async.cs` is the non-blocking version, best suited for VR. Tested on Oculus/Meta android headsets.