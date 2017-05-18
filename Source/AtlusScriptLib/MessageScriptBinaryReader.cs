﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using AtlusScriptLib.Common.IO;

namespace AtlusScriptLib
{
    public sealed class MessageScriptBinaryReader : IDisposable
    {
        private bool mDisposed;
        private long mPositionBase;
        private EndianBinaryReader mReader;
        private MessageScriptBinaryFormatVersion mVersion;

        public MessageScriptBinaryReader(Stream stream, MessageScriptBinaryFormatVersion version)
        {
            mPositionBase = stream.Position;
            mReader = new EndianBinaryReader(stream, version.HasFlag(MessageScriptBinaryFormatVersion.BE) ? Endianness.BigEndian : Endianness.LittleEndian);
            mVersion = version == MessageScriptBinaryFormatVersion.Unknown ? MessageScriptBinaryFormatVersion.V1 : version;
        }

        public MessageScriptBinary ReadBinary()
        {
            var binary = new MessageScriptBinary();

            binary.mHeader              = ReadHeader();
            binary.mMessageHeaders      = ReadMessageHeaders(binary.mHeader.MessageCount);
            binary.mSpeakerTableHeader  = ReadSpeakerTableHeader();
            binary.mFormatVersion       = mVersion;

            return binary;
        }

        public MessageScriptBinaryHeader ReadHeader()
        {
            MessageScriptBinaryHeader header = new MessageScriptBinaryHeader();

            // Check if the stream isn't too small to be a proper file
            if (mReader.BaseStreamLength < MessageScriptBinaryHeader.SIZE)
            {
                throw new InvalidDataException("Stream is too small to be valid");
            }
            else
            {
                header.FileType                 = mReader.ReadByte();
                header.IsCompressed             = mReader.ReadByte() != 0;
                header.UserId                   = mReader.ReadInt16();
                header.FileSize                 = mReader.ReadInt32();
                header.Magic                    = mReader.ReadBytes(4);
                header.Field0C                  = mReader.ReadInt32();
                header.RelocationTable.Address  = mReader.ReadInt32();
                header.RelocationTableSize      = mReader.ReadInt32();
                header.MessageCount             = mReader.ReadInt32();
                header.IsRelocated              = mReader.ReadInt16() != 0;
                header.Field1E                  = mReader.ReadInt16();

                // swap endianness
                if (header.Magic.SequenceEqual(MessageScriptBinaryHeader.MAGIC_LE))
                {
                    if (mVersion.HasFlag(MessageScriptBinaryFormatVersion.BE))
                    {
                        SwapHeader(ref header);
                        mVersion ^= MessageScriptBinaryFormatVersion.BE;
                        mReader.Endianness = Endianness.LittleEndian;
                    }
                }
                else if (header.Magic.SequenceEqual(MessageScriptBinaryHeader.MAGIC_BE))
                {
                    if (!mVersion.HasFlag(MessageScriptBinaryFormatVersion.BE))
                    {
                        SwapHeader(ref header);
                        mVersion |= MessageScriptBinaryFormatVersion.BE;
                        mReader.Endianness = Endianness.BigEndian;
                    }
                }
                else
                {
                    throw new InvalidDataException("Header magic value does not match");
                }

                mReader.PushPositionSeekBegin(mPositionBase + header.RelocationTable.Address);
                header.RelocationTable.Value = mReader.ReadBytes(header.RelocationTableSize);
                mReader.PopPosition();
            }

            return header;
        }

        private void SwapHeader(ref MessageScriptBinaryHeader header)
        {
            EndiannessHelper.Swap(ref header.UserId);
            EndiannessHelper.Swap(ref header.FileSize);
            EndiannessHelper.Swap(ref header.Field0C);
            EndiannessHelper.Swap(ref header.RelocationTable.Address);
            EndiannessHelper.Swap(ref header.RelocationTableSize);
            EndiannessHelper.Swap(ref header.MessageCount);
            EndiannessHelper.Swap(ref header.Field1E);
        }

        public MessageScriptBinaryMessageHeader[] ReadMessageHeaders(int count)
        {
            MessageScriptBinaryMessageHeader[] messageHeaders = new MessageScriptBinaryMessageHeader[count];

            for (int i = 0; i < messageHeaders.Length; i++)
            {
                ref var messageHeader = ref messageHeaders[i];
                messageHeader.MessageType = (MessageScriptBinaryMessageType)mReader.ReadInt32();
                messageHeader.Message.Address = mReader.ReadInt32();
                messageHeader.Message.Value = ReadMessage(messageHeader.MessageType, messageHeader.Message.Address);
            }

            return messageHeaders;
        }

