# Optitrack Motive data streamer

This code utilises [the NatNet SDK](https://docs.optitrack.com/developer-tools/natnet-sdk/natnet-4.0), and makes a streaming system that is much more flexible than NatNet.

## System requirements
You need the .NET 8 runtime. You can install it in Terminal with:

```powershell
winget install Microsoft.Dotnet.Runtime.8 --disable-interactivity
```
(...and of course you need an Optitrack system, with Motive configured to stream using Multicast on a network adapter this code is running on...)

## Usage

You can start this by specifying the IP address of the computer Motive runs on as the input argument

```
./optitrack-motive-data-streamer 192.168.42.5
```

If you have many local network adapters, or your system is configured to have both IPv6 and IPv4 addresses, you may have a selection of addresses to choose from. If you don't know which one to use, the code prints a list before running:

```
Motive is allegedly on 192.168.42.5
Local IP addresses are:
0: fe80::ef0c:aeb:c098:60d9%20
1: 192.168.42.79

Error - It seems that you have more than 1 IP address on your computer.
        Specify which one (0, 1, 2 ....) you want to use as your second input argument.
        Motive and your computer should ideally be in the same subnet.
```

So, in this case, we know that `1` is in the same subnet as Motive, so you can run the code with:

```
./optitrack-motive-data-streamer 192.168.42.5 1
```

You can control what is being streamed where with UDP packets containing unencrypted plain text string. The control packets are to be sent to port `64923` (this is hard-coded, sorry), see formatting below:

`<rigid_body_id_in_motive>;<udp_port_to_stream_to>;<decimation>`

The first two parts are self-explanatory. The third part, `decimation` is a number that reduces the packet rate, it only sends a packet at every N frames. So if your system runs at 200 frames per second, but your gadget can only process data at say 10 frames per second, there is no point in flooding its buffer until it crashes. So, by specifying `decomation` as `20`, it will only send a packet every twentieth received frame.

For example: you have te following rigid bodies in the system:
```
2425921 - NatNet has 4 rigid bodies, streaming at 200 frames per second:
         ID:2501        (Cylinder):     XYZ: 0,57740664, 1,2066058, 0,14536819
         ID:2509        (Surface):      XYZ: 0,953681, 0,25161505, 0,00040242076
         ID:2510        (Rotator):      XYZ: 0,5918385, 0,20872128, 0,34499633
         ID:2   (bits_and_pieces):      XYZ: 0,82329357, 0,1755859, 0,008196899
```

Say you want to stream the position and orientation to the rigid body `Rotator` to `192.168.42.79`, then from the slow computer that:
 * has the IP address of `192.168.42.79` and
* listens for packets on UDP port `24656` and
* only can process a rate of 10 packets a second,
 ...a packet with the following payload would need to be sent:
```
2510;24656;20
```

If received, the main screen will show that there is a destination:

```
---------------------------------------

UDP client: Sending data to 1 streaming destinations:
         To: 192.168.42.79:24656, ID: 2510, decimation: 200
---------------------------------------
Press Esc in this window to gracefully exit, or Ctrl+C in this window to rudely terminate.
```

In response, this code, 10 times per second, will send a packet to `192.168.42.79:24656`, with the payload in the following format:

`<rigid_body_id>;<X>,<Y>,<Z>>;<QX>,<QY>,<QZ>,<QW>;<rigid_body_name>`

Like so:

```
2510;0.59184,0.20873211,0.34497538;0.7072783,-0.0068869404,-0.020055031,0.7066172;Rotator
```

The rigid body ID is an integer. The X-Y-Z coordinates are floating point numbers, as is the rotation quaternion. The fields are separated with a semicolon (`;`), but corresponding elements are separated with a comma `,`. The decimals are separated with a decimal point `.`, irrespective of what regional settings are set.

This makes it easy to parse this string on the client, and you can use it in whatever application.

If you press Escape, it will close the threads and the connection to the NatNet server. If you press Ctrl+C, it will just kill everything, and the NatNet server will continue blasting data on Multicast.

Check the [Examples](examples) directory for various use cases.

## Why

I got angry because I found it too difficult to process this:

```
0000   48 4d 7e f0 74 f1 2c f0 5d 54 f5 5f 08 00 45 00   HM~.t.,.]T._..E.
0010   04 fc dd 00 00 00 80 11 83 93 c0 a8 2a 05 c0 a8   ............*...
0020   2a 07 05 e6 c4 1a 04 e8 3a 31 05 00 dc 04 11 00   *.......:1......
0030   00 00 01 00 00 00 43 79 6c 69 6e 64 65 72 00 cd   ......Cylinder..
0040   09 00 00 ff ff ff ff 00 00 00 00 00 00 00 00 00   ................
0050   00 00 00 06 00 00 00 16 39 9f 3c d9 5a 84 bc cf   ........9.<.Z...
0060   4d db 3d 82 52 19 bc 28 7b 70 3c 0c 56 dc 3d 30   M.=.R..({p<.V.=0
0070   60 6e bc 99 39 6e bc 81 42 dd 3d 77 c9 b0 3b dd   `n..9n..B.=w..;.
0080   27 64 bb 09 6d d9 3d b5 a9 a8 39 27 e5 c5 b9 8a   'd..m.=...9'....
0090   f9 37 b9 1a 82 c8 3c 7b aa 79 3c 79 b2 db 3d 00   .7....<{.y<y..=.
00a0   00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00   ................
00b0   00 00 00 00 00 00 00 4d 61 72 6b 65 72 31 00 4d   .......Marker1.M
00c0   61 72 6b 65 72 32 00 4d 61 72 6b 65 72 33 00 4d   arker2.Marker3.M
00d0   61 72 6b 65 72 34 00 4d 61 72 6b 65 72 35 00 4d   arker4.Marker5.M
00e0   61 72 6b 65 72 36 00 00 00 00 00 43 79 6c 69 6e   arker6.....Cylin
00f0   64 65 72 00 06 00 00 00 4d 61 72 6b 65 72 31 00   der.....Marker1.
0100   4d 61 72 6b 65 72 32 00 4d 61 72 6b 65 72 33 00   Marker2.Marker3.
0110   4d 61 72 6b 65 72 34 00 4d 61 72 6b 65 72 35 00   Marker4.Marker5.
0120   4d 61 72 6b 65 72 36 00 01 00 00 00 53 75 72 66   Marker6.....Surf
0130   61 63 65 00 ce 09 00 00 ff ff ff ff 00 00 00 00   ace
(it continues on, but you get the idea...)
```

I understand that Motive is made for some media production people. But me, as a researcher, I couldn't care less about what markers consist of my rigid body, or what is the exposure value in camera #6 mid-frame. I think, deep down somewhere at NaturalPoint, they realise this too, so they made the NatNet SDK relatively easy to adapt. But, then again, since they only distribute binaries for certain limited number of platforms, I cannot use it on most of my devices. And what devices those are, you might ask?

* Android VR headsets (Oculus Quest, Google Cardboard, and some Chinese internal market devices I may get in the future)
* Unsupported platforms and game engines (say GDevelop on Haiku OS, Quake engine on Palm OS)
* Embedded and/or legacy devices (Arduino, ESP-IDF, some old IBM PC/AT with Ethernet card)
* Linux (in practically any flavour)
* Data-centric languages such as R (to access real-time data)
* Octave (I made one for Matlab using a slightly butchered version of NatNet and some wrapper functions, [see here](https://github.com/ha5dzs/volciclab-utilities/tree/main/OptiTrack))

I made a couple of examples, but they all revolve around the simple message format introduced above.

I wrote this in C# because ~~I am a masochist~~ I wanted to re-use some of this code, with minimal modifications for the Unity game engine, which also uses the same language. I am really not happy with how many things are done in this environment. I especially found revolting how pre-allocating an array is not enough, and you have to write a loop to fill it with dummy data. Or when you have `List<variable_type>`, but it doesn't work with custom classes. So convenience ~~functions~~ methods such as `Find()` and `FindIndex()` don't work. I ended up using loops, and I just pretended I was writing C-code with some weird syntax. Is a structure array too difficult to comprehend? Or, since this is a 'high-level language', one could easy have a cell array or a data frame too. But no, that would be too simple.

## How does it work?

The NatNet part is loosely based on the SDK's `SampleClientML` example. It is stripped down to only use rigid bodies. After initialisation and successful connection to the NatNet server at `127.0.0.1`, `ObtainFrameData()` updates `shared_rigid_bodies` (IDs, names, translations, and quaternions), and the `framecounter` from Motive. This function is executed as a NatNet Event (`onFrameReady`), so effectively every frame. There are two threads that handle the UDP communication. `WaitForInstructions()` as a thread listens on port 64923, and looks for and incoming packet. Once it received a packet, it separates it, parses it, and places into `rigid_body_info_to_send` (rigid body ID, destination IP, destination port, decimation) what rigid body should be streamed where. A separate thread, `SendRigidBodyDataToClients()` goes through `rigid_body_info_to_send`, and if finds the same rigid body ID in `shared_rigid_bodies`, it sends its data to the destination IP and port set in the current index of `rigid_body_info_to_send`.

In the main thread, the console is initially set to display some diagnostic information (connection details, NatNet version, etc), and once the streaming starts, it updates the screen every second. It will show:

* The framecounter in the top left corner
* The frame rate of streaming (calculated locally, not as reported by NatNet - you can see if you have performance problems this way)
* The rigid bodies and indicative positions, as reported by NatNet
* The stats from the UDP client, showing where the packets are being sent to
* A friendly message on how to stop the application, depending on your mood. :)

