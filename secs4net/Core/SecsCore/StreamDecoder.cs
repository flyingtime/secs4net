﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Secs4Net
{
    /// <summary>
    ///  Stream based HSMS/SECS-II message decoder
    /// </summary>
    sealed class StreamDecoder
    {
        public byte[] Buffer => _buffer;
        public int BufferOffset => _BufferOffset;
        public int BufferCount => Buffer.Length - _BufferOffset;

        /// <summary>
        /// decoder step
        /// </summary>
        /// <param name="length"></param>
        /// <param name="need"></param>
        /// <returns>pipeline decoder index</returns>
        delegate int Decoder(ref int length, out int need);

        /// <summary>
        /// decode pipelines
        /// </summary>
        readonly Decoder[] _decoders;
        int _decoderStep;

        readonly Action<MessageHeader, SecsMessage> _dataMsgHandler;
        readonly Action<MessageHeader> _controlMsgHandler;

        /// <summary>
        /// data buffer
        /// </summary>
        byte[] _buffer;

        /// <summary>
        /// Control the range of data decoder
        /// </summary>
        int _decodeIndex;

        /// <summary>
        /// Control the range of data receiver 
        /// </summary>
        int _BufferOffset;

        /// <summary>
        /// previous decoded remained count
        /// </summary>
        int _previousRemainedCount;

        readonly Stack<List<Item>> _stack = new Stack<List<Item>>();
        uint _messageDataLength;
        MessageHeader _msgHeader;
        readonly byte[] _itemLengthBytes = new byte[4];
        SecsFormat _format;
        byte _lengthBits;
        int _itemLength;

        public void Reset()
        {
            _stack.Clear();
            _decoderStep = 0;
            _decodeIndex = 0;
            _BufferOffset = 0;
            _messageDataLength = 0;
            _previousRemainedCount = 0;
        }

        internal StreamDecoder(int streamBufferSize, Action<MessageHeader> controlMsgHandler, Action<MessageHeader, SecsMessage> dataMsgHandler)
        {
            _buffer = new byte[streamBufferSize];
            _BufferOffset = 0;
            _decodeIndex = 0;
            _dataMsgHandler = dataMsgHandler;
            _controlMsgHandler = controlMsgHandler;

            _decoders = new Decoder[]{
                    // 0: get total message length 4 bytes
                    (ref int length, out int need) =>
                    {
                       if (!CheckAvailable(ref length, 4, out need)) return 0;

                       Array.Reverse(_buffer, _decodeIndex, 4);
                       _messageDataLength = BitConverter.ToUInt32(_buffer, _decodeIndex);
                       Trace.WriteLine($"Get Message Length: {_messageDataLength}");
                       _decodeIndex += 4;
                       length -= 4;
                       return 1;
                    },
                    // 1: get message header 10 bytes
                    (ref int length, out int need) =>
                    {
                        if (!CheckAvailable(ref length, 10, out need)) return 1;

                        _msgHeader = new MessageHeader(_buffer, _decodeIndex);
                        _decodeIndex += 10;
                        _messageDataLength -= 10;
                        length -= 10;
                        if (_messageDataLength == 0)
                        {
                            if (_msgHeader.MessageType == MessageType.DataMessage)
                                _dataMsgHandler(_msgHeader, new SecsMessage(_msgHeader.S, _msgHeader.F, _msgHeader.ReplyExpected, string.Empty));
                            else
                                _controlMsgHandler(_msgHeader);
                            return 0;
                        }

                        if (length >= _messageDataLength)
                        {
                            Trace.WriteLine("Get Complete Data Message with total data");
                            _dataMsgHandler(_msgHeader, new SecsMessage(_msgHeader.S, _msgHeader.F, _msgHeader.ReplyExpected, _buffer, ref _decodeIndex));
                            length -= (int)_messageDataLength;
                            _messageDataLength = 0;
                            return 0; //completeWith message received
                        }
                        return 2;
                    },
                    // 2: get _format + lengthBits(2bit) 1 byte
                    (ref int length, out int need) =>
                    {
                        if (!CheckAvailable(ref length, 1, out need)) return 2;

                        _format = (SecsFormat)(_buffer[_decodeIndex] & 0xFC);
                        _lengthBits = (byte)(_buffer[_decodeIndex] & 3);
                        _decodeIndex++;
                        _messageDataLength--;
                        length--;
                        return 3;
                    },
                    // 3: get _itemLength _lengthBits bytes, at most 3 byte
                    (ref int length,out int need) =>
                    {
                        if (!CheckAvailable(ref length, _lengthBits, out need)) return 3;

                        Array.Copy(_buffer, _decodeIndex, _itemLengthBytes, 0, _lengthBits);
                        Array.Reverse(_itemLengthBytes, 0, _lengthBits);

                        _itemLength = BitConverter.ToInt32(_itemLengthBytes, 0);
                        Array.Clear(_itemLengthBytes, 0, 4);
                        Trace.WriteLineIf(_format!= SecsFormat.List, $"Get format: {_format}, length: {_itemLength}");

                        _decodeIndex += _lengthBits;
                        _messageDataLength -= _lengthBits;
                        length -= _lengthBits;
                        return 4;
                    },
                    // 4: get item value
                    (ref int length, out int need) =>
                    {
                        need = 0;
                        Item item;
                        if (_format == SecsFormat.List)
                        {
                            if (_itemLength == 0) {
                                item = Item.L();
                            }
                            else
                            {
                                _stack.Push(new List<Item>(_itemLength));
                                return 2;
                            }
                        }
                        else
                        {
                            if (!CheckAvailable(ref length, _itemLength, out need)) return 4;

                            item = _itemLength == 0 ? _format.BytesDecode() : _format.BytesDecode(_buffer, ref _decodeIndex, ref _itemLength);
                            Trace.WriteLine($"Complete Item: {_format}");

                            _decodeIndex += _itemLength;
                            _messageDataLength -= (uint)_itemLength;
                            length -= _itemLength;
                        }

                        if(_stack.Count==0)
                        {
                            Trace.WriteLine("Get Complete Data Message by stream decoded");
                            _dataMsgHandler(_msgHeader, new SecsMessage(_msgHeader.S, _msgHeader.F, _msgHeader.ReplyExpected, string.Empty, item));
                            return 0;
                        }

                        var list = _stack.Peek();
                        list.Add(item);
                        while (list.Count == list.Capacity)
                        {
                            item = Item.L(_stack.Pop());
                            Trace.WriteLine($"Complete List: {item.Count}");
                            if (_stack.Count > 0)
                            {
                                list = _stack.Peek();
                                list.Add(item);
                            }
                            else
                            {
                                Trace.WriteLine("Get Complete Data Message by stream decoded");
                                _dataMsgHandler(_msgHeader, new SecsMessage(_msgHeader.S, _msgHeader.F, _msgHeader.ReplyExpected, string.Empty, item));
                                return 0;
                            }
                        }

                        return 2;
                    },
                };
        }

        bool CheckAvailable(ref int length, int required, out int need)
        {
            if (length < required)
                need = required - length;
            else
                need = 0;
            return need == 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="length">data length</param>
        /// <returns>true, if need more data to decode completed message. otherwise, return false</returns>
        public bool Decode(int length)
        {
            Debug.Assert(length > 0,"decode data length is 0.");
            int currentLength = length;
            length += _previousRemainedCount; // total available length = current length + previous remained
            int need = 0;
            int nexStep = _decoderStep;
            do
            {
                _decoderStep = nexStep;
                nexStep = _decoders[_decoderStep](ref length, out need);
            } while (nexStep != _decoderStep);

            Debug.Assert(_decodeIndex >= _BufferOffset, "decode index should ahead of buffer index");

            int remainCount = length;
            Debug.Assert(remainCount >= 0,"remain count is only possible grater and equal zero");
            Trace.WriteLine($"remain data length: {remainCount}");
            Trace.WriteLineIf(_messageDataLength > 0, $"need data count: {need}");

            if (remainCount == 0)
            {
                if (need > Buffer.Length)
                {
                    var newSize = need * 2;
                    Trace.WriteLine($@"<<buffer resizing>>: current size = {_buffer.Length}, new size = {newSize}");

                    // increase buffer size
                    _buffer = new byte[newSize];
                }
                _BufferOffset = 0;
                _decodeIndex = 0;
                _previousRemainedCount = 0;
            }
            else 
            {
                _BufferOffset += currentLength; // move next receive index
                int nextStepReqiredCount = remainCount + need;              
                if (nextStepReqiredCount > BufferCount)
                {
                    if (nextStepReqiredCount > Buffer.Length)
                    {
                        var newSize = Math.Max(_messageDataLength / 2, nextStepReqiredCount) * 2;
                        Trace.WriteLine($@"<<buffer resizing>>: current size = {_buffer.Length}, remained = {remainCount}, new size = {newSize}");

                        // out of total buffer size
                        // increase buffer size
                        var newBuffer = new byte[newSize];
                        // keep remained data to new buffer's head
                        Array.Copy(_buffer, _BufferOffset - remainCount, newBuffer, 0, remainCount);
                        _buffer = newBuffer;
                    }
                    else
                    {
                        Trace.WriteLine($@"<<buffer recyling>>: avalible = {BufferCount}, need = {nextStepReqiredCount}, remained = {remainCount}");

                        // move remained data to buffer's head
                        Array.Copy(_buffer, _BufferOffset - remainCount, _buffer, 0, remainCount);
                    }
                    _BufferOffset = remainCount;
                    _decodeIndex = 0;
                }
                _previousRemainedCount = remainCount;
            }

            return _messageDataLength > 0;
        }
    }
}