        public MessageScriptBinarySpeakerTableHeader ReadSpeakerTableHeader()
        {
            MessageScriptBinarySpeakerTableHeader header;

            header.SpeakerNameArray.Address = mReader.ReadInt32();
            header.SpeakerCount = mReader.ReadInt32();
            header.Field08 = mReader.ReadInt32();
            header.Field0C = mReader.ReadInt32();
            header.SpeakerNameArray.Value = ReadSpeakerNames(header.SpeakerNameArray.Address, header.SpeakerCount);

            if (header.Field08 != 0)
                Debug.WriteLine($"{nameof(MessageScriptBinarySpeakerTableHeader)}.{nameof(header.Field08)} = {header.Field08}");

            if (header.Field0C != 0)
                Debug.WriteLine($"{nameof(MessageScriptBinarySpeakerTableHeader)}.{nameof(header.Field0C)} = {header.Field0C}");

            return header;
        }

        public AddressValuePair<string>[] ReadSpeakerNames(int address, int count)
        {
            mReader.SeekBegin(mPositionBase + MessageScriptBinaryHeader.SIZE + address);

            var speakerNameAddresses = mReader.ReadInt32s(count);
            var speakerNames = new AddressValuePair<string>[count];

            for (int i = 0; i < speakerNameAddresses.Length; i++)
            {
                ref int speakerNameAddress = ref speakerNameAddresses[i];

                mReader.SeekBegin(mPositionBase + MessageScriptBinaryHeader.SIZE + speakerNameAddress);
                string name = mReader.ReadString(StringBinaryFormat.NullTerminated);

                speakerNames[i] = new AddressValuePair<string>(speakerNameAddress, name);
            }

            return speakerNames;
        }

        private object ReadMessage(MessageScriptBinaryMessageType type, int address)
        {
            object message;

            mReader.PushPositionSeekBegin(mPositionBase + MessageScriptBinaryHeader.SIZE + address);

            switch (type)
            {
                case MessageScriptBinaryMessageType.Dialogue:
                    message = ReadDialogueMessage();
                    break;

                case MessageScriptBinaryMessageType.Selection:
                    message = ReadSelectionMessage();
                    break;

                default:
                    throw new InvalidDataException($"Unknown message type: {type}");
            }

            mReader.PopPosition();

            return message;
        }

        public MessageScriptBinaryDialogueMessage ReadDialogueMessage()
        {
            MessageScriptBinaryDialogueMessage message;

            message.Identifier          = mReader.ReadString(StringBinaryFormat.FixedLength, MessageScriptBinaryDialogueMessage.IDENTIFIER_LENGTH);
            message.LineCount           = mReader.ReadInt16();
            message.SpeakerId           = mReader.ReadInt16();
            message.LineStartAddresses  = mReader.ReadInt32s(message.LineCount);
            message.TextBufferSize      = mReader.ReadInt32();
            message.TextBuffer          = mReader.ReadBytes(message.TextBufferSize);

            return message;
        }

        public MessageScriptBinarySelectionMessage ReadSelectionMessage()
        {
            MessageScriptBinarySelectionMessage message;

            message.Identifier              = mReader.ReadString(StringBinaryFormat.FixedLength, MessageScriptBinaryDialogueMessage.IDENTIFIER_LENGTH);
            message.Field18                 = mReader.ReadInt16();
            message.OptionCount             = mReader.ReadInt16();
            message.Field1C                 = mReader.ReadInt16();
            message.Field1E                 = mReader.ReadInt16();
            message.OptionStartAddresses    = mReader.ReadInt32s(message.OptionCount);
            message.TextBufferSize          = mReader.ReadInt32();
            message.TextBuffer              = mReader.ReadBytes(message.TextBufferSize);

            if (message.Field18 != 0)
                Debug.WriteLine($"{nameof(MessageScriptBinarySelectionMessage)}.{nameof(message.Field18)} = {message.Field18}");

            if (message.Field1C != 0)
                Debug.WriteLine($"{nameof(MessageScriptBinarySelectionMessage)}.{nameof(message.Field1C)} = {message.Field1C}");

            if (message.Field1E != 0)
                Debug.WriteLine($"{nameof(MessageScriptBinarySelectionMessage)}.{nameof(message.Field1E)} = {message.Field1E}");

            return message;
        }

        public void Dispose()
        {
            if (mDisposed)
                return;

            mReader.Dispose();

            mDisposed = true;
        }
    }
}