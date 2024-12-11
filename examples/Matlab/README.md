# `udp_send_and_receive_test.m`

This was made in R2024a, with the 'new' `udpport` function.

Please update the IP address to wherever your system is.

Effectively, it compiles the string to send, and waits for the incoming packets in an infinite loop. There are some string bashing involved too, where the fields are split with the different separator characters. At the end of the packet, there is a `0x0A` byte to mark where packets end just in case you get some buffer overflow.