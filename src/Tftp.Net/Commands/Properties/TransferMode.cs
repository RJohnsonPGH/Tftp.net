using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Tftp.Net.Commands.Properties;

/// <summary>
/// Represents a TFTP (Trivial File Transfer Protocol) transfer mode, such as "netascii", "octet", or "mail".
/// </summary>
/// <remarks>Use the predefined static instances to specify the transfer mode when working with TFTP operations.
/// The transfer mode determines how file data is interpreted and transmitted between the client and server. This type
/// provides implicit conversion to a byte array for protocol-level operations.</remarks>
public sealed record TransferMode
{
	public static readonly TransferMode NetAscii = new("netascii");
	public static readonly TransferMode Octet = new("octet");
	public static readonly TransferMode Mail = new("mail");

	private readonly string _value;

	private TransferMode(string value)
	{
		_value = value;
	}

	public static implicit operator string(TransferMode mode) => mode._value;
    public override string ToString() => _value;

	/// <summary>
	/// Attempts to parse the specified string as a TFTP transfer mode value.
	/// </summary>
	/// <remarks>Valid mode values are "netascii", "octet", and "mail". If the input does not match any of these
	/// values (case-insensitive), parsing fails and the output parameter is set to null.</remarks>
	/// <param name="mode">The string representation of the transfer mode to parse. Comparison is case-insensitive.</param>
	/// <param name="value">When this method returns, contains the parsed TFTP transfer mode if parsing succeeded; otherwise, null. This
	/// parameter is passed uninitialized.</param>
	/// <returns>true if the string was successfully parsed as a TFTP transfer mode; otherwise, false.</returns>
	public static bool TryParse(string mode, [NotNullWhen(true)] out TransferMode? value)
	{
		value = mode.ToLowerInvariant() switch
		{
			"netascii" => NetAscii,
			"octet" => Octet,
			"mail" => Mail,
			_ => null
		};

		return value is not null;
	}
}
