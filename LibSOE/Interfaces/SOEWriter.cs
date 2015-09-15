﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SOE
{
    public class SOEWriter
    {
        // Message details
        private ushort OpCode;
        private bool IsMessage;

        // Message
        private List<byte> Data;

        public SOEWriter()
        {
            // Message information
            Data = new List<byte>();
            OpCode = 0;

            // We're some kinda data, not a message or packet..
            IsMessage = false;
        }

        public SOEWriter(ushort opCode, bool isMessage=false)
        {
            // Message information
            Data = new List<byte>();
            IsMessage = isMessage;
            OpCode = opCode;

            // Add our OpCode
            if (IsMessage)
            {
                AddHostUInt16(opCode);
            }
            else
            {
                AddUInt16(opCode);
            }
        }

        public SOEWriter(SOEPacket packet)
        {
            // Message information
            Data = new List<byte>(packet.Raw);
            OpCode = packet.OpCode;

            // We're a packet, not a message
            IsMessage = false;
        }

        public SOEWriter(SOEMessage message)
        {
            // Message information
            Data = new List<byte>(message.Raw);
            OpCode = message.OpCode;

            // We're a message!
            IsMessage = true;
        }

        public void AddByte(byte value)
        {
            Data.Add(value);
        }

        public void AddBytes(byte[] value)
        {
            foreach (byte b in value)
            {
                Data.Add(b);
            }
        }

        public void AddUInt16(ushort value)
        {
            byte[] Message = BitConverter.GetBytes(value).Reverse<byte>().ToArray<byte>();
            AddBytes(Message);
        }

        public void AddUInt32(uint value)
        {
            byte[] Message = BitConverter.GetBytes(value).Reverse<byte>().ToArray<byte>();
            AddBytes(Message);
        }

        public void AddInt16(short value)
        {
            byte[] Message = BitConverter.GetBytes(value).Reverse<byte>().ToArray<byte>();
            AddBytes(Message);
        }

        public void AddInt32(int value)
        {
            byte[] Message = BitConverter.GetBytes(value).Reverse<byte>().ToArray<byte>();
            AddBytes(Message);
        }

        public void AddHostUInt16(ushort value)
        {
            byte[] Message = BitConverter.GetBytes(value).ToArray<byte>();
            AddBytes(Message);
        }

        public void AddHostUInt32(uint value)
        {
            byte[] Message = BitConverter.GetBytes(value).ToArray<byte>();
            AddBytes(Message);
        }

        public void AddNullTerminatedString(string value)
        {
            value += (char)0x0;
            byte[] Message = Encoding.ASCII.GetBytes(value);
            AddBytes(Message);
        }

        public void AddBoolean(bool value)
        {
            byte v = (byte)(value == true ? 0x1 : 0x0);
            AddByte(v);
        }

        public void AddMessage(SOEMessage message)
        {
            if (IsMessage)
            {
                // Handle multi messages
                if (OpCode == (ushort)SOEOPCodes.MULTI_MESSAGE)
                {
                    if (message.OpCode == (ushort)SOEOPCodes.MULTI_MESSAGE)
                    {
                        // Setup a reader
                        SOEReader reader = new SOEReader(message);

                        // Get the messages and add them
                        byte[] messages = reader.ReadBytes(message.Raw.Length - 2);
                        AddBytes(messages);
                    }
                    else
                    {
                        // Get the size of the message
                        int size = message.Raw.Length;

                        // Is the size bigger than 255?
                        if (size > 0xFF)
                        {
                            // Do the stupid >255 thing
                            AddByte(0xFF);
                            size -= 0xFF;
                            
                            // Get how many bytes to add
                            byte toAdd = (byte)((size / 0xFF) + (size % 0xFF) & 0xFF);
                            AddByte(toAdd);

                            // Add sizes until we're at a value of 0
                            while (size > 0)
                            {
                                // Do we not want to add 0xFF?
                                if (size < 0xFF)
                                {
                                    // Add the rest of the size
                                    AddByte((byte)size);
                                    size = 0;
                                }
                                else
                                {
                                    // Add 0xFF
                                    AddByte(0xFF);
                                    size -= 0xFF;
                                }
                            }
                        }
                        else
                        {
                            // Just do the regular size adding
                            AddByte((byte)(size & 0xFF));
                        }

                        // Add the actual message
                        AddBytes(message.Raw);
                    }
                }
            }
            else
            {
                // Just add the message
                AddBytes(message.Raw);
            }
        }

        public SOEPacket GetFinalSOEPacket(SOEClient client, bool compressed, bool appendCRC)
        {
            // Data
            byte[] originalPacket = GetRaw();
            byte[] rawData = new byte[Data.Count - 2];
            byte[] newPacket;

            // Fail-safe
            ushort originalOpCode = 0;

            // Are we a message?
            if (IsMessage)
            {
                // Yes, so we'll try make a data packet.
                // Can we fit into one packet?
                SOEMessage message = GetFinalSOEMessage(client);
                if (message.IsFragmented)
                {
                    // We're gonna have to fragment, so we can't handle this gracefully...
                    client.Server.Log("[ERROR] Tried to handle 'GetFinalSOEPacket' call on written SOEMessage gracefully but failed due to fragmentation. Returning null.");
                    client.Server.Log("[INFO] Call 'GetFinalSOEMessage' as it deals with fragmentation!");

                    // Welp, goodbye world! :'(
                    return null;
                }

                // Make the new packet
                Data = new List<byte>();

                // Add the packet's arguments
                AddUInt16(client.GetNextSequenceNumber());
                AddBytes(originalPacket);
                rawData = GetRaw();

                // Change our OpCode so that we're a reliable data packet
                originalOpCode = OpCode;
                OpCode = (ushort)SOEOPCodes.RELIABLE_DATA;

                // Because we're reliable data, take compression into consideration and append a CRC
                compressed = true;
                appendCRC = true;

                // We handled it gracefully! :)
                client.Server.Log("[INFO] Handled 'GetFinalSOEPacket' call on written SOEMessage gracefully.");
            }
            else
            {
                // Get just the data for this packet. (Remove the OP Code)
                byte[] completeRawData = GetRaw();
                for (int i = 2; i < completeRawData.Length; i++)
                {
                    rawData[i - 2] = completeRawData[i];
                }
            }

            // Start a new packet
            Data = new List<byte>();
            AddUInt16(OpCode);

            // Are we compressable?
            if (client.IsCompressable())
            {
                if (compressed)
                {
                    AddBoolean(rawData.Length > 100);
                    if (rawData.Length > 100)
                    {
                        rawData = client.Compress(rawData);
                    }
                }
            }

            // Are we encrypted?
            if (client.IsEncrypted())
            {
                //  Encrypt the SOE Packet
                rawData = client.Encrypt(rawData);
            }

            // Add the raw data
            AddBytes(rawData);

            // Appended CRC32?
            if (appendCRC)
            {
                AddBytes(client.GetAppendedCRC32(GetRaw()));
            }

            // Set our new packet
            newPacket = GetRaw();

            // Get our old message before compression/encryption
            Data = new List<byte>(originalPacket);

            // If we are a message, set our OpCode back
            if (IsMessage)
            {
                // Set our OpCode back too..
                OpCode = originalOpCode;
            }

            // Return the compressed/encrypted packet
            return new SOEPacket(OpCode, newPacket);
        }

        public SOEMessage GetFinalSOEMessage(SOEClient client)
        {
            // Are we a packet?
            if (!IsMessage)
            {
                // Yes, and there really isn't a nice way to deal with this..
                client.Server.Log("[ERROR] Tried Calling 'GetFinalSOEMessage' on written SOEPacket. Returning null.");

                // Welp, goodbye world! :'(
                return null;
            }

            // Make our message
            SOEMessage message = new SOEMessage(OpCode, GetRaw());

            // Does this message have to be fragmented?
            // We do - 10 for saftey when making the packets
            if (Data.Count > client.GetBufferSize() - 10)
            {
                // Setup a reader and keep track of our size
                SOEReader reader = new SOEReader(GetRaw());
                int size = message.Raw.Length;

                // While there are fragments to be added..
                while (size > 0)
                {
                    // Store the next fragment
                    byte[] raw;

                    // Is this fragment going to be smaller than the buffer size?
                    if (size < client.GetBufferSize() - 10)
                    {
                        raw = reader.ReadBytes(size);
                        size = 0;
                    }
                    else
                    {
                        raw = reader.ReadBytes((int)client.GetBufferSize() - 10);
                        size -= (int)client.GetBufferSize() - 10;
                    }
                    
                    // Add the finalized fragment
                    message.AddFragment(raw);
                }

                // Fragmented!
                message.IsFragmented = true;
            }

            // Return the message we made
            return message;
        }

        public byte[] GetRaw()
        {
            return Data.ToArray();
        }
    }
}
