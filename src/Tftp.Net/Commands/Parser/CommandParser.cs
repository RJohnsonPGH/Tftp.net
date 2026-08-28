using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Tftp.Net.Commands.Properties;

namespace Tftp.Net.Commands.Parser;

/// <summary>
/// Provides static methods for parsing TFTP commands from message buffers in a safe and structured manner.
/// </summary>
/// <remarks>This class is intended for internal use within the TFTP protocol implementation. All parsing methods
/// are designed to avoid throwing exceptions for malformed or invalid messages; instead, they return failure indicators
/// and log relevant errors. The class supports parsing various TFTP command types, including read requests, write
/// requests, data, acknowledgements, errors, and option acknowledgements. Logging is integrated to facilitate
/// diagnostics and troubleshooting during parsing operations.</remarks>
internal static class CommandParser
{
	/// <summary>
	/// Attempts to parse a TFTP command from the specified message buffer.
	/// </summary>
	/// <remarks>This method does not throw exceptions for invalid or malformed messages; instead, it returns <see
	/// langword="false"/> and logs relevant errors. The caller should check the return value before using the parsed
	/// command.</remarks>
	/// <param name="message">The byte array containing the TFTP message to parse. Must not be null and should represent a valid TFTP packet.</param>
	/// <param name="tftpCommand">When this method returns <see langword="true"/>, contains the parsed TFTP command; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="logger">An optional logger used to record parsing events and errors. If <see langword="null"/>, a default logger is used.</param>
	/// <returns><see langword="true"/> if the message was successfully parsed into a TFTP command; otherwise, <see
	/// langword="false"/>.</returns>
	public static bool TryParse(byte[] message, [NotNullWhen(true)] out ICommand? tftpCommand, ILogger? logger = null)
	{
		logger ??= NullLogger.Instance;
		tftpCommand = null;

		using var _ = logger.BeginScope("Starting to parse TFTP command from message of length {MessageLength} bytes", message.Length);

		var messageWrapper = new MessageWrapper(message.AsSpan());

		// The first step in parsing is to read the operation code (OpCode) from the message, which determines the type of TFTP command being represented.
		// If the OpCode cannot be read or is invalid, the method logs an appropriate error and returns false.
		if (!TryReadOpCode(ref messageWrapper, out var opCode, logger))
		{
			logger.LogFailedToReadOpCode();
			return false;
		}

		// After successfully reading the OpCode, the method uses a switch expression to delegate the parsing of the rest of the message to specific methods based on the OpCode value.
		var result = opCode switch
		{
			OpCode.ReadRequest => TryParseReadRequest(ref messageWrapper, out tftpCommand, logger),
			OpCode.WriteRequest => TryParseWriteRequest(ref messageWrapper, out tftpCommand, logger),
			OpCode.Data => TryParseData(ref messageWrapper, out tftpCommand, logger),
			OpCode.Acknowledgement => TryParseAcknowledgement(ref messageWrapper, out tftpCommand, logger),
			OpCode.Error => TryParseError(ref messageWrapper, out tftpCommand, logger),
			OpCode.OptionAcknowledgement => TryParseOptionAcknowledgement(ref messageWrapper, out tftpCommand, logger),
			_ => InvalidOpCode(out tftpCommand, logger)
		};

		// A valid command must consume the entire packet. Trailing bytes indicate corruption
		// or an interleaved packet, so the whole message is rejected.
		if (result && !messageWrapper.IsComplete)
		{
			logger.LogTrailingBytesAfterCommand();
			tftpCommand = null;
			result = false;
		}

		if (!result)
		{
			logger.LogFailedToParseCommand();
		}
		else
		{
			logger.LogSuccessfullyParsedCommand();
		}

		return result;
	}

