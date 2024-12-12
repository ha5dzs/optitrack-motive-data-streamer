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