# C# streaming client examples

These are simple console apps. The way it sends a request to the server is the same, but there are differences on how these parse the replies. Essentially, thse are the building blocks for the Unity client.

## Intricacies that are worth mentioning

1. The regional settings are hard-coded to be `"en-US"`.
This is because the floating-point number format in the plain-text payload follows this convention.

2. The lengths of the split strings are checked in multiple levels
Just in case, because I ended up sending it garbage data and code crashed and burned everywhere. This is okay. The main separator character is `";"`, the coordinate separator character is `","`, and there is an extra `"\n"` character at the end of the payload, just in case if someone wants to dump them directly to a file.

3. `float.TryParse` statements implement some error management.
If something bad happens, the coordinates will be NaN.

### `see-sharp-streaming-client-example.cs`

Probably as simple as it can be. Everything is blocking execution. The loop will hang at `udp_client.Receive()` if nothing is coming in, and it will also hang at `Thread.Sleep()` too. Interestingly, even if I set the server to blast packets at this client at an unreasonable rate, it only returns the latest packet. At first, I suspected that `Encoding.ASCII.GetString()` plays up in the byte stream and it stops when it sees the very first `\n` character, but alas no. Even the received number of bytes in the buffer is only one packet's worth. In Matlab, where I have better control of the UDP functionality, it just fills the buffer and I have multiple packets until I read them out. Must be a Microsoft thing.

### `see-sharp-streaming-client-example-with-async.cs`

This is a truly non-blocking example where the output is timer-driven, and the reception of packet is event-driven.

For modularity, the request to the server is sent using `send_request()`. The `Main()` function sets up a timer and executes `display_function()` every 1000 milliseconds. This part is almost identical to the blocking streaming client example.

The nice part starts at `udp_client.BeginReceive(new AsyncCallback(receive_message), udp_client);`. So this line sets up `udp_client` to execute `receive_message` as soon as there is a packet coming in, and it passes itself as an input argument. Unlike the `Task` in C#, this is just another ordinary `static void` function, so I hope it will cause the least amount of disturbance in the Force. This function then receives the packet, obtains the data and pushes it into a byte[] buffer, from where I upload the contents into `received_payload_as_string`, which I can handle from wherever.

Scenarios:

* When we read faster when the data comes in, then it just uses the latest available data that was received by the network adapter. So probably no need for NaNs when the object disappears, it just sort of 'freezes' instead.

* When we read slower when the data comes in, then `receive_message()` is updating `received_payload_as_string` to whatever it just received. We read it out whenever read it out. Also, when there is no data available, it is possible to just make a `return` statement so nothing gets updated or broken.