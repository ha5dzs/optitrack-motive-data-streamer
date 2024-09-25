% This code just sends some UDP data.
clear;
clc;

destination_ip = "192.168.42.5"; % Our Motive computer. Yours will be different.

rigid_body_id_we_want = 2501;
destination_port = 64923;
receive_port = 64922;
decimation = 200;

% Create the udp object
udp_object = udpport('byte', 'LocalPort', receive_port);

% Example request: <RigidBodyID>,<SendToWhichPort>,<Decimation>
% If port is 0, stop streaming.
udp_payload = sprintf('%d;%d;%d', rigid_body_id_we_want, receive_port, decimation);
udp_object.flush; % Empty the buffer, just in case.

% Send an UDP packet.
write(udp_object, udp_payload, destination_ip, destination_port);

while(1)
    pause(0.2);
    % Receive a response, if there is anything in the buffer
    if(udp_object.NumBytesAvailable)
        received_data = read(udp_object, udp_object.NumBytesAvailable, 'string');
        udp_object.flush; % Empty the buffer, just in case.
        clc;

        fprintf("Received string is: %s\n", received_data); % This prints the string

        % Separate the received data
        separated_received_string = split(received_data, ";");

        rigid_body_id = str2num(separated_received_string{1});
        rigid_body_name = strip(separated_received_string{4}, char(10)); % remove the newline

        translation_coords_as_string_array = split(separated_received_string{2}, ",");
        translation = [str2double(translation_coords_as_string_array{1}), ...
                        str2double(translation_coords_as_string_array{2}), ...
                        str2double(translation_coords_as_string_array{3})];

        quaternion_as_string_array = split(separated_received_string{3}, ",");

        quaternion = [str2double(quaternion_as_string_array{1}), ...
                      str2double(quaternion_as_string_array{2}), ...
                      str2double(quaternion_as_string_array{3}), ...
                      str2double(quaternion_as_string_array{4})];

        fprintf("Parsed rigid body details are:\n\tID: %d, Name: %s\n", rigid_body_id, rigid_body_name)
        fprintf("\tTranslation coordinates: %0.6f, %0.6f, %0.6f\n", ...
                translation(1), translation(2), translation(3));
        fprintf("\tQuaternion: %0.6f, %0.6f, %0.6f, %0.6f\n", ...
                quaternion(1), quaternion(2), quaternion(3), quaternion(4));
    end
end