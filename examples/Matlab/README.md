# `udp_send_and_receive_test.m`

This was made in R2024a, with the 'new' `udpport` function.

Please update the IP address to wherever your system is.

Effectively, it compiles the string to send, and waits for the incoming packets in an infinite loop. There are some string bashing involved too, where the fields are split with the different separator characters. At the end of the packet, there is a `0x0A` byte to mark where packets end just in case you get some buffer overflow.

# `buffer_handling_test.m`

Let's say that there is some sort of a holdup, and you are polling for packets slower than how the packets are coming in. In this case, you will end up with more than one packet in the buffer. Now, since this is a UDP connection, this is totally chaotic, and there is no guarantee that:

* a sent packet will arrive at your host at all
* the packets sent by the server will arrive in the same order at your host

So this is when the timestamp business comes in. Since the packets are plain text, all this script does is to:

* get whatever is in the buffer, throw an error message if there is nothing in it.
* split the packets by the `\n` or `0x0A` character, convert buffer contents into a string array
* extract the first 13 characters (i.e. the timestamp) from the strings
* converts the timestamps to a vector
* find the index of the highest timestamp (i.e. the latest one)
* extract the latest packet in the string array

then you can process it to taste.

# `volciclab_optitrack_streamer.m`

This is a Matlab class that generates an object. When initialising this object, you can specify which rigid body to stream, how often you want to get updates. When the object is created, it will store the streamed packets in its buffer. You can request the latest data, or everything in the buffer. The code was written such that Matlab's internal documentation creator, which you can access by typing `doc volciclab_optitrack_streamer` in the command window.

You can make a separate object for every rigid body you want in order. You can even set up different objects for the same rigid body. It's all local within the class, and is accessible through the object you created.

## Usage

### Initialisation

Let's say you want access to rigid body ID 1010. Your motion tracker operates at 200 Hz sampling rate, and you want to get information about the whereabouts of the object no older than 100 millisconds. So the decimation will be 20.

So you can:

```Matlab
my_fancy_rigid_body = volciclab_optitrack_streamer(1010, 20)
```

If your system operates on a different network, chances are that the streamer server will not operate on `192.168.42.5`. You can customise this by either editing the Matlab file, or by specifying an optional argument. Say, if your server is at `10.230.240.138`, then you can specify this without editing anything as:

```Matlab
my_fancy_rigid_body = volciclab_optitrack_streamer(1010, 20, 'ServerIP', "10.230.240.138")
```

(note that the IP address must be a string with the string `"` commas!)

There are other options and error management involved, including input argument sanity checks and dynamic port assignment. You normally won't need to change anything besides the server IP. See the code for details.

### Getting the latest data

Once your object is created, you can get the following:

```Matlab
[unix_time, translation, quaternion, rigid_body_name] = my_fancy_rigid_body.get_latest
```

Which returns:

```Matlab
>> [unix_time, translation, quaternion, rigid_body_name] = my_fancy_rigid_body.get_latest

unix_time =

   1.7376e+12


translation =

    0.5760    0.0062    0.0606


quaternion =

   -0.1307    0.3228    0.0765    0.9343


rigid_body_name =

    '40mmBall'

>>
```

`unix_time` is the timestamp in milliseconds, `translation` is the X-Y-Z coordinates, `quaternion` is the Qx Qy Qz Qw orientation of the object. `rigid_body_name` is the name as it was specified in Motive.

### Getting the complete contents of the buffer

**IMPORTANT:** Do not rely on storing a lot of packets inside Matlab's UDP port receive buffer. Make sure you adjust the decimation to the least acceptable packet rate. This is also good for latency too. So ideally you shouldn't use this method at all, but it's here, just in case.

```Matlab
[unix_time, translation, quaternion, rigid_body_name] = my_fancy_rigid_body.get_everything
```

This is the same as `<your_object>.get_latest`, but the functions return every parsed packet in the buffer, and for N frames received, it returns:

 * `unix_time` as an N-element vector
 * `translation` as an Nx3 element matrix, with the X-Y-Z coordinates being in triplets
 * `quaternion` as an Xx4 element matrix, with the QxQyQzQw being in quadruplets
 * `rigid_body_name` as a string.

 ### Other controls

 You can tell the server to stop blasting packets at you by:

 ```Matlab
 my_fancy_rigid_body.stop
 ```

 Then, you can re-initialise it as you want. The object also has an event listener, and will automatically tell the server to stop streaming when you delete the object.