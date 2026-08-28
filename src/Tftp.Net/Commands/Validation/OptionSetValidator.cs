using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using Tftp.Net.Commands.Properties;

namespace Tftp.Net.Commands.Validation;

internal sealed partial class OptionSetValidator(ILogger<OptionSetValidator> logger)
{
	public bool TryParseOackOptionSet(IEnumerable<KeyValuePair<string, string>> unparsedOptions, [NotNullWhen(true)] out OptionSet? options)
		=> TryParseOptionSet(unparsedOptions, isOack: true, out options);

	public bool TryParseRequestOptionSet(IEnumerable<KeyValuePair<string, string>> unparsedOptions, [NotNullWhen(true)] out OptionSet? options)
		=> TryParseOptionSet(unparsedOptions, isOack: false, out options);

	/// <summary>
	/// Parses an unparsed option list into a protocol-valid <see cref="OptionSet"/>, adjudicating each option individually.
	/// </summary>
	/// <remarks>Following RFC 2347, options are never rejected as a whole because of a single bad entry:
	/// <list type="bullet">
	/// <item>Unknown option names are ignored and will not be acknowledged.</item>
	/// <item>Known options with unusable (non-numeric) values are declined and omitted from the result.</item>
	/// <item>Known options with values outside the protocol range are declined and omitted from the result.</item>
	/// <item>Duplicate option names violate the protocol ("an option may only be specified once") and fail the whole option set.</item>
	/// </list>
	/// </remarks>
	/// <param name="unparsedOptions">The raw name/value pairs as received from the remote endpoint.</param>
	/// <param name="isOack">Indicates whether the option set is being parsed from an OACK packet (true) or a request packet (false).</param>
	/// <param name="options">When this method returns <see langword="true"/>, contains the accepted (and possibly adjusted) options;
	/// otherwise, <see langword="null"/>.</param>
	/// <returns><see langword="true"/> if the option list is structurally valid; otherwise, <see langword="false"/> (duplicate option names).</returns>
	private bool TryParseOptionSet(IEnumerable<KeyValuePair<string, string>> unparsedOptions, bool isOack, [NotNullWhen(true)] out OptionSet? options)
	{
		options = null;

		if (!unparsedOptions.Any())
		{
			LogNoOptionsProvided();
			options = OptionSet.Empty;
			return true;
		}

		// Verify there are no duplicate keys in the unparsed options. RFC 2347 requires each
		// option to appear at most once, so duplicates reject the whole option list.
		var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var option in unparsedOptions)
		{
			if (!seenKeys.Add(option.Key))
			{
				LogDuplicateOptionName(option.Key);
				return false;
			}
		}

		// Convert the unparsed options to a dictionary
		var optionDictionary = unparsedOptions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

		// Check to see if the timeout option is present
		ushort? timeout = null;
		if (optionDictionary.TryGetValue("timeout", out var timeoutValue))
		{
			LogTimeoutValue(timeoutValue);

			// Per RFC2349:
			//	If the server is willing to accept the timeout option, it sends an
			//	Option Acknowledgment(OACK) to the client. The specified timeout
			//	value must match the value specified by the client.
			//
			// Practical implications of this:
			//  - If the requested timeout value is acceptable to the server, accept it as is. We cannot modify it at all.
			//  - If the requested timeout is valid, but unacceptable to our server configuration. We exclude it from the OACK, the client will use default values.
			//  - Other behaviors are somewhat undefined:
			//    - What about a non-numeric value?
			//    - What about a numeric value that is outside the protocol-defined range? 
			//  - We have two options for the above cases: generate an error, or omit the option from the OACK
			//  - To keep the implementation simple, we will omit.

			// An unusable value will keep timeout as null, it will not be included in the OptionSet
			// Result: declines the option rather than failing the request
			if (!ushort.TryParse(timeoutValue, out var parsedTimeoutValue))
			{
				LogFailedToParseTimeout();

				// If we are parsing an OACK, negotiation has already occured.
				// There is no mechanism to correct the value at this stage and we must fail the parse.
				if (isOack)
				{
					return false;
				}
			}
			// If the value exceeds the defined protocol range, do not set the variable.
			// This will result in it being excluded from the returned OptionSet.
			else if (parsedTimeoutValue < OptionSet.MinTimeoutValue || parsedTimeoutValue > OptionSet.MaxTimeoutValue)
			{
				LogTimeoutOutOfRange(parsedTimeoutValue);

				// If we are parsing an OACK, negotiation has already occured.
				// There is no mechanism to correct the value at this stage and we must fail the parse.
				if (isOack)
				{
					return false;
				}
			}
			// The value is valid as is, and requires no modification. Accept and include in the OptionSet.
			else
			{
				timeout = parsedTimeoutValue;
			}
		}

