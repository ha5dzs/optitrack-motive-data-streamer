# C# streaming client example

This code is a simple console app. It sends a request to the server, and parses the replies. It also checks whether the received packet is for the same rigid body that was requested. It could probably do with some more error management, but this is just a simple-to-understand template.

## Intricacies that are worth mentioning

1. The regional settings are hard-coded to be `"en-US"`.
This is because the floating-point number format in the plain-text payload follows this convention.

2. The lengths of the split strings are checked in multiple levels
Just in case, because I ended up sending it garbage data and code crashed and burned everywhere. This is okay. The main separator character is `";"`, the coordinate separator character is `","`, and there is an extra `"\n"` character at the end of the payload, just in case if someone wants to dump them directly to a file.

3. `float.TryParse` statements implement some error management.
If something bad happens, the coordinates will be NaN.