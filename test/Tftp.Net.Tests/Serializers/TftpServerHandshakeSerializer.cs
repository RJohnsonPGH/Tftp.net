using Tftp.Net.Transfer;
using Xunit.Sdk;

namespace Tftp.Net.Tests.Serializers;

internal class TftpServerHandshakeSerialzier : XunitSerializer<ServerHandshake>
{
	public override ServerHandshake Deserialize(Type type, string serializedValue)
	{
		throw new NotImplementedException();
	}

	public override string Serialize(ServerHandshake value)
	{
		return $"Endpoint = {value.RemoteEndpoint}, Mode = {value.Mode}, Filename = {value.Filename}, Timeout = {value.Options.Timeout}, Blocksize = {value.Options.BlockSize}, Tsize = {value.Options.TransferSize}";
	}
}