		// Check to see if the blocksize option is present
		ushort? blockSize = null;
		if (optionDictionary.TryGetValue("blksize", out var blockSizeValue))
		{
			LogBlockSizeValue(blockSizeValue);

			// Per RFC2348:
			//  If the server is willing to accept the blocksize option, it sends an
			//  Option Acknowledgment(OACK) to the client. The specified value must
			//  be less than or equal to the value specified by the client.
			//
			// Practical implications of this:
			//  - If the requested blocksize value is acceptable to the server, accept it as is.
			//  - If the requested blocksize is valid, but unacceptable to our server configuration. We will clamp it to the server's configured maximum and include it in the OACK.
			//  - Similarly to timeout, we will decline the option if it is non-numeric or outside the protocol-defined range. We will not fail the request, but simply omit the option from the OACK.

			// An unusable value will keep blockSize as null, it will not be included in the OptionSet
			if (!ushort.TryParse(blockSizeValue, out var parsedBlockSizeValue))
			{
				LogFailedToParseBlockSize();

				// If we are parsing an OACK, negotiation has already occured.
				// There is no mechanism to correct the value at this stage and we must fail the parse.
				if (isOack)
				{
					return false;
				}
			}
			// If the value exceeds the defined protocol range, do not set the variable.
			// This will result in it being excluded from the returned OptionSet.
			else if (parsedBlockSizeValue < OptionSet.MinBlockSizeValue || parsedBlockSizeValue > OptionSet.MaxBlockSizeValue)
			{
				LogBlockSizeOutOfRange(parsedBlockSizeValue);

				// If we are parsing an OACK, negotiation has already occured.
				// There is no mechanism to correct the value at this stage and we must fail the parse.
				if (isOack)
				{
					return false;
				}
			}
			// The value is valid as is, and requires no modification. Accept and include in the OptionSet.
			else
			{
				blockSize = parsedBlockSizeValue;
			}
		}

		// Check if the winsize option is present
		ushort? windowSize = null;
		if (optionDictionary.TryGetValue("winsize", out var windowSizeValue))
		{
			LogWindowSizeValue(windowSizeValue);

			// Per RFC7440:
			//	If the server is willing to accept the windowsize option, it sends an
			//  Option Acknowledgment (OACK) to the client.  The specified value MUST
			//  be less than or equal to the value specified by the client.
			//
			// Practical implications of this:
			//  - If the requested winsize value is acceptable to the server, accept it as is.
			//  - If the requested winsize is valid, but unacceptable to our server configuration. We will clamp it to the server's configured maximum and include it in the OACK.
			//  - Technically, this option may be better suited to generate an error in a non-valid configuration as it was published after RFC2119 and specifies valid range MUST be adhered to.
			//    In such a scenario, is a client that does not adhere to the protocol specification a valid client?
			//    For the sake of consistency with the other options, we will decline the option if it is non-numeric or outside the protocol-defined range. We will not fail the request, but simply omit the option from the OACK.

			// An unusable value will keep windowSize as null, it will not be included in the OptionSet
			if (!ushort.TryParse(windowSizeValue, out var parsedWindowSizeValue))
			{
				LogFailedToParseWindowSize();

				// If we are parsing an OACK, negotiation has already occured.
				// There is no mechanism to correct the value at this stage and we must fail the parse.
				if (isOack)
				{
					return false;
				}
			}
			// If the value exceeds the defined protocol range, do not set the variable.
			// This will result in it being excluded from the returned OptionSet.
			else if (parsedWindowSizeValue < OptionSet.MinWindowSizeValue || parsedWindowSizeValue > OptionSet.MaxWindowSizeValue)
			{
				LogWindowSizeOutOfRange(parsedWindowSizeValue);

				// If we are parsing an OACK, negotiation has already occured.
				// There is no mechanism to correct the value at this stage and we must fail the parse.
				if (isOack)
				{
					return false;
				}
			}
			// The value is valid as is, and requires no modification. Accept and include in the OptionSet.
			else
			{
				windowSize = parsedWindowSizeValue;
			}
		}

		// Check if the tsize option is present
		ulong? transferSize = null;
		if (optionDictionary.TryGetValue("tsize", out var transferSizeValue))
		{
			LogTransferSizeValue(transferSizeValue);

			// Per RFC2349:
			// Transfer size is not 'negotiated' it is the size of the file to be transferred.
			// We do not need to clamp or perform any other adjustments to the value.
			// We will simply accept it as is, or decline it if it is unusable.
			if (!ulong.TryParse(transferSizeValue, out var parsedTransferSizeValue))
			{
				LogFailedToParseTransferSize();
			}
			else
			{
				transferSize = parsedTransferSizeValue;
			}
		}

		options = new(timeout, blockSize, transferSize, windowSize);
		return true;
	}
}