	/// <summary>
	/// Attempts to read an OpCode value from the specified message wrapper.
	/// </summary>
	/// <remarks>If the message wrapper does not contain a valid UInt16 value or the value does not correspond to a
	/// defined OpCode, the method returns false and logs the failure. The method does not throw exceptions for invalid or
	/// missing data.</remarks>
	/// <param name="messageWrapper">A reference to the message wrapper containing the data to read. The position within the wrapper will be advanced if
	/// a value is successfully read.</param>
	/// <param name="value">When this method returns, contains the OpCode value read from the message wrapper if the operation succeeds;
	/// otherwise, contains the default value for OpCode.</param>
	/// <param name="logger">The logger used to record diagnostic information about the read operation, including failures and invalid values.</param>
	/// <returns>true if an OpCode value was successfully read and is valid; otherwise, false.</returns>
	private static bool TryReadOpCode(ref MessageWrapper messageWrapper, out OpCode value, ILogger logger)
	{
		if (!messageWrapper.TryGetUInt16(out var opcode))
		{
			logger.LogFailedToReadUInt16();
			value = default;
			return false;
		}

		if (!Enum.IsDefined((OpCode)opcode))
		{
			logger.LogInvalidOpCodeValue(opcode);
			value = default;
			return false;
		}

		value = (OpCode)opcode;
		logger.LogReadOpCode(value);
		return true;
	}

	/// <summary>
	/// Attempts to parse an option acknowledgement command from the specified message wrapper.
	/// </summary>
	/// <remarks>This method does not throw exceptions for parsing failures; instead, it logs errors and returns
	/// <see langword="false"/>. The output parameter <paramref name="value"/> is only valid when the method returns <see
	/// langword="true"/>.</remarks>
	/// <param name="messageWrapper">A reference to the message wrapper containing the data to parse. The message wrapper is updated to reflect the
	/// parsing progress.</param>
	/// <param name="value">When this method returns <see langword="true"/>, contains the parsed option acknowledgement command; otherwise,
	/// <see langword="null"/>.</param>
	/// <param name="logger">The logger used to record parsing events and errors.</param>
	/// <returns><see langword="true"/> if an option acknowledgement command was successfully parsed; otherwise, <see
	/// langword="false"/>.</returns>
	private static bool TryParseOptionAcknowledgement(ref MessageWrapper messageWrapper, [NotNullWhen(true)] out ICommand? value, ILogger logger)
	{
		if (!TryParseOptions(ref messageWrapper, out var options, logger))
		{
			logger.LogFailedToParseOptions();
			value = null;
			return false;
		}

		value = new OptionAcknowledgement(options);
		return true;
	}

	/// <summary>
	/// Attempts to parse an error command from the specified message wrapper.
	/// </summary>
	/// <remarks>This method does not throw exceptions for parsing failures; instead, it logs errors and returns
	/// <see langword="false"/>. The output parameter <paramref name="value"/> will be <see langword="null"/> if parsing
	/// fails.</remarks>
	/// <param name="messageWrapper">A reference to the message wrapper containing the data to parse. The position within the wrapper will be advanced
	/// if parsing succeeds.</param>
	/// <param name="value">When this method returns <see langword="true"/>, contains the parsed error command; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="logger">The logger used to record parsing events and errors during the operation.</param>
	/// <returns><see langword="true"/> if an error command was successfully parsed; otherwise, <see langword="false"/>.</returns>
	private static bool TryParseError(ref MessageWrapper messageWrapper, [NotNullWhen(true)] out ICommand? value, ILogger logger)
	{
		value = null;

		if (!messageWrapper.TryGetUInt16(out var errorCode))
		{
			logger.LogFailedToReadUInt16();
			return false;
		}

		if (!messageWrapper.TryGetNullTerminatedString(out var message))
		{
			logger.LogFailedToParseErrorMessage();
			return false;
		}

		logger.LogParsedError(errorCode, message);
		value = new Error(errorCode, message);
		return true;
	}

	/// <summary>
	/// Attempts to parse an acknowledgement command from the specified message wrapper.
	/// </summary>
	/// <remarks>If parsing fails, the method logs the failure and sets <paramref name="value"/> to <see
	/// langword="null"/>. The method does not throw exceptions for parsing errors.</remarks>
	/// <param name="messageWrapper">A reference to the message wrapper containing the data to parse. The wrapper will be read to extract the
	/// acknowledgement information.</param>
	/// <param name="value">When this method returns <see langword="true"/>, contains the parsed acknowledgement command; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="logger">The logger used to record parsing events and errors during the operation.</param>
	/// <returns><see langword="true"/> if the acknowledgement command was successfully parsed; otherwise, <see langword="false"/>.</returns>
	private static bool TryParseAcknowledgement(ref MessageWrapper messageWrapper, [NotNullWhen(true)] out ICommand? value, ILogger logger)
	{
		value = null;

		if (!messageWrapper.TryGetUInt16(out var blockNumber))
		{
			logger.LogFailedToReadBlockNumber();
			return false;
		}

		logger.LogParsedAcknowledgement(blockNumber);
		value = new Acknowledgement(blockNumber);
		return true;
	}

