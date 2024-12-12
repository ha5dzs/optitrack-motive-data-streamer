% This script intentionally allows the filling of the receive UDP buffer, and chooses the freshest packet available.
clear;
clc;

%% The sending bit.

destination_ip = "127.0.0.1";

rigid_body_id_we_want = 2501;
destination_port = 64923;
receive_port = 64922;
decimation = 198;

% Create the udp object
udp_object = udpport('byte', 'LocalPort', receive_port);

% Example request: <RigidBodyID>,<SendToWhichPort>,<Decimation>
% If port is 0, stop streaming.
udp_payload = sprintf('%d;%d;%d', rigid_body_id_we_want, receive_port, decimation);
udp_object.flush; % Empty the buffer, just in case.

% Send an UDP packet.
write(udp_object, udp_payload, destination_ip, destination_port);

%% The receiving bit. Execute this separately, occasionally.

% If there is in the buffer, save it, and then flush it.
if(udp_object.NumBytesAvailable > 1)
    received_data = read(udp_object, udp_object.NumBytesAvailable, 'string');
    clc;

    %fprintf("Received string is: %s\n", received_data); % This prints the string
else
    error('There was no data in the buffer.')
end
% Create a vector of strings.
incoming_packets = splitlines(received_data);

% There is an extra newline at the end
no_of_packets_in_buffer = length(incoming_packets) - 1;

% The packets may not be in order. This is a UDP thing.
% In order to choose the latest one, we can cheat.
% Since we use Unix time, and the first digit is 1, then we know that
% the first number of chatacters is 13, and is basically fixed.
% This will change in unix time 10000000000, which translates to
% Saturday, November 20, 2286 5:46:40 PM GMT.
% If you are using this code 240 years from now, then I apologise for messing this up.
% Otherwise:

timestamp_array = zeros(no_of_packets_in_buffer, 1); % Preallocate for speed.
for i = 1:no_of_packets_in_buffer
    single_frame = char(incoming_packets(i));
    % Because the unix timestamp with milliseconds is 13 digits long
    timestamp_array(i) = str2double(single_frame(1:13));
    clear single_frame % This is a bit of a hack, but I had to convert from string to character array.
end

% Now we can calculate which timestamp is the largest, i.e. the most recent packet.
most_recent_packet_index = find(timestamp_array==max(timestamp_array));

most_recent_packet = incoming_packets(most_recent_packet_index)