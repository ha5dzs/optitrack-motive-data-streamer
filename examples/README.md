# `optitrack-motive-data-streamer` examples

Every example here revolves around the following:

* Send `<rigid_body_id_in_motive>;<udp_port_to_stream_to>;<decimation>` to the server (in the lab, it is `192.168.42.5:64923`, yours will be different)
* Receive a packet on the host, at port `<udp_port_to_stream_to>` (hopefully at a decimation that is healthy enough for this thing to work without crashing)
* Parse the contents of the received packet, if necessary.

See the directories here.