	/// <summary>
	/// Attempts to parse a data message from the specified message wrapper and outputs the corresponding TFTP command if
	/// successful.
	/// </summary>
	/// <remarks>This method does not throw exceptions for parsing failures; instead, it logs errors and returns
	/// <see langword="false"/>. The output command is only set when parsing succeeds.</remarks>
	/// <param name="messageWrapper">A reference to the message wrapper containing the data to parse. The wrapper is updated as bytes are read during
	/// parsing.</param>
	/// <param name="value">When this method returns <see langword="true"/>, contains the parsed <see cref="ICommand"/> representing the
	/// data message; otherwise, contains <see langword="null"/>.</param>
	/// <param name="logger">The logger used to record parsing events and errors encountered during the operation.</param>
	/// <returns><see langword="true"/> if the data message was successfully parsed; otherwise, <see langword="false"/>.</returns>
	private static bool TryParseData(ref MessageWrapper messageWrapper, [NotNullWhen(true)] out ICommand? value, ILogger logger)
	{
		value = null;

		if (!messageWrapper.TryGetUInt16(out var blockNumber))
		{
			logger.LogFailedToReadDataBlockNumber();
			return false;
		}

		var data = messageWrapper.GetRemainingBytes();

		logger.LogParsedData(blockNumber, data.Length);
		value = new Data(blockNumber, data);
		return true;
	}

	/// <summary>
	/// Attempts to parse a write request from the specified message wrapper.
	/// </summary>
	/// <remarks>If parsing fails, the method logs the failure and sets <paramref name="value"/> to <see
	/// langword="null"/>. The caller should check the return value before using <paramref name="value"/>.</remarks>
	/// <param name="messageWrapper">A reference to the message wrapper containing the incoming TFTP request data. The value may be modified during
	/// parsing.</param>
	/// <param name="value">When this method returns <see langword="true"/>, contains the parsed write request command; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="logger">The logger used to record parsing failures or diagnostic information.</param>
	/// <returns><see langword="true"/> if the message was successfully parsed as a write request; otherwise, <see
	/// langword="false"/>.</returns>
	private static bool TryParseWriteRequest(ref MessageWrapper messageWrapper, [NotNullWhen(true)] out ICommand? value, ILogger logger)
	{
		if (!TryParseRequest(ref messageWrapper, out var filename, out var mode, out var options, logger))
		{
			logger.LogFailedToParseWriteRequest();
			value = null;
			return false;
		}

		value = new WriteRequest(filename, mode, options);
		return true;
	}

	/// <summary>
	/// Attempts to parse a TFTP read request from the specified message wrapper.
	/// </summary>
	/// <remarks>This method does not throw exceptions for invalid or malformed requests. Instead, it logs parsing
	/// failures and returns <see langword="false"/>.</remarks>
	/// <param name="messageWrapper">A reference to the message wrapper containing the incoming TFTP request data. The value may be modified during
	/// parsing.</param>
	/// <param name="value">When this method returns <see langword="true"/>, contains the parsed <see cref="ICommand"/> representing the
	/// read request; otherwise, <see langword="null"/>.</param>
	/// <param name="logger">The logger used to record parsing failures or diagnostic information.</param>
	/// <returns><see langword="true"/> if the message was successfully parsed as a read request; otherwise, <see
	/// langword="false"/>.</returns>
	private static bool TryParseReadRequest(ref MessageWrapper messageWrapper, [NotNullWhen(true)] out ICommand? value, ILogger logger)
	{
		if (!TryParseRequest(ref messageWrapper, out var filename, out var mode, out var options, logger))
		{
			logger.LogFailedToParseReadRequest();
			value = null;
			return false;
		}

		value = new ReadRequest(filename, mode, options);
		return true;
	}

