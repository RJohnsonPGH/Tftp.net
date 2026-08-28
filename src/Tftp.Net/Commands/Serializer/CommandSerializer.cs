using System.Buffers;
using System.Buffers.Binary;
using Tftp.Net.Commands.Properties;

namespace Tftp.Net.Commands.Serializer;

/// <summary>
/// Provides methods for serializing TFTP command objects into their binary format for network transmission.
/// </summary>
/// <remarks>This class supports serialization of various TFTP command types, including requests, data,
/// acknowledgements, errors, and option acknowledgements. It is intended for internal use within the TFTP protocol
/// implementation and is not thread-safe.</remarks>
internal static class CommandSerializer
{
	/// <summary>
	/// Serializes the specified TFTP command into its binary representation according to the TFTP protocol.
	/// </summary>
	/// <remarks>The returned byte array can be sent over a network to a TFTP server or client. The method supports
	/// all standard TFTP command types, including requests, data, acknowledgements, errors, and option
	/// acknowledgements.</remarks>
	/// <param name="command">The TFTP command to serialize. Must not be null.</param>
	/// <param name="logger">An optional logger used to record serialization details.</param>
	/// <returns>A byte array containing the serialized form of the command.</returns>
	public static byte[] Serialize(ICommand command)
	{		
		var bufferWriter = new ArrayBufferWriter<byte>();
		switch (command)
		{
			case Request request:
				SerializeRequest(bufferWriter, request);
				break;
			case Data data:
				SerializeData(bufferWriter, data);
				break;
			case Acknowledgement ack:
				SerializeAcknowledgement(bufferWriter, ack);
				break;
			case Error error:
				SerializeError(bufferWriter, error);
				break;
			case OptionAcknowledgement optionAck:
				SerializeOptionAcknowledgement(bufferWriter, optionAck);
				break;
		}

		byte[] serializedCommand = bufferWriter.WrittenSpan.ToArray();
		return serializedCommand;
	}

	private static void SerializeRequest(IBufferWriter<byte> bufferWriter, Request command)
	{
		Write(bufferWriter, command.OpCode);
		Write(bufferWriter, command.Filename);
		Write(bufferWriter, command.Mode);
		Write(bufferWriter, command.Options);
	}

	private static void SerializeData(IBufferWriter<byte> bufferWriter, Data command)
	{
		Write(bufferWriter, command.OpCode);
		Write(bufferWriter, command.BlockNumber);
		Write(bufferWriter, command.DataBytes);
	}

	private static void SerializeAcknowledgement(IBufferWriter<byte> bufferWriter, Acknowledgement command)
	{
		Write(bufferWriter, command.OpCode);
		Write(bufferWriter, command.BlockNumber);
	}

	private static void SerializeError(IBufferWriter<byte> bufferWriter, Error command)
	{
		Write(bufferWriter, command.OpCode);
		Write(bufferWriter, command.ErrorCode);
		Write(bufferWriter, command.Message);
	}

	private static void SerializeOptionAcknowledgement(IBufferWriter<byte> bufferWriter, OptionAcknowledgement command)
	{
		Write(bufferWriter, command.OpCode);
		Write(bufferWriter, command.Options);
	}

	private static void Write(IBufferWriter<byte> bufferWriter, OpCode opCode) =>
		Write(bufferWriter, (ushort)opCode);

	private static void Write(IBufferWriter<byte> bufferWriter, ushort value)
	{
		Span<byte> span = bufferWriter.GetSpan(2);
		// Network byte order is big endian
		BinaryPrimitives.WriteUInt16BigEndian(span, value);
		bufferWriter.Advance(2);
	}

	private static void Write(IBufferWriter<byte> bufferWriter, string data)
	{
		int byteCount = System.Text.Encoding.ASCII.GetByteCount(data);
		Span<byte> span = bufferWriter.GetSpan(byteCount + 1);
		System.Text.Encoding.ASCII.GetBytes(data, span);
		span[byteCount] = 0x00;
		bufferWriter.Advance(byteCount + 1);
	}

	private static void Write(IBufferWriter<byte> bufferWriter, IEnumerable<KeyValuePair<string, string>> options)
	{
		foreach (var option in options)
		{
			Write(bufferWriter, option.Key);
			Write(bufferWriter, option.Value);
		}
	}

	private static void Write(IBufferWriter<byte> bufferWriter, ReadOnlyMemory<byte> data)
	{
		Span<byte> span = bufferWriter.GetSpan(data.Length);
		data.Span.CopyTo(span);
		bufferWriter.Advance(data.Length);
	}
}
