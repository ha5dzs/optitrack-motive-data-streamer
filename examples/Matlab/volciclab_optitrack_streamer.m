% Matlab streamer class for the optitrack-motive-data-streamer.
% https://github.com/ha5dzs/optitrack-motive-data-streamer
%
% This had to be written because of NaturalPoint's support:
% [Internal iicket PNN-35]
% "Unfortunately, there have been no updates to the ticket or the
% underlying issue at this time. Matlab is a less frequently used
% platform among our user base, which has impacted our ability
% to prioritize this specific ticket."
% ...
% "While we do hope to revisit and improve the sample in the future,
% other major priorities and the fact that this has only been
% encountered by one user so far have placed this work low on our roadmap."
% These were the words of:
% Steven Andrews, Senior Director, Support Engineering, NaturalPoint
%
% In other words, I am on my own, and I make do with whatever I got.
%
%
% A bit of explanation here.
% There are two parts of this class.
% The constructor part. This bit:
%   -Creates the object the rest of the methods can communicate through
%   -Sends the initial request to the server
% The public methods part, which has a method to:
%   -Get the latest data
%   -Get every piece of data in the buffer
%   -Stop streaming
%   -Clean up before deletion

classdef volciclab_optitrack_streamer
    % Global variables to the class. Once I set these in the constructor, these will become read-only.
    properties (SetAccess = immutable)
        udp_object % This is the OS network socket interface
        rigid_body_id % This is set in Motive. From 0 to 65535
        decimation % Send packets every N frames. Set this to 1 to get every frame's data
        options % Override the default IP addresses and ports. See this file for default values.
    end
    % From: https://stackoverflow.com/questions/14057308/matlab-class-destructor-not-called-during-clear
    properties (Hidden)
        cleanup
    end
    % Operation variables for the class. get_latest() will update these.
    properties
        unix_time_stamp = 0; % Unix time when get_latest() was called the last time, in milliseconds.
        translation = [0, 0, 0]; % The whereabouts of the object, when get_latest() was called the last time.
        quaternion = [0, 0, 0, 1]; % The orientation of the object, when get_latest() was called the last time.
        rigid_body_name = 'dummy_placeholder'; % The name of the object, when get_latest() was called the last time.
    end

    methods
        %% Constructor function: Create the object and start the streaming.
        function object_to_be_put_out = volciclab_optitrack_streamer(rigid_body_id, decimation, options)
            % This is the constructor function. This creates the return object with all the data and settings.
            % At the very least, specify these input arguments:
            %   -rigid_body_id, which is a number between 0 and 65535, as you set it in Motive
            %   -decimation, which makes the streaming server send a packet to the client every N frames.
            %       This is useful to reduce network traffic. Say if you record at 200 Hz, but you just want to get a
            %       simple display that tells you where is the object, it's enough if you update it 10 times a second.
            %       In order to do this, just set the decimation to 20.
            % There are some optional arguments too. These are name-value pairs. For convenience, there are some default settings
            % Feel free to change this to your environment
            % These optional input arguments are:
            %   -'ServerIP', which is the IP address of the streaming server on your network in a string format. It is "192.168.42.5" by default.
            %   -'ServerPort', which is where the server is listening for instructions.
            %       By default, it is 24656. If you change this, change it on the server too. It is hard-coded.
            %   -'ReceivePort' is the UDP port where the streaming server sends data to. It starts from 64923,
            %       and keeps increasing when it finds that the ports are in use by something else.
            % Once the ebject is created, these are set as 'properties', and are immutable.
            % It also waits for the server to send the requested packet, and saves the rigid body information inside the object.

            % Begin processing the arguments
            arguments
               rigid_body_id (1, 1) {mustBeNumeric} % This is required, between 0 and 65535
               decimation (1, 1) {mustBeNumeric} % This is required, at least 1.
               options.ServerIP (1, 1) string = "192.168.42.5" % Specify this as a string to override the default IP of 192.168.42.5
               options.ServerPort (1, 1) {mustBeNumeric} = 64923 % Speficy this to override which server port the request should be sent to
               options.ReceivePort (1, 1) {mustBeNumeric} = 24656 % Specify this to override which port the server should send the data to
            end
            
            if(nargin < 2)
                error("volciclab_optitrack_streamer(): At the very least, specify the digid body ID and the decimation.")
            end
            
            % Sanity checks.
    
            % Sanity checks: Rigid body
            if(length(rigid_body_id) ~= 1)
               % Is this a single value?
               error("volciclab_optitrack_streamer(): The rigid body ID must be a single value.");
            end
            if(~isnumeric(rigid_body_id))
               % Is this a number?
               error("volciclab_optitrack_streamer(): The rigid body ID must be a number")
            end                
            if(round(rigid_body_id) ~= rigid_body_id)
               % Is this a round number?
               error("volciclab_optitrack_streamer(): The rigid body ID must be an integer.")
            end
            if( (rigid_body_id > 65536) || (rigid_body_id < 0) )
               % Is the value valid in range?
               error("volciclab_optitrack_streamer(): The rigid body ID must be between 0 and 65535.");
            end
            if(rigid_body_id == 0)
               % Is the origin marker being requested?
               warning("volciclab_optitrack_streamer(): You are requesting rigid body ID 0, which is hard-coded to tbe the origin marker.")
            end
            
            % Sanity checks: decimation
            if(length(decimation) ~= 1)
               % Is this a single value?
               error("volciclab_optitrack_streamer(): The decimation must be a single value.");
            end
            if(~isnumeric(decimation))
               % Is this a number?
               error("volciclab_optitrack_streamer(): The decimation value must be a number")
            end                
            if(round(decimation) ~= decimation)
               % Is this a round number?
               error("volciclab_optitrack_streamer(): The decimation value must be an integer.")
            end
            if(decimation < 1)
               % Is the value valid in range?
               error("volciclab_optitrack_streamer(): The decimation value must be at least 1. 0 means no data being sent.");
            end
    
            % Save these to the return object.
            object_to_be_put_out.rigid_body_id = rigid_body_id;
            object_to_be_put_out.decimation = decimation;
            object_to_be_put_out.options = options;
    
            % Create the UDP object.
            search_for_acceptable_port = true;
    
            while search_for_acceptable_port
                try
                    object_to_be_put_out.udp_object = udpport('byte', 'LocalPort', options.ReceivePort);
                    search_for_acceptable_port = false; % If this worked, then we can get out of the while loop
                    object_to_be_put_out.options.ReceivePort = options.ReceivePort; % Save the new port number.
                catch
                    % If the port didn't open due to it being already used, then increase the port number again.
                    options.ReceivePort = options.ReceivePort + 1;
                    search_for_acceptable_port = true; % If we got here, we need to repeat trying to create the UDP object.
    
                    if(options.ReceivePort > 65535)
                        error("volciclab_optitrack_streamer(): It seems that you ran out of ports that the UDP object can use.")
                    end
                end
    
            end
            % Now we have a working UDP object, and is assigned to the return value.
    
            % Send rigid body streaming request to the server
            udp_payload = sprintf('%d;%d;%d', rigid_body_id, options.ReceivePort, decimation);
            object_to_be_put_out.udp_object.flush; % % Empty the buffer, just in case.
            
            % ...Send the request
            write(object_to_be_put_out.udp_object, udp_payload, options.ServerIP, options.ServerPort);

            % This is an event listerer. If the object gets destroyed, it will send the server a command to stop streaming.
            object_to_be_put_out.cleanup = onCleanup(@()delete(object_to_be_put_out));

            % Wait for some data to come in.
            tic;
            throw_warning = false; % Bunch of semaphores.
            while(object_to_be_put_out.udp_object.NumBytesAvailable < 10)
                % If we got here, we are waiting for the streaming server to respond.
                elapsed_time = toc;
                if(throw_warning == false && elapsed_time > 5)
                    throw_warning = true;
                    warning("volciclab_optitrack_streamer(): No response from server in 5 seconds. If you use high decimation this may be expected, but you may have a network problem.")
                end
            end

            % Fill up the temporary data.
            [object_to_be_put_out.unix_time_stamp, object_to_be_put_out.translation, object_to_be_put_out.quaternion, object_to_be_put_out.rigid_body_name] = get_latest(object_to_be_put_out);

        end
        %% Stop function.
        function stop(volciclab_optitrack_streamer)
            % This function tells the streaming server to stop sending packets.
            udp_payload = sprintf('%d;%d;0', volciclab_optitrack_streamer.rigid_body_id, volciclab_optitrack_streamer.options.ReceivePort);
            volciclab_optitrack_streamer.udp_object.flush; % Empty the buffer, just in case.
    
            % Send an UDP packet.
            write(volciclab_optitrack_streamer.udp_object, udp_payload, volciclab_optitrack_streamer.options.ServerIP, volciclab_optitrack_streamer.options.ServerPort);
        end

        function delete(volciclab_optitrack_streamer)
            % This function is the same as the stop() function, but it automatically gets executed when the class gets destroyed.
            udp_payload = sprintf('%d;%d;0', volciclab_optitrack_streamer.rigid_body_id, volciclab_optitrack_streamer.options.ReceivePort);
            volciclab_optitrack_streamer.udp_object.flush; % Empty the buffer, just in case.
    
            % Send an UDP packet.
            write(volciclab_optitrack_streamer.udp_object, udp_payload, volciclab_optitrack_streamer.options.ServerIP, volciclab_optitrack_streamer.options.ServerPort);
        end
        %% Get latest data.
        function [unix_time_stamp, translation, quaternion, rigid_body_name] = get_latest(volciclab_optitrack_streamer)
            % This function gets the latest rigid body data available.
            % Returns:
            %   - The current Unix time, with millisecond precision.
            %   - The translation coordinates, in X-Y-Z format as a vector, with the units being in metres.
            %   - The rotation quaternion, in the Qx Qy Qz Qw format, as a vector.
            %   - The name of the rigid body, as it was set in Motive.
            % After the execution, it flushes the buffer. This meanss that:
            %   - Any data before the latest is being lost
            %   - If there is nothing available in the buffer, the function will just return without doing anything.
            % IMPORTANT:
            %   When you call this function more often than how fast packets are coming in,
            %   it will return the latest received data. You will not get any warnings about this.
            %   So if your rigid body gets stuck in the same position suddenly, you may have a network problem.
            if(volciclab_optitrack_streamer.udp_object.NumBytesAvailable > 10) % At least something should be in here, right?
                received_data = read(volciclab_optitrack_streamer.udp_object, volciclab_optitrack_streamer.udp_object.NumBytesAvailable, 'string');
            else
                % This is a fallback mode. If nothing is received, then return whatever was saved interally.
                unix_time_stamp = volciclab_optitrack_streamer.unix_time_stamp;
                translation = volciclab_optitrack_streamer.translation;
                quaternion = volciclab_optitrack_streamer.quaternion;
                rigid_body_name = volciclab_optitrack_streamer.rigid_body_name;
                return
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
            
            most_recent_packet = incoming_packets(most_recent_packet_index);

            

            % We now parse the latest packet.
            separated_received_string = split(most_recent_packet, ";");

            unix_time_stamp = str2num(separated_received_string{1});
            rigid_body_id_as_received = str2num(separated_received_string{2});

            % Added safeguard:
            if(volciclab_optitrack_streamer.rigid_body_id ~= rigid_body_id_as_received)
                fprintf(2, "\nSomething is wrong with the incoming packets!");
                fprintf(1, "This object is for rigid body %d. Instead, the packet's content is for rigid body ID %d", volciclab_optitrack_streamer.rigid_body_id, rigid_body_id_as_received)
                error("volciclab_optitrack_streamer::get_latest(): Rigid body ID mismatch.")
            end

            rigid_body_name = strip(separated_received_string{5}, newline); % remove the newline
    
            translation_coords_as_string_array = split(separated_received_string{3}, ",");
            translation = [str2double(translation_coords_as_string_array{1}), ...
                            str2double(translation_coords_as_string_array{2}), ...
                            str2double(translation_coords_as_string_array{3})];
    
            quaternion_as_string_array = split(separated_received_string{4}, ",");
    
            quaternion = [str2double(quaternion_as_string_array{1}), ...
                          str2double(quaternion_as_string_array{2}), ...
                          str2double(quaternion_as_string_array{3}), ...
                          str2double(quaternion_as_string_array{4})];


            % Save this to the object too, so this will be the latest.
            volciclab_optitrack_streamer.unix_time_stamp = unix_time_stamp;
            volciclab_optitrack_streamer.translation = translation;
            volciclab_optitrack_streamer.quaternion = quaternion;
            volciclab_optitrack_streamer.rigid_body_name = rigid_body_name;
    
            %fprintf("Parsed rigid body details are:\n")
            %fprintf("Date of sending: %s.%s\n", datetime(unix_time_stamp/1000, 'ConvertFrom', 'posixtime', 'TimeZone', 'Asia/Dubai'), separated_received_string{1}(end-2:end));
            %fprintf("\tID: %d, Name: %s\n", rigid_body_id, rigid_body_name)
            %fprintf("\tTranslation coordinates: %0.6f, %0.6f, %0.6f\n", ...
            %        translation(1), translation(2), translation(3));
            %fprintf("\tQuaternion: %0.6f, %0.6f, %0.6f, %0.6f\n", ...
            %        quaternion(1), quaternion(2), quaternion(3), quaternion(4));



        end

        %% Get everything.
        function [unix_time_stamp, translation, quaternion, rigid_body_name] = get_everything(volciclab_optitrack_streamer)
            % This function gets every piece of rigid body data in the buffer.
            % Returns:
            %   - The current Unix time, as an N-element vector for all the N packets, with millisecond precision.
            %   - The translation coordinates, in X-Y-Z format as 3-by-N matrix, for all the N packets. Units are metres.
            %   - The rotation quaternion, in the Qx Qy Qz Qw format, as a 4-by-N matrix, for all the N packets.
            %   - The name of the rigid body, as it was set in Motive.
            % After the execution, it flushes the buffer. This means that if there is nothing available in the buffer,
            % the function will just return without doing anything.
            % IMPORTANT:
            %   When you call this function more often than how fast packets are coming in,
            %   it will return the latest received data. You will not get any warnings about this.
            %   So if your rigid body gets stuck in the same position suddenly, you may have a network problem.
           
            if(volciclab_optitrack_streamer.udp_object.NumBytesAvailable > 10) % At least something should be in here, right?
                received_data = read(volciclab_optitrack_streamer.udp_object, volciclab_optitrack_streamer.udp_object.NumBytesAvailable, 'string');
            else
                % This is a fallback mode. If nothing is received, then return whatever was saved interally.
                unix_time_stamp = volciclab_optitrack_streamer.unix_time_stamp;
                translation = volciclab_optitrack_streamer.translation;
                quaternion = volciclab_optitrack_streamer.quaternion;
                rigid_body_name = volciclab_optitrack_streamer.rigid_body_name;
                return
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
            

            %Preallocate output variables here.
            unix_time_stamp = zeros(1, no_of_packets_in_buffer);
            translation = zeros(no_of_packets_in_buffer, 3);
            quaternion = zeros(no_of_packets_in_buffer, 4);

            for i = 1:no_of_packets_in_buffer
                % We now parse the latest packet.
                separated_received_string = split(incoming_packets(i), ";");
    
                unix_time_stamp(i) = str2num(separated_received_string{1});
                rigid_body_id_as_received = str2num(separated_received_string{2});
    
                % Added safeguard:
                if(volciclab_optitrack_streamer.rigid_body_id ~= rigid_body_id_as_received)
                    fprintf(2, "\nSomething is wrong with the incoming packets!");
                    fprintf(1, "This object is for rigid body %d. Instead, the packet's content is for rigid body ID %d", volciclab_optitrack_streamer.rigid_body_id, rigid_body_id_as_received)
                    error("volciclab_optitrack_streamer::get_latest(): Rigid body ID mismatch.")
                end
    
                % I don't really care if this one stays in the loop.
                rigid_body_name = strip(separated_received_string{5}, newline); % remove the newline
        
                translation_coords_as_string_array = split(separated_received_string{3}, ",");
                translation(i, :) = [str2double(translation_coords_as_string_array{1}), ...
                                str2double(translation_coords_as_string_array{2}), ...
                                str2double(translation_coords_as_string_array{3})];
        
                quaternion_as_string_array = split(separated_received_string{4}, ",");
        
                quaternion(i, :) = [str2double(quaternion_as_string_array{1}), ...
                              str2double(quaternion_as_string_array{2}), ...
                              str2double(quaternion_as_string_array{3}), ...
                              str2double(quaternion_as_string_array{4})];
    
            end
    
                    

        end
    end
    
end