	/// <summary>
	/// Attempts to parse a TFTP read request from the specified messageWrapper, extracting the filename, transfer mode, and any
	/// associated options.
	/// </summary>
	/// <remarks>Parsing is purely syntactic: the mode string and option values are returned exactly as
	/// received, without checking them against the protocol's semantics. If parsing fails structurally
	/// (missing null terminators, incomplete option pairs), the method returns <see langword="false"/> and
	/// all output parameters are set to <see langword="null"/>. The method logs detailed trace information
	/// for each parsing step, which can assist in diagnosing malformed requests.</remarks>
	/// <param name="messageWrapper">The messageWrapper containing the TFTP request data to be parsed. Must be readable and positioned at the start of the
	/// request.</param>
	/// <param name="filename">When this method returns <see langword="true"/>, contains the parsed filename from the request; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="mode">When this method returns <see langword="true"/>, contains the parsed transfer mode; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="options">When this method returns <see langword="true"/>, contains a read-only list of parsed TFTP options; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="logger">The logger used to record parsing progress and errors.</param>
	/// <returns><see langword="true"/> if the request was successfully parsed and all output parameters are set; otherwise, <see
	/// langword="false"/>.</returns>
	private static bool TryParseRequest(ref MessageWrapper messageWrapper, [NotNullWhen(true)] out string? filename, [NotNullWhen(true)] out string? mode, [NotNullWhen(true)] out IEnumerable<KeyValuePair<string, string>>? options, ILogger logger)
	{
		mode = null;
		options = null;

		// Parse filename
		if (!messageWrapper.TryGetNullTerminatedString(out filename))
		{
			logger.LogFailedToParseString();
			return false;
		}

		// Parse transfer mode
		if (!messageWrapper.TryGetNullTerminatedString(out mode))
		{
			logger.LogFailedToParseString();
			return false;
		}

		// Parse options as raw name/value pairs. Parsing is purely syntactic: whether the mode
		// is a defined transfer mode and whether option values are usable is decided by the
		// caller's semantic validation, not here.
		if (!TryParseOptions(ref messageWrapper, out options, logger))
		{
			logger.LogFailedToParseRequestOptions();
			return false;
		}

		logger.LogParsedRequest(filename, mode);
		return true;
	}

	/// <summary>
	/// Attempts to parse TFTP options from the specified message wrapper.
	/// </summary>
	/// <remarks>If parsing fails due to malformed option data, the method logs the failure and returns <see
	/// langword="false"/> with <paramref name="options"/> set to <see langword="null"/>. The method does not throw
	/// exceptions for parsing errors.</remarks>
	/// <param name="messageWrapper">A reference to the message wrapper containing the TFTP option data to parse. The position within the wrapper will
	/// be advanced as options are read.</param>
	/// <param name="options">When this method returns <see langword="true"/>, contains a read-only list of parsed TFTP options; otherwise, <see
	/// langword="null"/>.</param>
	/// <param name="logger">The logger used to record parsing failures or diagnostic information.</param>
	/// <returns><see langword="true"/> if all options are successfully parsed; otherwise, <see langword="false"/>.</returns>
	private static bool TryParseOptions(ref MessageWrapper messageWrapper, [NotNullWhen(true)] out IEnumerable<KeyValuePair<string, string>>? options, ILogger logger)
	{
		options = null;
		var parsedOptions = new List<KeyValuePair<string, string>>();

		while (!messageWrapper.IsComplete)
		{
			if (!messageWrapper.TryGetNullTerminatedString(out var name) ||
				!messageWrapper.TryGetNullTerminatedString(out var value))
			{
				logger.LogFailedToParseOptions();
				return false;
			}

			logger.LogParsedOption(name, value);
			parsedOptions.Add(new(name, value));
		}

		options = parsedOptions;
		return true;
	}

	/// <summary>
	/// Handles an invalid TFTP operation code and logs the occurrence.
	/// </summary>
	/// <param name="command">When the method returns, contains a null reference to indicate that no valid command was parsed.</param>
	/// <param name="logger">The logger used to record the invalid operation code event. Cannot be null.</param>
	/// <returns>Always returns <see langword="false"/> to indicate that the operation code was invalid.</returns>
	private static bool InvalidOpCode(out ICommand? command, ILogger logger)
	{
		command = null;
		logger.LogInvalidOpCode();
		return false;
	}

	/// <summary>
	/// Provides a read-only, forward-only wrapper for parsing structured messages from a span of bytes.
	/// </summary>
	/// <remarks>This type is a ref struct and must be used on the stack. It is intended for efficient, sequential
	/// parsing of binary message data without allocations. The wrapper maintains an internal position and exposes methods
	/// to read specific data types or slices from the underlying message buffer. Once a value is read, the position
	/// advances, and previously read data cannot be re-read. This type is not thread-safe.</remarks>
	internal ref struct MessageWrapper
	{
		/// <summary>
		/// Gets a value indicating whether the current operation has reached the end of the message.
		/// </summary>
		public readonly bool IsComplete => _position >= _message.Length;

		private readonly ReadOnlySpan<byte> _message;
		private int _position;

		internal MessageWrapper(ReadOnlySpan<byte> message)
		{
			_message = message;
			_position = 0;
		}

		/// <summary>
		/// Attempts to read a 16-bit unsigned integer from the current position in the message buffer.
		/// </summary>
		/// <remarks>The method advances the current position by two bytes if the read operation succeeds. If there
		/// are fewer than two bytes remaining in the buffer, the method returns false and does not advance the
		/// position.</remarks>
		/// <param name="value">When this method returns, contains the 16-bit unsigned integer value read from the buffer, if the operation
		/// succeeds; otherwise, contains zero.</param>
		/// <returns>true if a 16-bit unsigned integer was successfully read from the buffer; otherwise, false.</returns>
		internal bool TryGetUInt16(out ushort value)
		{
			if (_position + 2 > _message.Length)
			{
				value = default;
				return false;
			}

			value = BinaryPrimitives.ReadUInt16BigEndian(_message.Slice(_position, 2));
			_position += 2;

			return true;
		}

		/// <summary>
		/// Attempts to extract a slice of bytes from the current position up to the next null terminator.
		/// </summary>
		/// <remarks>The current position is advanced past the null terminator if a slice is successfully extracted.
		/// If no null terminator is found, the position remains unchanged.</remarks>
		/// <param name="slice">When the method returns <see langword="true"/>, contains a read-only span of bytes representing the data before
		/// the null terminator. When the method returns <see langword="false"/>, contains the default value.</param>
		/// <returns><see langword="true"/> if a null terminator is found and the slice is successfully extracted; otherwise, <see
		/// langword="false"/>.</returns>
		private bool TryGetNullTerminatedSlice(out ReadOnlySpan<byte> slice)
		{
			var nullTerminatorIndex = _message[_position..].IndexOf((byte)0);
			if (nullTerminatorIndex == -1)
			{
				slice = default;
				return false;
			}

			slice = _message.Slice(_position, nullTerminatorIndex);
			_position += nullTerminatorIndex + 1; // +1 is to skip the null terminator itself
			return true;
		}

		/// <summary>
		/// Attempts to retrieve a null-terminated ASCII string from the underlying data source.
		/// </summary>
		/// <remarks>This method does not throw exceptions if the string is not found; instead, it returns <see
		/// langword="false"/> and sets <paramref name="value"/> to <see langword="null"/>. The returned string excludes the
		/// terminating null character.</remarks>
		/// <param name="value">When this method returns <see langword="true"/>, contains the decoded ASCII string. When this method returns <see
		/// langword="false"/>, contains <see langword="null"/>.</param>
		/// <returns><see langword="true"/> if a null-terminated ASCII string was successfully retrieved; otherwise, <see
		/// langword="false"/>.</returns>
		internal bool TryGetNullTerminatedString([NotNullWhen(true)] out string? value)
		{
			if (!TryGetNullTerminatedSlice(out var slice))
			{
				value = default;
				return false;
			}

			value = Encoding.ASCII.GetString(slice);

			if (string.IsNullOrEmpty(value))
			{
				value = default;
				return false;
			}

			return true;
		}

		/// <summary>
		/// Retrieves all remaining bytes from the current position to the end of the message buffer.
		/// </summary>
		/// <remarks>After calling this method, the internal position is advanced to the end of the message buffer.
		/// Subsequent calls will return an empty array unless the position is reset.</remarks>
		/// <returns>A byte array containing the remaining bytes in the message. The array will be empty if there are no bytes left to
		/// read.</returns>
		internal byte[] GetRemainingBytes()
		{
			var remaining = _message[_position..];
			_position += remaining.Length;
			return remaining.ToArray();
		}
	}
}
